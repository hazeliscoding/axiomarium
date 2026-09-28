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
    /// <remarks>
    /// A pattern that isn't valid matches nothing, and the others still count. The patterns share a
    /// budget of 1,000 brace expansions; past it a pattern is used unexpanded, and its literal braces
    /// match nothing.
    /// </remarks>
    public bool Matches(string file)
    {
        if (!Instructions.Paths.IsUnder(file, Base))
        {
            return false;
        }

        var relative = System.IO.Path.GetRelativePath(Base, file).Replace('\\', '/');
        long budget = 1000;
        foreach (var pattern in Frontmatter.Paths ?? [])
        {
            budget -= Expansions(pattern);
            if (budget >= 0 && Glob.TryParse(pattern, out var glob, out _) && glob.IsMatch(relative))
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
