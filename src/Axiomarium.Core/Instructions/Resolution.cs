namespace Axiomarium.Core.Instructions;

/// <summary>Where the harnesses keep their user-level files. Injected, so tests never read the real machine.</summary>
/// <param name="Home">The user's home folder, for <c>~</c> in paths.</param>
/// <param name="CodexHome">Codex's folder: <c>$CODEX_HOME</c>, or <c>~/.codex</c>.</param>
/// <param name="ClaudeConfig">Claude Code's folder: <c>$CLAUDE_CONFIG_DIR</c>, or <c>~/.claude</c>.</param>
/// <param name="ClaudeManaged">The folder that holds Claude Code's managed-policy <c>CLAUDE.md</c>, which differs by OS.</param>
/// <param name="FileSystemRoot">Where upward walks stop: the real filesystem root, or a test's own folder.</param>
public sealed record Machine(string Home, string CodexHome, string ClaudeConfig, string ClaudeManaged, string FileSystemRoot);

/// <summary>Whose instructions a file holds, as the harness labels them.</summary>
public enum InstructionScope
{
    /// <summary>Set by an administrator for every user on the machine.</summary>
    Managed,

    /// <summary>The user's own, for every project: Codex's global file, or Claude Code's user memory.</summary>
    User,

    /// <summary>The project's, shared through the repo.</summary>
    Project,

    /// <summary>The user's, for this project only, such as <c>CLAUDE.local.md</c>.</summary>
    Local,
}

/// <summary>When a harness puts a file into the model's context.</summary>
public enum LoadTiming
{
    /// <summary>When the session starts, from the launch directory.</summary>
    AtLaunch,

    /// <summary>When the agent reads the target file.</summary>
    OnRead,
}

/// <summary>A loading rule of a harness: why a file loads, or why it doesn't.</summary>
/// <param name="Id">Stable and kebab-case after the harness, such as <c>codex/byte-budget</c>.</param>
/// <param name="Summary">The rule, in a sentence.</param>
/// <param name="Source">The doc or source code the rule comes from.</param>
public sealed record HarnessRule(string Id, string Summary, string Source);

/// <summary>Where a file is imported from.</summary>
/// <param name="File">The absolute path of the file that holds the import.</param>
/// <param name="Line">The import's line in that file, from 1.</param>
public sealed record ImportSite(string File, int Line);

/// <summary>An instruction file a harness puts into the model's context.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Scope">Whose instructions they are. An imported file takes the scope of the file that imports it.</param>
/// <param name="Timing">When it loads.</param>
/// <param name="Rule">The rule that loads it.</param>
/// <param name="Bytes">How many bytes of it the model sees, which is fewer than the file's when <paramref name="Cut"/>.</param>
/// <param name="Cut">Whether the harness cut it short, such as at a byte budget.</param>
/// <param name="Via">Where it was imported from, when an import loaded it.</param>
public sealed record LoadedInstruction(string Path, InstructionScope Scope, LoadTiming Timing, HarnessRule Rule, int Bytes, bool Cut = false, ImportSite? Via = null);

/// <summary>An instruction file that doesn't reach the model, including an import whose file doesn't exist.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Rule">The rule that drops it.</param>
/// <param name="Via">Where it was imported from, when it's an import.</param>
public sealed record DroppedInstruction(string Path, HarnessRule Rule, ImportSite? Via = null);

/// <summary>What a harness loads for a file, and what it drops.</summary>
/// <param name="Loaded">The files the model sees, in context order.</param>
/// <param name="Dropped">The files that exist but don't reach the model.</param>
public sealed record Resolution(IReadOnlyList<LoadedInstruction> Loaded, IReadOnlyList<DroppedInstruction> Dropped);
