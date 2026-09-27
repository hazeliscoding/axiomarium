# Roadmap

Axiomarium is a lab for tools that make coding agents observable, constrained, testable and correctable. Its first piece is `axm explain`, a .NET CLI that shows which instructions Claude Code and Codex load for a file, and which they drop. This file tracks what gets built, in what order, and the decisions already made.

## Decisions (2026-09-27)

- **Purpose: a showcase.** Each piece ships as a working tool, a demo you can reproduce and a write-up in `docs/writeups/`. The README is the storefront that links to them. A few finished pieces beat many half-built ones.
- **The first piece is the instruction compiler**, `axm explain`. The pieces that come later (trigger tests, prompt fossils) need to know which instructions are in effect, so this one comes first.
- **v0.1 covers Claude Code and Codex.** Two harnesses are enough to show that the same file gets different instructions. Copilot and Cursor come later.
- **Stack:** .NET 10 with NativeAOT, System.CommandLine, Spectre.Console and xUnit. It ships as binaries on GitHub Releases and as the `Axiomarium` dotnet tool.
- **The command is `axm`.** `axiom` would collide with the CLI of Axiom (axiom.co), and their npm package `axiom` is an AI evals SDK in the same space.
- **Deterministic only.** No model calls, no network and no edits to the target repo. Every loaded or dropped entry cites the loading rule that produced it. Detecting contradictions between instructions needs judgment, so it comes later, as a separate pass that labels itself as model output.
- **Harness models follow the docs and are checked against the real harness.** Each model records the docs it was built from and the harness version it was confirmed against. The first models follow the Claude Code memory docs and the Codex AGENTS.md docs as read on 2026-09-27.
- **Dropped files are output, not silence.** A `Resolution` has three parts: loaded (in context order, with reason, timing and bytes), dropped (with the reason) and findings.
- **"Effective for a file"** means what the harness loads at launch from the launch directory, plus what it loads on demand when the agent reads that file. The launch directory defaults to the repo root and is set with `--cwd`.
- **The JSON output is the contract.** Snapshot tests use it, and the Spectre tree is rendered from the same `Resolution`.
- **Tests never read the real machine.** The home, `CODEX_HOME` and managed-policy directories are injected, and fixtures supply them.
- **One folder per finding,** as in pgcheckup: `findings/<id>/finding.md` plus `fixtures/fires/` and `fixtures/clean/`, each a tiny repo.
- **The collection** (skills, hooks, agents, a catalog and harness adapters) starts when its first asset exists. No empty folders before that.
- **Brand follows the KAIRO design system.** KAIRO has no drawn logo: the name, set in Saira Condensed 600, is the mark.
- **The logo is option 1B, "Bracketed":** the wordmark inside corner brackets, KAIRO's mark for the object in focus, because that is what `axm explain` does to a file. Pink replaces KAIRO's signal red: `#f0569b` on dark backgrounds and `#c2185b` on light ones. The mark is an "A" in the same brackets. The lockup has no `AXM/CLI` tag, because the tag can't be read at README size.
- **CLI output uses KAIRO's content rules:** uppercase section labels, `//` separators, zero-padded indices and severity as a word. No emoji.

## M0: Placeholder (as soon as possible)

- [x] Add `LICENSE` (Apache-2.0), `.gitignore` and `.gitattributes`.
- [x] Write `README.md`, `ROADMAP.md`, `AGENTS.md` and `CLAUDE.md`.
- [x] Brand: pick a KAIRO wordmark option, export `mark.svg` and `lockup.svg` with `-dark` variants to `docs/brand/`, and add the `<picture>` header to the README. Convert the text to paths.
- [ ] Scaffold the solution: `Directory.Build.props` (nullable on, warnings as errors, XML docs required), central package management, and the `Axiomarium.Instructions`, `Axiomarium.Cli` and test projects.
- [ ] `axm --version`, and an `axm explain` that prints a canned tree for the demo scenario.
- [ ] CI: build, test and format check on Linux, Windows and macOS.
- [ ] Guardrail: a test fails if `Axiomarium.Instructions` references `System.Console`, Spectre.Console or `System.Net.Http`.

**Done when:** CI is green on all three platforms, `axm explain` prints the canned tree, and a test PR that adds an `HttpClient` to the library fails the build.

## M1: Claude Code model

