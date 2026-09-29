# skill-description-cut

**Severity:** warning · **Harness:** Claude Code and Codex

## What happens

A skill's description is longer than the harness shows for one skill. Claude Code cuts each entry, `description` and `when_to_use` together, at 1,536 characters (`skillListingMaxDescChars`) and ends it with `…`. Codex cuts each description at 1,024 characters and ends it with `...`. The model decides whether to use a skill from that text alone.

```text
WARNING  skill-description-cut
         .claude/skills/review/SKILL.md has a description over the 1,536 characters Claude Code shows for one skill, so the model sees it cut.
         Fix: Shorten the description and when_to_use, and put the words that should make the agent pick the skill first.
```

Skills you can't edit, such as a plugin's or Codex's bundled ones, aren't reported.

## Why it matters

The end of a long description is often where its "use when" cases are, and those are what make the agent pick the skill. The cut also leaves less room in the listing for other skills.

## Fix

Shorten the description, and put the words that should trigger the skill first. Move detail into the skill's body, which the agent reads once it uses the skill.

## Source

[Claude Code skills: Skill descriptions are cut short](https://code.claude.com/docs/en/skills#skill-descriptions-are-cut-short) and Codex's [`render.rs`](https://github.com/openai/codex/blob/main/codex-rs/ext/skills/src/render.rs). The recordings in `scenarios/claude-code-skills/` (Claude Code 2.1.284) and `scenarios/codex-skills/` (Codex 0.156.1) confirm both caps.
