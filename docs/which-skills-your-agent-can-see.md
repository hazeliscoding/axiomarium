# Which skills your agent can see

A skill is a folder with a `SKILL.md`. The harness doesn't load it up front. It lists each skill's name and description for the model, and the model decides when to open one. So a skill only works if the harness lists it, with enough of its description left for the model to tell when it applies. Hooks have a similar blind spot: a hook that never runs looks exactly like one that ran and found nothing wrong.

Which skills get listed, and which hooks run, depends on the harness, where the files are, what else is installed, and a size budget you can't see. So we recorded both harnesses again, this time for skills and hooks, and taught `axm explain` to show the result.

## The same repo, two agents

The demo shop from [What your agent actually reads](what-your-agent-reads.md), in [`scenarios/demo/`](../scenarios/demo), gained two skills and a hook, each where one harness's docs say to put it:

- `.claude/skills/release/` holds the release steps.
- `.agents/skills/db-migration/` says how to write a database migration.
- `.claude/settings.json` and `.codex/hooks.json` each hold the same after-edit hook, meant to run the API tests after an edit under `src/api/`.

Here is the new part of what `axm explain` shows for `src/api/orders.cs`. The instruction files are as in the first write-up:

```text
$ axm explain src/api/orders.cs

AXM EXPLAIN // src/api/orders.cs

  CLAUDE CODE // launched at the repo root
  …

  CLAUDE CODE SKILLS // 14 listed · 6,374 of 8,000 characters, assuming a 200k-token context window
  01  release              .claude/skills/release/SKILL.md   project    at launch
  02  13 built-in skills                                     built in   at launch

  CLAUDE CODE HOOKS // at session start and around an edit of the file, in a trusted workspace
  01  after edit   .claude/settings.json   echo MARKER repo/.claude/settings.json test-api   RUNS     project, if Edit(src/api/**)

  CODEX // launched at the repo root
  …

  CODEX SKILLS // 1 listed · 52 of 5,440 tokens, assuming 2% of a 272k-token context window, that of Codex's default models
  01  db-migration   .agents/skills/db-migration/SKILL.md   repo   at launch

  CODEX HOOKS // at session start and around an edit of the file
  --  after edit   .codex/hooks.json   echo MARKER repo/.codex/hooks.json test-api   NOT RUN  the project isn't trusted

WARNING  codex-hook-untrusted
         Codex doesn't load .codex/hooks.json, because the project isn't trusted, so its 1 hook never runs.
         Fix: Trust the project in Codex, which sets trust_level = "trusted" for it in ~/.codex/config.toml.

…

2 harnesses · 5 loaded · 4 not loaded · 15 skills listed · 1 hook runs · 4 warnings
```

Each harness lists the skill that was written for it and never sees the other one. Claude Code runs its hook after the edit. Codex doesn't run its copy: a fresh clone isn't a trusted project, and even in a trusted one each hook has to be trusted on its own. `--diff` shows the skills only one harness lists:

```text
$ axm explain src/api/orders.cs --diff

AXM EXPLAIN // src/api/orders.cs // diff
…

  ONLY CLAUDE CODE SKILLS
  01  release   .claude/skills/release/SKILL.md   project   at launch

…

  ONLY CODEX SKILLS
  01  db-migration   .agents/skills/db-migration/SKILL.md   repo   at launch

3 files only in Claude Code · 2 only in Codex · 1 skill only in Claude Code · 1 only in Codex
```

## What the recordings settled

These are the parts the docs leave out, or that the harness does differently. Most have a scenario that recorded them, the rest come from a first spike or the harness's source, and most have a finding that catches them.

