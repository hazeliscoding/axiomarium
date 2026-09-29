# Why assets carry manifests

A prompt file says what it tells the model. It doesn't say what it is, what it's allowed to touch, which tools it works in, or whether anyone ever checked that it works. So you can't validate it, compare two versions of it, or decide how far to trust it. You can only read it and hope.

Every asset in this vault is one folder: a Markdown file with the content, and an `asset.yaml` that says what the content can't.

```yaml
name: determinism-auditor
kind: agent
version: 0.1.0
description: >-
  Reviews a codebase for decisions that depend on LLM judgment but should be
  enforced by deterministic code, and says where the boundary belongs.
maturity: experimental
supports:
  claude-code: experimental
  codex: experimental
  generic: full
permissions:
  filesystem: read
  shell: none
  network: none
side_effects: none
evals:
  trigger: false
  behavioral: false
  regression: false
```

Skills, hooks and policies add a block named after their kind: a skill says when it should activate, a hook says when it runs and whether it blocks, warns or only records evidence, and each policy rule names what enforces it.

## Claims that can be checked

Each field is a claim, and the useful claims are the ones a tool can check. `axm validate` checks every asset, and fails CI when a claim doesn't hold:

- The manifest matches `schemas/asset.schema.json`, and its kind's block matches that block's schema.
- The name matches the folder, the kind matches the kind folder, and the content file exists and isn't empty.
- What the manifest points to exists: each asset a policy rule names in `enforced_by`, and the eval files behind each `evals` flag that is `true`.
- A skill's trigger prompts in `evals/trigger/prompts.yaml` match `schemas/trigger-prompts.schema.json`, name the skill's folder, and each prompt's `should_trigger` agrees with its kind.
- Each eval case is a kebab-case folder under `evals/behavioral/` or `evals/regression/` whose `eval.yaml` matches `schemas/eval.schema.json`. Each check is exactly one kind and takes only its own fields, and a regression case, and only a regression case, names the failure it guards.
- The maturity has the evidence its level requires.

Every error names the file, the line, the field, and what would be right:

```text
ERROR  agents/determinism-auditor/asset.yaml:8
       maturity "tested" lacks its evidence
       Needs 1 usage entry in docs/dogfooding.md, found 0.
       Needs behavioral evals, and evals.behavioral isn't true.
       Needs regression evals, and evals.regression isn't true.
       The evidence supports experimental. Lower the maturity, or add the evidence.
```

Some claims can't be checked yet, and it's worth being plain about which. `permissions` and `side_effects` are declared, not enforced. `supports` is the author's word until `axm explain` and `axm sync` can show an asset loading in each harness. And until v0.6, an eval counts once its files exist, whether or not it passes.

## Eval cases

A behavioral or regression eval is a folder under the asset's `evals/behavioral/` or `evals/regression/`. Its `eval.yaml` says what to ask and what to check, and a `repo/` beside it holds the files the session starts with, when it needs any. `axm eval` runs the case in a copy of `repo/` with the asset installed.

```yaml
# skills/agent-asset-authoring/evals/behavioral/new-hook/eval.yaml
prompt: Add a hook that warns when a database migration file changes. It runs after an edit.
allow:
  - axm validate
  - axm list
checks:
  - loaded: agent-asset-authoring
  - file: hooks/*/hook.md
  - file: hooks/*/asset.yaml
    contains: "response: warn"
  - run: axm validate
  - ran: git commit
    not: true
judge:
  rubric: The hook warns and never blocks.
```

A run passes when every check passes:

- `file` looks for a file that matches a glob, and with `contains`, for a text in it, in any case.
- `run` runs a command in the copy after the session, and expects its `exit`, 0 unless it says otherwise.
- `loaded`, `ran` and `reply` read what the harness recorded, never what the reply claims: the skills the session loaded and the agents it ran, the commands it ran, and its final message.
- `not: true` turns a check around.

`allow` lists the commands the session may run. A judge's rubric is graded by a model, shown as model judgment, and never changes what the checks decided. A regression case also says, in `guards`, which failure it guards against.

## What each maturity level promises

Maturity is earned, not declared. Each level promises something to whoever uses the asset, and requires evidence before an asset may claim it:

| Level | Promise | Evidence |
|---|---|---|
| experimental | An idea worth trying. It may change or go away. | none |
| incubating | Used on real work, and still changing. | 1 usage entry |
| tested | Evals pin down its behavior and guard against past failures. | 1 usage entry, behavioral and regression evals, and trigger evals for skills |
| stable | Used on real work again and again. Its behavior changes carefully. | 3 usage entries, and the evals `tested` needs |
| battle-tested | Proven on several real projects, not just one. | 5 usage entries across 3 repos, and the evals `tested` needs |

The levels live in `registry/maturity.yaml`, and `axm` embeds them, so every vault is judged by the same promises.

A usage entry is a dated section of `docs/dogfooding.md` that links to the asset's folder:

```markdown
## 2026-10-02 · carmine-workbench

Ran the [determinism auditor](../agents/determinism-auditor/) on the billing module.
It flagged a retry that could charge a card twice.
```

The heading starts with the date and names the repo. The entry counts for every asset folder it links to, and sections without a date never count. So raising an asset's level means writing down where it was used and what happened, which is the log I'd want anyway.
