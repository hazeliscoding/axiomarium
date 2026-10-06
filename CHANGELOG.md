# Changelog

This file records the user-visible changes to `axm` and the vault's schemas. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases follow [Semantic Versioning](https://semver.org/). Each asset also carries its own version in its `asset.yaml`.

## [Unreleased]

### Added

- `axiomarium.yaml` takes an `evidence` block: each check's name, the commands that count as running it, such as `dotnet test`, and the files it covers, every file git sees unless `covers` says otherwise. `axm doctor` checks it: a check named twice, a compound command such as `dotnet build && dotnet test`, or an invalid glob is an error at its line. `schemas/evidence.schema.json` describes the record of a check's latest run, kept in `.axm/evidence/`.
- `axm evidence` shows each declared check as FRESH (its latest run passed and the files it covers hold what they held then), STALE (with the files that changed since, up to three named), FAILED or MISSING, with when it ran and the command behind it. `--json` prints it as JSON, shape 1. It needs a git repo, and reads git's list of files.
- `axm evidence check [<name>...]` prints only the checks that aren't fresh and exits 1 when one isn't: a gate for a pre-commit hook.
- `axm evidence record <name> [-- <command>...]` runs a check's command directly, not through a shell, with its output on your terminal, then records how it ended and a hash of each file the check covers. It refuses a command that doesn't count as running the check, exits with 0 or 1 as the command passed or failed, and with 2, recording nothing, when the program can't start.

### Fixed

- `axm explain` and `axm doctor` read Codex's trust entries in `~/.codex/config.toml` the way Codex does. A hook's entry counts only under the key Codex writes: the hooks file's path, with backslashes on Windows, under `CODEX_HOME` spelled as Codex canonicalizes it, with its links followed and, on Windows, its case as on disk. A `[projects]` entry, trusted or untrusted, counts for a folder or the folder its links lead to, as written, with no trailing separator and, on Windows, with backslashes but in any case. Before, an entry with forward slashes on Windows, or a project entry with a trailing separator, counted where Codex ignores it, so `codex-hook-untrusted` missed hooks that never run and an untrusted project's AGENTS.md files still loaded; and a `CODEX_HOME` reached through a link, such as `/tmp` on macOS, hid trust entries Codex honors.
- `axm eval run` and `axm eval compare` give Claude Code's sessions and judge a temp folder inside the run. Before, each session left a folder in your own temp folder, such as `%TEMP%\claude\C--axm-evals-…`.

## [0.5.0] - 2026-09-30

Evals: what an asset does once it's active, measured on the real Claude Code and Codex in a sealed home, and whether a change made it better.

### Added

- `schemas/eval.schema.json`: an asset's eval cases, each a folder under `evals/behavioral/` or `evals/regression/` with an `eval.yaml` (the prompt, the commands the session may run, the checks and an optional judge's rubric) and a `repo/` for the files the session starts with. A check is one of `file`, `run`, `loaded`, `ran` or `reply`, and `not: true` turns it around. A regression case names the failure it guards in `guards`.
- `axm eval run [<asset>...]` runs each asset's eval cases on Claude Code and Codex, each session in a sealed home with only your logins and chosen model, and in its own git copy of the case's repo with the asset installed. It reports how many runs passed, each failed check, and the median and range of tokens, time and tool calls, plus turns and cost on Claude Code. `--json` prints it as JSON, shape 1, and each asset's part is saved in `.axm/evals/`. On Windows, one unscored Codex session starts Codex's sandbox first. Each Codex command is timed from Codex's own records, with each case's slowest named, and every path outside the copy that a run's commands touched is named, since Codex on Windows can read the whole disk.
- `axm eval compare <asset>` runs an asset's cases on a baseline, its files at a git ref or `--baseline none`, and on the working tree, with sessions alternating, and shows each case's passed runs and measures side by side with the change. It reuses a saved baseline that still matches, unless `--fresh`, and saves each version's runs in `.axm/evals/`. `--json` prints it as JSON, shape 1.
- An eval case's `judge.rubric` is graded by a model for each finished run, from the prompt, the commands, the copy's git diff and the final message, in the sealed home and through Claude Code unless `axm eval run --judge-with codex` says otherwise. Its verdicts are shown as model judgment, apart from the checks, which alone decide a run.
- `axm conflicts <path> --judge` has a model find instructions a file gets that contradict each other, through Claude Code or, with `--judge-with codex`, Codex. It keeps only the contradictions whose two passages the model quotes from the files, each with its `file:line`, drops and counts the rest, and labels everything as model judgment.
- The `agent-asset-authoring` skill, 0.2.0, tells the user which tool should enforce a rule, such as a pre-commit hook or a CI check, before writing anything. On Codex with gpt-6-sol, the judge passed its `prefers-tooling` case in 2 of 3 runs, where 0.1.0 passed in none.
- The four starter assets have behavioral evals, and set `evals.behavioral: true`: three cases for the `agent-asset-authoring` skill (a new hook, a new skill, and a rule tooling should enforce), one each for the `scope-sheriff` and `session-doctor` hooks, and one for the `determinism-auditor` agent.
- `axm validate` and `axm doctor` check every eval case: its schema, that each check is exactly one kind and takes only its own fields, that each `file` glob is valid, and that the case is a kebab-case folder with its `eval.yaml`.

### Changed

- Files directly in an asset's `evals/behavioral/` or `evals/regression/` are errors: each case is now a folder with an `eval.yaml`.

