using System.Text.RegularExpressions;

namespace Axiomarium.Core.Instructions;

/// <summary>The parts of a Markdown instruction file the findings read: its paragraphs and its links.</summary>
internal static partial class MarkdownText
{
    /// <summary>
    /// Each paragraph, meaning a run of lines between blank lines, with its whitespace collapsed, and the
    /// line it starts on. Frontmatter at the top isn't a paragraph.
    /// </summary>
    public static IEnumerable<(string Text, int Line)> Paragraphs(string text)
    {
        var (lines, first) = Body(text);
        var start = -1;
        for (var i = first; i <= lines.Length; i++)
        {
            var blank = i == lines.Length || string.IsNullOrWhiteSpace(lines[i]);
            if (!blank && start < 0)
            {
                start = i;
            }
            else if (blank && start >= 0)
            {
                yield return (Whitespace().Replace(string.Join(' ', lines[start..i]), " ").Trim(), start + 1);
                start = -1;
            }
        }
    }

    /// <summary>
    /// Each link's target as written, inline or in a reference definition, with its line. Code spans and
    /// fenced blocks hold no links.
    /// </summary>
    public static IEnumerable<(string Target, int Line)> Links(string text)
    {
        var (lines, first) = Body(text);
        string? fence = null;
        for (var i = first; i < lines.Length; i++)
        {
            var opener = Fence().Match(lines[i]);
            if (opener.Success && (fence is null || opener.Groups[1].Value.StartsWith(fence, StringComparison.Ordinal)))
            {
                fence = fence is null ? opener.Groups[1].Value : null;
                continue;
            }

            if (fence is not null)
            {
                continue;
            }

            var line = CodeSpan().Replace(lines[i], " ");
            foreach (Match link in InlineLink().Matches(line))
            {
                yield return (link.Groups[1].Value, i + 1);
            }

            if (Definition().Match(line) is { Success: true } definition)
            {
                yield return (definition.Groups[1].Value, i + 1);
            }
        }
    }

    // The lines, and the index of the first one after any frontmatter.
    private static (string[] Lines, int First) Body(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var (_, body) = Frontmatter.Split(text);
        var skipped = text[..(text.Length - body.Length)].Count(character => character == '\n');
        return (lines, skipped);
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\s{0,3}(`{3,}|~{3,})", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();

    [GeneratedRegex(@"(`+).+?\1", RegexOptions.CultureInvariant)]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"!?\[[^\]]*\]\(\s*(<[^>]*>|[^)\s]+)[^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex InlineLink();

    [GeneratedRegex(@"^\s{0,3}\[[^\]]+\]:\s*(<[^>]*>|\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex Definition();
}
