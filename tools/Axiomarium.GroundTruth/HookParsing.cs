using System.Text.Json.Nodes;

namespace Axiomarium.GroundTruth;

/// <summary>A scenario hook Claude Code ran.</summary>
/// <param name="File">The settings file that holds it, by its scenario path.</param>
/// <param name="Label">The label its marker gives it, unique in that file.</param>
/// <param name="Hook">When it ran, as Claude Code names it, such as PostToolUse:Edit.</param>
internal sealed record HookRun(string File, string Label, string Hook);

/// <summary>A hook Codex has configured, as `codex app-server` lists it.</summary>
/// <param name="File">The file that holds it, by its scenario path.</param>
/// <param name="Label">The label its marker gives it.</param>
/// <param name="Event">Its event, as Codex names it, such as preToolUse.</param>
/// <param name="Matcher">Its matcher, or null when it has none.</param>
/// <param name="Trust">Whether Codex will run it: trusted, untrusted, modified or managed.</param>
/// <param name="Hash">The hash Codex trusts it by.</param>
/// <param name="Enabled">Whether the user config leaves it on.</param>
internal sealed record CodexHook(string File, string Label, string Event, string? Matcher, string Trust, string Hash, bool Enabled);

internal static class HookMarkers
{
    // A scenario hook's command is "echo MARKER <file> <label>", so a recording can tell which hook it was.
    public static (string File, string Label)? In(string text)
    {
        const string prefix = "MARKER ";
        var line = text.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        var parts = line?[prefix.Length..].Trim().Split(' ', 2);
        return parts is [var file, var label] ? (file, label) : null;
    }
}

/// <summary>Reads the hooks Claude Code ran out of a session transcript.</summary>
internal static class ClaudeHookRuns
{
    private static readonly string[] RunTypes = ["hook_success", "hook_non_blocking_error", "hook_blocking_error"];

    private static readonly string[] EventOrder = ["SessionStart", "PreToolUse", "PostToolUse"];

    /// <summary>The scenario hooks that ran, sorted by event, then name, file and label.</summary>
    /// <param name="transcript">The session transcript's lines.</param>
    /// <param name="recorderCommand">The recorder's own hook command, which is left out.</param>
    public static IReadOnlyList<HookRun> Parse(IEnumerable<string> transcript, string recorderCommand)
    {
        var runs = new List<HookRun>();
        foreach (var line in transcript.Where(line => line.Length > 0))
        {
            if (JsonNode.Parse(line)?["attachment"] is not JsonObject attachment
                || attachment["type"]?.GetValue<string>() is not { } type
                || !RunTypes.Contains(type)
                || attachment["command"]?.GetValue<string>() == recorderCommand)
            {
                continue;
            }

            // Only the event: the command may be someone's private hook.
            var hook = attachment["hookName"]!.GetValue<string>();
            var (file, label) = HookMarkers.In(attachment["stdout"]?.GetValue<string>() ?? "")
                ?? throw new GroundTruthException($"A hook without a scenario marker ran on {hook}. Only scenario hooks may run while recording, so check this machine for managed settings.");
            runs.Add(new HookRun(file, label, hook));
        }

        // Hooks on one event run in parallel, so the transcript's order changes from run to run.
        return [.. runs
            .OrderBy(run => Array.IndexOf(EventOrder, run.Hook.Split(':')[0]) is var rank and >= 0 ? rank : EventOrder.Length)
            .ThenBy(run => run.Hook, StringComparer.Ordinal)
            .ThenBy(run => run.File, StringComparer.Ordinal)
            .ThenBy(run => run.Label, StringComparer.Ordinal)];
    }
}

/// <summary>Reads the answer to a `hooks/list` request to `codex app-server`.</summary>
internal static class CodexHookList
{
    /// <summary>The configured hooks in Codex's order, and Codex's warnings with paths made relative to the run.</summary>
    /// <param name="response">The JSON-RPC response to hooks/list for one working directory.</param>
    /// <param name="runDirectory">The folder the scenario was copied to, which holds repo/ and home/.</param>
    public static (IReadOnlyList<CodexHook> Hooks, IReadOnlyList<string> Warnings) Parse(string response, string runDirectory)
    {
        var run = SkillEntries.Slashes(runDirectory).TrimEnd('/') + "/";
        var entry = JsonNode.Parse(response)!["result"]!["data"]!.AsArray().Single()!;
        var hooks = new List<CodexHook>();
        foreach (var hook in entry["hooks"]!.AsArray().Select(hook => hook!))
        {
            // Only the path: the hook may be someone's private one.
            var source = SkillEntries.Slashes(hook["sourcePath"]!.GetValue<string>());
            var file = source.StartsWith(run, StringComparison.Ordinal)
                ? source[run.Length..]
                : throw new GroundTruthException($"Codex lists a hook from {source}, which is outside the scenario. Make sure CODEX_HOME points at the scenario's home.");
            var marker = HookMarkers.In(hook["command"]?.GetValue<string>() is { } command && command.StartsWith("echo ", StringComparison.Ordinal) ? command[5..] : "");
            if (marker is not { } found || found.File != file)
            {
                throw new GroundTruthException($"Codex lists a hook in {file} without its marker. Make its command \"echo MARKER {file} <label>\".");
            }

            hooks.Add(new CodexHook(
                file,
                found.Label,
                hook["eventName"]!.GetValue<string>(),
                hook["matcher"]?.GetValue<string>(),
                hook["trustStatus"]!.GetValue<string>(),
                hook["currentHash"]!.GetValue<string>(),
                hook["enabled"]?.GetValue<bool>() ?? true));
        }

        var warnings = (entry["warnings"] as JsonArray ?? [])
            .Select(warning => warning!.GetValue<string>())
            .Concat((entry["errors"] as JsonArray ?? []).Select(error => "error: " + (error?["message"]?.GetValue<string>() ?? error?.ToJsonString())))
            .Select(text => SkillEntries.Slashes(text).Replace(run, "", StringComparison.Ordinal));
        return (hooks, [.. warnings]);
    }
}
