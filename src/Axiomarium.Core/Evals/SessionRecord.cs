using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Core.Evals;

/// <summary>The tokens a session used, as its harness counted them.</summary>
/// <param name="Input">Every input token, cached or not.</param>
/// <param name="Cached">The input tokens read from the harness's cache, a part of <paramref name="Input"/>.</param>
/// <param name="Output">The tokens the model wrote, reasoning included where the harness counts it there.</param>
public sealed record TokenCount(long Input, long Cached, long Output)
{
    /// <summary>Adds two counts, such as a session's and its subagent's.</summary>
    /// <param name="left">One count.</param>
    /// <param name="right">The other.</param>
    /// <returns>The sum of each part.</returns>
    public static TokenCount operator +(TokenCount left, TokenCount right) =>
        new(left.Input + right.Input, left.Cached + right.Cached, left.Output + right.Output);
}

/// <summary>What one eval session did and cost, read from its harness's own records.</summary>
/// <param name="Activity">The skills and agents it loaded, the commands it ran and its final message.</param>
/// <param name="Tokens">The tokens it used, subagents included, or <see langword="null"/> when it ended before the harness counted them.</param>
/// <param name="ToolCalls">How many tools it called, such as shell commands, edits and agent spawns.</param>
/// <param name="Turns">How many turns it took, where the harness says: Claude Code only.</param>
/// <param name="Cost">What it cost in US dollars, where the harness says: Claude Code only.</param>
/// <param name="Denials">How many tool calls the harness denied it.</param>
/// <param name="Model">The model, where the stream names it, without a context-window suffix such as <c>[1m]</c>.</param>
/// <param name="Version">The harness's version, where the stream names it.</param>
/// <param name="Stopped">Why it ended before finishing, as a phrase such as <c>it reached the turn cap</c>, or <see langword="null"/> when it finished.</param>
public sealed record SessionRecord(
    SessionActivity Activity,
    TokenCount? Tokens,
    int ToolCalls,
    int? Turns,
    double? Cost,
    int Denials,
    string? Model,
    string? Version,
    string? Stopped);

/// <summary>
/// Reads a whole <c>claude -p --output-format stream-json --verbose</c> session, as the M5 spike confirmed on
/// Claude Code 2.1.285 (see <c>ROADMAP.md</c>).
/// </summary>
public static partial class ClaudeCodeSessions
{
    /// <summary>Reads a session from the lines the harness printed.</summary>
    /// <param name="lines">The lines. Lines that aren't JSON events are skipped.</param>
    /// <returns>
    /// The session. Its loads are its <c>Skill</c> calls and the agents its <c>Agent</c> calls ran, subagents'
    /// included; its commands, its <c>Bash</c> and <c>PowerShell</c> calls; its tokens, the <c>result</c> event's
    /// <c>modelUsage</c>, which counts subagents where <c>usage</c> doesn't.
    /// </returns>
    public static SessionRecord Read(IEnumerable<string> lines)
    {
        string? initModel = null, version = null, reply = null, model = null, stopped = "it ended without a result";
        int? turns = null;
        double? cost = null;
        TokenCount? tokens = null;
        var toolCalls = 0;
        var denials = 0;
        var loads = new List<string>();
        var commands = new List<string>();
        foreach (var line in lines)
        {
            if (JsonEvents.Parse(line) is not { } e)
            {
                continue;
            }

            switch (JsonEvents.Text(e, "type"))
            {
                case "system" when JsonEvents.Text(e, "subtype") == "init":
                    initModel = JsonEvents.Text(e, "model");
                    version = JsonEvents.Text(e, "claude_code_version");
                    break;
                case "assistant":
                    foreach (var call in JsonEvents.Items(JsonEvents.Child(JsonEvents.Child(e, "message"), "content"))
                        .Where(block => JsonEvents.Text(block, "type") == "tool_use"))
                    {
                        toolCalls++;
                        var input = JsonEvents.Child(call, "input");
                        var loaded = JsonEvents.Text(call, "name") switch
                        {
                            "Skill" => JsonEvents.Text(input, "skill"),
                            "Agent" or "Task" => JsonEvents.Text(input, "subagent_type"),
                            _ => null,
                        };
                        if (loaded is not null)
                        {
                            loads.Add(loaded);
                        }

                        if (JsonEvents.Text(call, "name") is "Bash" or "PowerShell" && JsonEvents.Text(input, "command") is { } command)
                        {
                            commands.Add(command);
                        }
                    }

                    break;
                case "result":
                    var failed = JsonEvents.Child(e, "is_error")?.ValueKind == JsonValueKind.True;
                    var subtype = JsonEvents.Text(e, "subtype");
                    reply = failed ? null : JsonEvents.Text(e, "result");
                    stopped = subtype == "error_max_turns" ? "it reached the turn cap"
                        : failed || subtype != "success" ? $"Claude Code reported an error: {JsonEvents.Text(e, "result") ?? subtype}"
                        : null;
                    turns = Number(e, "num_turns") is { } count ? (int)count : null;
                    cost = Number(e, "total_cost_usd");
                    denials = JsonEvents.Items(JsonEvents.Child(e, "permission_denials")).Count();
                    (tokens, model) = Usage(e, initModel);
                    break;
            }
        }

        return new SessionRecord(
            new SessionActivity([.. loads.Distinct(StringComparer.Ordinal)], commands, reply),
            tokens,
            toolCalls,
            turns,
            cost,
            denials,
            model ?? (initModel is null ? null : WindowSuffix().Replace(initModel, "")),
            version,
            stopped);
    }

