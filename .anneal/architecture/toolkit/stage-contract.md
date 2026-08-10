---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/StageContractOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/StageContractReport.cs
---

[← Toolkit](../toolkit.md)

# Stage Contract

`stage-contract` is the compiled front door for staging a contract clause ahead of implementation —
the one remaining job `architecture-update.agent.md` still did after `apply.agent.md` retired at
Migration stage S16, before `architecture-update.agent.md` itself retired once this action was
live-validated. `route`'s Contract Change and Structural Change paths always compose `DocumentAuthor`
with `Developer` and `Verifier` in one atomic pass; neither has a "write the promise, implement it
later" mode, and Migration itself depends on that mode existing — `TOOLKIT-29`/`30`/`31` were each
staged this way, as a `TODO.` placeholder clause, before their implementation landed. `stage-contract`
is that mode, compiled.

No new worker type is introduced. `DocumentAuthor` is the exact primitive `ContractChangeWorker` and
`StructuralChangeWorker` already use for their own documentation half; `stage-contract` runs it alone,
instructed to stop before `Developer` or `Verifier` would run, and to write every clause in
`system-contracts.md`'s `TODO.`/`TODO_` placeholder form since no implementation exists yet for a real
test to verify. This operation constructs no `Router` and asks no routing oracle: a caller invoking
`stage-contract` has already decided the work is a staged, not-yet-implemented contract clause, the
same "Scope already fixed before this action is reached" reasoning `maintain.md` already applies to
Maintenance mode.

## Contract

### Provides

- **TOOLKIT-32** — `stage-contract` takes a work item and runs it directly against `DocumentAuthor`,
  asking no routing oracle and running no `Developer` or `Verifier` pass. It succeeds when a clause was
  authored and a non-strict `check-contracts` run (`TOOLKIT-34`) reports it well-formed; escalates when
  `DocumentAuthor` names a reroute, a protected-path write is refused, or the actual changes reach
  outside `docs/architecture/` (`TOOLKIT-33`); fails when the staged clause is not well-formed, the
  file-count budget is exceeded, or no model could be reached. A missing work item is a usage error
  under `TOOLKIT-10`.
  *Verified by:* `StageContractRunsWorkItemDirectlyThroughDocumentAuthor`

- **TOOLKIT-33** — After `DocumentAuthor`'s run, `stage-contract` checks the actual files it reports
  having changed and forces escalation, naming the offending file, when any of them falls outside
  `docs/architecture/` — the mirror image of `ProtectedPathTripwire`'s rule for Maintenance
  (`TOOLKIT-31`), since this action's whole job is to touch the architecture tree and nothing else.
  Checked against `DocumentAuthor`'s reported changed-file list, normalized against the repository
  root the same way `ProtectedPathTripwire` normalizes a declared file scope, rather than trusted as a
  literal string — no ledger of the model's real tool calls is consulted, since none yet exists;
  `DocumentAuthor`'s own report is the only evidence this check has, the same evidence `maintain`'s
  own equivalent check (`TOOLKIT-30`) reasons from.
  *Verified by:* `StageContractEscalatesWhenActualChangesReachOutsideTheArchitectureTree`

- **TOOLKIT-34** — After the architecture-tree check clears, `stage-contract` runs a non-strict
  `check-contracts` pass against the whole repository — the repository's configured arguments with any
  `-Strict` entry filtered out — and fails, printing what it found, rather than reporting an unqualified
  success, when that pass does not exit clean. Non-strict, because a staged clause's unfulfilled
  obligation is exactly what `-Strict` would otherwise promote from a warning to an error, per
  `system-contracts.md`'s own "use `-Strict` once implementation is complete" rule; a genuinely
  malformed document (for example, missing its `## Contract` section) still fails, since `check-contracts`
  fails closed regardless of `-Strict`. Because the check runs repository-wide rather than scoped to
  this run's own changes, a pre-existing unrelated failure elsewhere in the tree also fails this action —
  a coarser signal than "the clause this run staged is malformed", but the only one available without
  building `check-contracts` a change-scoped mode it does not otherwise need.
  *Verified by:* `StageContractFailsWhenTheStagedClauseIsNotWellFormed`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every operation is built
  from, and the escalation outcome this operation reports through.
- **[Model Seam](./model-seam.md)** — every model call `DocumentAuthor`'s own authoring pass makes.
- **[Process](../process.md)** — `DocumentAuthor` and `ContractCheckRunner`, both reused unchanged (the
  latter widened with a `strict` parameter, defaulting to its existing behavior) from the machinery
  `route`'s workers already built.

## Decisions

**No new worker type, and no routing-oracle pass** — `DocumentAuthor` is already proven inside
`ContractChangeWorker`/`StructuralChangeWorker`, and staging a clause ahead of implementation is a
scope the caller has already fixed before invoking `stage-contract`, the same "one oracle pass, not
two" reasoning `maintain.md` and `route.md` § Decisions already apply to their own narrower fronts.
`stage-contract` is a thinner front door than either, not a second `route`.

**The architecture-tree check is the mirror image of `maintain`'s protected-path tripwire, not the same
check reused unchanged** — Maintenance may never touch `docs/architecture/`; `stage-contract`'s entire
job is to touch nothing else. Reusing `ProtectedPathTripwire` as-is would have answered the wrong
question, so this operation checks containment directly rather than stretching that type to mean the
opposite of what its own name says.

**`ContractCheckRunner` gained an optional `strict` parameter rather than a second implementation** —
`stage-contract` needs the exact same in-process `check-contracts` call `ContractChangeWorker` and
`StructuralChangeWorker` already make, with one difference: it must not promote an unfulfilled planned
obligation to an error, since that obligation is what this action deliberately produces. Widening the
existing seam with a boolean, defaulting to the unchanged strict behavior for its two existing callers,
keeps one implementation of "run the repository's configured contract check" rather than two that could
drift apart.

**`StageContractReport` is a new record, not a reuse of `RouteReport` or `MaintainReport`** — neither
existing report carries a shape for "the change reached outside the architecture tree" or "the staged
clause is malformed", and both existing records carry fields (phase history, a declared file-scope
bound) this action has no equivalent for. A new record projecting `Primitives.DocumentAuthoringResult`
directly keeps the same "internal type stays internal, a public record projects it" discipline
`RouteReport` and `MaintainReport` already established, additive alongside both rather than a fourth
incompatible outcome shape.

**No separate declared-bound argument, unlike `maintain`** — Maintenance's bound is caller-declared
because a Maintenance work item could touch almost anywhere in the repository, and the tripwire alone
cannot say which files a caller actually intended. `stage-contract` has exactly one legal target
directory by construction (`docs/architecture/`), so there is nothing for a caller to declare that the
architecture-tree check does not already know.
