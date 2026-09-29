using Axiomarium.Core.Health;
using Axiomarium.Core.Paths;

namespace Axiomarium.Core.Instructions;

// The findings about skills and hooks, read from the same resolutions as the instruction findings. Each
// is documented in findings/<id>/finding.md, and only fires on files the user can fix: not on a plugin's
// skills and hooks, synced or bundled skills, or managed ones.
public static partial class InstructionFindings
{
    private static IEnumerable<InstructionFinding> SkillFindings(HarnessResolution harness, Context context)
    {
        var resolution = harness.Resolution;
        var claude = harness.Harness == Harness.ClaudeCode;
        var name = claude ? "Claude Code" : "Codex";

        foreach (var finding in claude ? ClaudeCodeClashes(resolution, context) : CodexClashes(resolution, context))
        {
            yield return finding;
        }

        foreach (var skill in resolution.Skills.Where(skill => skill.Cut && skill.Path is not null && Editable(skill.Path, context)))
        {
            var shown = context.Show(skill.Path!);
            yield return new InstructionFinding(
                "skill-description-cut", Severity.Warning, shown, null,
                $"{shown} has a description over the {(claude ? "1,536" : "1,024")} characters {name} shows for one skill, so the model sees it cut.",
                claude
                    ? "Shorten the description and when_to_use, and put the words that should make the agent pick the skill first."
                    : "Shorten the description, and put the words that should make the agent pick the skill first.");
        }

        if (resolution.Listing is { OverBudget: true } listing)
        {
            yield return claude
                ? new InstructionFinding(
                    "skill-listing-over-budget", Severity.Warning, context.Show(Path.Combine(context.Machine.ClaudeConfig, "settings.json")), null,
                    $"Claude Code's skill listing takes {listing.Size:N0} characters, over its budget of {listing.Budget:N0}, assuming {listing.Assumption}, so some skills are listed without their descriptions, starting with the ones used least.",
                    $"Shorten descriptions, set skills you rarely use to \"name-only\" in skillOverrides, or raise skillListingBudgetFraction in {context.Show(Path.Combine(context.Machine.ClaudeConfig, "settings.json"))}.")
                : new InstructionFinding(
                    "skill-listing-over-budget", Severity.Warning, CodexConfigFile(context), null,
                    $"Codex's skill listing takes about {listing.Size:N0} tokens, over its budget of {listing.Budget:N0}, assuming {listing.Assumption}, so Codex shortens descriptions and then drops skills from the end.",
                    $"Shorten descriptions, turn skills off with [[skills.config]], or raise skills.max_context_tokens in {CodexConfigFile(context)}.");
        }

        foreach (var skill in resolution.Skills.Where(skill => skill.Fallback is not null && skill.Path is not null && Editable(skill.Path, context)))
        {
            var shown = context.Show(skill.Path!);
            var problem = skill.Fallback == "it has no description" ? "has no description" : "has frontmatter that doesn't parse";
            yield return new InstructionFinding(
                "skill-frontmatter-invalid", Severity.Warning, shown, null,
                $"{shown} {problem}, so Claude Code lists the skill with its first line in place of a description.",
                "Fix the YAML between the --- lines, and give the skill a description.");
        }

        foreach (var skill in resolution.NotListed.Where(skill => skill.Rule == CodexSkillRules.Invalid && skill.Path is not null && Editable(skill.Path, context)))
        {
            var shown = context.Show(skill.Path!);
            yield return new InstructionFinding(
                "skill-frontmatter-invalid", Severity.Warning, shown, null,
                $"Codex skips {shown}, because {skill.Detail ?? "it can't read its frontmatter"}, so the model never sees it.",
                "Start SKILL.md with frontmatter that gives a description and a name of up to 64 characters.");
        }

        // Only the repo's skills: a personal skill that matches nothing here may match in another repo.
        if (claude && context.RepoRoot is { } root)
        {
            var scoped = resolution.Skills.Where(skill => skill.Rule == ClaudeCodeSkillRules.PathsSkill).Select(skill => skill.Path)
                .Concat(resolution.NotListed.Where(skill => skill.Rule == ClaudeCodeSkillRules.PathsSkillNoMatch).Select(skill => skill.Path))
                .OfType<string>()
                .Where(path => Instructions.Paths.IsUnder(path, root));
            foreach (var path in scoped)
            {
                foreach (var finding in SkillPathsMatchNothing(path, root, context))
                {
                    yield return finding;
                }
            }
        }
    }

