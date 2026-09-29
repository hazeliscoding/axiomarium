using System.Text;
using System.Text.RegularExpressions;
using Axiomarium.Core.Health;
using Axiomarium.Core.Paths;

namespace Axiomarium.Core.Instructions;

/// <summary>A problem with instruction files: an instruction that doesn't reach an agent as meant, or context spent twice.</summary>
/// <param name="Id">The finding's stable id, such as <c>dead-import</c>, which names its folder in <c>findings/</c>.</param>
/// <param name="Severity"><see cref="Severity.Warning"/> when an instruction doesn't reach the agent as meant, <see cref="Severity.Info"/> for waste.</param>
/// <param name="File">The file the finding is about, shown as <see cref="DisplayPath"/> shows paths.</param>
/// <param name="Line">The line in <paramref name="File"/>, from 1, when the finding points at one.</param>
/// <param name="Message">What is wrong and why it matters, as one or two sentences that start with the file.</param>
/// <param name="Fix">What to do about it, as a sentence.</param>
public sealed record InstructionFinding(string Id, Severity Severity, string File, int? Line, string Message, string Fix);

/// <summary>An instruction file in a repo check, and which harnesses load it.</summary>
/// <param name="Path">The file, shown as <see cref="DisplayPath"/> shows paths.</param>
/// <param name="LoadedBy">The harnesses that load it for some file in the repo, launched from the repo root. Empty when none does.</param>
public sealed record InstructionFile(string Path, IReadOnlyList<Harness> LoadedBy);

/// <summary>What <see cref="InstructionFindings.Check"/> found in a repo.</summary>
/// <param name="Files">Every instruction file the harnesses load or drop, in the order they meet them, ignored files left out.</param>
/// <param name="Findings">Each finding once, warnings first, then by file and line, ignored files left out.</param>
/// <param name="Ignored">How many instruction files the ignore globs left out.</param>
public sealed record InstructionCheck(IReadOnlyList<InstructionFile> Files, IReadOnlyList<InstructionFinding> Findings, int Ignored)
{
    /// <summary>The repo's and the user's skills, as the harnesses list them from the repo root, built-in ones left out.</summary>
    public IReadOnlyList<InventorySkill> Skills { get; init; } = [];

    /// <summary>Every hook the harnesses have configured, launched from the repo root.</summary>
    public IReadOnlyList<InventoryHook> Hooks { get; init; } = [];
}

/// <summary>A skill in the setup, and which harnesses list it for at least one file.</summary>
/// <param name="Name">The name the first harness to list it uses.</param>
/// <param name="Path">Its <c>SKILL.md</c> or command file, as the doctor shows paths.</param>
/// <param name="ListedBy">The harnesses that list it for some file, in harness order. Empty when none does.</param>
/// <param name="NotListed">Why no harness lists it, when none does.</param>
/// <param name="Source">The rule the first harness lists it by, such as <c>claude-code/personal-skill</c>, or <see langword="null"/> when none does.</param>
/// <param name="InRepo">Whether its file is in the repo, rather than in the home folder, a plugin or the system.</param>
public sealed record InventorySkill(string Name, string Path, IReadOnlyList<Harness> ListedBy, HarnessRule? NotListed, HarnessRule? Source, bool InRepo);

/// <summary>A hook a harness has configured, and why it can't run, if it can't.</summary>
/// <param name="Harness">The harness.</param>
/// <param name="Event">The harness's event, such as <c>SessionStart</c>.</param>
/// <param name="Path">The file that declares it, as the doctor shows paths.</param>
/// <param name="Handler">What it runs.</param>
/// <param name="Blocked">Why it can never run, or <see langword="null"/> when it can.</param>
/// <param name="InRepo">Whether the file that declares it is in the repo.</param>
public sealed record InventoryHook(Harness Harness, string Event, string Path, string Handler, HarnessRule? Blocked, bool InRepo);

/// <summary>Finds the problems in what the harnesses load. Each finding is documented in <c>findings/&lt;id&gt;/finding.md</c>.</summary>
public static partial class InstructionFindings
{
    // A paragraph shorter than this, such as a heading, repeats by accident and costs next to nothing.
    private const int ShortestDuplicate = 40;

    private const string Probe = "axm-probe";

