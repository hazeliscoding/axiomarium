# import-too-deep

**Severity:** warning · **Harness:** Claude Code

## What happens

Imports can import more files, but Claude Code follows at most four hops from the file it loaded first. The fifth import in a chain is dropped, along with everything it would have imported.

```text
WARNING  import-too-deep
         docs/4.md:1 imports docs/5.md, a fifth hop from CLAUDE.md. Claude Code follows four, so docs/5.md never loads.
         Fix: Import docs/5.md from a file fewer hops from CLAUDE.md, or move its content up the chain.
```

## Why it matters

The end of a long chain is usually the most specific guidance, and it silently never reaches the agent.

## Fix

Import the file from one closer to the start of the chain, or move its content into a file that is already within four hops.

## Source

[Claude Code memory: Import additional files](https://code.claude.com/docs/en/memory#import-additional-files), which gives the limit of four hops. The recordings in `scenarios/imports/` confirm it on Claude Code 2.1.283.
