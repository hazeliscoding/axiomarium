# Scope sheriff

Agents wander. A task about the orders API ends with an edit to billing, and nobody asked why. The scope sheriff notices: after every file edit, it checks the path against the task's declared scope, and when the edit is outside it, it asks the agent to explain the edit in its reply to you.

It warns and never blocks. The edit has already happened, and an edit outside the scope is sometimes right. The point is that the agent says so, and you see it.

## Declaring the scope

At the start of a task, write the paths it should touch to `.axm/scope` at the repo root, one glob per line:

```text
# Task: add paging to the orders API
src/api/orders/
tests/api/orders/**
docs/api.md
```

- `*` matches within one folder, `**` across folders, `?` one character, and `{a,b}` either alternative. A line that ends with `/` covers everything under that folder.
- Paths are relative to the folder that holds `.axm/`, with forward slashes. Matching is case-sensitive.
- Blank lines and lines starting with `#` are ignored.
- Without `.axm/scope`, or with only comments in it, the hook stays silent.
- An invalid line matches nothing, and the warning names it.

The agent can write the file itself when you give it the task, or you can. When the task grows, add the new paths. `.axm/` is local state: add it to `.gitignore`.

## What the agent sees

For an edit to `src/billing/invoice.cs` with the scope above, the agent is told:

```text
src/billing/invoice.cs is outside this task's scope (src/api/orders/, tests/api/orders/**, docs/api.md). In your reply to the user, say why this edit was needed. If the task grew, add the path to .axm/scope.
```

## Wiring it into Claude Code

Until `axm sync` generates harness files, add this to `.claude/settings.json` (shared with the repo) or `.claude/settings.local.json` (just you). `axm` must be on your `PATH`.

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Write|Edit|NotebookEdit",
        "hooks": [{ "type": "command", "command": "axm hook scope-sheriff" }]
      }
    ]
  }
}
```

## How it behaves

- It runs after Write, Edit and NotebookEdit, and only for edits that happened. It reads the edited path from the hook input, and finds `.axm/scope` by walking up from the session's working directory.
- It answers through `hookSpecificOutput.additionalContext`, which Claude Code hands to the model without blocking anything.
- An edit outside the repo shows its full path.
- When it can't read its input, it exits with 1 and says why on stderr. It never exits with 2, which Claude Code would read as "block".
- It takes about 80 ms per edit when run directly, and about 200 ms inside Claude Code on Windows.

Confirmed against Claude Code 2.1.283 on 2026-09-27, following the Claude Code hooks docs as read on that date. Those docs don't say which output of a tool hook reaches the model, so headless sessions settled it.

## Limits

- **Only file-editing tools.** An edit made through the shell, such as `sed -i` or a script, isn't seen.
- **Claude Code only.** Codex support waits until its hooks are confirmed against the real harness.
- **It only asks.** Whether agents actually explain edits they chose themselves is a question for behavioral evals, which arrive in v0.4. In the first sessions, the model received the warning but didn't explain an edit the user had asked for.
- **No evidence yet.** Recording each out-of-scope edit waits for the evidence store in v0.5.
