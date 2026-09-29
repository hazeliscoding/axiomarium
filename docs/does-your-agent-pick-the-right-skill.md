# Does your agent pick the right skill?

[Which skills your agent can see](which-skills-your-agent-can-see.md) ended on a warning. A skill can be listed, with its full description, and still lose, because another skill says much the same thing and the model picks that one. `axm triggers` could only point at shared wording. v0.4 runs the prompts on the real harnesses and counts what the agent picks.

## The test

The vault's one skill, [`agent-asset-authoring`](../skills/agent-asset-authoring/skill.md), is for writing an Axiomarium asset: an agent, skill, hook, policy, workflow or experiment with its `asset.yaml`. `axm triggers generate` asked Claude Code for prompts it should and shouldn't pick. I reviewed the 17 it wrote before they went into [`evals/trigger/prompts.yaml`](../skills/agent-asset-authoring/evals/trigger/prompts.yaml):

- 5 positive and 4 paraphrased prompts ask for an asset, the paraphrased ones without the description's words: "Package the way I review database migrations into something reusable in this repo that coding bots can load."
- 4 negative prompts are ordinary coding tasks.
- 4 adversarial prompts share its words but aren't about the vault: static "assets", an IAM "policy", a Claude Code hook in `.claude/settings.json`.

There were no ambiguous prompts, because no listed skill overlapped it enough to be a rival.

`axm triggers test` then ran each prompt three times on Claude Code 2.1.284 and Codex 0.156.1, on my own setup, where about 90 skills compete in Claude Code's listing and about 60 in Codex's.

## What came back

```text
$ axm triggers test

AXM TRIGGERS TEST // 1 skill · 17 prompts × 3 runs · 2 harnesses · 102 sessions, 4 at a time
  agent-asset-authoring's prompts were written by claude-opus-5-5 on 2026-09-29

  CLAUDE CODE 2.1.284 // picks by claude-opus-5-5
  01  agent-asset-authoring   precision 1.00 (27 of 27)   recall 1.00 (27 of 27)
…
```

Claude Code picked the skill in all 27 runs that should have, and in none of the 24 that shouldn't. The adversarial prompts didn't fool it. These numbers come from the first run, whose rule the fix below changed, but the new rule can't lower a perfect recall, and it could only add false triggers where a run loaded the skill after another one, which none did.

Codex, after the fix described below, on two of its models: `gpt-6-astra`, which my Codex is set to, and `gpt-6-sol`, its everyday coding model.

| Model | Runs scored | Precision | Recall |
|---|---|---|---|
| `gpt-6-astra` | 32 of 51 | 1.00 (21 of 21) | 0.78 (21 of 27) |
| `gpt-6-sol` | 51 of 51 | 1.00 (6 of 6) | 0.22 (6 of 27) |

The `gpt-6-astra` run:

```text
$ axm triggers test --harness codex

  CODEX 0.156.1 // picks by its configured model
  note: its skill listing is over budget, about 8,592 of 5,440 tokens assuming 2% of a 272k-token context window, that of Codex's default models, so descriptions are shortened and skills dropped from the end
  01  agent-asset-authoring   precision 1.00 (21 of 21)   recall 0.78 (21 of 27)

  COLLISIONS
  --  "Create a new skill asset under skills/ in our Axiomarium vault that teaches agents how to summarize PR diffs, with its asset.yaml and content file, and make sure it passes axm validate."  picked skill-creator in 1 of 3 runs, expected agent-asset-authoring
      the prompt shares create with skill-creator, and axiomarium, vault and yaml with agent-asset-authoring
  --  "The release-notes persona's YAML descriptor is missing an owner field and its prompt body is out of date. Bring both up to date."  picked using-superpowers in 2 of 3 runs, expected agent-asset-authoring
      no cause found in the listing
…
  FAILED // 19 sessions gave no answer and aren't scored
  --  codex  19 sessions: You’ve hit your usage limit. …
```

