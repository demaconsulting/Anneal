---
level: system
covers:
  - src/DemaConsulting.Anneal.Toolkit/**
  - test/DemaConsulting.Anneal.Toolkit.Tests/**
---

[← Architecture Overview](./overview.md)

# Toolkit

The Toolkit is a .NET tool, invoked as `dotnet anneal <action>`, that hosts operations an agent cannot
perform reliably by reading a prompt: deterministic checks over the repository, and model-backed
judgement whose prompt, schema and context are controlled programmatically. It exists as a separate
system because it is the only part of Anneal that is **executed** rather than **read** — every other
system delivers text that something else interprets. If it were rewritten, a consumer would notice
through the action names, their exit codes and their output shape, and through nothing else.

Its reason for existing is narrower than "scripts, but in C#". A model call whose response schema is
supplied at the end of the conversation rather than the beginning is measurably more reliable, and
that ordering cannot be expressed in an agent prompt at all: by the time the answer is wanted, an
instruction given at the start is far behind in the context window. Controlling when the schema is
presented requires owning the conversation, which requires code.

## Contract

The clauses here are the tool's own identity — how it is invoked, how it reports its version, and how
it handles misuse and discovery — owned by no single operation and no single machinery layer. The
promises every operation obeys live in the [Runtime](./toolkit/runtime.md); the promises only a
model-backed operation touches live in the [Model Seam](./toolkit/model-seam.md); each operation's own
promise lives at its own node.

### Provides

- **TOOLKIT-01** — The tool is invoked as `dotnet anneal <action>` with the action named first, and an
  unrecognized action lists the actions that exist, so a caller discovers the surface without reading
  the source. The exit code it leaves is the caller-error code of `TOOLKIT-10`.
  *Verified by:* `ToolkitContractTests.UnknownActionListsAvailableActions`

- **TOOLKIT-09** — The tool reports the Anneal version it was built from, so an installed payload can
  be identified by version rather than inferred from its contents.
  *Verified by:* `ToolkitContractTests.ToolReportsPayloadVersion`

- **TOOLKIT-10** — An invocation the tool cannot act on — no action named, an action that does not
  exist, or arguments the named action cannot use — exits with the caller-error code `2`, whatever
  category the named action declares, and reports no outcome, because none was reached. That code is
  distinct from the codes carried by a gated failure and by a refusal, so a caller that scripts the
  wrong argument form cannot read its own mistake as a check that ran and passed.
  *Verified by:* `ToolkitContractTests.UsageErrorExitsAsCallerErrorWhateverTheCategory`

- **TOOLKIT-12** — `dotnet anneal help`, given no further argument, lists every shipped action with its
  one-line summary and exits with the success code `0`. The action list a caller could until now reach
  only by naming no action or an unknown one is reachable deliberately, so the surface is discoverable
  without provoking an error and without reading the source.
  *Verified by:* `ToolkitContractTests.HelpListsEveryActionAndSucceeds`

- **TOOLKIT-13** — `dotnet anneal help <action>` prints the named action's detailed usage — how it is
  invoked and what arguments it takes — and exits `0`. A `<action>` that is not a shipped action is a
  usage error under `TOOLKIT-10`, reported with the same list of existing actions an unknown action
  already produces, so `help` never fabricates guidance for a surface that does not exist.
  *Verified by:* `ToolkitContractTests.HelpForActionPrintsItsUsageAndRejectsUnknown`

### Requires

- **[Process](./process.md)** — the agents that invoke operations, and the standards whose rules the
  deterministic checks and probes are written against.
- **[Template](./template.md)** — the tool manifest that makes the tool restorable in a target
  repository.
- **GitHub Copilot SDK** — model access under the ambient Copilot account of the calling session, with
  no token supplied by the Toolkit.
- **.NET SDK** — the runtime the tool is restored into and executed by.

### Invariants

- **TOOLKIT-I4** — The detailed usage an action presents through `help <action>` and the usage it
  presents when invoked with arguments it cannot use (`TOOLKIT-10`) are one and the same text, drawn
  from a single declared source, so the two renderings cannot state the invocation differently or drift
  apart as the action changes.
  *Verified by:* `ToolkitContractTests.HelpAndUsageErrorShareOneUsageSource`

## Composition

The system decomposes into the operations callers invoke and the two machinery layers they are built
from.

- **Operations** are the contract surface: one per action, each declaring its category and, where
  applicable, its capability role. This is the only layer the contract describes at the operation
  level, which is deliberate — an operation invoked by a downstream agent *is* a promise, so adding one
  is a contract change and is meant to feel like one. [VerifyEvidence](./toolkit/verify-evidence.md) and
  [ContractCheck](./toolkit/contract-check.md) are deterministic and built on the Runtime alone;
  [ProbeRuleOwner](./toolkit/probe-rule-owner.md) is model-backed and adds the Model Seam.
- **[Runtime](./toolkit/runtime.md)** is the shared execution every operation is built from: category
  and gating, the outcome-and-exit-code model, the structured invocation record, the finding an
  operation returns beside its outcome, and the asynchronous boundary carrying the caller's
  cancellation. Everything above it is dispatch and argument parsing; a gate must not depend on a
  network, which is why a deterministic operation reaches for nothing below this layer.
- **[Model Seam](./toolkit/model-seam.md)** is the machinery only a model-backed operation touches: a
  provider resolving a capability role to an endpoint, the *run* and *probe* verbs over it, and the
  prompt assembly that owns the schema so a caller cannot place it early. If operations built their own
  prompts, the schema-last ordering that justifies the system would be re-implemented per operation and
  decay unevenly.

This tree records only two things about an action, and both stay bounded as the set grows: the
**inventory** — each action named once with its one-line role — and the **participation rules** every
action obeys whatever it does, which are contract clauses held in the [Runtime](./toolkit/runtime.md)
and at this root rather than per-action prose. An action's *usage* — how it is invoked and what
arguments it takes — is per-action detail served by `help <action>` from the operation itself, never
written into this tree. That routing is load-bearing, not a convenience: it is what holds this
document's growth to one contract clause per new action rather than a clause plus a paragraph of usage
each, and it makes the operation the single owner of its usage by construction.

## Decisions

**A locally built Toolkit is acquired the way a published one is** — the tool manifest and the restore
that reads it are the same whether the package came from a feed or from this repository's own build, so
the invocation `TOOLKIT-01` fixes is the one callers use here exactly as downstream callers use it. The
rejected alternative was running the project in place as a documented local override: it makes every
prompt and document that names the invocation false where it is written, it reaches the code without
going through packaging, so a manifest or packaging fault would first appear at somebody else's install,
and an override introduced as temporary outlives the condition that produced it.

**Operations are listed individually in the contract** — the alternative was contracting the shape of an
operation once and leaving the set open. That was rejected because the set is what consumers actually
depend on: an agent that invokes an action by name is relying on that name. Listing them costs a Tier 1
change per operation and buys one clause and one boundary test per operation, which the existing
contract check already enforces. The cost is accepted for the visibility.

**Judgement stays in the model; code owns control flow** — sequencing, gating and evidence handling move
into the Toolkit because they are deterministic and were unreliable when expressed as prose for an agent
to follow. The decisions themselves do not move, because a unit test can prove that a step ran and
cannot prove that a verdict was right, and because a rule compiled into a released tool is corrected
through build, test, publish and restore where a prompt is corrected in one edit. Prompts, standards and
contracts stay data the Toolkit composes. The rejected alternative was a process engine that owns both
control flow and judgement — rejected because encoding judgement makes correcting it expensive on every
subsequent change, which is the cost *What must not be reintroduced* in [overview.md](./overview.md)
refuses.

**A misuse is not an outcome** — an operation that cannot use its arguments never ran, so it has nothing
to report and the gating rule has nothing to weigh; `TOOLKIT-10` therefore routes it to the same
caller-error code an unknown action already produced, independently of category. The rejected
alternative was leaving a caller mistake to be reported as an ordinary failure, which is what the code
did: a research operation given the wrong argument form exits zero, and an unattended agent reads that
as a check that ran and found nothing. Fail-open on the caller's error is the shape this repository
treats as worse than no check, so the outcome model has to be able to say "no answer was attempted"
separately from "the answer is no" — which is the same distinction `TOOLKIT-06` draws for refusal, at
the other end of the invocation.

**Discovery is a first-class path, not only an error path** — `help` and `help <action>` (`TOOLKIT-12`,
`TOOLKIT-13`) let a caller learn the surface deliberately, where before the action list was reachable
only by naming no action or an unknown one. Bare `anneal` stays the usage error exiting `2` that
`TOOLKIT-10` fixes — not an alias for `help`, which would exit `0` and hide a script's omission of its
action. Discovery is the deliberate path; the usage error is the guard rail; they are not merged.

**Usage is declared once and rendered twice** — an action states how it is invoked in a single place,
and both `help <action>` and that action's own usage-error message render from it (`TOOLKIT-I4`). The
rejected alternative is what the code did: each operation hand-wrote its own usage line inside `Execute`,
and `help` would have hand-written a second copy — two independently authored strings asserting the same
fact, which drift silently, exactly as a rule stated in two files drifts. That is the process-level
property `CONSTRAINTS.md` holds — every rule has one owning file — appearing here in code rather than
prose; one source removes the drift rather than policing it. The single source is a new **required**
member on the public `IOperation`. Requiring it rather than giving it a default that falls back to
`Summary` is a **breaking change** to any external operation implementer — `system-contracts.md` defines
the term, and here it is a compile-time break — admitted for two reasons: the tool has never been
released, so the break costs nothing today, and a default silently falling back to the one-line purpose
summary would let "detailed usage" be skipped without anyone noticing, reintroducing the very gap this
change closes. The clause names no member, because how the single source is declared is interior; only
that the two renderings share one source is the promise.

**`help` is a dispatcher verb, not an operation** — `AnnealTool` handles it before dispatch rather than
shipping it as an `IOperation`, because it must list the whole operation set the dispatcher holds and an
operation is never given, and it must exit `0` and never gate — both guaranteed by construction when it
stays outside the outcome-and-category machinery rather than by a category choice a later edit could get
wrong. It follows that `help` is absent from its own action list, and that `help help` names an action
that does not exist, so it is the usage error `TOOLKIT-13` describes rather than a self-description.
`--help` and `-h` are deliberately absent for the same reason discovery is scoped tightly: the two verbs
satisfy the goal completely, and option flags were declined because surface no caller asked for becomes
contract the moment it ships and flag parsing sits awkwardly against a tool whose actions are positional
and whose bare invocation is deliberately a usage error — `anneal --help` would have to decide whether
`--help` is an action, and every answer complicates `TOOLKIT-10`. Should a caller ever need any of these,
adding them is an additive Tier 1 change.

**The reference implementation is studied, not copied or depended on** — an existing internal codebase
solves the same problem at much larger scale, including retrieval, memory and a process engine. Its
seams informed this design and none of its code is taken wholesale, because importing the parts that are
not needed is how a small tool acquires a large one's maintenance cost.

**The unreliability this system targets was measured, not assumed** — one pass over the agent-report
corpus produced the figures the design rests on: `tier-check` returned FAILED in 8 of 16 runs, and at
least two of the remaining SUCCEEDED verdicts were wrong, found only by hand; `apply` returned SUCCEEDED
16 of 16 while its own verifier failed half of what it saw; across 65 reports the header fields were not
uniform (`Result` 64, `Tier` 57, `Repairs Used` 16, `Residual` 14, one `Result` that did not parse), and
four worker names appeared that match no agent in `.github/agents/`. The asymmetry is the point, and is
why [MIGRATION.md](../../MIGRATION.md) stage S3 re-checks verdicts rather than counting them: a false
FAILED is loud and gets fixed, a false SUCCEEDED ships. The non-uniformity is why `TOOLKIT-08` records
invocations structurally — the figures above were themselves recovered by regex-scraping prose, which
had already produced one plausible wrong answer. They are a baseline to re-measure against, not a
target.

## Details

- [Runtime](./toolkit/runtime.md) — the shared execution every operation is built from: category and
  gating, outcomes and exit codes, the invocation record, findings-as-data, and cancellation
- [Model Seam](./toolkit/model-seam.md) — the machinery only a model-backed operation touches: role
  resolution, the run and probe verbs, refusal, offline failure, and transcription
- [VerifyEvidence](./toolkit/verify-evidence.md) — how `verify-evidence` checks each cited locator's
  quoted text against the file and line it names, reaching no verdict on the report's conclusion
- [ProbeRuleOwner](./toolkit/probe-rule-owner.md) — how `probe-rule-owner` names the single file that
  owns a rule, and why it refuses when ownership is split or absent
- [ContractCheck](./toolkit/contract-check.md) — how the `check-contracts` action reads a repository's
  architecture tree, and what each way it can reject one means
