# AGENTS.md

These are the working rules for agents in this repo. Axiomarium is the owner's personal lab (public, Apache-2.0) for building, testing and debugging AI coding environments like software. It has two parts: the vault (agents, skills, hooks, policies, workflows and evals) and `axm`, a .NET 10 NativeAOT CLI that inspects, validates, tests and debugs them.

## Sources of truth

- `README.md`: the pitch and the principles.
- `ROADMAP.md`: decisions already made, the milestones, and what is out of scope. Check it before proposing features. Respect those decisions unless the owner reopens them. Record new or changed decisions there, with the date.
- Work from the next unchecked item in `ROADMAP.md`. Don't build past the current milestone without asking.

## Commands

Keep commands cross-platform (`dotnet`), because the owner develops on Windows. Avoid bash-only scripts.

- Build: `dotnet build axiomarium.slnx`.
- Test: `dotnet test --project tests/Axiomarium.Tests`.
- Format check, which CI runs: `dotnet format axiomarium.slnx --verify-no-changes`.
- Run: `dotnet run --project src/Axiomarium.Cli -- doctor`.
- Publish: `dotnet publish src/Axiomarium.Cli -c Release -r win-x64 -o out` (`linux-x64`, `osx-arm64`). Trim and AOT warnings fail it.
- Test the published binary: set `AXM_BINARY` to it, then run `dotnet test --project tests/Axiomarium.Tests -- --filter-class Axiomarium.Tests.Cli.NativeBinaryTests`.
- Record ground truth: `dotnet run --project tools/Axiomarium.GroundTruth -- [scenario ...] [--harness claude-code|codex]`. Each Claude Code recording is a short Haiku session on your login, which the recorder borrows for the run and deletes. Codex runs offline, but on Windows it reads the real `~/.agents/skills`, so there the recorder records Claude Code only, and `.github/workflows/record-codex.yml` records Codex on Linux: when it fails on a pull request, download its `codex-recordings` artifact (`gh run download <run> -n codex-recordings -D <folder>`, which won't overwrite files), copy it over `scenarios/`, review the diff and commit it. Re-record after a harness update and review the diff of each `expected.json`.
- NativeAOT publish on this Windows machine fails with `'vswhere.exe' is not recognized` unless the VS Installer folder is on PATH. Run it as `$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"; dotnet publish …`. That is an environment problem, not an AOT warning.

## Local, deterministic and honest (hard rules)

The tool is only worth trusting if these hold. Never break them, not even in debug modes or dev tooling.

- **Local first.** No accounts, telemetry, hosted services, update checks or uploads. The only network traffic is model calls made by `axm eval`, `axm triggers`, `axm fossil` and `axm conflicts --judge`, and only when the user runs them.
- **A deterministic core.** `Axiomarium.Core` makes no model or network calls. A test fails if it references `System.Console`, Spectre.Console or `System.Net.Http`.
- **Writes only on request.** `list`, `validate`, `doctor`, `hook`, `detect`, `explain`, `triggers` and `conflicts` are read-only. `init`, `sync` and `incident new` show what they will write and wait for approval. `fossil` and `distill` recommend changes and never delete or rewrite instructions.
- **Label model output.** Anything a model produced, such as a judged contradiction or a distilled root cause, says so in the output.
- **Every explain entry cites its rule.** Each loaded or dropped file carries the loading rule that produced it. If you can't name the rule, don't emit the entry.
- **Harness models follow the docs, then the real harness.** When you change a model, cite the doc section and update the docs date and harness version recorded in the model. If the docs and the real harness disagree, the real harness wins, and the disagreement goes into `ROADMAP.md` as a decision.
- **Don't guess what the model will read.** Skills are "available", never "loaded". When a harness leaves loading to the model, such as a Codex AGENTS.md below the launch directory, report it as "not loaded by the harness".
- **Tests never read the real machine.** Home, `CODEX_HOME`, Codex's system folder, managed-policy and settings locations are injected. Fixtures and docs use placeholder paths such as `/home/dev` and `C:\Users\dev`, never real ones. `CliRun` refuses `doctor`, `explain` and `triggers` without an injected machine, and the native binary tests only run commands that read no harness files.
- **Scenarios are ground truth.** Each one in `scenarios/<name>/` is a tiny repo, a fake home, `scenario.yaml` and the recording in `expected.json`. Every Markdown file in it starts with its `MARKER <path>` line (after any frontmatter), so a recording can name the file even when a harness cuts it short. A skill's description starts with the same marker, because a listing shows descriptions, and a hook's command is `echo MARKER <path> <label>`. Only the recorder writes `expected.json`, never a person.

## Vault assets

- One folder per asset under `agents/`, `skills/`, `hooks/`, `policies/`, `workflows/` or `experiments/`, with an `asset.yaml` manifest that validates against `schemas/asset.schema.json`, and its content in a Markdown file named after its kind: `agent.md`, `skill.md`, `hook.md`, `policy.md`, `workflow.md` or `experiment.md`. File names are matched exactly, on every platform. Create a folder with its first asset, never as an empty placeholder.
- Assets are canonical and vendor neutral. Never put harness-specific files (`.claude/`, `.agents/`, `.github/`) inside an asset. Adapters generate those.
- An asset's `version` follows SemVer. Bump it when the asset's behavior changes.
- Schemas in `schemas/` are the single source of truth and are embedded in the binary. `SchemaValidator` supports only the keywords in `SchemaValidator.SupportedKeywords`. To use another keyword, extend the validator and its tests first; a test fails if a schema uses an unsupported one.
- YAML is read with the YAML 1.2 core schema: `yes` and `on` are strings. A number JSON can write as is, such as `1.0`, keeps its source text; hex, octal and forms such as `+1`, `.5` or `01` become their value. Quote a version such as `"1.0"`.
- Maturity is earned. Raise an asset's level only when the evidence that level requires in `registry/maturity.yaml` exists: usage entries in `docs/dogfooding.md` (a `## YYYY-MM-DD · repo` heading whose section links to the asset's folder) and evals. `axm doctor` fails on a claim without its evidence.
- Before adding an instruction, ask whether deterministic tooling could enforce it instead. Before adding a skill, write down when it should activate, in its `skill.use_when`. Before adding a hook, decide whether it should block, warn or only gather evidence (`hook.response`), and prefer warning with evidence. Each policy rule names what enforces it in `enforced_by`, and an empty list admits that nothing does yet.
- Failures become tests, not paragraphs. When an agent fails, capture an incident and add a regression eval. Don't answer a failure with another paragraph of prose.

