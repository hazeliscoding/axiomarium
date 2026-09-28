# Prompt fossil

**Status:** designed, not run. v0.5 brings the eval runner this needs, and v0.8 turns the method into `axm fossil`.

## Hypothesis

Instruction files collect fossils. A rule goes into `CLAUDE.md` after one bad session and never leaves. Months later it no longer changes what the agent does, yet it still costs tokens and attention in every session. Some fossils are worse than inert: removing them makes the agent better.

## What counts as a fossil

- **Inert:** removing it changes no measured result, but it adds tokens or latency to every session.
- **Misplaced:** it matters only for a task that rarely comes up, yet loads every session. It belongs in a skill or a path-scoped rule.
- **Harmful:** removing it improves results, because it over-constrains the agent.
- **Over-eager:** it pulls the agent toward behavior on tasks where it doesn't apply.

## Method

1. **Split the file into instructions.** Take one instruction file, such as a repo's `CLAUDE.md`, and give each bullet or paragraph an id.
2. **Build the eval set.** Collect tasks from real work in the repo, each with a pass check that code decides: tests pass, a file has a property, the diff stays inside the expected paths. Aim for ten tasks or more. An instruction that no task exercises can't be judged, so list it as untested.
3. **Run the baseline.** Run every task with the full file, several times each, because model output varies. Record the pass rate, input tokens and wall-clock time.
4. **Ablate.** For each instruction, run the whole set again with that instruction removed. Where it makes sense, also run it moved into a skill or a path-scoped rule.
5. **Compare.** An instruction whose removal moves no pass rate beyond the spread between baseline runs is a fossil candidate. One whose removal raises a pass rate is harmful.
6. **Recommend.** For each candidate, recommend one of: delete it, move it, or replace it with tooling such as a test, a lint rule or a hook. Never delete or rewrite anything automatically.

## Measurements

- The pass rate for each task, with its spread across repeats.
- The input tokens each instruction costs per session.
- Wall-clock time per task.
- The model, the harness and their versions, since a fossil for one model may still matter to another.

## What would disprove it

- If removing instructions one at a time never moves a result beyond the noise, and the full file doesn't beat an empty one either, the evals are too weak to tell. That says nothing about the instructions.
- If most instructions measurably help, fossils are rare, and `axm fossil` isn't worth building.

## Risks

- **Noise:** single runs mislead. Report spreads, not single numbers, and repeat runs until the baseline is stable.
- **Coverage:** an instruction for a rare situation looks like a fossil when no task hits it. Call it untested, not a fossil.
- **Interactions:** two instructions can each look inert because the other covers the same ground. Remove candidates in pairs before calling both fossils.
- **Cost:** instructions × tasks × repeats is a lot of model calls. Estimate the cost before running, and start with a small file.
