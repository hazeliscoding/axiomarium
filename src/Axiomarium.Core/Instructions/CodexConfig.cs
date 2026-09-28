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
        return new CodexConfig(
            table.TryGetValue("project_doc_max_bytes", out var max) && max is long bytes ? bytes : DefaultMaxBytes,
            [.. Strings(table, "project_doc_fallback_filenames").Where(SafeFilename)],
            table.ContainsKey("project_root_markers") ? Strings(table, "project_root_markers") : [".git"],
            untrusted);
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
