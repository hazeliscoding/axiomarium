using System.Text.Json.Nodes;

namespace Axiomarium.Core.Instructions;

/// <summary>Which skills Claude Code lists for the model, at launch and when the agent reads or edits a file.</summary>
/// <remarks>
/// Follows the Claude Code skills docs as read on 2026-09-28, confirmed against Claude Code
/// <see cref="ClaudeCodeModel.ConfirmedWith"/> by the recordings in <c>scenarios/</c>. Where they disagree the
/// model follows the recordings: the listing names a skill by its folder, not its frontmatter <c>name</c>.
/// </remarks>
internal static class ClaudeCodeSkills
{
    // The listing's budget scales with the model's context window, which no file says, so it assumes the
    // common one and says so.
    private const int AssumedWindow = 200_000;
    private const string Assumption = "a 200k-token context window";

    /// <summary>Claude Code's built-in skills in listing order, with the length of each entry, as recorded on 2.1.284.</summary>
    public static IReadOnlyList<(string Name, int Chars)> BuiltIns { get; } =
    [
        ("dataviz", 1447), ("update-config", 707), ("keybindings-help", 248), ("code-review", 853), ("simplify", 188),
        ("fewer-permission-prompts", 191), ("loop", 344), ("schedule", 381), ("claude-api", 1082), ("workflow-authoring", 252),
        ("run", 368), ("init", 67), ("security-review", 90),
    ];

    /// <summary>The skills Claude Code lists when launched in <paramref name="launch"/> and the agent reads or edits <paramref name="target"/>.</summary>
    /// <returns>The listed skills in listing order, the ones kept out, and the listing's size at launch against its budget.</returns>
    public static (IReadOnlyList<AvailableSkill> Skills, IReadOnlyList<UnlistedSkill> NotListed, SkillListing Listing) Resolve(
        string launch, string target, Machine machine, ClaudeSettings settings)
    {
        var listing = new Listing(launch, target, settings);
        var projects = ProjectDirectories(launch, machine).ToList();

        // At launch, in the order the recordings show: managed, personal, project skills from the launch
        // directory up, commands, then the built-in skills.
        foreach (var file in SkillFiles(Path.Combine(machine.ClaudeManaged, ".claude", "skills")))
        {
            listing.Consider(Folder(file), file, ClaudeCodeSkillRules.ManagedSkill);
        }

        foreach (var file in SkillFiles(Path.Combine(machine.ClaudeConfig, "skills")))
        {
            listing.Consider(Folder(file), file, ClaudeCodeSkillRules.PersonalSkill);
        }

        foreach (var file in projects.SelectMany(directory => SkillFiles(Path.Combine(directory, ".claude", "skills"))))
        {
            listing.Consider(Folder(file), file, ClaudeCodeSkillRules.ProjectSkill);
        }

        foreach (var folder in projects.Select(directory => Path.Combine(directory, ".claude", "commands")).Prepend(Path.Combine(machine.ClaudeConfig, "commands")))
        {
            foreach (var (name, file) in Commands(folder))
            {
                listing.Consider(name, file, ClaudeCodeSkillRules.Command, command: true);
            }
        }

        foreach (var (plugin, root) in Plugins(machine, settings))
        {
            foreach (var file in PluginSkillFiles(root))
            {
                var front = Frontmatter.ReadSkill(File.ReadAllText(file));
                var last = front.Name ?? Folder(file);
                var name = last.StartsWith(plugin + ":", StringComparison.Ordinal) ? last : $"{plugin}:{last}";
                listing.Consider(name, file, ClaudeCodeSkillRules.PluginSkill, front: front, plugin: true);
            }

            foreach (var (name, file) in PluginCommandFolders(root).SelectMany(Commands))
            {
                listing.Consider($"{plugin}:{name}", file, ClaudeCodeSkillRules.PluginSkill, command: true, plugin: true);
            }
        }

        listing.BuiltIns();

        var synced = Path.Combine(machine.ClaudeConfig, "skills", "synced");
        if (!settings.SyncOff && Directory.Exists(synced))
        {
            foreach (var account in Directory.EnumerateDirectories(synced).Where(folder => !Path.GetFileName(folder).StartsWith('.')).Order(StringComparer.Ordinal))
            {
                foreach (var file in SkillFiles(account))
                {
                    listing.Consider($"anthropic-skills:{Folder(file)}", file, ClaudeCodeSkillRules.SyncedSkill);
                }
            }
        }

        // On read: the skills of each folder between the launch directory and the file.
        foreach (var directory in Paths.Below(launch, target))
        {
            var from = Path.GetRelativePath(launch, directory).Replace('\\', '/');
            foreach (var file in SkillFiles(Path.Combine(directory, ".claude", "skills")))
            {
                listing.Consider(Folder(file), file, ClaudeCodeSkillRules.NestedSkill, nestedFrom: from);
            }
        }

        return listing.Result();
    }

