# Session doctor

Instruction files break quietly. A CLAUDE.md added for one note hides every AGENTS.md from Claude Code, an import points at a file someone renamed, a rule's frontmatter stops parsing, and nobody notices, because a session looks the same whether its instructions arrived or not. The session doctor runs `axm doctor` when a Claude Code session starts. When something is wrong, it tells you in one line and tells the model in full. When all is well, it says nothing.

It warns and never blocks. A session can't be blocked from starting, and a warning is all it needs to give.

## What you see

When a session starts in a repo whose CLAUDE.md hides AGENTS.md and imports a missing file, Claude Code shows you:

```text
axm doctor found 2 warnings in this repo: agents-md-hidden in AGENTS.md, dead-import in CLAUDE.md:3. Run axm doctor to see them.
```

It names at most three problems, then says how many more there are. Run `axm doctor` for the full report.

## What the model sees

The model gets every problem with its fix, and is told not to go fixing instruction files on its own:

```text
axm doctor checked this repo when the session started and found problems:
- WARNING agents-md-hidden: Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code. Fix: Add @AGENTS.md to CLAUDE.md.
- WARNING dead-import: CLAUDE.md:3 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place. Fix: Restore the file, or remove the import.
None of this blocks anything. If a file you were meant to read matters to your task, read it yourself. Change instruction files only when the user asks.
```

## Wiring it into Claude Code

Until `axm sync` generates harness files, add this to `.claude/settings.json` (shared with the repo) or `~/.claude/settings.json` (every repo you open). `axm` must be on your `PATH`. On Windows with the dotnet tool, write `axm.cmd hook session-doctor`: the tool is installed behind an `axm.cmd` launcher, and bash, which Claude Code may use to run hooks, doesn't find it as plain `axm`. The release binary is a real `axm.exe` and needs no change.

```json
{
  "hooks": {
    "SessionStart": [
      {
        "matcher": "startup",
        "hooks": [{ "type": "command", "command": "axm hook session-doctor" }]
      }
    ]
  }
}
```

The `startup` matcher runs it for new sessions only. Add `|clear` to run it after `/clear` too.

## How it behaves

- It runs the same checks as `axm doctor`, from the repo root above the session's working directory: the instruction findings, and the vault checks when the repo holds a vault. It reads `doctor.ignore` in `axiomarium.yaml`, so files that are broken on purpose stay quiet.
- Errors and warnings speak up. Info findings, such as a duplicated paragraph, are waste rather than problems, so they never interrupt a session.
- Your line goes out as `systemMessage` and the model's text as `hookSpecificOutput.additionalContext`. The model never sees your line.
- It ignores every event except SessionStart.
- When it can't read its input, or the working directory is gone, it exits with 1 and says why on stderr. It never exits with 2.
- It takes about 25 ms on a small repo and 70 ms on this one when run directly. Inside Claude Code on Windows the hook takes about 450 ms, most of it starting Git Bash. In an interactive session, SessionStart hooks run in the background, so you can type right away; Claude's first reply waits for them.

Confirmed against Claude Code 2.1.284 on 2026-09-28, following the [SessionStart section](https://code.claude.com/docs/en/hooks#sessionstart) of the Claude Code hooks docs as read on that date. Headless sessions with one kind of output each showed what the docs say: plain stdout and `additionalContext` reach the model, and `systemMessage` doesn't. The transcript records it as a separate message.

## Limits

- **Claude Code only.** Codex support waits until its hooks are confirmed against the real harness.
- **The whole repo, not your task.** It checks the repo launched from its root, so a problem in a folder you never touch still gets mentioned.
- **Headless proof only.** The recordings show that the model receives the context and not your line. That the terminal shows you the line comes from the docs.
