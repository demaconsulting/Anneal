# Migration: adopting the Toolkit

This file is the approved proposal every Migration commit references. It exists only while stages are
outstanding; the commit landing the final stage deletes it.

The move is staged so that Anneal proves the Toolkit on itself before any downstream repository depends
on it. Before S2 the only change an installed repository receives is to how `check-contracts.ps1`
discovers tests — a facility that alters no repository's behavior until that repository adopts a second
test framework — so the early stages can still be abandoned without a downstream consequence.

**S1 was split into S1a and S1b by amendment**, after implementation found that the two halves carry
unrelated risk. S1a is scaffolding and a deterministic operation: no model, no network, nothing that can
fail in an unfamiliar way. S1b is the model seam, where every unknown lives. Bundled, a failure in the
SDK integration would block the build scaffolding that every later stage needs. The exit conditions are
unchanged in substance; they are divided between the two.

`check-contracts.ps1` runs **without** `-Strict` until the final stage lands, because planned clauses are
closed stage by stage and unfulfilled obligations are expected in between.

## S1a — Foundation and the deterministic operation

Anneal acquires `src/`, `test/`, a solution and a working `build.ps1` — the last of which
[AGENTS.md](AGENTS.md) and the check-contracts skill have both instructed agents to run for some time
despite it not existing. The tool is built and tested here but published nowhere and shipped to nobody.

The operation is `verify-evidence`: deterministic, consulting no model, reporting whether each evidence
locator cited in an agent report is really present at the file and line named. It is the half of the
*judging agent must show its basis* constraint that a machine can settle.

This stage also carries a payload change that implementation revealed to be unavoidable.
`check-contracts.ps1` models one repository as having one test framework: `-TestResultFormat` takes a
single value, and `-ContractTestFolder` must be empty for Anneal's flat root-level suites but `Contract`
for C# boundary tests. Once Anneal has both, no combination of the parameters expresses its layout —
which is the case the script's own modification policy reserves for editing it. The alternative,
contorting Anneal's layout to fit one profile, was rejected because it requires emitting TRX from
PowerShell purely to impersonate a C# result, and hand-written result parsing in PowerShell is
specifically the cost this whole system exists to stop paying.

The extension must stay narrow: repeatable discovery profiles, not a general configuration language.
`check-contracts.ps1` is the one mechanically enforced check in the process and must fail closed, so its
34-case suite is extended in the same change rather than afterwards.

Three documentation claims become false at the moment this stage lands, and are corrected in the **same
commit** rather than before or after it: [AGENTS.md](AGENTS.md)'s Template Stewardship section states
Anneal has no `src/` or `test/` tree and that `build.ps1` legitimately differs from the template's; the
same claim is duplicated in [CONSTRAINTS.md](CONSTRAINTS.md), which by AGENTS.md's own one-owner rule is
a defect to fix while both sentences are being rewritten anyway; and the stewardship label for
`build.ps1` is *adopted from the template*, not *deliberately divergent*, because the file does not
exist here at all.

**Leaves working:** everything. The existing PowerShell suites keep passing unchanged, and a downstream
repository receives only a discovery facility it does not yet use.

**Exit conditions:** `TOOLKIT-01`, `TOOLKIT-02` and `TOOLKIT-03` are verified by tests that exist and
pass. `TOOLKIT-06` closes here only if a deterministic operation can express refusal honestly; if it
cannot, it moves to S1b rather than acquiring an invented refusal path to satisfy the check. Anneal's
own build runs the .NET tests alongside the PowerShell suites, `check-contracts.ps1` resolves clauses
in both languages in one invocation, and the three documentation claims above are true again.

## S1b — The model seam

The provider, the capability roles, and the schema-last probe: a conversation whose response schema is
presented after the reasoning rather than before it. The operation is `probe-rule-owner`, which names
the single file owning a given rule or refuses.

This is where every unknown in the design lives, which is why it follows a stage that already produced
a working build with tests running.

**Leaves working:** everything S1a left working. The deterministic operation does not depend on this
stage and must keep passing without a model reachable.