## [0.4.0] - 2026-09-29

Trigger testing: whether the agent picks the right skill for a prompt, measured on the real Claude Code and Codex.

### Added

- The `agent-asset-authoring` skill has trigger prompts in `evals/trigger/prompts.yaml`, 17 of them, written by `axm triggers generate` and reviewed, and sets `evals.trigger: true`.
- `axm triggers export <skill> --format skill-creator|promptfoo` prints a vault skill's trigger prompts for other skill-eval tools: the skill-creator's list of `{query, should_trigger}`, or a `promptfooconfig.yaml` with a provider for each harness the skill supports and a `skill-used` or `not-skill-used` assertion for each prompt. It writes nothing.
- `axm triggers test [<skill>...]` runs each vault skill's trigger prompts on Claude Code and Codex, three times each by default, and reports which skill each run picked. The sessions run four at a time in a throwaway copy of the repo with the vault's skills written in, on your real home, so every skill you have competes, and each stops at its first action that isn't loading a skill. It reports precision and recall for each skill and harness, counted in runs, and every collision, false trigger and miss with a cause from the listing, never from asking the model. A run counts a skill as picked when it loaded it before its first other action, whatever it loaded first. A skill loaded before every task, such as a plugin's skill that tells the agent to use skills first, is named as background, and a listing over its budget is noted once for its harness. A harness that isn't installed is skipped, and sessions that gave no answer are listed and not scored. `--harness`, `--runs` and `--model` narrow it down.
- `axm triggers generate <skill>` has Claude Code write trigger prompts for a vault skill: positive, paraphrased, negative and adversarial ones, and ambiguous ones aimed at the listed skills it overlaps most. It asks for one turn with tools off, checks the answer against the schema and retries once, shows every prompt grouped by kind, and writes `evals/trigger/prompts.yaml` only after you approve, labeled with the model and date. It refuses before calling the model when the file exists (unless `--replace`) or there's no terminal to ask in (unless `--yes`). `--model` picks the model. It never edits `asset.yaml`.
- `schemas/trigger-prompts.schema.json`: a skill's trigger prompts in `evals/trigger/prompts.yaml`, each a prompt with its kind (positive, paraphrased, negative, adversarial or ambiguous) and whether it should pick the skill, and a `generated` block naming the harness, model and date when a model wrote them. `axm doctor` and `axm validate` report a file that doesn't match the schema, names another skill, has a prompt whose `should_trigger` contradicts its kind, or gives a rival to a prompt that isn't ambiguous.

## [0.3.0] - 2026-09-28

Skills and hooks: which skills Claude Code and Codex list for the model, which hooks run, and what keeps them from reaching it.

### Added

- `axm explain` shows, for each harness, the skills it lists for the model and the hooks that run for the file. A skill is available, never loaded: each row says whether it's listed at launch or joins when the file is read or edited, and the rule that lists it. A skill the harness finds but doesn't list says why, and the listing's size is shown against its budget, with what the budget assumes. Hooks show at session start, before an edit and after one, each RUNS or NOT RUN with the reason, such as an `if` that doesn't match or a Codex hook that isn't trusted. `--diff` also shows the skills only one harness lists, and `--json` gains `skills`, `notListed`, `listing` and `hooks` for each harness, still `schemaVersion` 1. The models cover Claude Code's managed, personal, project, nested, path-scoped, plugin and synced skills and its settings and plugin hooks, and Codex's skill roots, policies and hook trust, confirmed against Claude Code 2.1.284 and Codex 0.156.1.
- `axm doctor` lists the skills the harnesses list and the hooks they have configured, in HARNESS SKILLS and HARNESS HOOKS blocks after the instruction files, and counts them in its summary. The repo's own skills and hooks get a row each, with the harnesses that list a skill or why none does, and why a hook can't run. Those from outside the repo, such as personal and plugin skills, take one row per harness and source.
- Seven skill and hook findings, all warnings, shown by `axm explain` and `axm doctor` and passed on by `session-doctor`. `skill-name-clash` finds two skills with one name, `skill-description-cut` a description the harness cuts, `skill-listing-over-budget` a listing over its budget (stating what it assumes), `skill-frontmatter-invalid` a `SKILL.md` a harness can't use, `skill-paths-match-nothing` a skill whose `paths` match no file, `hook-never-runs` a hook that is set up so it never runs, and `codex-hook-untrusted` Codex hooks that aren't trusted. Each is documented in `findings/<id>/finding.md` with fixtures. Files the user can't edit, such as plugin, bundled and managed skills and hooks, are left out, and clashes between the same two folders make one finding.
- `axm triggers` finds skills whose descriptions overlap, in the text each harness shows the model, with no model call. For Claude Code and for Codex, launched at the repo root, it compares every listed skill and the vault's skills as sync would list them, and shows each pair that shares rare terms, with its score, the terms and both files. Overlap is shared wording, not proof the agent confuses the two, so it always exits 0.
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

[Unreleased]: https://github.com/hazeliscoding/axiomarium/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/hazeliscoding/axiomarium/releases/tag/v0.5.0
[0.4.0]: https://github.com/hazeliscoding/axiomarium/releases/tag/v0.4.0
[0.3.0]: https://github.com/hazeliscoding/axiomarium/releases/tag/v0.3.0
[0.2.0]: https://github.com/hazeliscoding/axiomarium/releases/tag/v0.2.0
[0.1.0]: https://github.com/hazeliscoding/axiomarium/releases/tag/v0.1.0
