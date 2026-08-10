# Assumptions

Curated, descriptive truths about the world the design rests on — accepted as fact, and free for
Anneal to lean on or disregard, but not chosen the way a Tenet is chosen. If one of these stops
holding, the architecture resting on it is the wrong shape, so they are recorded here rather than
left implicit in agent prompts or standards. A disproved assumption is a re-cut trigger, not a bug.

Owner-authored, owner-approved to change, same gate as `tenets.md` and `vision.md`. Anneal may
propose a revision but never edits this file unilaterally.

- **A focused agent is a reliable judge.** An agent given the specific facts and a single clear
  question answers it reliably. Reliability degrades with breadth and vagueness far more than with
  difficulty. This is why judgement is split into separate single-question invocations instead of
  being asked as one part of a larger job.
- **Judging and doing have different incentives.** An agent asked to complete work is under pressure
  to call the work done. An agent asked only to judge is not. Classification and verification are
  therefore never performed by the agent that did the work.
- **Correlated error is the residual risk.** Separate invocations of the same model can share a blind
  spot; independence of incentive is not independence of judgement. A judging agent is therefore
  given first-hand facts rather than the working agent's summary of them.
- **An agent that must justify its answer is more reliable than one that merely states it.**
  Separating incentives removes the motive to approve but does not oblige a judge to derive its
  verdict. If reasoning-required agents proved no more accurate than agents asked only for a
  conclusion, the judging layer would be ceremony.
- **The prompt files are the reliability mechanism.** Reliability follows from the quality of the
  facts and the clarity of the question, so a defect in an agent prompt degrades every downstream
  agent's facts. Prompt changes are the highest-risk changes in this repository.
- **Products adopting this process are .NET and C#.** The shipped layout defaults to `*.cs` sources,
  xUnit attributes and TRX results, and the template's scripts assume a solution. The process itself
  is language-neutral; the repository it hands you is not. Adoption for another ecosystem would mean
  the template is the wrong shape, not a defect to patch. The
  [Toolkit](../architecture/toolkit.md) hardens this into a dependency: it ships as a .NET tool, so
  a repository outside that ecosystem can read the process but not run its operations.
- **Structural properties of a prompt predict how an agent behaves.** Checking references resolve,
  every result value is handled, and the context budget holds is worth doing because those
  properties correlate with reliable behavior. If they don't, the mechanical contract is theater, and
  verification would have to move wholesale to inspection and sandbox runs.
- **Where a response schema appears in a conversation changes how reliably it is followed** — a
  schema given after the reasoning is done is followed more closely than one given at the outset.
  This is the belief the [Toolkit](../architecture/toolkit.md) exists to exploit; see there for why.
- **A described schema is enough without constrained decoding.** The Copilot session API has no
  response-format facility, so a typed answer rests on a schema described in the prompt and a retry
  on parse failure. If failures survive the retry budget often enough to matter — measured at stage
  S1b of [active-plan.md](../work/active-plan.md) — typed probes need a provider that enforces the shape on
  the wire.
- **The build now requires network access**, to fetch the Copilot CLI the SDK depends on. Build-time
  only — no enforcement operation's runtime determinism is affected. See
  [Toolkit](../architecture/toolkit.md) for the mechanism.