    // A copied skills folder clashes a skill at a time, so clashes between the same folders make one finding.
    private static IEnumerable<InstructionFinding> ClaudeCodeClashes(Resolution resolution, Context context)
    {
        var clashes = resolution.NotListed
            .Where(skill => skill.Rule == ClaudeCodeSkillRules.Shadowed && skill.Path is not null && Editable(skill.Path, context))
            .Select(hidden => (Hidden: hidden, Winner: resolution.Skills.FirstOrDefault(skill => skill.Name == hidden.Name)?.Path
                ?? resolution.NotListed.FirstOrDefault(skill => skill.Name == hidden.Name && skill.Rule != ClaudeCodeSkillRules.Shadowed)?.Path));
        foreach (var group in clashes.GroupBy(clash => (Hidden: SkillFolder(clash.Hidden.Path!), Winner: clash.Winner is null ? null : SkillFolder(clash.Winner))))
        {
            var list = group.ToList();
            if (list.Count == 1)
            {
                var (hidden, winner) = list[0];
                var shown = context.Show(hidden.Path!);
                yield return new InstructionFinding(
                    "skill-name-clash", Severity.Warning, shown, null,
                    $"{shown} is named {hidden.Name}, like {(winner is null ? "another skill" : context.Show(winner))}, which Claude Code lists instead, so this one never reaches the model.",
                    "Rename one of the two folders: Claude Code names a skill after its folder.");
                continue;
            }

            var folder = context.Show(group.Key.Hidden);
            yield return new InstructionFinding(
                "skill-name-clash", Severity.Warning, folder, null,
                $"{folder} holds {list.Count} skills named like ones in {(group.Key.Winner is null ? "other folders" : context.Show(group.Key.Winner))}, such as {Examples(list.Select(clash => clash.Hidden.Name))}, which Claude Code lists instead, so these never reach the model.",
                "Rename one folder of each pair: Claude Code names a skill after its folder.");
        }
    }

    private static IEnumerable<InstructionFinding> CodexClashes(Resolution resolution, Context context)
    {
        var clashes = resolution.Skills
            .Where(skill => skill.Path is not null)
            .GroupBy(skill => skill.Name)
            .Select(group => (Name: group.Key, Paths: group.Select(skill => skill.Path!).ToList()))
            .Where(clash => clash.Paths.Count > 1 && clash.Paths.Any(path => Editable(path, context)));
        foreach (var group in clashes.GroupBy(clash => string.Join('\n', clash.Paths.Select(SkillFolder))))
        {
            var list = group.ToList();
            if (list.Count == 1)
            {
                var (name, paths) = list[0];
                yield return new InstructionFinding(
                    "skill-name-clash", Severity.Warning, context.Show(paths.First(path => Editable(path, context))), null,
                    $"Codex lists {paths.Count} skills named {name}, {Join(paths.Select(context.Show))}, so ${name} picks none of them.",
                    $"Give one a different name in its SKILL.md frontmatter, or turn one off with [[skills.config]] in {CodexConfigFile(context)}.");
                continue;
            }

            var folders = list[0].Paths.Select(SkillFolder).ToList();
            var copies = folders.Count == 2 ? "twice" : $"{folders.Count} times";
            yield return new InstructionFinding(
                "skill-name-clash", Severity.Warning, context.Show(folders.First(folder => Editable(folder, context))), null,
                $"Codex lists {list.Count} skills {copies}, from {Join(folders.Select(context.Show))}, such as {Examples(list.Select(clash => clash.Name))}, so a $name mention picks {(folders.Count == 2 ? "neither copy" : "none of them")}.",
                $"Give one of each pair a different name in its SKILL.md frontmatter, or turn one off with [[skills.config]] in {CodexConfigFile(context)}.");
        }
    }

    // The folder that holds the skill's folder, or a command's own folder.
    private static string SkillFolder(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        return Path.GetFileName(path) == "SKILL.md" ? Path.GetDirectoryName(folder)! : folder;
    }

    private static string Examples(IEnumerable<string> names) => Join(names.Take(3));

    private static IEnumerable<InstructionFinding> SkillPathsMatchNothing(string skill, string root, Context context)
    {
        if (Frontmatter.ReadSkill(File.ReadAllText(skill)).Paths is not { } patterns)
        {
            yield break;
        }

        var shown = context.Show(skill);
        foreach (var pattern in patterns)
        {
            if (!Glob.TryParse(pattern, out _, out var problem))
            {
                yield return new InstructionFinding(
                    "skill-paths-match-nothing", Severity.Warning, shown, null,
                    $"{shown} has the pattern \"{pattern}\", which matches nothing: {problem}",
                    "Fix the pattern, or remove it.");
            }
        }

        if (context.FirstMatch(root, patterns) is null)
        {
            yield return new InstructionFinding(
                "skill-paths-match-nothing", Severity.Warning, shown, null,
                $"{shown} applies to {string.Join(", ", patterns)}, which {(patterns.Count == 1 ? "matches" : "match")} no file in the repo, so Claude Code never lists it.",
                "Fix the patterns. They match paths from the launch directory, the way .gitignore lines do.");
        }
    }

