# Assumptions

Curated, descriptive truths the design rests on, disprovable but not chosen. See `docs/user-guide/` for detail.

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
