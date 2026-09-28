using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axiomarium.Core.Paths;

namespace Axiomarium.Core.Instructions;

/// <summary>Claude Code's Project instructions setting: which of CLAUDE.md and AGENTS.md load.</summary>
internal enum InstructionFiles
{
    /// <summary><c>claude-md-or-agents-md</c>, the default: AGENTS.md only when no CLAUDE file is in the launch directory or above.</summary>
    ClaudeMdOrAgentsMd,

    /// <summary><c>claude-md-and-agents-md</c>: both, each directory's AGENTS.md after its CLAUDE files.</summary>
    ClaudeMdAndAgentsMd,

    /// <summary><c>claude-md</c>: never AGENTS.md.</summary>
    ClaudeMd,

    /// <summary><c>managed-only</c>: only the managed file at launch; files below the launch directory and path rules still load on read.</summary>
    ManagedOnly,
}

/// <summary>The Claude Code settings that decide which instruction files load.</summary>
/// <param name="Mode">The Project instructions setting, read from managed and user settings only.</param>
/// <param name="Excludes"><c>claudeMdExcludes</c> from every layer, matched against absolute paths.</param>
internal sealed record ClaudeSettings(InstructionFiles Mode, IReadOnlyList<Glob> Excludes)
{
    /// <summary><c>skillOverrides</c> merged across the layers: each skill's state, such as <c>off</c> or <c>name-only</c>.</summary>
    public IReadOnlyDictionary<string, string> SkillOverrides { get; init; } = new Dictionary<string, string>();

    /// <summary>Whether <c>disableBundledSkills</c> turns the built-in skills off.</summary>
    public bool BuiltInsOff { get; init; }

    /// <summary>Whether <c>syncClaudeAiSkills</c> is false, which stops loading the skills synced from claude.ai.</summary>
    public bool SyncOff { get; init; }

    /// <summary><c>skillListingBudgetFraction</c>: the share of the context window the skill listing gets.</summary>
    public double ListingBudgetFraction { get; init; } = 0.01;

    /// <summary><c>skillListingMaxDescChars</c>: where one entry's text is cut.</summary>
    public int ListingMaxDescChars { get; init; } = 1536;

    /// <summary>The plugins <c>enabledPlugins</c> turns on, merged across the layers, by id such as <c>tools@market</c>, in id order.</summary>
    public IReadOnlyList<string> EnabledPlugins { get; init; } = [];

    public static ClaudeSettings Load(Machine machine, string launch)
    {
        var managed = Read(Path.Combine(machine.ClaudeManaged, "managed-settings.json"));
        var user = Read(Path.Combine(machine.ClaudeConfig, "settings.json"));
        var project = Read(Path.Combine(launch, ".claude", "settings.json"));
        var local = Read(Path.Combine(launch, ".claude", "settings.local.json"));

        // Claude Code ignores the mode in project and local settings.
        var mode = ModeIn(managed) ?? ModeIn(user) ?? InstructionFiles.ClaudeMdOrAgentsMd;
        var excludes = new[] { managed, user, project, local }
            .SelectMany(layer => layer?["claudeMdExcludes"] as JsonArray ?? [])
            .Select(pattern => pattern?.GetValueKind() == JsonValueKind.String ? pattern.GetValue<string>() : null)
            .OfType<string>()
            .Select(pattern => Glob.TryParse(pattern.Replace('\\', '/'), out var glob, out _) ? glob : null)
            .OfType<Glob>()
            .ToList();

        // Managed settings win, then local, then project, then user.
        JsonNode?[] byPrecedence = [managed, local, project, user];
        JsonValue? First(string key, JsonValueKind kind) =>
            byPrecedence.Select(layer => layer?[key]).OfType<JsonValue>().FirstOrDefault(value => value.GetValueKind() == kind);
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        var plugins = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var layer in byPrecedence.Reverse())
        {
            foreach (var (name, state) in layer?["skillOverrides"] as JsonObject ?? [])
            {
                if (state is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                {
                    overrides[name] = value.GetValue<string>();
                }
            }

            foreach (var (id, enabled) in layer?["enabledPlugins"] as JsonObject ?? [])
            {
                if (enabled?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                {
                    plugins[id] = enabled.GetValueKind() == JsonValueKind.True;
                }
            }
        }

        return new ClaudeSettings(mode, excludes)
        {
            SkillOverrides = overrides,
            EnabledPlugins = [.. plugins.Where(plugin => plugin.Value).Select(plugin => plugin.Key).Order(StringComparer.Ordinal)],
            BuiltInsOff = byPrecedence
                .Select(layer => layer?["disableBundledSkills"]?.GetValueKind())
                .FirstOrDefault(kind => kind is JsonValueKind.True or JsonValueKind.False) == JsonValueKind.True,

            // A repository can't turn the sync off, so only user, local and managed settings count.
            SyncOff = new[] { managed, local, user }.Any(layer => layer?["syncClaudeAiSkills"]?.GetValueKind() == JsonValueKind.False),
            ListingBudgetFraction = First("skillListingBudgetFraction", JsonValueKind.Number) is { } fraction && fraction.TryGetValue<double>(out var share) ? share : 0.01,
            ListingMaxDescChars = First("skillListingMaxDescChars", JsonValueKind.Number) is { } cap && cap.TryGetValue<int>(out var chars) ? chars : 1536,
        };
    }

    public bool IsExcluded(string path)
    {
        var absolute = Path.GetFullPath(path).Replace('\\', '/');
        return Excludes.Any(glob => glob.IsMatch(absolute));
    }

    private static InstructionFiles? ModeIn(JsonNode? settings) =>
        settings?["pluginConfigs"]?["agents-md@builtin"]?["options"]?["instructionFiles"] is JsonValue value
        && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>() switch
            {
                "claude-md-or-agents-md" => InstructionFiles.ClaudeMdOrAgentsMd,
                "claude-md-and-agents-md" => InstructionFiles.ClaudeMdAndAgentsMd,
                "claude-md" => InstructionFiles.ClaudeMd,
                "managed-only" => InstructionFiles.ManagedOnly,
                _ => null,
            }
            : null;

    // A settings file that doesn't parse contributes nothing, and neither does a plugin record.
    internal static JsonNode? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Removes the block-level HTML comments Claude Code strips before the model sees a file.</summary>
internal static partial class HtmlComments
{
    /// <summary>
    /// A comment that starts a line removes every line up to the one that closes it, and the blank lines
    /// after it, as a Markdown parser's HTML block does. Inline comments and comments in code blocks stay.
    /// </summary>
    public static string Strip(string text)
    {
        var lines = text.Split('\n');
        var kept = new List<string>(lines.Length);
        string? fence = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var opener = Fence().Match(line);
            if (opener.Success && (fence is null || opener.Groups[1].Value.StartsWith(fence, StringComparison.Ordinal)))
            {
                fence = fence is null ? opener.Groups[1].Value : null;
            }
            else if (fence is null && Comment().IsMatch(line))
            {
                while (i < lines.Length && !lines[i].Contains("-->", StringComparison.Ordinal))
                {
                    i++;
                }

                while (i + 1 < lines.Length - 1 && string.IsNullOrWhiteSpace(lines[i + 1]))
                {
                    i++;
                }

                continue;
            }

            kept.Add(line);
        }

        return string.Join('\n', kept);
    }

    [GeneratedRegex(@"^\s{0,3}(`{3,}|~{3,})", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();

    [GeneratedRegex(@"^ {0,3}<!--", RegexOptions.CultureInvariant)]
    private static partial Regex Comment();
}
