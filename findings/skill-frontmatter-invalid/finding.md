# skill-frontmatter-invalid

**Severity:** warning · **Harness:** Claude Code and Codex

## What happens

A `SKILL.md` has frontmatter the harness can't use.

- Claude Code first quotes each value that looks like YAML syntax and parses again. When that fails too, or the skill has no `description`, it lists the skill with the first line of its body in place of a description.
- Codex skips a `SKILL.md` without frontmatter, whose YAML doesn't parse even after it quotes values holding `": "`, without a `description`, or with a `name` over 64 characters. The model never sees it.

```text
WARNING  skill-frontmatter-invalid
         .claude/skills/deploy/SKILL.md has frontmatter that doesn't parse, so Claude Code lists the skill with its first line in place of a description.
         Fix: Fix the YAML between the --- lines, and give the skill a description.
```

```text
WARNING  skill-frontmatter-invalid
         Codex skips .agents/skills/release/SKILL.md, because it has no description, so the model never sees it.
         Fix: Start SKILL.md with frontmatter that gives a description and a name of up to 64 characters.
```

## Why it matters

The description is what makes the agent pick a skill. A first line such as "Deploy the shop to production." rarely says when to use it, and in Codex the skill is gone.

## Fix

Fix the YAML between the `---` lines and give the skill a `description`. Quote a value that starts with `[`, `{`, `@` or a backtick, or that holds `": "`.

## Source

[Claude Code skills: Frontmatter reference](https://code.claude.com/docs/en/skills#frontmatter-reference) and Codex's [`parser.rs`](https://github.com/openai/codex/blob/main/codex-rs/skills/src/parser.rs). The recordings in `scenarios/claude-code-skills/` (Claude Code 2.1.284) and `scenarios/codex-skills/` (Codex 0.156.1) confirm what each harness does with such a skill.
