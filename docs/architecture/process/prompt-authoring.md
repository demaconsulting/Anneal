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
selects — nothing else.

**The context budget is 20,000 tokens**, and this document is where that number is declared; `PROCESS-06`
is the clause that defends it, over the worst-case load the clause itself defines. Measured against the
current payload:

| File | Tokens |
| --- | --- |
| `AGENTS.md` | 2,819 |
| `architecture-design.agent.md` — largest agent prompt | 2,708 |
| `architecture-documentation.md` | 3,168 |
| `change-classification.md` | 2,664 |
| `system-contracts.md` | 2,364 |
| `technical-documentation.md` | 2,106 |
| **Worst case** | **15,829** |

The measurement is recorded beside the ceiling on purpose. 20,000 leaves roughly a quarter of headroom: it
does not fire today, and it fires well before the payload becomes a problem rather than after. Whoever
next wants to raise it then has to argue against a datum rather than against a preference.

**A token is counted as one byte divided by four, after line endings are normalized to LF.** Both halves
of that are load-bearing. The division is crude, but it is crude *consistently*, and this repository has
no tokenizer dependency and must not acquire one to run a budget gate — the ceiling was chosen against
this method and means nothing measured any other way. The normalization matters because the working tree
is CRLF while git stores LF, so a raw byte count differs between a fresh clone and a Windows checkout of
the same commit; a gate that passes in CI and fails locally is flaky, and a flaky gate gets disabled. The
table above is the demonstration: `AGENTS.md` counts 2,819 normalized and 2,880 unnormalized on a Windows
checkout, and the 61-token gap is exactly its 244 line endings.

The budget is not currently under pressure, and stating so matters: the reason to write tersely here is
**not** to save tokens. It is that a rule buried in a long passage is a rule an agent may not act on.
Conciseness is about attention, which no clause can measure.

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

## What a Judging Prompt Must Demand

`Actionability` above governs the instruction an agent is *given*. This section governs the opposite
direction: what a prompt obliges an agent to *emit* when the work it has been given is judgement. The two
are easy to conflate, and keeping them apart matters because a perfectly actionable instruction can still
be answered with a bare verdict — which is precisely how a judging agent fails here.

**Evidence before verdict.** A prompt that asks an agent to judge must require the basis first and the
conclusion second, and its report template must order its *body sections* that way — the `**Result**`
metadata field required by `PROCESS-04` still comes first, so a caller can route on the outcome without
parsing the report. An agent that writes out its reasoning *after* the section stating its conclusion
spends that reasoning rationalizing toward it, so the ordering is not presentation: it is what stops the
argument from being retrofitted to a conclusion already argued for. A one-word routing field commits no
argument; a body section does.

**The report template is a closed set.** A prompt that asks an agent to judge must make its report
template the only body sections that agent may emit, and must say so in the prompt rather than leaving it
implied by the template's existence. Anything the agent wants to raise beyond them goes in a non-blocking
advisory section, which by definition cannot carry a pass verdict and cannot contribute to a success
result. This is independent of `Evidence before verdict` above: that rule governs the *order* of the body
sections, this one governs the *set*, and an invented section placed dutifully after every templated one
satisfies the ordering rule completely. The reason the set must be closed is that a section an agent adds
at judging time is a section whose criteria the agent also authored at judging time. A templated section's
criteria were fixed in advance by the prompt author, so a reader can audit the verdict against them; a
self-authored section grades the change against a rule that exists nowhere the caller can read, and a pass
on it asserts conformance to something unwritten. The advisory section is the counterweight that makes the
closure survivable rather than an afterthought — an agent that has found something real that no templated
section covers needs somewhere to put it, or closure only pressures it into forcing the finding into a
templated section where it does not belong.

**The universally-quantified negative is the specific trap.** "Names no sub-agent", "contains no hedging",
"all bad examples were excluded" — these are the claims an agent asserts from a gist of a file without
opening it to check, and reviews accepted here have carried exactly that shape while being false. A prompt
that asks for such a claim must demand the check that establishes it: for a negative claim over a file,
quote the nearest surviving counter-candidate and say why it does not count. Absence shown by quoting the
closest thing to a violation can be audited by the caller; absence asserted cannot.

