# skill-paths-match-nothing

**Severity:** warning · **Harness:** Claude Code

## What happens

A project skill's `paths` patterns match no file in the repo, or one of its patterns isn't valid. Claude Code lists a skill with `paths` only once the agent reads, writes or edits a file they match, so the skill never reaches the model. Patterns match from the launch directory, the way `.gitignore` lines do: one without a slash, such as `*.ts`, matches at any depth, one with a slash inside is anchored, and a trailing `/` or `/**` matches a folder at any depth. Every file on disk counts except those under `.git`, the skill's own files included.

```text
WARNING  skill-paths-match-nothing
         .claude/skills/api/SKILL.md applies to src/api/**, which matches no file in the repo, so Claude Code never lists it.
         Fix: Fix the patterns. They match paths from the launch directory, the way .gitignore lines do.
```

Personal skills in `~/.claude/skills/` aren't checked, because they may match in another repo.

## Why it matters

A folder was renamed, or the pattern has a typo, and the skill quietly stops being offered.

## Fix

Fix the patterns so they match the files the skill is for, from the repo root, where the agent is launched.

## Source

[Claude Code skills: Frontmatter reference](https://code.claude.com/docs/en/skills#frontmatter-reference). How the patterns match comes from the recordings in `scenarios/claude-code-skills/` and `scenarios/skills-and-hooks/`, on Claude Code 2.1.284: the docs call them glob patterns, but Claude Code matches them like `.gitignore` lines.
