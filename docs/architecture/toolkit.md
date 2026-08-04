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

### Provides

- **TOOLKIT-01** — The tool is invoked as `dotnet anneal <action>` with the action named first, and an
  unrecognized action lists the actions that exist, so a caller discovers the surface without reading
  the source. The exit code it leaves is the caller-error code of `TOOLKIT-10`.
  *Verified by:* `ToolkitContractTests.UnknownActionListsAvailableActions`

- **TOOLKIT-02** — Every operation declares exactly one category — enforcement, research, advisory or
  authoring — and for an operation that ran, the category alone determines whether the non-zero exit
  of its outcome gates a build. Only enforcement operations gate.
  *Verified by:* `ToolkitContractTests.OnlyEnforcementOperationsGate`

- **TOOLKIT-03** — `verify-evidence` reports, for each evidence locator cited in an agent report,
  whether the quoted text is present at the file and line named. It reaches no verdict about the
  report's conclusion and consults no model.
  *Verified by:* `ToolkitContractTests.EvidenceLocatorsAreCheckedAgainstSource`

- **TOOLKIT-04** — `probe-rule-owner` names the single file that owns a given rule, or refuses when
  the rule is stated in more than one place or in none.
  *Verified by:* `ToolkitContractTests.RuleOwnerProbeNamesOneFileOrRefuses`

- **TOOLKIT-05** — Every operation that consults a model declares the capability role it requires, and
  roles resolve to concrete models through repository configuration rather than through the operation.
  *Verified by:* `ToolkitContractTests.OperationRolesResolveThroughConfiguration`

- **TOOLKIT-06** — Refusal is reported as an outcome distinct from both success and failure, so a
  caller can tell "the question could not be answered on the available evidence" from "the answer is
  no".
  *Verified by:* `ToolkitContractTests.RefusalIsDistinctFromFailure`

- **TOOLKIT-07** — A model-backed operation that cannot reach a model fails with a message naming the
  cause. It never falls back to a deterministic approximation, and never reports success on a
  judgement it did not obtain.
  *Verified by:* `ToolkitContractTests.UnreachableModelFailsLoudly`

- **TOOLKIT-08** — Every invocation appends a structured record of the operation, its inputs, its
  outcome and any model usage, in a form a later query can aggregate without parsing prose. The record
  identifies the outcome so that its meaning is fixed as new outcomes are added, so records aggregated
  across versions — which is what aggregation means, since runs span releases — cannot silently change
  meaning when the set of possible outcomes grows.
  *Verified by:* `ToolkitContractTests.InvocationsAppendStructuredRecords`

- **TOOLKIT-09** — The tool reports the Anneal version it was built from, so an installed payload can
  be identified by version rather than inferred from its contents.
  *Verified by:* `ToolkitContractTests.ToolReportsPayloadVersion`

- **TOOLKIT-10** — An invocation the tool cannot act on — no action named, an action that does not
  exist, or arguments the named action cannot use — exits with the caller-error code `2`, whatever
  category the named action declares, and reports no outcome, because none was reached. That code is
  distinct from the codes carried by a gated failure and by a refusal, so a caller that scripts the
  wrong argument form cannot read its own mistake as a check that ran and passed.
  *Verified by:* `ToolkitContractTests.UsageErrorExitsAsCallerErrorWhateverTheCategory`

- **TOOLKIT-11** — Every model interaction records a transcript of it — the prompt sent, the reply
  received, the model consulted and the token usage — for every interaction rather than only for those
  that failed or refused, and with no opt-in that could leave it off. A model asked the same question
  later may answer differently, so this is the only evidence in the system that cannot be reconstructed
  by re-running, and it is captured at the time or lost.
  *Verified by:* `ToolkitContractTests.ModelInteractionsAreTranscribed`

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

- **TOOLKIT-14** — An operation reports what it found as data, carried alongside its outcome and never
  in place of it, so a caller — including another operation — consumes the finding without parsing the
  text the operation renders for a person. An operation with nothing structured to report carries no
  data, and that absence is an answer rather than an invented payload or a failure.
  *Verified by:* `ToolkitContractTests.OperationFindingsReachCallersAsData`

- **TOOLKIT-15** — A caller supplies a cancellation signal with an invocation, and cancelling it stops
  the invocation rather than letting it run to completion, so a host that must stay responsive — an
  interactive loop, or an agent abandoning a question — can withdraw a request that consults a model for
  tens of seconds.
  *Verified by:* `ToolkitContractTests.CancellingAnInvocationStopsIt`

