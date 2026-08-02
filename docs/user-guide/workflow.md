# Workflow

How work actually flows through this process, and the ways it goes wrong.

## The Questions That Decide Everything

Before any work starts, two questions in order.

> **What kind of work is this?**

Recording a need is **Intake**. Tidying with no requested outcome is **Maintenance**. An approved
restructure is **Migration**. Everything else is a **Change**, and only Changes carry a tier.

> **Does this change what the contract promises?**

If no, it is Tier 0 and no documentation moves — including a bug fix that makes the code finally do
what the contract already promised. If yes, ask whether the set of systems or their interactions
change: no means Tier 1, yes means Tier 2.

That is the entire routing decision. `change-classification.md` holds the authoritative rules; this
page is about applying them.

## What Each Mode and Tier Costs

| Mode / Tier | Agents | Documentation | Typical wall time |
| --- | --- | --- | --- |
| Intake | `dispatch` | One bullet in `BACKLOG.md`, `CONSTRAINTS.md`, or README assumptions | Seconds |
| Maintenance | `apply`, bounded | None | Minutes |
| Change, Tier 0 | `apply` | None | Minutes |
| Change, Tier 1 | `architecture-update` → `apply` → `tier-check` | One system document | Tens of minutes |
| Change, Tier 2 | `architecture-update` → `apply` → `tier-check` | Overview plus affected systems | Longer |
| Migration | `architecture-design` → staged work | The tree, plus `MIGRATION.md` | Days, staged |

The cost difference is the point. It is why classification happens first and why rounding up "to be
safe" is a real cost, not free insurance.

## Tier 0 Should Be Most Changes

If Tier 0 is rare in your repository, something is wrong — almost always that contracts are pitched
too low. A contract clause describing a helper method, an internal class, or a specific error message
format turns ordinary refactoring into Tier 1 work.

Symptoms of contracts pitched too low:

- Refactors keep breaking contract tests
- The contract has more than forty clauses
- Clauses mention types or method names
- Every change seems to need the `architecture-update`

The fix is to raise the contract to the boundary a real consumer sees, not to work around it.

## Order Matters on Tier 1 and 2

The contract is written **before** the implementation. This is not bureaucracy — a contract written
afterwards is a description of whatever got built, and it applies no design pressure at all. It is
also the single easiest rule to violate accidentally, because writing the code first feels faster.

`tier-check` looks for this specifically. Clauses that read like a summary of the diff are a
failure, even when everything passes.

## Raising a Tier Mid-Flight

Implementation regularly reveals that a change is bigger than it looked. When that happens:

- **Stop.** Do not finish the implementation and document afterwards.
- Restate the tier and route through `architecture-update` before continuing.

Tiers may be raised at any time. They may never be silently lowered.

## Do Not Split to Stay Low

Landing a contract change as two Tier 0 commits produces an undocumented breaking change and defeats
the whole mechanism. If a change needs a contract update, it gets one — splitting the work across
commits does not make it Tier 0.

## The Repair Pass

`dispatch` gets at most two repairs, and they are not interchangeable: **one documentation repair**
through `architecture-update`, and **one code repair** through `apply`. A documentation finding —
wrong tier, missing clause, stale tree — routes to the agent that owns `docs/architecture/` and then
continues into implementation, because a corrected clause still needs a test. Neither is a planning
phase.

A repair that does not clear the finding it targeted is information: the change was misunderstood at
the start. `dispatch` stops there rather than spending the other repair. The old process allowed three
retries through a full replan cycle, and that loop is where most of its wall-clock time went.

## Worked Examples

| Request | Tier | Reasoning |
| --- | --- | --- |
| "Speed up the parser" | 0 | Same outputs, nobody outside notices |
| "Split this 900-line class up" | 0 | Pure interior restructuring |
| "Fix the crash on empty input" | 0 | Contract already promised it would not crash |
| "Add a `--verbose` flag" | 1 | New consumer-visible behavior needs a clause |
| "Reject inputs over 10 MB" | 1 | Narrows an existing clause; breaking |
| "Return 404 instead of 400 for missing records" | 1 | Consumers branch on the code |
| "Move indexing into a background worker" | 2 | Process boundary changes |
| "Split Storage into Storage and Cache" | 2 | System inventory changes |

## Tests Follow the Tier

- **Tier 0** — interior tests are yours to rewrite or delete. Contract tests must pass **unchanged**.
  If a Tier 0 change breaks a contract test, either the tier was wrong or you have found a defect.
- **Tier 1 and 2** — every new or changed clause needs a contract test, using the exact name written
  in the clause.

Deleting an interior test whose subject is gone needs no justification. Deleting a contract test
does.

## Reviewing a Pull Request

Four questions, in order:

1. **Is the declared tier honest?** Check whether anything outside the system observes a difference.
2. **For Tier 1 and 2, did the contract come first?** Clauses that merely narrate the diff are the
   tell.
3. **Did documentation land at exactly one level?** An edit to `overview.md` restating something a
   system document already says is an ownership violation, and it will cost you on every future
   change.
4. **Was anything pruned?** Tier 1 and 2 changes include a prune check. A prune that never deletes
   anything over many changes is not being applied honestly.

`check-contracts.ps1` already proved the clause-to-test links, so do not re-check those by hand.

## When `dispatch` Is Not What Runs

`helper` routes past `dispatch` entirely when there is nothing to classify:

- The change is trivial and obviously interior — it goes straight to `apply`.
- You are only fixing lint — `lint-fix`.
- You are reshaping system boundaries — `helper` hands you to `architecture-design`, because that is a
  design conversation rather than a change, and it needs you in the room.
