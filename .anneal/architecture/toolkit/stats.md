---
level: subsystem
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/StatsOperation.cs
---

[← Toolkit](../toolkit.md)

`stats` reads the invocation records `TOOLKIT-08` already makes every invocation append, and reports,
for each action found in the corpus, its pass rate — `Succeeded ÷ (Succeeded + Failed + Refused +
Escalated)`, with `UsageError` excluded from both sides because a caller's typo is not evidence about
the process — across five cumulative time windows: today, last 3 days, last 7 days, last 30 days, and
all-time. Each window includes everything the narrower window already counted, so a trend is visible
in one run without the tool storing a baseline to diff against. It is deterministic: it computes
counts from records already on disk and consults no model, which is also why it declares
`OperationCategory.Advisory` rather than gating — it answers a question nobody put, read at the start
of a stage to ground a conversation in data, and nothing downstream is obliged to act on it.

## Contract

### Provides

- **TOOLKIT-21** — `stats` reads a repository's invocation records and reports, for each action found,
  its pass rate — `Succeeded ÷ (Succeeded + Failed + Refused + Escalated)`, excluding `UsageError` —
  across five cumulative time windows (today, last 3 days, last 7 days, last 30 days, all-time), with
  the raw counts behind every percentage. It is deterministic and consults no model.
  *Verified by:* `ToolkitContractTests.StatsReportsPerActionPassRatesAcrossWindows`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every operation is built
  from.
