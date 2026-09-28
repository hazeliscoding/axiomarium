# duplicate-block

**Severity:** info · **Harness:** Claude Code and Codex

## What happens

The same paragraph loads from two different files in one harness's context. A paragraph is a run of lines between blank lines, compared with its whitespace collapsed. Paragraphs shorter than 40 characters, such as headings, don't count. Each pair of files is reported once, at the first paragraph the later file repeats.

```text
INFO  duplicate-block
      AGENTS.md:3 repeats a paragraph from CLAUDE.md:3. Both files load together, so it takes up context twice.
      Fix: Keep the paragraph in one of the files.
```

## Why it matters

Nothing breaks, but the paragraph costs tokens twice in every session, and the two copies drift apart when only one gets edited.

## Fix

Keep the paragraph in one of the files. In Claude Code, a CLAUDE file can import the other file with `@path` instead of copying it.

## Source

[Claude Code memory: How CLAUDE.md files load](https://code.claude.com/docs/en/memory#how-claude-md-files-load) and [Codex: AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md), which say that every loaded file is added to the context in full.
