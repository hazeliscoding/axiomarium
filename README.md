<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/lockup-dark.svg">
    <img alt="Axiomarium" src="docs/brand/lockup.svg" height="40">
  </picture>
</h1>

<sub><i>ak·see·uh·<b>MAIR</b>·ee·um</i>: like aquarium, but for axioms</sub>

**Build, test, and debug your AI coding environment like software.**

Axiomarium is my lab for engineering reliable AI coding environments. It holds the agents, skills, hooks, policies, workflows and evals I use, and `axm`, a local CLI that inspects, validates, tests and debugs them. It treats agent configuration as real software infrastructure: kept in git, inspectable, testable, portable, and able to learn from its failures. It's built for my own setup first, and it's public in case it's useful to you too.

> **Status:** early development. `axm explain` shows what Claude Code and Codex actually load for a file, and since v0.3 which skills each lists for the model and which hooks run. `axm doctor` checks the instruction files, skills and hooks of any repo, and `axm triggers` finds skills whose descriptions overlap. Since v0.4, `axm triggers test` runs a vault skill's trigger prompts on both harnesses and reports whether the agent picks it. Since v0.5, `axm eval run` runs an asset's eval cases in a sealed home, `axm eval compare` shows a change to the asset side by side with its last commit, and `axm conflicts --judge` has a model find instructions that contradict each other. v0.1's vault checks stay: `axm list`, `axm validate` and `axm doctor` in a vault. Evidence freshness follows in v0.6. See [ROADMAP.md](ROADMAP.md).

## The problem

Most AI coding configuration ends up looking like this:

```text
CLAUDE.md
AGENTS.md
.github/copilot-instructions.md
.claude/
.agents/
.cursor/
skills/
random-prompts/
```

Over time, instructions contradict each other, skills overlap, old rules stay forever and the context keeps growing. Agents wander outside the task, and nobody can tell which instruction caused a behavior. Broken behavior gets fixed by adding more prose, the same configuration is copied between tools, and nobody tests whether any of it works.

Axiomarium treats that as an engineering problem.

> **Failures should become tests, not paragraphs.**

## Two parts

**The vault** holds agents, skills, hooks, policies, workflows, schemas, evals, experiments and write-ups. It is all Markdown, YAML and JSON Schema, so you can read it without `axm`. Every asset carries a manifest that says what it is, which harnesses it supports, what it's allowed to touch, how it's tested and how far it can be trusted. [Why assets carry manifests](docs/assets.md).

**`axm`** is the engineering around the vault:

```text
axm list        the vault's assets, and which harnesses each supports
axm validate    every check on the vault, for CI and hooks
axm doctor      health of the agent environment
axm explain     which instructions apply to a file, and which get dropped
axm triggers    which skill fires for which prompt, and where skills collide
axm eval        whether a change actually improves agent behavior
axm evidence    which test and build results are still fresh
axm incident    turn an agent failure into a guardrail and a regression test
axm fossil      instructions that only cost tokens now
axm sync        generate each harness's files from one source
```

The vault provides the knowledge and behavior. `axm` provides the infrastructure around it.

## What it looks like

`axm doctor`, `axm explain` and `axm triggers` work today. In a terminal the output is in color, with a kaomoji for the outcome. Piped, in CI or read by an agent, it's plain text.

