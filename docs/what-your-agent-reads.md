# What your agent actually reads

You write the rules in CLAUDE.md or AGENTS.md and assume the agent follows them. Whether it even sees them depends on things you rarely look at: which harness you run, which folder it starts in, which file the agent opens, and a handful of loading rules that the docs leave out or get wrong. A session looks the same whether its instructions arrived or not.

So we recorded what Claude Code and Codex really load, file by file, and built `axm explain` to show it.

## One repo, two agents

The demo is a small shop, in [`scenarios/demo/`](../scenarios/demo). It's the kind of setup that grows by accident:

- `AGENTS.md` holds the rules for every coding agent: run the tests before you commit, never commit secrets.
- `CLAUDE.md` was added later for one Claude-specific note. It also imports `docs/testing.md`, which someone has since renamed to `docs/tests.md`.
- `.claude/rules/api.md` is a rule for files under `src/api/`.
- `src/api/AGENTS.md` holds the API's own rules.

Here is what each harness loads when the agent works on `src/api/orders.cs`:

```text
$ axm explain src/api/orders.cs

AXM EXPLAIN // src/api/orders.cs

  CLAUDE CODE // launched at the repo root
  01  ~/.claude/CLAUDE.md    user                at launch
  02  CLAUDE.md              project             at launch
  03  .claude/rules/api.md   paths: src/api/**   when the file is read
  --  docs/testing.md        DROPPED     file is missing, imported by CLAUDE.md:6
  --  AGENTS.md              DROPPED     a CLAUDE file exists and doesn't import it
  --  src/api/AGENTS.md      DROPPED     a CLAUDE file exists and doesn't import it

  CODEX // launched at the repo root
  01  ~/.codex/AGENTS.md     global              at launch
  02  AGENTS.md              project             at launch
  --  src/api/AGENTS.md      NOT LOADED  below the launch directory

WARNING  agents-md-hidden
         Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
         Fix: Add @AGENTS.md to CLAUDE.md.

WARNING  dead-import
         CLAUDE.md:6 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place.
         Fix: Restore the file, or remove the import.

WARNING  agents-md-hidden
         Claude Code skips src/api/AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
         Fix: Add a CLAUDE.md next to it that says @AGENTS.md.

2 harnesses · 5 loaded · 4 not loaded · 3 warnings
```

Adding one CLAUDE.md took every AGENTS.md away from Claude Code, the nested one too. Codex never sees `CLAUDE.md` or the API rule. The API's own rules reach neither harness: Claude Code hides them, and Codex leaves files below its launch directory to the model, which may or may not open them. `--diff` puts the split side by side:

```text
$ axm explain src/api/orders.cs --diff

AXM EXPLAIN // src/api/orders.cs // diff

  ONLY CLAUDE CODE
  01  ~/.claude/CLAUDE.md    user                at launch
  02  CLAUDE.md              project             at launch
  03  .claude/rules/api.md   paths: src/api/**   when the file is read

  ONLY CODEX
  01  ~/.codex/AGENTS.md     global              at launch
  02  AGENTS.md              project             at launch

3 only in Claude Code · 2 only in Codex
```

The two agents share no instructions at all.

## What the recordings settled

Most of what the harnesses do is in their docs. These are the parts that weren't, or that the harness does differently. Each one has a scenario that recorded it, and most have a finding that catches it.

1. **One CLAUDE file hides every AGENTS.md.** Claude Code reads AGENTS.md only when no `CLAUDE.md`, `.claude/CLAUDE.md` or `CLAUDE.local.md` exists in the launch directory or above, and that switch covers nested AGENTS.md files too. [`agents-md-mixed`](../scenarios/agents-md-mixed), [`agents-md-hidden`](../findings/agents-md-hidden/finding.md).
2. **A rule with broken frontmatter never loads.** The docs say it loads for every file. An unclosed bracket or quote in the `paths` list makes Claude Code skip the rule entirely. [`bad-frontmatter`](../scenarios/bad-frontmatter), [`rule-frontmatter-invalid`](../findings/rule-frontmatter-invalid/finding.md).
3. **An import ends at the next space.** `See @docs/testing.md, then run it` imports a file named `testing.md,`, which doesn't exist, so nothing loads and nothing says so. [`imports`](../scenarios/imports), [`dead-import`](../findings/dead-import/finding.md).
4. **Four hops, then nothing.** An import chain stops after four hops from the file that loaded first. [`imports`](../scenarios/imports), [`import-too-deep`](../findings/import-too-deep/finding.md).
5. **Codex shares one budget across project files.** `project_doc_max_bytes`, 32 KiB by default, covers the whole chain from the project root down. The file that crosses it is cut mid-file and the files after it are dropped. The docs contradict each other on this, so the code decided. [`codex-byte-cap`](../scenarios/codex-byte-cap), [`codex-byte-cap`](../findings/codex-byte-cap/finding.md).
6. **An empty override hides its neighbor.** Codex picks `AGENTS.override.md` over `AGENTS.md` because it exists, and only then checks whether it's empty, so emptying an override turns off the folder's instructions. [`codex-empty-override`](../scenarios/codex-empty-override), [`codex-empty-override`](../findings/codex-empty-override/finding.md).
7. **Launched in a subfolder, Claude Code drops imports outside it.** When Claude Code starts in `src/app/`, the root `CLAUDE.md` still loads, but its import of `docs/guide.md` doesn't, although the file is in the same repo. A headless session never loads a project import from outside its launch directory. [`import-from-subdir`](../scenarios/import-from-subdir).
8. **HTML comments take the blank lines after them.** Claude Code strips a block comment before the model sees the file, together with the blank lines that follow it, so an import inside a comment never loads. [`agents-md-mixed`](../scenarios/agents-md-mixed).

## How we know

Each scenario in [`scenarios/`](../scenarios) is a tiny repo with a fake home folder, a launch directory and a target file, and every Markdown file in it starts with a unique marker line. A recorder, never shipped, runs the real harnesses on a copy outside the user's home folder:

- **Codex:** `codex debug prompt-input` with `CODEX_HOME` pointed at the fake home. It needs no login or network, and prints the AGENTS.md block the model would get, in order.
- **Claude Code:** a short Haiku session, with its config folder pointed at a copy of the fake `.claude` and a borrowed login that is deleted when the run ends. The session transcript records which files loaded and in what order, and an `InstructionsLoaded` hook records why.

The markers show which file each piece of loaded text came from, even when a harness cuts a file short. The recordings are committed as `expected.json`, and CI replays every one against the models that `axm explain` runs, so a model that disagrees with a real harness fails the build. They were made with Claude Code 2.1.283 (the demo with 2.1.284) and Codex 0.156.1. When a harness updates, the recordings are made again and the diff says what changed.

Three limits:

- The Claude Code sessions are headless. What the terminal shows you comes from the docs.
- A few cases can't be recorded with a fake home, such as a managed policy file or the 4 MiB limit, so those follow the docs.
- Skills and hooks aren't covered yet. `axm explain` lists them from v0.3, as available rather than loaded, because the model decides when a skill loads.

## Check your own repo

```text
axm explain path/to/file          what each harness loads for that file, and what it drops
axm explain path/to/file --diff   only the files one harness loads
axm doctor                        every instruction file in the repo, and every finding
```

`axm doctor` works in any repo, with no setup. To hear about problems without asking, wire [`session-doctor`](../hooks/session-doctor/hook.md) into Claude Code: when a session starts in a repo with a problem, you get one line and the model gets the details. When all is well, it says nothing.
