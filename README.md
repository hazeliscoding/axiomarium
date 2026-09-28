<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/lockup-dark.svg">
    <img alt="Axiomarium" src="docs/brand/lockup.svg" height="40">
  </picture>
</h1>

<sub><i>ak·see·uh·<b>MAIR</b>·ee·um</i>: like aquarium, but for axioms</sub>

**Build, test, and debug your AI coding environment like software.**

Axiomarium is my lab for engineering reliable AI coding environments. It holds the agents, skills, hooks, policies, workflows and evals I use, and `axm`, a local CLI that inspects, validates, tests and debugs them. It treats agent configuration as real software infrastructure: kept in git, inspectable, testable, portable, and able to learn from its failures. It's built for my own setup first, and it's public in case it's useful to you too.

> **Status:** early development. v0.1 inspects and validates a vault of agent assets with `axm list`, `axm validate` and `axm doctor`, and `axm explain` follows in v0.2. See [ROADMAP.md](ROADMAP.md).

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

`axm doctor` works today, and `axm explain` is planned for v0.2. In a terminal the output is in color, with a kaomoji for the outcome. Piped, in CI or read by an agent, it's plain text.

![A terminal runs axm list, which shows the five starter assets. A one-line edit then raises the determinism auditor's maturity to tested, and axm doctor reports that the claim lacks its evidence.](docs/demo/doctor.gif)

`axm doctor` catches a broken asset before any agent loads it:

```text
$ axm doctor

AXM DOCTOR // 2 assets

  AGENTS
  01  determinism-auditor   experimental  0.1.0   OK
  02  scope-reviewer        ERROR

ERROR  agents/scope-reviewer/asset.yaml:5
       Unknown maturity: "production-ready"
       Allowed: experimental, incubating, tested, stable, battle-tested

2 assets · 1 error  (╥﹏╥)
```

`axm explain` shows what each harness loads for a file, and what it silently drops:

```text
$ axm explain src/api/orders/OrderService.cs

AXM EXPLAIN // src/api/orders/OrderService.cs

  CLAUDE CODE // launched at the repo root
  01  ~/.claude/CLAUDE.md        user                at launch
  02  CLAUDE.md                  project             at launch
  03  .claude/rules/backend.md   paths: src/api/**   when the file is read
  --  docs/testing.md            DROPPED     file is missing, imported by CLAUDE.md:12
  --  AGENTS.md                  DROPPED     a CLAUDE file exists and doesn't import it
  --  src/api/AGENTS.md          DROPPED     a CLAUDE file exists and doesn't import it

  CODEX // launched at the repo root
  01  ~/.codex/AGENTS.md         global              at launch
  02  AGENTS.md                  project             at launch
  --  src/api/AGENTS.md          NOT LOADED  below the launch directory

WARNING  agents-md-hidden
         Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
         Fix: Add @AGENTS.md to CLAUDE.md.

WARNING  dead-import
         CLAUDE.md:12 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place.
         Fix: Restore the file, or remove the import.

WARNING  agents-md-hidden
         Claude Code skips src/api/AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
         Fix: Add a CLAUDE.md next to it that says @AGENTS.md.

2 harnesses · 5 loaded · 4 not loaded · 3 warnings  (・_・;)
```

## Install

`axm` is one native binary, with no runtime to install.

- **Download** the archive for your platform (Linux x64, Windows x64 or macOS arm64) from the [latest release](https://github.com/hazeliscoding/axiomarium/releases/latest), check it against `SHA256SUMS`, and put `axm` on your `PATH`.
- **Or install the .NET tool**, which needs the .NET 10 SDK: `dotnet tool install -g Axiomarium`. To try it without installing, run `dnx Axiomarium doctor`.

Then, in a vault such as this repo:

```text
axm list
axm validate
axm doctor
```

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
