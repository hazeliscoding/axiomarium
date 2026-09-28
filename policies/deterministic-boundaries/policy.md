# Deterministic boundaries

When you write or change code that acts on a model's output, keep every decision that must be correct, repeatable or auditable in deterministic code. **The model proposes, code decides.** The model is good at reading intent, classifying, extracting and drafting. It isn't a safe owner for a decision that must come out the same way every time, that someone must be able to audit, or that can't be undone.

## Rules

- **authorization-in-code:** Code decides who someone is and what they may do. Model output never grants access, even when it "clearly" identifies the user.
- **money-in-code:** Code computes and approves prices, refunds, discounts and charges. The model may explain a charge or classify a request, but never sets the amount or approves it.
- **irreversible-actions-need-a-gate:** Deleting data, sending a message, deploying, and dropping or migrating a table run only after a deterministic check or a human approval.
- **state-transitions-in-code:** Code moves a record from one state to another, such as approving, closing or escalating. The model may only propose the move.
- **invariants-in-the-data-layer:** Constraints or code enforce uniqueness, limits and balances. An instruction such as "never create duplicates" is not a guarantee.
- **idempotent-side-effects:** Every side effect a model can trigger carries an idempotency key, so a retry, or a model that repeats itself, can't apply it twice.
- **validated-model-output:** Code parses model output against a schema, such as a typed tool call or a JSON schema, and validates it before acting on it. Free text is never matched loosely to pick an action.
- **allowlisted-reach:** The tools, files, hosts and secrets a model can reach are an allowlist in code or configuration, not a list in the prompt.
- **traceable-decisions:** Every decision a model proposes is logged with its input and the verdict the code reached, so a wrong decision can be traced to its cause.

## Applying it

For each place model output is read, follow it to the effect it has, and ask what stops a wrong answer. If nothing does, move the decision into code and keep the model on the part it's good at. For example, the model classifies a refund request, and code evaluates the refund policy and executes the refund.

A rule here is not enforced by being written down. The `determinism-auditor` agent reviews code against the first eight rules. Nothing enforces `traceable-decisions` yet.