    private static (TokenCount?, string?) Usage(JsonElement result, string? initModel)
    {
        TokenCount? total = null;
        string? model = null;
        if (JsonEvents.Child(result, "modelUsage") is { ValueKind: JsonValueKind.Object } byModel)
        {
            foreach (var entry in byModel.EnumerateObject())
            {
                var cached = Number(entry.Value, "cacheReadInputTokens") ?? 0;
                var count = new TokenCount(
                    (long)((Number(entry.Value, "inputTokens") ?? 0) + cached + (Number(entry.Value, "cacheCreationInputTokens") ?? 0)),
                    (long)cached,
                    (long)(Number(entry.Value, "outputTokens") ?? 0));
                total = total is null ? count : total + count;
                if (entry.Name == initModel)
                {
                    model = JsonEvents.Text(entry.Value, "canonicalModel");
                }
            }

            return (total, model);
        }

        // Older versions report only the main thread's usage.
        if (JsonEvents.Child(result, "usage") is { } usage)
        {
            var cached = Number(usage, "cache_read_input_tokens") ?? 0;
            total = new TokenCount(
                (long)((Number(usage, "input_tokens") ?? 0) + cached + (Number(usage, "cache_creation_input_tokens") ?? 0)),
                (long)cached,
                (long)(Number(usage, "output_tokens") ?? 0));
        }

        return (total, null);
    }

    private static double? Number(JsonElement? element, string name) =>
        JsonEvents.Child(element, name) is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;

    [GeneratedRegex(@"\[[^\]]*\]$", RegexOptions.CultureInvariant)]
    private static partial Regex WindowSuffix();
}

/// <summary>
/// Reads a whole <c>codex exec --json</c> session, and the rollouts Codex saves in its home for the custom agents
/// the session spawned, as the M5 spike confirmed on Codex 0.156.1 (see <c>ROADMAP.md</c>).
/// </summary>
public static class CodexSessions
{
    private static readonly string[] ToolItems = ["command_execution", "file_change", "collab_tool_call", "mcp_tool_call", "web_search"];

    /// <summary>Reads a session from the lines Codex printed.</summary>
    /// <param name="lines">The lines. Lines that aren't JSON events, such as log output, are skipped.</param>
    /// <param name="workingDirectory">Where Codex ran, which relative paths in its commands start from.</param>
    /// <param name="home">The home folder, which <c>~/</c> in its commands stands for.</param>
    /// <param name="sessionsFolder">
    /// The <c>sessions</c> folder of the Codex home the session ran with, whose rollouts name the custom agents it
    /// spawned and hold their tokens, or <see langword="null"/> when there is none, such as an ephemeral session.
    /// </param>
    /// <returns>
    /// The session. Its loads are the skills whose <c>SKILL.md</c> any command read and the agents its children ran
    /// as; its commands, what each command asked its shell to run. A child's own commands aren't read: Codex records
    /// them as code its code mode ran, not as commands.
    /// </returns>
    public static SessionRecord Read(IEnumerable<string> lines, string workingDirectory, string home, string? sessionsFolder)
    {
        string? thread = null, reply = null, error = null;
        var completed = false;
        var failed = false;
        TokenCount? tokens = null;
        var toolCalls = 0;
        var loads = new List<string>();
        var commands = new List<string>();
        foreach (var line in lines)
        {
            if (JsonEvents.Parse(line) is not { } e)
            {
                continue;
            }

            switch (JsonEvents.Text(e, "type"))
            {
                case "thread.started":
                    thread = JsonEvents.Text(e, "thread_id");
                    break;
                case "item.completed" when JsonEvents.Child(e, "item") is { } item:
                    var kind = JsonEvents.Text(item, "type");
                    if (ToolItems.Contains(kind))
                    {
                        toolCalls++;
                    }

                    if (kind == "command_execution" && JsonEvents.Text(item, "command") is { } command)
                    {
                        commands.Add(Unwrap(command));
                        loads.AddRange(CodexStream.SkillFiles(command, workingDirectory, home).Select(CodexStream.Folder));
                    }
                    else if (kind == "agent_message")
                    {
                        reply = JsonEvents.Text(item, "text");
                    }

                    break;
                case "turn.completed":
                    completed = true;
                    tokens = Tokens(JsonEvents.Child(e, "usage"));
                    break;
                case "turn.failed":
                    failed = true;
                    error = JsonEvents.Text(JsonEvents.Child(e, "error"), "message") ?? error;
                    break;
                case "error":
                    error = JsonEvents.Text(e, "message") ?? error;
                    break;
            }
        }

        if (thread is not null && sessionsFolder is not null && Directory.Exists(sessionsFolder))
        {
            var rollouts = Rollouts(sessionsFolder);
            foreach (var child in Descendants(rollouts, thread))
            {
                if (child.Agent is not null)
                {
                    loads.Add(child.Agent);
                }

                if (tokens is not null && child.Tokens is not null)
                {
                    tokens += child.Tokens;
                }
            }
        }

        var stopped = failed || (!completed && error is not null) ? $"Codex reported an error: {error ?? "the turn failed"}"
            : completed ? null
            : "it ended before its turn finished";
        return new SessionRecord(
            new SessionActivity([.. loads.Distinct(StringComparer.Ordinal)], commands, reply),
            completed ? tokens : null,
            toolCalls,
            null,
            null,
            0,
            null,
            null,
            stopped);
    }

