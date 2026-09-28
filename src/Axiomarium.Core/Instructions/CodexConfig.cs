using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Axiomarium.Core.Instructions;

/// <summary>The settings in Codex's <c>config.toml</c> that decide which AGENTS.md files load.</summary>
/// <param name="MaxBytes">The byte budget project files share: <c>project_doc_max_bytes</c>, 32 KiB by default. 0 turns project files off.</param>
/// <param name="FallbackFilenames">Names tried after AGENTS.md in each directory: <c>project_doc_fallback_filenames</c>, with unsafe names left out.</param>
/// <param name="RootMarkers">What marks the project root: <c>project_root_markers</c>, <c>.git</c> by default. Empty means only the launch directory counts.</param>
/// <param name="UntrustedProjects">The directories whose <c>trust_level</c> is <c>untrusted</c>.</param>
internal sealed record CodexConfig(long MaxBytes, IReadOnlyList<string> FallbackFilenames, IReadOnlyList<string> RootMarkers, IReadOnlyList<string> UntrustedProjects)
{
    public const long DefaultMaxBytes = 32 * 1024;

    public static CodexConfig Default { get; } = new(DefaultMaxBytes, [], [".git"], []);

    /// <summary><c>[[skills.config]]</c> entries in order: a skill's <c>SKILL.md</c> path or its name, and whether it's enabled. The last one that matches wins.</summary>
    public IReadOnlyList<(string? Path, string? Name, bool Enabled)> SkillRules { get; init; } = [];

    /// <summary>Whether <c>skills.bundled.enabled</c> is false, which turns the bundled skills off.</summary>
    public bool BundledSkillsOff { get; init; }

    /// <summary>Whether <c>skills.include_instructions</c> is false, which leaves the skill listing out of the prompt.</summary>
    public bool SkillsListingOff { get; init; }

    /// <summary><c>skills.max_context_tokens</c>, which replaces the listing's budget, capped at 10,000 tokens.</summary>
    public long? SkillsMaxContextTokens { get; init; }

    public static CodexConfig Load(string codexHome)
    {
        var path = Path.Combine(codexHome, "config.toml");
        if (!File.Exists(path))
        {
            return Default;
        }

        var table = TomlSerializer.Deserialize(File.ReadAllText(path), CodexConfigContext.Default.TomlTable) ?? [];
        var untrusted = table.TryGetValue("projects", out var projects) && projects is TomlTable projectTable
            ? projectTable
                .Where(pair => pair.Value is TomlTable settings && settings.TryGetValue("trust_level", out var level) && level is "untrusted")
                .Select(pair => Path.GetFullPath(pair.Key))
                .ToList()
            : [];
        var skills = table.TryGetValue("skills", out var skillsValue) && skillsValue is TomlTable skillsTable ? skillsTable : [];
        var rules = skills.TryGetValue("config", out var configValue) && configValue is TomlTableArray entries
            ? entries
                .Select(entry => (
                    Path: entry.TryGetValue("path", out var path) && path is string text ? text : null,
                    Name: entry.TryGetValue("name", out var name) && name is string label ? label.Trim() : null,
                    Enabled: !entry.TryGetValue("enabled", out var enabled) || enabled is not false))
                .Where(rule => (rule.Path is null) != (rule.Name is null) && rule.Name is not "")
                .ToList()
            : [];
        return new CodexConfig(
            table.TryGetValue("project_doc_max_bytes", out var max) && max is long bytes ? bytes : DefaultMaxBytes,
            [.. Strings(table, "project_doc_fallback_filenames").Where(SafeFilename)],
            table.ContainsKey("project_root_markers") ? Strings(table, "project_root_markers") : [".git"],
            untrusted)
        {
            SkillRules = rules,
            BundledSkillsOff = skills.TryGetValue("bundled", out var bundled) && bundled is TomlTable bundledTable
                && bundledTable.TryGetValue("enabled", out var on) && on is false,
            SkillsListingOff = skills.TryGetValue("include_instructions", out var include) && include is false,
            SkillsMaxContextTokens = skills.TryGetValue("max_context_tokens", out var tokens) && tokens is long budget && budget > 0 ? budget : null,
        };
    }

    /// <summary>Whether <c>[[skills.config]]</c> turns the skill at <paramref name="skillFile"/>, listed as <paramref name="name"/>, off.</summary>
    public bool IsSkillDisabled(string skillFile, string name)
    {
        var enabled = true;
        foreach (var rule in SkillRules)
        {
            if (rule.Name == name || (rule.Path is { } path && Paths.Same(System.IO.Path.GetFullPath(path), skillFile)))
            {
                enabled = rule.Enabled;
            }
        }

        return !enabled;
    }

    public bool IsUntrusted(string directory) =>
        UntrustedProjects.Any(project => string.Equals(
            Path.TrimEndingDirectorySeparator(project),
            Path.TrimEndingDirectorySeparator(directory),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

    private static List<string> Strings(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is TomlArray array ? [.. array.OfType<string>()] : [];

    // Codex ignores a fallback name that could leave the directory.
    private static bool SafeFilename(string name) =>
        name.Length > 0
        && name is not "." and not ".."
        && !name.Contains('/')
        && !name.Contains('\0')
        && !(OperatingSystem.IsWindows() && (name.Contains('\\') || name.Contains(':')));
}

[TomlSerializable(typeof(TomlTable))]
internal sealed partial class CodexConfigContext : TomlSerializerContext;
