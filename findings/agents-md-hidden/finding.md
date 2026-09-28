# agents-md-hidden

**Severity:** warning · **Harness:** Claude Code

## What happens

Claude Code reads AGENTS.md only when there is no CLAUDE file (`CLAUDE.md`, `.claude/CLAUDE.md` or `CLAUDE.local.md`) in the launch directory or above. Once one exists, every AGENTS.md is skipped, nested ones too, unless a CLAUDE file imports it. Codex and other agents keep reading AGENTS.md.

```text
WARNING  agents-md-hidden
         Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
         Fix: Add @AGENTS.md to CLAUDE.md.
```

The finding doesn't fire when the user's Project instructions setting turns AGENTS.md off on purpose, or for an AGENTS.md above the repo, such as one in the home folder, which serves other folders too.

## Why it matters

AGENTS.md is where a repo keeps the instructions meant for every agent. Adding a CLAUDE.md for one Claude-specific note quietly takes all of them away from Claude Code, while Codex still follows them.

## Fix

Import it from the CLAUDE file next to it, with `@AGENTS.md`. For a nested AGENTS.md with no CLAUDE file beside it, add a `CLAUDE.md` next to it that says `@AGENTS.md`, so it still loads only when the agent works in that folder.

## Source

[Claude Code memory: AGENTS.md](https://code.claude.com/docs/en/memory#agents-md). That one CLAUDE file hides nested AGENTS.md files too comes from the recordings in `scenarios/agents-md-and-claude-md/` and `scenarios/agents-md-two-levels/`, on Claude Code 2.1.283.