    // Project skills load from the launch directory up to the repository root, and never from the home
    // folder, whose .claude/skills holds the personal ones.
    private static IEnumerable<string> ProjectDirectories(string launch, Machine machine)
    {
        foreach (var directory in Paths.Upward(launch, machine.FileSystemRoot))
        {
            if (!Paths.Same(directory, launch) && Paths.Same(directory, machine.Home))
            {
                yield break;
            }

            yield return directory;
            if (Path.Exists(Path.Combine(directory, ".git")))
            {
                yield break;
            }
        }
    }

    // Each enabled plugin that's on disk, by its name and folder.
    private static IEnumerable<(string Plugin, string Root)> Plugins(Machine machine, ClaudeSettings settings)
    {
        var folder = Path.Combine(machine.ClaudeConfig, "plugins");
        var installed = ClaudeSettings.Read(Path.Combine(folder, "installed_plugins.json"))?["plugins"] as JsonObject;
        var marketplaces = ClaudeSettings.Read(Path.Combine(folder, "known_marketplaces.json")) as JsonObject;
        foreach (var id in settings.EnabledPlugins)
        {
            var at = id.LastIndexOf('@');
            if (at <= 0)
            {
                continue;
            }

            var plugin = id[..at];
            var root = InPlace(marketplaces?[id[(at + 1)..]], plugin)
                ?? (installed?[id] as JsonArray ?? []).Select(install => Text(install?["installPath"])).FirstOrDefault(Directory.Exists);
            if (root is not null)
            {
                yield return (plugin, root);
            }
        }
    }

    // A plugin with a relative source, in a marketplace added from a local folder, loads from that folder.
    private static string? InPlace(JsonNode? marketplace, string plugin)
    {
        if (marketplace?["source"] is not JsonObject source || Text(source["source"]) != "directory" || Text(source["path"]) is not { } folder)
        {
            return null;
        }

        var entry = (ClaudeSettings.Read(Path.Combine(folder, ".claude-plugin", "marketplace.json"))?["plugins"] as JsonArray ?? [])
            .FirstOrDefault(item => Text(item?["name"]) == plugin);
        return Text(entry?["source"]) is { } relative && Directory.Exists(Path.Combine(folder, relative)) ? Path.GetFullPath(Path.Combine(folder, relative)) : null;
    }

    // A plugin's skills/ folder, and the folders its manifest adds.
    private static IEnumerable<string> PluginSkillFiles(string root) =>
        ManifestPaths(root, "skills")
            .Prepend(Path.Combine(root, "skills"))
            .SelectMany(folder => File.Exists(Path.Combine(folder, "SKILL.md")) ? [Path.Combine(folder, "SKILL.md")] : SkillFiles(folder));

    // A manifest's commands replace the plugin's commands/ folder.
    private static IEnumerable<string> PluginCommandFolders(string root) =>
        ManifestPaths(root, "commands") is { Count: > 0 } folders ? folders : [Path.Combine(root, "commands")];

