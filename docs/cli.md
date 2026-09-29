# The axm command line

`axm` is one native binary. Every command reads by default. The only ones that call a model are `axm triggers generate` and `axm triggers test`, and they do it through Claude Code or Codex on your own login, only when you run them. The only one that writes is `axm triggers generate`, and it asks first.

## Exit codes

| Code | Means |
|---|---|
| 0 | The command ran. Warnings, info and overlapping skills don't change that. |
| 1 | It ran and found errors: an invalid asset or manifest, or a broken `axiomarium.yaml`. Only `doctor` and `validate` find errors. |
| 2 | It couldn't run: bad arguments, a missing folder, no vault where one is needed, or no harness installed. |

`axm hook` commands never exit with 2, because Claude Code reads 2 as "block the action". A hook that can't run exits with 1 and says why on stderr.

## Output

- In a terminal, output has color and one kaomoji per summary line. Piped, in CI or read by an agent, it's plain text, and so is any run with `AXM_PLAIN` set. `NO_COLOR` or `TERM=dumb` turns color off. Terminal output is always the plain output plus color and kaomoji.
- Paths are relative to the repo root with forward slashes. Paths in your home folder start with `~`.
- `axm explain --json` prints JSON whose shape is a contract, versioned by `schemaVersion`, which is 1. New fields may appear, but existing ones don't change without a new version.
- `axm triggers export` prints a file for another tool, with no header and no color.

## Where axm looks

The harness models read the same places the harnesses do: your home folder (`USERPROFILE` on Windows, `HOME` elsewhere), `CODEX_HOME` (else `~/.codex`), `CLAUDE_CONFIG_DIR` (else `~/.claude`), Claude Code's managed settings, and Codex's system folder (`/etc/codex`, or `%ProgramData%\OpenAI\Codex` on Windows).

A repo can have an `axiomarium.yaml` at its root, checked against [`schemas/axiomarium.schema.json`](../schemas/axiomarium.schema.json). Its `doctor.ignore` lists globs for instruction files that are broken on purpose, such as test fixtures, which the doctor leaves out and counts.

## Commands

### `axm doctor`

Checks the repo's health: every instruction file and which harness loads it, the skills and hooks the harnesses find, and every [finding](findings.md). When the folder or its repo root is a vault, it checks every asset too. It exits 1 only for errors, such as an invalid asset. Findings are warnings or info.

- `--root <dir>`: where to start. The repo root is the nearest folder above it with a `.git`. Defaults to the current directory.

### `axm validate`

Checks every asset in a vault and prints only the problems, for CI and hooks. It exits 1 when an asset is invalid.

- `--root <dir>`: the vault. Defaults to the current directory.

### `axm list`

Lists the vault's assets by kind, with maturity, version and harness support.

- `--root <dir>`: the vault. Defaults to the current directory.
- `--kind <kind>`: only `agent`, `experiment`, `hook`, `policy`, `skill` or `workflow` assets.
- `--harness <harness>`: only assets that support `claude-code`, `codex`, `copilot` or `generic`, and how well.

### `axm explain <path>`

Shows which instruction files each harness loads for a file, and which it drops, each with the rule that decides it. It also shows the skills each harness lists and the hooks that run at session start and around an edit of the file. It prints the findings for that file, and exits 0 whenever it ran. The file needn't exist yet, but its folder must. [What your agent actually reads](what-your-agent-reads.md) and [Which skills your agent can see](which-skills-your-agent-can-see.md) walk through it.

- `--harness <all|claude-code|codex>`: only this harness. Defaults to both.
- `--cwd <dir>`: where the harness starts. Defaults to the repo root, or the current directory outside a repo.
- `--diff`: only the files and skills one harness has and the other doesn't. Needs both harnesses, and can't be combined with `--json`.
- `--json`: the same result as JSON, shape 1.

### `axm triggers`

Finds skills whose descriptions overlap in each harness's listing, and the words they share, without a model. The vault's skills join each listing as sync would list them. Overlap is shared wording, not proof the agent confuses two skills, so it exits 0 whenever it ran.

- `--root <dir>`: where to start. The harnesses launch at the repo root above it. Defaults to the current directory.

### `axm triggers generate <skill>`

Has Claude Code write trigger prompts for a vault skill, in one turn with tools off: positive, paraphrased, negative and adversarial ones, and ambiguous ones aimed at the listed skills it overlaps most. It shows every prompt and writes `skills/<skill>/evals/trigger/prompts.yaml` only after you approve, labeled with the model and date. It never edits `asset.yaml`: setting `evals.trigger: true` is your call. The file is checked against [`schemas/trigger-prompts.schema.json`](../schemas/trigger-prompts.schema.json).

- `--root <dir>`: where to start. The vault is here or at the repo root above it.
- `--model <model>`: the model to write the prompts with. Defaults to the one Claude Code is set to.
- `--yes`: write without asking. Without it and without a terminal to ask in, it stops before calling the model.
- `--replace`: write over the skill's existing prompts. Without it, an existing file stops it before calling the model.

### `axm triggers test [<skill>...]`

Runs each vault skill's trigger prompts on Claude Code and Codex, in a throwaway copy of the repo with the vault's skills written in, on your real home, four sessions at a time. Each session stops at its first action that isn't loading a skill. It reports precision and recall for each skill and harness, counted in runs, and every collision, false trigger and miss with a cause from the listing. A skill loaded before every task is named as background and not counted as a pick. A harness that isn't installed is skipped. It exits 0 whenever it ran, and 2 when no harness could.

- `--root <dir>`: where to start. The harnesses launch at the repo root above it.
- `--harness <all|claude-code|codex>`: only this harness. Defaults to both.
- `--runs <n>`: how many times each prompt runs on each harness. Defaults to 3.
- `--model <model>`: the model each harness picks with. Defaults to the one it's set to.

### `axm triggers export <skill>`

Prints a vault skill's trigger prompts for other skill-eval tools, and writes nothing.

- `--format skill-creator`: the skill-creator's list of `{query, should_trigger}`.
- `--format promptfoo`: a `promptfooconfig.yaml` with a provider for each harness the skill supports and a `skill-used` or `not-skill-used` assertion for each prompt.
- `--root <dir>`: where to start. The vault is here or at the repo root above it.

### `axm hook scope-sheriff`

A Claude Code hook that runs after each edit and warns the agent when the edit leaves the task's scope in `.axm/scope`. It never blocks. [`hooks/scope-sheriff/hook.md`](../hooks/scope-sheriff/hook.md) has the settings that wire it in.

### `axm hook session-doctor`

A Claude Code SessionStart hook: when a session starts in a repo where `axm doctor` finds an error or a warning, it tells you in one line and gives the model every problem with its fix. It says nothing when all is well. [`hooks/session-doctor/hook.md`](../hooks/session-doctor/hook.md) has the settings that wire it in.
