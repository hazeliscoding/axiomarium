# Changelog

This file records the user-visible changes to `axm` and the vault's schemas. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases follow [Semantic Versioning](https://semver.org/). Each asset also carries its own version in its `asset.yaml`.

## [Unreleased]

### Added

- `axm explain` shows, for each harness, the skills it lists for the model and the hooks that run for the file. A skill is available, never loaded: each row says whether it's listed at launch or joins when the file is read or edited, and the rule that lists it. A skill the harness finds but doesn't list says why, and the listing's size is shown against its budget, with what the budget assumes. Hooks show at session start, before an edit and after one, each RUNS or NOT RUN with the reason, such as an `if` that doesn't match or a Codex hook that isn't trusted. `--diff` also shows the skills only one harness lists, and `--json` gains `skills`, `notListed`, `listing` and `hooks` for each harness, still `schemaVersion` 1. The models cover Claude Code's managed, personal, project, nested, path-scoped, plugin and synced skills and its settings and plugin hooks, and Codex's skill roots, policies and hook trust, confirmed against Claude Code 2.1.284 and Codex 0.156.1.
- `axm doctor` lists the skills the harnesses list and the hooks they have configured, in HARNESS SKILLS and HARNESS HOOKS blocks after the instruction files, and counts them in its summary. The repo's own skills and hooks get a row each, with the harnesses that list a skill or why none does, and why a hook can't run. Those from outside the repo, such as personal and plugin skills, take one row per harness and source.
- `axm explain` says when a Claude Code skill is listed with its first line because it has no usable description, and why Codex can't read a `SKILL.md`. `--json` gains `fallback` on a skill and `detail` on a skill that isn't listed.

### Fixed

- A Claude Code rule's `paths` match the way `.gitignore` lines do, as in Claude Code: a pattern without a slash, such as `*.cs`, matches at any depth, and `api/**` matches an `api` folder anywhere. `axm explain` said such rules don't load when they do, and `rule-matches-nothing` could report them.
- A Claude Code rule whose frontmatter doesn't parse loads for every file, as the docs say, and `axm explain` now shows it loaded. `rule-frontmatter-invalid` said such a rule never loads. Claude Code quotes values that look like YAML syntax and parses again first, so the finding now fires only on YAML that still doesn't parse, such as a line indented wrongly, and an unclosed bracket in `paths` becomes a pattern that matches nothing, which `rule-matches-nothing` reports.

## [0.2.0] - 2026-09-28

Instruction intelligence: what Claude Code and Codex actually load for a file, what they silently drop, and why.

### Added

- `axm explain <path>` shows which instruction files Claude Code and Codex load for a file, in context order, and which they drop, each with the rule that decides it. Rows say whether a file loads at launch or when the file is read, which `paths` patterns matched, which line imported it, and whether Codex's byte budget cut it. A file a harness leaves to the model shows as NOT LOADED, never as dropped. `--harness` picks one harness, `--cwd` sets the launch directory (the repo root by default), `--diff` shows only the files one harness loads, and `--json` prints the same result as JSON with `schemaVersion` 1. The models are confirmed against Claude Code 2.1.284 and Codex 0.156.1.
- Nine instruction findings, shown by `axm explain`. `dead-import`, `import-too-deep`, `agents-md-hidden`, `rule-frontmatter-invalid`, `rule-matches-nothing`, `codex-byte-cap`, `codex-empty-override` and `dead-link` are warnings, and `duplicate-block` is info. Each says what is wrong, why it matters and what to do, and each is documented in `findings/<id>/finding.md`, with one fixture that fires it and one that doesn't. `explain` still exits 0 when it finds something, and its JSON lists the findings under `findings`.
- `axm hook session-doctor`, a Claude Code SessionStart hook, and the `session-doctor` hook asset that documents it. When a session starts in a repo where `axm doctor` finds an error or a warning, it shows the user a one-line summary and gives the model every problem with its fix. It says nothing when all is well, and never blocks. `hook.md` has the settings that wire it in.
- `axiomarium.yaml` at the repo root configures `axm` for the repo, checked against `schemas/axiomarium.schema.json`. `doctor.ignore` lists globs for instruction files that are broken on purpose, such as test fixtures.

### Changed

