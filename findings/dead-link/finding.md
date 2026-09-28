# dead-link

**Severity:** warning · **Harness:** Claude Code and Codex

## What happens

A loaded instruction file has a Markdown link, inline or as a reference definition, to a relative path that doesn't exist. Links in code spans and fenced blocks, URLs, `#anchors` and absolute paths aren't checked.

```text
WARNING  dead-link
         AGENTS.md:3 links to docs/testing.md, which does not exist, so an agent that follows the link finds nothing.
         Fix: Fix the link, or restore the file.
```

## Why it matters

Unlike an import, a link isn't loaded: the agent reads the target only when it follows the link. When the target is gone, the agent works without the guidance it was sent to find.

## Fix

Fix the link, or restore the file.

## Source

[Claude Code memory: Import additional files](https://code.claude.com/docs/en/memory#import-additional-files), which covers what loads and what doesn't, and [Codex: AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md). Neither harness follows Markdown links on its own.
