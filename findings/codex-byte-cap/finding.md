# codex-byte-cap

**Severity:** warning · **Harness:** Codex

## What happens

Codex's project files, from the project root down to the launch directory, share one budget: `project_doc_max_bytes`, 32 KiB by default. The file that crosses it is cut mid-file, and the files after it are dropped. The global `~/.codex/AGENTS.md` doesn't count.

```text
WARNING  codex-byte-cap
         AGENTS.md is cut to 32768 of its 40112 bytes, because Codex's project files share project_doc_max_bytes.
         Fix: Shorten the project files, or raise project_doc_max_bytes in ~/.codex/config.toml.
```

## Why it matters

The end of the file, and every file below it, never reaches the agent. Nothing in the session says so, and the most specific instructions sit in the files that load last.

## Fix

Shorten the project files, or raise `project_doc_max_bytes` in `~/.codex/config.toml`.

## Source

[Codex: AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md) and [`agents_md.rs`](https://github.com/openai/codex/blob/main/codex-rs/core/src/agents_md.rs). The docs contradict each other on the budget, so the code decides. The recording in `scenarios/codex-byte-cap/` confirms it on Codex 0.156.1.
