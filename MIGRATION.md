# Migration: adopting the Toolkit

This file is the approved proposal every Migration commit references. It exists only while stages are
outstanding; the commit landing the final stage deletes it.

The move is staged so that Anneal proves the Toolkit on itself before any downstream repository depends
on it. No stage before S2 changes what an installed repository receives, which means S1 can be abandoned
without a downstream consequence.

`check-contracts.ps1` runs **without** `-Strict` until the final stage lands, because planned clauses are
closed stage by stage and unfulfilled obligations are expected in between.

## S1 — Skeleton and the two seed operations

Anneal acquires `src/`, `test/`, a solution and a working `build.ps1` — the last of which
[AGENTS.md](AGENTS.md) and the check-contracts skill have both instructed agents to run for some time
despite it not existing. The tool is built and tested here but published nowhere and shipped to nobody.

Seed operations are `verify-evidence`, which is deterministic and consults no model, and
`probe-rule-owner`, which is the first model-backed operation and therefore the first real exercise of
the schema-last probe.

**Leaves working:** everything. Anneal's existing PowerShell gates are untouched, and the payload is
unchanged, so a downstream repository sees nothing.

**Exit conditions:** `TOOLKIT-01`, `TOOLKIT-02`, `TOOLKIT-03`, `TOOLKIT-04`, `TOOLKIT-06`, `TOOLKIT-07`,
`TOOLKIT-I1`, `TOOLKIT-I2` are verified by tests that exist and pass. Anneal's own build runs the .NET
tests alongside the PowerShell suites.

Because `TOOLKIT-04` is the first probe, this stage also produces the first measurement of parse-failure
rate under a described schema with no constrained decode — the number the *schema is a prompt-level hint*
decision in [toolkit.md](docs/architecture/toolkit.md) says must be measured rather than assumed. Record
it; S3 and S5 both lean on it.

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
[toolkit.md](docs/architecture/toolkit.md) and is deleted, and the system count returns to five.

**Leaves working:** the enforcement gate, which must behave identically before and after. The existing
34-case suite is the acceptance evidence and is ported, not discarded.

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
