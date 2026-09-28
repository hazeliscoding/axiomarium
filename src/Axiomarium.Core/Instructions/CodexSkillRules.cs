namespace Axiomarium.Core.Instructions;

/// <summary>The rules Codex lists skills by, or keeps them out of the listing.</summary>
public static class CodexSkillRules
{
    private const string Guide = "https://developers.openai.com/codex/skills";
    private const string Roots = "https://github.com/openai/codex/blob/main/codex-rs/ext/skills/src/host_roots.rs";
    private const string Render = "https://github.com/openai/codex/blob/main/codex-rs/ext/skills/src/render.rs";
    private const string Parser = "https://github.com/openai/codex/blob/main/codex-rs/skills/src/parser.rs";

    /// <summary>The bundled skills.</summary>
    public static HarnessRule BundledSkill { get; } = new(
        "codex/bundled-skill", "bundled", "Codex's bundled skills, which it extracts into $CODEX_HOME/skills/.system, are listed first.", Roots);

    /// <summary>Skills in the system folder.</summary>
    public static HarnessRule AdminSkill { get; } = new(
        "codex/admin-skill", "admin", "Skills in the system folder's skills/ (/etc/codex, or %ProgramData%\\OpenAI\\Codex on Windows) are listed after the bundled ones.", Roots);

    /// <summary>A project's <c>.codex/skills</c>.</summary>
    public static HarnessRule ProjectSkill { get; } = new(
        "codex/project-skill", "project .codex", ".codex/skills in each directory from the project root down to the launch directory is listed, even in an untrusted project, which the docs say skips project .codex folders.", Roots);

    /// <summary>A repo's <c>.agents/skills</c>.</summary>
    public static HarnessRule RepoSkill { get; } = new(
        "codex/repo-skill", "repo", ".agents/skills in each directory from the project root down to the launch directory is listed.", Guide);

    /// <summary>The user's <c>~/.agents/skills</c>.</summary>
    public static HarnessRule UserSkill { get; } = new(
        "codex/user-skill", "user", "~/.agents/skills is listed in every project.", Guide);

    /// <summary>The old user skills folder.</summary>
    public static HarnessRule CodexHomeSkill { get; } = new(
        "codex/codex-home-skill", "user, old folder", "$CODEX_HOME/skills, where user skills used to live, is still listed.", Roots);

    /// <summary>A skill Codex can't read.</summary>
    public static HarnessRule Invalid { get; } = new(
        "codex/skill-invalid", "SKILL.md can't be read", "A SKILL.md without frontmatter, whose YAML doesn't parse even after Codex quotes values holding \": \", without a description, or with a name over 64 characters is skipped.", Parser);

    /// <summary>A skill turned off in config.</summary>
    public static HarnessRule Disabled { get; } = new(
        "codex/skill-disabled", "turned off in [[skills.config]]", "A [[skills.config]] entry in the user config with enabled = false, matching the skill's SKILL.md path or its name, turns it off.", Guide);

    /// <summary>A skill the model may not invoke on its own.</summary>
    public static HarnessRule ImplicitOff { get; } = new(
        "codex/implicit-invocation-off", "allow_implicit_invocation: false", "A skill whose agents/openai.yaml sets policy.allow_implicit_invocation: false isn't listed. A $name mention still runs it.", Guide);

    /// <summary>A skill for another product.</summary>
    public static HarnessRule OtherProduct { get; } = new(
        "codex/other-product", "for another product", "A skill whose agents/openai.yaml lists policy.products without codex isn't loaded in Codex.", Guide);

    /// <summary>Bundled skills turned off.</summary>
    public static HarnessRule BundledOff { get; } = new(
        "codex/bundled-off", "skills.bundled.enabled is false", "skills.bundled.enabled = false turns Codex's bundled skills off.", Guide);

    /// <summary>The listing left out of the prompt.</summary>
    public static HarnessRule ListingOff { get; } = new(
        "codex/skills-listing-off", "skills.include_instructions is false", "With skills.include_instructions = false, the prompt has no skill listing.", Guide);

    /// <summary>The listing's budget and each description's cap.</summary>
    public static HarnessRule ListingBudget { get; } = new(
        "codex/skill-listing-budget", "listing budget", "The listing gets skills.max_context_tokens (at most 10,000), else 2% of the model's context window, else 8,000 characters, counting a token as 4 bytes. Each description is cut at 1,024 characters. Over budget, descriptions shrink in turn, then skills are dropped from the end.", Render);

    /// <summary>Every Codex skill rule.</summary>
    public static IReadOnlyList<HarnessRule> All { get; } =
    [
        BundledSkill, AdminSkill, ProjectSkill, RepoSkill, UserSkill, CodexHomeSkill, Invalid, Disabled, ImplicitOff, OtherProduct, BundledOff,
        ListingOff, ListingBudget,
    ];
}