- **TOOLKIT-16** — An invocation interrupted at the terminal stops where it is rather than being killed,
  and exits with the interrupt code `130`, which is distinct from every code an outcome maps to and from
  the caller-error code of `TOOLKIT-10`, because an interrupted invocation reached no outcome to map. A
  caller reading exit codes can therefore tell a run somebody stopped from any run that finished.
  *Verified by:* `ToolkitContractTests.InterruptedInvocationStopsAndExitsOutsideTheOutcomeCodes`

### Requires

- **[Process](./process.md)** — the agents that invoke operations, and the standards whose rules the
  deterministic checks and probes are written against.
- **[Template](./template.md)** — the tool manifest and the role configuration file that make the tool
  restorable and configurable in a target repository.
- **GitHub Copilot SDK** — model access under the ambient Copilot account of the calling session, with
  no token supplied by the Toolkit.
- **.NET SDK** — the runtime the tool is restored into and executed by.

### Invariants

- **TOOLKIT-I1** — A model consulted by any operation is granted read-only repository tools. No
  operation grants a tool that executes a command or writes a file, and the granted tool set is always
  an explicit allowlist rather than an absent one.
  *Verified by:* `ToolkitContractTests.ModelToolGrantsAreReadOnlyAndExplicit`

- **TOOLKIT-I2** — A probe result reaches a caller only as a fully decoded typed value. A response
  that cannot be decoded within the retry budget fails the operation; no partially populated result is
  returned.
  *Verified by:* `ToolkitContractTests.UndecodableProbeResultFailsTheOperation`

- **TOOLKIT-I3** — An enforcement operation given identical repository inputs reaches an identical
  verdict, so a gating check cannot change answer on unchanged input.
  *Verified by:* `TODO.EnforcementVerdictsAreStableOnUnchangedInput`

- **TOOLKIT-I4** — The detailed usage an action presents through `help <action>` and the usage it
  presents when invoked with arguments it cannot use (`TOOLKIT-10`) are one and the same text, drawn
  from a single declared source, so the two renderings cannot state the invocation differently or drift
  apart as the action changes.
  *Verified by:* `ToolkitContractTests.HelpAndUsageErrorShareOneUsageSource`

- **TOOLKIT-I5** — The caller's cancellation signal is the only one in effect for the whole of an
  invocation: no operation substitutes a signal of its own at any point between the invocation and the
  model it consults. A cancellation therefore takes effect while a model call is still waiting for its
  reply, rather than only after the reply arrives.
  *Verified by:* `ToolkitContractTests.CancellationTakesEffectWhileAModelCallIsInFlight`

## Composition

Three parts, cut where the reasoning differs.

**Operations** are the contract surface: one per action, each declaring its category and, where
applicable, its capability role. Everything above is dispatch and argument parsing; everything below is
shared machinery. This is the only layer the contract describes, which is deliberate — an operation
invoked by a downstream agent *is* a promise, so adding one is a contract change and is meant to feel
like one.

This tree therefore records only two things about actions, and both are bounded as the set grows: the
**inventory** — each action named with its one-line role, one clause per action — and the
**participation rules** every action obeys whatever it does, which are contract clauses rather than
per-action prose: category decides gating (`TOOLKIT-02`), a misuse maps to the caller-error code
(`TOOLKIT-10`), each action declares its detailed usage exactly once (`TOOLKIT-I4`), each runs under the
caller's cancellation signal and no other (`TOOLKIT-15`, `TOOLKIT-I5`), and each reports what it found as
data beside its outcome (`TOOLKIT-14`). An action's
*usage* — how it is invoked and what arguments it takes — is per-action detail served by `help <action>`
from the operation itself, never written into this tree. That routing is load-bearing, not a
convenience: it is what holds this document's growth to one contract clause per new action rather than a
clause plus a paragraph of usage each, and it makes the operation the single owner of its usage by
construction.

**Deterministic checks** read the repository and reach verdicts without a model. `verify-evidence` is
one, and `check-contracts.ps1` is expected to become another. They are kept apart from model-backed
work because they are the operations that may gate, and a gate must not depend on a network.

**The model seam** is a provider that resolves a capability role to an endpoint, and one object over it
offering two verbs: *run*, whose request and reply both join a conversation, and *probe*, a one-shot
question whose typed answer joins nothing. The seam exists so that no operation knows which provider
answered, and so the two interaction shapes cannot drift apart in how they decode or retry. If the seam
moved — if operations built their own prompts — the schema-last ordering that justifies the system
would be re-implemented per operation and would decay unevenly.

