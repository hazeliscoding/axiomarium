using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class SkillAndHookFindingsTests
{
    private static TempVault Repo() => new TempVault().Folder("repo/.git").Folder("home/.codex");

    private static string Skill(string description, string extra = "") => $"---\ndescription: {description}\n{extra}---\nSteps.\n";

    private static IReadOnlyList<InstructionFinding> Check(TempVault vault) =>
        InstructionFindings.ForRepo(Path.Combine(vault.Root, "repo"), TestMachine.For(vault.Root));

    private static InstructionFinding Single(TempVault vault, string id) => Assert.Single(Check(vault), finding => finding.Id == id);

    private static InstructionFinding Finding(string id, string file, string message, string fix) => new(id, Severity.Warning, file, null, message, fix);

    [Fact]
    public void Skill_name_clash_in_claude_code_names_the_skill_listed_instead()
    {
        using var vault = Repo()
            .Write("home/.claude/skills/deploy/SKILL.md", Skill("Personal deploy."))
            .Write("repo/.claude/skills/deploy/SKILL.md", Skill("Project deploy."));

        Assert.Equal(
            Finding(
                "skill-name-clash", ".claude/skills/deploy/SKILL.md",
                ".claude/skills/deploy/SKILL.md is named deploy, like ~/.claude/skills/deploy/SKILL.md, which Claude Code lists instead, so this one never reaches the model.",
                "Rename one of the two folders: Claude Code names a skill after its folder."),
            Single(vault, "skill-name-clash"));
    }

    [Fact]
    public void Skill_name_clash_in_codex_says_a_mention_picks_neither()
    {
        using var vault = Repo()
            .Write("repo/.agents/skills/deploy/SKILL.md", Skill("Repo deploy."))
            .Write("home/.agents/skills/deploy/SKILL.md", Skill("User deploy."));

        Assert.Equal(
            Finding(
                "skill-name-clash", ".agents/skills/deploy/SKILL.md",
                "Codex lists 2 skills named deploy, .agents/skills/deploy/SKILL.md and ~/.agents/skills/deploy/SKILL.md, so $deploy picks none of them.",
                "Give one a different name in its SKILL.md frontmatter, or turn one off with [[skills.config]] in ~/.codex/config.toml."),
            Single(vault, "skill-name-clash"));
    }

    // A copied skills folder clashes a skill at a time, and one finding says so.
    [Fact]
    public void Clashes_between_the_same_two_folders_make_one_finding()
    {
        using var vault = Repo()
            .Write("home/.claude/skills/deploy/SKILL.md", Skill("Personal deploy."))
            .Write("home/.claude/skills/review/SKILL.md", Skill("Personal review."))
            .Write("repo/.claude/skills/deploy/SKILL.md", Skill("Project deploy."))
            .Write("repo/.claude/skills/review/SKILL.md", Skill("Project review."))
            .Write("home/.agents/skills/deploy/SKILL.md", Skill("User deploy."))
            .Write("home/.agents/skills/review/SKILL.md", Skill("User review."))
            .Write("home/.agents/skills/triage/SKILL.md", Skill("User triage."))
            .Write("home/.codex/skills/deploy/SKILL.md", Skill("Old deploy."))
            .Write("home/.codex/skills/review/SKILL.md", Skill("Old review."))
            .Write("home/.codex/skills/triage/SKILL.md", Skill("Old triage."));

        Assert.Equal(
            [
                Finding(
                    "skill-name-clash", ".claude/skills",
                    ".claude/skills holds 2 skills named like ones in ~/.claude/skills, such as deploy and review, which Claude Code lists instead, so these never reach the model.",
                    "Rename one folder of each pair: Claude Code names a skill after its folder."),
                Finding(
                    "skill-name-clash", "~/.agents/skills",
                    "Codex lists 3 skills twice, from ~/.agents/skills and ~/.codex/skills, such as deploy, review and triage, so a $name mention picks neither copy.",
                    "Give one of each pair a different name in its SKILL.md frontmatter, or turn one off with [[skills.config]] in ~/.codex/config.toml."),
            ],
            Check(vault).Where(finding => finding.Id == "skill-name-clash"));
    }

    // A local skill that replaces a built-in one does so on purpose.
    [Fact]
    public void Replacing_a_built_in_skill_is_not_a_clash()
    {
        using var vault = Repo().Write("repo/.claude/skills/init/SKILL.md", Skill("Our init."));

        Assert.DoesNotContain(Check(vault), finding => finding.Id == "skill-name-clash");
    }

    [Fact]
    public void Skill_description_cut_names_each_harness_cap()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/long/SKILL.md", Skill(new string('x', 1600)))
            .Write("repo/.agents/skills/long/SKILL.md", Skill(new string('y', 1100)));

        Assert.Equal(
            [
                Finding(
                    "skill-description-cut", ".agents/skills/long/SKILL.md",
                    ".agents/skills/long/SKILL.md has a description over the 1,024 characters Codex shows for one skill, so the model sees it cut.",
                    "Shorten the description, and put the words that should make the agent pick the skill first."),
                Finding(
                    "skill-description-cut", ".claude/skills/long/SKILL.md",
                    ".claude/skills/long/SKILL.md has a description over the 1,536 characters Claude Code shows for one skill, so the model sees it cut.",
                    "Shorten the description and when_to_use, and put the words that should make the agent pick the skill first."),
            ],
            Check(vault).Where(finding => finding.Id == "skill-description-cut"));
    }

    // Plugin skills are the plugin author's to fix.
    [Fact]
    public void A_skill_the_user_cannot_edit_gets_no_finding()
    {
        using var vault = Repo().Write("home/.codex/skills/.system/long/SKILL.md", Skill(new string('z', 1100)));

        Assert.Empty(Check(vault));
    }

    [Fact]
    public void Skill_listing_over_budget_states_its_assumption_and_the_settings_to_change()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/one/SKILL.md", Skill(new string('a', 1000)))
            .Write("repo/.claude/skills/two/SKILL.md", Skill(new string('b', 1000)));

        var finding = Single(vault, "skill-listing-over-budget");

        Assert.Equal(
            Finding(
                "skill-listing-over-budget", "~/.claude/settings.json",
                "Claude Code's skill listing takes 8,246 characters, over its budget of 8,000, assuming a 200k-token context window, so some skills are listed without their descriptions, starting with the ones used least.",
                "Shorten descriptions, set skills you rarely use to \"name-only\" in skillOverrides, or raise skillListingBudgetFraction in ~/.claude/settings.json."),
            finding);
    }

    [Fact]
    public void Skill_listing_over_budget_in_codex_says_what_codex_does_about_it()
    {
        using var vault = Repo()
            .Write("home/.codex/config.toml", "[skills]\nmax_context_tokens = 20\n")
            .Write("repo/.agents/skills/one/SKILL.md", Skill("A skill with a description long enough to go over."));

        var finding = Single(vault, "skill-listing-over-budget");

        Assert.Equal(("~/.codex/config.toml", "Shorten descriptions, turn skills off with [[skills.config]], or raise skills.max_context_tokens in ~/.codex/config.toml."), (finding.File, finding.Fix));
        Assert.Matches(@"^Codex's skill listing takes about [\d,]+ tokens, over its budget of 20, assuming skills\.max_context_tokens, so Codex shortens descriptions and then drops skills from the end\.$", finding.Message);
    }

    [Fact]
    public void Skill_frontmatter_invalid_says_what_each_harness_does_instead()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/broken/SKILL.md", "---\ndescription: [unclosed\n  bad: - indent\n---\nFirst line.\n")
            .Write("repo/.claude/skills/bare/SKILL.md", "---\nname: bare\n---\nFirst line.\n")
            .Write("repo/.agents/skills/bare/SKILL.md", "---\nname: bare\n---\nFirst line.\n");

        Assert.Equal(
            [
                Finding(
                    "skill-frontmatter-invalid", ".agents/skills/bare/SKILL.md",
                    "Codex skips .agents/skills/bare/SKILL.md, because it has no description, so the model never sees it.",
                    "Start SKILL.md with frontmatter that gives a description and a name of up to 64 characters."),
                Finding(
                    "skill-frontmatter-invalid", ".claude/skills/bare/SKILL.md",
                    ".claude/skills/bare/SKILL.md has no description, so Claude Code lists the skill with its first line in place of a description.",
                    "Fix the YAML between the --- lines, and give the skill a description."),
                Finding(
                    "skill-frontmatter-invalid", ".claude/skills/broken/SKILL.md",
                    ".claude/skills/broken/SKILL.md has frontmatter that doesn't parse, so Claude Code lists the skill with its first line in place of a description.",
                    "Fix the YAML between the --- lines, and give the skill a description."),
            ],
            Check(vault).Where(finding => finding.Id == "skill-frontmatter-invalid"));
    }

    [Fact]
    public void Skill_paths_match_nothing_names_the_patterns()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/writing/SKILL.md", Skill("The docs.", "paths: \"docs/**\"\n"))
            .Write("repo/src/app.ts", "export const a = 1;\n");

        Assert.Equal(
            Finding(
                "skill-paths-match-nothing", ".claude/skills/writing/SKILL.md",
                ".claude/skills/writing/SKILL.md applies to docs/**, which matches no file in the repo, so Claude Code never lists it.",
                "Fix the patterns. They match paths from the launch directory, the way .gitignore lines do."),
            Single(vault, "skill-paths-match-nothing"));
    }

    [Fact]
    public void Hook_never_runs_names_the_reason_in_each_harness()
    {
        using var vault = Repo()
            .Write("repo/.claude/settings.json", """
                { "hooks": {
                  "SessionStart": [ { "hooks": [ { "type": "command", "command": "start", "if": "Edit(src/**)" } ] } ],
                  "PreToolUse": [ { "matcher": "Edit(", "hooks": [ { "type": "command", "command": "check" } ] } ] } }
                """)
            .Write("home/.codex/hooks.json", """{ "hooks": { "SessionStart": [ { "hooks": [ { "type": "prompt", "prompt": "Say hi." } ] } ] } }""");

        Assert.Equal(
            [
                Finding(
                    "hook-never-runs", ".claude/settings.json",
                    ".claude/settings.json gives a SessionStart hook the condition if Edit(src/**), and Claude Code reads if only on tool events, so the hook never runs.",
                    "Remove the if, or move the hook to PreToolUse or PostToolUse."),
                Finding(
                    "hook-never-runs", ".claude/settings.json",
                    ".claude/settings.json has a PreToolUse hook whose matcher, Edit(, isn't a valid regex, so it never runs.",
                    "Fix the regex, or list exact names such as Edit|Write."),
                Finding(
                    "hook-never-runs", "~/.codex/hooks.json",
                    "~/.codex/hooks.json has a SessionStart hook of type prompt, which Codex skips, so it never runs.",
                    "Rewrite it as a command hook."),
            ],
            Check(vault).Where(finding => finding.Id == "hook-never-runs"));
    }

    [Fact]
    public void Codex_hook_untrusted_counts_the_hooks_in_each_file()
    {
        using var vault = Repo()
            .Write("home/.codex/hooks.json", """{ "hooks": { "SessionStart": [ { "hooks": [ { "type": "command", "command": "a" } ] }, { "hooks": [ { "type": "command", "command": "b" } ] } ] } }""")
            .Write("repo/.codex/hooks.json", """{ "hooks": { "SessionStart": [ { "hooks": [ { "type": "command", "command": "c" } ] } ] } }""");

        Assert.Equal(
            [
                Finding(
                    "codex-hook-untrusted", ".codex/hooks.json",
                    "Codex doesn't load .codex/hooks.json, because the project isn't trusted, so its 1 hook never runs.",
                    "Trust the project in Codex, which sets trust_level = \"trusted\" for it in ~/.codex/config.toml."),
                Finding(
                    "codex-hook-untrusted", "~/.codex/hooks.json",
                    "~/.codex/hooks.json has 2 hooks Codex hasn't trusted, so they never run.",
                    "Review the hooks in Codex and trust them, which records each one's hash under [hooks.state] in ~/.codex/config.toml."),
            ],
            Check(vault).Where(finding => finding.Id == "codex-hook-untrusted"));
    }

    [Fact]
    public void A_hook_that_changed_since_it_was_trusted_says_so()
    {
        using var vault = Repo().Write("home/.codex/hooks.json", """{ "hooks": { "SessionStart": [ { "hooks": [ { "type": "command", "command": "a" } ] } ] } }""");
        var key = Path.Combine(vault.Root, "home", ".codex", "hooks.json").Replace('\\', '/') + ":session_start:0:0";
        vault.Write("home/.codex/config.toml", $"[hooks.state.\"{key}\"]\ntrusted_hash = \"sha256:0\"\n");

        Assert.Equal(
            Finding(
                "codex-hook-untrusted", "~/.codex/hooks.json",
                "~/.codex/hooks.json has 1 hook that changed since Codex trusted it, so it doesn't run.",
                "Review the change in Codex and trust the hook again."),
            Single(vault, "codex-hook-untrusted"));
    }
}