    /// <summary>
    /// What <paramref name="command"/> asked its shell to run: the argument after PowerShell's <c>-Command</c>, or
    /// after a POSIX shell's <c>-c</c> or <c>-lc</c>, since Codex wraps every command in one.
    /// </summary>
    /// <param name="command">A command as Codex reports it.</param>
    /// <returns>The inner command, or <paramref name="command"/> itself when it isn't wrapped.</returns>
    public static string Unwrap(string command)
    {
        var words = CommandLine.Words(command);
        if (words.Count < 3)
        {
            return command;
        }

        var shell = CommandLine.Program(words[0]).ToLowerInvariant();
        var flag = shell switch
        {
            "pwsh" or "powershell" => words.FindIndex(1, word => word.Equals("-Command", StringComparison.OrdinalIgnoreCase)),
            "bash" or "sh" or "zsh" => words.FindIndex(1, word => word.Length > 1 && word[0] == '-' && word[1..].All(char.IsAsciiLetterLower) && word.Contains('c', StringComparison.Ordinal)),
            _ => -1,
        };
        return flag > 0 && flag + 1 < words.Count ? string.Join(' ', words.Skip(flag + 1)) : command;
    }

    private static TokenCount? Tokens(JsonElement? usage) =>
        usage is null ? null : new TokenCount(Count(usage, "input_tokens"), Count(usage, "cached_input_tokens"), Count(usage, "output_tokens"));

    private static long Count(JsonElement? element, string name) =>
        JsonEvents.Child(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var count) ? count : 0;

    // Each rollout starts with its thread's session_meta, which names its parent and the agent it runs as.
    private static List<(string Id, string? Parent, string? Agent, TokenCount? Tokens)> Rollouts(string sessionsFolder)
    {
        var rollouts = new List<(string, string?, string?, TokenCount?)>();
        foreach (var file in Directory.EnumerateFiles(sessionsFolder, "rollout-*.jsonl", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (ReadShared(file) is not { } lines)
            {
                continue;
            }

            var events = lines.Select(JsonEvents.Parse).OfType<JsonElement>().ToList();
            var meta = events.FirstOrDefault(e => JsonEvents.Text(e, "type") == "session_meta");
            var payload = JsonEvents.Child(meta, "payload");
            if (JsonEvents.Text(payload, "id") is not { } id)
            {
                continue;
            }

            var last = events.LastOrDefault(e => JsonEvents.Text(JsonEvents.Child(e, "payload"), "type") == "token_count");
            var usage = JsonEvents.Child(JsonEvents.Child(JsonEvents.Child(last, "payload"), "info"), "total_token_usage");
            rollouts.Add((id, JsonEvents.Text(payload, "parent_thread_id"), JsonEvents.Text(payload, "agent_role"), Tokens(usage)));
        }

        return rollouts;
    }

    // A stopped Codex can still hold its rollout open for writing, so the file is shared, and one that can't be
    // read at all is left out rather than losing the whole session.
    private static List<string>? ReadShared(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<(string Id, string? Parent, string? Agent, TokenCount? Tokens)> Descendants(
        List<(string Id, string? Parent, string? Agent, TokenCount? Tokens)> rollouts, string thread)
    {
        foreach (var child in rollouts.Where(rollout => rollout.Parent == thread))
        {
            yield return child;
            foreach (var grandchild in Descendants(rollouts, child.Id))
            {
                yield return grandchild;
            }
        }
    }
}
