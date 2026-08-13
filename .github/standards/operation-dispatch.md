---
name: Operation Dispatch
description: Shared dispatch table, exit-code handling, work-selection categories, and failure recovery used by helper and autonomous agents.
---

# Operation Dispatch

This standard owns the mechanics both helper and autonomous modes use identically: how to invoke a
compiled path, how to read the exit code, how to pick the next item when no task is named, and how
to recover from a failure. Neither agent restates these rules; both point here.

# Shared Ground Rules

- **Never implement Intake, Change, or Maintenance directly.** Invoke the compiled path.
- **Mode classification is yours.** Read `change-classification.md` and decide `Intake`, `Change`,
  `Maintenance`, or `Migration`. Within `Change`, do **not** decide Scope or Effort yourself —
  `route` owns both.
- **Apply the Intake admission test yourself.**
- **Maintenance needs a bound before it starts.**

# Reading Exit Codes

For every direct compiled call, use this table exactly:

- **Exit 0 (Succeeded)** — report that it succeeded, using the reported files changed and summary.
- **Exit 4 (Escalated)** — a step only a person can take was named (an unapproved Migration, a
  declared bound tripped, a write outside the permitted path, an unclassifiable request). Report that
  it needs human input, quoting the reason verbatim.
- **Exit 1 (Failed) or Exit 3 (Refused)** — nothing completed. Report the failure, including what
  was tried, what was learned, and the recommended next step from the output.
- **Exit 2 (UsageError)** — the invocation was malformed (empty or missing arguments). Correct the
  command and retry once before treating a repeat failure as a defect to report.

Never retry a Failed or Escalated outcome by rephrasing and calling the same action again.

# Dispatch Table

Invoke the smallest direct path that matches what was confirmed:

| What was confirmed | Call |
| --- | --- |
| Work to build now | `dotnet anneal route "<work item, in plain text, describing the task>" [<changed-file-hint> ...]` |
| A need to record rather than build | `dotnet anneal intake "<work item>"` |
| A bounded tidy-up, with the bound agreed | `dotnet anneal maintain "<work item, in plain text, describing the bounded tidy-up>" <file-scope-hint> [<file-scope-hint> ...]` |
| A specific fix already reported | `dotnet anneal route "<finding, quoted plainly as the task>" [<changed-file-hint> ...]` |
| A contract clause to write now and implement later | boundary work in `.anneal/architecture/` using the planned-obligation form from `system-contracts.md`; no compiled action |
| Verifying a change someone has finished | run `dotnet anneal verify-change [<base-ref>]` directly |
| Asking how an action is performing — pass rates, failure trends | run `dotnet anneal stats` directly |
| Lint noise before a pull request | run `dotnet anneal lint-fix` directly |
| A report cites evidence (quoted text plus file:line) to spot-check | run `dotnet anneal verify-evidence` directly |
| About to add or state a rule and it is unclear whether it is already stated elsewhere | run `dotnet anneal probe-rule-owner` directly before writing it |

Use these rules:

- **`route`** — pass the confirmed request plainly; give file hints only when you know real files.
- **`maintain`** — file-scope hints are required and must match the declared bound verbatim.
- **planned obligations** — only use the placeholder form from `system-contracts.md` when staging was explicitly requested.
- **`intake`** — send the work item itself, not the derived bullet.

# Choosing What's Next (when no task is named)

When asked to pick the work without a named task, do not brainstorm freely. Check the following
categories in order and act on the first with a real, concrete finding, skipping a category silently
when it finds nothing. Anneal has no scheduler, so how often is read from repository signals, not a
clock.

1. **Backlog items ready to route** — an item in `.anneal/work/backlog.md` already detailed enough
   to state as a work item plainly, especially one tightly coupled to what was just landed, since
   that has the freshest context.

2. **Backlog/active-plan staleness** — re-read every entry in `.anneal/work/backlog.md` and
   `.anneal/work/active-plan.md` against `.anneal/architecture/` and recent commits, removing or
   rewording anything a landed change already resolved or invalidated. Due after every 3–5 backlog
   items have been resolved, or immediately after a large or disruptive item lands, rather than on
   any fixed commit-count schedule. This is also due immediately after a design decision made in
   conversation rejects or narrows an idea, even when nothing has landed yet — a decision with no
   corresponding commit is otherwise invisible to this check, and the entry it invalidates goes
   stale silently. Removal means deletion, never a retained "Retired:" entry or a trailing
   parenthetical recording the old reasoning next to the surviving ask.

3. **Minimalism sweep** — a bounded Maintenance pass over recently-touched systems for dead code,
   stale remarks, or duplication now that similar work has landed more than once, per
   `coding-principles.md`'s Minimalism principle. This includes each touched system's `## Decisions`
   section: prune or consolidate an entry once it stops being load-bearing for understanding the
   current design — a `## Decisions` section is a record of *why the system is shaped the way it is*,
   not an append-only log of every idea ever floated and rejected, and grows unbounded exactly like a
   backlog nobody ever grooms.

4. **Documentation/architecture drift** — spot-check `.anneal/architecture/` against the code it
   describes for anything a per-change `verify-change` would not catch in isolation.

5. **Constraint/tenet alignment** — read `.anneal/governance/tenets.md` and
   `.anneal/work/constraints.md` against recent landed work, moving a now-satisfied constraint to
   Satisfied or flagging one that no longer holds.

6. **Skill corpus re-validation** — if a skills corpus exists in this repository, sweep it (once
   it has enough entries) for staleness or consolidation candidates.

7. **Process health check** — run `dotnet anneal stats` and look for a worsening pass rate, or a
   rising cost/latency trend (token usage, duration) that is not matched by a corresponding gain in
   reliability, across the time windows the command already reports.

When a major or disruptive change has just landed, treat categories 2, 3, and 4 as a combined
sweep rather than stopping at whichever is checked first. These three are different symptoms of the
same underlying cause: a functional change moved code and behavior, and the surrounding record —
backlog entries, near-duplicate logic, and architecture prose — did not automatically follow. After
a major change all three deserve a deliberate look together, because a resolved backlog item, a
newly duplicated helper, and a stale architecture paragraph are likely to appear at the same time
and for the same reason. This combined sweep applies only in that specific situation; the ordinary
'stop at the first real finding' rule remains in place for routine day-to-day picks where no recent
major change has landed.

Prefer the earliest category with a real finding over a later one, and 'nothing here' is a
legitimate answer for every category — never invent a finding to justify moving further down the
list. These categories document shared judgement about what to do next; they do not authorize
autonomous or self-triggered work, and every item found here still goes through the appropriate
confirmation and dispatch steps before any action is taken.

# Recovering From a Failure

Read a failing report or build output first. If it already names the fix, say what you are about to
do, then run the matching direct path. Ask a question only when the missing fact is genuinely outside
the report.

The same discipline applies to any unexpected result — not only a named failure. A step that reported
success but required a manual correction afterward, an outcome that did not match what was expected,
or a gap that only became visible after the fact: each of these is a signal that the process itself
has a gap, not just an isolated incident to patch around. Before moving on, investigate why the
surprise occurred and name the gap. A one-off correction that skips the investigation leaves the same
gap open for the next run.