- `axm doctor` works in any repo. It checks the instruction files from the repo root, the nearest folder above with a `.git`, and lists each one with the harnesses that load it, followed by the findings. It still checks the vault when `--root` or the repo root holds one. Findings are warnings or info, so they never fail it, but a problem in `axiomarium.yaml` is an error. `validate` still checks only the vault.

## [0.1.0] - 2026-09-28

The asset model: agent configuration can be inspected and validated like software.

### Added

- `axm doctor` finds every asset in `agents/`, `skills/`, `hooks/`, `policies/`, `workflows/` and `experiments/` and reports its health. Each error names the file, the line, the field and the allowed values, and a broken manifest never stops the rest of the vault from being checked.
- `axm validate` runs the same checks and prints only the problems and a summary line, for CI and hooks. Both render one result, so they never disagree.
- `axm list` shows the vault's assets by kind, with maturity, version and the harnesses each supports. `--kind` and `--harness` filter it, and `--harness codex` is the compatibility table for Codex. An invalid asset is still listed, as `INVALID`.
- `schemas/asset.schema.json`: the manifest every asset carries, with its name, kind, version, description, maturity, harness support, permissions, side effects, inputs, outputs and evals.
- Skills, hooks and policies carry a block named after their kind, checked against `schemas/skill.schema.json`, `hook.schema.json` and `policy.schema.json`. A skill says when it should activate (`use_when`). A hook says when it runs, what it runs and whether it blocks, warns or only records evidence. Each policy rule names what enforces it. A block on the wrong kind of asset is an error.
- Each asset has a content file named after its kind (`agent.md`, `skill.md`, `hook.md`, `policy.md`, `workflow.md` or `experiment.md`), and it must not be empty. File names must match exactly, including case, so a vault means the same thing on every platform. A near miss such as `Agent.md` gets a rename hint.
- A valid manifest's references are checked. Each `enforced_by` must name an asset in the vault, and each `evals.<type>: true` needs at least one file in the asset's `evals/<type>/` (hidden files such as `.gitkeep` don't count).
- `registry/maturity.yaml` says what each maturity level promises and the evidence it needs, and `axm` fails an asset that claims a level without that evidence. The error lists what's missing and the level the evidence does support. Usage evidence comes from dated entries in `docs/dogfooding.md` that link to the asset.
- `axm hook scope-sheriff`, a Claude Code `PostToolUse` hook for Write, Edit and NotebookEdit. When `.axm/scope` declares the task's scope (one glob per line: `*`, `**`, `?`, `{a,b}`, and a trailing `/` for everything under a folder), an edit outside it asks the agent why, through `additionalContext`. It never blocks, stays silent without a scope, and exits with 1, never 2, when it can't run.
- Manifests are read with the YAML 1.2 core schema, so `yes` and `on` stay strings, and a number such as `1.0` keeps its source text.
- Exit codes: 0 when everything passed, 1 when errors were found, and 2 when the command couldn't run.
- Terminal output in color, with a kaomoji for the outcome. Output is plain when stdout isn't a terminal or `AXM_PLAIN` is set, and `NO_COLOR` turns color off.
- NativeAOT binaries for Linux x64, Windows x64 and macOS arm64 on GitHub Releases, with `SHA256SUMS`, and the `Axiomarium` dotnet tool on NuGet.
- Five starter assets, all experimental:
  - the `determinism-auditor` agent, which finds decisions an LLM shouldn't own and suggests the deterministic boundary;
  - the `agent-asset-authoring` skill: when an asset should exist at all, how to pick its kind, a manifest to copy, and how to get it through `axm validate`;
  - the `scope-sheriff` hook: the `.axm/scope` contract, the Claude Code settings that wire it in, and its limits;
  - the `deterministic-boundaries` policy: nine rules for which decisions belong to code instead of the model, eight of them enforced by the determinism auditor;
  - the `prompt-fossil` experiment: the method for finding instructions that cost tokens but no longer change behavior. Designed, not run yet.

[Unreleased]: https://github.com/hazeliscoding/axiomarium/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/hazeliscoding/axiomarium/releases/tag/v0.2.0
[0.1.0]: https://github.com/hazeliscoding/axiomarium/releases/tag/v0.1.0