    /// <summary>Every finding id, in the order the findings are documented. The ids are a contract.</summary>
    public static IReadOnlyList<string> Ids { get; } =
    [
        "dead-import", "import-too-deep", "agents-md-hidden", "rule-frontmatter-invalid", "rule-matches-nothing",
        "codex-byte-cap", "codex-empty-override", "duplicate-block", "dead-link",
    ];

    /// <summary>The findings in what <paramref name="explanation"/> says each harness loads and drops.</summary>
    /// <param name="explanation">What the harnesses load for one file. Only its harnesses are checked.</param>
    /// <param name="machine">Where the home folder and the harnesses' user files are.</param>
    /// <returns>
    /// Each finding once, warnings first, then by file and line. <c>rule-matches-nothing</c> needs a repo to
    /// search, so outside a repo it never fires.
    /// </returns>
    public static IReadOnlyList<InstructionFinding> For(Explanation explanation, Machine machine) =>
        Ordered(Find(explanation, new Context(explanation.RepoRoot, machine)));

    /// <summary>The findings for a whole repo, launched from its root. See <see cref="Check"/>.</summary>
    /// <param name="repoRoot">The repo's root folder. It needn't hold a <c>.git</c>.</param>
    /// <param name="machine">Where the home folder and the harnesses' user files are.</param>
    /// <returns>Each finding once, warnings first, then by file and line.</returns>
    public static IReadOnlyList<InstructionFinding> ForRepo(string repoRoot, Machine machine) => Check(repoRoot, machine, []).Findings;

    /// <summary>
    /// Checks a whole repo, launched from its root, as Claude Code and Codex: for a file in each folder that
    /// holds an instruction file, and for a file each path rule matches.
    /// </summary>
    /// <param name="repoRoot">The repo's root folder. It needn't hold a <c>.git</c>.</param>
    /// <param name="machine">Where the home folder and the harnesses' user files are.</param>
    /// <param name="ignore">
    /// Globs relative to the repo root for files that are broken on purpose. Files they match aren't
    /// listed, and no finding about them is reported, but they still count as files a rule can match.
    /// </param>
    /// <returns>Every instruction file the harnesses load or drop, the findings, and how many instruction files were ignored.</returns>
    public static InstructionCheck Check(string repoRoot, Machine machine, IReadOnlyList<Glob> ignore)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        var context = new Context(root, machine);
        bool Ignored(string path) =>
            Instructions.Paths.IsUnder(path, root) && !Instructions.Paths.Same(path, root) && ignore.Any(glob => glob.IsMatch(Relative(root, path)));

        var targets = new List<string> { Path.Combine(root, Probe) };
        foreach (var file in context.Files)
        {
            if (InstructionFolder(file) is { } folder)
            {
                targets.Add(Path.Combine(folder, Probe));
            }
        }

        foreach (var rule in context.Files.Where(IsRule).Where(rule => !Ignored(rule)))
        {
            var frontmatter = Frontmatter.Read(File.ReadAllText(rule));
            if (frontmatter is { Valid: true, Paths: { } patterns } && context.FirstMatch(RuleBase(rule), patterns) is { } match)
            {
                targets.Add(match);
            }
        }

        Harness[] harnesses = [Harness.ClaudeCode, Harness.Codex];
        targets = [.. targets.Where(target => !Ignored(target)).Distinct(Context.PathComparer)];
        var explanations = targets.Select(target => Explainer.Explain(target, root, harnesses, machine, root, repoRoot: root)).ToList();

        // A skill with paths joins the listing for the files they match, so one such file is explained too.
        var atRoot = explanations.FirstOrDefault()?.Harnesses ?? [];
        var skillTargets = atRoot
            .SelectMany(harness => harness.Resolution.NotListed)
            .Where(skill => skill.Rule == ClaudeCodeSkillRules.PathsSkillNoMatch && skill.Path is not null)
            .Select(skill => Frontmatter.ReadSkill(File.ReadAllText(skill.Path!)).Paths is { } patterns
                ? context.Files.FirstOrDefault(file => PathPatterns.MatchLikeGitignore(patterns, root, file))
                : null)
            .OfType<string>()
            .Where(target => !Ignored(target) && !targets.Contains(target, Context.PathComparer))
            .Distinct(Context.PathComparer);
        explanations.AddRange(skillTargets.Select(target => Explainer.Explain(target, root, harnesses, machine, root, repoRoot: root)));

