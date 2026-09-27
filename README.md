<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/lockup-dark.svg">
    <img alt="Axiomarium" src="docs/brand/lockup.svg" height="40">
  </picture>
</h1>

**Build, test, and debug your AI coding environment like software.**

Axiomarium is my lab for engineering reliable AI coding environments. It holds the agents, skills, hooks, policies, workflows and evals I use, and `axm`, a local CLI that inspects, validates, tests and debugs them. It treats agent configuration as real software infrastructure: kept in git, inspectable, testable, portable, and able to learn from its failures. It's built for my own setup first, and it's public in case it's useful to you too.

> **Status:** planning. There is nothing to install yet. See [ROADMAP.md](ROADMAP.md).

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

**The vault** holds agents, skills, hooks, policies, workflows, schemas, evals, experiments and write-ups. It is all Markdown, YAML and JSON Schema, so you can read it without `axm`. Every asset carries a manifest that says what it is, which harnesses it supports, what it's allowed to touch, how it's tested and how far it can be trusted.

**`axm`** is the engineering around the vault:

```text
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

The output below is planned, not built yet. In a terminal it's in color, with a kaomoji for the outcome. Piped, in CI or read by an agent, it's plain text.

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
$ axm explain src/api/orders/OrderService.cs --harness all

CLAUDE CODE // launched at repo root
  01  ~/.claude/CLAUDE.md          user                at launch
  02  CLAUDE.md                    project             at launch
  03  .claude/rules/backend.md     paths: src/api/**   when the file is read
  --  AGENTS.md                    DROPPED  CLAUDE.md exists and doesn't import it
  --  src/api/AGENTS.md            DROPPED  CLAUDE.md exists and doesn't import it
  --  docs/testing.md              DROPPED  imported by CLAUDE.md:12, file is missing

CODEX // launched at repo root
  01  ~/.codex/AGENTS.md           global              at launch
  02  AGENTS.md                    project             at launch
  --  src/api/AGENTS.md            NOT LOADED  below the launch directory

WARNING  agents-md-hidden
         Codex reads AGENTS.md. Claude Code skips it, because CLAUDE.md doesn't import it.
         Fix: add @AGENTS.md to CLAUDE.md.

WARNING  dead-import
         CLAUDE.md:12 imports docs/testing.md, which does not exist.
         Fix: restore the file, or remove the import.

2 harnesses · 5 loaded · 4 not loaded · 2 warnings  (・_・;)
```

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
