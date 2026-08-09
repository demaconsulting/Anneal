---
id: check-contracts-placeholder-form
tags:
  - check-contracts
  - contracts
  - placeholders
summary: Planned contract verifiers must begin with TODO. or TODO_ and are closed by replacing that prefix with the real boundary test name.
---

Use the `TODO.` or `TODO_` prefix only at the start of a planned verifier name when a contract clause
is intentionally staged ahead of implementation. `check-contracts` treats exactly that anchored prefix
as an unfulfilled obligation in non-strict mode and promotes it to an error under `-Strict`; any other
missing verifier is an error immediately, so a stray TODO elsewhere does not defer anything.
