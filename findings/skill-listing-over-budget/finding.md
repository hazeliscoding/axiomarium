# skill-listing-over-budget

**Severity:** warning · **Harness:** Claude Code and Codex

## What happens

The listing of every skill the harness offers the model at launch, built-in skills included, is over its budget.

- Claude Code gives the listing 1% of the context window at 4 characters a token (`skillListingBudgetFraction`): 8,000 characters at 200k tokens. Its 13 built-in skills take 6,230 of them. Over budget, some skills are listed by name only, starting with the ones used least.
- Codex gives it `skills.max_context_tokens`, else 2% of the context window, else 8,000 characters, counting 4 bytes a token. Over budget, it shortens descriptions in turn, then drops skills from the end.

`axm` can't read the model's context window or how often each skill is used, so the message states what it assumes and never says which skill loses its description.

```text
WARNING  skill-listing-over-budget
         Claude Code's skill listing takes 8,267 characters, over its budget of 8,000, assuming a 200k-token context window, so some skills are listed without their descriptions, starting with the ones used least.
         Fix: Shorten descriptions, set skills you rarely use to "name-only" in skillOverrides, or raise skillListingBudgetFraction in ~/.claude/settings.json.
```

## Why it matters

A skill listed without its description is only a name, and the agent rarely picks a skill it can't tell the use of. Which skills lose out changes with use, so a skill can work one week and not the next.

## Fix

Shorten descriptions. In Claude Code, set skills you rarely use to `"name-only"` in `skillOverrides`, or raise `skillListingBudgetFraction`. In Codex, turn skills off with `[[skills.config]]`, or raise `skills.max_context_tokens`.

## Source

[Claude Code skills: Skill descriptions are cut short](https://code.claude.com/docs/en/skills#skill-descriptions-are-cut-short) and Codex's [`render.rs`](https://github.com/openai/codex/blob/main/codex-rs/ext/skills/src/render.rs). The spike on Claude Code 2.1.284 measured the built-in skills and saw them lose their descriptions over budget too.
