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
  *Verified by:* `TODO.OperationRolesResolveThroughConfiguration`

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
  *Verified by:* `TODO.InvocationsAppendStructuredRecords`

- **TOOLKIT-09** — The tool reports the Anneal version it was built from, so an installed payload can
  be identified by version rather than inferred from its contents.
  *Verified by:* `TODO.ToolReportsPayloadVersion`

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
  *Verified by:* `TODO.ModelInteractionsAreTranscribed`

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

## Composition

Three parts, cut where the reasoning differs.

**Operations** are the contract surface: one per action, each declaring its category and, where
applicable, its capability role. Everything above is dispatch and argument parsing; everything below is
shared machinery. This is the only layer the contract describes, which is deliberate — an operation
invoked by a downstream agent *is* a promise, so adding one is a contract change and is meant to feel
like one.

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
