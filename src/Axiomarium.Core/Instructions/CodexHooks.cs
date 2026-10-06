using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tomlyn.Model;

namespace Axiomarium.Core.Instructions;

/// <summary>Which Codex hooks run at session start and around an edit, and whether Codex trusts each one.</summary>
/// <remarks>
/// Follows the Codex hooks guide and <c>codex-rs/hooks</c> as read at 0.156.1 on 2026-09-28. Recordings confirm
/// the hooks Codex lists and their trust and hashes; whether a hook fires needs a model turn, so matching
/// follows the source. How Codex keys a hook and a trusted project on Windows follows <c>hooks/list</c> from
/// <c>codex app-server</c> 0.156.1 there, and sessions that ran the hooks, on 2026-10-04.
/// </remarks>
internal static class CodexHooks
{
    // The order Codex reads events in, with each one's key in a hook's trust key and hash.
    private static readonly (string Name, string Key)[] Events =
    [
        ("PreToolUse", "pre_tool_use"), ("PermissionRequest", "permission_request"), ("PostToolUse", "post_tool_use"), ("PreCompact", "pre_compact"),
        ("PostCompact", "post_compact"), ("SessionStart", "session_start"), ("SessionEnd", "session_end"), ("UserPromptSubmit", "user_prompt_submit"),
        ("SubagentStart", "subagent_start"), ("SubagentStop", "subagent_stop"), ("Stop", "stop"), ("Interrupt", "interrupt"),
    ];

    private static readonly string[] EditInputs = ["apply_patch", "Write", "Edit"];
    private static readonly string[] ContextEvents = ["PreToolUse", "PostToolUse", "SessionStart", "UserPromptSubmit", "SubagentStart"];
    private const int DefaultContextLimit = 2500;

    /// <summary>The hooks at each moment, and every configured hook with its trust and hash.</summary>
    public static (IReadOnlyList<MomentHook> Hooks, IReadOnlyList<ConfiguredHook> Configured) Resolve(string launch, Machine machine, CodexConfig config)
    {
        var root = CodexModel.ProjectRoot(launch, config.RootMarkers, machine.FileSystemRoot) ?? launch;
        var hooks = new List<ConfiguredHook>();
        void Add(string file, JsonObject? events, HarnessRule source, bool managed = false, HarnessRule? layerOff = null)
        {
            foreach (var (name, key) in Events)
            {
                var groups = events?[name] as JsonArray ?? [];
                for (var group = 0; group < groups.Count; group++)
                {
                    // Codex ignores the matcher on events that don't take one.
                    var matcher = name is "UserPromptSubmit" or "Stop" or "Interrupt" ? null : Text(groups[group]?["matcher"]);
                    var handlers = groups[group]?["hooks"] as JsonArray ?? [];
                    for (var index = 0; index < handlers.Count; index++)
                    {
                        // The key is the one Codex writes: the file's full path in the platform's form, under the
                        // canonical CODEX_HOME that Machine.FromEnvironment holds, and matched exactly.
                        hooks.Add(Configure(handlers[index] as JsonObject ?? [], file, name, key, matcher, $"{Path.GetFullPath(file)}:{key}:{group}:{index}", source, managed, layerOff, config));
                    }
                }
            }
        }

        // The system folder's hooks, the user's, then each project .codex from the project root down.
        var admin = machine.CodexAdmin;
        Add(Path.Combine(admin, "requirements.toml"), Json(CodexConfig.HooksIn(Path.Combine(admin, "requirements.toml"))), CodexHookRules.AdminHook, managed: true);
        Add(Path.Combine(admin, "hooks.json"), HooksJson(Path.Combine(admin, "hooks.json")), CodexHookRules.AdminHook, managed: true);
        Add(Path.Combine(admin, "config.toml"), Json(CodexConfig.HooksIn(Path.Combine(admin, "config.toml"))), CodexHookRules.AdminHook, managed: true);
        Add(Path.Combine(machine.CodexHome, "hooks.json"), HooksJson(Path.Combine(machine.CodexHome, "hooks.json")), CodexHookRules.UserHook);
        Add(Path.Combine(machine.CodexHome, "config.toml"), Json(config.Hooks), CodexHookRules.UserHook);
        foreach (var directory in CodexModel.Chain(root, launch))
        {
            var folder = Path.Combine(directory, ".codex");
            if (!Directory.Exists(folder) || Paths.Same(folder, machine.CodexHome))
            {
                continue;
            }

            var off = config.IsTrusted(directory, root) ? null : CodexHookRules.ProjectUntrusted;
            Add(Path.Combine(folder, "hooks.json"), HooksJson(Path.Combine(folder, "hooks.json")), CodexHookRules.ProjectHook, layerOff: off);
            Add(Path.Combine(folder, "config.toml"), Json(CodexConfig.HooksIn(Path.Combine(folder, "config.toml"))), CodexHookRules.ProjectHook, layerOff: off);
        }

        var moments = new List<MomentHook>();
        foreach (var (moment, name, inputs) in new[] { (HookMoment.SessionStart, "SessionStart", new[] { "startup" }), (HookMoment.BeforeEdit, "PreToolUse", EditInputs), (HookMoment.AfterEdit, "PostToolUse", EditInputs) })
        {
            foreach (var hook in hooks.Where(hook => hook.Event == name && hook.Blocked != CodexHookRules.MatcherInvalid && inputs.Any(input => Matches(hook.Matcher, input) == true)))
            {
                var runs = hook.Blocked ?? (moment == HookMoment.SessionStart ? hook.Source : CodexHookRules.EveryEdit);
                moments.Add(new MomentHook(moment, hook, hook.Blocked is null, runs, inputs[0]));
            }
        }

        return (moments, hooks);
    }

