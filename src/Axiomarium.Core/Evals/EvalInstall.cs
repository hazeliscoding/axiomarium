using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Core.Evals;

/// <summary>The files that install an asset where one harness finds it, or why <c>axm eval</c> can't install it.</summary>
/// <param name="Copy">Files to write into the session's copy of the case's repo, relative to its root.</param>
/// <param name="CodexHome">Files to write into the sealed Codex home, relative to it.</param>
/// <param name="Problem">Why the asset can't be installed for the harness, or <see langword="null"/>.</param>
public sealed record Installation(IReadOnlyList<WorkspaceFile> Copy, IReadOnlyList<WorkspaceFile> CodexHome, string? Problem);

/// <summary>
/// Installs an asset for an eval session the way <c>axm sync</c> will in v0.9: as each harness's own files. Reads,
/// never writes: the runner writes the files it returns.
/// </summary>
public static class EvalInstall
{
    // The editing tools, as Claude Code names them, confirmed on 2.1.283 (see ROADMAP.md's hook decisions).
    private const string Edits = "Write|Edit|NotebookEdit";

    /// <summary>Why <c>axm eval</c> can't install an asset of <paramref name="kind"/> for <paramref name="harness"/>.</summary>
    /// <param name="kind">The asset's kind.</param>
    /// <param name="harness">The harness.</param>
    /// <returns>The reason, as sentences, or <see langword="null"/> when it can: skills and agents on both harnesses, hooks on Claude Code.</returns>
    public static string? Refusal(AssetKind kind, Harness harness) => kind switch
    {
        AssetKind.Skill or AssetKind.Agent => null,
        AssetKind.Hook => harness == Harness.ClaudeCode ? null : "axm eval installs hooks for Claude Code only.",
        AssetKind.Policy => "axm eval runs skills, hooks and agents. A policy's rules are tested through the assets that enforce them.",
        AssetKind.Workflow => "axm eval runs skills, hooks and agents. A workflow is tested through the assets it uses.",
        _ => "axm eval runs skills, hooks and agents. An experiment is a hypothesis to test, not something to install.",
    };

    /// <summary>Plans the files that install <paramref name="asset"/> for <paramref name="harness"/>.</summary>
    /// <param name="asset">The asset, whose manifest must be valid.</param>
    /// <param name="body">Its content file's text.</param>
    /// <param name="harness">The harness the session runs on.</param>
    /// <param name="axm">The path of the running <c>axm</c>, which a hook's leading <c>axm</c> becomes.</param>
    /// <param name="caseSettings">The case repo's own <c>.claude/settings.json</c>, which a hook joins, or <see langword="null"/>.</param>
    /// <returns>
    /// A skill as each harness's skill file; a hook as an entry in the copy's <c>.claude/settings.json</c>; an agent
    /// as a Claude Code subagent in the copy, or a custom agent in the sealed Codex home. Otherwise, why not.
    /// </returns>
    /// <exception cref="ArgumentException">The asset has no valid manifest.</exception>
    public static Installation Plan(DiscoveredAsset asset, string body, Harness harness, string axm, string? caseSettings)
    {
        var manifest = asset.Manifest ?? throw new ArgumentException($"{asset.Folder} has no valid manifest.", nameof(asset));
        if (Refusal(asset.Kind, harness) is { } refusal)
        {
            return new Installation([], [], refusal);
        }

        body = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        return asset.Kind switch
        {
            AssetKind.Skill => new Installation([SkillFile(asset.Name, manifest.Description, manifest.UseWhen ?? "", body, harness)], [], null),
            AssetKind.Hook => Hook(manifest.Hook ?? throw new ArgumentException($"{asset.Folder} has no hook block.", nameof(asset)), axm, caseSettings),
            _ when harness == Harness.ClaudeCode => new Installation(
                [new WorkspaceFile($".claude/agents/{asset.Name}.md", $"---\nname: {asset.Name}\ndescription: {TriggerPrompts.Quoted(manifest.Description)}\n---\n{body}")], [], null),
            // A JSON string is a valid TOML basic string, so any instructions round-trip.
            _ => new Installation(
                [],
                [new WorkspaceFile(
                    $"agents/{asset.Name}.toml",
                    $"name = {TriggerPrompts.Quoted(asset.Name)}\ndescription = {TriggerPrompts.Quoted(manifest.Description)}\ndeveloper_instructions = {TriggerPrompts.Quoted(body)}\n")],
                null),
        };
    }

    /// <summary>A skill as <paramref name="harness"/>'s skill file, in its repo folder, as sync would write it.</summary>
    /// <param name="name">The skill's name.</param>
    /// <param name="description">Its <c>description</c>.</param>
    /// <param name="useWhen">Its <c>skill.use_when</c>.</param>
    /// <param name="body">Its content, with <c>\n</c> line ends.</param>
    /// <param name="harness">The harness.</param>
    /// <returns>
    /// <c>.claude/skills/&lt;name&gt;/SKILL.md</c> with <c>when_to_use</c> for Claude Code, and
    /// <c>.agents/skills/&lt;name&gt;/SKILL.md</c> for Codex, which has no <c>when_to_use</c>, so both go in its description.
    /// </returns>
    public static WorkspaceFile SkillFile(string name, string description, string useWhen, string body, Harness harness) =>
        harness == Harness.ClaudeCode
            ? new WorkspaceFile(
                $".claude/skills/{name}/SKILL.md",
                $"---\nname: {name}\ndescription: {TriggerPrompts.Quoted(description)}\nwhen_to_use: {TriggerPrompts.Quoted(useWhen)}\n---\n{body}")
            : new WorkspaceFile(
                $".agents/skills/{name}/SKILL.md",
                $"---\nname: {name}\ndescription: {TriggerPrompts.Quoted($"{description} - {useWhen}")}\n---\n{body}");

    private static Installation Hook(HookBlock hook, string axm, string? caseSettings)
    {
        var (claudeEvent, matcher) = hook.Event switch
        {
            "session-start" => ("SessionStart", "startup"),
            "before-edit" => ("PreToolUse", Edits),
            _ => ("PostToolUse", Edits),
        };

        // Claude Code may run hooks through bash, which reads forward slashes on Windows too.
        var command = hook.Command == "axm" || hook.Command.StartsWith("axm ", StringComparison.Ordinal)
            ? $"\"{axm.Replace('\\', '/')}\"{hook.Command[3..]}"
            : hook.Command;

        JsonObject settings;
        try
        {
            settings = caseSettings is null ? [] : JsonNode.Parse(caseSettings) as JsonObject ?? [];
        }
        catch (JsonException problem)
        {
            return new Installation([], [], $"The case's .claude/settings.json isn't valid JSON: {problem.Message}");
        }

        if (settings["hooks"] is not JsonObject hooks)
        {
            hooks = [];
            settings["hooks"] = hooks;
        }

        if (hooks[claudeEvent] is not JsonArray entries)
        {
            entries = [];
            hooks[claudeEvent] = entries;
        }

        // The JsonNode overload, since the generic one isn't safe under NativeAOT.
        entries.Add((JsonNode)new JsonObject
        {
            ["matcher"] = matcher,
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command }),
        });

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            settings.WriteTo(writer);
        }

        return new Installation([new WorkspaceFile(".claude/settings.json", Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n")], [], null);
    }
}
