# Axiomarium docs

Start with the [README](../README.md) for what Axiomarium is and how to install it. Then, in this order:

1. [What your agent actually reads](what-your-agent-reads.md): which instruction files Claude Code and Codex load for a file, which they drop, and how the recordings settle it.
2. [Which skills your agent can see](which-skills-your-agent-can-see.md): which skills each harness lists, which hooks run, and what keeps them from the agent.
3. [The axm command line](cli.md): every command and option, the exit codes and the output contracts.
4. [Findings](findings.md): every problem `axm doctor` and `axm explain` report, with a page for each.
5. [Assets](assets.md): why vault assets carry a manifest, and what each maturity level promises.

For how the project decides and plans, see [ROADMAP.md](../ROADMAP.md). For what changed in each release, see [CHANGELOG.md](../CHANGELOG.md). The demo repo behind both write-ups is [`scenarios/demo`](../scenarios/demo), and the hooks' own pages are [`session-doctor`](../hooks/session-doctor/hook.md) and [`scope-sheriff`](../hooks/scope-sheriff/hook.md).
