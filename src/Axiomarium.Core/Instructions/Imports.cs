using System.Text.RegularExpressions;

namespace Axiomarium.Core.Instructions;

/// <summary>Finds a Claude Code memory file's <c>@path</c> imports.</summary>
internal static partial class Imports
{
    /// <summary>Each import's resolved path and line, in order of appearance.</summary>
    /// <remarks>
    /// Code spans and fenced blocks hold no imports. The path runs to the next whitespace, so trailing
    /// punctuation is part of it, as the recordings show. Relative paths resolve from the importing
    /// file's folder, and <c>~/</c> from the home folder.
    /// </remarks>
    public static IEnumerable<(string Path, int Line)> Find(string text, string importingFile, string home)
    {
        var folder = Path.GetDirectoryName(importingFile)!;
        var lines = text.Split('\n');
        string? fence = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var opener = Fence().Match(line);
            if (opener.Success && (fence is null || opener.Groups[1].Value.StartsWith(fence, StringComparison.Ordinal)))
            {
                fence = fence is null ? opener.Groups[1].Value : null;
                continue;
            }

            if (fence is not null)
            {
                continue;
            }

            foreach (Match import in Import().Matches(CodeSpan().Replace(line, " ")))
            {
                var path = import.Groups[1].Value;
                var resolved = path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, path[2..])
                    : Path.IsPathRooted(path) ? path
                    : Path.Combine(folder, path);

                // Only the folder is normalized: Windows would strip a trailing dot from the file name.
                yield return (Path.Combine(Path.GetFullPath(Path.GetDirectoryName(resolved)!), Path.GetFileName(resolved)), i + 1);
            }
        }
    }

    /// <summary>
    /// Whether the file exists under exactly this name. Windows ignores a trailing dot, so it would find
    /// "guide.md." as "guide.md", but Claude Code doesn't load it.
    /// </summary>
    public static bool Exists(string path)
    {
        var folder = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        return folder is not null && Directory.Exists(folder)
            && Directory.EnumerateFiles(folder).Any(file => string.Equals(
                Path.GetFileName(file), name, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^\s{0,3}(`{3,}|~{3,})", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();

    [GeneratedRegex(@"(`+).+?\1", RegexOptions.CultureInvariant)]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"(?<=^|\s)@(\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex Import();
}