**Exit conditions:** `TOOLKIT-04`, `TOOLKIT-07`, `TOOLKIT-I1`, `TOOLKIT-I2` verified, plus `TOOLKIT-06`
if S1a left it open.

Because `TOOLKIT-04` is the first probe, this stage produces the first measurement of parse-failure rate
under a described schema with no constrained decode — the number the *schema is a prompt-level hint*
decision in [toolkit.md](docs/architecture/toolkit.md) says must be measured rather than assumed, and
the falsifier for the matching assumption in [README.md](README.md). Record it; S3 and S5 both lean on
it, and a bad number is a reason to revisit the design rather than to patch around it.

## S2 — Shipping it

Template gains `.config/dotnet-tools.json` and the role configuration file, Installer delivers both, and
CI restores the tool. Roles become configurable per repository, and invocations begin appending
structured records.

**Leaves working:** a repository that never invokes an operation, which still needs only the copied
payload. Restore failure must not break a repository that calls nothing.

**Exit conditions:** `TOOLKIT-05`, `TOOLKIT-08`, `TOOLKIT-09` verified. The *installed payload must be
identifiable by version* entry in [CONSTRAINTS.md](CONSTRAINTS.md) moves up to Satisfied, and the
*agent-report corpus* entry is absorbed by structured records. `agent-metrics.ps1`, which scrapes prose
with regular expressions, is deleted rather than left as a second source.

## S3 — Auditing verdicts

Adds an operation that samples reported SUCCEEDED verdicts and re-checks them against their evidence.
This targets the failure the report corpus shows to be both real and silent: a false FAILED is loud and
gets fixed, while a false SUCCEEDED ships unnoticed and is invisible to any metric the agent produces
about itself.

**Leaves working:** everything; the operation is advisory and cannot gate.

**Exit condition:** the new clause is verified, and a sample of historical reports is audited with the
result recorded — including how often the audit itself is wrong, since an unreliable auditor of verdicts
is worse than none.

## S4 — Folding in ContractCheck

`check-contracts.ps1` becomes a Toolkit operation in the enforcement category.
[contract-check.md](docs/architecture/contract-check.md) merges into
[toolkit.md](docs/architecture/toolkit.md) and is deleted, returning the repository to four systems.

**Leaves working:** the enforcement gate, which must behave identically before and after. The check's
own suite is the acceptance evidence and is ported, not discarded.

**Exit conditions:** the ported operation reproduces every failure in the documented taxonomy; the
script is removed from the payload only once the tool has replaced it in CI for a full release; and
`TOOLKIT-I3` is verified, since this is the first enforcement operation that gates a downstream build.

## S5 — Mechanizing stable rules (final)

Rules currently held in prose move into code **individually, and only on evidence**. A rule qualifies
when it is deterministic — decidable without judgement — and when it has been measured stable, meaning
its wording has not needed correction across a meaningful run of changes. Judgement does not move at
all: no agent's verdict becomes a C# decision, because a unit test can prove a step ran and cannot prove
a verdict was right.

The gate is deliberate. A wrong prompt is corrected in one edit; a wrong encoded rule is corrected
through build, test, publish and restore, and an agent editing the tool does not change the tool it is
running under. Encoding an unstable rule therefore buys determinism at the price of the cheap design
change this repository exists to protect. `PROCESS-05` is the natural first candidate and also the
caution: it cannot be mechanized today because the invocation graph is stated as prose in one file, a table
in another and a non-invocation in a third — that is a rule which is not yet one rule, and the repair is
to make it one, not to encode three.

Nothing here may reintroduce per-unit artifact fan-out, per-unit requirements, hard-fail companion gates
or multi-retry orchestration. Those four are named in *What must not be reintroduced* in
[overview.md](docs/architecture/overview.md), and a process engine that owns both control flow and
judgement is their shape.

**This is the final stage. The commit that lands it deletes this file.**

**Exit conditions:** every planned clause in [toolkit.md](docs/architecture/toolkit.md) is verified,
`pwsh ./check-contracts.ps1 -Strict` passes, and each rule moved is recorded with the evidence that
qualified it. A rule that cannot show that evidence stays in prose, and stopping here with rules still
in prose is a legitimate end state rather than an incomplete migration.
