# Agent asset authoring

You're adding or changing an asset in an Axiomarium vault. An asset is one folder with a manifest, `asset.yaml`, and one Markdown content file. `axm validate` checks everything below, so follow it and the asset passes the first time.

## Before you write

Each of these questions can mean the asset shouldn't exist. Answer them first.

- **Could tooling enforce it instead?** A test, a lint rule, a database constraint or a hook beats an instruction the model may ignore. Prefer the tooling.
- **Does an asset already cover it?** Run `axm list`, and extend the existing asset instead of adding a near-duplicate.
- **For a skill:** when exactly should it activate? If you can't say it in one sentence, it will fire at the wrong time.
- **For a hook:** should it block, warn or only gather evidence? Prefer warning. Block only destructive actions.
- **For a policy:** what enforces each rule? If nothing does, say so rather than pretend.

## Pick the kind

| Kind | Folder | Content file | Block in `asset.yaml` |
|---|---|---|---|
| agent: a specialist with its own instructions | `agents/` | `agent.md` | none |
| skill: knowledge the model loads when relevant | `skills/` | `skill.md` | `skill`: `use_when` |
| hook: runs when the harness hits an event | `hooks/` | `hook.md` | `hook`: `event`, `command`, `response` |
| policy: rules for which decisions go where | `policies/` | `policy.md` | `policy`: `rules` |
| workflow: steps across several assets | `workflows/` | `workflow.md` | none |
| experiment: a hypothesis and its method | `experiments/` | `experiment.md` | none |

The folder is `<kind folder>/<name>/`, and the name is kebab-case. Create the folder with the asset, never as an empty placeholder.

## Write the manifest

```yaml
# yaml-language-server: $schema=../../schemas/asset.schema.json
name: release-notes
kind: skill
version: 0.1.0
description: >-
  Writes release notes from merged pull requests, grouped by user-visible change.
maturity: experimental
supports:
  claude-code: experimental
  generic: full
permissions:
  filesystem: read
  shell: read-only
  network: none
side_effects: none
inputs:
  - merged-pull-requests
outputs:
  - release-notes
evals:
  trigger: false
  behavioral: false
  regression: false
skill:
  use_when: >-
    Preparing a release, or asked to summarize what changed since the last tag.
```

- **name** matches the folder, and **kind** matches the kind folder.
- **version** is SemVer, starting at `0.1.0`. Bump it when the asset's behavior changes. Quote any version that YAML would read as a number, such as `"1.0"`.
- **description** says what the asset does, in one or two sentences.
- **maturity** starts at `experimental`. Raise it only when the evidence `registry/maturity.yaml` requires exists: usage entries in `docs/dogfooding.md` that link to the asset, and eval files. `axm validate` fails a claim without its evidence.
- **supports** lists only harnesses you have tried, each as `full`, `partial` or `experimental`. `generic` means the content works as plain instructions anywhere.
- **permissions** and **side_effects** state the most the asset needs and the worst it can change. Don't round down.
- **inputs** and **outputs** are kebab-case nouns.
- **evals**: set a flag to `true` only when the asset's `evals/<type>/` folder has files.
- **The block** for skills, hooks and policies:
  - a skill's `use_when` is the activation sentence from above;
  - a hook has an `event` (`session-start`, `before-edit` or `after-edit`), the `command` the harness runs, and a `response` (`block`, `warn` or `evidence`);
  - a policy has `rules`, each with a kebab-case `id`, the `rule` in one sentence, and `enforced_by`: the asset folders that enforce it, or `[]` when nothing does yet.

YAML is read with the 1.2 core schema, so `yes` and `on` are strings, not booleans.

## Write the content

- Address the agent that will read it: "You review…", "When you write code that…".
- Cover when to use it, what to do, what to produce, and the rules. Cut everything else: the content costs context every time it loads.
- Keep it vendor neutral. Never put harness files such as `.claude/`, `.agents/` or `.github/` in the asset; adapters generate those.

## Check it

Run `axm validate`. Each error names the file, the line and the field, with the allowed values or the missing evidence. Fix it and run it again until it reports 0 errors, then check that `axm list` shows the asset.
