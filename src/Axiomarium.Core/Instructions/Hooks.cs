namespace Axiomarium.Core.Instructions;

/// <summary>The moments <c>axm explain</c> looks at hooks for, the same three as the vault's hook schema.</summary>
public enum HookMoment
{
    /// <summary>When a session starts fresh.</summary>
    SessionStart,

    /// <summary>Before the agent edits the file.</summary>
    BeforeEdit,

    /// <summary>After the agent edits the file.</summary>
    AfterEdit,
}

/// <summary>A hook handler a harness has configured, from one source file.</summary>
/// <param name="Path">The file that declares it, such as <c>.claude/settings.json</c> or <c>hooks.json</c>.</param>
/// <param name="Event">The harness's event, such as <c>PreToolUse</c>.</param>
/// <param name="Matcher">Its matcher, or <see langword="null"/> when it has none and matches everything.</param>
/// <param name="Handler">What it runs: a command, a URL, an MCP tool, or the handler's type when it has none of those.</param>
/// <param name="Condition">Claude Code's <c>if</c> permission rule, or <see langword="null"/>.</param>
/// <param name="Source">The rule for where it comes from, such as <c>claude-code/project-hook</c>.</param>
public sealed record ConfiguredHook(string Path, string Event, string? Matcher, string Handler, string? Condition, HarnessRule Source)
{
    /// <summary>Why the hook can never run, for any file, or <see langword="null"/> when it can.</summary>
    public HarnessRule? Blocked { get; init; }

    /// <summary>Codex's trust in it: <c>trusted</c>, <c>untrusted</c>, <c>modified</c> or <c>managed</c>. <see langword="null"/> for Claude Code.</summary>
    public string? Trust { get; init; }

    /// <summary>The hash Codex trusts it by, <c>sha256:</c> and hex. <see langword="null"/> for Claude Code.</summary>
    public string? Hash { get; init; }
}

/// <summary>Whether a configured hook runs at one moment for the file.</summary>
/// <param name="Moment">The moment.</param>
/// <param name="Hook">The hook.</param>
/// <param name="Runs">Whether it runs.</param>
/// <param name="Rule">The rule that decides it: where the hook comes from when it runs, or why it doesn't.</param>
/// <param name="Input">What its matcher was tested against: <c>startup</c>, or the tool the agent edits with.</param>
public sealed record MomentHook(HookMoment Moment, ConfiguredHook Hook, bool Runs, HarnessRule Rule, string Input);
