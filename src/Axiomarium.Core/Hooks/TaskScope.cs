using Axiomarium.Core.Paths;

namespace Axiomarium.Core.Hooks;

/// <summary>A task's declared scope, read from <c>.axm/scope</c>: one glob per line, relative to the folder that holds <c>.axm/</c>.</summary>
/// <param name="Root">The folder the patterns are relative to.</param>
/// <param name="Patterns">The valid patterns.</param>
/// <param name="Problems">A sentence for each line that isn't a valid pattern, naming the line.</param>
internal sealed record TaskScope(string Root, IReadOnlyList<Glob> Patterns, IReadOnlyList<string> Problems)
{
    /// <summary>Where the scope lives, relative to its root.</summary>
    public const string File = ".axm/scope";

    /// <summary>Finds the nearest scope at or above <paramref name="directory"/>.</summary>
    /// <param name="directory">Where to start looking, usually the session's working directory.</param>
    /// <returns>
    /// The scope, or <see langword="null"/> when there is no scope file, or it has only blank lines and
    /// comments, which declares no scope.
    /// </returns>
    public static TaskScope? Find(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var path = Path.Combine(current.FullName, ".axm", "scope");
            if (System.IO.File.Exists(path))
            {
                return Parse(current.FullName, System.IO.File.ReadAllText(path));
            }
        }

        return null;
    }

    private static TaskScope? Parse(string root, string text)
    {
        var patterns = new List<Glob>();
        var problems = new List<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // Patterns are relative to the root, so ./src and /src both mean src.
            var pattern = line.StartsWith("./", StringComparison.Ordinal) ? line[2..] : line.TrimStart('/');
            if (Glob.TryParse(pattern, out var glob, out var problem))
            {
                patterns.Add(glob);
            }
            else
            {
                problems.Add($"Line {i + 1} of {File} matches nothing: {problem}");
            }
        }

        return patterns.Count == 0 && problems.Count == 0 ? null : new TaskScope(root, patterns, problems);
    }
}
