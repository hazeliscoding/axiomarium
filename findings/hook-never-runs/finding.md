# hook-never-runs

**Severity:** warning · **Harness:** Claude Code and Codex

## What happens

A hook is set up so that the harness never runs it:

- In Claude Code, a hook with `if` on an event that isn't a tool event, such as `SessionStart`. `if` works only on `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `PermissionRequest` and `PermissionDenied`.
- In either harness, a matcher that isn't a valid regex. A matcher of only letters, digits, `_` and `|` (in Claude Code also `-`, spaces and commas) is a list of exact names. Anything else is a regex, and one that doesn't compile matches nothing.
- In Codex, a `prompt` or `agent` handler, which Codex skips with a warning. It runs only `command` and `mcp_tool` handlers.

```text
WARNING  hook-never-runs
         .claude/settings.json gives a SessionStart hook the condition if Edit(src/**), and Claude Code reads if only on tool events, so the hook never runs.
         Fix: Remove the if, or move the hook to PreToolUse or PostToolUse.
```

Plugin and managed hooks aren't reported, because they're someone else's to fix.

## Why it matters

The hook looks set up, so a check you rely on, such as a linter after each edit, silently never happens.

## Fix

Remove the `if` or move the hook to a tool event, fix the matcher's regex or list exact names such as `Edit|Write`, and rewrite a Codex `prompt` hook as a `command` hook.

## Source

[Claude Code hooks: Common fields](https://code.claude.com/docs/en/hooks#common-fields) and [Matcher patterns](https://code.claude.com/docs/en/hooks#matcher-patterns), the [Codex hooks guide](https://developers.openai.com/codex/hooks), and Codex's [`common.rs`](https://github.com/openai/codex/blob/main/codex-rs/hooks/src/events/common.rs). The spike on Claude Code 2.1.284 saw a SessionStart hook with `if` never run, and `scenarios/codex-hook-trust/` records Codex 0.156.1 skipping a `prompt` handler and a matcher that doesn't compile.