    private static ConfiguredHook Configure(
        JsonObject handler, string file, string name, string key, string? matcher, string trustKey, HarnessRule source, bool managed, HarnessRule? layerOff, CodexConfig config)
    {
        var type = Text(handler["type"]) ?? "command";
        var command = OperatingSystem.IsWindows() ? Text(handler["commandWindows"]) ?? Text(handler["command_windows"]) ?? Text(handler["command"]) : Text(handler["command"]);
        var described = type == "mcp_tool" ? $"{Text(handler["server"])} {Text(handler["tool"])}".Trim() : command ?? type;
        var hook = new ConfiguredHook(file, name, matcher, described, null, source);
        if (type is not ("command" or "mcp_tool"))
        {
            return hook with { Blocked = CodexHookRules.HandlerSkipped };
        }

        if (Matches(matcher, "") is null)
        {
            return hook with { Blocked = CodexHookRules.MatcherInvalid };
        }

        var hash = Hash(name, key, matcher, type, handler, command);
        var state = config.HookStates.GetValueOrDefault(trustKey);
        var trust = managed ? "managed" : state.TrustedHash is null ? "untrusted" : state.TrustedHash == hash ? "trusted" : "modified";
        var blocked = layerOff
            ?? (managed ? null
            : state.Enabled == false ? CodexHookRules.Disabled
            : trust == "untrusted" ? CodexHookRules.Untrusted
            : trust == "modified" ? CodexHookRules.Modified
            : null);
        return hook with { Blocked = blocked, Trust = trust, Hash = hash };
    }

    // The hash covers the event, the matcher and the handler with its defaults filled in, as canonical JSON:
    // keys sorted, nothing between tokens. The recordings confirm it matches Codex's.
    private static string Hash(string name, string key, string? matcher, string type, JsonObject handler, string? command)
    {
        var normalized = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["type"] = type };
        var timeout = handler["timeout"] is JsonValue value && value.TryGetValue<long>(out var seconds) ? seconds : (long?)null;
        normalized["timeout"] = name is "SessionEnd" or "Interrupt" ? Math.Clamp(timeout ?? 1, 1, 3) : Math.Max(timeout ?? 600, 1);
        if (Text(handler["statusMessage"]) is { } status)
        {
            normalized["statusMessage"] = status;
        }

