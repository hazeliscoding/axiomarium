# Roadmap

Axiomarium is my lab for building, testing and debugging AI coding environments like software. It has two parts: the vault (agents, skills, hooks, policies, workflows and evals) and `axm`, a .NET CLI that inspects, validates, tests and debugs them. This file tracks what gets built, in what order, and the decisions already made.

## Decisions (2026-09-27)

### Direction

- **Personal first, public by default.** It's built for my own setup: Claude Code and Codex first, on .NET, Angular, PostgreSQL and Terraform projects. It's public in case it helps someone else. Each piece ships with a demo and a write-up in `docs/`, so the repo also shows how I work.
- **Thesis: failures should become tests, not paragraphs.** Agent configuration is software infrastructure. When an agent fails, the answer is a guardrail and a regression test, not more prose in CLAUDE.md.
- **The asset model comes first.** v0.1 proves that agent configuration can be inspected and validated like software: manifests, a schema, a registry and a handful of real assets. The instruction compiler (`axm explain`) follows in v0.2, and needs the asset model to show which of the vault's skills and hooks apply to a file.
- **A small coherent system before a big library.** Five good starter assets, not forty skills.
- **Planning lives in this file.** There is no GitHub Project board. Each milestone is an epic, and its checkboxes are the tasks.

### Principles

- **Local first.** No account, hosted storage, telemetry, SaaS backend or uploaded code.
- **Vendor neutral.** Assets are canonical and belong to Axiomarium, not to any harness. Thin adapters generate the files for Claude Code, Codex, Copilot and a generic target.
- **Markdown where possible.** Assets are Markdown, YAML and JSON Schema, readable without `axm`. No custom DSL.
- **Progressive disclosure.** Global rules, then contextual rules, then task-relevant skills, then a specialized agent.
- **Evidence over confidence.** Claims like "tests pass" carry the command, the result and the tree they ran on.
- **A deterministic core.** Discovery, validation, `explain`, `doctor` and `conflicts` never call a model or the network. Only `eval`, `triggers`, `fossil` and `conflicts --judge` call models, only when the user runs them, and never through one provider's API alone.
- **Writes only on request.** Diagnostic commands are read-only. `init`, `sync` and `incident new` show what they will write and wait for approval. `fossil` and `distill` recommend changes and never delete or rewrite instructions.
- **Hooks prefer explanation to blocking.** A hook first asks the agent for a reason and records evidence. It blocks only when an action is destructive.

### Build and release

- **Stack:** .NET 10 with NativeAOT, System.CommandLine, YamlDotNet and xunit.v3. NativeAOT keeps startup fast for hooks that run on every session or tool call. Rust was considered, but .NET stays because it is my main stack and versioned releases solve distribution.
- **Manifests are validated by our own small validator** for the subset of JSON Schema 2020-12 our schemas use, and the schema files stay the single source of truth, embedded in the binary. JsonSchema.Net moved to a maintenance-fee EULA, which is friction for anyone using `axm` at work, and Corvus compiles validators at runtime, which NativeAOT can't do. A test fails if a schema uses a keyword the validator doesn't support.
- **No Spectre.Console.** The output is simple enough for a plain writer with ANSI colors, as in pgcheckup, and startup stays fast for hooks. `axm doctor` on this repo takes about 60 ms.
- **YAML follows the 1.2 core schema:** `yes` and `on` stay strings, and numbers keep their source text, so an error can say `Quote it: "1.0"`.
- **The version is `0.1.0-dev`** until the v0.1.0 release.
- **Projects:** `Axiomarium.Core` (assets, schemas, registry, instruction resolution) and `Axiomarium.Cli`. `Axiomarium.Eval` and `Axiomarium.Adapters` arrive with their milestones.
- **The command is `axm`.** `axiom` would collide with the CLI of Axiom (axiom.co), and their npm package `axiom` is an AI evals SDK in the same space.
- **Versioned releases.** Each milestone from M1 on is a release (v0.1, v0.2 …), tagged `vX.Y.Z`, with NativeAOT binaries for `win-x64`, `linux-x64` and `osx-arm64` on GitHub Releases, `SHA256SUMS`, the `Axiomarium` dotnet tool on NuGet, and a `CHANGELOG.md` entry. Each asset also carries its own SemVer `version` in its manifest.
- **Storage:** configuration lives in git (`axiomarium.yaml`, `registry/`, `evals/`, `incidents/`). SQLite is only for disposable local state in `.axm/cache.db`: eval history, hashes, evidence and cached scans. The repo never needs the database to be understood.

