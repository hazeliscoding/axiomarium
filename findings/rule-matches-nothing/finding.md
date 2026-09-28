# rule-matches-nothing

**Severity:** warning · **Harness:** Claude Code

## What happens

A project rule's `paths` patterns match no file in the repo, or one of its patterns isn't valid, such as a `[` that is never closed. Claude Code loads a path rule only when the agent reads a file it matches, so the rule never loads. Patterns match from the folder that holds the rule's `.claude/`. Every file on disk counts except those under `.git`.

```text
WARNING  rule-matches-nothing
         .claude/rules/api.md applies to src/api/**, which matches no file in the repo, so Claude Code never loads it.
         Fix: Fix the patterns. They match paths relative to the repo root.
```

User rules in `~/.claude/rules/` aren't checked, because they may match in another repo.

## Why it matters

A folder was renamed, or the pattern has a typo, and the rule quietly stops applying.

## Fix

Fix the patterns so they match the files the rule is for, relative to the folder that holds `.claude/`.

## Source

[Claude Code memory: Path-specific rules](https://code.claude.com/docs/en/memory#path-specific-rules). Where patterns match from comes from the recordings in `scenarios/path-rules/` and `scenarios/nested-rules/`, on Claude Code 2.1.283.
