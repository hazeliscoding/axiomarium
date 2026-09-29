namespace Axiomarium.Core.Instructions;

/// <summary>The rules Codex runs hooks by, or doesn't.</summary>
public static class CodexHookRules
{
    private const string Guide = "https://developers.openai.com/codex/hooks";
    private const string Discovery = "https://github.com/openai/codex/blob/main/codex-rs/hooks/src/engine/discovery.rs";
    private const string Matchers = "https://github.com/openai/codex/blob/main/codex-rs/hooks/src/events/common.rs";

    /// <summary>A hook in the system folder.</summary>
    public static HarnessRule AdminHook { get; } = new(
        "codex/admin-hook", "admin, managed", "Hooks in the system folder (/etc/codex, or %ProgramData%\\OpenAI\\Codex on Windows), in hooks.json, config.toml or requirements.toml, are managed: they run without being trusted.", Discovery);

    /// <summary>A hook in CODEX_HOME.</summary>
    public static HarnessRule UserHook { get; } = new(
        "codex/user-hook", "user", "Hooks in $CODEX_HOME's hooks.json and config.toml run in every project, once trusted.", Guide);

    /// <summary>A hook in a project's <c>.codex</c>.</summary>
    public static HarnessRule ProjectHook { get; } = new(
        "codex/project-hook", "project", "Hooks in .codex/hooks.json and .codex/config.toml, in each directory from the project root down to the launch directory, run once the project and the hook are trusted.", Guide);

    /// <summary>An edit hook, which can't be scoped to a file.</summary>
    public static HarnessRule EveryEdit { get; } = new(
        "codex/every-edit", "runs on every edit", "An edit hook matches apply_patch, which Codex also names Write and Edit, and runs on every edit: Codex gives a hook the whole patch, so no hook can be scoped to one file.", Discovery);

    /// <summary>A hook that was never trusted.</summary>
    public static HarnessRule Untrusted { get; } = new(
        "codex/hook-untrusted", "not trusted", "A hook that isn't managed runs only once the user config's [hooks.state] holds its trusted_hash, which Codex writes when the user trusts it.", Discovery);

    /// <summary>A hook that changed since it was trusted.</summary>
    public static HarnessRule Modified { get; } = new(
        "codex/hook-modified", "changed since it was trusted", "A hook whose hash no longer matches its trusted_hash has changed since it was trusted, and doesn't run until it's trusted again.", Discovery);

    /// <summary>A hook turned off in the user config.</summary>
    public static HarnessRule Disabled { get; } = new(
        "codex/hook-disabled", "enabled = false", "[hooks.state] in the user config turns the hook off with enabled = false.", Discovery);

    /// <summary>A hook in an untrusted project.</summary>
    public static HarnessRule ProjectUntrusted { get; } = new(
        "codex/hook-project-untrusted", "the project isn't trusted", "A project's .codex doesn't load until the user config marks the project trusted, so its hooks never run.", Guide);

    /// <summary>A handler type Codex skips.</summary>
    public static HarnessRule HandlerSkipped { get; } = new(
        "codex/hook-handler-skipped", "handler type not supported", "Codex runs command and mcp_tool handlers, and skips prompt and agent ones with a warning.", Discovery);

    /// <summary>A matcher that isn't a valid regex.</summary>
    public static HarnessRule MatcherInvalid { get; } = new(
        "codex/hook-matcher-invalid", "matcher isn't a valid regex", "A matcher of only letters, digits, _ and | is an exact list, which the docs don't say. Anything else is a regex, and one that doesn't compile is skipped.", Matchers);

    /// <summary>Every Codex hook rule.</summary>
    public static IReadOnlyList<HarnessRule> All { get; } =
        [AdminHook, UserHook, ProjectHook, EveryEdit, Untrusted, Modified, Disabled, ProjectUntrusted, HandlerSkipped, MatcherInvalid];
}
