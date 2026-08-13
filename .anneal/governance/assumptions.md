---
description: Curated, descriptive truths the design rests on, disprovable but not chosen (see docs/user-guide/).
maintenance: Only edited by the repository author.
---

# Assumptions

- **A focused agent is a reliable judge.** An agent given the specific facts and a single clear
  question answers it reliably. Reliability degrades with breadth and vagueness far more than with
  difficulty.
- **Judging and doing have different incentives.** An agent asked to complete work is under pressure
  to call the work done; an agent asked only to judge is not.
- **Correlated error is the residual risk.** Separate invocations of the same model can share a blind
  spot; independence of incentive is not independence of judgement.
- **An agent that must justify its answer is more reliable than one that merely states it.**
- **Structural properties of a prompt predict how an agent behaves.** Whether references resolve,
  every result value is handled, and the context budget holds correlates with reliable behavior.
- **Where a response schema appears in a conversation changes how reliably it is followed** — a
  schema given after the reasoning is done is followed more closely than one given at the outset.
- **A model's context should carry only what bears on the decision in front of it, not the full body
  of rules that could apply somewhere.** Precision of relevance is a reliability property, not merely a
  cost saving: a correct rule the model is never shown, or is shown buried among irrelevant ones, might
  as well not exist for that decision.
- **A lightweight repository map can stay accurate enough to guide work.** A concise, human/model-readable
  representation of the repository's structure and intent can be kept current enough, at acceptable
  cost, to support reliable change without requiring full code re-derivation on every task.
- **Classifying freeform prose is a model's job, not deterministic code's.** Pattern-matching over a
  user's own wording to infer intent, scope, or risk is unreliable in a way regex cannot detect in
  itself; the same judgment asked as a narrow oracle question is answered reliably instead. A
  mechanical check earns its place only when the property it tests is itself structural — a path, a
  count, a diff's syntactic shape — not when it is standing in for understanding what someone meant.
- **A model erodes hedges and confidence markers in text it reads or repeats.** Qualifying language
  like "probably," "not yet verified," or "deliberately imprecise" tends to flatten or vanish as text
  is read, summarized, or passed along — a downstream reader sees the claim but not the doubt that
  qualified it, unless the uncertainty is carried by the response's own structure rather than by its
  wording alone.