- [ ] Launch-time chain: managed policy, `~/.claude/CLAUDE.md` and `~/.claude/rules/`, then each directory from the filesystem root down to the launch directory (`CLAUDE.md`, `.claude/CLAUDE.md`, then `CLAUDE.local.md`), then `.claude/rules/**/*.md` without `paths`.
- [ ] `@path` imports: relative to the importing file, absolute and `~` paths, at most 4 hops, skipped inside code spans and fenced blocks, and cycles detected. Imports outside the launch directory are marked as needing approval.
- [ ] On-demand files for the target: `CLAUDE.md` and `CLAUDE.local.md` in subdirectories between the launch directory and the file, and rules whose `paths` match it. Covers brace expansion and its budget, invalid patterns (they match nothing) and invalid frontmatter (the rule loads for every file).
- [ ] AGENTS.md under each **Project instructions** mode: `claude-md-or-agents-md` (the default), `claude-md-and-agents-md`, `claude-md` and `managed-only`. `.claude/AGENTS.md` is read, and `AGENTS.override.md` and `AGENTS.local.md` never are.
- [ ] `claudeMdExcludes` from user, project and local settings. It can't exclude the managed file.
- [ ] Block-level HTML comments are stripped before bytes are counted.
- [ ] Output: the Spectre tree (loaded, then dropped with reasons) and `--json`.
- [ ] Ground truth: a script runs the real Claude Code on each scenario with an `InstructionsLoaded` hook and records what loaded. That hook doesn't fire for an AGENTS.md read through the setting, so those cases are confirmed from the session's load message instead.

**Done when:** every Claude Code scenario's JSON snapshot passes and matches what the real Claude Code loaded, with its version recorded next to the scenario.

## M2: Codex model and `--diff`

- [ ] Global file: in `$CODEX_HOME` (default `~/.codex`), `AGENTS.override.md`, then `AGENTS.md`. The first non-empty one is used.
- [ ] Project chain: from the git root (or the launch directory when there is no repo) down to the launch directory, at most one file per directory: `AGENTS.override.md`, then `AGENTS.md`, then each of `project_doc_fallback_filenames`.
- [ ] `project_doc_max_bytes` (default 32 KiB): files past the limit are dropped. Confirm against the real Codex whether the file that crosses the limit is cut or dropped whole.
- [ ] Instruction files below the launch directory are reported as "not loaded by the harness".
- [ ] `--harness claude-code|codex|all`, and `--diff` to show what only one harness loads.
- [ ] Ground truth for each Codex scenario, with the Codex version recorded.

**Done when:** every Codex scenario passes, and `--diff` on the demo scenario shows at least one instruction that only one harness loads, confirmed against both real harnesses.

## M3: Findings

- [ ] Finding contract: `findings/<id>/finding.md` (what happens, why it matters, fix, source) and a fixture runner that checks each finding fires on `fires/` and stays silent on `clean/`.
- [ ] The eight findings:

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

- [ ] Findings appear in `explain` and in `--json`, each with a severity: `warning` when an instruction doesn't reach the agent where you meant it to, or reaches it where you didn't, and `info` for waste such as duplicated blocks.

**Done when:** all eight findings fire on their `fires` fixture, stay silent on their `clean` fixture, and appear in both output formats.

## M4: Write-up and demo

- [ ] `docs/writeups/what-your-agent-actually-reads.md`: the loading rules of both harnesses side by side, the ways each one silently drops instructions, and how `axm` models them.
- [ ] A demo scenario in `fixtures/scenarios/demo/` that triggers the headline findings, and a VHS tape that renders the README GIF from it.
- [ ] Dogfooding log in `docs/dogfooding.md`: `axm` run on the owner's own repos, noting what it found and what it got wrong.

**Done when:** the README shows the demo GIF, the write-up is linked from the README, and the dogfooding log has entries from at least three repos.

## M5: v0.1.0

- [ ] Release workflow: NativeAOT binaries for `win-x64`, `linux-x64` and `osx-arm64` on GitHub Releases, with `SHA256SUMS`, and the `Axiomarium` dotnet tool on NuGet.
- [ ] `CONTRIBUTING.md`: how to add a finding in one folder, and how to update a harness model when its docs change.
- [ ] `SECURITY.md`: `axm` reads files and nothing else.
- [ ] A "wrong resolution" issue template that asks for the harness version and a minimal fixture.
- [ ] Understandable errors for a missing repo, a path outside the repo and unreadable settings.
- [ ] README quick start, checked on a clean machine.

**Done when:** someone on a clean machine can install `axm` from the README, run `axm explain` on their repo and understand the output, and CI is green.

## Later

- `axm check` for CI: fail the build on findings, with a baseline.
- Copilot and Cursor harness models.
- `--add-dir` directories and symlinked rules.
- A contradiction pass that is clearly labeled as model judgment.
- A freshness check that warns when the docs a harness model was built from have changed.
- The next pieces: determinism auditor, trigger collision lab, prompt fossil and scope sheriff.
- The collection: skills, hooks and agents with a catalog, and adapters that generate each harness's files from one source.
- A short launch video.
- winget and Homebrew.

## Not planned

- A dashboard or control plane. That is a different, much larger product.
- Anything hosted, accounts or telemetry.
- Generic prompt, agent or skill collections. Plenty exist already.
- Editing instruction files. `axm` reports and suggests fixes. It never changes files.

## How we'll know it works

Evidence comes from dogfooding (n=1, recorded in the log). After release it also comes from public signals: "wrong resolution" issues, PRs that add findings, downloads and other projects linking to it.
