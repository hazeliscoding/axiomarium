namespace Axiomarium.Core.Instructions;

/// <summary>The rules Claude Code loads CLAUDE files, rules and imports by.</summary>
public static class ClaudeCodeRules
{
    private const string Memory = "https://code.claude.com/docs/en/memory";

    /// <summary>The managed-policy CLAUDE.md.</summary>
    public static HarnessRule ManagedMemory { get; } = new(
        "claude-code/managed-memory", "managed", "The managed-policy CLAUDE.md, set by an administrator, loads first.", Memory + "#choose-where-to-put-claude-md-files");

    /// <summary>The user's CLAUDE.md.</summary>
    public static HarnessRule UserMemory { get; } = new(
        "claude-code/user-memory", "user", "~/.claude/CLAUDE.md loads for every project.", Memory + "#choose-where-to-put-claude-md-files");

    /// <summary>The user's rules without paths.</summary>
    public static HarnessRule UserRule { get; } = new(
        "claude-code/user-rule", "user rule", "Rules in ~/.claude/rules/ without paths load at launch, before any project file.", Memory + "#user-level-rules");

    /// <summary>CLAUDE files from the root down to the launch directory.</summary>
    public static HarnessRule AncestorMemory { get; } = new(
        "claude-code/ancestor-memory", "project", "CLAUDE.md and .claude/CLAUDE.md load from every directory from the filesystem root down to the launch directory.", Memory + "#how-claude-md-files-load");

    /// <summary>A directory's rules without paths.</summary>
    public static HarnessRule AncestorRule { get; } = new(
        "claude-code/ancestor-rule", "rule", "A directory's .claude/rules/ without paths load right after that directory's CLAUDE files.", Memory + "#organize-rules-with-claude-rules");

    /// <summary>CLAUDE.local.md.</summary>
    public static HarnessRule LocalMemory { get; } = new(
        "claude-code/local-memory", "local", "CLAUDE.local.md loads after its directory's CLAUDE files and rules.", Memory + "#how-claude-md-files-load");

    /// <summary>CLAUDE files below the launch directory.</summary>
    public static HarnessRule NestedMemory { get; } = new(
        "claude-code/nested-memory", "nested", "CLAUDE files in directories below the launch directory load when the agent reads a file there.", Memory + "#how-claude-md-files-load");

    /// <summary>An <c>@path</c> import.</summary>
    public static HarnessRule Import { get; } = new(
        "claude-code/import", "import", "An @path import loads right after the file that imports it, depth first, with that file's scope.", Memory + "#import-additional-files");

    /// <summary>An import whose file doesn't exist.</summary>
    public static HarnessRule MissingImport { get; } = new(
        "claude-code/missing-import", "file is missing", "An import whose file doesn't exist loads nothing. The path runs to the next space, so trailing punctuation is part of it.", Memory + "#import-additional-files");

    /// <summary>An import past four hops.</summary>
    public static HarnessRule ImportTooDeep { get; } = new(
        "claude-code/import-too-deep", "past four import hops", "Imports stop after four hops, so a file five imports away never loads.", Memory + "#import-additional-files");

    /// <summary>A project import outside the working directory.</summary>
    public static HarnessRule ExternalImport { get; } = new(
        "claude-code/external-import", "outside the launch directory, needs approval", "A project file's import that resolves outside the launch directory waits for approval. Interactive sessions ask once; headless sessions never load it.", Memory + "#import-additional-files");

    /// <summary>A rule without paths in a folder below the launch directory.</summary>
    public static HarnessRule NestedRule { get; } = new(
        "claude-code/nested-rule", "nested rule", "Rules without paths in a .claude/rules/ below the launch directory load when the agent reads a file there.", Memory + "#organize-rules-with-claude-rules");

