# dead-import

**Severity:** warning · **Harness:** Claude Code

## What happens

A CLAUDE file, a rule or an imported file imports another file with `@path`, and that file doesn't exist. Claude Code loads the rest and skips the import without saying so.

An import ends at the next space, so a comma or period right after it becomes part of the path. `See @docs/testing.md, then run it` imports `docs/testing.md,`, which doesn't exist.

```text
WARNING  dead-import
         CLAUDE.md:3 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place.
         Fix: Restore the file, or remove the import.
```

## Why it matters

Whatever the missing file was meant to tell the agent never reaches it, and nothing in the session shows that.

## Fix

Restore the file, or remove the import. When a punctuation mark follows the import, remove it or put a space before it.

## Source

[Claude Code memory: Import additional files](https://code.claude.com/docs/en/memory#import-additional-files). That trailing punctuation is part of the path comes from the recordings in `scenarios/imports/`, on Claude Code 2.1.283.
