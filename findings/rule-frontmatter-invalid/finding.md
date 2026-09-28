# rule-frontmatter-invalid

**Severity:** warning · **Harness:** Claude Code

## What happens

A rule in `.claude/rules/` has YAML frontmatter that doesn't parse, such as a line indented wrongly. Claude Code first quotes each value that looks like YAML syntax and parses again, and when that fails too, it ignores the whole frontmatter and loads the rule for every file, at launch, as if it had no `paths`.

```text
WARNING  rule-frontmatter-invalid
         .claude/rules/api.md has frontmatter that doesn't parse, so Claude Code ignores all of it and loads the rule for every file.
         Fix: Fix the YAML between the --- lines, such as its indentation, so the rule's paths count again.
```

## Why it matters

A rule written for one folder reaches every task instead, where it takes up context and can steer work it wasn't meant for. Nothing in the editor says its `paths` were ignored.

## Fix

Fix the YAML between the `---` lines, and quote each pattern, as in `- "src/**/*.ts"`. A value Claude Code can mend by quoting, such as an unclosed `[`, becomes a pattern that matches nothing instead, which `rule-matches-nothing` reports.

## Source

[Claude Code memory: Rule frontmatter reference](https://code.claude.com/docs/en/memory#rule-frontmatter-reference). The docs say a rule whose frontmatter doesn't parse loads for every file, and the recordings in `scenarios/bad-frontmatter/` confirm it on Claude Code 2.1.284. Until v0.3 this finding said such a rule never loads: the rules recorded then had values Claude Code mends by quoting, into patterns that match nothing.