## Instruction compiler and findings

- `Axiomarium.Core` holds discovery, schemas, the registry, the harness models and the findings. `Axiomarium.Cli` only parses arguments and renders. Both output formats render the same result.
- Each harness is one model class. Differences between harnesses live in the models, never in the renderer.
- One folder per finding: `findings/<id>/finding.md`, `fixtures/fires/` and `fixtures/clean/`. Each fixture is a tiny repo in `repo/`, with a fake home in `home/` when it needs one. Both fixtures are required. `fires/` is the positive control, so a finding without one isn't done. `fires/` produces only its own finding, and `clean/` produces none.
- A finding reads what the harness models load and drop, so it never disagrees with `explain`. Its message and fix are written in `InstructionFindings`, and its `finding.md` shows the same output.
- `finding.md` has **What happens**, **Why it matters**, **Fix** and **Source** sections. The source links to the harness doc section the finding relies on.
- Finding ids are kebab-case and stable. Renaming one is a breaking change that needs a decision in `ROADMAP.md`.
- `axiomarium.yaml` at the repo root configures `axm` for the repo and validates against `schemas/axiomarium.schema.json`. A new folder of instruction files that are broken on purpose, such as fixtures, goes under `doctor.ignore`, or `axm doctor` reports it.
- Severity: `error` when an asset or manifest is invalid. `warning` when an instruction doesn't reach the agent where it was meant to, or reaches it where it wasn't. `info` for waste, such as a duplicated block. Don't inflate severity.
- Paths are shown relative to the repo root with forward slashes, except home paths, which start with `~`. CI runs on Linux, Windows and macOS, because path handling is where this breaks.

## .NET and NativeAOT

- .NET 10 with NativeAOT. Trim and AOT warnings (IL2xxx, IL3xxx) are errors. Never suppress one without a decision in `ROADMAP.md`.
- No reflection-based serialization. Use System.Text.Json source generation, and a YAML setup that works under NativeAOT.
- The JSON output shapes, the manifest schema, finding ids and exit codes are contracts. Change them only through a decision in `ROADMAP.md`.

