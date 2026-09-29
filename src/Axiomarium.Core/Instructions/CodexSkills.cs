using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axiomarium.Core.Manifests;

namespace Axiomarium.Core.Instructions;

/// <summary>Which skills Codex lists for the model. Codex finds them all at launch.</summary>
/// <remarks>
/// Follows the Codex skills guide and <c>codex-rs/ext/skills</c> as read at 0.156.1 on 2026-09-28, confirmed
/// against Codex <see cref="CodexModel.ConfirmedWith"/> by the recordings in <c>scenarios/</c>. Where the guide
/// and the code disagree, the model follows the code.
/// </remarks>
internal static class CodexSkills
{
    private const int MaxNameChars = 64;
    private const int MaxDescriptionChars = 1024;
    private const int MaxDepth = 6;

    // The listing's budget is 2% of the model's context window, which no file says, so it assumes the window
    // of Codex's default models and says so.
    private const int AssumedWindow = 272_000;

    private static readonly string[] PluginManifests = [".codex-plugin/plugin.json", ".claude-plugin/plugin.json", ".cursor-plugin/plugin.json"];

    private static readonly string[] Products = ["chatgpt", "codex", "atlas"];

    private enum Scope
    {
        System,
        Admin,
        Repo,
        User,
    }

    /// <summary>The skills Codex lists when launched in <paramref name="launch"/>.</summary>
    /// <returns>The listed skills in listing order, the ones kept out, and the listing's size against its budget.</returns>
    public static (IReadOnlyList<AvailableSkill> Skills, IReadOnlyList<UnlistedSkill> NotListed, SkillListing Listing) Resolve(
        string launch, Machine machine, CodexConfig config)
    {
        var root = CodexModel.ProjectRoot(launch, config.RootMarkers, machine.FileSystemRoot) ?? launch;
        var chain = CodexModel.Chain(root, launch);

        // The roots in the order Codex finds them, which numbers their aliases: each project .codex from the
        // launch directory up, the user folders, the bundled and admin skills, then each .agents/skills.
        var roots = new List<(string Folder, Scope Scope, HarnessRule Rule)>();
        roots.AddRange(Enumerable.Reverse(chain)
            .Select(directory => Path.Combine(directory, ".codex"))
            .Where(folder => !Paths.Same(folder, machine.CodexHome))
            .Select(folder => (Path.Combine(folder, "skills"), Scope.Repo, CodexSkillRules.ProjectSkill)));
        roots.Add((Path.Combine(machine.CodexHome, "skills"), Scope.User, CodexSkillRules.CodexHomeSkill));
        roots.Add((Path.Combine(machine.Home, ".agents", "skills"), Scope.User, CodexSkillRules.UserSkill));
        roots.Add((Path.Combine(machine.CodexHome, "skills", ".system"), Scope.System, CodexSkillRules.BundledSkill));
        roots.Add((Path.Combine(machine.CodexAdmin, "skills"), Scope.Admin, CodexSkillRules.AdminSkill));
        roots.AddRange(chain.Select(directory => (Path.Combine(directory, ".agents", "skills"), Scope.Repo, CodexSkillRules.RepoSkill)));

        var listed = new List<(string Name, string File, string Description, Scope Scope, HarnessRule Rule, int Root)>();
        var notListed = new List<UnlistedSkill>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var namespaces = new Dictionary<string, string?>(seen.Comparer);
        for (var index = 0; index < roots.Count; index++)
        {
            var (folder, scope, rule) = roots[index];
            if (!seen.Add(Path.GetFullPath(folder)))
            {
                continue;
            }

            foreach (var file in SkillFiles(folder))
            {
                var directory = Path.GetDirectoryName(file)!;
                var (problem, name, description) = ReadSkill(File.ReadAllText(file), Path.GetFileName(directory));
                if (problem is not null)
                {
                    notListed.Add(new UnlistedSkill(name, file, CodexSkillRules.Invalid, problem));
                    continue;
                }

                if (Namespace(folder, machine.FileSystemRoot, namespaces) is { } plugin)
                {
                    name = $"{plugin}:{name}";
                }

                var (implicitOff, otherProduct) = Policy(Path.Combine(directory, "agents", "openai.yaml"));
                var hidden = otherProduct ? CodexSkillRules.OtherProduct
                    : scope == Scope.System && config.BundledSkillsOff ? CodexSkillRules.BundledOff
                    : config.IsSkillDisabled(file, name) ? CodexSkillRules.Disabled
                    : implicitOff ? CodexSkillRules.ImplicitOff
                    : config.SkillsListingOff ? CodexSkillRules.ListingOff
                    : null;
                if (hidden is not null)
                {
                    notListed.Add(new UnlistedSkill(name, file, hidden));
                    continue;
                }

                listed.Add((name, file, description!, scope, rule, index));
            }
        }

        // Bundled first, then admin, repo and user skills, each by name, then by path.
        listed = [.. listed.OrderBy(skill => skill.Scope).ThenBy(skill => skill.Name, StringComparer.Ordinal).ThenBy(skill => skill.File, StringComparer.Ordinal)];

        // Aliases number only the roots that hold a listed skill.
        var aliases = listed.Select(skill => skill.Root).Distinct().Order().Select((root, alias) => (root, alias)).ToDictionary(pair => pair.root, pair => pair.alias);
        var skills = new List<AvailableSkill>();
        long aliasedCost = aliases.Keys.Sum(root => Tokens($"- `r{aliases[root]}` = `{Slashes(roots[root].Folder)}`"));
        long absoluteCost = 0;
        foreach (var skill in listed)
        {
            var cut = skill.Description.Length > MaxDescriptionChars;
            var description = cut ? skill.Description[..(MaxDescriptionChars - 3)] + "..." : skill.Description;
            var relative = Slashes(Path.GetRelativePath(roots[skill.Root].Folder, skill.File));
            var aliased = $"- {skill.Name}: {description} (file: r{aliases[skill.Root]}/{relative})";
            aliasedCost += Tokens(aliased);
            absoluteCost += Tokens($"- {skill.Name}: {description} (file: {Slashes(skill.File)})");
            skills.Add(new AvailableSkill(skill.Name, skill.File, LoadTiming.AtLaunch, skill.Rule, aliased.Length, cut));
        }

        var (budget, assumption) = config.SkillsMaxContextTokens is { } tokens
            ? ((int)Math.Min(tokens, 10_000), "skills.max_context_tokens")
            : (AssumedWindow * 2 / 100, "2% of a 272k-token context window, that of Codex's default models");
        var size = (int)Math.Min(aliasedCost, absoluteCost);
        return (skills, notListed, new SkillListing(size, budget, "tokens", assumption, CodexSkillRules.ListingBudget));
    }

