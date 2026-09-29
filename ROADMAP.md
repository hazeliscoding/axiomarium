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
- **A deterministic core.** Discovery, validation, `explain` and `doctor` never call a model or the network. Only `eval`, `triggers`, `fossil` and `conflicts --judge` call models, only when the user runs them, and never through one provider's API alone.
- **Writes only on request.** Diagnostic commands are read-only. `init`, `sync` and `incident new` show what they will write and wait for approval. `fossil` and `distill` recommend changes and never delete or rewrite instructions.
- **Hooks prefer explanation to blocking.** A hook first asks the agent for a reason and records evidence. It blocks only when an action is destructive.

### Build and release

- **Stack:** .NET 10 with NativeAOT, System.CommandLine, YamlDotNet and xunit.v3. NativeAOT keeps startup fast for hooks that run on every session or tool call. Rust was considered, but .NET stays because it is my main stack and versioned releases solve distribution.
- **Manifests are validated by our own small validator** for the subset of JSON Schema 2020-12 our schemas use, and the schema files stay the single source of truth, embedded in the binary. JsonSchema.Net moved to a maintenance-fee EULA, which is friction for anyone using `axm` at work, and Corvus compiles validators at runtime, which NativeAOT can't do. A test fails if a schema uses a keyword the validator doesn't support.
- **Codex's `config.toml` is read with Tomlyn** (BSD-2-Clause) through a source-generated `TomlSerializerContext`, which keeps it NativeAOT-clean. Its reflection-based API is flagged under NativeAOT, so the core never calls it.
- **No Spectre.Console.** The output is simple enough for a plain writer with ANSI colors, as in pgcheckup, and startup stays fast for hooks. `axm doctor` on this repo takes about 60 ms.
- **YAML follows the 1.2 core schema:** `yes` and `on` stay strings, and a number JSON can write as is keeps its source text, so an error can say `Quote it: "1.0"`. Other number forms, such as hex, octal, `+1` or `.5`, become their value.
- **The doctor checks each asset in isolation.** YamlDotNet sometimes throws exceptions that aren't `YamlException`, and reports some errors (tab indentation) at line 1. So reading YAML never throws, a wrong line is replaced by the real one or by none, and anything unexpected while checking one manifest becomes an error on that file while the rest of the vault is still checked.
- **Versions:** between releases, `main` carries the next version with a `-dev` suffix, such as `0.2.0-dev`. A release commit drops the suffix and dates the version's section in `CHANGELOG.md`, and its tag must match.
- **Projects:** `Axiomarium.Core` (assets, schemas, registry, instruction resolution) and `Axiomarium.Cli`. `Axiomarium.Eval` and `Axiomarium.Adapters` arrive with their milestones.
- **The command is `axm`.** `axiom` would collide with the CLI of Axiom (axiom.co), and their npm package `axiom` is an AI evals SDK in the same space.
- **Versioned releases.** Each milestone from M1 on is a release (v0.1, v0.2 …), tagged `vX.Y.Z`, with NativeAOT binaries for `win-x64`, `linux-x64` and `osx-arm64` on GitHub Releases, `SHA256SUMS`, the `Axiomarium` dotnet tool on NuGet, and a `CHANGELOG.md` entry. Each asset also carries its own SemVer `version` in its manifest.
- **Storage:** configuration lives in git (`axiomarium.yaml`, `registry/`, `evals/`, `incidents/`). SQLite is only for disposable local state in `.axm/cache.db`: eval history, hashes, evidence and cached scans. The repo never needs the database to be understood.

### Assets

- **One folder per asset,** with an `asset.yaml` manifest that validates against `schemas/asset.schema.json`, and the asset's content in Markdown. Folders appear with their first asset, never as empty placeholders.
- **The manifest** declares name, kind, version, description, maturity, support per harness (`full`, `partial` or `experimental`), permissions (filesystem, shell, network), side effects, inputs, outputs and which evals exist.
- **Maturity is earned.** Experimental: an interesting idea with few or no evals. Incubating: used successfully and still changing. Tested: behavioral and regression evals exist. Stable: behavior changes carefully. Battle-tested: used repeatedly on real projects, with accumulated regression coverage. `axm doctor` checks each asset has the evidence its level requires.

### Asset model (v0.1)

- **The registry is `registry/maturity.yaml` only.** A catalog or compatibility file would restate what every manifest already says, and drift from it. `axm list` is the catalog, and `axm list --harness <harness>` is the compatibility table.
- **Each asset has one content file named after its kind:** `agent.md`, `skill.md`, `hook.md`, `policy.md`, `workflow.md` or `experiment.md`. Adapters generate harness files such as `SKILL.md` from it.
- **Skills, hooks and policies carry a block named after their kind** in `asset.yaml`, validated by `schemas/skill.schema.json`, `hook.schema.json` and `policy.schema.json`. A skill says when it should activate (`use_when`), a hook says whether it blocks, warns or only gathers evidence (`response`), and each policy rule names what enforces it (`enforced_by`). Agents need nothing beyond `permissions` yet, so there is no agent schema.
- **References are checked:** the content file exists and isn't empty, each `enforced_by` names an asset that exists, and each `evals.<type>: true` has at least one file in the asset's `evals/<type>/`.
- **Maturity evidence is data** in `registry/maturity.yaml`: each level's promise and what it requires. The binary embeds it, like the schemas, so every vault is judged by the same promises. Usage evidence is a dated entry in `docs/dogfooding.md`: a heading such as `## 2026-10-02 · carmine-workbench` whose section links to the asset's folder. Incubating needs 1 entry. Tested adds behavioral and regression evals, plus trigger evals for skills. Stable needs 3 entries, and battle-tested 5 entries across 3 repos. A maturity claim without its evidence is an error, because the manifest says something untrue. Until v0.5, eval evidence means the eval files exist, not that they pass.
- **`validate` and `doctor` render the same result.** `validate` prints only the problems, for CI and hooks. `doctor` adds the inventory, and from v0.2 the instruction findings. `list` shows the inventory and judges nothing.
- **No `--json` in v0.1.** Agents and hooks read the plain output. JSON becomes a contract with `explain` in v0.2.
- **`inspect` and `init` move to v0.10.** `inspect` would duplicate `detect`, and `init` has nothing to write until it can detect a stack and install assets. Until then a vault is recognized by its kind folders.
- **Hooks run through `axm`.** The harness calls `axm hook <name>`, which reads the hook's JSON on stdin. It needs no extra runtime on any OS, starts fast, and keeps the hook's logic in the tested core, where the protocol for each harness lives. A hook command never exits with 2, because Claude Code reads 2 as "block": bad input exits with 1 and a message.
- **The scope sheriff reads the task's scope from `.axm/scope`:** one glob per line, relative to the repo root, written by the agent or the user at the start of a task. Without the file the hook is silent. An edit outside the scope sends the agent a message asking why, and is never blocked. Recording evidence waits for the v0.6 evidence store.
- **Claude Code hooks, confirmed on 2.1.283 (2026-09-27).** The hooks docs don't say which output of a tool hook reaches the model, so headless sessions settled it. A `PostToolUse` hook's `hookSpecificOutput.additionalContext` reaches the model without blocking anything, and so does a `PreToolUse` one. `decision: "block"` with a `reason` on `PostToolUse` also reaches it. `permissionDecision: "allow"` with a `permissionDecisionReason` on `PreToolUse` reaches only the user. The hook reads `tool_name`, `cwd` and `tool_input.file_path` (`tool_input.notebook_path` for NotebookEdit), which are absolute paths. The editing tools are Write, Edit and NotebookEdit; MultiEdit no longer exists. So the scope sheriff runs after the edit (`after-edit`, which only fires for an edit that happened) and warns through `additionalContext`.
- **Releases** run from `.github/workflows/release.yml` when a `v*` tag is pushed. The tag must match `<Version>`, and `CHANGELOG.md` must have that version's section, which becomes the release notes. Each OS builds its own NativeAOT binary (archived with `SHA256SUMS`) and its own `Axiomarium.<rid>` tool package, and Linux also packs the `Axiomarium` pointer package, following the .NET 10 docs on RID-specific tools. NuGet gets them through Trusted Publishing, so no API key is stored, with the pointer package pushed last. Fresh runners then install both the binary and the tool and run them. A pull request that touches the release setup runs everything except publishing, so the pipeline is proven before a tag needs it. Released builds ship without symbols. Two limits: on Windows the tool is installed behind an `axm.cmd` launcher that bash only finds by that name, and the Linux binary is built on Ubuntu 24.04, so it needs that glibc or newer.