    private static IEnumerable<InstructionFinding> HookFindings(HarnessResolution harness, Context context)
    {
        var hooks = harness.Resolution.ConfiguredHooks.Where(hook => Editable(hook.Path, context) && hook.Source != ClaudeCodeHookRules.PluginHook).ToList();
        foreach (var hook in hooks)
        {
            var shown = context.Show(hook.Path);
            if (hook.Blocked == ClaudeCodeHookRules.IfIgnored)
            {
                yield return new InstructionFinding(
                    "hook-never-runs", Severity.Warning, shown, null,
                    $"{shown} gives a {hook.Event} hook the condition if {hook.Condition}, and Claude Code reads if only on tool events, so the hook never runs.",
                    "Remove the if, or move the hook to PreToolUse or PostToolUse.");
            }
            else if (hook.Blocked == ClaudeCodeHookRules.MatcherInvalid || hook.Blocked == CodexHookRules.MatcherInvalid)
            {
                yield return new InstructionFinding(
                    "hook-never-runs", Severity.Warning, shown, null,
                    $"{shown} has a {hook.Event} hook whose matcher, {hook.Matcher}, isn't a valid regex, so it never runs.",
                    "Fix the regex, or list exact names such as Edit|Write.");
            }
            else if (hook.Blocked == CodexHookRules.HandlerSkipped)
            {
                yield return new InstructionFinding(
                    "hook-never-runs", Severity.Warning, shown, null,
                    $"{shown} has a {hook.Event} hook of type {hook.Handler}, which Codex skips, so it never runs.",
                    "Rewrite it as a command hook.");
            }
        }

        // One finding per file and reason, since a file often holds several hooks.
        foreach (var group in hooks.Where(hook => hook.Blocked == CodexHookRules.Untrusted || hook.Blocked == CodexHookRules.Modified || hook.Blocked == CodexHookRules.ProjectUntrusted)
            .GroupBy(hook => (hook.Path, hook.Blocked)))
        {
            var shown = context.Show(group.Key.Path);
            var count = group.Count();
            var (hooksWord, theyRun, them, the) = count == 1 ? ("hook", "it never runs", "it", "the hook") : ("hooks", "they never run", "them", "the hooks");
            yield return group.Key.Blocked == CodexHookRules.ProjectUntrusted
                ? new InstructionFinding(
                    "codex-hook-untrusted", Severity.Warning, shown, null,
                    $"Codex doesn't load {shown}, because the project isn't trusted, so its {count} {hooksWord} never run{(count == 1 ? "s" : "")}.",
                    $"Trust the project in Codex, which sets trust_level = \"trusted\" for it in {CodexConfigFile(context)}.")
                : group.Key.Blocked == CodexHookRules.Modified
                    ? new InstructionFinding(
                        "codex-hook-untrusted", Severity.Warning, shown, null,
                        $"{shown} has {count} {hooksWord} that changed since Codex trusted {them}, so {(count == 1 ? "it doesn't" : "they don't")} run.",
                        $"Review the change in Codex and trust {the} again.")
                    : new InstructionFinding(
                        "codex-hook-untrusted", Severity.Warning, shown, null,
                        $"{shown} has {count} {hooksWord} Codex hasn't trusted, so {theyRun}.",
                        $"Review {the} in Codex and trust {them}, which records {(count == 1 ? "its" : "each one's")} hash under [hooks.state] in {CodexConfigFile(context)}.");
        }
    }

    // A plugin's, a synced, a bundled or a managed file is someone else's to fix.
    private static bool Editable(string path, Context context)
    {
        var machine = context.Machine;
        string[] theirs =
        [
            Path.Combine(machine.ClaudeConfig, "plugins"), Path.Combine(machine.ClaudeConfig, "skills", "synced"), Path.Combine(machine.CodexHome, "skills", ".system"),
            machine.ClaudeManaged, machine.CodexAdmin,
        ];
        return !theirs.Any(folder => Instructions.Paths.IsUnder(path, folder));
    }

    private static string CodexConfigFile(Context context) => context.Show(Path.Combine(context.Machine.CodexHome, "config.toml"));

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 2 ? string.Join(" and ", list) : $"{string.Join(", ", list[..^1])} and {list[^1]}";
    }
}
