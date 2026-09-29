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
    /// <returns>
    /// The skills it read before its first other action: every skill read by its leading commands that only read
    /// skills, and by the first command that does anything else. Commands after that never count.
    /// </returns>
    public static CodexSession Read(IEnumerable<string> lines, string workingDirectory, string home)
    {
        string? message = null, error = null;
        var commands = new List<(string Id, string Command)>();
        foreach (var line in lines)
        {
            if (JsonEvents.Parse(line) is not { } e)
            {
                continue;
            }

            var item = JsonEvents.Child(e, "item");
            if (JsonEvents.Text(item, "type") == "command_execution")
            {
                // Each command is reported when it starts and again when it completes.
                var id = JsonEvents.Text(item, "id") ?? $"#{commands.Count}";
                if (commands.All(command => command.Id != id))
                {
                    commands.Add((id, JsonEvents.Text(item, "command") ?? ""));
                }
            }
            else if (message is null && JsonEvents.Text(item, "type") == "agent_message")
            {
                message = JsonEvents.Text(item, "text");
            }
            else if (JsonEvents.Text(e, "type") is "turn.failed" or "error")
            {
                error ??= JsonEvents.Text(JsonEvents.Child(e, "error"), "message") ?? JsonEvents.Text(e, "message");
            }
        }

        var reads = new List<string>();
        foreach (var (_, command) in commands)
        {
            reads.AddRange(SkillFiles(command, workingDirectory, home));
            if (!ReadsOnlySkills(command))
            {
                break;
            }
        }

        return new CodexSession(reads, [.. reads.Select(Folder)], message, error);
    }

    /// <summary>
    /// Whether <paramref name="line"/> ends the skills a session loads: a finished command that does more than
    /// read skills, or the turn ending. The runner stops a trigger test's session there.
    /// </summary>
    /// <param name="line">One line Codex printed.</param>
    /// <returns><see langword="true"/> for a completed command that isn't only skill reads, or the end of the turn.</returns>
    public static bool EndsPick(string line) =>
        JsonEvents.Parse(line) is { } e && (JsonEvents.Text(e, "type") is "turn.completed" or "turn.failed" or "error"
            || (JsonEvents.Text(e, "type") == "item.completed" && JsonEvents.Child(e, "item") is { } item
                && JsonEvents.Text(item, "type") == "command_execution" && !ReadsOnlySkills(JsonEvents.Text(item, "command") ?? "")));

    // A command that only reads skills names a SKILL.md in each of its steps, whether it runs them through
    // PowerShell, as on Windows, or bash.
    private static bool ReadsOnlySkills(string command)
    {
        var steps = Steps().Split(command).Where(step => step.Trim().Trim('"', '\\').Length > 0).ToList();
        return steps.Count > 0 && steps.All(step => step.Contains("SKILL.md", StringComparison.Ordinal));
    }

    // Paths ending in SKILL.md: single-quoted, double-quoted or bare, in the order they appear. PowerShell
    // commands on Windows carry doubled backslashes.
    internal static List<string> SkillFiles(string command, string workingDirectory, string home) =>
        [.. SkillPath().Matches(command)
            .Select(match => match.Groups.Values.Skip(1).First(group => group.Success).Value.Replace(@"\\", @"\", StringComparison.Ordinal))
            .Select(path => path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal) ? Path.Combine(home, path[2..])
                : Path.IsPathRooted(path) || WindowsDrive().IsMatch(path) ? path
                : Path.Combine(workingDirectory, path))];

    internal static string Folder(string path)
    {
        var parts = path.Split('/', '\\');
        return parts.Length > 1 ? parts[^2] : path;
    }

    [GeneratedRegex("""'([^']*?SKILL\.md)'|"([^"']*?SKILL\.md)"|((?:[A-Za-z]:)?[^\s'"|;&<>(){}]*SKILL\.md)""", RegexOptions.CultureInvariant)]
    private static partial Regex SkillPath();

    [GeneratedRegex(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDrive();

    [GeneratedRegex(@";|&&|\|\||\n", RegexOptions.CultureInvariant)]
    private static partial Regex Steps();
}