The prompt assembly deliberately owns more than the caller supplies: an operation provides its question
and its authoritative context, while the framework contributes the response schema, derived by
reflection over the typed result. A caller cannot forget the schema, and cannot place it early.

## Decisions

**Operations are listed individually in the contract** — the alternative was contracting the shape of an
operation once and leaving the set open. That was rejected because the set is what consumers actually
depend on: an agent that invokes an action by name is relying on that name. Listing them costs a Tier 1
change per operation and buys one clause and one boundary test per operation, which the existing
contract check already enforces. The cost is accepted for the visibility.

**Judgement stays in the model; code owns control flow** — sequencing, gating and evidence handling move
into the Toolkit because they are deterministic and were unreliable when expressed as prose for an agent
to follow. The decisions themselves do not move, because a unit test can prove that a step ran and
cannot prove that a verdict was right. The rejected alternative was a process engine that owns both,
which is the shape of the predecessor this repository exists to escape — see *What must not be
reintroduced* in [overview.md](./overview.md).

**Model configuration is data, not code** — roles appear in this contract; the models behind them do
not. A repository changes the models behind its roles by editing a configuration file the Template
ships, without a Toolkit release. The rejected alternative, model names in the contract, would make
every model substitution a contract change.

**The schema is a prompt-level hint, not a transport guarantee** — the Copilot session API offers no
constrained-decode facility, so a typed response rests on a described schema, tolerant extraction of the
object body, and a retry that shows the model its own parse error. This is weaker than a provider that
enforces a response format, and the difference is recorded because it is invisible in the type
signature: `Probe<T>` looks equally reliable either way. Parse-failure rate is therefore something to
measure rather than assume.

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

**A finding is data; the text is a rendering of it** — an operation returns what it concluded as a value
carried beside its outcome — typed but not type-parameterized: the value is a domain type while the slot
holding it is not, so which type a given operation puts there is the caller's knowledge rather than the
compiler's — and separately writes the human text it writes today (`TOOLKIT-14`).
The forcing case is composition: the verdict auditor `MIGRATION.md` schedules re-checks verdicts another
operation reported, and if the only channel out of an operation is a `TextWriter`, composing means
re-parsing prose — the exact mistake stage S2 deletes when it retires `agent-metrics.ps1` for scraping
reports with regular expressions. The evidence that the channel is wrong rather than merely narrow is
that `probe-rule-owner` already computes a typed answer and flattens it to lines at the boundary: the
structure exists and is being thrown away one layer before the caller. Two alternatives were rejected. A
generic operation interface parameterized by its result type was rejected because the dispatcher — which
is the surface consumers actually hold — must keep a heterogeneous set of actions, so it can only hold
the non-generic form, and the type parameter buys nothing at the one boundary that matters while
splitting the public surface into two shapes that must be kept aligned and forcing every operation with
nothing structured to say to either pick the other shape or invent a payload. Keeping the writer as the
only channel and treating separation alone as the answer was rejected because it supplies no data at all;
what survives from it is adopted as a rule rather than as the mechanism — the writer is a rendering
channel and never the data channel, which is why the seven observable invocations print exactly what they
printed before and `help` is untouched. The outcome vocabulary is deliberately *not* folded into the
payload: refusal stays an outcome distinct from success and failure (`TOOLKIT-06`) and exit codes keep
mapping from the outcome, because a refusal is a fact about the invocation and not a value the operation
found. **This is not `TOOLKIT-08`.** That clause promises a persisted, aggregatable record of every
invocation, queryable across releases; this is an in-process return value that outlives nothing and is
written nowhere. They resemble each other only in both being structured, and a later reader must not read
`TOOLKIT-14` as `TOOLKIT-08` partly delivered — it remains entirely unbuilt, as does `TOOLKIT-11`.
Streaming a finding as it is produced is a separate, later question; nothing here forecloses it, and it is
deliberately unpromised. A middle option between the two rejected alternatives — a marker finding type,
leaving the dispatcher heterogeneous while making the slot a domain type rather than an untyped one — was
not evaluated, and is left as an open question for the user to rule on before the large batch of new
operations lands, because at fifteen operations an untyped slot answers the same empty answer both to
"nothing was found" and to "you asked for the wrong type".

