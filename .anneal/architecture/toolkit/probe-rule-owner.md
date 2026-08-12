---
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/ProbeRuleOwnerOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/RuleOwnerAnswer.cs
---

[← Toolkit](../toolkit.md)

# ProbeRuleOwner

`probe-rule-owner` answers which single file owns a given rule. It asks a model to locate the rule
across the payload and names the one file that states it. Where the rule is stated in more than one
place, or in none, it does not guess: it refuses, and that refusal is reported as an outcome distinct
from a failure and from a confident answer, so a caller can tell "the rule has no single owner" from
"the probe could not run". Because it consults a model, it is built on the [Model Seam](./model-seam.md)
in addition to the [Runtime](./runtime.md), and its refusal is the seam's refusal outcome
(`TOOLKIT-06`) surfacing at the operation boundary.

## Contract

### Provides

- **TOOLKIT-04** — `probe-rule-owner` names the single file that owns a given rule, or refuses when
  the rule is stated in more than one place or in none.
  *Verified by:* `ToolkitContractTests.RuleOwnerProbeNamesOneFileOrRefuses`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every operation is built
  from.
- **[Model Seam](./model-seam.md)** — role resolution, the typed probe, refusal as a distinct outcome,
  and transcription of the interaction.