        if (type == "command")
        {
            normalized["command"] = command ?? "";
            normalized["async"] = handler["async"] is JsonValue async && async.TryGetValue<bool>(out var on) && on;
            if (ContextEvents.Contains(name) && handler["additionalContextLimit"] is JsonValue limit && limit.TryGetValue<long>(out var tokens) && tokens != DefaultContextLimit)
            {
                normalized["additionalContextLimit"] = tokens;
            }
        }
        else
        {
            normalized["server"] = Text(handler["server"]) ?? "";
            normalized["tool"] = Text(handler["tool"]) ?? "";
            normalized["input"] = handler["input"] as JsonObject ?? [];
        }

        var identity = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["event_name"] = key, ["hooks"] = new[] { normalized } };
        if (matcher is not null)
        {
            identity["matcher"] = matcher;
        }

        var text = new StringBuilder();
        Canonical(identity, text);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    // serde_json's compact form: only quotes, backslashes and control characters are escaped.
    private static void Canonical(object? value, StringBuilder text)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                break;
            case bool flag:
                text.Append(flag ? "true" : "false");
                break;
            case long number:
                text.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case string content:
                text.Append('"');
                foreach (var character in content)
                {
                    text.Append(character switch
                    {
                        '"' => "\\\"",
                        '\\' => "\\\\",
                        '\n' => "\\n",
                        '\r' => "\\r",
                        '\t' => "\\t",
                        '\b' => "\\b",
                        '\f' => "\\f",
                        < ' ' => $"\\u{(int)character:x4}",
                        _ => character.ToString(),
                    });
                }

                text.Append('"');
                break;
            case IDictionary<string, object?> map:
                text.Append('{');
                var first = true;
                foreach (var (name, item) in map)
                {
                    text.Append(first ? "" : ",");
                    first = false;
                    Canonical(name, text);
                    text.Append(':');
                    Canonical(item, text);
                }

                text.Append('}');
                break;
            case JsonObject node:
                Canonical(new SortedDictionary<string, object?>(node.ToDictionary(pair => pair.Key, pair => Plain(pair.Value)), StringComparer.Ordinal), text);
                break;
            case System.Collections.IEnumerable list:
                text.Append('[');
                var firstItem = true;
                foreach (var item in list)
                {
                    text.Append(firstItem ? "" : ",");
                    firstItem = false;
                    Canonical(item, text);
                }

                text.Append(']');
                break;
            default:
                text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static object? Plain(JsonNode? node) => node switch
    {
        JsonObject map => map,
        JsonArray list => list.Select(Plain).ToList(),
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag,
        JsonValue value when value.TryGetValue<long>(out var number) => number,
        JsonValue value => value.ToString(),
        _ => null,
    };

    // A matcher that's empty or * matches everything, one of only letters, digits, _ and | is an exact
    // list, and anything else is a regex search. Null means it isn't a valid regex.
    private static bool? Matches(string? matcher, string input)
    {
        if (string.IsNullOrEmpty(matcher) || matcher == "*")
        {
            return true;
        }

        if (matcher.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '|'))
        {
            return matcher.Split('|').Contains(input);
        }

        try
        {
            return new Regex(matcher, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).IsMatch(input);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static JsonObject? HooksJson(string file) => ClaudeSettings.Read(file)?["hooks"] as JsonObject;

    // A config file's [hooks] events, in the shape hooks.json has.
    private static JsonObject? Json(TomlTable? hooks)
    {
        if (hooks is null)
        {
            return null;
        }

        var events = new JsonObject();
        foreach (var (name, _) in Events)
        {
            if (hooks.TryGetValue(name, out var groups) && groups is TomlTableArray array)
            {
                events[name] = new JsonArray([.. array.Select(Node)]);
            }
        }

        return events;
    }

    private static JsonNode? Node(object? value) => value switch
    {
        TomlTable table => new JsonObject(table.Select(pair => KeyValuePair.Create(pair.Key, Node(pair.Value)))),
        TomlTableArray tables => new JsonArray([.. tables.Select(Node)]),
        TomlArray items => new JsonArray([.. items.Select(Node)]),
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        long number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        _ => null,
    };

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
