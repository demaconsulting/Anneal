# Backlog

Wanted, not yet scheduled.

Items here **complete** — each one finishes and stays finished. Standing properties the system must
always satisfy hold rather than complete, so they go in [CONSTRAINTS.md](CONSTRAINTS.md) instead,
where `architecture-design` will read them. See the Intake admission test in
`change-classification.md`.

- **Write a version marker on install** — record the Anneal version into the target repository so an
  upgrade knows what it is upgrading from, so `template-sync` can report drift against a known
  baseline, and so `-Prune` can match an installed manifest rather than a list of retired names.
- **Back up or diff before overwriting** — give `install.ps1 -Force` a way to preserve locally edited
  standards, or at minimum report what it replaced. `AGENTS.md` no longer needs this: it carries no
  per-repository values, so replacing it outright is the intended upgrade path.
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
- **Reconcile the paired root and template files once** — moving to a self-hosted layout made the
  pairing visible but did not audit it. Ten root files differ from their template counterparts and
  only `.cspell.yaml` has been examined, where the divergence turned out to be real drift rather than
  an intended difference. `lint.ps1`, `fix.ps1`, `.markdownlint-cli2.yaml`, `.yamllint.yaml`,
  `.yamlfix.toml` and `.gitignore` have not been looked at. Classify each as flows-to-template,
  adopt-from-template, or deliberately divergent, and fix the first two.
- **Give `install.ps1` a fixture suite** — it is the entry point every user runs first and it now
  carries real logic: a payload table that renames `AGENTS.pristine.md` on the way in, collision
  detection before any write, `-Prune` with the retired-payload list, and a claim that the installed
  layout matches this repository's own. All of that is currently verified by hand. Model it on
  `test-check-contracts.ps1`, which exists for the same reason.
- **Decide whether a narrow class of trivial edits may run inline in `dispatch`** — Step 3 is now
  unconditional: `dispatch` always calls `apply` and never edits directly. That is the safe default,
  because `dispatch` cannot know *in advance* that a sub-agent would add nothing — `apply` selects
  standards by file type and descends the architecture tree, neither of which `dispatch` does, so
  "it added nothing" is only ever available after the work and cannot gate the decision to skip it.
  The reason to revisit it is cost: `fix.ps1` takes 11.3s and `lint.ps1` 7.5s in this repository, so
  a three-agent chain spends the large majority of its wall-clock on model reasoning in separate
  contexts rather than on tooling, and a two-line change measured 4m53s end to end. Any exemption
  must be checkable before the work rather than after; the shape discussed was mode is Maintenance
  or Change/Tier 0, the routed request already names the exact files and the exact final text, no
  file created or deleted, no test touched, nothing under `src/`, plus a requirement that `dispatch`
  record `performed inline` with its reason and run the gates itself. Weigh against the risk that
  decided the default: a self-granted exemption does not stay narrow, and unless the boundary is
  stated precisely it degrades into "whenever `dispatch` is confident", which is how a process
  quietly stops being followed.