![A terminal runs axm list, which shows the five starter assets. A one-line edit then raises the determinism auditor's maturity to tested, and axm doctor reports that the claim lacks its evidence.](docs/demo/doctor.gif)

`axm doctor` catches a broken asset before any agent loads it, and lists which harness loads each instruction file:

```text
$ axm doctor

AXM DOCTOR // 2 assets · 2 instruction files

  AGENTS
  01  determinism-auditor   experimental  0.1.0   OK
  02  scope-reviewer        ERROR

  INSTRUCTIONS
  03  CLAUDE.md   claude-code
  04  AGENTS.md   claude-code codex

ERROR  agents/scope-reviewer/asset.yaml:5
       Unknown maturity: "production-ready"
       Allowed: experimental, incubating, tested, stable, battle-tested

2 assets · 2 instruction files · 1 error  (╥﹏╥)
```

In a repo without a vault, `axm doctor` checks only the instruction files. Their findings are warnings, so they never fail it. List paths that are broken on purpose, such as test fixtures, under `doctor.ignore` in `axiomarium.yaml`, and the doctor leaves them out and says how many.

`axm explain` shows what each harness loads for a file, what it silently drops, and which skills and hooks reach the agent:

![A terminal runs axm explain on a demo shop. Claude Code loads CLAUDE.md and a path rule but skips both AGENTS.md files and a missing import, lists a release skill and runs its after-edit hook. Codex loads AGENTS.md and lists a migration skill, but doesn't run its hook, because the project isn't trusted. With --diff, the two agents share no instructions and no skills.](docs/demo/explain.gif)

```text
$ axm explain src/api/orders.cs

AXM EXPLAIN // src/api/orders.cs

  CLAUDE CODE // launched at the repo root
  01  ~/.claude/CLAUDE.md    user                at launch
  02  CLAUDE.md              project             at launch
  03  .claude/rules/api.md   paths: src/api/**   when the file is read
  --  docs/testing.md        DROPPED     file is missing, imported by CLAUDE.md:6
  --  AGENTS.md              DROPPED     a CLAUDE file exists and doesn't import it
  --  src/api/AGENTS.md      DROPPED     a CLAUDE file exists and doesn't import it

  CLAUDE CODE SKILLS // 14 listed · 6,374 of 8,000 characters, assuming a 200k-token context window
  01  release              .claude/skills/release/SKILL.md   project    at launch
  02  13 built-in skills                                     built in   at launch

  CLAUDE CODE HOOKS // at session start and around an edit of the file, in a trusted workspace
  01  after edit   .claude/settings.json   echo MARKER repo/.claude/settings.json test-api   RUNS     project, if Edit(src/api/**)

  CODEX // launched at the repo root
  01  ~/.codex/AGENTS.md     global              at launch
  02  AGENTS.md              project             at launch
  --  src/api/AGENTS.md      NOT LOADED  below the launch directory

  CODEX SKILLS // 1 listed · 48 of 5,440 tokens, assuming 2% of a 272k-token context window, that of Codex's default models
  01  db-migration   .agents/skills/db-migration/SKILL.md   repo   at launch

  CODEX HOOKS // at session start and around an edit of the file
  --  after edit   .codex/hooks.json   echo MARKER repo/.codex/hooks.json test-api   NOT RUN  the project isn't trusted

WARNING  codex-hook-untrusted
         Codex doesn't load .codex/hooks.json, because the project isn't trusted, so its 1 hook never runs.
         Fix: Trust the project in Codex, which sets trust_level = "trusted" for it in ~/.codex/config.toml.

WARNING  agents-md-hidden
         Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
         Fix: Add @AGENTS.md to CLAUDE.md.

WARNING  dead-import
         CLAUDE.md:6 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place.
         Fix: Restore the file, or remove the import.

WARNING  agents-md-hidden
         Claude Code skips src/api/AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
         Fix: Add a CLAUDE.md next to it that says @AGENTS.md.

2 harnesses · 5 loaded · 4 not loaded · 15 skills listed · 1 hook runs · 4 warnings  (・_・;)
```

This is the demo in [`scenarios/demo`](scenarios/demo), and both harnesses were recorded loading exactly these files and listing these skills, with only Claude Code running its hook. [What your agent actually reads](docs/what-your-agent-reads.md) and [Which skills your agent can see](docs/which-skills-your-agent-can-see.md) walk through it, and through what else the recordings found.

## Install

`axm` is one native binary, with no runtime to install.

- **Download** the archive for your platform (Linux x64, Windows x64 or macOS arm64) from the [latest release](https://github.com/hazeliscoding/axiomarium/releases/latest), check it against `SHA256SUMS`, and put `axm` on your `PATH`.
- **Or install the .NET tool**, which needs the .NET 10 SDK: `dotnet tool install -g Axiomarium`. To try it without installing, run `dnx Axiomarium doctor`.

Then, in any repo:

```text
axm doctor                     every instruction file, skill and hook the harnesses find, and what's wrong
axm explain src/app/main.cs    what Claude Code and Codex load for that file, what they drop, and its skills and hooks
axm triggers                   skills whose descriptions overlap, and the words they share
```

In a vault such as this repo, `axm list` and `axm validate` check the assets too, and `axm doctor` adds them to its report. `axm triggers generate <skill>` writes a skill's trigger prompts after you approve them, and `axm triggers test` runs them on Claude Code and Codex, on your own login. `axm eval run <asset>` runs an asset's eval cases and `axm eval compare <asset>` compares it with its last commit, each session in a sealed home that borrows only your logins and model. The [docs](docs/README.md) have the full command reference and the write-ups.

A few platform notes:

- On macOS, a binary downloaded with a browser is quarantined. Clear it with `xattr -d com.apple.quarantine axm`.
- The Linux binary is built on Ubuntu 24.04, so it needs that glibc or newer.
- On Windows, the .NET tool runs as `axm` in PowerShell and cmd, and as `axm.cmd` in Git Bash. The downloaded `axm.exe` works everywhere.

## Principles

- **Local first.** No account, hosted storage, telemetry or uploaded code. Agent configuration often holds sensitive repository context, so it stays on your machine.
- **Vendor neutral.** The canonical definitions belong to Axiomarium, not to Claude Code, Codex or Copilot. Thin adapters generate each harness's files.
- **Markdown where possible.** Markdown, YAML and JSON Schema, never a custom DSL.
- **Progressive disclosure.** Global rules, then contextual rules, then task-relevant skills, then a specialized agent. Not a 150 KB system prompt.
- **Evidence over confidence.** "`dotnet test` passed at commit abc123", not "I think this works".
- **A deterministic core.** Discovery, validation, `explain` and `doctor` never call a model. Only `eval`, `triggers`, `fossil` and `conflicts --judge` do, and only when you run them.
- **Recommend, don't rewrite.** `axm` suggests changes to your instructions. It writes files only when you ask it to.

## What it isn't

Axiomarium is not an AI IDE, an agent runtime, an autonomous coding agent, a prompt or MCP marketplace, an LLM gateway, a model router, an observability service or another giant collection of prompts. It manages the environment around agents.

## Using it

It's built for my own setup: Claude Code and Codex first, on .NET, Angular, PostgreSQL and Terraform projects. Each asset is one folder with a manifest, so it's easy to read or borrow even if you never run `axm`. Issues and ideas are welcome.

## License

[Apache-2.0](LICENSE)