### Instruction intelligence (v0.2)

- **Claude Code and Codex first.** Two harnesses are enough to show that the same file gets different instructions. Copilot comes with the adapters in v0.9.
- **Harness models follow the docs, then the real harness.** Each model records the docs it was built from and the harness version it was confirmed against. The first models follow the Claude Code memory and hooks docs as read on 2026-09-28 (Claude Code 2.1.283), and the Codex AGENTS.md docs and source as read the same day (Codex 0.156.1, whose loading code matches 0.158.0). Every scenario was recorded again on Claude Code 2.1.284 the same day, and nothing changed but the version, so the Claude Code model is confirmed against 2.1.284.
- **Dropped files are output, not silence.** A resolution has three parts: loaded (in context order, with reason, timing and bytes), dropped (with the reason) and findings. Every loaded or dropped entry carries the id of the rule that produced it, such as `claude-code/ancestor-memory`, and each rule id maps to its doc section and the harness version it was confirmed against.
- **"Effective for a file"** means what the harness loads at launch from the launch directory, plus what it loads on demand when the agent reads that file. The launch directory defaults to the repo root and is set with `--cwd`.
- **v0.2 covers instruction files only.** Skills and hooks move to v0.3, which builds skill discovery for each harness anyway. The vault's own assets reach a harness only through `sync` in v0.9, so `explain` shows what the harness sees, not what the vault holds.
- **Skills are available, not loaded.** The model decides when a skill loads, so from v0.3 `explain` lists matching skills as available and never claims they loaded.
- **No `axm conflicts` command.** Duplicate blocks, dead references and shadowed files are findings, shown by `explain` and `doctor`. Contradictions between rules need a model, so `conflicts --judge` moves to v0.5 with the model providers, and v0.2 stays fully deterministic.
- **The JSON output is the contract.** `explain --json` carries a `schemaVersion`, snapshot tests use it, and the text output is rendered from the same resolution. `explain` exits 0 whenever it ran, because every instruction finding is a warning or info, and 2 when it couldn't run.
- **`explain` output, shape 1 (2026-09-28).** Paths are relative to the repo root with forward slashes, `~/…` under the home folder, and whole otherwise, so outside a repo nothing is relative. The JSON has the `target` and `launchDirectory`, then each harness with `confirmedWith`, its `loaded` files (`path`, `scope`, `timing` as `at-launch` or `when-read`, `rule`, `bytes`, and `cut`, `importedFrom` and `patterns` when they apply) and its `dropped` files (`path`, `rule`, `importedFrom`), then every rule they cite, by id, with its `label`, `summary`, `source` and `leftToModel`. A file the harness leaves to the model shows as NOT LOADED, never DROPPED. `--diff` is text only, because the JSON already lists what each harness loads.
- **Tests never read the real machine.** The home, `CODEX_HOME` and managed-policy directories, and the harnesses' user settings (Claude Code's `claudeMdExcludes` and Project instructions mode, Codex's `config.toml`), are one injected input, and fixtures supply it.
- **Ground truth is recorded locally and replayed in CI.** Each scenario in `scenarios/<name>/` is a tiny repo, a fake home, a launch directory and a target file, and every file in it carries a unique marker. A recorder in `tools/`, never shipped, writes `expected.json` with both harness versions, and CI replays every recording without credentials or network. Re-recording after a harness update shows where a model needs work. The spike (2026-09-28, Claude Code 2.1.283, Codex 0.156.1) settled how:
  - **Codex:** `codex debug prompt-input` with `CODEX_HOME` pointed at a temporary copy of the fake home. It needs no login or network, and its one AGENTS.md block lists the loaded files in order, with the bytes as the model sees them.
  - **Claude Code:** a short Haiku session with `CLAUDE_CONFIG_DIR` pointed at a temporary copy of the fake `.claude`, plus a copy of the login file that is deleted when the run ends, so the real `~/.claude` is never touched. The session transcript has the order: an `instructions` attachment lists every launch-time file with its path, scope and content; `nested_memory` attachments record on-demand CLAUDE files and rules; and a nested AGENTS.md arrives as a PostToolUse `hook_additional_context` from the built-in AGENTS.md plugin. An `InstructionsLoaded` hook adds each load's reason, but its events don't come in context order and never cover AGENTS.md.
  - **Isolation:** scenarios run outside the user's home folder, because Claude Code's walk from the filesystem root passes through it and reads `~/.claude/CLAUDE.md` there as a project file. A recording that contains a file without a scenario marker fails, and its content is never written anywhere.
- **What the research settled (2026-09-28).**
  - Claude Code reads AGENTS.md only when no CLAUDE file exists in the working directory or above, unless the Project instructions mode says otherwise. That mode is read only from user and managed settings.
  - Codex applies one 32 KiB budget across all project files. The file that crosses it is cut mid-file, later files are dropped, and the global file doesn't count. The docs contradict each other on this, so the code decides.
  - An empty `AGENTS.override.md` hides the `AGENTS.md` next to it, because Codex picks a file by existence before it checks that it isn't empty. Codex also skips every project file in an untrusted project.
  - In Claude Code, a project rule without `paths` loads before `CLAUDE.local.md`, which the docs don't state. For a project under the home folder, the walk from the root reaches the home folder's `.claude/CLAUDE.md`, which is also the user memory; whether Claude Code loads it once or twice there can't be recorded with a fake home, so that case is hand-checked.
