---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/VerifyEvidenceOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/EvidenceLocator.cs
---

[← Toolkit](../toolkit.md)

# VerifyEvidence

`verify-evidence` takes the evidence locators an agent report cites — each a claim that a particular
quotation appears at a named file and line — and reports, locator by locator, whether the quoted text
is present where the report says it is. It is deterministic: it reaches no verdict about
whether the report's conclusion follows from its evidence, and it consults no model. A locator is
checked as a textual and positional fact about the repository, nothing more, which is why the operation
is built on the [Runtime](./runtime.md) alone and touches none of the model seam.

## Contract

### Provides

- **TOOLKIT-03** — `verify-evidence` reports, for each evidence locator cited in an agent report,
  whether the quoted text is present at the file and line named. It reaches no verdict about the
  report's conclusion and consults no model.
  *Verified by:* `ToolkitContractTests.EvidenceLocatorsAreCheckedAgainstSource`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every operation is built
  from.
