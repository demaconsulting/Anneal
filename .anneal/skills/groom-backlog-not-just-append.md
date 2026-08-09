---
id: groom-backlog-not-just-append
tags:
  - backlog
  - migration
  - maintenance
  - self-review
summary: BACKLOG.md and MIGRATION.md need active re-validation after landed work, not just appending
---

BACKLOG.md and MIGRATION.md are append-mostly in practice: entries are added when noticed and removed only when someone happens to work that exact item. They are the same structural class as the skills corpus (accumulated notes nobody re-walks) for the same reason: a human maintainer rarely re-reads a backlog after a large change, and nothing currently forces a re-read. An item can be silently resolved as a side effect of unrelated landed work (a new operation absorbs what it asked for, an architectural pivot removes an assumption a MIGRATION.md stage depended on) and nothing catches that.

Trigger: after landing a significant piece of work, and before self-selecting the next autonomous task, re-read the full current BACKLOG.md (and MIGRATION.md's open stages) against what has just landed and against docs/architecture/ as it now stands. Check each item for three outcomes, not just done-or-not: still valid as written; resolved or made moot by recent work and should be removed; or still valid but needs re-scoping (narrower, broader, lower priority) because the surrounding architecture shifted. Do this as a real read, not a keyword grep for item text mentioned in recent commits — resolution is often a side effect, not a direct match.

This was learned from a concrete miss: after several commits landed (Skills, verify-change, stats wiring), a full backlog re-read caught a broken sentence in one long-standing item and confirmed the probe-rule-owner/verify-evidence item's premise still held by checking the actual files, rather than assuming continuity.
