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
- **No release packaging** — `install.ps1` covers installation from a clone, and
  `.github/workflows/build.yml` covers per-repository CI, but Anneal itself does not publish an
  artifact.
- **No graduation path tooling** — the compelling story is evolving fast under Anneal and promoting a
  stabilized repository into the `Agents` process once the design stops moving. Nothing automates
  that today, and the mapping from contracts to system requirements would be the place to start.
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
- **Build a companion CLI shipped as payload** — a .NET tool invoked as `dotnet anneal <action>`,
  developed alongside the prompts and callable by agents, combining deterministic checks with
  one-shot LLM probes, structured run history, and repository-reading tools against the GitHub-hosted
  model only, with no RAG or vector memory. Copy the oracle/LLM/history design from the sibling
  project Jeeves (`docs/architecture/jeeves-core.md` there) rather than depending on it. The
  decisive argument is a structural limit of the `.agent.md` format, not a preference: a markdown
  prompt can only place text at the *start* of a context it does not control, and reliable
  structured output needs two passes — research freely, then probe separately against a declared
  schema — because a schema supplied up front is too far back in the window by the time output is
  wanted. The same limit blocks schema-at-point-of-use, deterministic verification of evidence
  locators, bounded repair loops, and routing that cannot be skipped; all need something acting *between* the
  model's turns. It cannot be settled by experiment first, because the deciding measurement requires
  the very context control the tool would provide. Measured here today: `tier-check` returned FAILED
  in 8 of 16 runs, and at least two of the 8 SUCCEEDED results were wrong, found only by hand — a
  false FAILED is loud, a false SUCCEEDED ships; `apply` returned SUCCEEDED 16 of 16 while its
  verifier failed half of what it saw; header fields across 65 reports are not uniform (`Result` 64,
  `Tier` 57, `Repairs Used` 16, `Residual` 14, one `Result` that did not parse); agents have invented
  sections outside their templates, one containing a false claim; `designer`, `docs` and two further
  worker names appear in the corpus but match no agent in `.github/agents/`; `PROCESS-05` and
  `PROCESS-I2` were refused as not mechanizable, the former because the invocation graph exists only
  as prose, a table, and a non-invocation in three separate files; `agent-metrics.ps1` regex-scrapes
  markdown to recover any of this and has already produced a plausible wrong answer; and `helper`
  routed a five-line Tier 0 change through the full `dispatch` chain that `AGENTS.md` says goes to
  `apply` alone. Design positions reached: C# records define request schema and response shape
  together so the two cannot drift; refusal ("insufficient evidence") is a legal answer and stays
  distinguishable from a pass, the same reasoning that keeps `TODO.` placeholders honest; one narrow
  question per probe, since reliability degrades with breadth far more than with difficulty
  (`README.md` line 116); oracle answers carry evidence locators the tool then verifies
  deterministically, making model output falsifiable by a program; cache by input hash so CI replays
  rather than re-asks; advisory before gating, because a flaky gate gets disabled; do not port the
  working PowerShell — `check-contracts.ps1` is cross-platform-aware and reliable, so let scripts be
  superseded job by job; and gaining `src/` and `test/` would close a self-hosting gap, since Anneal
  ships C# standards and an xUnit-shaped contract checker it has never run against itself. Weigh
  against the costs: Anneal has zero runtime and zero dependencies today and `install.ps1` merely
  copies files, which this ends; as payload every downstream repository gains a runtime, a version
  to track, and model credentials in CI, permanently repeating the problem today's Actions bump
  caused by imposing a runner floor of 2.327.1; non-determinism enters a gate downstream users are
  told to trust; it adds a second surface rather than shrinking the first, so the win only arrives
  by migrating rules out of prose one at a time, each with a clause and a test, not by running both
  indefinitely; and the opportunity cost is that `installer.md` (8 clauses) and `template.md` (7
  clauses) are entirely unverified `TODO.` placeholders while `install.ps1` is the one artifact
  downstream users actually execute.