**A judgement with no demonstrated basis is not a finding**, and a prompt should say so, so that "I could
not establish this" is an available answer rather than a failure to report something. The counterweight is
part of the rule: demanding evidence must not push an agent into manufacturing findings to satisfy the
demand, so an ambiguity it cannot resolve is reported as advisory, never as failure.

## Consequences

A developer or agent editing anything under `.github/agents/` or `.github/standards/` must:

- **Place the rule at exactly one level**, and link rather than restate it from anywhere else. Duplication
  is the failure `PROCESS-I2` targets; a rule stated twice drifts, and the copy read first wins.
- **Add the reason when the rule is a boundary, a stop condition, or counter-intuitive** — and leave it
  out when the rule is mechanical.
- **Demand the basis wherever the prompt asks the agent to judge**, per "What a Judging Prompt Must
  Demand" above. A verdict the prompt never obliged the agent to derive is the defect that passes every
  mechanical gate and reaches the reader intact.
- **Check the result values.** Adding a new outcome to an agent obliges every caller to handle it. This
  is the defect class that has survived manual review here before.
- **Not trim prose that carries a rejected alternative.** A short prompt missing the *why* is the more
  expensive failure, because the reasoning cannot be recovered from anywhere else.

Editing a prompt also invalidates any behavioral verification recorded against that agent. Structural
clauses are re-checked by script; behavioral claims must be re-established by inspection or a sandbox run,
because the evidence described the prose that just changed.

## When a Prohibition Earns Its Place

Prohibitions and anti-pattern examples cost the same tokens as anything else, but they earn their place by
a different test. A prohibition is worth its place only when it targets a **specific failure the agent is
actually likely to commit** — a behavior the model defaults to, or one it has demonstrably chosen under
pressure in this repository.

The applicable test: ask whether removing the prohibition would cause the failure to recur within a small
number of invocations. If yes, it pays for itself. If it warns against something an agent would not do
unprompted, it is consuming attention that could go toward a rule the agent might break.

A worked example clarifies the boundary. Stating that a parent document must not summarize its children
earns its place, because summarizing is the default behavior of a model given a hierarchy — remove the
prohibition and it recurs immediately. By contrast, listing diagnostic "symptoms" of a degrading process
does not earn its place, because it names no specific act an agent could commit or avoid; it teaches
recognition to a reader who cannot act on it inside a prompt.

This is adjacent to "When a Why Earns Its Place" above and must not overlap it. A *why* earns its place
by giving an agent something to reason from in a novel case. A *prohibition* earns its place by
intercepting a specific default the agent would otherwise follow. One explains; the other blocks.

## Checklists for New Payload Files

These are the obligations that apply when creating a new agent, standard, or skill. They are recorded
here because `prompt-authoring.md` is the single owner of "how payload files are authored" at level 3,
and because each obligation flows from the structural contract in [process.md](../process.md).

### New Agent

- Front matter: `name` (matching filename), `description`, `user-invocable` or
  `disable-model-invocation`
- Named for what it owns or does — `{artifact}-{verb}` for an artifact owner, bare verb for a general
  actor. Check the name against modes, other agents, scripts, and host built-in agent names
- A stated purpose narrow enough that "when not to use this" is obvious
- Explicit standards to load, by name
- A report template with `**Result**` as the first metadata field
- When the agent judges: a report template ordering basis before verdict, demanding the check behind any
  universal negative, and declared in the prompt as the closed set of body sections with one non-blocking
  advisory section for anything else — see
  [What a Judging Prompt Must Demand](#what-a-judging-prompt-must-demand)
- Listed in `AGENTS.md` under **Agent Delegation Guidelines**
- `AGENTS.md` is kept in sync with `.github/template/AGENTS.pristine.md` — any edit to it must be made
  in both copies, because `lint.ps1` enforces the match and `PROCESS-08` contracts it

### New Standard

- Front matter: `name`, `description`, optional `globs`
- Every MANDATORY rule states **why**
- Quality gates at the end as a checklist
- Listed in the **Standards Application** matrix in `AGENTS.md` (same pristine-copy obligation applies)

### New Skill

- Front matter: `name`, `description` stating *when* to load it
- Describes a procedure, not a parameter list
- Says what to do when its subject is absent
- The agent prompts that carried the procedure now reference the skill instead
- Listed in the **Skills** section of `AGENTS.md` (same pristine-copy obligation applies)