### Assets

- **One folder per asset,** with an `asset.yaml` manifest that validates against `schemas/asset.schema.json`, and the asset's content in Markdown. Folders appear with their first asset, never as empty placeholders.
- **The manifest** declares name, kind, version, description, maturity, support per harness (`full`, `partial` or `experimental`), permissions (filesystem, shell, network), side effects, inputs, outputs and which evals exist.
- **Maturity is earned.** Experimental: an interesting idea with few or no evals. Incubating: used successfully and still changing. Tested: behavioral and regression evals exist. Stable: behavior changes carefully. Battle-tested: used repeatedly on real projects, with accumulated regression coverage. `axm doctor` checks each asset has the evidence its level requires.

### Instruction compiler (v0.2)

- **Claude Code and Codex first.** Two harnesses are enough to show that the same file gets different instructions. Copilot comes with the adapters in v0.8.
- **Harness models follow the docs, then the real harness.** Each model records the docs it was built from and the harness version it was confirmed against. The first models follow the Claude Code memory docs and the Codex AGENTS.md docs as read on 2026-09-27.
- **Dropped files are output, not silence.** A resolution has three parts: loaded (in context order, with reason, timing and bytes), dropped (with the reason) and findings.
- **"Effective for a file"** means what the harness loads at launch from the launch directory, plus what it loads on demand when the agent reads that file. The launch directory defaults to the repo root and is set with `--cwd`.
- **Skills are available, not loaded.** The model decides when a skill loads, so `explain` lists matching skills as available and never claims they loaded.
- **Contradictions need judgment.** Duplicates, dead references and shadowed files are found deterministically. Contradictions between rules come from `axm conflicts --judge`, which asks a model and labels its output as model judgment.
- **The JSON output is the contract.** Snapshot tests use it, and the Spectre tree is rendered from the same resolution.
- **Tests never read the real machine.** The home, `CODEX_HOME` and managed-policy directories are injected, and fixtures supply them.
- **One folder per finding,** as in pgcheckup: `findings/<id>/finding.md` plus `fixtures/fires/` and `fixtures/clean/`, each a tiny repo. Findings also run as part of `axm doctor`.
- **People who don't read code get the warning without asking.** A Claude Code SessionStart hook runs `axm doctor`, so anyone who steers their agent only through instruction files learns when one silently doesn't load.

### Brand

- **Brand follows the KAIRO design system.** KAIRO has no drawn logo: the name, set in Saira Condensed 600, is the mark.
- **The logo is option 1B, "Bracketed":** the wordmark inside corner brackets, KAIRO's mark for the object in focus. Pink replaces KAIRO's signal red: `#f0569b` on dark backgrounds and `#c2185b` on light ones. The mark is an "A" in the same brackets. The lockup has no `AXM/CLI` tag, because the tag can't be read at README size.
- **CLI output is colorful and a little playful.** KAIRO's structure stays: uppercase section labels, `//` separators, zero-padded indices and severity as a word. On top of it go colors from the brand palette and a kaomoji that matches the outcome (`ヽ(・∀・)ﾉ` all clear, `(╥﹏╥)` errors). The fun never carries meaning on its own: every kaomoji sits next to words that say the same thing. This departs from KAIRO's "no emoji, never jokey" voice on purpose.
- **Plain output for machines.** When stdout isn't a terminal (pipes, CI, hooks, agents), or `AXM_PLAIN` is set, output has no color and no kaomoji. `NO_COLOR` turns color off. Terminal output is exactly the plain output plus color and kaomoji, and a test holds that.

## M0: Day 0 (as soon as possible)