    // A manifest path is one string or a list of them, relative to the plugin's folder.
    private static List<string> ManifestPaths(string root, string key)
    {
        var value = ClaudeSettings.Read(Path.Combine(root, ".claude-plugin", "plugin.json"))?[key];
        IEnumerable<string?> paths = value is JsonArray list ? list.Select(Text) : [Text(value)];
        return [.. paths.OfType<string>().Select(path => Path.GetFullPath(Path.Combine(root, path)))];
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    // Claude Code keeps synced skills in a folder named synced, whatever its case, and skips hidden ones.
    private static IEnumerable<string> SkillFiles(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder)
                .Where(directory => Path.GetFileName(directory) is var name && !name.StartsWith('.') && !name.Equals("synced", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .Select(directory => Path.Combine(directory, "SKILL.md"))
                .Where(File.Exists)
            : [];

    private static string Folder(string skillFile) => Path.GetFileName(Path.GetDirectoryName(skillFile)!);

    // A command's name is its path under commands/, with a colon for each folder.
    private static IEnumerable<(string Name, string File)> Commands(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories)
                .Select(file => (Path.ChangeExtension(Path.GetRelativePath(folder, file), null).Replace('\\', '/').Replace('/', ':'), file))
                .OrderBy(command => command.Item1, StringComparer.Ordinal)
            : [];

    // What has been listed so far. The first skill to take a name keeps it.
    private sealed class Listing(string launch, string target, ClaudeSettings settings)
    {
        private readonly List<AvailableSkill> _atLaunch = [];
        private readonly List<AvailableSkill> _nested = [];
        private readonly List<AvailableSkill> _byPaths = [];
        private readonly List<UnlistedSkill> _notListed = [];
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private readonly HashSet<string> _files = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public void Consider(
            string name, string file, HarnessRule rule, bool command = false, string? nestedFrom = null, SkillFrontmatter? front = null, bool plugin = false)
        {
            if (!_files.Add(Path.GetFullPath(file)))
            {
                return;
            }

            // A nested skill whose name is taken stays, under its folder's name.
            string? unscoped = null;
            if (!_names.Add(name))
            {
                if (nestedFrom is null)
                {
                    _notListed.Add(new UnlistedSkill(name, file, ClaudeCodeSkillRules.Shadowed));
                    return;
                }

                (unscoped, name) = (name, $"{nestedFrom}:{name}");
                _names.Add(name);
            }

            // skillOverrides never applies to a plugin's skills, which aren't listed without a description.
            front ??= Frontmatter.ReadSkill(File.ReadAllText(file));
            var state = plugin ? null : settings.SkillOverrides.GetValueOrDefault(name);
            var paths = command ? null : front.Paths;
            var hidden = front.ModelInvocationOff ? ClaudeCodeSkillRules.ModelInvocationOff
                : state is "off" or "user-invocable-only" ? ClaudeCodeSkillRules.Override
                : plugin && front.Description is null && front.WhenToUse is null ? ClaudeCodeSkillRules.PluginNoDescription
                : paths is not null && !PathPatterns.MatchLikeGitignore(paths, launch, target) ? ClaudeCodeSkillRules.PathsSkillNoMatch
                : null;
            if (hidden is not null)
            {
                _notListed.Add(new UnlistedSkill(name, file, hidden));
                return;
            }

            var text = front.Description ?? front.FirstBodyLine ?? "";
            if (front.WhenToUse is { } whenToUse)
            {
                text = text.Length > 0 ? $"{text} - {whenToUse}" : whenToUse;
            }

            // The recordings show how a nested skill says where it applies, and how one that shares its name
            // with another says to use it instead.
            if (unscoped is not null)
            {
                text += $" (scoped to {nestedFrom}/ — use this instead of the unscoped \"{unscoped}\" skill when the files being changed are under {nestedFrom}/)";
            }
            else if (nestedFrom is not null)
            {
                text += $" (from {nestedFrom}/.claude/skills — applies when working on files under {nestedFrom}/)";
            }

            var nameOnly = state == "name-only";
            var (chars, cut) = Entry(name, text, nameOnly);
            var timing = paths is not null || nestedFrom is not null ? LoadTiming.OnRead : LoadTiming.AtLaunch;
            var skill = new AvailableSkill(name, file, timing, paths is not null ? ClaudeCodeSkillRules.PathsSkill : rule, chars, cut, nameOnly, paths);
            (paths is not null ? _byPaths : nestedFrom is not null ? _nested : _atLaunch).Add(skill);
        }

        public void BuiltIns()
        {
            foreach (var (name, chars) in ClaudeCodeSkills.BuiltIns)
            {
                var state = settings.SkillOverrides.GetValueOrDefault(name);
                var hidden = settings.BuiltInsOff ? ClaudeCodeSkillRules.BuiltInsOff
                    : !_names.Add(name) ? ClaudeCodeSkillRules.Shadowed
                    : state is "off" or "user-invocable-only" ? ClaudeCodeSkillRules.Override
                    : null;
                if (hidden is not null)
                {
                    _notListed.Add(new UnlistedSkill(name, null, hidden));
                    continue;
                }

                var nameOnly = state == "name-only";
                _atLaunch.Add(new AvailableSkill(name, null, LoadTiming.AtLaunch, ClaudeCodeSkillRules.BuiltIn, nameOnly ? $"- {name}".Length : chars, NameOnly: nameOnly));
            }
        }

        // The listing at launch joins its entries with newlines.
        public (IReadOnlyList<AvailableSkill>, IReadOnlyList<UnlistedSkill>, SkillListing) Result()
        {
            var chars = _atLaunch.Sum(skill => skill.Chars) + Math.Max(0, _atLaunch.Count - 1);
            var budget = (int)Math.Round(AssumedWindow * 4 * settings.ListingBudgetFraction);
            return ([.. _atLaunch, .. _nested, .. _byPaths], _notListed, new SkillListing(chars, budget, Assumption, ClaudeCodeSkillRules.ListingBudget));
        }

        // An entry is "- name: text", or "- name" alone. Text past the cap ends in an ellipsis at the cap.
        private (int Chars, bool Cut) Entry(string name, string text, bool nameOnly)
        {
            if (nameOnly || text.Length == 0)
            {
                return ($"- {name}".Length, false);
            }

            var cap = settings.ListingMaxDescChars;
            return text.Length > cap ? ($"- {name}: ".Length + cap, true) : ($"- {name}: {text}".Length, false);
        }
    }
}
