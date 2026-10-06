# codex-hook-untrusted

**Severity:** warning · **Harness:** Codex

## What happens

Codex runs a hook that isn't managed only once the user has trusted it: `[hooks.state]` in `~/.codex/config.toml` must hold the hook's SHA-256 as its `trusted_hash`. A hook that was never trusted doesn't run, and neither does one that changed since, whose hash no longer matches. A project's `.codex` hooks don't even load until the project itself is trusted. The finding counts the hooks in each file.

```text
WARNING  codex-hook-untrusted
         ~/.codex/hooks.json has 1 hook Codex hasn't trusted, so it never runs.
         Fix: Review the hook in Codex and trust it, which records its hash under [hooks.state] in ~/.codex/config.toml.
```

Codex finds a hook's entry only under the key it writes itself: the hook file's absolute path, with backslashes on Windows, then the event, group and index, matched exactly, case included. When `CODEX_HOME` is set, the path starts with it as Codex canonicalizes it: links followed and, on Windows, each folder's case as on disk. A project's entry under `[projects]` likewise counts only with the folder's path as written or the path its links lead to, with no trailing separator, though on Windows in any case. An entry written with forward slashes on Windows is ignored, so its hooks are reported here.

Managed hooks, in the system folder, run without being trusted and aren't reported. The clean fixture uses one, because a trusted hook's key holds the absolute path of its file.

## Why it matters

A hook added to the repo, or edited, stops running until someone reviews it in Codex, and nothing else says so.

## Fix

Review the hooks in Codex and trust them, which records each one's hash. For a project's hooks, trust the project first, which sets `trust_level = "trusted"` for it in `~/.codex/config.toml`.

## Source

The [Codex hooks guide](https://developers.openai.com/codex/hooks) and Codex's [`discovery.rs`](https://github.com/openai/codex/blob/main/codex-rs/hooks/src/engine/discovery.rs). The recording in `scenarios/codex-hook-trust/` confirms each trust state and the hash `axm` computes, on Codex 0.156.1. How Codex spells `CODEX_HOME` and looks up a project follows [`home-dir`](https://github.com/openai/codex/blob/rust-v0.156.1/codex-rs/utils/home-dir/src/lib.rs) and `normalized_project_trust_keys` in [`codex-rs/config`](https://github.com/openai/codex/blob/rust-v0.156.1/codex-rs/config/src/loader/mod.rs) at 0.156.1. On Windows, `hooks/list` from `codex app-server` 0.156.1 agreed, and sessions confirmed that a hook trusted under a key with forward slashes never runs (2026-10-04).