- [x] Add `LICENSE` (Apache-2.0), `.gitignore` and `.gitattributes`.
- [x] Write `README.md`, `ROADMAP.md`, `AGENTS.md` and `CLAUDE.md`.
- [x] Brand: pick a KAIRO wordmark option, export `mark.svg` and `lockup.svg` with `-dark` variants to `docs/brand/`, and add the `<picture>` header to the README. Convert the text to paths.
- [x] Scaffold the solution: `Directory.Build.props` (nullable on, warnings as errors, XML docs required, NativeAOT), central package management, and the `Axiomarium.Core`, `Axiomarium.Cli` and test projects.
- [x] Prove that YAML parsing and JSON Schema validation work in a published NativeAOT binary, with no trim or AOT warnings.
- [x] Write `schemas/asset.schema.json`.
- [x] `axm --version`, and a first `axm doctor` that discovers assets and validates their manifests.
- [x] Add the first real asset: `agents/determinism-auditor/`.
- [ ] CI: build, test and format check on Linux, Windows and macOS, plus a NativeAOT publish on each.
- [x] Guardrail: a test fails if `Axiomarium.Core` references `System.Console`, Spectre.Console or `System.Net.Http`.

**Done when:** CI is green on all three platforms, `axm doctor` validates the determinism auditor, a test PR that breaks its manifest makes `axm doctor` fail and name the field, and a test PR that adds an `HttpClient` to `Axiomarium.Core` fails the build.

## M1: v0.1, asset model

Agent configuration can be inspected and validated like software.

- [ ] Discovery across `agents/`, `skills/`, `hooks/`, `policies/`, `workflows/` and `experiments/`. Manifest errors name the file, the field and the allowed values.
- [ ] Schemas: `asset`, `agent` and `policy`. The `evidence` and `incident` schemas come with their milestones.
- [ ] Registry: `registry/catalog.yaml`, `compatibility.yaml` and `maturity.yaml`, with the evidence each maturity level requires.
- [ ] Commands:
  - [ ] `axm list`: assets grouped by kind, with maturity and version.
  - [ ] `axm validate`: schema and reference checks, with exit codes for CI.
  - [ ] `axm doctor`: asset counts, schema and reference health, and maturity claims that lack evidence.
  - [ ] `axm inspect`: the repo's languages, frameworks and detected agent harnesses.
  - [ ] `axm init`: shows the `axiomarium.yaml` it would write, then writes it on approval.
- [ ] Five starter assets:
  - [ ] `determinism-auditor` (agent): finds decisions an LLM shouldn't own, such as authorization, billing, irreversible actions, state transitions, invariants, retries and idempotency, and suggests the deterministic boundary.
  - [ ] `agent-asset-authoring` (skill): how to write an asset and its manifest.
  - [ ] `scope-sheriff` (hook, experimental): warns when an edit leaves the task's expected scope and asks the agent to explain why.
  - [ ] `deterministic-boundaries` (policy): which decisions belong to code, not to the model.
  - [ ] `prompt-fossil` (experiment): the write-up and method that v0.7 builds on.
- [ ] Write-up: why assets carry manifests, and what each maturity level promises.
- [ ] Release v0.1.0: the release workflow, `CHANGELOG.md`, and install steps in the README.

**Done when:** the five starter assets pass `axm validate`, breaking any manifest field makes `axm doctor` name the file, the field and the allowed values, and v0.1.0 installs from the release on a clean machine.

## M2: v0.2, instruction intelligence

- [ ] Claude Code model:
  - [ ] the launch-time chain: managed policy, `~/.claude/CLAUDE.md` and `~/.claude/rules/`, then each directory from the filesystem root down to the launch directory (`CLAUDE.md`, `.claude/CLAUDE.md`, then `CLAUDE.local.md`), then `.claude/rules/**/*.md` without `paths`;
  - [ ] `@path` imports: relative, absolute and `~` paths, at most 4 hops, skipped inside code spans and fenced blocks, cycles detected, and external imports marked as needing approval;
  - [ ] on-demand files for the target: subdirectory CLAUDE files, and rules whose `paths` match, including brace expansion, invalid patterns and invalid frontmatter;
  - [ ] AGENTS.md under each Project instructions mode, `claudeMdExcludes`, and HTML comments stripped before bytes are counted.