        // A file is listed once, in the order the harnesses first meet it, with every harness that loads it.
        var files = new List<(string Path, List<Harness> LoadedBy)>();
        void Meet(string path, Harness? loadedBy)
        {
            if (Ignored(path) || !File.Exists(path))
            {
                return;
            }

            var index = files.FindIndex(file => Instructions.Paths.Same(file.Path, path));
            if (index < 0)
            {
                files.Add((path, []));
                index = files.Count - 1;
            }

            if (loadedBy is { } harness && !files[index].LoadedBy.Contains(harness))
            {
                files[index].LoadedBy.Add(harness);
            }
        }

        foreach (var harness in explanations.SelectMany(explanation => explanation.Harnesses))
        {
            foreach (var item in harness.Resolution.Loaded)
            {
                Meet(item.Path, harness.Harness);
            }

            foreach (var item in harness.Resolution.Dropped)
            {
                Meet(item.Path, null);
            }
        }

        var findings = Ordered(explanations.SelectMany(explanation => Find(explanation, context)))
            .Where(finding => !IgnoredShown(finding.File, ignore))
            .ToList();
        return new InstructionCheck(
            [.. files.Select(file => new InstructionFile(context.Show(file.Path), [.. file.LoadedBy.Order()]))],
            findings,
            context.Files.Count(file => IsInstructionFile(file) && Ignored(file)))
        {
            Skills = Skills(explanations, context, Ignored),
            Hooks = [.. atRoot.SelectMany(harness => harness.Resolution.ConfiguredHooks
                .Where(hook => !Ignored(hook.Path))
                .Select(hook => new InventoryHook(harness.Harness, hook.Event, context.Show(hook.Path), hook.Handler, hook.Blocked, Instructions.Paths.IsUnder(hook.Path, root))))],
        };
    }

    // Each skill once, in the order the harnesses first meet it, with every harness that lists it for some file.
    private static List<InventorySkill> Skills(IEnumerable<Explanation> explanations, Context context, Func<string, bool> ignored)
    {
        var skills = new List<(string Name, string Path, List<Harness> ListedBy, HarnessRule? NotListed, HarnessRule? Source)>();
        void Meet(string name, string path, Harness? listedBy, HarnessRule rule)
        {
            if (ignored(path))
            {
                return;
            }

            var index = skills.FindIndex(skill => Instructions.Paths.Same(skill.Path, path));
            if (index < 0)
            {
                skills.Add((name, path, [], null, null));
                index = skills.Count - 1;
            }

            var skill = skills[index];
            if (listedBy is { } harness && !skill.ListedBy.Contains(harness))
            {
                skill.ListedBy.Add(harness);
            }

            skills[index] = skill with { NotListed = skill.NotListed ?? (listedBy is null ? rule : null), Source = skill.Source ?? (listedBy is null ? null : rule) };
        }

        foreach (var harness in explanations.SelectMany(explanation => explanation.Harnesses))
        {
            foreach (var skill in harness.Resolution.Skills.Where(skill => !Explainer.IsBuiltIn(skill) && skill.Path is not null))
            {
                Meet(skill.Name, skill.Path!, harness.Harness, skill.Rule);
            }

            foreach (var skill in harness.Resolution.NotListed.Where(skill => skill.Path is not null))
            {
                Meet(skill.Name, skill.Path!, null, skill.Rule);
            }
        }

        return [.. skills.Select(skill => new InventorySkill(
            skill.Name,
            context.Show(skill.Path),
            [.. skill.ListedBy.Order()],
            skill.ListedBy.Count == 0 ? skill.NotListed : null,
            skill.Source,
            context.RepoRoot is { } root && Instructions.Paths.IsUnder(skill.Path, root)))];
    }

    // Whether a path as findings show it is inside the repo and ignored. Home and absolute paths never are.
    private static bool IgnoredShown(string shown, IReadOnlyList<Glob> ignore) =>
        !shown.StartsWith('~') && !Path.IsPathRooted(shown) && ignore.Any(glob => glob.IsMatch(shown));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static IEnumerable<InstructionFinding> Find(Explanation explanation, Context context)
    {
        foreach (var harness in explanation.Harnesses)
        {
            var found = harness.Harness switch
            {
                Harness.ClaudeCode => ClaudeCode(harness.Resolution, context),
                Harness.Codex => Codex(harness.Resolution, context),
                _ => [],
            };
            foreach (var finding in found.Concat(DuplicateBlocks(harness.Resolution, context)))
            {
                yield return finding;
            }
        }

        foreach (var finding in DeadLinks(explanation.Harnesses.SelectMany(harness => harness.Resolution.Loaded), context))
        {
            yield return finding;
        }
    }

    private static IEnumerable<InstructionFinding> ClaudeCode(Resolution resolution, Context context)
    {
        var importedBy = new Dictionary<string, ImportSite>(Context.PathComparer);
        foreach (var item in resolution.Loaded.Where(item => item.Via is not null))
        {
            importedBy.TryAdd(Path.GetFullPath(item.Path), item.Via!);
        }

        // An AGENTS.md above the repo serves other folders too, so hiding it is a choice, not this repo's problem.
        bool InRepo(string path) => context.RepoRoot is not { } root || Instructions.Paths.IsUnder(path, root);

        foreach (var dropped in resolution.Dropped)
        {
            var rule = dropped.Rule;
            var finding =
                rule == ClaudeCodeRules.MissingImport ? DeadImport(dropped, context)
                : rule == ClaudeCodeRules.ImportTooDeep ? ImportTooDeep(dropped, importedBy, context)
                : rule == ClaudeCodeRules.AgentsMdHidden && InRepo(dropped.Path) ? AgentsMdHidden(dropped, context)
                : null;
            if (finding is not null)
            {
                yield return finding;
            }
        }

        foreach (var item in resolution.Loaded.Where(item => item.Rule == ClaudeCodeRules.InvalidFrontmatter))
        {
            yield return new InstructionFinding(
                "rule-frontmatter-invalid", Severity.Warning, context.Show(item.Path), 1,
                $"{context.Show(item.Path)} has frontmatter that doesn't parse, so Claude Code ignores all of it and loads the rule for every file.",
                "Fix the YAML between the --- lines, such as its indentation, so the rule's paths count again.");
        }

        if (context.RepoRoot is not { } repoRoot)
        {
            yield break;
        }

        // Only project rules: a user rule that matches nothing in this repo may match in another.
        var loaded = resolution.Loaded.Where(item => item.Rule == ClaudeCodeRules.PathRule).Select(item => item.Path);
        var skipped = resolution.Dropped.Where(item => item.Rule == ClaudeCodeRules.PathRuleNoMatch).Select(item => item.Path);
        foreach (var rule in loaded.Concat(skipped).Where(rule => Instructions.Paths.IsUnder(rule, repoRoot)))
        {
            foreach (var finding in RuleMatchesNothing(rule, loadedHere: loaded.Contains(rule, Context.PathComparer), context))
            {
                yield return finding;
            }
        }
    }

    private static InstructionFinding DeadImport(DroppedInstruction dropped, Context context)
    {
        var site = $"{context.Show(dropped.Via!.File)}:{dropped.Via.Line}";
        var name = Path.GetFileName(dropped.Path);
        var trimmed = dropped.Path[..^1];
        if (name.Length > 1 && TrailingPunctuation().IsMatch(name))
        {
            var mark = name[^1];
            var restore = Imports.Exists(trimmed) ? "" : $", and restore {context.Show(trimmed)} or remove the import";
            return new InstructionFinding(
                "dead-import", Severity.Warning, context.Show(dropped.Via.File), dropped.Via.Line,
                $"{site} imports {context.Show(trimmed)} with a \"{mark}\" after it, which Claude Code reads as part of the path, so it loads nothing.",
                $"Remove the \"{mark}\", or put a space before it{restore}.");
        }

        return new InstructionFinding(
            "dead-import", Severity.Warning, context.Show(dropped.Via.File), dropped.Via.Line,
            $"{site} imports {context.Show(dropped.Path)}, which does not exist, so Claude Code loads nothing in its place.",
            "Restore the file, or remove the import.");
    }

    private static InstructionFinding ImportTooDeep(DroppedInstruction dropped, Dictionary<string, ImportSite> importedBy, Context context)
    {
        var root = dropped.Via!.File;
        while (importedBy.TryGetValue(Path.GetFullPath(root), out var site))
        {
            root = site.File;
        }

        var (target, start) = (context.Show(dropped.Path), context.Show(root));
        return new InstructionFinding(
            "import-too-deep", Severity.Warning, context.Show(dropped.Via.File), dropped.Via.Line,
            $"{context.Show(dropped.Via.File)}:{dropped.Via.Line} imports {target}, a fifth hop from {start}. Claude Code follows four, so {target} never loads.",
            $"Import {target} from a file fewer hops from {start}, or move its content up the chain.");
    }

    private static InstructionFinding AgentsMdHidden(DroppedInstruction dropped, Context context)
    {
        string[] names = ["CLAUDE.md", Path.Combine(".claude", "CLAUDE.md"), "CLAUDE.local.md"];
        var folder = Path.GetDirectoryName(dropped.Path)!;
        var owner = Path.GetFileName(folder) == ".claude" ? Path.GetDirectoryName(folder)! : folder;
        var userMemory = Path.Combine(context.Machine.ClaudeConfig, "CLAUDE.md");
        var nextToIt = names.Select(name => Path.Combine(owner, name)).FirstOrDefault(File.Exists);
        var hiding = nextToIt ?? Instructions.Paths.Upward(owner, context.Machine.FileSystemRoot)
            .SelectMany(directory => names.Select(name => Path.Combine(directory, name)))
            .FirstOrDefault(path => File.Exists(path) && !Instructions.Paths.Same(path, userMemory));

        var agents = context.Show(dropped.Path);
        var cause = hiding is null ? "a CLAUDE file exists and doesn't import it" : $"{context.Show(hiding)} exists and doesn't import it";
        var fix = nextToIt is null
            ? $"Add a CLAUDE.md next to it that says @{Path.GetFileName(dropped.Path)}."
            : $"Add @{Path.GetRelativePath(Path.GetDirectoryName(nextToIt)!, dropped.Path).Replace(Path.DirectorySeparatorChar, '/')} to {context.Show(nextToIt)}.";
        return new InstructionFinding(
            "agents-md-hidden", Severity.Warning, agents, null,
            $"Claude Code skips {agents}, because {cause}, so instructions written there for every agent never reach Claude Code.",
            fix);
    }

    private static IEnumerable<InstructionFinding> RuleMatchesNothing(string rule, bool loadedHere, Context context)
    {
        var frontmatter = Frontmatter.Read(File.ReadAllText(rule));
        if (frontmatter is not { Valid: true, Paths: { } patterns })
        {
            yield break;
        }

        var shown = context.Show(rule);
        foreach (var pattern in patterns)
        {
            if (!Glob.TryParse(pattern, out _, out var problem))
            {
                yield return new InstructionFinding(
                    "rule-matches-nothing", Severity.Warning, shown, null,
                    $"{shown} has the pattern \"{pattern}\", which matches nothing: {problem}",
                    "Fix the pattern, or remove it.");
            }
        }

        // A rule that loaded for the file in hand matches at least that file, even if it doesn't exist yet.
        var ruleBase = RuleBase(rule);
        if (loadedHere || context.FirstMatch(ruleBase, patterns) is not null)
        {
            yield break;
        }

        var baseName = Instructions.Paths.Same(ruleBase, context.RepoRoot!) ? "the repo root" : context.Show(ruleBase);
        yield return new InstructionFinding(
            "rule-matches-nothing", Severity.Warning, shown, null,
            $"{shown} applies to {string.Join(", ", patterns)}, which {(patterns.Count == 1 ? "matches" : "match")} no file in the repo, so Claude Code never loads it.",
            $"Fix the patterns. They match paths relative to {baseName}.");
    }

    private static IEnumerable<InstructionFinding> Codex(Resolution resolution, Context context)
    {
        var cut = resolution.Loaded.FirstOrDefault(item => item.Cut);
        var over = resolution.Dropped.Where(item => item.Rule == CodexRules.ByteBudget).Select(item => context.Show(item.Path)).ToList();
        if (cut is not null || over.Count > 0)
        {
            var dropped = over.Count == 0 ? ""
                : $"{over.Count} later file{(over.Count == 1 ? " is" : "s are")} dropped: {string.Join(", ", over)}";
            var message = cut is null
                ? $"Codex's project files use up project_doc_max_bytes, so {dropped}."
                : $"{context.Show(cut.Path)} is cut to {cut.Bytes} of its {new FileInfo(cut.Path).Length} bytes, because Codex's project files share project_doc_max_bytes{(dropped.Length > 0 ? $", and {dropped}" : "")}.";
            yield return new InstructionFinding(
                "codex-byte-cap", Severity.Warning, cut is null ? over[0] : context.Show(cut.Path), null, message,
                $"Shorten the project files, or raise project_doc_max_bytes in {context.Show(Path.Combine(context.Machine.CodexHome, "config.toml"))}.");
        }

        foreach (var folder in resolution.Dropped.Where(item => item.Rule == CodexRules.EmptyOverride).GroupBy(item => Path.GetDirectoryName(item.Path)!))
        {
            var empty = context.Show(Path.Combine(folder.Key, "AGENTS.override.md"));
            yield return new InstructionFinding(
                "codex-empty-override", Severity.Warning, empty, null,
                $"{empty} is empty, and Codex picks it over {string.Join(", ", folder.Select(item => context.Show(item.Path)))}, so Codex loads nothing from that folder.",
                "Delete the empty AGENTS.override.md, or write the override in it.");
        }
    }

    // One finding per pair of files, pointing at the first paragraph the later file repeats.
    private static IEnumerable<InstructionFinding> DuplicateBlocks(Resolution resolution, Context context)
    {
        var firstSeen = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        var pairs = new List<(string Later, int Line, string Earlier, int EarlierLine, int Count)>();
        foreach (var item in resolution.Loaded)
        {
            foreach (var (paragraph, line) in MarkdownText.Paragraphs(Visible(item)).Where(paragraph => paragraph.Text.Length >= ShortestDuplicate))
            {
                if (!firstSeen.TryGetValue(paragraph, out var earlier))
                {
                    firstSeen[paragraph] = (item.Path, line);
                    continue;
                }

                if (Instructions.Paths.Same(earlier.File, item.Path))
                {
                    continue;
                }

                var index = pairs.FindIndex(pair => pair.Later == item.Path && pair.Earlier == earlier.File);
                if (index < 0)
                {
                    pairs.Add((item.Path, line, earlier.File, earlier.Line, 1));
                }
                else
                {
                    pairs[index] = pairs[index] with { Count = pairs[index].Count + 1 };
                }
            }
        }

        foreach (var (later, line, earlier, earlierLine, count) in pairs)
        {
            var (shown, source) = (context.Show(later), context.Show(earlier));
            var more = count == 1 ? ". Both files load together, so it takes up context twice."
                : $", and {count - 1} more of its paragraphs {(count == 2 ? "repeats" : "repeat")} {source} too. Both files load together, so they take up context twice.";
            yield return new InstructionFinding(
                "duplicate-block", Severity.Info, shown, line,
                $"{shown}:{line} repeats a paragraph from {source}:{earlierLine}{more}",
                "Keep the paragraph in one of the files.");
        }
    }

    private static IEnumerable<InstructionFinding> DeadLinks(IEnumerable<LoadedInstruction> loaded, Context context)
    {
        foreach (var item in loaded.DistinctBy(item => Path.GetFullPath(item.Path), Context.PathComparer))
        {
            var folder = Path.GetDirectoryName(item.Path)!;
            foreach (var (written, line) in MarkdownText.Links(Visible(item)))
            {
                if (LinkedPath(written, folder) is { } target && !Exists(target))
                {
                    var shown = context.Show(item.Path);
                    yield return new InstructionFinding(
                        "dead-link", Severity.Warning, shown, line,
                        $"{shown}:{line} links to {context.Show(target)}, which does not exist, so an agent that follows the link finds nothing.",
                        "Fix the link, or restore the file.");
                }
            }
        }
    }

    // The file a relative link points to, or null for a URL, an anchor or an absolute path, which aren't checked.
    private static string? LinkedPath(string written, string folder)
    {
        var target = written.StartsWith('<') && written.EndsWith('>') ? written[1..^1] : written;
        var end = target.IndexOfAny(['#', '?']);
        target = end < 0 ? target : target[..end];
        if (target.Length == 0 || Scheme().IsMatch(target) || target[0] is '/' or '\\' or '~')
        {
            return null;
        }

        var resolved = Path.Combine(folder, Uri.UnescapeDataString(target));

        // Only the folder is normalized: Windows would strip a trailing dot from the name.
        return Path.Combine(Path.GetFullPath(Path.GetDirectoryName(resolved)!), Path.GetFileName(resolved));
    }

    private static bool Exists(string path) =>
        Path.GetFileName(path).Length == 0 ? Directory.Exists(path) : Imports.Exists(path) || Directory.Exists(path);

    // What the harness passes on: the whole file, or its first bytes when the harness cut it.
    private static string Visible(LoadedInstruction item)
    {
        var bytes = File.ReadAllBytes(item.Path);
        return Encoding.UTF8.GetString(bytes, 0, item.Cut ? Math.Min(item.Bytes, bytes.Length) : bytes.Length);
    }

    // The folder whose instruction files a file belongs to, when it is one: CLAUDE.md, AGENTS.md and the
    // like, and anything in .claude/, which belongs to the folder that holds .claude/.
    private static string? InstructionFolder(string file)
    {
        var folder = Path.GetDirectoryName(file)!;
        for (var directory = folder; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (Path.GetFileName(directory) == ".claude")
            {
                return Path.GetDirectoryName(directory);
            }
        }

        return Path.GetFileName(file) is "CLAUDE.md" or "CLAUDE.local.md" or "AGENTS.md" or "AGENTS.override.md" ? folder : null;
    }

    private static bool IsInstructionFile(string file) =>
        Path.GetFileName(file) is "CLAUDE.md" or "CLAUDE.local.md" or "AGENTS.md" or "AGENTS.override.md" || IsRule(file);

    private static bool IsRule(string file) =>
        file.EndsWith(".md", StringComparison.Ordinal)
        && file.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}rules{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // The folder a project rule's patterns match from: the one that holds its .claude/.
    private static string RuleBase(string rule)
    {
        var directory = Path.GetDirectoryName(rule);
        while (directory is not null && Path.GetFileName(directory) != ".claude")
        {
            directory = Path.GetDirectoryName(directory);
        }

        return Path.GetDirectoryName(directory ?? rule)!;
    }

    private static IReadOnlyList<InstructionFinding> Ordered(IEnumerable<InstructionFinding> findings) =>
        [.. findings.Distinct()
            .OrderBy(finding => finding.Severity)
            .ThenBy(finding => finding.File, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line ?? 0)
            .ThenBy(finding => finding.Id, StringComparer.Ordinal)];

    [GeneratedRegex(@"[.,;:!?)\]}'""]$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingPunctuation();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex Scheme();

    // What the findings share for one repo: how to show paths, and its files, listed once.
    private sealed class Context(string? repoRoot, Machine machine)
    {
        private IReadOnlyList<string>? _files;

        public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public string? RepoRoot => repoRoot;

        public Machine Machine => machine;

        /// <summary>Every file in the repo, in path order, except under <c>.git</c> and behind links. Empty outside a repo.</summary>
        public IReadOnlyList<string> Files => _files ??= repoRoot is null ? [] : ListFiles(repoRoot);

        public string Show(string path) => DisplayPath.Of(path, repoRoot, machine.Home);

        /// <summary>The first file under <paramref name="patternBase"/> that one of the valid <paramref name="patterns"/> matches.</summary>
        public string? FirstMatch(string patternBase, IReadOnlyList<string> patterns) =>
            Files.FirstOrDefault(file => !Instructions.Paths.Same(file, patternBase) && PathPatterns.MatchLikeGitignore(patterns, patternBase, file));

        private static List<string> ListFiles(string root)
        {
            // Hidden files count: on Linux and macOS every dotfile is hidden, .claude/ included.
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            var files = new List<string>();
            var folders = new Stack<string>([root]);
            while (folders.Count > 0)
            {
                var folder = folders.Pop();
                files.AddRange(Directory.EnumerateFiles(folder, "*", options));
                foreach (var child in Directory.EnumerateDirectories(folder, "*", options).Where(child => Path.GetFileName(child) != ".git"))
                {
                    folders.Push(child);
                }
            }

            files.Sort(StringComparer.Ordinal);
            return files;
        }
    }
}
