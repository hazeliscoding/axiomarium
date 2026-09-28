using System.Globalization;
using System.Text.RegularExpressions;
using Axiomarium.Core.Assets;

namespace Axiomarium.Core.Registry;

/// <summary>One dated entry in the usage log: an asset used on real work.</summary>
/// <param name="Date">The date in the entry's heading.</param>
/// <param name="Repo">The repo the heading names after the date, or empty when it names none.</param>
/// <param name="Assets">The asset folders the entry links to, such as <c>agents/determinism-auditor</c>, each once.</param>
public sealed record UsageEntry(DateOnly Date, string Repo, IReadOnlyList<string> Assets);

/// <summary>Reads <c>docs/dogfooding.md</c>, the log of assets used on real work.</summary>
/// <remarks>
/// An entry is a level-two heading that starts with a date, such as <c>## 2026-10-02 · carmine-workbench</c>,
/// and runs to the next level-one or level-two heading. It counts as usage of every asset folder it
/// links to. Other sections are free-form and never count.
/// </remarks>
public static partial class UsageLog
{
    /// <summary>Where the log lives, relative to the vault root.</summary>
    public const string File = "docs/dogfooding.md";

    /// <summary>Finds the log's entries.</summary>
    /// <param name="markdown">The log's text.</param>
    /// <returns>The entries, in file order. A heading whose date isn't a real date starts no entry.</returns>
    public static IReadOnlyList<UsageEntry> Parse(string markdown)
    {
        var entries = new List<UsageEntry>();
        (DateOnly Date, string Repo, List<string> Assets)? open = null;
        foreach (var line in markdown.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal) || line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (open is { } finished)
                {
                    entries.Add(new UsageEntry(finished.Date, finished.Repo, finished.Assets));
                }

                open = Heading(line);
                continue;
            }

            if (open is not { } entry)
            {
                continue;
            }

            foreach (Match link in Link().Matches(line))
            {
                if (AssetFolder(link.Groups[1].Value) is { } folder && !entry.Assets.Contains(folder))
                {
                    entry.Assets.Add(folder);
                }
            }
        }

        if (open is { } last)
        {
            entries.Add(new UsageEntry(last.Date, last.Repo, last.Assets));
        }

        return entries;
    }

    private static (DateOnly, string, List<string>)? Heading(string line)
    {
        var match = DatedHeading().Match(line);
        if (!match.Success || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        var repo = match.Groups[2].Value.Trim().TrimStart('·', '-', '–', '—', ':').Trim();
        return (date, repo, []);
    }

    // Links resolve from docs/, where the log lives, or from the root when they start with a slash.
    private static string? AssetFolder(string target)
    {
        if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith('#') || target.StartsWith("mailto:", StringComparison.Ordinal))
        {
            return null;
        }

        var end = target.IndexOfAny(['#', '?']);
        var path = end < 0 ? target : target[..end];
        var segments = new List<string>();
        foreach (var segment in (path.StartsWith('/') ? path : "docs/" + path).Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;
                case "..":
                    if (segments.Count == 0)
                    {
                        return null;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    break;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return segments.Count >= 2 && AssetKinds.All.Any(kind => kind.Folder() == segments[0]) ? $"{segments[0]}/{segments[1]}" : null;
    }

    [GeneratedRegex(@"^## (\d{4}-\d{2}-\d{2})(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex DatedHeading();

    [GeneratedRegex(@"\]\(([^)\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Link();
}
