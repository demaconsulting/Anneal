# Backlog

Wanted, not yet scheduled.

Items here **complete** — each one finishes and stays finished. Standing properties the system must
always satisfy hold rather than complete, so they go in [CONSTRAINTS.md](CONSTRAINTS.md) instead,
where `architecture-design` will read them. See the Intake admission test in
`change-classification.md`.

- **Write a version marker on install** — record the Anneal version into the target repository so an
  upgrade knows what it is upgrading from, so `template-sync` can report drift against a known
  baseline, and so `-Prune` can match an installed manifest rather than a list of retired names.
- **Back up or diff before overwriting** — give `install.ps1 -Force` a way to preserve a customized
  `AGENTS.md` and locally edited standards, or at minimum report what it replaced.
- **Scan `docs/architecture/` recursively in `check-contracts.ps1`** — the scan is currently
  non-recursive, so a clause in a section document below the system level is not checked.
- **Rename the level-3 "section document" concept** — "section" also means a markdown heading block,
  and the two meanings collide throughout the standards and the template.
- **Document failure and recovery paths for each agent** — the user guide covers the success path and
  a general repair pass, but each agent's INCOMPLETE and FAILED outcomes deserve worked examples.
- **Check sub-agent handoff coverage mechanically** — parse each agent's `**Result**` values and
  verify every calling agent handles all of them. The one defect class in the prompt files that a
  script can catch; a missing INCOMPLETE branch in `dispatch` survived five manual review rounds.
- **Check the mechanical architecture rules in `check-contracts.ps1`** — `level:`/`covers:` front
  matter presence and `definition.yaml` agreeing with the markdown files beside it are both MANDATORY,
  both deterministic, and both have drifted in practice. Extend the existing check rather than adding a
  second script; weigh against the cost that "the **only** mechanically enforced relationship" is
  asserted in six files and would all need revisiting. The navigation rules are not candidates: a child
  linking its parent is a SHOULD, and no rule yet forbids linking more than one level down.
- **Decide whether Anneal should host its own architecture tree** — it would demonstrate the process
  on itself and let CI run `check-contracts.ps1` against a real repository rather than only fixtures,
  but `check-contracts.ps1` assumes `*.cs`, xUnit attributes and TRX, so it is a tooling change as
  much as a documentation one. Weigh against documenting something because it ought to exist.
- **Detect drift between paired root and template files** — Anneal now keeps most root files twice,
  and the `AGENTS.md` pair is checked mechanically because it is the one pair that must be exactly
  equal. The rest legitimately differ, so identity comparison is useless and an allowlist of permitted
  divergences would not help either: `.cspell.yaml` would sit on that list, and the drift actually
  found in it was a missing policy comment inside a file that was supposed to differ. Until something
  better exists, the **Template Stewardship** section of `AGENTS.md` carries this as prose, and prose
  is exactly what failed the first time.
- **Update `AGENTS.md` downstream without a reinstall** — `template-sync` Patch inserts missing
  sections but cannot update content that changed inside a section that already exists, which is most
  process edits. `AGENTS.md` now carries no customization, so `install.ps1 -Force` replaces it safely
  and is the recommended route; a `template-sync` mode that refreshes an uncustomized mapped file
  wholesale would remove the need to know that.
