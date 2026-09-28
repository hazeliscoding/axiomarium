namespace Axiomarium.Core.Instructions;

/// <summary>Path helpers the harness models share.</summary>
internal static class Paths
{
    private static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool Same(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), Comparison);

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