    // Every SKILL.md up to six levels below the root, outside hidden folders, in path order.
    private static IEnumerable<string> SkillFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var files = new List<string>();
        var pending = new Stack<(string Folder, int Depth)>([(root, 0)]);
        while (pending.TryPop(out var current))
        {
            var skill = Path.Combine(current.Folder, "SKILL.md");
            if (current.Depth > 0 && File.Exists(skill))
            {
                files.Add(skill);
            }

            if (current.Depth + 1 >= MaxDepth)
            {
                continue;
            }

            foreach (var folder in Directory.EnumerateDirectories(current.Folder).Where(folder => !Path.GetFileName(folder).StartsWith('.')))
            {
                pending.Push((folder, current.Depth + 1));
            }
        }

        return files.Order(StringComparer.Ordinal);
    }

    // Codex reads name and description, folds each onto one line, and needs a description. The problem says
    // why it skips the skill, or is null when it doesn't.
    private static (string? Problem, string Name, string? Description) ReadSkill(string content, string folder)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (lines[0].Trim() != "---" || end <= 1)
        {
            return ("it has no frontmatter", folder, null);
        }

        var yaml = string.Join('\n', lines[1..end]);
        var parsed = YamlDocument.Parse(yaml);
        if (parsed.Problem is not null && Repair(yaml) is { } repaired)
        {
            parsed = YamlDocument.Parse(repaired);
        }

        if (parsed.Problem is not null || parsed.Root is not JsonObject root)
        {
            return ("its frontmatter doesn't parse", folder, null);
        }

        string? Text(string field) => root[field] is JsonValue value ? string.Join(' ', value.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) : null;
        var name = Text("name") is { Length: > 0 } given ? given : folder;
        var description = Text("description");
        var problem = description is not { Length: > 0 } ? "it has no description"
            : name.Length > MaxNameChars ? $"its name is over {MaxNameChars} characters"
            : null;
        return (problem, name, description);
    }

    // When the YAML doesn't parse, Codex single-quotes a value that holds ": ", or that starts with a bracket,
    // @ or a backtick and doesn't parse, and tries once more.
    private static string? Repair(string yaml)
    {
        var changed = false;
        int? blockIndent = null;
        var lines = new List<string>();
        foreach (var line in yaml.Split('\n'))
        {
            var indent = line.TakeWhile(character => character == ' ').Count();
            if (blockIndent is { } block && (line.Trim().Length == 0 || indent > block))
            {
                lines.Add(line);
                continue;
            }

            blockIndent = null;
            var colon = line.IndexOf(':');
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (colon < 0 || line[..colon].Trim().Length == 0 || (value.Length > 0 && !char.IsWhiteSpace(value[0])))
            {
                lines.Add(line);
                continue;
            }

            var start = value.TrimStart();
            var leading = value[..(value.Length - start.Length)];
            var comment = -1;
            for (var index = 0; index < start.Length && comment < 0; index++)
            {
                if (start[index] == '#' && (index == 0 || char.IsWhiteSpace(start[index - 1])))
                {
                    comment = index;
                }
            }

            var scalar = (comment < 0 ? start : start[..comment]).TrimEnd();
            var trailing = comment < 0 ? "" : start[scalar.Length..];
            if (scalar.Length == 0 || scalar[0] is '\'' or '"')
            {
                lines.Add(line);
                continue;
            }

            if (scalar[0] is '|' or '>')
            {
                blockIndent = indent;
                lines.Add(line);
                continue;
            }

            var colonInside = scalar.Zip(scalar.Skip(1)).Any(pair => pair.First == ':' && char.IsWhiteSpace(pair.Second));
            var brokenFlow = scalar[0] is '[' or '{' or '@' or '`' && YamlDocument.Parse(scalar).Problem is not null;
            if (!colonInside && !brokenFlow)
            {
                lines.Add(line);
                continue;
            }

            lines.Add($"{line[..colon]}:{leading}'{scalar.Replace("'", "''", StringComparison.Ordinal)}'{trailing}");
            changed = true;
        }

        return changed ? string.Join('\n', lines) : null;
    }

    // What agents/openai.yaml's policy says. A file Codex can't read is ignored.
    private static (bool ImplicitOff, bool OtherProduct) Policy(string file)
    {
        if (!File.Exists(file) || YamlDocument.Parse(File.ReadAllText(file)) is not { Problem: null, Root: JsonObject root } || root["policy"] is not JsonObject policy)
        {
            return (false, false);
        }

        var products = (policy["products"] as JsonArray ?? []).Select(product => product?.ToString().ToLowerInvariant() ?? "").ToList();
        if (products.Any(product => !Products.Contains(product)))
        {
            return (false, false);
        }

        var implicitOff = policy["allow_implicit_invocation"] is JsonValue allowed && allowed.GetValueKind() == JsonValueKind.False;
        return (implicitOff, products.Count > 0 && !products.Contains("codex"));
    }

    // The nearest plugin manifest at or above the skills root names its skills. The recordings show that one
    // inside the root doesn't count: every manifest sits in a hidden folder, which the scan skips.
    private static string? Namespace(string directory, string fileSystemRoot, Dictionary<string, string?> cache)
    {
        foreach (var folder in Paths.Upward(directory, fileSystemRoot))
        {
            if (!cache.TryGetValue(folder, out var name))
            {
                name = PluginManifests.Select(manifest => Path.Combine(folder, manifest)).Where(File.Exists).Select(ManifestName).FirstOrDefault();
                name = name is null ? null : name.Trim().Length == 0 ? Path.GetFileName(folder) : name;
                cache[folder] = name;
            }

            if (name is not null)
            {
                return name;
            }
        }

        return null;
    }

    private static string? ManifestName(string manifest)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(manifest))?["name"] is JsonValue value && value.TryGetValue<string>(out var name) ? name : "";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long Tokens(string line) => (Encoding.UTF8.GetByteCount(line) + 1 + 3) / 4;

    private static string Slashes(string path) => path.Replace('\\', '/');
}
