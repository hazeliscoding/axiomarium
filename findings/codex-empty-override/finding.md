# codex-empty-override

**Severity:** warning · **Harness:** Codex

## What happens

In each folder, Codex loads `AGENTS.override.md` instead of `AGENTS.md` when it exists. It picks the file by existence and only then checks whether it's empty, so an empty override hides the `AGENTS.md` next to it and Codex loads nothing from that folder.

```text
WARNING  codex-empty-override
         AGENTS.override.md is empty, and Codex picks it over AGENTS.md, so Codex loads nothing from that folder.
         Fix: Delete the empty AGENTS.override.md, or write the override in it.
```

## Why it matters

An override emptied to "turn it off" turns off the folder's instructions instead.

## Fix

Delete the empty `AGENTS.override.md`, or write the override in it.

## Source

[Codex: AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md) and [`agents_md.rs`](https://github.com/openai/codex/blob/main/codex-rs/core/src/agents_md.rs). The recording in `scenarios/codex-empty-override/` confirms it on Codex 0.156.1.
