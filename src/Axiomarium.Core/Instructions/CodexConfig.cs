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

    /// <summary>The directories whose <c>trust_level</c> is <c>trusted</c>.</summary>
    public IReadOnlyList<string> TrustedProjects { get; init; } = [];

    /// <summary>
    /// <c>[hooks.state]</c> in the user config, by hook key exactly as written: whether each hook is enabled and the hash
    /// it's trusted at. Codex finds an entry only under the key it writes itself, with no change of case or slashes.
    /// </summary>
    public IReadOnlyDictionary<string, (bool? Enabled, string? TrustedHash)> HookStates { get; init; } = new Dictionary<string, (bool?, string?)>();

    /// <summary>The config file's own <c>[hooks]</c> events, or <see langword="null"/> when it has none.</summary>
    public TomlTable? Hooks { get; init; }

    /// <summary><c>model</c>: the model Codex uses unless told otherwise, or <see langword="null"/> for its default.</summary>
    public string? Model { get; init; }

    /// <summary><c>model_reasoning_effort</c>, or <see langword="null"/> for the model's default.</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary><c>windows.sandbox</c>: <c>elevated</c> or <c>unelevated</c>, or <see langword="null"/> when it isn't set.</summary>
    public string? WindowsSandbox { get; init; }

    public static CodexConfig Load(string codexHome)
    {
        var path = Path.Combine(codexHome, "config.toml");
        if (!File.Exists(path))
        {
            return Default;
        }

        var table = TomlSerializer.Deserialize(File.ReadAllText(path), CodexConfigContext.Default.TomlTable) ?? [];
        // Entries keep the path as written: Codex doesn't resolve or turn the slashes of an entry before it compares (see ProjectIs).
        var untrusted = table.TryGetValue("projects", out var projects) && projects is TomlTable projectTable
            ? projectTable
                .Where(pair => pair.Value is TomlTable settings && settings.TryGetValue("trust_level", out var level) && level is "untrusted")
                .Select(pair => pair.Key)
                .ToList()
            : [];
        var trusted = table.TryGetValue("projects", out var trustedProjects) && trustedProjects is TomlTable trustedTable
            ? trustedTable
                .Where(pair => pair.Value is TomlTable settings && settings.TryGetValue("trust_level", out var level) && level is "trusted")
                .Select(pair => pair.Key)
                .ToList()
            : [];
        var hooks = table.TryGetValue("hooks", out var hooksValue) && hooksValue is TomlTable hooksTable ? hooksTable : null;

        // Codex matches a hook's key exactly, case and slashes included, even on Windows.
        var states = new Dictionary<string, (bool?, string?)>(StringComparer.Ordinal);
        if (hooks?.TryGetValue("state", out var stateValue) == true && stateValue is TomlTable stateTable)
        {
            foreach (var (key, value) in stateTable)
            {
                if (value is TomlTable state)
                {
                    states[key] = (
                        state.TryGetValue("enabled", out var enabled) && enabled is bool flag ? flag : null,
                        state.TryGetValue("trusted_hash", out var hash) && hash is string text ? text : null);
                }
            }
        }

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
            TrustedProjects = trusted,
            HookStates = states,
            Hooks = hooks,
            Model = table.TryGetValue("model", out var model) && model is string modelName ? modelName : null,
            ReasoningEffort = table.TryGetValue("model_reasoning_effort", out var effort) && effort is string effortName ? effortName : null,
            WindowsSandbox = table.TryGetValue("windows", out var windows) && windows is TomlTable windowsTable
                && windowsTable.TryGetValue("sandbox", out var sandbox) && sandbox is string sandboxName ? sandboxName : null,
        };
    }

    /// <summary>Whether a project's <c>.codex</c> in <paramref name="directory"/> is trusted: that folder's own entry decides, else the project root's.</summary>
    public bool IsTrusted(string directory, string projectRoot)
    {
        foreach (var folder in new[] { directory, projectRoot })
        {
            if (TrustedProjects.Any(project => ProjectIs(project, folder)))
            {
                return true;
            }

            if (UntrustedProjects.Any(project => ProjectIs(project, folder)))
            {
                return false;
            }
        }

        return false;
    }

    // Codex looks a folder up under its canonical path and under the path as given, and compares each [projects] entry
    // exactly, lowercased on Windows, without turning slashes or trimming separators (normalized_project_trust_keys in
    // codex-rs/config, 0.156.1). hooks/list on Windows agreed: it ignored an entry with forward slashes or a trailing
    // backslash, and took one in another case (2026-10-04).
    private static bool ProjectIs(string entry, string folder)
    {
        var given = Path.TrimEndingDirectorySeparator(folder);
        return string.Equals(TrustKey(entry), TrustKey(given), StringComparison.Ordinal)
            || string.Equals(TrustKey(entry), TrustKey(Paths.Canonical(given)), StringComparison.Ordinal);
    }

    // Codex lowercases ASCII letters only, so a non-ASCII letter's case still counts.
    private static string TrustKey(string path) =>
        OperatingSystem.IsWindows() ? string.Concat(path.Select(character => character is >= 'A' and <= 'Z' ? (char)(character + 32) : character)) : path;

    /// <summary>Reads another layer's <c>config.toml</c>, such as a project's or the system folder's, for its <c>[hooks]</c> alone.</summary>
    public static TomlTable? HooksIn(string configFile)
    {
        if (!File.Exists(configFile))
        {
            return null;
        }

        try
        {
            var table = TomlSerializer.Deserialize(File.ReadAllText(configFile), CodexConfigContext.Default.TomlTable);
            return table?.TryGetValue("hooks", out var hooks) == true ? hooks as TomlTable : null;
        }
        catch (TomlException)
        {
            return null;
        }
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

    public bool IsUntrusted(string directory) => UntrustedProjects.Any(project => ProjectIs(project, directory));

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
