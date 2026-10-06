namespace Axiomarium.Core.Instructions;

/// <summary>Path helpers the harness models share.</summary>
internal static class Paths
{
    private static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool Same(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), Comparison);

    /// <summary>
    /// <paramref name="path"/> as Rust's <c>std::fs::canonicalize</c> spells it, which is how Codex spells a set
    /// <c>CODEX_HOME</c> and a project's folder: absolute, each link followed and, on Windows, each folder's name as it
    /// is on disk with an upper-case drive letter. Short 8.3 names are left as written.
    /// </summary>
    /// <param name="path">A path to a folder or file.</param>
    /// <returns>The canonical spelling, or <paramref name="path"/> unchanged when it doesn't exist or can't be read.</returns>
    public static string Canonical(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full) && !File.Exists(full))
            {
                return path;
            }

            var root = Path.GetPathRoot(full)!;
            var current = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
            foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = OperatingSystem.IsWindows()
                    ? new DirectoryInfo(current).EnumerateFileSystemInfos().FirstOrDefault(entry => string.Equals(entry.Name, part, StringComparison.OrdinalIgnoreCase))?.Name ?? part
                    : part;
                current = Path.Combine(current, name);
                var link = Directory.Exists(current) ? new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) : new FileInfo(current).ResolveLinkTarget(returnFinalTarget: true);
                if (link is not null)
                {
                    current = Canonical(link.FullName);
                }
            }

            return current;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            // A link cycle or a folder that can't be listed: Codex would fail to start there anyway.
            return path;
        }
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="directory"/> or anywhere inside it.</summary>
    public static bool IsUnder(string path, string directory) =>
        Same(path, directory)
        || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar, Comparison);

    /// <summary>From <paramref name="start"/> up to <paramref name="top"/> inclusive, or to the real root when start isn't under top.</summary>
    public static IEnumerable<string> Upward(string start, string top)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            yield return directory.FullName;
            if (Same(directory.FullName, top))
            {
                yield break;
            }
        }
    }

    /// <summary>The directories below <paramref name="launch"/> down to the one holding <paramref name="target"/>, top first. Empty when the target isn't below the launch directory.</summary>
    public static IReadOnlyList<string> Below(string launch, string target)
    {
        var below = new List<string>();
        for (var directory = new FileInfo(target).Directory; directory is not null; directory = directory.Parent)
        {
            if (Same(directory.FullName, launch))
            {
                below.Reverse();
                return below;
            }

            below.Add(directory.FullName);
        }

        return [];
    }
}
