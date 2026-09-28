using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axiomarium.Core.Manifests;

namespace Axiomarium.Core.Instructions;

/// <summary>What a rule file's frontmatter says about when it loads.</summary>
/// <param name="Valid">Whether the frontmatter parses. A rule whose frontmatter doesn't parse never loads.</param>
/// <param name="Paths">The rule's <c>paths</c> patterns, or <see langword="null"/> when it has none and always loads.</param>
internal sealed record RuleFrontmatter(bool Valid, IReadOnlyList<string>? Paths);

/// <summary>What a <c>SKILL.md</c> or command file's frontmatter says about how it's listed.</summary>
/// <param name="Valid">Whether the frontmatter parses. One that doesn't counts as no fields at all.</param>
/// <param name="Name">The <c>name</c> field.</param>
/// <param name="Description">The <c>description</c> field.</param>
/// <param name="WhenToUse">The <c>when_to_use</c> field.</param>
/// <param name="Paths">The <c>paths</c> patterns, or <see langword="null"/> when there are none.</param>
/// <param name="ModelInvocationOff">Whether <c>disable-model-invocation</c> is on.</param>
/// <param name="FirstBodyLine">The first line of the body that isn't blank, which stands in for a missing description.</param>
internal sealed record SkillFrontmatter(
    bool Valid, string? Name, string? Description, string? WhenToUse, IReadOnlyList<string>? Paths, bool ModelInvocationOff, string? FirstBodyLine);

/// <summary>The YAML frontmatter at the top of a Claude Code rule file, or of a skill.</summary>
internal static partial class Frontmatter
{
    /// <summary>Reads a skill's frontmatter. A boolean also accepts <c>yes</c>, <c>on</c> and <c>1</c>, as Claude Code's does.</summary>
    public static SkillFrontmatter ReadSkill(string content)
    {
        var (yaml, body) = Split(content);
        var firstLine = body.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
        var (valid, root) = string.IsNullOrWhiteSpace(yaml) ? (true, null) : Parse(yaml);
        if (!valid || root is null)
        {
            return new SkillFrontmatter(valid, null, null, null, null, false, firstLine);
        }

        string? Text(string field) => root[field] is JsonValue value && value.ToString().Trim() is { Length: > 0 } text ? text : null;
        var off = Text("disable-model-invocation")?.ToLowerInvariant() is "true" or "yes" or "on" or "1";
        return new SkillFrontmatter(true, Text("name"), Text("description"), Text("when_to_use"), PathsIn(root), off, firstLine);
    }

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

        var (valid, root) = Parse(yaml);
        return new RuleFrontmatter(valid, valid ? PathsIn(root) : null);
    }

    // When the YAML doesn't parse, Claude Code 2.1.284 tries once more: it quotes each top-level value that
    // holds a character YAML would read as syntax, and turns leading tabs into spaces. Only if that fails
    // too is the frontmatter broken.
    private static (bool Valid, JsonObject? Root) Parse(string yaml)
    {
        var parsed = YamlDocument.Parse(yaml);
        if (parsed.Problem is not null)
        {
            parsed = YamlDocument.Parse(LeadingTabs().Replace(QuoteLossyValues(yaml), tabs => new string(' ', tabs.Length * 2)));
        }

        return parsed.Problem is null ? (true, parsed.Root as JsonObject) : (false, null);
    }

    // A value already quoted, or a flow list that parses, stays as it is.
    private static string QuoteLossyValues(string yaml) => string.Join('\n', yaml.Split('\n').Select(line =>
    {
        var match = KeyValue().Match(line);
        if (!match.Success)
        {
            return line;
        }

        var (key, value) = (match.Groups[1].Value, match.Groups[2].Value);
        var quoted = value.Length > 1 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''));
        var list = value.StartsWith('[') && value.EndsWith(']') && YamlDocument.Parse(value) is { Problem: null, Root: JsonArray };
        return quoted || list || !Lossy().IsMatch(value)
            ? line
            : $"{key}: \"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }));

    [GeneratedRegex(@"^([a-zA-Z_-]+):\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"[{}[\]*&#!|>%@`]|: ", RegexOptions.CultureInvariant)]
    private static partial Regex Lossy();

    // paths is a YAML list or a comma-separated string.
    private static IReadOnlyList<string>? PathsIn(JsonObject? root) => root?["paths"] switch
    {
        JsonArray list => [.. list.Select(item => item?.ToString()).OfType<string>()],
        JsonValue value => [.. value.ToString().Split(',').Select(pattern => pattern.Trim()).Where(pattern => pattern.Length > 0)],
        _ => null,
    };

    [GeneratedRegex(@"\A---\r?\n(.*?)\r?\n?---(?:\r?\n|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Block();

    [GeneratedRegex(@"(?<=^|\n)\t+", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTabs();
}
