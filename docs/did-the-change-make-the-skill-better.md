# Did the change make the skill better?

[Does your agent pick the right skill?](does-your-agent-pick-the-right-skill.md) measured whether the agent picks a skill. It said nothing about what the agent does once it has the skill. v0.5 measures that. `axm eval run` gives an asset a task in a throwaway repo and checks what the session did, and `axm eval compare` runs a changed asset against its last commit, side by side.

## The test

Each asset's eval cases are small folders: a prompt, the repo the session starts in, and checks that read what the harness recorded, never what the reply claims. The vault's skill, [`agent-asset-authoring`](../skills/agent-asset-authoring/skill.md), has three:

- [`new-hook`](../skills/agent-asset-authoring/evals/behavioral/new-hook/eval.yaml): add a hook that warns when a migration changes. The checks want the skill loaded, a hook that runs after an edit and only warns, no harness files inside the asset, and `axm validate` passing.
- [`new-skill`](../skills/agent-asset-authoring/evals/behavioral/new-skill/eval.yaml): add a skill for safe database migrations, with a `use_when`, that validates.
- [`prefers-tooling`](../skills/agent-asset-authoring/evals/behavioral/prefers-tooling/eval.yaml): add a policy so agents never commit an AWS secret key. The rule belongs in a tool, and the skill's first question is whether tooling could enforce it instead.

A case can also carry a rubric, which a model grades after the checks. `prefers-tooling` leans on it: "The agent says that tooling, such as a secret scanner in a pre-commit hook or CI, should enforce this rather than an instruction alone."

The first runs showed where the skill fell short. On Claude Code, the judge passed every case. On Codex with gpt-6-sol, the agent wrote an honest policy for `prefers-tooling`, with nothing claimed in `enforced_by`, but never said which tool should enforce the rule. So I made one change to the skill:

```diff
- **Could tooling enforce it instead?** A test, a lint rule, a database constraint or a hook beats an instruction the model may ignore. Prefer the tooling.
+ **Could tooling enforce it instead?** A test, a lint rule, a database constraint, a pre-commit hook or a CI check beats an instruction the model may ignore. When one could, tell the user which one before you write anything, even if you write the asset too.
```

Then `axm eval compare agent-asset-authoring` ran the three cases on `HEAD` and on the working tree, three runs a side, on Claude Code 2.1.285 with claude-opus-5-5 and on Codex 0.156.1 with gpt-6-sol. Every session ran in a sealed home, which holds my logins and the model I chose for each harness, and nothing else of my setup, and baseline and candidate sessions alternated.

## What came back

The checks pass a run. The judge's verdict is model judgment and never changes that, so it gets its own column:

| Case | Harness | Checks passed, before → after | Judge passed, before → after |
|---|---|---|---|
| `new-hook` | Claude Code | 3 of 3 → 3 of 3 | 3 of 3 → 3 of 3 |
| `new-skill` | Claude Code | 3 of 3 → 3 of 3 | 3 of 3 → 3 of 3 |
| `prefers-tooling` | Claude Code | 3 of 3 → 3 of 3 | 3 of 3 → 3 of 3 |
| `new-hook` | Codex | 2 of 3 → 2 of 3 | 2 of 2 → 2 of 2 |
| `new-skill` | Codex | 3 of 3 → 3 of 3 | 3 of 3 → 3 of 3 |
| `prefers-tooling` | Codex | 3 of 3 → 3 of 3 | **0 of 3 → 2 of 3** |

The change did what it was for, where it was needed. On Codex, the judge found the agent naming the tool in two of three runs, where it had in none. On Claude Code there was nothing to fix, and nothing broke. The checks didn't move anywhere, so the rest of the skill held.

The tokens and times moved both ways without meaning much. On Claude Code, the median input fell by 27,314 tokens on `new-hook` and rose by 56,070 on `new-skill`, with the two sides' ranges overlapping, and the cost moved by one to three cents a session. Three runs a side is a small sample, and `axm` says so instead of calling a winner. The two failed Codex runs of `new-hook`, one a side, were stopped at the ten-minute timeout.

## What the runs got wrong at first

Nothing here came out of the design as planned. Each of these showed up in a real run:

- **The sealed home leaked on Codex.** On Windows, Codex reads `~/.agents/skills` from the real home, whatever `USERPROFILE` and `HOME` say, so a sealed session listed 34 of my own skills. The sealed config now turns each one off.
- **Codex reads the whole disk on Windows.** Its sandbox keeps writes in the copy, but it can't deny reads there. In one run the agent listed `C:\ai`, found this repo, and read its real hooks for the answer, and in another it read a parallel session's copy. The report now names every path outside the copy that a run's commands touched.
- **Codex's sandbox starts slowly.** A new home's first command stalls for two to five minutes, so a run warms it with one unscored session. The compare showed that isn't the whole story: in sessions that started together right after the warm-up, a first command could still stall for about 400 seconds, while the sessions that started later ran every command in seconds. The report names each case's slowest call, which is how that showed.
- **What counts as a command.** A command the harness denied never ran, so it no longer counts, and a Git Bash path such as `/c/axm-evals/…` is the copy, not somewhere else.
- **A case has to make its behavior happen.** The first `scope-sheriff` case asked for a rename "in `src/price.ts`", so the agent stayed inside the scope and the hook never fired. It now asks for the rename everywhere the function is used.

## What this doesn't tell you

- **Whether the change is better in general.** Three runs a side on one model per harness is a signal. A rerun can differ.
- **Whether the judge is right.** Its verdicts are a model's reading of the diff and the reply, labeled as such, and they never pass or fail a run.
- **How the asset behaves in your own setup.** The sealed home leaves out your plugins, hooks and instructions, which is what keeps runs comparable, and also what it doesn't test.
- **How fast Codex is.** On Windows, its times and tokens carry the sandbox's stalls.

## Try it on your assets

Add a case under an asset's `evals/behavioral/`, as [Eval cases](assets.md#eval-cases) describes, then:

```text
axm eval run <asset>
axm eval compare <asset>
```

`axm eval compare --baseline none` asks a different question: does the asset help at all? Each run is saved in `.axm/evals/`, and a compare reuses a saved baseline while it still describes the files, so a second compare after another change runs only the new version.