- [ ] Codex model:
  - [ ] the global file in `$CODEX_HOME`, then the chain from the git root down to the launch directory, one file per directory (`AGENTS.override.md`, then `AGENTS.md`, then the fallback filenames);
  - [ ] `project_doc_max_bytes`: files past the limit are dropped. Confirm against the real Codex whether the file that crosses the limit is cut or dropped whole;
  - [ ] files below the launch directory are reported as "not loaded by the harness".
- [ ] `axm explain <path>`: loaded and dropped files with reasons, the vault's matching skills (available) and hooks (active), `--harness`, `--diff` and `--json`.
- [ ] `axm conflicts`: duplicate blocks, dead references and shadowed files, plus `--judge` for contradictions.
- [ ] Findings, each with `finding.md` and fires and clean fixtures, also run by `axm doctor`:

| Finding | Fires when |
|---|---|
| `dead-import` | An `@path` import points to a file that doesn't exist |
| `import-too-deep` | An import chain goes past 4 hops, so the rest never loads |
| `agents-md-hidden` | Claude Code skips an AGENTS.md because a CLAUDE file exists and doesn't import it |
| `rule-frontmatter-invalid` | A rule's YAML doesn't parse, so the rule loads for every file |
| `rule-matches-nothing` | A path-scoped rule matches no file in the repo, or one of its patterns is invalid |
| `codex-byte-cap` | The Codex chain reaches `project_doc_max_bytes`, so later files are dropped |
| `duplicate-block` | The same paragraph loads from two different files |
| `dead-link` | A Markdown link in a loaded file points to a file that doesn't exist |

- [ ] Ground truth: run the real Claude Code (with an `InstructionsLoaded` hook) and the real Codex on each scenario, and record the harness versions next to it.
- [ ] `hooks/session-doctor/`: a Claude Code SessionStart hook that runs `axm doctor`, starts the session with a short notice when something is wrong, and prints nothing otherwise. A benchmark keeps it under 200 ms.
- [ ] Write-up, "What your agent actually reads", and a VHS tape that renders the README demo GIF.
- [ ] Release v0.2.0.

**Done when:** every scenario matches what the real Claude Code and Codex loaded, `--diff` on the demo scenario shows an instruction only one harness loads, all eight findings pass their fixtures, and a fresh Claude Code session on the demo scenario opens with the doctor's notice.

## M3: v0.3, skills and triggering

- [ ] `axm triggers`: skills whose descriptions overlap, and the terms they share.
- [ ] `axm triggers generate <skill>`: positive, negative, ambiguous, paraphrased and adversarial prompts.
- [ ] `axm triggers test`: precision and recall for each skill over the generated prompts, and each collision with its prompt, the skill selected and the skill expected.
- [ ] Emit the prompts as fixtures that existing skill-eval tools can run.

**Done when:** `axm triggers test` on the vault's skills reports precision and recall for each one, and names every collision with a likely cause.

## M4: v0.4, evals

- [ ] `axm eval run` and `axm eval compare`: a baseline against a candidate, recording result, cost, tokens, latency and behavior.
- [ ] Behavioral and regression evals for the starter assets, with history in `.axm/cache.db`.
- [ ] Providers sit behind one interface, so no eval depends on a single vendor.

**Done when:** comparing an asset before and after a change reports behavior, tokens and latency for both versions, on two different model providers.

## M5: v0.5, evidence

- [ ] `schemas/evidence.schema.json`, and `axm evidence record` and `axm evidence check`.
- [ ] `axm evidence`: each kind of check (build, unit tests, Terraform validate…) as FRESH, STALE or MISSING, with the reason.
- [ ] `hooks/evidence-freshness/`: records test and build runs as the agent makes them.

**Done when:** editing a file covered by recorded test evidence turns that evidence STALE with the reason, and running the tests again turns it FRESH.

## M6: v0.6, failure engineering