`gpt-6-astra` missed the skill in 6 of 27 runs. One went to Codex's own `skill-creator`, which a prompt about creating a skill can fairly reach for. Three read `using-superpowers` and then acted without the skill, and two picked no skill at all. My ChatGPT plan ran out of `gpt-6-astra` partway through, so 19 sessions weren't scored, all of them negative or adversarial prompts, and its precision rests on the 5 of those that ran.

`gpt-6-sol` ran every session and never picked the skill when it shouldn't have, but in 20 of its 21 misses it loaded no skill at all before its first other action. Whether it would have read the skill a step later, the test can't say: it stops each session at the first action that isn't loading a skill, so a model that looks around before reaching for a skill scores low. That's a limit of the measure as much as a trait of the model, and the same prompts, the same skill and the same listing gave 0.78 on one model and 0.22 on the other.

Both runs saw the same listing, more than half again over its budget, so Codex shortens every description in it, this one's included.

## What the first run got wrong

The first full run gave Codex a recall of 0.41, and the report blamed a skill called `using-superpowers` for 14 of the 16 misses. It comes from my superpowers plugin and tells the agent to use skills before anything else, so Codex read it first, then read the skill the task needed.

The test counted only the first skill a run loaded, and skipped a background skill only when every run loaded it. `using-superpowers` was read in most runs but not all, so it counted as the pick, and 14 good runs became misses. Now a run picks a skill when the skill is anywhere among what it loaded before its first other action, the way promptfoo's `skill-used` counts. Codex's loads also go on through commands that only read `SKILL.md` files, since that's how Codex loads a skill.

Two of the first run's misses were real: superpowers' `systematic-debugging` took "axm validate is failing… Fix the asset" in 2 of 3 runs, the kind of collision this test is for.

The first attempt at the run found a bug of its own. One harness printed an event with a key twice, which JSON allows and .NET's `JsonNode` rejects, and it ended the whole run after eight minutes of sessions. Events are read with `JsonDocument` now, and a session that fails is reported on its own.

## How a pick is read

A session is a real `claude -p` or `codex exec` run in a throwaway copy of the repo with the vault's skills written in, on my real home. Every skill I have competes, and every hook I run fires. The question is only which skills the agent reaches for before it starts working, so each session stops at its first action that isn't loading a skill:

- **Claude Code** reports each `Skill` tool call with the skill's name. Left alone, one session went on for 8 turns and cost $0.34, so stopping early is what keeps 102 sessions affordable.
- **Codex** has no skill event. It reads a skill's `SKILL.md` with a command, which its own source counts as use.
- **Background skills.** A skill loaded in every run, whatever the prompt, is named as background, and a collision never names it.

Each wrong run gets the first cause the listing shows: the expected skill isn't listed, its description is cut, it's listed by name only, or else the words the prompt shares with each skill, the rarest first. A listing over its budget is noted once for its harness. The model is never asked why it picked a skill, because models make up reasons after the fact.

## What this doesn't tell you

- **It's a sample.** Three runs of 17 prompts give rates, not guarantees, and the same prompt can go either way on another day.
- **The prompts came from a model.** I reviewed them, but they're one model's idea of how people ask.
- **A cause is the likely one.** It's what the listing shows, not what the model thought. "No cause found" is common, because the pick often comes from meaning rather than shared words.
- **The first action is the cut-off.** A skill an agent would load only after reading some files doesn't count, which is why `gpt-6-sol` scored so much lower than `gpt-6-astra`.
- **It's my setup, and my plan's limits.** Your skills compete differently, which is why the test runs on yours. A run of 102 sessions can reach a plan's usage limit, and the report lists what didn't run.

## Try it on your skills

```text
axm triggers generate <skill>                     write prompts for a vault skill, after you approve them
axm triggers test                                 run them on both harnesses, three times each
axm triggers export <skill> --format promptfoo    the same prompts, for promptfoo
```
