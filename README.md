<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/lockup-dark.svg">
    <img alt="Axiomarium" src="docs/brand/lockup.svg" height="40">
  </picture>
</h1>

**See exactly which instructions your coding agent loads for any file, and what it silently drops.** `axm explain` reads the CLAUDE.md files, AGENTS.md files, rules and imports in a repo, and shows what Claude Code and Codex each load for a given file, in order, with the reason for every one.

Instructions for coding agents now live in many files: a global CLAUDE.md, the project's AGENTS.md, nested files per directory, path-scoped rules and imports. Each harness loads them by its own rules, and none of them tells you when something is skipped. A `CLAUDE.local.md` makes Claude Code stop reading your AGENTS.md. A rule with broken frontmatter loads for every file instead of the ones it names. Codex stops adding files once they reach 32 KiB. You find out when the agent ignores a rule you know you wrote.

> **Status:** planning. There is nothing to install yet. See [ROADMAP.md](ROADMAP.md).

## How it works

The output below is the planned v0.1 output for a small repo. Its `CLAUDE.md` imports a missing file, doesn't import `AGENTS.md`, and sits next to a rule scoped to `src/api/**`.

```text
$ axm explain src/api/orders/OrderService.cs --harness all

CLAUDE CODE // launched at repo root
  01  ~/.claude/CLAUDE.md          user                at launch
  02  CLAUDE.md                    project             at launch
  03  .claude/rules/backend.md     paths: src/api/**   when the file is read
  --  AGENTS.md                    DROPPED  CLAUDE.md exists and doesn't import it
  --  src/api/AGENTS.md            DROPPED  CLAUDE.md exists and doesn't import it
  --  docs/testing.md              DROPPED  imported by CLAUDE.md:12, file is missing

CODEX // launched at repo root
  01  ~/.codex/AGENTS.md           global              at launch
  02  AGENTS.md                    project             at launch
  --  src/api/AGENTS.md            NOT LOADED  below the launch directory

WARNING  agents-md-hidden
         Codex reads AGENTS.md. Claude Code skips it, because CLAUDE.md doesn't import it.
         Fix: add @AGENTS.md to CLAUDE.md.

WARNING  dead-import
         CLAUDE.md:12 imports docs/testing.md, which does not exist.
         Fix: restore the file, or remove the import.

2 harnesses · 5 loaded · 4 not loaded · 2 warnings
```

The same file gets different instructions from each harness. Claude Code applies the backend rule but never sees AGENTS.md. Codex sees AGENTS.md but not the rule, and not the AGENTS.md in `src/api/`.

## What it does

- **Resolves** the instructions a harness loads for a file, in context order, with why each one loaded and when: at launch, or when the agent reads the file.
- **Shows what's dropped:** files that are excluded, hidden by another file, past the byte cap or too many imports deep.
- **Compares harnesses.** `--diff` shows what Claude Code sees that Codex doesn't, and the reverse.
- **Flags problems:** dead imports and links, rules that match no file or load for every file, and blocks duplicated across files.

## Deterministic, and honest about it

- **No model in the loop.** Every line of output comes from your files and a documented loading rule. `axm` makes no network calls and never edits your files.
- **Checked against the real harness.** Each harness model names the docs and version it was built against, and its test scenarios are confirmed by running the real Claude Code and Codex.
- **Clear about what it can't know.** When a harness leaves loading to the model, as Codex does with an AGENTS.md below the launch directory, `axm` says "not loaded by the harness" instead of guessing.

## Axiomarium

Axiomarium is a lab for tools that make coding agents observable, constrained, testable and correctable, rather than smarter. Each piece ships as a working tool, a demo you can reproduce and a write-up of the thinking behind it. `axm explain` is the first piece.

## Contributing

Each finding will be one folder: a note that explains it, and two small fixture repos, one that triggers it and one that doesn't. A contributor guide arrives with v0.1.

## License

[Apache-2.0](LICENSE)
