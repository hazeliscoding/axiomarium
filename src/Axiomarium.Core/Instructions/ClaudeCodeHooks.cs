using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axiomarium.Core.Paths;

namespace Axiomarium.Core.Instructions;

/// <summary>Which Claude Code hooks run at session start and around an edit of a file.</summary>
/// <remarks>
/// Follows the Claude Code hooks and permissions docs as read on 2026-09-28, confirmed against Claude Code
/// <see cref="ClaudeCodeModel.ConfirmedWith"/> by the spike and the recordings in <c>scenarios/</c>. It assumes
/// a trusted workspace, as a <c>claude -p</c> session does.
/// </remarks>
internal static partial class ClaudeCodeHooks
{
    private static readonly string[] ToolEvents = ["PreToolUse", "PostToolUse", "PostToolUseFailure", "PermissionRequest", "PermissionDenied"];
    private static readonly string[] EditTools = ["Edit", "Write", "NotebookEdit"];

    /// <summary>The hooks at each moment for <paramref name="target"/>, and every configured hook.</summary>
    public static (IReadOnlyList<MomentHook> Hooks, IReadOnlyList<ConfiguredHook> Configured) Resolve(string launch, string target, Machine machine, ClaudeSettings settings)
    {
        var hooks = new List<Hook>();
        void Add(string file, JsonNode? events, HarnessRule source, string anchor, bool managed = false, bool plugin = false)
        {
            foreach (var (name, groups) in events as JsonObject ?? [])
            {
                foreach (var group in groups as JsonArray ?? [])
                {
                    var matcher = group?["matcher"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
                    foreach (var handler in group?["hooks"] as JsonArray ?? [])
                    {
                        var condition = Text(handler?["if"]);
                        var configured = new ConfiguredHook(file, name, matcher, Describe(handler), condition, source)
                        {
                            Blocked = condition is not null && !ToolEvents.Contains(name) ? ClaudeCodeHookRules.IfIgnored
                                : Matches(matcher, "") is null ? ClaudeCodeHookRules.MatcherInvalid
                                : settings.HooksOff == HooksOff.All || (!managed && settings.HooksOff == HooksOff.AllButManaged) ? ClaudeCodeHookRules.HooksDisabled
                                : !managed && settings.ManagedHooksOnly ? ClaudeCodeHookRules.ManagedHooksOnly
                                : !managed && !plugin && settings.PluginHooksOnly ? ClaudeCodeHookRules.PluginHooksOnly
                                : null,
                        };
                        hooks.Add(new Hook(configured, Text(handler?["type"]) ?? "command", anchor, plugin));
                    }
                }
            }
        }

        var managedFile = Path.Combine(machine.ClaudeManaged, "managed-settings.json");
        var userFile = Path.Combine(machine.ClaudeConfig, "settings.json");
        var projectFile = Path.Combine(launch, ".claude", "settings.json");
        var localFile = Path.Combine(launch, ".claude", "settings.local.json");
        Add(managedFile, ClaudeSettings.Read(managedFile)?["hooks"], ClaudeCodeHookRules.ManagedHook, launch, managed: true);
        Add(userFile, ClaudeSettings.Read(userFile)?["hooks"], ClaudeCodeHookRules.UserHook, machine.ClaudeConfig);
        Add(projectFile, ClaudeSettings.Read(projectFile)?["hooks"], ClaudeCodeHookRules.ProjectHook, launch);
        Add(localFile, ClaudeSettings.Read(localFile)?["hooks"], ClaudeCodeHookRules.LocalHook, launch);
        foreach (var (_, root) in ClaudeCodeSkills.Plugins(machine, settings))
        {
            var manifest = Path.Combine(root, ".claude-plugin", "plugin.json");
            foreach (var file in ClaudeCodeSkills.ManifestPaths(root, "hooks").Prepend(Path.Combine(root, "hooks", "hooks.json")).Distinct())
            {
                Add(file, ClaudeSettings.Read(file)?["hooks"], ClaudeCodeHookRules.PluginHook, launch, plugin: true);
            }

            if (ClaudeSettings.Read(manifest)?["hooks"] is JsonObject inline)
            {
                Add(manifest, inline["hooks"] ?? inline, ClaudeCodeHookRules.PluginHook, launch, plugin: true);
            }
        }

        // The agent edits a file that exists with Edit, writes a new one with Write, and edits a notebook with NotebookEdit.
        var tool = Path.GetExtension(target).Equals(".ipynb", StringComparison.OrdinalIgnoreCase) ? "NotebookEdit" : File.Exists(target) ? "Edit" : "Write";
        var moments = new List<MomentHook>();
        foreach (var (moment, name, input) in new[] { (HookMoment.SessionStart, "SessionStart", "startup"), (HookMoment.BeforeEdit, "PreToolUse", tool), (HookMoment.AfterEdit, "PostToolUse", tool) })
        {
            var seen = new HashSet<(string, string, string, string?)>();
            foreach (var hook in hooks.Where(hook => hook.Configured.Event == name && Matches(hook.Configured.Matcher, input) == true))
            {
                var configured = hook.Configured;
                var rule = configured.Blocked
                    ?? (!hook.Plugin && !seen.Add((name, hook.Type, configured.Handler, configured.Condition)) ? ClaudeCodeHookRules.Duplicate
                    : configured.Condition is { } condition && !ConditionMatches(condition, tool, target, hook.Anchor, launch, machine) ? ClaudeCodeHookRules.IfNoMatch
                    : null);
                moments.Add(new MomentHook(moment, configured, rule is null, rule ?? configured.Source, input));
            }
        }

        return (moments, [.. hooks.Select(hook => hook.Configured)]);
    }

    // A matcher that's empty or * matches everything, one of only name characters is an exact list, and
    // anything else is an unanchored regex. Null means it isn't a valid regex.
    private static bool? Matches(string? matcher, string input)
    {
        if (string.IsNullOrEmpty(matcher) || matcher == "*")
        {
            return true;
        }

        if (ExactMatcher().IsMatch(matcher))
        {
            return matcher.Split('|', ',').Select(name => name.Trim()).Contains(input);
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

    // An if is one permission rule, Tool or Tool(pattern). An Edit rule covers every tool that edits files.
    private static bool ConditionMatches(string condition, string tool, string target, string anchor, string launch, Machine machine)
    {
        var rule = PermissionRule().Match(condition.Trim());
        if (!rule.Success || !(rule.Groups[1].Value == tool || (rule.Groups[1].Value == "Edit" && EditTools.Contains(tool))))
        {
            return false;
        }

        return !rule.Groups[2].Success || PathMatches(rule.Groups[2].Value, target, anchor, launch, machine);
    }

    // A file pattern follows the permissions docs: //path from the filesystem root, ~/path from home, /path
    // from the settings file's folder, and anything else from the launch directory, where a bare name
    // matches at any depth and a pattern with a slash is anchored.
    private static bool PathMatches(string pattern, string target, string anchor, string launch, Machine machine)
    {
        if (pattern.StartsWith("//", StringComparison.Ordinal))
        {
            return Glob.TryParse(pattern[1..], out var absolute, out _) && absolute.IsMatch(Posix(target));
        }

        var (folder, relative) = pattern.StartsWith("~/", StringComparison.Ordinal) ? (machine.Home, pattern[2..])
            : pattern.StartsWith('/') ? (anchor, pattern[1..])
            : (launch, pattern.StartsWith("./", StringComparison.Ordinal) ? pattern[2..] : pattern);
        var anchored = pattern.StartsWith('/') || pattern.StartsWith("~/", StringComparison.Ordinal) || relative.TrimEnd('/').Contains('/');
        if (!Instructions.Paths.IsUnder(target, folder) || !Glob.TryParse(anchored ? relative.TrimEnd('/') : "**/" + relative.TrimEnd('/'), out var glob, out _))
        {
            return false;
        }

        var parts = Path.GetRelativePath(folder, target).Replace('\\', '/').Split('/');
        return Enumerable.Range(1, parts.Length).Any(count => glob.IsMatch(string.Join('/', parts[..count])));
    }

    // Windows paths are matched in POSIX form, as /c/Users/...
    private static string Posix(string path)
    {
        var full = Path.GetFullPath(path).Replace('\\', '/');
        return full.Length > 1 && full[1] == ':' ? $"/{char.ToLowerInvariant(full[0])}{full[2..]}" : full;
    }

    private static string Describe(JsonNode? handler) =>
        Text(handler?["command"]) ?? Text(handler?["url"])
        ?? (Text(handler?["server"]) is { } server ? $"{server} {Text(handler?["tool"])}".TrimEnd() : null)
        ?? Text(handler?["type"]) ?? "command";

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    [GeneratedRegex(@"^[A-Za-z0-9_\- ,|]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactMatcher();

    [GeneratedRegex(@"^([A-Za-z_]+)(?:\((.*)\))?$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex PermissionRule();

    // A configured hook, with its handler's type, where a /path pattern in its if is anchored, and whether a plugin holds it.
    private sealed record Hook(ConfiguredHook Configured, string Type, string Anchor, bool Plugin);
}
