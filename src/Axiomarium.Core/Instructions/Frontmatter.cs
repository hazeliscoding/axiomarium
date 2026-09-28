using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axiomarium.Core.Manifests;

namespace Axiomarium.Core.Instructions;

/// <summary>What a rule file's frontmatter says about when it loads.</summary>
/// <param name="Valid">Whether the frontmatter parses. A rule whose frontmatter doesn't parse never loads.</param>
/// <param name="Paths">The rule's <c>paths</c> patterns, or <see langword="null"/> when it has none and always loads.</param>
internal sealed record RuleFrontmatter(bool Valid, IReadOnlyList<string>? Paths);

/// <summary>The YAML frontmatter at the top of a Claude Code rule file.</summary>
internal static partial class Frontmatter
{
    /// <summary>The frontmatter's YAML, or <see langword="null"/> when there is none, and the text after it.</summary>
    public static (string? Yaml, string Body) Split(string content)
    {
        var match = Block().Match(content);
        return match.Success ? (match.Groups[1].Value, content[match.Length..]) : (null, content);
    }

    /// <summary>Reads the rule's frontmatter. <c>paths</c> is a YAML list or a comma-separated string.</summary>
    public static RuleFrontmatter Read(string content)
    {
        var (yaml, _) = Split(content);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new RuleFrontmatter(true, null);
        }

        // Claude Code's YAML reader accepts tab indentation, which the core's doesn't, so try once more
        // with tabs turned into spaces before calling the frontmatter broken.
        var parsed = YamlDocument.Parse(yaml);
        if (parsed.Problem is not null)
        {
            parsed = YamlDocument.Parse(LeadingTabs().Replace(yaml, tabs => new string(' ', tabs.Length * 2)));
        }

        if (parsed.Problem is not null)
        {
            return new RuleFrontmatter(false, null);
        }

        IReadOnlyList<string>? paths = (parsed.Root as JsonObject)?["paths"] switch
        {
            JsonArray list => [.. list.Select(item => item?.ToString()).OfType<string>()],
            JsonValue value => [.. value.ToString().Split(',').Select(pattern => pattern.Trim()).Where(pattern => pattern.Length > 0)],
            _ => null,
        };
        return new RuleFrontmatter(true, paths);
    }

    [GeneratedRegex(@"\A---\r?\n(.*?)\r?\n?---(?:\r?\n|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Block();

    [GeneratedRegex(@"(?<=^|\n)\t+", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTabs();
}