- **What the Claude Code recordings settled (2026-09-28, 2.1.283).** Twelve scenarios in `scenarios/` pin these, and where the docs disagree, the model follows the harness:
  - **Broken frontmatter (corrected 2026-09-28):** when a rule's YAML doesn't parse, Claude Code quotes values that look like YAML syntax and parses again, so an unclosed bracket in `paths` becomes a pattern that matches nothing. YAML that still doesn't parse makes the rule load for every file, as the docs say. This bullet first said such a rule never loads, from rules the retry had mended; see "Frontmatter is parsed twice" under Skills and hooks (v0.3).
  - **Order at launch:** managed, user memory, user rules, then for each directory from the root down: `CLAUDE.md`, `.claude/CLAUDE.md`, its rules without `paths`, `CLAUDE.local.md`, then its AGENTS files. Rule folders above the launch directory load too.
  - **Order on read:** nested AGENTS.md first (the built-in plugin's PostToolUse context), then user path rules, then each directory below the launch directory (its CLAUDE files, its rules, `CLAUDE.local.md`), then the path rules of the launch directory and above.
  - **Path rules** match from the folder that holds their `.claude/`, or from the launch directory for user rules, the way `.gitignore` lines do (corrected 2026-09-28; see "Paths match like .gitignore lines" under Skills and hooks). Claude Code's YAML reader accepts tab indentation.
  - **Imports** end at the next space, so trailing punctuation is part of the path and the import silently loads nothing. A project file's import outside the launch directory never loads headless, including a root `CLAUDE.md`'s imports when Claude Code starts in a subdirectory.
  - **AGENTS.md by default:** one CLAUDE file in the launch directory or above turns every AGENTS.md off, nested ones too.
  - **Block HTML comments** go with the blank lines after them, so an import inside one never loads, and `claudeMdExcludes` reaches imports.
  - **Bytes:** launch files are trimmed; files loaded on read are not; frontmatter is removed.
- **One folder per finding,** as in pgcheckup: `findings/<id>/finding.md` plus `fixtures/fires/` and `fixtures/clean/`, each a tiny repo with a fake home. A test checks every finding folder: both fixtures exist, `finding.md` has its four sections, `fires` produces the finding and `clean` doesn't.
- **How the findings decide (2026-09-28).** Findings read the resolutions, so they report only what a harness model loads or drops, and the fixtures are checked from the repo root, for a file in each folder that holds instruction files and a file each path rule matches.
  - `rule-matches-nothing` checks project rules against every file on disk except under `.git`. User rules aren't checked, because they may match in another repo.
  - `duplicate-block` compares paragraphs of 40 characters or more, with whitespace collapsed, and reports each pair of files once.
  - `dead-link` checks relative Markdown links only, never URLs, anchors or absolute paths.
  - `agents-md-hidden` reports only AGENTS.md files inside the repo. One above it, such as in the home folder, serves other folders too, so hiding it is a choice rather than this repo's problem.
  - `explain --json` gains a `findings` array (`id`, `severity`, `file`, `line` when there is one, `message` and `fix`) in shape 1, since v0.2 isn't released yet. `--diff` shows no findings. Severity gains `info`.
- **`axm doctor` works in any repo.** It always checks the repo's instruction files for both harnesses, launched from the repo root, and runs the vault checks when a vault exists. Warnings don't fail it. `validate` stays vault-only, for CI.
- **How the doctor finds the repo (2026-09-28).** The repo root is the nearest folder with a `.git` at or above `--root` (the current directory by default), or `--root` itself outside a repo. The vault is `--root` or, failing that, the repo root. An INSTRUCTIONS block lists every instruction file the harnesses load or drop, with the harnesses that load it, so a file no harness loads shows as "not loaded".
- **`axiomarium.yaml` configures a repo (2026-09-28).** It sits at the repo root and validates against `schemas/axiomarium.schema.json`, which the binary embeds. Its first field is `doctor.ignore`: globs for instruction files that are broken on purpose, such as test fixtures. The doctor doesn't list or check them, and its summary says how many it ignored, so nothing disappears silently. A problem in the file is an error, and a file with problems ignores nothing, so a typo can't hide a finding. This repo ignores `scenarios/` and `findings/*/fixtures/`.
- **People who don't read code get the warning without asking.** A Claude Code SessionStart hook, `axm hook session-doctor`, runs the doctor's checks and starts the session with a short notice to the user and the model when something is wrong, and prints nothing otherwise. It aims for 200 ms, measured locally, and CI holds the native binary under 1 s.
- **Claude Code SessionStart hooks, confirmed on 2.1.284 (2026-09-28).** Headless sessions with one kind of output each confirmed the SessionStart section of the hooks docs: plain stdout and `hookSpecificOutput.additionalContext` reach the model, and `systemMessage` doesn't. The transcript records it as a separate `hook_system_message`, which the docs say the user sees. The input names how the session started as `source`. So `session-doctor` gives the user a one-line summary as `systemMessage` and the model every error and warning with its fix as `additionalContext`, and says nothing when there are only info findings or none. Run directly it takes 25 to 70 ms; inside Claude Code on Windows about 450 ms, most of it starting Git Bash. CI times the native binary, cold, on this repo, and runs it on a broken repo as the positive control.

### Skills and hooks (v0.3)

- **v0.3 is skills and hooks, and trigger testing moves to v0.4 (2026-09-28).** M3 as first planned held two things: deterministic skill and hook discovery, and trigger tests that run a model. They ship apart. v0.3 models which skills each harness offers the model and which hooks run, and `axm triggers` finds overlapping descriptions without a model. v0.4 generates trigger prompts and runs them. Every later milestone moves down one, and the versions in the decisions above follow.
- **Skills are part of the resolution (2026-09-28).** Each harness model returns the skills it would list for the file, next to the instruction files: AVAILABLE, `at-launch` or `when-read`, or NOT LISTED with the reason, each citing its rule. Nothing ever says a skill loaded.
- **What the research found about Claude Code skills (2026-09-28, the 2.1.284 docs and binary).**
  - At launch it lists managed skills, personal skills in `~/.claude/skills` (with those synced from claude.ai), project skills from the launch directory up to the git root, enabled plugins' skills as `plugin:skill`, and legacy `.claude/commands`. Enterprise beats personal, personal beats project, and a skill beats a command of the same name.
  - A `.claude/skills` below the launch directory joins the first time the agent reads or edits a file there. A skill with `paths` joins when the agent reads, writes or edits a matching file.
  - The listing is a `skill_listing` attachment with entries such as `- name: description - when_to_use`. Each entry is cut at 1,536 characters, and the whole listing gets 1% of the context window at 4 characters a token: 8,000 characters at 200k. Over budget, some entries become names only, chosen partly by how often each skill is used.
  - A skill with `disable-model-invocation`, a skill hidden by `skillOverrides`, and a plugin skill without a description are never listed.
- **What the research found about Codex skills (2026-09-28, the 0.156.1 source).**
  - Everything is found at launch: `.agents/skills` from the project root down to the launch directory, each project `.codex/skills`, `$CODEX_HOME/skills` (deprecated) and its bundled `.system` skills, `~/.agents/skills`, and the admin folder.
  - Two skills with the same name are both listed, and a `$name` mention then selects neither.
  - The listing is a `<skills_instructions>` developer message. Each description is cut at 1,024 characters, and the whole fits `skills.max_context_tokens`, else 2% of the context window, else 8,000 characters. Over budget, descriptions shrink in turn, then skills are dropped from the end.
  - Disabled skills, skills whose `agents/openai.yaml` sets `allow_implicit_invocation: false` or lists `products` without `codex`, and invalid `SKILL.md` files are never listed.
  - **The docs are wrong:** Codex reads project `.codex/skills` in untrusted projects, where the config reference says untrusted projects skip project `.codex/` layers. The model follows the source.
- **What the research found about hooks (2026-09-28).**
  - Claude Code merges hooks from managed settings, `~/.claude/settings.json`, the launch directory's `.claude/settings.json` (not its ancestors'), `settings.local.json` and enabled plugins, and runs a handler defined in two settings files once. A matcher of only letters, digits, `_`, `-`, spaces, `,` and `|` is an exact list, and anything else is an unanchored regex. `if` scopes a tool hook to paths, such as `Edit(src/**)`, and a hook on any other event with `if` set never runs.
  - Codex loads hooks from managed `requirements.toml`, and from `hooks.json` and `[hooks]` in `config.toml` in the admin folder, `$CODEX_HOME` and each trusted project's `.codex`. None overrides another. A hook that isn't managed runs only when its SHA-256 matches the `trusted_hash` in the user config, and only `command` and `mcp_tool` handlers run.
  - **The docs are wrong:** a Codex matcher of only letters, digits, `_` and `|` is an exact list, not the regex the docs describe. The model follows the source.
  - A Codex `apply_patch` hook gets the whole patch and no file path, so no Codex hook can be scoped to one file.
