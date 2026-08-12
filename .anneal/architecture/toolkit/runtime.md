---
covers:
  - src/DemaConsulting.Anneal.Toolkit/IOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/OperationCategory.cs
  - src/DemaConsulting.Anneal.Toolkit/OperationOutcome.cs
  - src/DemaConsulting.Anneal.Toolkit/OperationResult.cs
  - src/DemaConsulting.Anneal.Toolkit/Recording/**
---

[← Toolkit](../toolkit.md)

# Runtime

The Runtime is the execution machinery every Toolkit operation is built from, whatever the operation
does: the category-and-gating rule, the outcome-and-exit-code model, the structured invocation record,
the finding an operation returns beside its outcome, and the asynchronous boundary that carries the
caller's cancellation signal to the operation. A deterministic operation is built from the Runtime
alone; a model-backed one adds the [model seam](./model-seam.md) on top of it. These are the promises
every operation obeys, so they live once here rather than being restated at each operation.

## Contract

### Provides

- **TOOLKIT-02** — Every operation declares exactly one category — enforcement, research, advisory or
  authoring — and for an operation that ran, the category alone determines whether the non-zero exit
  of its outcome gates a build. Only enforcement operations gate.
  *Verified by:* `ToolkitContractTests.OnlyEnforcementOperationsGate`

- **TOOLKIT-08** — Every invocation appends a structured record of the operation, its inputs, its
  outcome and any model usage, in a form a later query can aggregate without parsing prose. The record
  identifies the outcome so that its meaning is fixed as new outcomes are added, so records aggregated
  across versions — which is what aggregation means, since runs span releases — cannot silently change
  meaning when the set of possible outcomes grows.
  *Verified by:* `ToolkitContractTests.InvocationsAppendStructuredRecords`

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

- **TOOLKIT-20** — An operation reports escalation as an outcome distinct from both success and
  failure, carrying its own exit code and rendering distinctly at the terminal, so a caller can tell
  "it ran, could not finish, and finishing needs a decision only you can make" from "it failed". This
  is what `TOOLKIT-06` does for refusal one level down, at the model call, applied at the operation.
  *Verified by:* `ToolkitContractTests.EscalationIsDistinctFromSuccessAndFailure`

### Requires

- **.NET SDK** — the runtime the operations execute on, and the filesystem the invocation record is
  appended to.

### Invariants

- **TOOLKIT-I3** — An enforcement operation given identical repository inputs reaches an identical
  verdict, so a gating check cannot change answer on unchanged input.
  *Verified by:* `ToolkitContractTests.EnforcementVerdictsAreStableOnUnchangedInput`

- **TOOLKIT-I5** — The caller's cancellation signal is the only one in effect for the whole of an
  invocation: no operation substitutes a signal of its own at any point between the invocation and the
  model it consults. A cancellation therefore takes effect while a model call is still waiting for its
  reply, rather than only after the reply arrives.
  *Verified by:* `ToolkitContractTests.CancellationTakesEffectWhileAModelCallIsInFlight`

## Decisions

**A finding is data; the text is a rendering of it** — an operation returns what it concluded as a value
carried beside its outcome — typed but not type-parameterized: the value is a domain type while the slot
holding it is not, so which type a given operation puts there is the caller's knowledge rather than the
compiler's — and separately writes the human text it writes today (`TOOLKIT-14`).
The forcing case is composition: the verdict auditor [active-plan.md](../../work/active-plan.md) carries as
a candidate re-checks verdicts another
operation reported, and if the only channel out of an operation is a `TextWriter`, composing means
re-parsing prose — the exact mistake `agent-metrics.ps1` made before its retirement, scraping
reports with regular expressions. The evidence that the channel is wrong rather than merely narrow is
that `probe-rule-owner` already computes a typed answer and flattens it to lines at the boundary: the
structure exists and is being thrown away one layer before the caller. Two alternatives were rejected. A
generic operation interface parameterized by its result type was rejected because the dispatcher — which
is the surface consumers hold — must keep a heterogeneous set of actions, so it can only hold
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
`TOOLKIT-14` as `TOOLKIT-08` partly delivered.
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

**An outcome is identified so its meaning survives the outcome set growing** — `TOOLKIT-08` requires the
recorded outcome to keep its meaning as new outcomes are added, because aggregation is across runs and
runs span releases. Identifying an outcome by a position that shifts when a member is inserted — as
happened when a new outcome was added mid-set and moved an existing one's ordinal — would make a record
written by one version mean something else when read against another, quietly corrupting the aggregation
the clause promises. The clause states the stability as observable behavior and names no encoding,
because how the identity is made stable is an interior decision and only the survival of meaning is the
promise.