## Releases

- Each milestone from M1 on ships as a release tagged `vX.Y.Z`: NativeAOT binaries on GitHub Releases with `SHA256SUMS`, and the `Axiomarium` dotnet tool on NuGet.
- Record user-visible changes in `CHANGELOG.md` under **Unreleased** in the same commit as the change.
- Before 1.0, a breaking change to a contract bumps the minor version and needs a decision in `ROADMAP.md`.

## CLI output and copy

- Structure follows KAIRO: uppercase section labels, `//` separators, zero-padded indices and machine-report status lines. Wording is short, declarative and friendly. No exclamation marks, no emoji, no fake hacker jargon.
- Output is colorful and a little playful: brand-palette colors and one kaomoji per summary or status line, chosen by the outcome, from the fixed set in the CLI's `Kaomoji` class. Don't invent new ones inline, and never pick one at random: the same outcome always gets the same kaomoji.
- Informative first. A kaomoji or a color never carries meaning on its own: severity is always a word, and counts are always written out.
- Plain output (no color, no kaomoji) when stdout isn't a terminal, or `AXM_PLAIN` is set. `NO_COLOR` turns color off. Terminal output must equal the plain output plus color and kaomoji. Hooks and agents always read plain output.
- `axm hook` commands print only what the harness reads, such as Claude Code's hook JSON, and never exit with 2, because Claude Code reads 2 as "block". A hook that can't run exits with 1 and says why on stderr. The protocol for each harness lives in the core, confirmed against the real harness.
- Every finding says what is wrong, why it matters and what to do: "CLAUDE.md:12 imports docs/testing.md, which does not exist."
- Write the project name as Axiomarium and the command as `axm`.

## Brand

- Brand follows the KAIRO design system. KAIRO has no drawn logo: the name, set in Saira Condensed 600, is the mark. Don't draw a logo, and don't redraw or restyle the brackets.
- The logo is option 1B, "Bracketed": the wordmark inside corner brackets. The mark is an "A" in the same brackets.
- The assets are in `docs/brand/`: `mark.svg` and `lockup.svg` for light backgrounds, and `-dark` files for dark backgrounds. Use the SVGs, and don't re-typeset the wordmark with a web font.
- The wordmark is Saira Condensed SemiBold, uppercase, with 0.02em letter spacing, converted to vector paths.
- The accent is pink, replacing KAIRO's signal red: `#f0569b` on dark and `#c2185b` on light. Ink is `#101418` on light backgrounds and `#e9e7e1` on dark.

## Working style

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `chore:`, `test:`, `ci:`, `build:`, `refactor:`). Keep each commit atomic, and use a scope when it adds clarity (`feat(doctor): …`, `feat(assets): …`, `feat(claude-code): …`).
- **No AI attribution** in commits or PRs. That means no `Co-Authored-By` trailers, no "Generated with" lines and no session links.
- **`AGENTS.md` and `CLAUDE.md` are committed.** `.gitignore` un-ignores them, overriding the global gitignore. Keep them free of secrets and private paths.
- **Planning** lives in `ROADMAP.md`. Don't create project boards, backlog documents or spec files unless the owner asks.
- **Checks:** automate acceptance checks instead of handing manual steps to the owner. Give every check that tests for an absence a positive control, meaning a case that proves the check can fail.
- **Validation:** evidence comes from dogfooding (the log) and, after releases, public async signals (issues, PRs, downloads). Don't plan interviews, recruiting or outreach.
- **Docs:** short and concise. Prefer editing `ROADMAP.md` over creating new planning documents. Repo files never reference the owner's private notes.
- **XML docs:** every public type and member in `src/` has an XML doc comment (`///`). Say what it does and its contract: parameters, return value, exceptions and edge cases. Don't just restate the name. Tests don't need XML docs, because their names say what they check.
- **Code comments:** explain why, not what. Only comment on what the code can't say for itself: a non-obvious constraint, a workaround and its cause, or a line that keeps a hard rule. Don't leave commented-out code. A finding's `finding.md` and an asset's Markdown are their documentation.
