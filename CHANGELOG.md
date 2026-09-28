# Changelog

This file records the user-visible changes to `axm` and the vault's schemas. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases follow [Semantic Versioning](https://semver.org/). Each asset also carries its own version in its `asset.yaml`.

## [Unreleased]

### Added

- `axm doctor` finds every asset in `agents/`, `skills/`, `hooks/`, `policies/`, `workflows/` and `experiments/` and validates its `asset.yaml`. Each error names the file, the line, the field and the allowed values. A broken manifest never stops the rest of the vault from being checked.
- `axm doctor` checks that each asset has a content file named after its kind (`agent.md`, `skill.md`, `hook.md`, `policy.md`, `workflow.md` or `experiment.md`), and that the file isn't empty. File names must match exactly, including case, so a vault means the same thing on every platform. A near miss such as `Agent.md` gets a rename hint.
- `schemas/asset.schema.json`: the manifest every asset carries, with its name, kind, version, description, maturity, harness support, permissions, side effects, inputs, outputs and evals.
- Skills, hooks and policies carry a block named after their kind, checked against `schemas/skill.schema.json`, `hook.schema.json` and `policy.schema.json`. A skill says when it should activate (`use_when`). A hook says when it runs, what it runs and whether it blocks, warns or only records evidence. Each policy rule names what enforces it. A block on the wrong kind of asset is an error.
- `axm doctor` checks what a valid manifest points to. Each `enforced_by` must name an asset in the vault, and each `evals.<type>: true` needs at least one file in the asset's `evals/<type>/` (hidden files such as `.gitkeep` don't count).
- Manifests are read with the YAML 1.2 core schema, so `yes` and `on` stay strings, and a number such as `1.0` keeps its source text.
- Exit codes: 0 when everything passed, 1 when errors were found, and 2 when the command couldn't run.
- Terminal output in color, with a kaomoji for the outcome. Output is plain when stdout isn't a terminal or `AXM_PLAIN` is set, and `NO_COLOR` turns color off.
- `axm --version`.
- The `determinism-auditor` agent.
