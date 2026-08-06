---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Model/**
  - src/DemaConsulting.Anneal.Toolkit/Recording/ModelTranscript.cs
  - src/DemaConsulting.Anneal.Toolkit/Recording/ToolCallTranscript.cs
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

- **TOOLKIT-18** — Every tool invocation a model makes is transcribed with its arguments and its
  outcome, including one that was refused. A provider that runs the tool loop natively leaves
  `TOOLKIT-11` recording a prompt and a final reply while blind to every file the model touched, which
  for a worker that writes is the only part of its behavior worth auditing.
  *Verified by:* `ToolkitContractTests.ToolInvocationsAreTranscribed`

### Requires

- **[Runtime](./runtime.md)** — the invocation record the transcript is captured alongside, and the
  outcome vocabulary refusal is reported through.
- **GitHub Copilot SDK** — model access under the ambient Copilot account of the calling session, with
  no token supplied by the Toolkit.

### Invariants

- **TOOLKIT-I2** — A probe result reaches a caller only as a fully decoded typed value. A response
  that cannot be decoded within the retry budget fails the operation; no partially populated result is
  returned.
  *Verified by:* `ToolkitContractTests.UndecodableProbeResultFailsTheOperation`

- **TOOLKIT-I6** — A model is granted tools only by group selection: the set handed to it is the tools
  of the groups its operation was granted, always as an explicit allowlist rather than an absent one.
  Every filesystem path a tool is given resolves inside the repository root, and a write to a protected
  configuration file or repository script is refused.
  *Verified by:* `ToolkitContractTests.ToolGrantsAreScopedContainedAndProtected`

## Decisions

**`TOOLKIT-I1` is retired and replaced by `TOOLKIT-I6`** — the old invariant guaranteed that a model
is granted *read-only* repository tools, and that guarantee cannot survive a process whose whole job
is to edit files. It is retired rather than weakened, because a clause that says "read-only except
when not" states nothing. What was doing the real work in it — the granted set is always an explicit
allowlist and never an absent one, since an absent allowlist imposes no restriction and exposes the
provider's own built-in mutating tools — is kept verbatim inside `TOOLKIT-I6`, which adds the two
guarantees that replace read-only: every path resolves inside the repository root, and the protected
configuration files and repository scripts are refused. The number `TOOLKIT-I1` is never reused.

**Scoping is by selection, not by a runtime gate** — an operation names the groups it was granted and
receives exactly those groups' tools; a tool from a group that was not granted is absent from the set the model
is offered. The rejected alternative was a permission check inside each tool, which leaves the tool
present and therefore callable, arguable with, and forgettable to check. An absent tool cannot be
called, so there is nothing to talk past. Groups rather than individual grants because grouping is
what stops every later tool addition from needing its own wiring decision at every call site.

**There is no shell group, and none is granted** — the processes that need `fix.ps1` and `lint.ps1`
run them as their own control flow. A worker granted a command tool can do anything and then report
plausibly that it did not, which is the precise failure this system exists to eliminate; a script the
operation runs itself has an exit code the operation read.

**Containment is lexical, and the limit is stated rather than implied** — a model-supplied path is
resolved against the repository root and compared textually, which rejects rooted, drive-qualified,
cross-drive, UNC and device paths, anything climbing above the root, and a sibling directory that
merely shares a textual prefix with the root. It does **not** resolve symbolic links or junctions, so
a link inside the repository pointing outside it is followed. That is recorded because it is
invisible at the call site — the check looks total and is not — and closing it requires resolving
every path through the filesystem, which fails differently on each platform and cannot be tested
without creating links the test runner may not be privileged to create.

It also rejects any path carrying a colon beyond a drive-letter position, because Windows lets a
file's own contents be named through an alternate stream alias — `fix.ps1::$DATA` reads and writes
exactly what `fix.ps1` does, while matching no deny-list entry spelled `fix.ps1`. The rejection lives
in the containment primitive rather than in the deny-list so that it closes for every tool at once
rather than for the one check someone remembered; a repository-relative path has no legitimate use
for that spelling.

**A refused write to a protected path is a different refusal from a path outside the repository** —
both are refused, both are transcribed, and they carry distinct prefixes and distinct transcript
classifications. They mean different things to the process driving the conversation: a worker denied
a path outside the repository made a mistake it can correct, while a worker denied a protected file
has run into a decision only the user can make. Reading them as one refusal was a real defect —
`lint-fix` escalated on an out-of-bounds read, telling the user a protected file needed their
approval when none did. A false escalation is exactly as damaging as a false success, since both
report something that did not happen.

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
