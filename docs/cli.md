# The axm command line

`axm` is one native binary. Every command reads by default. The only ones that call a model are `axm triggers generate`, `axm triggers test`, `axm eval run` and `axm conflicts --judge`, and they do it through Claude Code or Codex on your own login, only when you run them. `axm triggers generate` writes a prompt file and asks first, and `axm eval run` writes only its history in `.axm/evals/`.

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
- `axm explain --json`, `axm eval run --json` and `axm eval compare --json` print JSON whose shape is a contract, versioned by `schemaVersion`, which is 1 for each. New fields may appear, but existing ones don't change without a new version.
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

### `axm conflicts <path> --judge`

Has a model find instructions a file gets that contradict each other: two that can't both be followed. It takes the files `axm explain` says each harness loads for the path, asks once for each distinct set, in one turn with no tools, and keeps only the contradictions whose two passages the model quotes from the files, each with its `file:line`. It drops and counts the rest, because a made-up contradiction can't quote text that isn't there. Quotes match with runs of whitespace read as one space. Everything it reports is labeled as model judgment. It writes nothing, and exits 0 whenever it ran. Without `--judge` it exits 2, because the checks that need no model, such as duplicated blocks and dead imports, are already findings in `axm explain` and `axm doctor`.

- `--judge`: ask the model. Required.
- `--judge-with <claude-code|codex>`: the harness whose model judges. Defaults to Claude Code.
- `--harness <all|claude-code|codex>`: only the instructions this harness loads. Defaults to both.
- `--cwd <dir>`: where the harness starts. Defaults to the repo root, or the current directory outside a repo.
- `--model <model>`: the model that judges. Defaults to the one the judging harness is set to.

### `axm eval`

Runs the vault's behavioral and regression evals on the real harnesses. Each case is described in [Eval cases](assets.md#eval-cases).

### `axm eval run [<asset>...]`

Runs each asset's eval cases on Claude Code and Codex, four sessions at a time. Each session runs in a sealed home that holds your logins and the model you chose for each harness, and nothing else of your setup: no plugins, hooks, MCP servers, skills or instructions. It runs in its own copy of the case's `repo/`, committed once as a git repo, with the asset installed as that harness's files. Claude Code may edit only inside the copy and run only the case's `allow` commands. Codex runs in its workspace-write sandbox, with the network off. After each session, the case's `run` commands run in the copy, and every check is decided from what the harness recorded.

For each case on each harness, it reports how many runs passed, each check that failed with its runs, and the median and range of tokens, wall time and tool calls, plus turns and cost where Claude Code reports them. For Codex, it also times each command from Codex's own records and names the slowest, so a command that stalls shows by name. It names every path outside the copy that a run's commands touched: on Windows, Codex's sandbox can read the whole disk, so a run can find the answer in a real repo.

When a case has a `judge.rubric`, a model grades each finished run against it, from the prompt, the commands the session ran, its git diff of the copy and its final message. The judge runs in the same sealed home, through Claude Code unless `--judge-with codex` says otherwise, and its verdicts are shown as model judgment, counted apart from the checks: a run passes on its checks alone. It gives no verdict beyond the counts, because each run is the model at work. On Windows, it first runs one unscored Codex session to start Codex's sandbox, which takes about two minutes in a new home. Claude Code's sessions and judge get a temp folder inside the run, so they leave nothing in yours. The copies, the sealed home, that temp folder and the borrowed logins are deleted afterwards, and the next run removes anything a stopped run left. Each asset's part of the run is saved as its `--json` output in `.axm/evals/<asset folder>/<time>.json`. It exits 0 whenever it ran, and 2 when nothing could.

Claude Code sessions can't run on macOS yet, because Claude Code keeps its login in the Keychain there.

- `--root <dir>`: where to start. The vault is here or at the repo root above it.
- `--harness <all|claude-code|codex>`: only this harness. Defaults to both.
- `--runs <n>`: how many times each case runs on each harness. Defaults to 3.
- `--model <model>`: the model each harness runs. Defaults to the one you chose for it.
- `--json`: the result as JSON, shape 1, the same as the saved history.
- `--judge-with <claude-code|codex>`: the harness whose model grades the rubrics. Defaults to Claude Code.

### `axm eval compare <asset>`

Runs an asset's eval cases on two versions of it and shows them side by side. The baseline is the asset's files at a git ref, `HEAD` unless `--baseline` says otherwise, or no asset at all with `--baseline none`. The candidate is the working tree. Both versions run the working tree's cases, and the output says so when the cases changed since the ref too. Baseline and candidate sessions alternate in one sealed home, as `axm eval run` runs them, so a rate limit or a slow hour falls on both.

For each case on each harness, it shows how many runs of each version passed, the median of each measure with its range when the runs differ, and the change from baseline to candidate. Like `axm eval run`, it names each version's slowest Codex command and every path outside the copy that a run's commands touched. It draws no conclusion, because a few runs a side is a small sample and each run is the model at work. It exits 2 when the asset is unchanged since the ref, and 0 whenever it ran.

Each version's runs are saved in `.axm/evals/<asset folder>/`, as `<time>-baseline.json` and `<time>-candidate.json`, in the shape `axm eval run --json` prints. When a saved baseline still describes the same asset and cases, on the same harness versions and models, with as many runs, the compare reuses it, says which, and runs only the candidate. An agent can't be compared on Codex yet, because both versions would share the one Codex home.

- `--baseline <ref|none>`: the version to compare with. Defaults to `HEAD`.
- `--root <dir>`: where to start. The vault is here or at the repo root above it.
- `--harness <all|claude-code|codex>`: only this harness. Defaults to both.
- `--runs <n>`: how many times each case runs on each harness, for each version. Defaults to 3.
- `--model <model>`: the model each harness runs. Defaults to the one you chose for it.
- `--judge-with <claude-code|codex>`: the harness whose model grades the rubrics. Defaults to Claude Code.
- `--fresh`: run the baseline again, even when a saved run still describes it.
- `--json`: the result as JSON, shape 1, with both sides of each case.

### `axm hook scope-sheriff`

A Claude Code hook that runs after each edit and warns the agent when the edit leaves the task's scope in `.axm/scope`. It never blocks. [`hooks/scope-sheriff/hook.md`](../hooks/scope-sheriff/hook.md) has the settings that wire it in.

### `axm hook session-doctor`

A Claude Code SessionStart hook: when a session starts in a repo where `axm doctor` finds an error or a warning, it tells you in one line and gives the model every problem with its fix. It says nothing when all is well. [`hooks/session-doctor/hook.md`](../hooks/session-doctor/hook.md) has the settings that wire it in.
