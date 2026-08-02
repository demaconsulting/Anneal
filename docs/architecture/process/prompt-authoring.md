---
level: section
covers:
  - .github/agents/**
  - .github/standards/**
  - AGENTS.md
---

[← Process](../process.md)

# Prompt Authoring

This document covers how a prompt earns the tokens it costs, and it meets the **cross-cutting mechanism**
creation test: every agent prompt and every standard in the payload must participate in it correctly, and
a file that gets it wrong degrades the behavior of whatever agent loads it rather than failing visibly.
It is the one place in this repository where writing style is a correctness concern.

## What a Prompt Costs

Prompt text is paid for on **every invocation**, unlike documentation a human reads once. The load for a
single agent invocation is `AGENTS.md`, plus one agent prompt, plus the two to four standards that agent
selects — nothing else. Measured against the current payload, the worst case is roughly six to thirteen
thousand tokens, with `AGENTS.md` around a fifth of it.

That budget is not currently under pressure, and stating so matters: the reason to write tersely here is
**not** to save tokens. It is that a rule buried in a long passage is a rule an agent may not act on. The
budget is a ceiling that `PROCESS-06` defends; conciseness is about attention, which no clause can
measure.

The practical consequence is that content belongs at the cheapest level that still guarantees it is read:

- `AGENTS.md` — loaded always. Routing and classification only; it must never explain.
- An agent prompt — loaded when that agent runs. Its procedure and its stop conditions.
- A standard — loaded on demand by task. The single definition of a subject.
- A skill — loaded only in the situation it describes. Repeatable procedures with worked failures.

Moving a rule down this list makes it cheaper and later; moving it up makes it more certain and more
expensive. A rule in the wrong place is either unread or paid for constantly.

## When a Why Earns Its Place

The temptation is to strip prompts to imperatives. That is wrong, and the failure it produces is worse
than verbosity: an agent given a bare rule and an unanticipated situation has nothing to reason from, so
it either applies the rule where it does not fit or abandons it entirely.

A justification earns its place when it does one of these:

- **Names the rejected alternative.** "Do not call `architecture-design`" is a rule an agent will bend
  when calling seems efficient. "Called headless it would have nobody to ask and would invent the answers
  — worse than producing none" is a rule it can apply to a case nobody wrote down.
- **Explains a counter-intuitive instruction.** Any rule that looks like unnecessary friction will be
  optimized away unless the friction is explained.
- **Marks a boundary that must not be crossed under pressure.** Stop conditions are exactly where an
  agent is most motivated to reason its way past.

A justification does **not** earn its place when it restates the rule in other words, motivates something
obvious, or explains a subject that has its own owning standard — in that last case, link and stop.

Because this repetition is deliberate and load-bearing, it is worth naming what it is doing. Stating a
rule and then its reason gives the same instruction two independent chances to land: once as a
prescription, once as something the agent can re-derive. Rules that must survive a novel situation get
both. Rules that are mechanical get one.

## Actionability

An instruction is actionable when an agent can tell, without judgement, whether it has complied. Prefer:

- **Imperative and concrete.** Name the file, the command, the exact section to edit.
- **A stated outcome.** What must be true when the step is done.
- **An explicit failure branch.** What to do when it is not — every result an agent can emit must be
  something its caller handles, which is why `PROCESS-05` exists.

Avoid hedging inside a mandatory instruction. "Consider", "if appropriate" and "try to" convert a rule
into a preference, and an agent under pressure will read them exactly that way. If the instruction is
genuinely conditional, state the condition.

## Consequences

A developer or agent editing anything under `.github/agents/` or `.github/standards/` must:

- **Place the rule at exactly one level**, and link rather than restate it from anywhere else. Duplication
  is the failure `PROCESS-I2` targets; a rule stated twice drifts, and the copy read first wins.
- **Add the reason when the rule is a boundary, a stop condition, or counter-intuitive** — and leave it
  out when the rule is mechanical.
- **Check the result values.** Adding a new outcome to an agent obliges every caller to handle it. This
  is the defect class that has survived manual review here before.
- **Not trim prose that carries a rejected alternative.** A short prompt missing the *why* is the more
  expensive failure, because the reasoning cannot be recovered from anywhere else.

Editing a prompt also invalidates any behavioral verification recorded against that agent. Structural
clauses are re-checked by script; behavioral claims must be re-established by inspection or a sandbox run,
because the evidence described the prose that just changed.
