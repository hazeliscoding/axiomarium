# skill-name-clash

**Severity:** warning · **Harness:** Claude Code and Codex

## What happens

Two skills have the same name. Claude Code names a skill after its folder and lists only one of them: a managed skill beats a personal one, a personal skill beats a project one, and a skill beats a command. The other never reaches the model. Codex lists both, and a `$name` mention then picks neither of them.

```text
WARNING  skill-name-clash
         .claude/skills/deploy/SKILL.md is named deploy, like ~/.claude/skills/deploy/SKILL.md, which Claude Code lists instead, so this one never reaches the model.
         Fix: Rename one of the two folders: Claude Code names a skill after its folder.
```

```text
WARNING  skill-name-clash
         Codex lists 2 skills named deploy, .agents/skills/deploy/SKILL.md and ~/.agents/skills/deploy/SKILL.md, so $deploy picks none of them.
         Fix: Give one a different name in its SKILL.md frontmatter, or turn one off with [[skills.config]] in ~/.codex/config.toml.
```

A copied skills folder clashes a skill at a time, so clashes between the same folders make one finding:

```text
WARNING  skill-name-clash
         Codex lists 3 skills twice, from ~/.agents/skills and ~/.codex/skills, such as deploy, review and triage, so a $name mention picks neither copy.
         Fix: Give one of each pair a different name in its SKILL.md frontmatter, or turn one off with [[skills.config]] in ~/.codex/config.toml.
```

A skill that replaces a Claude Code built-in one, such as a project `init`, does so on purpose and isn't reported. Neither is a clash only between skills you can't edit, such as a plugin's or Codex's bundled ones.

## Why it matters

A project skill written for this repo silently loses to a personal one with the same name, so the agent follows the wrong steps. In Codex, asking for the skill by name stops working.

## Fix

Rename one of the two. In Claude Code that means its folder, and in Codex the `name` in its frontmatter. In Codex you can also turn one off with a `[[skills.config]]` entry.

## Source

[Claude Code skills: Resolve skills that share a name](https://code.claude.com/docs/en/skills#resolve-skills-that-share-a-name) and the [Codex skills guide](https://developers.openai.com/codex/skills). The recordings in `scenarios/claude-code-skills/` (Claude Code 2.1.284) and `scenarios/codex-skills/` (Codex 0.156.1) confirm which skill each harness lists.
