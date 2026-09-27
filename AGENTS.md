# AGENTS.md

These are the working rules for agents in this repo. Axiomarium is a lab for tools that make coding agents observable, constrained, testable and correctable (Apache-2.0). Its first piece is `axm explain`, a .NET 10 NativeAOT CLI that shows which instructions Claude Code and Codex load for a file, and which they drop.

## Sources of truth

- `README.md`: the pitch and the "deterministic, and honest about it" promises.
- `ROADMAP.md`: decisions already made, the milestones, and what is out of scope. Check it before proposing features. Respect those decisions unless the owner reopens them. Record new or changed decisions there, with the date.
- Work from the next unchecked item in `ROADMAP.md`. Don't build past the current milestone without asking.

## Commands

The M0 scaffold adds the build, test and publish commands here. Keep them cross-platform (`dotnet`), because the owner develops on Windows. Avoid bash-only scripts.

## Deterministic, and honest about it (hard rules)

The tool is only worth trusting if these hold. Never break them, not even in debug modes or dev tooling.

- **No model in the loop.** `Axiomarium.Instructions` makes no model calls and no network calls. A test fails if it references `System.Console`, Spectre.Console or `System.Net.Http`.
- **Read-only.** `axm` never writes, moves or deletes files in the repo it inspects. Fixes are printed as text.
- **Every entry cites its rule.** Each loaded or dropped file carries the loading rule that produced it. If you can't name the rule, don't emit the entry.
- **Harness models follow the docs, then the real harness.** When you change a model, cite the doc section and update the docs date and harness version recorded in the model. If the docs and the real harness disagree, the real harness wins, and the disagreement goes into `ROADMAP.md` as a decision.
- **Don't guess what the model will read.** When a harness leaves loading to the model, such as a Codex AGENTS.md below the launch directory, report it as "not loaded by the harness". Never report it as loaded.
- **Tests never read the real machine.** Home, `CODEX_HOME`, managed-policy and settings locations are injected. Fixtures and docs use placeholder paths such as `/home/dev` and `C:\Users\dev`, never real ones.

## Resolver and findings

- `Axiomarium.Instructions` holds the resolver, the harness models and the findings. `Axiomarium.Cli` only parses arguments and renders. Both output formats render the same `Resolution`.
- Each harness is one model class under `Harnesses/`. Differences between harnesses live in the models, never in the renderer.
- One folder per finding: `findings/<id>/finding.md`, `fixtures/fires/` and `fixtures/clean/`. Each fixture is a tiny repo. Both fixtures are required. `fires/` is the positive control, so a finding without one isn't done.
- `finding.md` has **What happens**, **Why it matters**, **Fix** and **Source** sections. The source links to the harness doc section the finding relies on.
- Finding ids are kebab-case and stable. Renaming one is a breaking change that needs a decision in `ROADMAP.md`.
- Severity: `warning` when an instruction doesn't reach the agent where it was meant to, or reaches it where it wasn't. `info` for waste, such as a duplicated block. Don't inflate severity.
- Paths are shown relative to the repo root with forward slashes, except home paths, which start with `~`. CI runs on Linux, Windows and macOS, because path handling is where this breaks.

## .NET and NativeAOT

- .NET 10 with NativeAOT. Trim and AOT warnings (IL2xxx, IL3xxx) are errors. Never suppress one without a decision in `ROADMAP.md`.
- No reflection-based serialization. Use System.Text.Json source generation.
- The JSON output shape, finding ids and exit codes are contracts. Change them only through a decision in `ROADMAP.md`.

## CLI output and copy

- Voice is calm, short and declarative, following KAIRO's content rules: uppercase section labels, `//` separators, zero-padded indices and machine-report status lines. No exclamation marks, no emoji, no fake hacker jargon.
- Every finding says what is wrong, why it matters and what to do: "CLAUDE.md:12 imports docs/testing.md, which does not exist."
- Severity is always a word, never only a color. Respect `NO_COLOR`, and print plain output when stdout isn't a terminal.
- Write the project name as Axiomarium and the command as `axm`.

## Brand

- Brand follows the KAIRO design system. KAIRO has no drawn logo: the name, set in Saira Condensed 600, is the mark. Don't draw a logo, and don't redraw or restyle the brackets.
- The logo is option 1B, "Bracketed": the wordmark inside corner brackets. The mark is an "A" in the same brackets.
- The assets are in `docs/brand/`: `mark.svg` and `lockup.svg` for light backgrounds, and `-dark` files for dark backgrounds. Use the SVGs, and don't re-typeset the wordmark with a web font.
- The wordmark is Saira Condensed SemiBold, uppercase, with 0.02em letter spacing, converted to vector paths.
- The accent is pink, replacing KAIRO's signal red: `#f0569b` on dark and `#c2185b` on light. Ink is `#101418` on light backgrounds and `#e9e7e1` on dark.

## Working style

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `chore:`, `test:`, `ci:`, `build:`, `refactor:`). Keep each commit atomic, and use a scope when it adds clarity (`feat(claude-code): …`, `feat(findings): …`).
- **No AI attribution** in commits or PRs. That means no `Co-Authored-By` trailers, no "Generated with" lines and no session links.
- **`AGENTS.md` and `CLAUDE.md` are committed.** `.gitignore` un-ignores them, overriding the global gitignore. Keep them free of secrets and private paths.
- **Checks:** automate acceptance checks instead of handing manual steps to the owner. Give every check that tests for an absence a positive control, meaning a case that proves the check can fail.
- **Validation:** evidence comes from dogfooding (the log) and public async signals (issues, PRs, downloads). Don't plan interviews, recruiting or outreach.
- **Docs:** short and concise. Prefer editing `ROADMAP.md` over creating new planning documents. Repo files never reference the owner's private notes.
- **XML docs:** every public type and member in `src/` has an XML doc comment (`///`). Say what it does and its contract: parameters, return value, exceptions and edge cases. Don't just restate the name. Tests don't need XML docs, because their names say what they check.
- **Code comments:** explain why, not what. Only comment on what the code can't say for itself: a non-obvious constraint, a workaround and its cause, or a line that keeps a hard rule. Don't leave commented-out code. A finding's `finding.md` is its documentation.