    /// <summary>A path-scoped rule whose patterns match the file.</summary>
    public static HarnessRule PathRule { get; } = new(
        "claude-code/path-rule", "paths", "A rule with paths loads when the agent reads a file they match. Patterns are relative to the folder that holds .claude/, or to the launch directory for user rules.", Memory + "#path-specific-rules");

    /// <summary>A path-scoped rule whose patterns don't match the file.</summary>
    public static HarnessRule PathRuleNoMatch { get; } = new(
        "claude-code/path-rule-no-match", "paths don't match", "A rule whose paths don't match the file doesn't load for it.", Memory + "#path-specific-rules");

    /// <summary>A rule whose frontmatter doesn't parse.</summary>
    public static HarnessRule InvalidFrontmatter { get; } = new(
        "claude-code/invalid-frontmatter", "frontmatter doesn't parse", "A rule whose frontmatter doesn't parse never loads. The docs say it loads for every file, but Claude Code 2.1.283 skips it.", Memory + "#rule-frontmatter-reference");

    /// <summary>AGENTS.md in the launch directory and above.</summary>
    public static HarnessRule AgentsMd { get; } = new(
        "claude-code/agents-md", "project", "AGENTS.md and .claude/AGENTS.md load from the launch directory and every directory above it, after that directory's CLAUDE files, when the Project instructions setting allows them.", Memory + "#agents-md");

    /// <summary>A nested AGENTS.md, on read.</summary>
    public static HarnessRule NestedAgentsMd { get; } = new(
        "claude-code/nested-agents-md", "nested", "A subdirectory's AGENTS.md loads when the agent reads a file there, ahead of that read's other files, through the built-in AGENTS.md plugin. By default only in a folder with no CLAUDE file of its own.", Memory + "#agents-md");

    /// <summary>AGENTS.md hidden by a CLAUDE file.</summary>
    public static HarnessRule AgentsMdHidden { get; } = new(
        "claude-code/agents-md-hidden", "a CLAUDE file exists and doesn't import it", "By default AGENTS.md loads only when no CLAUDE.md, .claude/CLAUDE.md or CLAUDE.local.md is in the launch directory or above, so one such file turns every AGENTS.md off, nested ones too.", Memory + "#choose-which-instruction-files-load");

    /// <summary>AGENTS.md turned off by the setting.</summary>
    public static HarnessRule AgentsMdOff { get; } = new(
        "claude-code/agents-md-off", "Project instructions is claude-md", "The Project instructions setting is claude-md, so AGENTS.md never loads.", Memory + "#choose-which-instruction-files-load");

    /// <summary>Files left out by managed-only.</summary>
    public static HarnessRule ManagedOnly { get; } = new(
        "claude-code/managed-only", "Project instructions is managed-only", "The Project instructions setting is managed-only: at launch only the managed file loads, and on read only files below the launch directory and path rules.", Memory + "#choose-which-instruction-files-load");

    /// <summary>A file claudeMdExcludes matches.</summary>
    public static HarnessRule Excluded { get; } = new(
        "claude-code/excluded", "claudeMdExcludes", "A claudeMdExcludes pattern matches the file's absolute path, so it doesn't load. The managed file can't be excluded.", Memory + "#exclude-specific-claude-md-files");

    /// <summary>A file over 4 MiB.</summary>
    public static HarnessRule TooLarge { get; } = new(
        "claude-code/too-large", "over 4 MiB", "Claude Code skips a file over 4 MiB.", Memory + "#my-claude-md-is-too-large");

    /// <summary>Every Claude Code rule.</summary>
    public static IReadOnlyList<HarnessRule> All { get; } =
    [
        ManagedMemory, UserMemory, UserRule, AncestorMemory, AncestorRule, LocalMemory, NestedMemory, Import, MissingImport, ImportTooDeep,
        ExternalImport, NestedRule, PathRule, PathRuleNoMatch, InvalidFrontmatter, AgentsMd, NestedAgentsMd, AgentsMdHidden, AgentsMdOff,
        ManagedOnly, Excluded, TooLarge,
    ];
}
