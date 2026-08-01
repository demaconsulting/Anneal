# Backlog

Wanted, not yet scheduled.

Items here **complete** — each one finishes and stays finished. Standing properties the system must
always satisfy hold rather than complete, so they go in [CONSTRAINTS.md](CONSTRAINTS.md) instead,
where `architecture-design` will read them. See the Intake admission test in
`change-classification.md`.

- **Prune stale payload files on upgrade** — have `install.ps1` remove payload-owned files that no
  longer exist upstream, so a renamed agent does not linger and stay selectable.
- **Write a version marker on install** — record the Anneal version into the target repository so an
  upgrade knows what it is upgrading from, and so `template-sync` can report drift against a known
  baseline.
- **Back up or diff before overwriting** — give `install.ps1 -Force` a way to preserve a customized
  `AGENTS.md` and locally edited standards, or at minimum report what it replaced.
- **Scan `docs/architecture/` recursively in `check-contracts.ps1`** — the scan is currently
  non-recursive, so a clause in a section document below the system level is not checked.
- **Rename the level-3 "section document" concept** — "section" also means a markdown heading block,
  and the two meanings collide throughout the standards and the template.
- **Document failure and recovery paths for each agent** — the user guide covers the success path and
  a general repair pass, but each agent's INCOMPLETE and FAILED outcomes deserve worked examples.
