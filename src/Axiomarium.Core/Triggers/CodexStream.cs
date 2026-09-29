using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Axiomarium.Core.Triggers;

/// <summary>What a <c>codex exec --json</c> session showed of the skills it loaded.</summary>
/// <param name="Reads">The <c>SKILL.md</c> files its first command read, in order, as absolute paths or as written when already absolute.</param>
/// <param name="Loads">The skills those files belong to, named by their folders, in the same order.</param>
/// <param name="Message">What it said before acting, from its first <c>agent_message</c>, or <see langword="null"/>.</param>
/// <param name="Error">Why its turn failed, or <see langword="null"/>.</param>
public sealed record CodexSession(IReadOnlyList<string> Reads, IReadOnlyList<string> Loads, string? Message, string? Error);

/// <summary>
/// Reads the events <c>codex exec --json</c> prints, one JSON object a line. Codex has no skill event: it reads a
/// skill's <c>SKILL.md</c> with a command, which its source counts as use, as the M4 spike confirmed on 0.156.1.
/// </summary>
public static partial class CodexStream
{
    /// <summary>Reads a session from its lines.</summary>
    /// <param name="lines">The lines Codex printed. Lines that aren't JSON events, such as log output, are skipped.</param>
    /// <param name="workingDirectory">Where Codex ran, which relative paths in its commands start from.</param>
    /// <param name="home">The home folder, which <c>~/</c> in its commands stands for.</param>
    /// <returns>The skills its first command read. A later command never counts, because the first action is the pick.</returns>
    public static CodexSession Read(IEnumerable<string> lines, string workingDirectory, string home)
    {
        string? command = null, message = null, error = null;
        foreach (var line in lines)
        {
            if (Event(line) is not { } e)
            {
                continue;
            }

            var item = e["item"] as JsonObject;
            if (command is null && item is not null && Text(item, "type") == "command_execution")
            {
                command = Text(item, "command") ?? "";
            }
            else if (message is null && item is not null && Text(item, "type") == "agent_message")
            {
                message = Text(item, "text");
            }
            else if (Text(e, "type") is "turn.failed" or "error")
            {
                error ??= e["error"] is JsonObject failure ? Text(failure, "message") : Text(e, "message");
            }
        }

        var reads = command is null ? [] : SkillFiles(command, workingDirectory, home);
        return new CodexSession(reads, [.. reads.Select(Folder)], message, error);
    }

    /// <summary>
    /// Whether <paramref name="line"/> ends the skills a session loads: its first command finishing, or the
    /// turn ending. The runner stops a trigger test's session there.
    /// </summary>
    /// <param name="line">One line Codex printed.</param>
    /// <returns><see langword="true"/> for a completed command or the end of the turn.</returns>
    public static bool EndsPick(string line) =>
        Event(line) is { } e && (Text(e, "type") is "turn.completed" or "turn.failed" or "error"
            || (Text(e, "type") == "item.completed" && e["item"] is JsonObject item && Text(item, "type") == "command_execution"));

    // Paths ending in SKILL.md: single-quoted, double-quoted or bare, in the order they appear. PowerShell
    // commands on Windows carry doubled backslashes.
    private static List<string> SkillFiles(string command, string workingDirectory, string home) =>
        [.. SkillPath().Matches(command)
            .Select(match => match.Groups.Values.Skip(1).First(group => group.Success).Value.Replace(@"\\", @"\", StringComparison.Ordinal))
            .Select(path => path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal) ? Path.Combine(home, path[2..])
                : Path.IsPathRooted(path) || WindowsDrive().IsMatch(path) ? path
                : Path.Combine(workingDirectory, path))];

    private static string Folder(string path)
    {
        var parts = path.Split('/', '\\');
        return parts.Length > 1 ? parts[^2] : path;
    }

    private static JsonObject? Event(string line)
    {
        if (!line.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    [GeneratedRegex("""'([^']*?SKILL\.md)'|"([^"']*?SKILL\.md)"|((?:[A-Za-z]:)?[^\s'"|;&<>(){}]*SKILL\.md)""", RegexOptions.CultureInvariant)]
    private static partial Regex SkillPath();

    [GeneratedRegex(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDrive();
}
