using Axiomarium.Core.Paths;

namespace Axiomarium.Core.Instructions;

/// <summary>A rule file, with the folder its <c>paths</c> are relative to and when it loads.</summary>
/// <param name="Path">The rule file.</param>
/// <param name="Base">The folder its patterns match from: the one that holds <c>.claude/</c>, or the launch directory for user rules.</param>
/// <param name="Frontmatter">What its frontmatter says.</param>
internal sealed record RuleFile(string Path, string Base, RuleFrontmatter Frontmatter)
{
    // A rule whose frontmatter doesn't parse loads for every file, as one without paths does.
    public bool Always => Frontmatter.Paths is null;

    public bool Scoped => Frontmatter is { Valid: true, Paths: not null };

    /// <summary>A folder's rules, recursively, in path order.</summary>
    public static IReadOnlyList<RuleFile> In(string folder, string patternBase) =>
        Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => new RuleFile(path, patternBase, Instructions.Frontmatter.Read(File.ReadAllText(path))))]
            : [];

    /// <summary>Whether the rule's patterns match <paramref name="file"/>.</summary>
    public bool Matches(string file) => PathPatterns.MatchLikeGitignore(Frontmatter.Paths ?? [], Base, file);
}

/// <summary>Matches <c>paths</c> patterns, which rules and skills share, against a file.</summary>
internal static class PathPatterns
{
    /// <summary>
    /// Whether any of <paramref name="patterns"/> matches <paramref name="file"/> the way a <c>.gitignore</c>
    /// line would, relative to <paramref name="baseDirectory"/>, as Claude Code matches a rule's or a skill's <c>paths</c>.
    /// </summary>
    /// <remarks>
    /// A pattern without a slash matches a name at any depth, so <c>*.ts</c> matches <c>src/app.ts</c>. One
    /// with a slash is anchored at the base folder, a trailing slash or <c>/**</c> matches a folder, and a
    /// pattern that matches a folder matches every file in it. A file outside the base folder never
    /// matches. A pattern that isn't valid matches nothing, and the others still count. The patterns share
    /// a budget of 1,000 brace expansions; past it a pattern is used unexpanded, and its literal braces
    /// match nothing.
    /// </remarks>
    public static bool MatchLikeGitignore(IReadOnlyList<string> patterns, string baseDirectory, string file)
    {
        if (!Instructions.Paths.IsUnder(file, baseDirectory))
        {
            return false;
        }

        var parts = System.IO.Path.GetRelativePath(baseDirectory, file).Replace('\\', '/').Split('/');
        var folders = Enumerable.Range(1, parts.Length - 1).Select(count => string.Join('/', parts[..count])).ToList();
        long budget = 1000;
        foreach (var raw in patterns.Select(pattern => pattern.Trim()).Where(pattern => pattern.Length > 0 && !pattern.StartsWith('#')))
        {
            // The recordings show a trailing /** matching a folder at any depth, as a trailing slash does.
            budget -= Expansions(raw);
            var folderOnly = raw.EndsWith('/') || raw.EndsWith("/**", StringComparison.Ordinal);
            var pattern = raw.EndsWith("/**", StringComparison.Ordinal) ? raw[..^3] : raw.TrimEnd('/');
            var anchored = pattern.Contains('/');
            pattern = pattern.TrimStart('/');
            if (budget < 0 || pattern.Length == 0 || !Glob.TryParse(anchored ? pattern : "**/" + pattern, out var glob, out _))
            {
                continue;
            }

            if (folders.Any(glob.IsMatch) || (!folderOnly && glob.IsMatch(string.Join('/', parts))))
            {
                return true;
            }
        }

        return false;
    }

    // How many patterns the braces expand to: the product of each top-level group's alternatives.
    private static long Expansions(string pattern)
    {
        long total = 1;
        var depth = 0;
        var alternatives = 1;
        foreach (var character in pattern)
        {
            switch (character)
            {
                case '{':
                    if (depth++ == 0)
                    {
                        alternatives = 1;
                    }

                    break;
                case ',' when depth == 1:
                    alternatives++;
                    break;
                case '}' when depth > 0:
                    if (--depth == 0)
                    {
                        total = Math.Min(total * alternatives, long.MaxValue / 64);
                    }

                    break;
            }
        }

        return total;
    }
}
