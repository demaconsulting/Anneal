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
  unrecognized action exits non-zero listing the actions that exist, so a caller discovers the surface
  without reading the source.
  *Verified by:* `ToolkitContractTests.UnknownActionListsAvailableActions`

- **TOOLKIT-02** — Every operation declares exactly one category — enforcement, research, advisory or
  authoring — and the category alone determines whether a non-zero exit gates a build. Only
  enforcement operations gate.
  *Verified by:* `ToolkitContractTests.OnlyEnforcementOperationsGate`

- **TOOLKIT-03** — `verify-evidence` reports, for each evidence locator cited in an agent report,
  whether the quoted text is present at the file and line named. It reaches no verdict about the
  report's conclusion and consults no model.
  *Verified by:* `ToolkitContractTests.EvidenceLocatorsAreCheckedAgainstSource`

- **TOOLKIT-04** — `probe-rule-owner` names the single file that owns a given rule, or refuses when
  the rule is stated in more than one place or in none.
  *Verified by:* `TODO.RuleOwnerProbeNamesOneFileOrRefuses`

- **TOOLKIT-05** — Every operation that consults a model declares the capability role it requires, and
  roles resolve to concrete models through repository configuration rather than through the operation.
  *Verified by:* `TODO.OperationRolesResolveThroughConfiguration`

- **TOOLKIT-06** — Refusal is reported as an outcome distinct from both success and failure, so a
  caller can tell "the question could not be answered on the available evidence" from "the answer is
  no".
  *Verified by:* `TODO.RefusalIsDistinctFromFailure`

- **TOOLKIT-07** — A model-backed operation that cannot reach a model fails with a message naming the
  cause. It never falls back to a deterministic approximation, and never reports success on a
  judgement it did not obtain.
  *Verified by:* `TODO.UnreachableModelFailsLoudly`

- **TOOLKIT-08** — Every invocation appends a structured record of the operation, its inputs, its
  outcome and any model usage, in a form a later query can aggregate without parsing prose.
  *Verified by:* `TODO.InvocationsAppendStructuredRecords`

- **TOOLKIT-09** — The tool reports the Anneal version it was built from, so an installed payload can
  be identified by version rather than inferred from its contents.
  *Verified by:* `TODO.ToolReportsPayloadVersion`

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
  *Verified by:* `TODO.ModelToolGrantsAreReadOnlyAndExplicit`

- **TOOLKIT-I2** — A probe result reaches a caller only as a fully decoded typed value. A response
  that cannot be decoded within the retry budget fails the operation; no partially populated result is
  returned.
  *Verified by:* `TODO.UndecodableProbeResultFailsTheOperation`

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

**Offline is a failure, not a degraded mode** — a model-backed operation that cannot reach a model
stops. Falling back to a weaker deterministic answer was rejected because the caller cannot see which
answer it received, and a silently weakened check is the failure this repository treats as worse than
no check.

**The reference implementation is studied, not copied or depended on** — an existing internal codebase
solves the same problem at much larger scale, including retrieval, memory and a process engine. Its
seams informed this design and none of its code is taken wholesale, because importing the parts that are
not needed is how a small tool acquires a large one's maintenance cost.
