namespace Axiomarium.Core.Instructions;

/// <summary>The rules Claude Code lists skills by, or keeps them out of the listing.</summary>
public static class ClaudeCodeSkillRules
{
    private const string Skills = "https://code.claude.com/docs/en/skills";

    /// <summary>Skills in the managed settings folder.</summary>
    public static HarnessRule ManagedSkill { get; } = new(
        "claude-code/managed-skill", "managed", "Skills in the managed settings folder's .claude/skills/ are listed for every user, ahead of any other skill with the same name.", Skills + "#where-skills-live");

    /// <summary>The user's own skills.</summary>
    public static HarnessRule PersonalSkill { get; } = new(
        "claude-code/personal-skill", "personal", "~/.claude/skills/<folder>/SKILL.md is listed in every project, by its folder name.", Skills + "#where-skills-live");

    /// <summary>Project skills from the launch directory up to the repository root.</summary>
    public static HarnessRule ProjectSkill { get; } = new(
        "claude-code/project-skill", "project", ".claude/skills/ in the launch directory and in every directory above it up to the repository root is listed at launch, by folder name.", Skills + "#discovery-from-parent-and-nested-directories");

    /// <summary>Skills below the launch directory.</summary>
    public static HarnessRule NestedSkill { get; } = new(
        "claude-code/nested-skill", "nested", "A .claude/skills/ below the launch directory joins the listing the first time the agent reads or edits a file there, saying which folder it applies to. A name another skill already has gets the folder as a prefix, such as apps/web:deploy.", Skills + "#discovery-from-parent-and-nested-directories");

    /// <summary>A skill whose <c>paths</c> match the file.</summary>
    public static HarnessRule PathsSkill { get; } = new(
        "claude-code/paths-skill", "paths", "A skill with paths joins the listing when the agent reads, writes or edits a file they match, relative to the launch directory.", Skills + "#frontmatter-reference");

    /// <summary>A skill whose <c>paths</c> don't match the file.</summary>
    public static HarnessRule PathsSkillNoMatch { get; } = new(
        "claude-code/paths-skill-no-match", "paths don't match", "A skill whose paths don't match the file doesn't join the listing for it.", Skills + "#frontmatter-reference");

    /// <summary>A command file, listed like a skill.</summary>
    public static HarnessRule Command { get; } = new(
        "claude-code/command", "command", "A file in .claude/commands/ is listed like a skill, named by its path with a colon for each folder.", Skills + "#how-a-skill-gets-its-command-name");

    /// <summary>An enabled plugin's skills and commands.</summary>
    public static HarnessRule PluginSkill { get; } = new(
        "claude-code/plugin-skill", "plugin", "An enabled plugin's skills and commands are listed as plugin:name, after the commands. A plugin from a marketplace in a local folder loads in place; any other loads from its installed copy.", Skills + "#where-skills-live");

    /// <summary>A plugin skill without a description.</summary>
    public static HarnessRule PluginNoDescription { get; } = new(
        "claude-code/plugin-skill-no-description", "plugin skill without a description", "A plugin skill with neither description nor when_to_use isn't listed. The spike on Claude Code 2.1.284 showed it.", Skills + "#frontmatter-reference");

    /// <summary>A skill synced from the claude.ai account.</summary>
    public static HarnessRule SyncedSkill { get; } = new(
        "claude-code/synced-skill", "claude.ai", "Skills enabled on the claude.ai account sync into ~/.claude/skills/synced/ and are listed as anthropic-skills:name after the built-in skills, unless syncClaudeAiSkills is false. Recordings turn the sync off, so this follows the docs and the spike on 2.1.284.", Skills + "#where-synced-skills-load");

    /// <summary>A skill built into Claude Code.</summary>
    public static HarnessRule BuiltIn { get; } = new(
        "claude-code/built-in-skill", "built in", "Claude Code lists its built-in skills in every session. They aren't files: their names and sizes are recorded from Claude Code 2.1.284.", Skills + "#bundled-skills");

    /// <summary>A skill another skill with the same name hides.</summary>
    public static HarnessRule Shadowed { get; } = new(
        "claude-code/skill-shadowed", "another skill has its name", "Of two skills with one name only one is listed: managed beats personal, personal beats project, a skill beats a command, and your skill replaces a built-in one.", Skills + "#resolve-skills-that-share-a-name");

    /// <summary>A skill the model may not invoke.</summary>
    public static HarnessRule ModelInvocationOff { get; } = new(
        "claude-code/model-invocation-off", "disable-model-invocation", "A skill with disable-model-invocation: true isn't listed, so only the user can run it.", Skills + "#control-who-invokes-a-skill");

    /// <summary>A skill <c>skillOverrides</c> hides.</summary>
    public static HarnessRule Override { get; } = new(
        "claude-code/skill-override", "skillOverrides", "skillOverrides set to off or user-invocable-only keeps a skill out of the listing. It never applies to plugin skills.", Skills + "#override-skill-visibility-from-settings");

    /// <summary>Built-in skills turned off.</summary>
    public static HarnessRule BuiltInsOff { get; } = new(
        "claude-code/built-ins-off", "disableBundledSkills", "disableBundledSkills turns Claude Code's built-in skills off.", Skills + "#bundled-skills");

    /// <summary>The listing's budget and each entry's cap.</summary>
    public static HarnessRule ListingBudget { get; } = new(
        "claude-code/skill-listing-budget", "listing budget", "The listing gets 1% of the context window at 4 characters a token (skillListingBudgetFraction), and each entry's text is cut at 1,536 characters (skillListingMaxDescChars). Over budget, some descriptions are dropped, starting with the skills used least.", Skills + "#skill-descriptions-are-cut-short");

    /// <summary>Every Claude Code skill rule.</summary>
    public static IReadOnlyList<HarnessRule> All { get; } =
    [
        ManagedSkill, PersonalSkill, ProjectSkill, NestedSkill, PathsSkill, PathsSkillNoMatch, Command, PluginSkill, PluginNoDescription, SyncedSkill,
        BuiltIn, Shadowed, ModelInvocationOff, Override, BuiltInsOff, ListingBudget,
    ];
}