- **What the spike settled (2026-09-28, Claude Code 2.1.284 on Windows, Codex 0.156.1 on Linux).**
  - Claude Code lists a skill by its folder name, even when its frontmatter `name` differs. Codex lists the frontmatter `name`, or the folder name when there is none.
  - Claude Code leaves out a plugin skill without a description, and lists a skill whose frontmatter doesn't parse, or has no description, with its first body line. A nested skill's entry ends with `(from sub/.claude/skills — applies when working on files under sub/)`.
  - Claude Code's 13 built-in skills take 6,230 of Haiku's 8,000 characters in a fresh home. Over budget, a plugin skill and two built-ins became names only, so the research's "built-ins keep their text" was wrong.
  - Skills enabled on the claude.ai account sync into the fake home seconds after launch. The recorder sets `syncClaudeAiSkills: false` there, so no account skill reaches a recording.
  - On Windows, Claude Code reads `.claude/settings.local.json` from the launch directory, like `.claude/settings.json`. The docs say the repo root elsewhere; that isn't recorded, because Claude Code is recorded on Windows only.
  - Every Claude Code hook run reaches the transcript as a `hook_success` attachment with its command and output. The same handler in user and project settings ran once, `if` and both kinds of matcher behaved as documented, and a SessionStart hook with `if` never ran.
  - Codex lists skills under a root table (`r0` = a folder, entries `(file: r0/name/SKILL.md)`), bundled first, then repo, then user, by name within each. A repo whose root holds `.claude-plugin/plugin.json` gets its skills named `plugin:skill`. `scenarios/codex-skills/` adds that only a manifest at or above a skills root counts: one inside it sits in a hidden folder, which the scan skips, though the source would honor it.
  - Codex's `hooks/list` reports each hook's key (`<source path>:<event>:<group>:<handler>`), source, matcher and trust: `trusted`, `untrusted`, `modified` or `managed`. The hash covers the event, the matcher and the handler as canonical JSON, not the path, so `axm` can compute it and a recording can confirm it. The trust key holds the absolute path, so the recorder writes it at run time. A project's hooks don't load until the project is trusted, and a `prompt` handler is skipped with a warning.
