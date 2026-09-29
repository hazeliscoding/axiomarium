namespace Axiomarium.Core.Instructions;

/// <summary>The rules Claude Code runs hooks by, or doesn't.</summary>
public static class ClaudeCodeHookRules
{
    private const string Hooks = "https://code.claude.com/docs/en/hooks";

    /// <summary>A hook in managed settings.</summary>
    public static HarnessRule ManagedHook { get; } = new(
        "claude-code/managed-hook", "managed", "Hooks in managed settings run for every user, and only managed settings can turn them off.", Hooks + "#hook-locations");

    /// <summary>A hook in the user's settings.</summary>
    public static HarnessRule UserHook { get; } = new(
        "claude-code/user-hook", "user", "Hooks in ~/.claude/settings.json run in every project.", Hooks + "#hook-locations");

    /// <summary>A hook in the project's settings.</summary>
    public static HarnessRule ProjectHook { get; } = new(
        "claude-code/project-hook", "project", "Hooks in .claude/settings.json in the launch directory run, and settings in the folders above it don't count.", Hooks + "#hook-locations");

    /// <summary>A hook in the local settings.</summary>
    public static HarnessRule LocalHook { get; } = new(
        "claude-code/local-hook", "local", "Hooks in .claude/settings.local.json run too. On Windows it's read from the launch directory, as the recordings show.", Hooks + "#hook-locations");

    /// <summary>A hook from an enabled plugin.</summary>
    public static HarnessRule PluginHook { get; } = new(
        "claude-code/plugin-hook", "plugin", "An enabled plugin's hooks/hooks.json, and the hooks its manifest names, run when the session loads the plugin.", Hooks + "#hook-locations");

    /// <summary>An <c>if</c> that doesn't match the tool call.</summary>
    public static HarnessRule IfNoMatch { get; } = new(
        "claude-code/hook-if-no-match", "if doesn't match", "The hook's if permission rule doesn't match this tool call, so the hook doesn't run. A pattern such as src/** is anchored at the launch directory, and a bare name matches at any depth.", Hooks + "#common-fields");

    /// <summary>An <c>if</c> on an event that isn't a tool event.</summary>
    public static HarnessRule IfIgnored { get; } = new(
        "claude-code/hook-if-ignored", "if on an event that isn't a tool event", "if only works on PreToolUse, PostToolUse, PostToolUseFailure, PermissionRequest and PermissionDenied. On any other event a hook with if never runs.", Hooks + "#common-fields");

    /// <summary>The same handler in two settings files.</summary>
    public static HarnessRule Duplicate { get; } = new(
        "claude-code/hook-duplicate", "same handler, runs once", "The same handler in two settings files runs once, as the one listed first.", Hooks + "#hook-locations");

    /// <summary>A matcher that isn't a valid regex.</summary>
    public static HarnessRule MatcherInvalid { get; } = new(
        "claude-code/hook-matcher-invalid", "matcher isn't a valid regex", "A matcher of only letters, digits, _, -, spaces, commas and | is an exact list. Anything else is a regex, and one that doesn't compile matches nothing.", Hooks + "#matcher-patterns");

    /// <summary>Hooks turned off by <c>disableAllHooks</c>.</summary>
    public static HarnessRule HooksDisabled { get; } = new(
        "claude-code/hooks-disabled", "disableAllHooks", "disableAllHooks turns hooks off: outside managed settings every hook but the managed ones, in managed settings all of them.", Hooks + "#disable-or-remove-hooks");

    /// <summary>Only managed hooks allowed.</summary>
    public static HarnessRule ManagedHooksOnly { get; } = new(
        "claude-code/managed-hooks-only", "allowManagedHooksOnly", "allowManagedHooksOnly in managed settings lets only managed hooks run.", Hooks + "#disable-or-remove-hooks");

    /// <summary>Only plugin hooks allowed.</summary>
    public static HarnessRule PluginHooksOnly { get; } = new(
        "claude-code/plugin-hooks-only", "strictPluginOnlyCustomization", "strictPluginOnlyCustomization listing hooks in managed settings stops hooks in user, project and local settings.", Hooks + "#disable-or-remove-hooks");

    /// <summary>Every Claude Code hook rule.</summary>
    public static IReadOnlyList<HarnessRule> All { get; } =
    [
        ManagedHook, UserHook, ProjectHook, LocalHook, PluginHook, IfNoMatch, IfIgnored, Duplicate, MatcherInvalid, HooksDisabled, ManagedHooksOnly,
        PluginHooksOnly,
    ];
}
