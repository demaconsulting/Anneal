---
id: route-non-trivial-change-through-dispatch
tags:
  - dispatch
  - routing
  - process-discipline
summary: Before hand-launching a sub-agent to implement a non-trivial change, check whether dispatch/route already has a compiled path for it.
---

AGENTS.md's routing table is unambiguous: any non-trivial Change routes through dispatch, which classifies scope and hands the work to route's compiled worker catalog (SmallFixWorker/ContractChangeWorker/StructuralChangeWorker). Those workers already compose document authoring and verification together, and route's Massive-effort path (TOOLKIT-26/27/28) already decomposes large work into phases with a mandatory cumulative check before any phase runs - a more rigorous version of any manual multi-slice-with-a-stop-condition plan a session might improvise. A session that has spent a long stretch doing direct architecture-design prose work can drift into defaulting to a hand-written sub-agent prompt for the next piece of work out of habit, even when dispatch is sitting right there and had already fired correctly earlier in the same task. Before delegating implementation of a staged contract clause (or any other non-trivial change) to a free-form sub-agent, stop and call dispatch first; only fall back to a manual approach if dispatch itself reports it cannot route the work, and say why in the report.
