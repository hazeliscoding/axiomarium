namespace Axiomarium.Core.Instructions;

/// <summary>Where the harnesses keep their user-level files. Injected, so tests never read the real machine.</summary>
/// <param name="Home">The user's home folder, for <c>~</c> in paths.</param>
/// <param name="CodexHome">Codex's folder: <c>$CODEX_HOME</c>, or <c>~/.codex</c>.</param>
/// <param name="ClaudeConfig">Claude Code's folder: <c>$CLAUDE_CONFIG_DIR</c>, or <c>~/.claude</c>.</param>
public sealed record Machine(string Home, string CodexHome, string ClaudeConfig);

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

/// <summary>An instruction file a harness puts into the model's context.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Scope">Whose instructions they are.</param>
/// <param name="Timing">When it loads.</param>
/// <param name="Rule">The rule that loads it.</param>
/// <param name="Bytes">How many bytes of it the model sees, which is fewer than the file's when <paramref name="Cut"/>.</param>
/// <param name="Cut">Whether the harness cut it short, such as at a byte budget.</param>
public sealed record LoadedInstruction(string Path, InstructionScope Scope, LoadTiming Timing, HarnessRule Rule, int Bytes, bool Cut = false);

/// <summary>An instruction file that exists but doesn't reach the model.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Rule">The rule that drops it.</param>
public sealed record DroppedInstruction(string Path, HarnessRule Rule);

/// <summary>What a harness loads for a file, and what it drops.</summary>
/// <param name="Loaded">The files the model sees, in context order.</param>
/// <param name="Dropped">The files that exist but don't reach the model.</param>
public sealed record Resolution(IReadOnlyList<LoadedInstruction> Loaded, IReadOnlyList<DroppedInstruction> Dropped);