- **What the Claude Code skill recordings settled (2026-09-28, 2.1.284).** `scenarios/claude-code-skills/` and `scenarios/skills-and-hooks/` pin these:
  - At launch the listing holds managed skills, personal skills, project skills from the launch directory up (each folder's by name), commands, enabled plugins' skills and commands, the built-ins, then skills synced from claude.ai. On read come nested skills, outer folder first, then skills whose `paths` match.
  - A skill's `paths` match the way a rule's do, like `.gitignore` lines, but relative to the launch directory, so `*.ts` matches `src/api/orders.ts`.
  - A nested skill whose name another skill has is listed as `src:deploy`, and its entry ends `(scoped to src/ — use this instead of the unscoped "deploy" skill when the files being changed are under src/)`.
  - Plugin and synced skills follow the docs and the spike, not a scenario: a plugin's install records hold absolute paths, and recordings turn the sync off. A plugin from a marketplace in a local folder loads in place, and any other from its installed copy.
- **Paths match like .gitignore lines (2026-09-28).** Claude Code 2.1.284 matches a rule's and a skill's `paths` with the `ignore` package, a `.gitignore` matcher, as its code shows and two probes and `scenarios/path-rules/` confirm. A pattern without a slash, such as `*.cs`, matches a name at any depth; one with a slash inside, such as `api/orders.cs`, is anchored at the base folder; a trailing `/` matches a folder at any depth, and so does a trailing `/**`, so `api/**` matches `src/api/orders.cs`. The docs call them glob patterns. The M2 model matched them as globs from the base folder, so it said such rules don't load when they do, and `rule-matches-nothing` could fire on them. Both now use the one matcher.
- **Frontmatter is parsed twice (2026-09-28).** Claude Code 2.1.284's frontmatter reader, found in its code and confirmed by `scenarios/bad-frontmatter/` and a skill in `scenarios/claude-code-skills/`, first parses the YAML as it is. When that fails, it quotes each top-level value holding one of ``{}[]*&#!|>%@` `` or `": "`, turns leading tabs into spaces, and parses again. Only YAML that still fails counts as broken, and then every field is ignored: a rule loads for every file, as the docs say, and a skill lists its first body line. That corrects the M2 reading that a broken rule never loads. The rules recorded then had values the retry mends into patterns that match nothing, which `rule-matches-nothing` reports, so `rule-frontmatter-invalid` now fires only on YAML the retry can't mend and says the rule loads everywhere. The finding keeps its id.
- **What the hook recordings settled (2026-09-28).** `scenarios/skills-and-hooks/` and `scenarios/codex-hook-trust/` pin these:
  - A Claude Code edit is an `Edit` of a file that exists, a `Write` of a new one, or a `NotebookEdit` of a notebook, and a hook's `if` is one permission rule: `Edit(...)` covers every tool that edits files, `src/**` is anchored at the launch directory, and a bare name matches at any depth.
  - Codex's trust hash is SHA-256 over canonical JSON of the event, the matcher and the handler with its defaults (`timeout` 600, `async` false), and `axm` computes the same value. The trust key names the source file's absolute path, so a scenario's `config.toml` writes it as `{run}`, which the recorder and the replay fill in.
  - Codex lists a hook the user turned off with `enabled: false`, still `trusted`, and skips a `prompt` handler and a matcher that doesn't compile, with a warning each. It also warns when one folder holds hooks in both `hooks.json` and `config.toml`.
- **The doctor lists what the harnesses find (2026-09-28).** Its HARNESS SKILLS and HARNESS HOOKS blocks show the repo's own skills and hooks a row each, and those from outside the repo, which follow the user into every repo, one row per harness and source, so a home with dozens of personal and plugin skills takes a few rows. Built-in skills stay out, since `explain` shows them. With skills and hooks, CI's native `session-doctor` took 24 ms on Linux, 46 ms on Windows and 63 ms on macOS on this repo. On a Windows machine with about 150 user-level skills its median is 182 ms, 100 ms of it finding those skills, inside the 200 ms target.
- **Hooks at three moments (2026-09-28).** `explain` shows the hooks that would run at `session-start`, `before-edit` and `after-edit`, the moments of the vault's hook schema, as RUNS or NOT RUN with the reason. A Codex edit hook runs on every edit, and `explain` says so. It assumes a trusted Claude Code workspace, as `claude -p` does, and says that too. The doctor lists every configured hook on every event, with whether it can run at all.
- **Not modeled in v0.3,** and `explain` says so: Codex plugin skills and hooks, `--add-dir`, hooks in skill or subagent frontmatter, and hooks on a read.
- **A budget states its assumption.** Where a listing's cut depends on what `axm` can't read, such as how often each skill is used or the model's context window, the output states its assumption and never names the skill that loses its description. For Claude Code that's a 200k-token window, 8,000 characters, unless the `model` setting names a 1M-token variant, such as `opus[1m]` or Sonnet 5, where it's 40,000. For Codex it's the 272k-token window of its default models: 2% is 5,440 tokens, at 4 bytes a token. Claude Code's built-in skills aren't files, so the recorder saves their names and entry lengths for the confirmed version, and the model lists them as built in.
- **Codex is recorded in CI.** On Windows Codex finds `~/.agents/skills` through the Known Folder profile, whatever `HOME` says, so a local recording would read the owner's real skills. `.github/workflows/record-codex.yml` installs the confirmed Codex on Ubuntu and records every scenario offline: `codex debug prompt-input` for the listing, and `codex app-server`'s `hooks/list` for each hook with its trust. It runs on demand and on pull requests that touch `scenarios/`, uploads the recordings, and fails when they differ from what's committed. The local recorder refuses Codex on Windows. Whether a Codex hook fires needs a model turn, so the model's hook matching follows the source at 0.156.1 and isn't recorded.
- **Scenario skills and hooks carry markers.** The listing shows descriptions, not bodies, so each scenario skill's description starts with its marker, and each scenario hook command prints its marker. `scenario.yaml` gains `action: read | edit`. A recording that lists an unmarked skill that isn't a built-in fails, and nothing of that skill is written.
- **`explain --json` stays shape 1.** Each harness gains `skills`, `notListed`, `listing` and `hooks`. No existing field changes, so `schemaVersion` stays 1.
- **Skill and hook findings are for what the user can fix (2026-09-28).** They leave out plugin, synced, bundled and managed skills and hooks. A copied skills folder clashes a skill at a time, so `skill-name-clash` makes one finding for the clashes between the same folders, naming up to three skills: on a real home with about 150 skills, 21 near-identical warnings became one, which matters because `session-doctor` hands every warning to the model. `skill-paths-match-nothing` checks only the repo's own skills, since a personal skill may match in another repo, and the skill's own files count as matches, because Claude Code lists the skill when the agent reads them.
- **`axm triggers` finds overlap, not collisions.** For each harness it compares every skill listed from the repo root, plus the vault's skills as they'd be listed after sync (their description and `use_when`), on the text the model sees. A term weighs more the rarer it is in that listing, and each pair above a fixed threshold is shown with the terms it shares. It exits 0 whenever it ran and isn't a doctor finding, because only `triggers test` in v0.4 can call a pair a collision.
- **How `axm triggers` scores overlap (2026-09-28).** Each skill's text is its name, without a namespace such as a plugin's, and the entry the harness shows; a vault skill's is its `description` and `use_when`, not cut. Words are lowercased, common words dropped and forms of a word joined by a small stemmer. A pair's score is the cosine of TF-IDF weights, with IDF smoothed as ln(1 + N/df) so two skills of two can still overlap, and pairs from 0.15 are shown. The threshold was set on a real setup of 80 Claude Code and 60 Codex skills: from 0.15 the pairs share what they're for, such as two document converters or two skills for one tool, and below it pairs increasingly share only incidental words. A name listed twice is compared once, since `skill-name-clash` reports the copy, and built-in skills are left out because their text isn't recorded.

### Trigger testing (v0.4)

- **Trigger tests run against the real setup (2026-09-28).** `axm triggers test` copies the repo to a throwaway folder outside the home folder (`C:\axm-triggers\<run>` on Windows, `/tmp/axm-triggers/<run>` elsewhere), writes the vault's skills into it as each harness's skill files, the way sync will in v0.9, and launches the harness there with the user's real home. Every skill the agent actually sees competes, and the user's hooks and plugins run, as in real use. The repo is never written to, and the copy is deleted afterwards. A sealed home is repeatable but blind to the collisions that matter, so it waits in Later.
- **Both harnesses, with the pick read from the harness (revised after the spike, 2026-09-28).** A session's loads are the skills it brings in before its first other action: Claude Code's `Skill` tool calls before any other tool, and the `SKILL.md` files Codex reads in its first command, which Codex's source counts as use. The session stops at that first other action. A skill that isn't under test and is loaded in every run, whatever the prompt, is a background skill, such as a plugin's skill that tells the agent to use skills before anything else: the output names it with its count and doesn't count it as a pick. The pick is the first load that isn't a background skill, or "no skill". A skill under test is never background, so one that loads on every prompt shows as false triggers.
- **What the M4 spike settled (2026-09-28, Claude Code 2.1.284 and Codex 0.156.1 on Windows, on the owner's real home).**
  - `claude -p --output-format stream-json --verbose --no-session-persistence` starts with the user's SessionStart hooks, then an `init` event listing the session's skills by name, its tools, its model and the Claude Code version. A pick is an `assistant` event with a `tool_use` named `Skill` whose input names the skill, answered by "Launching skill: <name>". Left alone, the session went on for 8 turns and cost $0.34, so stopping early is what keeps a test affordable.
  - `codex exec --json -s read-only --ephemeral` has no skill event. Codex said which skill it would use in an `agent_message`, then read two `SKILL.md` files in one `command_execution` (`Get-Content` through PowerShell on Windows, with doubled backslashes in the command): a plugin's `using-superpowers`, which it reads before any task, and the demo's `db-migration`. That's why picks skip background skills. The read-only sandbox stopped its later `dotnet ef` attempt. Its stderr carried errors from the user's MCP servers, so only the JSON events and the exit code count.
  - `claude -p --tools ""` answered a JSON request in one text turn, for $0.06 in 4 seconds. MCP tools stay available with `--tools ""`, so `generate` also passes `--strict-mcp-config`, and its parser still rejects an answer that calls a tool. `claude -p` waits 3 seconds for stdin unless the runner closes it.
  - The trimmed streams are fixtures in `tests/Axiomarium.Tests/Fixtures/triggers/`, with placeholder ids and paths.
- **Prompts are generated through the harness and kept in the vault.** `axm triggers generate <skill>` asks `claude -p` for about 20 prompts in one turn with tools off: positive, paraphrased, negative, adversarial and, when the skill has rivals, ambiguous ones aimed at the skills `axm triggers` finds overlapping. It shows them and writes `skills/<name>/evals/trigger/prompts.yaml` only after approval in the same run (`--yes` without a terminal), labeled with the harness, model and date that wrote them. It never edits `asset.yaml`: setting `evals.trigger: true` is the owner's claim. The harness is the only way `axm` reaches a model, so there's no API key and no HTTP client.
- **The harness's own model picks, three runs per prompt.** Tests use the model each harness is set to, because that's what picks skills in real sessions, and run each prompt three times, so results are rates. `--model` and `--runs` override both. The output names the model, the harness and its version.
- **Scores count runs, and causes come from the listing.** Precision and recall for each skill and harness count runs over the skill's own prompts, shown with their counts and no pass or fail threshold. A problem run is a collision (another skill fired), a false trigger or a miss. Each gets the first deterministic cause that applies: the expected skill isn't listed, with its rule; its description is cut; the listing is over its budget, with the stated assumption; or else the terms the prompt shares with each skill, weighted as `axm triggers` weights them. When none applies, it says no cause was found in the listing. The model is never asked why, because models make up reasons after the fact.
- **The CLI starts the harnesses.** The Core holds the prompt schema, the brief, the parsers for the generated answer and each harness's event stream, the scoring and the causes, all pure. The CLI starts the processes behind a runner interface, and tests swap in one that replays captured streams, trimmed and with placeholder paths. `axm triggers test` exits 0 whenever it ran, and 2 when nothing could run.
- **Docs stay in `docs/` (2026-09-28).** A synced GitHub wiki was considered and dropped: it's a separate git repo without PR review or CI, and links into `findings/` and `scenarios/` break there. `docs/README.md` is the home and the reading order. Reference pages sit next to the write-ups and link to what exists instead of repeating it, and tests check that links resolve and that every command and finding id is documented. The repo's GitHub Wiki tab goes off, so there's one place to look. A generated site can come from the same files near v1.0.

### Brand

- **Brand follows the KAIRO design system.** KAIRO has no drawn logo: the name, set in Saira Condensed 600, is the mark.
- **The logo is option 1B, "Bracketed":** the wordmark inside corner brackets, KAIRO's mark for the object in focus. Pink replaces KAIRO's signal red: `#f0569b` on dark backgrounds and `#c2185b` on light ones. The mark is an "A" in the same brackets. The lockup has no `AXM/CLI` tag, because the tag can't be read at README size.
- **CLI output is colorful and a little playful.** KAIRO's structure stays: uppercase section labels, `//` separators, zero-padded indices and severity as a word. On top of it go colors from the brand palette and a kaomoji that matches the outcome (`ヽ(・∀・)ﾉ` all clear, `(╥﹏╥)` errors). The fun never carries meaning on its own: every kaomoji sits next to words that say the same thing. This departs from KAIRO's "no emoji, never jokey" voice on purpose.
- **Plain output for machines.** When stdout isn't a terminal (pipes, CI, hooks, agents), or `AXM_PLAIN` is set, output has no color and no kaomoji. `NO_COLOR` turns color off. Terminal output is exactly the plain output plus color and kaomoji, and a test holds that.
- **The README's terminal demos are VHS tapes,** `docs/demo/doctor.tape` and `docs/demo/explain.tape`. `.github/workflows/demo.yml` renders them with the `axm` release from NuGet whenever a tape changes in a pull request, and the GIFs are committed after a look at them, so the demos always show what users install.

## M0: Day 0 (as soon as possible)

- [x] Add `LICENSE` (Apache-2.0), `.gitignore` and `.gitattributes`.
- [x] Write `README.md`, `ROADMAP.md`, `AGENTS.md` and `CLAUDE.md`.
- [x] Brand: pick a KAIRO wordmark option, export `mark.svg` and `lockup.svg` with `-dark` variants to `docs/brand/`, and add the `<picture>` header to the README. Convert the text to paths.
- [x] Scaffold the solution: `Directory.Build.props` (nullable on, warnings as errors, XML docs required, NativeAOT), central package management, and the `Axiomarium.Core`, `Axiomarium.Cli` and test projects.
- [x] Prove that YAML parsing and JSON Schema validation work in a published NativeAOT binary, with no trim or AOT warnings.
- [x] Write `schemas/asset.schema.json`.
- [x] `axm --version`, and a first `axm doctor` that discovers assets and validates their manifests.
- [x] Add the first real asset: `agents/determinism-auditor/`.
- [x] CI: build, test and format check on Linux, Windows and macOS, plus a NativeAOT publish on each.
- [x] Guardrail: a test fails if `Axiomarium.Core` references `System.Console`, Spectre.Console or `System.Net.Http`.

**Done when:** CI is green on all three platforms, `axm doctor` validates the determinism auditor, a test PR that breaks its manifest makes `axm doctor` fail and name the field, and a test PR that adds an `HttpClient` to `Axiomarium.Core` fails the build.

**Done (2026-09-27).** CI passed on all three platforms ([run](https://github.com/hazeliscoding/axiomarium/actions/runs/36353578047)). Throwaway PR #2 broke the manifest, and CI failed with `Unknown maturity: "production-ready"` at `asset.yaml:8` ([run](https://github.com/hazeliscoding/axiomarium/actions/runs/36353703863)). Throwaway PR #3 added an `HttpClient` to the core, and the guardrail failed on `System.Net.Http.HttpClient` ([run](https://github.com/hazeliscoding/axiomarium/actions/runs/36353712232)). Both PRs were closed without merging.

## M1: v0.1, asset model

Agent configuration can be inspected and validated like software.

- [x] Discovery across `agents/`, `skills/`, `hooks/`, `policies/`, `workflows/` and `experiments/`, and the `asset` schema. Manifest errors name the file, the field and the allowed values. (Done in M0.)
- [x] `CHANGELOG.md`, with the work so far under Unreleased.
- [x] Asset structure:
  - [x] the content file named after the kind, and a check that it exists and isn't empty;
  - [x] `schemas/skill.schema.json`, `hook.schema.json` and `policy.schema.json`, and a check that each of those kinds has its block and no asset has another kind's block;
  - [x] reference checks: `enforced_by` targets and eval folders.
- [x] `registry/maturity.yaml` and its schema, and a check that each maturity claim has its evidence.
- [x] Commands:
  - [x] `axm list`: assets grouped by kind, with version, maturity and harness support, filtered by `--kind` and `--harness`.
  - [x] `axm validate`: every check, printing only the problems, with exit codes for CI.
  - [x] `axm doctor`: rebuilt on the same result as `validate`, plus the inventory.
- [x] Scope sheriff:
  - [x] Ground truth: log what a real Claude Code hook receives for Write, Edit, MultiEdit and NotebookEdit, confirm which hook output reaches the model, and record the Claude Code version and the docs date.
  - [x] A glob matcher in the core (`*`, `**`, `?` and `{a,b}`), for v0.2 to extend.
  - [x] `axm hook scope-sheriff`.
- [x] Five starter assets, all experimental:
  - [x] `determinism-auditor` (agent): finds decisions an LLM shouldn't own, such as authorization, billing, irreversible actions, state transitions, invariants, retries and idempotency, and suggests the deterministic boundary.
  - [x] `agent-asset-authoring` (skill): how to write an asset and its manifest.
  - [x] `scope-sheriff` (hook): warns when an edit leaves the task's declared scope and asks the agent to explain why. `hook.md` has the `.axm/scope` contract and the settings that wire it into Claude Code.
  - [x] `deterministic-boundaries` (policy): which decisions belong to code, not to the model. Each rule names what enforces it, or says that nothing does yet.
  - [x] `prompt-fossil` (experiment): the write-up and method that v0.7 builds on.
- [x] Write-up in `docs/assets.md`: why assets carry manifests, and what each maturity level promises.
- [x] M0 review leftovers:
  - [x] the no-vault hint is chosen by reading the core's message text;
  - [x] the validator silently ignores a non-string `type` and a schema-valued `additionalProperties`;
  - [x] only JSON-shaped numbers keep their source text, but the docs say all numbers do;
  - [x] an empty maturity prints `null`, `--root <file>` says the folder doesn't exist, and a duplicate key `"a:b"` is reported as `a`;
  - [x] on Windows, virtual terminal mode is enabled for stdout but not stderr;
  - [x] the doctor's text assumes a valid manifest has a maturity and a version without saying so;
  - [x] a raw byte order mark sits in the source.
- [x] Release v0.1.0:
  - [x] the release workflow: binaries, `SHA256SUMS`, and release notes from `CHANGELOG.md`;
  - [x] the `Axiomarium` dotnet tool on NuGet through Trusted Publishing;
  - [x] a job that installs the release on fresh Linux, Windows and macOS runners and runs `axm validate`;
  - [x] install steps in the README.

**Done when:** the five starter assets pass `axm validate` in CI; breaking a manifest field, a kind block, a reference or a maturity claim makes `validate` and `doctor` name the file, the field, and the allowed values or the missing evidence; `scope-sheriff` warns a real Claude Code session about an edit outside its scope; and v0.1.0 installs from GitHub Releases and from NuGet on fresh Linux, Windows and macOS runners.

**Done (2026-09-28).** v0.1.0 is out on [GitHub Releases](https://github.com/hazeliscoding/axiomarium/releases/tag/v0.1.0) and on [NuGet](https://www.nuget.org/packages/Axiomarium).
- The five starter assets pass `axm validate` in CI on all three platforms ([run](https://github.com/hazeliscoding/axiomarium/actions/runs/36434144871)), and a test names each of them.
- Tests break a manifest field, a kind block, a reference and a maturity claim, and check that the error names the file, the field, and the allowed values or the missing evidence.
- In headless Claude Code 2.1.283 sessions, the scope sheriff's warning reached the model for an edit outside `.axm/scope` and stayed silent for one inside it.
- The release run installed the binary and the dotnet tool on fresh Linux, Windows and macOS runners and ran them ([run](https://github.com/hazeliscoding/axiomarium/actions/runs/36434175157)).

The first NuGet push was rejected until the Trusted Publishing policy's repository owner was re-entered without a stray semicolon. The 7-day notice on the policy was the clue, because a public repository's policy is active at once.

## M2: v0.2, instruction intelligence

What each harness actually reads for a file, and what it silently drops.

- [x] Ground-truth spike: record one scenario from each real harness, including user-level files in a fake home, without touching the real `~/.claude`. If Claude Code can't read a fake home cleanly, its user-level scenarios fall back to hand-checked expectations, marked as such.
- [x] Scenarios and the recorder: the `scenarios/<name>/` format, the recorder in `tools/`, the first four recordings (`launch-chain`, `agents-md-only`, `codex-byte-cap`, `codex-empty-override`), and a test that every scenario and recording is well formed.
- [x] The core: the resolution (loaded, dropped and findings, each entry with its rule id), the injected machine, and a test that replays every recording against the models.
- [x] Codex model:
  - [x] the global file in `$CODEX_HOME`: `AGENTS.override.md`, then `AGENTS.md`, the first that isn't empty;
  - [x] the chain from the project root (`project_root_markers`, `.git` by default) down to the launch directory, one file per directory (`AGENTS.override.md`, `AGENTS.md`, then `project_doc_fallback_filenames`), including the empty override;
  - [x] the 32 KiB `project_doc_max_bytes` budget: the crossing file cut, later files dropped, the global file exempt;
  - [x] untrusted projects, and files below the launch directory reported as "not loaded by the harness".
- [x] Claude Code model:
  - [x] the launch-time chain: managed policy, `~/.claude/CLAUDE.md` and `~/.claude/rules/`, then each directory from the filesystem root down to the launch directory (`CLAUDE.md`, `.claude/CLAUDE.md`, then `CLAUDE.local.md`), then `.claude/rules/**/*.md` without `paths`;
  - [x] `@path` imports: relative, absolute and `~` paths, at most 4 hops, skipped inside code spans and fenced blocks, cycles detected, and external imports marked as needing approval;
  - [x] on-demand files for the target: subdirectory CLAUDE files, and rules whose `paths` match, with the glob matcher extended to brackets, invalid patterns and the brace-expansion budget, and invalid frontmatter loading the rule for every file;
  - [x] AGENTS.md under each Project instructions mode, `claudeMdExcludes`, the 4 MiB file limit, and block-level HTML comments stripped before bytes are counted.
- [x] `axm explain <path>`: loaded and dropped files with their rules, `--harness`, `--cwd`, `--diff` and `--json`.
- [x] Findings, each with `finding.md` and fires and clean fixtures, shown by `explain` and `doctor`:

| Finding | Severity | Fires when |
|---|---|---|
| `dead-import` | warning | An `@path` import points to a file that doesn't exist |
| `import-too-deep` | warning | An import chain goes past 4 hops, so the rest never loads |
| `agents-md-hidden` | warning | Claude Code skips an AGENTS.md because a CLAUDE file exists and doesn't import it |
| `rule-frontmatter-invalid` | warning | A rule's YAML doesn't parse even after Claude Code quotes its values, so the rule loads for every file |
| `rule-matches-nothing` | warning | A path-scoped rule matches no file in the repo, or one of its patterns is invalid |
| `codex-byte-cap` | warning | The Codex chain reaches `project_doc_max_bytes`, so one file is cut and later files are dropped |
| `codex-empty-override` | warning | An empty `AGENTS.override.md` hides the `AGENTS.md` next to it |
| `duplicate-block` | info | The same paragraph loads from two different files |
| `dead-link` | warning | A Markdown link in a loaded file points to a file that doesn't exist |

- [x] `axm doctor` in any repo: the instruction findings always, and the vault checks when a vault exists, with `axiomarium.yaml` to leave out files that are broken on purpose.
- [x] `hooks/session-doctor/` and `axm hook session-doctor`: confirm on the real harness which SessionStart output the user and the model see, then build the hook, silent when all is well. Under 200 ms locally, and a CI check that holds the native binary under 1 s.
- [x] Write-up, "What your agent actually reads", on the recorded demo scenario in `scenarios/demo/`, and a second VHS tape for an `axm explain` demo GIF. The tape renders once v0.2.0 is on NuGet, because the demo workflow installs the release.
- [x] Release v0.2.0, then render `docs/demo/explain.tape` with the release and add the GIF to the README.

**Done when:** every scenario matches what the real Claude Code and Codex recorded, `--diff` on the demo scenario shows an instruction only one harness loads, all nine findings pass their fixtures, a fresh Claude Code session on the demo scenario opens with the doctor's notice, and v0.2.0 installs from GitHub Releases and NuGet on fresh runners.

**Done (2026-09-28).** v0.2.0 is out on [GitHub Releases](https://github.com/hazeliscoding/axiomarium/releases/tag/v0.2.0) and on [NuGet](https://www.nuget.org/packages/Axiomarium).
- All 14 scenarios were recorded again on Claude Code 2.1.284 and Codex 0.156.1, and both models reproduce every recording in CI ([run](https://github.com/hazeliscoding/axiomarium/actions/runs/36473235289)).
- On the demo scenario, `axm explain --diff` lists three files only Claude Code loads and two only Codex loads: the two agents share no instructions.
- All nine findings fire on their `fires` fixture and stay silent on their `clean` one.
- A fresh headless Claude Code 2.1.284 session on the demo scenario, with `session-doctor` wired in, opened with the doctor's notice, and asked what the session start reported, the model named the problems.
- The release run installed the binary and the dotnet tool on fresh Linux, Windows and macOS runners and ran them ([run](https://github.com/hazeliscoding/axiomarium/actions/runs/36473272914)). On Windows and macOS the first attempt gave up after ten minutes, before NuGet listed the platform packages, and passed on a re-run. The release workflow now waits up to thirty.
- The owner checked the colors and kaomoji in Windows Terminal.

## M3: v0.3, skills and hooks

Which skills each harness offers the model for a file, and which hooks run.

- [x] Ground-truth spike: record one scenario with skills and hooks from each harness, Claude Code locally and Codex through `record-codex.yml`, and settle what the research left open: whether the listing shows a skill's `name` or its folder, whether a plugin skill without a description is left out, where `settings.local.json` is read on Windows, and whether hook runs reach the transcript.
- [x] Scenarios and recorders: markers in skill descriptions and hook commands, `action: read | edit`, `skills` and `hooks` in `expected.json`, the Claude Code recorder reading `skill_listing` and hook events, `record-codex.yml`, and all 14 scenarios recorded again.
- [x] Claude Code skills: managed, personal, synced and project skills, enabled plugins and legacy commands, nested and `paths` skills when read, precedence, and the listing's format, caps and budget, with built-ins from the recording.
- [x] Codex skills: every root, `[[skills.config]]` and `agents/openai.yaml`, names (with a plugin repo's `plugin:skill`), and the listing's format, root table, caps and budget. `Machine` gains Codex's admin folder.
- [x] Hooks for both harnesses: sources, merging, trust, matchers and `if`, at `session-start`, `before-edit` and `after-edit`.
- [x] `axm explain`: SKILLS and HOOKS blocks for each harness, skills in `--diff`, and the new fields in `--json`.
- [x] `axm doctor`: SKILLS and HOOKS inventories and their counts, with `session-doctor` still under 1 s in CI.
- [x] Findings, each with `finding.md` and fires and clean fixtures, shown by `explain` and `doctor`:

| Finding | Severity | Fires when |
|---|---|---|
| `skill-name-clash` | warning | Two skills share a name: in Claude Code one hides the other, and in Codex both are listed and `$name` selects neither |
| `skill-description-cut` | warning | A skill's entry is longer than its harness's cap (1,536 characters in Claude Code, 1,024 in Codex), so the model sees it cut |
| `skill-listing-over-budget` | warning | A harness's listing, built-ins included, is over its budget at the stated window, so some descriptions are cut or dropped |
| `skill-frontmatter-invalid` | warning | A `SKILL.md`'s frontmatter doesn't parse or has no description: Claude Code lists its first body line instead, and Codex skips it |
| `skill-paths-match-nothing` | warning | A Claude Code skill's `paths` match no file in the repo, or a pattern is invalid, so it never becomes available |
| `hook-never-runs` | warning | A hook's matcher isn't a valid regex, it sets `if` on an event that isn't a tool event, or Codex skips its handler type |
| `codex-hook-untrusted` | warning | A Codex hook was never trusted, changed since it was, or sits in a project that isn't trusted, so it never runs |

- [x] `axm triggers`: overlapping descriptions in each harness's listing and the vault's skills, with the terms they share.
- [x] Write-up, "Which skills your agent can see", on the demo scenario with skills and a hook added, and `docs/demo/explain.tape` updated to show them.
- [x] Release v0.3.0, then render the tape with the release.

**Done when:** every scenario matches what the real Claude Code and Codex recorded, skill listings and hooks included; `axm explain` on the demo scenario shows a skill only one harness lists and a hook only one harness would run for the file; all seven findings pass their fixtures; `axm triggers` on my own setup names each overlapping pair with the terms it shares; and v0.3.0 installs from GitHub Releases and NuGet on fresh runners.

**Done (2026-09-28).** v0.3.0 is out on [GitHub Releases](https://github.com/hazeliscoding/axiomarium/releases/tag/v0.3.0) and on [NuGet](https://www.nuget.org/packages/Axiomarium).
- All 18 scenarios hold recordings from Claude Code 2.1.284 and Codex 0.156.1, skill listings and hooks included, and both models reproduce every one in CI ([run](https://github.com/hazeliscoding/axiomarium/actions/runs/36513196412)).
- On the demo scenario, `axm explain` shows `release` listed only by Claude Code and `db-migration` only by Codex, and the after-edit hook runs only in Claude Code, because Codex doesn't load an untrusted project's hooks. A test holds it.
- All seven findings fire on their `fires` fixture and stay silent on their `clean` one.
- `axm triggers` on my own setup compared 80 Claude Code and 39 Codex skills and named 84 overlapping pairs, each with the terms it shares, in 156 ms from the native binary.
- The release run installed the binary and the dotnet tool on fresh Linux, Windows and macOS runners and ran them, all on the first attempt ([run](https://github.com/hazeliscoding/axiomarium/actions/runs/36513226561)).

## M4: v0.4, trigger testing

Whether the model picks the right skill for a prompt, measured on the real harnesses.

- [x] Spike: one `claude -p` and one `codex exec --json` session on the demo scenario, to settle how a pick shows in each stream, how to stop a session right after it, and how to get a one-turn text answer with tools off. The results go in the decisions, and the trimmed streams become parser fixtures.
- [x] Prompt files: `schemas/trigger-prompts.schema.json`, and `axm doctor` validating `skills/<name>/evals/trigger/prompts.yaml`.
- [ ] `axm triggers generate <skill>`: the brief with the skill's rivals, one-turn generation with a schema check and one retry, the preview and approval, `--yes` and `--replace`.
- [ ] The runner: the throwaway copy with the vault's skills written in, four sessions at a time, each stopped at its first action, and a parser for each harness's stream.
- [ ] `axm triggers test`: the plan line, precision and recall for each skill and harness, and each collision, false trigger and miss with its cause.
- [ ] `axm triggers export <skill> --format skill-creator|promptfoo`, printed, with CI running `promptfoo validate` on the export.
- [ ] Prompts for `agent-asset-authoring`: generated, reviewed by the owner, committed, and `evals.trigger: true` set.
- [ ] Docs in `docs/`: `docs/README.md` as the home and reading order, `docs/cli.md` for every command, option, exit code and output contract, and `docs/findings.md` linking each finding, with tests that relative links resolve and that every command and finding id is documented.
- [ ] Write-up, "Does your agent pick the right skill?", on a real run.
- [ ] Release v0.4.0.

**Done when:** `axm triggers test` on the vault's skills reports precision and recall for each one on both harnesses, and names every collision, false trigger and miss with a cause from the listing; the vault skill's prompts were written by `generate` after approval; the promptfoo export validates in CI; the docs tests pass; and v0.4.0 installs from GitHub Releases and NuGet on fresh runners.

**For planning (research, 2026-09-28).** Every trigger tester found drives the real harness headless: Anthropic's skill-creator runs `claude -p` and counts a first `Skill` call, `claude plugin eval` grades `tool_used: Skill`, and promptfoo asserts `skill-used` over the Claude Agent SDK and the Codex SDK. None calls a model API with a rebuilt listing, and Claude Code's listing text isn't published. `claude plugin eval` runs in a sealed home, so it can't see the other skills a prompt competes with. `codex exec --json` has no skill event; Codex's own source counts a read of `SKILL.md` as use. The formats worth emitting are the skill-creator's `{query, should_trigger}` set and a `promptfooconfig.yaml`.

## M5: v0.5, evals

- [ ] `axm eval run` and `axm eval compare`: a baseline against a candidate, recording result, cost, tokens, latency and behavior.
- [ ] Behavioral and regression evals for the starter assets, with history in `.axm/cache.db`.
- [ ] Providers sit behind one interface, so no eval depends on a single vendor.
- [ ] `axm conflicts --judge`: contradictions between the instructions a file gets, found by a model and labeled as model judgment. (Moved from v0.2.)

**Done when:** comparing an asset before and after a change reports behavior, tokens and latency for both versions, on two different model providers.

## M6: v0.6, evidence

- [ ] `schemas/evidence.schema.json`, and `axm evidence record` and `axm evidence check`.
- [ ] `axm evidence`: each kind of check (build, unit tests, Terraform validate…) as FRESH, STALE or MISSING, with the reason.
- [ ] `hooks/evidence-freshness/`: records test and build runs as the agent makes them.

**Done when:** editing a file covered by recorded test evidence turns that evidence STALE with the reason, and running the tests again turns it FRESH.

## M7: v0.7, failure engineering

- [ ] `schemas/incident.schema.json`, and `axm incident new`, which creates `incidents/<date>-<name>/` with `incident.md`, `evidence.yaml`, `root-cause.md` and `regression.yaml`.
- [ ] `axm distill <incident>`: the failure class, the root cause, existing protections and possible responses, which prefer an eval or a hook over a new global instruction.
- [ ] `axm regress`: runs every incident's regression eval.
- [ ] `agents/failure-distiller/` and `workflows/failure-to-guardrail/`.

**Done when:** one real incident from my own sessions goes from `axm incident new` to a regression eval that fails without its guardrail and passes with it.

## M8: v0.8, prompt fossil

- [ ] `axm fossil`: uses eval history to find instructions that add tokens or latency, rarely matter, reduce capability or activate needlessly, and suggests a replacement for each.

**Done when:** `axm fossil` on my own instruction files reports each candidate with its measured token and quality impact, and changes no file.

## M9: v0.9, adapters

- [ ] `axm sync`: generates each harness's files from the canonical assets (`.claude/`, `.agents/`, `.github/`), shows the diff, and writes only what is approved.
- [ ] Adapters for Claude Code, Codex, GitHub Copilot and a generic target.

**Done when:** one skill synced to all three harnesses loads in each of them, confirmed with `axm explain` and the real harness.

## M10: v0.10, project intelligence

- [ ] `axm detect`: runtime, frontend, database, infrastructure, CI and the agent harnesses in use. It absorbs the `axm inspect` once planned for v0.1.
- [ ] `axm recommend`: assets whose manifests fit what was detected.
- [ ] `axm init`: uses both, shows the `axiomarium.yaml` it would write and the assets it would install, and writes only what the user approves.

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
- `axm triggers test` for any skill in the listing, not only the vault's, and a `--sealed` home for a clean baseline.
- A generated docs site from `docs/`, near v1.0, with pages for wiring hooks and for how the ground truth works.
- winget and Homebrew.
- A short launch video.

## Not planned

- An AI IDE, an agent runtime or an autonomous coding agent.
- A prompt or MCP marketplace, or another giant collection of community prompts.
- An LLM gateway or a model router.
- An observability service, or anything hosted: no accounts and no telemetry.
- Silently rewriting instruction files. `axm` recommends, and writes only when asked.
- Asking the model why it picked a skill. Causes come from the listing.

## How we'll know it works

Evidence comes from dogfooding on my own repos, recorded in `docs/dogfooding.md`. The clearest sign is that my own agent failures turn into regression evals instead of new paragraphs. After releases, issues, PRs and downloads from other people are a bonus.
