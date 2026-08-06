---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Model/**
  - src/DemaConsulting.Anneal.Toolkit/Recording/ModelTranscript.cs
---

[← Toolkit](../toolkit.md)

# Model Seam

The Model Seam is the machinery only a model-backed operation touches: how a capability role resolves
to a concrete model, how a refusal is told apart from a failure, what happens when no model can be
reached, how every interaction is transcribed, and what a model is allowed to do with the repository.
A deterministic operation such as [verify-evidence](./verify-evidence.md) or
[check-contracts](./contract-check.md) reaches none of this; a model-backed one such as
[probe-rule-owner](./probe-rule-owner.md) is built on it in addition to the
[Runtime](./runtime.md).

The seam is a provider that resolves a capability role to an endpoint, and one object over it offering
two verbs: *run*, whose request and reply both join a conversation, and *probe*, a one-shot question
whose typed answer joins nothing. The seam exists so that no operation knows which provider answered,
and so the two interaction shapes cannot drift apart in how they decode or retry. If the seam moved —
if operations built their own prompts — the schema-last ordering that justifies the Toolkit would be
re-implemented per operation and would decay unevenly.

The prompt assembly deliberately owns more than the caller supplies: an operation provides its question
and its authoritative context, while the framework contributes the response schema, derived by
reflection over the typed result. A caller cannot forget the schema, and cannot place it early.

## Contract

### Provides

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

- **TOOLKIT-11** — Every model interaction records a transcript of it — the prompt sent, the reply
  received, the model consulted and the token usage — for every interaction rather than only for those
  that failed or refused, and with no opt-in that could leave it off. A model asked the same question
  later may answer differently, so this is the only evidence in the system that cannot be reconstructed
  by re-running, and it is captured at the time or lost.
  *Verified by:* `ToolkitContractTests.ModelInteractionsAreTranscribed`

### Requires

- **[Runtime](./runtime.md)** — the invocation record the transcript is captured alongside, and the
  outcome vocabulary refusal is reported through.
- **GitHub Copilot SDK** — model access under the ambient Copilot account of the calling session, with
  no token supplied by the Toolkit.

### Invariants

- **TOOLKIT-I1** — A model consulted by any operation is granted read-only repository tools. No
  operation grants a tool that executes a command or writes a file, and the granted tool set is always
  an explicit allowlist rather than an absent one.
  *Verified by:* `ToolkitContractTests.ModelToolGrantsAreReadOnlyAndExplicit`

- **TOOLKIT-I2** — A probe result reaches a caller only as a fully decoded typed value. A response
  that cannot be decoded within the retry budget fails the operation; no partially populated result is
  returned.
  *Verified by:* `ToolkitContractTests.UndecodableProbeResultFailsTheOperation`

## Decisions

**Model configuration is data, not code** — roles appear in this contract; the models behind them do
not. Defaults are compiled in, and a repository changes the models behind its roles by writing its own
`.anneal/config.json` over them, without a Toolkit release. The rejected alternative, model names in
the contract, would make every model substitution a contract change.

**A role names an ordered list of candidates, and the first one the account offers answers** — both
the compiled-in defaults and `.anneal/config.json` map a role to candidates in preference order, and
the role resolves by asking the provider which models the account is offered and taking the first
candidate present. The forcing case is rot rather than choice: a compiled-in default naming a single
model breaks every repository that has not written its own config the day that model is retired, and
only a Toolkit release fixes it — which is already live, since one compiled-in default names a model
the account no longer offers at all. An ordered list lets a newer model lead with an older one held
as a rearguard, so a retirement degrades instead of breaking. The rejected alternative was a
failure-triggered fallback chain — try a candidate, and on error try the next. It was rejected
because the model seam deliberately flattens every provider-side error into one unavailability
failure and so cannot tell a retired model from a rate limit, an expired credential or a transport
fault; falling back on failure would silently downgrade a heavy-role judgement to a lighter model
whenever the network hiccuped, which is the same silently-weakened answer *Offline is a failure, not
a degraded mode* refuses. Availability is asked about, never inferred from a failure, and that error
flattening is not being changed to accommodate this. Configuration remains the only source of
candidates, so `TOOLKIT-05` is unchanged: the operation still has no say in which model answers, and
no model outside the repository's configured set can ever be selected.

**The configuration format changed outright, and the old form fails loudly** — a repository still
holding the single-name form (`"light": "gpt-5.4-mini"`) no longer loads: a role is a list now, so
the old file stops working. It stops with the same unavailability failure a missing model raises,
naming what could not be resolved, rather than substituting a model silently — the loudness is the
point, and it is the same reasoning that already makes a file which exists but cannot be parsed an
error instead of a fallback to defaults. This is a **breaking change** in the sense
`system-contracts.md` defines, and it is admitted rather than softened, for the same reason a
tolerant parser accepting both forms was declined rather than written: nothing is published, the
tool is pinned `0.1.0-dev`, and there is no installed base to protect, so carrying two formats
forever would buy nobody anything.

**Availability is asked lazily, and a failed enquiry is not a gate** — the enquiry happens only when
a role is actually being resolved for use, so a run that consults no model makes no such call and a
deterministic check acquires no network dependency; that separation is the whole reason a
deterministic operation stays apart from model-backed work. When the enquiry itself fails, resolution
falls back to the first configured candidate and the call succeeds or fails on its own terms.
Treating a failed enquiry as a failed resolution was rejected because it would convert an
optimization over guessing into a new way for a working run to stop. A role whose candidates are all
absent from the offered set is a different case and does stop, under `TOOLKIT-07`, naming the role,
the candidates tried and `.anneal/config.json` as the place to change them.

**The model seam breaks at compile time, and the break is taken now** — `IChatEndpoint` gains a
**required** member, the availability enquiry, and single-model lookup gives way to candidate lookup
and asynchronous resolution. That is a **breaking change** to any external implementer of the seam,
in the sense `system-contracts.md` defines, and a compile-time one exactly as requiring `Usage` and
the asynchronous operation boundary were. It is admitted for the reason those were: nothing is
published, the tool is pinned `0.1.0-dev`, and there is no installed base to protect. A defaulted
enquiry answering "everything is offered" was rejected on the same grounds a defaulted `Usage` was —
it would leave a seam implementable that cannot answer the question resolution rests on, and
resolution would then be guessing while the contract claimed it asks.

**Availability-based resolution is a new exposure on `TOOLKIT-I3`, and is recorded rather than
hidden** — which model answers now depends on the calling account's entitlements, which are not part
of the repository input, so two runs over identical files could in principle resolve a role to
different models and an enforcement operation built on model judgement could differ. The operation that
gates today, `check-contracts`, consults no model — its verdict is a pure function of repository
inputs — so `TOOLKIT-I3` holds for it and is verified. The exposure is therefore a standing condition
on any *future* model-backed enforcement operation, which would have to re-establish that stability
independently; it is not a reason to prefer the single compiled-in model this resolution replaces, a
shape that does not make a verdict reproducible and only makes the failure total when the model is
retired.

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

**Transcripts are captured always, not on demand** — capture covers every model interaction, not only
those that failed or refused, and is not placed behind an off-by-default flag. An opt-in guarantees the
evidence is absent exactly when something surprising happened, and unlike a deterministic check the
interaction cannot be recovered by re-running it. Failure-and-refusal-only capture misses the case a
later audit stage exists to catch — the confidently wrong SUCCEEDED, which is silent by construction.
The volume is real but small; a measurement run made sixteen probes. Because a transcript contains
repository source, the files are gitignored and never committed. That capture happens is therefore what
`TOOLKIT-11` contracts; where the transcripts live, their format, and any pruning of them are interior
concerns.
