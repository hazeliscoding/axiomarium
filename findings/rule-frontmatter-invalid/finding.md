# rule-frontmatter-invalid

**Severity:** warning · **Harness:** Claude Code

## What happens

A rule in `.claude/rules/` has YAML frontmatter that doesn't parse, such as an unclosed bracket or quote, or an unquoted pattern that starts with `*` or `{`. Claude Code never loads the rule, for any file.

```text
WARNING  rule-frontmatter-invalid
         .claude/rules/api.md has frontmatter that doesn't parse, so Claude Code never loads the rule.
         Fix: Fix the YAML between the --- lines: close every bracket and quote, and quote patterns that start with * or {.
```

## Why it matters

The rule looks finished in the editor and never reaches the agent. The docs say such a rule loads for every file, so even a careful reader expects the opposite of what happens.

## Fix

Fix the YAML between the `---` lines. Quote each pattern, as in `- "src/**/*.ts"`.

## Source

[Claude Code memory: Rule frontmatter reference](https://code.claude.com/docs/en/memory#rule-frontmatter-reference). The docs say a rule whose frontmatter doesn't parse loads for every file. The recordings in `scenarios/bad-frontmatter/` show it never loads, on Claude Code 2.1.283, and the harness wins.