1. **Each harness reads its own folders.** Claude Code lists `.claude/skills` from your home, from the launch directory up to the repo root, and from folders below once the agent works there. Codex lists `.agents/skills` from the repo root down to the launch directory, `~/.agents/skills` and its own home's `skills`. It also lists a project's `.codex/skills` in a project you haven't trusted, although its docs say untrusted projects skip `.codex`. [`skills-and-hooks`](../scenarios/skills-and-hooks), [`codex-skills`](../scenarios/codex-skills).
2. **Claude Code names a skill after its folder, and Codex after its `name`.** When two skills share a name, Claude Code lists one: managed beats personal, personal beats project, and a skill beats a command. Codex lists both, and then `$deploy` picks neither. [`claude-code-skills`](../scenarios/claude-code-skills), [`codex-skills`](../scenarios/codex-skills), [`skill-name-clash`](../findings/skill-name-clash/finding.md).
3. **The built-in skills take most of Claude Code's budget.** The listing gets 1% of the context window at 4 characters a token: 8,000 characters at 200k tokens. Claude Code's 13 built-in skills take 6,230 of them. Past the budget, some skills are listed by name only, starting with the ones used least, and the spike saw built-ins lose theirs too. Codex gives its listing 2% of the window, and past it shortens descriptions, then drops skills from the end. [`skill-listing-over-budget`](../findings/skill-listing-over-budget/finding.md).
4. **Long descriptions are cut.** Claude Code shows 1,536 characters of `description` and `when_to_use` together, and Codex 1,024 of the description. The end, where the "use when" cases often are, is what goes. [`skill-description-cut`](../findings/skill-description-cut/finding.md).
5. **Broken frontmatter lists the first line, or nothing.** Claude Code quotes values that look like YAML syntax and parses again. When that still fails, or there's no description, it lists the skill with the first line of its body. Codex skips such a skill. [`claude-code-skills`](../scenarios/claude-code-skills), [`codex-skills`](../scenarios/codex-skills), [`skill-frontmatter-invalid`](../findings/skill-frontmatter-invalid/finding.md).
6. **`paths` match like `.gitignore` lines.** A skill with `paths` joins the listing when the agent reads or edits a file they match, from the launch directory. `*.ts` matches at any depth and `api/**` matches an `api` folder anywhere. The docs call them glob patterns. [`claude-code-skills`](../scenarios/claude-code-skills), [`skill-paths-match-nothing`](../findings/skill-paths-match-nothing/finding.md).
7. **Codex runs a hook only once you trust it.** A hook runs when its SHA-256, taken over its event, matcher and handler, matches the `trusted_hash` in your config. Editing the hook changes the hash, so it stops until you trust it again, and a project's hooks don't load at all until the project is trusted. [`codex-hook-trust`](../scenarios/codex-hook-trust), [`codex-hook-untrusted`](../findings/codex-hook-untrusted/finding.md).
8. **Some hooks never run.** Claude Code reads `if` only on tool events, so a `SessionStart` hook with `if` never runs. Codex skips `prompt` and `agent` handlers, and a matcher that isn't a valid regex. [`codex-hook-trust`](../scenarios/codex-hook-trust), [`hook-never-runs`](../findings/hook-never-runs/finding.md).
9. **A Codex matcher of plain names is a list.** `Edit|Write` matches those two tools exactly, as in Claude Code, although Codex's docs call every matcher a regex. Codex's [`common.rs`](https://github.com/openai/codex/blob/main/codex-rs/hooks/src/events/common.rs).
10. **A Codex edit hook can't be scoped to a file.** It gets the whole patch and no path, so it runs on every edit, and `axm explain` says so. Codex's [`discovery.rs`](https://github.com/openai/codex/blob/main/codex-rs/hooks/src/engine/discovery.rs).

## Skills that sound alike

A skill can be listed and still lose: when two descriptions say much the same thing, the model may pick the other one. `axm triggers` compares the skills each harness lists, and the vault's skills as sync would list them, on the text the model sees. A word counts for more the rarer it is in that listing, and each pair that shares enough is shown with the words it shares:

```text
$ axm triggers

AXM TRIGGERS // 2 harnesses · 1 overlapping pair

  CLAUDE CODE // 3 skills compared · 13 built-in skills left out, their text isn't recorded
  01  0.21  ship ~ deploy  shares production, shop
      ~/.claude/skills/ship/SKILL.md
      .claude/skills/deploy/SKILL.md

  CODEX // 1 skill compared
  --  no overlap

1 overlapping pair. Shared wording, not proof the agent mixes them up.
```

That's the whole claim: shared wording. Whether the model actually picks the wrong skill takes prompts and a model, which is what v0.4 adds.

## How we know

As for instruction files, each scenario in [`scenarios/`](../scenarios) is a tiny repo with a fake home, and the recorder runs the real harnesses on a copy of it. Each skill's description starts with its file's marker, and each hook's command echoes its marker, so a recording can name the file behind every entry and every hook run.

- **Claude Code:** the session transcript holds the skill listing as a `skill_listing` attachment, and each hook run as a `hook_success` attachment. Scenarios with `action: edit` have the agent edit the target, so the edit hooks run.
- **Codex:** `codex debug prompt-input` prints the skill listing, and `codex app-server`'s `hooks/list` names each hook with its trust. Codex is recorded on Linux, by [`record-codex.yml`](../.github/workflows/record-codex.yml): on Windows it reads the real `~/.agents/skills` whatever `HOME` says.

CI replays every recording against the models `axm explain` runs. They were made with Claude Code 2.1.284 and Codex 0.156.1.

What this doesn't cover:

- **Whether the model uses a skill.** A listed skill is available, not loaded, and `axm` never says otherwise.
- **The text of Claude Code's built-in skills.** They aren't files, so their names and sizes are recorded, and `axm triggers` leaves them out.
- **What the budget depends on.** It turns on the model's context window and on how often you use each skill, which no file says, so `axm` states what it assumes.
- **Plugin and synced skills.** They follow the docs, because a plugin's install records hold absolute paths and the recordings turn syncing off.
- **Whether a Codex hook fires.** Seeing that takes a model turn, so hook matching follows Codex's source. Codex plugin skills and hooks, `--add-dir`, hooks in skill frontmatter and hooks on a read aren't modeled yet.

## Check your own setup

```text
axm explain path/to/file   the skills each harness lists and the hooks that run for that file
axm doctor                 every skill and hook the harnesses find, and what's wrong with them
axm triggers               skills whose descriptions overlap
```

The doctor lists your personal and plugin skills too, grouped by where they come from, and warns about the ones that clash, are cut or don't fit the budget. [`session-doctor`](../hooks/session-doctor/hook.md) passes those warnings on at the start of a Claude Code session.