**The operation boundary is asynchronous, and the caller owns cancellation** — an invocation is
asynchronous and takes the caller's cancellation signal, which reaches the model seam intact
(`TOOLKIT-15`, `TOOLKIT-I5`). This reverses a rationale recorded in the code itself: that a process
existing to run exactly one operation makes a synchronous boundary free, and keeps `IOperation` clear of a
concern only one implementation has. That reasoning was true of the *process* and false of the
*interface*. `IOperation` is public precisely so the tool can be hosted by something that is not this
process, and the stated goal of an interactive loop is such a host: a synchronous boundary blocks it for
the tens of seconds a reasoning model takes and offers no way to interrupt. The concern is also no longer
one implementation's — the seam below is already asynchronous and every model-backed operation crosses
it, so a synchronous surface above it means sync-over-async at the boundary, and blocking with a
hardcoded absent cancellation signal is what the code does twice today. Relocating that block rather than
removing it would satisfy the letter of the change and none of its purpose, which is why `TOOLKIT-I5`
states the property as an observable one — a cancellation lands while a model call is still waiting —
rather than as a prohibition on a construct. This is a **breaking change** to any external implementer of
`IOperation`, in the sense `system-contracts.md` defines, and a compile-time one, exactly as requiring
`Usage` was; it is admitted rather than softened. It is taken now because it is cheapest now: there are
two implementations, no tag, nothing published, and a large batch of new operations is about to be
commissioned against whichever shape exists. An overload preserving the synchronous form was rejected for
the same reason a defaulted `Usage` was — it would leave the blocking path implementable, and therefore
implemented, while the contract claimed otherwise. The clauses name no member, because how the signal is
carried and how the finding is typed are interior; only that cancellation is the caller's and the finding
is data are promises. For `TOOLKIT-15` to be observable in the tool anyone runs rather than only in the
library, the signal has to originate at the process itself, which is why an interrupt there is contracted
separately as `TOOLKIT-16`.

**Offline is a failure, not a degraded mode** — a model-backed operation that cannot reach a model
stops. Falling back to a weaker deterministic answer was rejected because the caller cannot see which
answer it received, and a silently weakened check is the failure this repository treats as worse than
no check.

**The reference implementation is studied, not copied or depended on** — an existing internal codebase
solves the same problem at much larger scale, including retrieval, memory and a process engine. Its
seams informed this design and none of its code is taken wholesale, because importing the parts that are
not needed is how a small tool acquires a large one's maintenance cost.

**The diagnostic trace is deliberately left out of the contract** — the tool emits a low-level, high-volume trace of
its own execution for the purpose of debugging the tool itself, and that stream carries no `Provides`
clause on purpose. Its entire value is being free to change: contracting its shape, volume or
destination would turn a debugging aid into a promise and every restructuring of the tool's internals
into a breaking change. It is named here so its absence from the contract reads as a decision rather than
an omission. Whether such a trace exists at all, what it contains, and what produces it are interior
matters for a later stage to settle.

**The diagnostic trace, the invocation record and the transcript are three streams, not one** — the
trace above, the queryable invocation record (`TOOLKIT-08`) and the model-interaction transcript
(`TOOLKIT-11`) serve three different purposes, and serving them from a single structured sink is the
obvious-looking simplification that must be refused. If the interior trace *were* also the contracted
record, the interior would become contracted by accident: the trace could never be restructured without
breaking `TOOLKIT-08`, and the very freedom that justifies leaving the trace out of the contract would be gone.
The record and the transcript are contracted for what they promise a later query; the trace is free
precisely because it promises nothing, and that separation is what keeps both properties true.

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

**Transcripts are captured always, not on demand** — capture covers every model interaction, not only
those that failed or refused, and is not placed behind an off-by-default flag. An opt-in guarantees the
evidence is absent exactly when something surprising happened, and unlike a deterministic check the
interaction cannot be recovered by re-running it. Failure-and-refusal-only capture misses the case a
later audit stage exists to catch — the confidently wrong SUCCEEDED, which is silent by construction.
The volume is real but small; a measurement run made sixteen probes. Because a transcript contains
repository source, the files are gitignored and never committed. That capture happens is therefore what
`TOOLKIT-11` contracts; where the transcripts live, their format, and any pruning of them are interior
concerns.

**An outcome is identified so its meaning survives the outcome set growing** — `TOOLKIT-08` requires the
recorded outcome to keep its meaning as new outcomes are added, because aggregation is across runs and
runs span releases. Identifying an outcome by a position that shifts when a member is inserted — as
happened when a new outcome was added mid-set and moved an existing one's ordinal — would make a record
written by one version mean something else when read against another, quietly corrupting the aggregation
the clause promises. The clause states the stability as observable behavior and names no encoding,
because how the identity is made stable is an interior decision and only the survival of meaning is the
promise.
