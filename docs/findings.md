# Findings

A finding is something `axm doctor` and `axm explain` notice in what the harnesses load: an instruction, skill or hook that doesn't reach the agent where it was meant to, or reaches it where it wasn't. Each one is read from the same harness models `axm explain` runs, so a finding never disagrees with what `explain` shows.

Findings are warnings or info, never errors, so they don't fail `axm doctor`. Each id is stable, and each has a page with what happens, why it matters, the fix and its source, and two tiny repos that prove it: one where it fires and one where it doesn't.

| Finding | Severity | Harness | Fires when |
|---|---|---|---|
| [`dead-import`](../findings/dead-import/finding.md) | warning | Claude Code | An `@path` import points to a file that doesn't exist |
| [`import-too-deep`](../findings/import-too-deep/finding.md) | warning | Claude Code | An import chain goes past the four hops Claude Code follows |
| [`agents-md-hidden`](../findings/agents-md-hidden/finding.md) | warning | Claude Code | A CLAUDE file makes Claude Code skip an AGENTS.md written for every agent |
| [`rule-frontmatter-invalid`](../findings/rule-frontmatter-invalid/finding.md) | warning | Claude Code | A rule's frontmatter doesn't parse, so it loads for every file |
| [`rule-matches-nothing`](../findings/rule-matches-nothing/finding.md) | warning | Claude Code | A rule's `paths` match no file in the repo, so it never loads |
| [`codex-byte-cap`](../findings/codex-byte-cap/finding.md) | warning | Codex | Project files go past Codex's 32 KiB budget, so one is cut and later ones dropped |
| [`codex-empty-override`](../findings/codex-empty-override/finding.md) | warning | Codex | An empty `AGENTS.override.md` hides the `AGENTS.md` next to it |
| [`duplicate-block`](../findings/duplicate-block/finding.md) | info | Claude Code and Codex | The same paragraph loads from two files |
| [`dead-link`](../findings/dead-link/finding.md) | warning | Claude Code and Codex | A loaded file links to a relative path that doesn't exist |
| [`skill-name-clash`](../findings/skill-name-clash/finding.md) | warning | Claude Code and Codex | Two skills share a name, so one is hidden, or a `$name` mention picks neither |
| [`skill-description-cut`](../findings/skill-description-cut/finding.md) | warning | Claude Code and Codex | A skill's description is longer than the harness shows for one skill |
| [`skill-listing-over-budget`](../findings/skill-listing-over-budget/finding.md) | warning | Claude Code and Codex | The skill listing is over its budget, so some skills lose their descriptions |
| [`skill-frontmatter-invalid`](../findings/skill-frontmatter-invalid/finding.md) | warning | Claude Code and Codex | A `SKILL.md`'s frontmatter can't be used, so it's listed by its first line or skipped |
| [`skill-paths-match-nothing`](../findings/skill-paths-match-nothing/finding.md) | warning | Claude Code | A skill's `paths` match no file in the repo, so it's never listed |
| [`hook-never-runs`](../findings/hook-never-runs/finding.md) | warning | Claude Code and Codex | A hook is set up so the harness never runs it |
| [`codex-hook-untrusted`](../findings/codex-hook-untrusted/finding.md) | warning | Codex | A Codex hook isn't trusted, changed since it was, or sits in an untrusted project |
