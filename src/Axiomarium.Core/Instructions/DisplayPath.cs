namespace Axiomarium.Core.Instructions;

/// <summary>How reports show a path, so the text and JSON output agree.</summary>
public static class DisplayPath
{
    /// <summary>
    /// Shows <paramref name="path"/> relative to <paramref name="repoRoot"/> with forward slashes, else under
    /// <paramref name="home"/> as <c>~/…</c>, else whole with forward slashes.
    /// </summary>
    /// <param name="path">An absolute path. The file name is kept exactly, trailing dots included.</param>
    /// <param name="repoRoot">The repo root, or <see langword="null"/> outside a repo.</param>
    /// <param name="home">The user's home folder.</param>
    /// <returns>The path as reports show it: <c>.</c> for the repo root itself and <c>~</c> for home.</returns>
    public static string Of(string path, string? repoRoot, string home)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        if (repoRoot is not null && Paths.IsUnder(path, repoRoot))
        {
            return Relative(repoRoot, path);
        }

        if (Paths.IsUnder(path, home))
        {
            var relative = Relative(home, path);
            return relative == "." ? "~" : $"~/{relative}";
        }

        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    // Only the folder goes through GetRelativePath, which on Windows would strip a trailing dot from the name.
    private static string Relative(string root, string path)
    {
        if (Paths.Same(path, root))
        {
            return ".";
        }

        var folder = Path.GetRelativePath(root, Path.GetDirectoryName(path)!).Replace(Path.DirectorySeparatorChar, '/');
        return folder == "." ? Path.GetFileName(path) : $"{folder}/{Path.GetFileName(path)}";
    }
}
