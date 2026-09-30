using System.Text.RegularExpressions;

namespace Axiomarium.Core.Evals;

/// <summary>
/// Finds the absolute paths a session's commands name outside its copy. On Windows, Codex's sandbox can read the whole
/// disk (see the M5 spike in <c>ROADMAP.md</c>), so a run can read the answers from a real repo, and the report says so.
/// </summary>
public static partial class OutsidePaths
{
    // Where tools live, and the null device: naming them isn't leaving the copy to read someone's files.
    private static readonly string[] WindowsSystem = [@"\Windows", @"\Program Files", @"\Program Files (x86)", @"\ProgramData"];
    private static readonly string[] PosixSystem = ["/dev/", "/usr/", "/bin/", "/sbin/", "/etc/", "/proc/", "/sys/", "/lib/", "/lib64/", "/opt/", "/System/", "/Library/"];

    /// <summary>The absolute paths <paramref name="commands"/> name outside <paramref name="copyRoot"/>.</summary>
    /// <param name="commands">The commands a session ran.</param>
    /// <param name="copyRoot">The session's copy, absolute.</param>
    /// <param name="allowed">Other folders a session may name, such as the folder of the <c>axm</c> it runs.</param>
    /// <returns>
    /// Each path once, in the order it first appears, with doubled backslashes read as one. Paths in the copy, in
    /// <paramref name="allowed"/> and in the system's own folders are left out. A relative path that climbs out of the
    /// copy, such as <c>../x</c>, isn't found.
    /// </returns>
    public static IReadOnlyList<string> Of(IEnumerable<string> commands, string copyRoot, IEnumerable<string> allowed)
    {
        var roots = allowed.Prepend(copyRoot).Select(Normal).ToList();
        var windows = Drive().IsMatch(copyRoot);
        var found = new List<string>();
        foreach (var command in commands)
        {
            foreach (Match match in Path().Matches(command))
            {
                var written = match.Groups.Values.Skip(1).First(group => group.Success).Value.TrimEnd('.', ',', ')', ':');

                // Git Bash, which Claude Code's Bash tool is on Windows, writes C:\x as /c/x.
                if (windows && GitBash().Match(written) is { Success: true } bash)
                {
                    written = $"{char.ToUpperInvariant(bash.Groups[1].Value[0])}:\\{bash.Groups[2].Value}";
                }

                var path = Normal(written);
                if (path.Length > 3 && !System(path) && !roots.Any(root => Under(path, root)) && !found.Contains(path, Comparer(path)))
                {
                    found.Add(path);
                }
            }
        }

        return found;
    }

    private static string Normal(string path) =>
        Drive().IsMatch(path) ? path.Replace(@"\\", @"\", StringComparison.Ordinal).Replace('/', '\\').TrimEnd('\\') : path.TrimEnd('/');

    private static bool Under(string path, string root)
    {
        var comparison = Drive().IsMatch(path) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var separator = Drive().IsMatch(path) ? '\\' : '/';
        return path.Equals(root, comparison) || path.StartsWith(root + separator, comparison);
    }

    private static bool System(string path) => Drive().IsMatch(path)
        ? WindowsSystem.Any(folder => path[2..].Equals(folder, StringComparison.OrdinalIgnoreCase) || path[2..].StartsWith(folder + '\\', StringComparison.OrdinalIgnoreCase))
        : PosixSystem.Any(folder => path.StartsWith(folder, StringComparison.Ordinal));

    private static StringComparer Comparer(string path) => Drive().IsMatch(path) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // A Windows path quoted or bare, or a POSIX path of two parts or more that isn't part of a URL, another path or a
    // glob's exclusion such as rg's -g '!/.git/**'.
    [GeneratedRegex("""'([A-Za-z]:[\\/][^']*)'|"([A-Za-z]:[\\/][^"]*)"|(?<![\w\\/])([A-Za-z]:[\\/][^\s'"`;|&<>]*)|(?<![\w.:~\\/!-])(/[\w.~-]+/[^\s'"`;|&<>]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex Path();

    [GeneratedRegex(@"^[A-Za-z]:", RegexOptions.CultureInvariant)]
    private static partial Regex Drive();

    [GeneratedRegex(@"^/([A-Za-z])/(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex GitBash();
}