- [ ] `schemas/incident.schema.json`, and `axm incident new`, which creates `incidents/<date>-<name>/` with `incident.md`, `evidence.yaml`, `root-cause.md` and `regression.yaml`.
- [ ] `axm distill <incident>`: the failure class, the root cause, existing protections and possible responses, which prefer an eval or a hook over a new global instruction.
- [ ] `axm regress`: runs every incident's regression eval.
- [ ] `agents/failure-distiller/` and `workflows/failure-to-guardrail/`.

**Done when:** one real incident from my own sessions goes from `axm incident new` to a regression eval that fails without its guardrail and passes with it.

## M7: v0.7, prompt fossil

- [ ] `axm fossil`: uses eval history to find instructions that add tokens or latency, rarely matter, reduce capability or activate needlessly, and suggests a replacement for each.

**Done when:** `axm fossil` on my own instruction files reports each candidate with its measured token and quality impact, and changes no file.

## M8: v0.8, adapters

- [ ] `axm sync`: generates each harness's files from the canonical assets (`.claude/`, `.agents/`, `.github/`), shows the diff, and writes only what is approved.
- [ ] Adapters for Claude Code, Codex, GitHub Copilot and a generic target.

**Done when:** one skill synced to all three harnesses loads in each of them, confirmed with `axm explain` and the real harness.

## M9: v0.9, project intelligence

- [ ] `axm detect`: runtime, frontend, database, infrastructure and CI.
- [ ] `axm recommend`: assets whose manifests fit what was detected.
- [ ] `axm init` uses both, and installs only the assets the user picks.

**Done when:** `axm init` on three of my repos detects each stack correctly and recommends only assets whose manifests support it.

## v1.0

Axiomarium reaches 1.0 when someone can clone it, then run `axm init`, `axm doctor`, `axm sync` and `axm eval` in their own project and get a reliable, portable agent environment without learning Axiomarium's internals. Before that it needs:

- a stable asset schema and a stable CLI;
- at least three harness adapters;
- the behavioral eval and regression frameworks;
- the instruction compiler, scope sheriff, trigger collision testing and evidence freshness;
- the failure-to-regression workflow;
- documentation someone else can follow;
- dogfooding on several real repositories.

## Later

- `axm knowledge check`: a source, a `verified_at` date and a freshness class (static, slow, normal, fast, volatile) on reference material, with a warning when it may have rotted.
- `axm bom`: what each asset reads, writes and runs, its network access, environment variables, dependencies and supported harnesses, with `--format json`.
- `axm public-check`: secrets, internal hosts, company names, absolute paths, usernames and copied transcripts, caught before anything is published.
- A change validator router: given a diff, the smallest set of checks worth running.
- `axm explain --why <skill>`: why a particular skill activated.
- Agent contracts checked against the agent's actual behavior.
- More harnesses: Cursor, OpenCode and Gemini CLI.
- `--add-dir` directories and symlinked rules in the Claude Code model, and a check that warns when a harness model's source docs change.
- Assets for my stack: `dotnet`, `angular`, `postgres` and `terraform` skills, `architecture-critic` and `scope-reviewer` agents, and workflows to investigate a bug, plan a feature, review a change and turn research into an ADR.
- A "why did you ignore my rule?" skill that runs `axm explain` and says whether the rule loaded at all.
- A local dashboard, `axm ui`, much later and only after the CLI: instruction graphs, activation heatmaps, token use, evidence history and failure timelines.
- winget and Homebrew.
- A short launch video.

## Not planned

- An AI IDE, an agent runtime or an autonomous coding agent.
- A prompt or MCP marketplace, or another giant collection of community prompts.
- An LLM gateway or a model router.
- An observability service, or anything hosted: no accounts and no telemetry.
- Silently rewriting instruction files. `axm` recommends, and writes only when asked.

## How we'll know it works

Evidence comes from dogfooding on my own repos, recorded in `docs/dogfooding.md`. The clearest sign is that my own agent failures turn into regression evals instead of new paragraphs. After releases, issues, PRs and downloads from other people are a bonus.
