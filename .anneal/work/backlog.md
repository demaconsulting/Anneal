---
description: Wanted, not yet scheduled work items.
maintenance: Appended by intake when a work item is admitted as backlog-worthy; entries are removed or reworded directly once resolved, invalidated, or groomed.
---

# Backlog

Items here **complete** — each one finishes and stays finished. Standing properties the system must
always satisfy hold rather than complete, so they go in [constraints.md](constraints.md) instead,
where `helper` will read them during boundary work. See the Intake admission test in
`change-classification.md`.

A rejected idea is not backlog content. When grooming removes or narrows an entry, delete the
rejected reasoning outright — never leave it behind as a "Retired:"-titled entry or a trailing
parenthetical bolted onto a still-live item. The default is to write nothing further down: git
history already remembers what was tried and rejected, and re-reading it is cheaper than maintaining
a second record forever. Write a note in the relevant system document's `## Decisions` section only
when both hold — the rejected alternative is genuinely non-obvious enough that it would plausibly be
proposed again without that note, and it materially explains why the current design is shaped the way
it is. That section is pruned by the same discipline as this file, not an unbounded log either (see
`operation-dispatch.md`'s Minimalism sweep) — a decision stops earning its place once it no longer
informs the current shape of the system.

- **Document failure and recovery paths for each agent** — the user guide covers the success path and
  a general repair pass, but each agent's INCOMPLETE and FAILED outcomes deserve worked examples.
- **Mechanically check that `docs/user-guide/definition.yaml` agrees with the markdown files beside
  it** — presence and agreement is MANDATORY and deterministic, and has drifted in practice (one
  instance found and fixed by hand). No check catches this today. Weigh against the cost that "the
  **only** mechanically enforced relationship" is asserted in six files and would all need revisiting
  if this became a second one.
- **Research Roslyn-inspection-based software structure comprehension** — a compiled analyzer over
  the C# solution could mechanically derive a structural map (namespaces, classes, call relationships)
  the way `.anneal/architecture/`'s Purpose/Behavior prose does today by hand, if forced-accurate
  XmlDoc on namespaces and classes existed as the anchor. That would not replace the requirements layer
  — a promise like "this stays idempotent" is a decision no analyzer can derive by reading code, so a
  contract store would still be needed — but it could remove the hand-authorship burden from the
  comprehension half of today's architecture docs entirely, leaving them a pure requirements store.
  Exploratory only; no decision to build this yet.
  **Research finding (2026-08-13):** the premise splits into two separable claims with different
  feasibility. The **structural skeleton** — namespace/class inventory and a call/reference graph — is
  straightforwardly derivable from Roslyn's symbol model alone, with no XmlDoc dependency at all: it is
  a projection of the code, so it cannot drift from a separate claim the way prose can, and this half is
  low-risk to prototype whenever it is prioritized. The **prose** half is the harder claim, and the
  premise has it backwards: Roslyn does not need XmlDoc to derive structure, only to source a
  human-readable one-line label for each symbol, and that label is only as trustworthy as the comment
  already is. This repository already forces XmlDoc *presence* on every public member today
  (`TreatWarningsAsErrors` plus `GenerateDocumentationFile` in
  `DemaConsulting.Anneal.Toolkit.csproj`, with `CS1591` not suppressed) — but presence is not accuracy,
  and nothing mechanically checks that a summary line is still true of the code beneath it. Anchoring
  comprehension prose on XmlDoc would face exactly the same drift problem `architecture-documentation.md`'s
  Drift Anchors and `verify-change`'s advisory checks already exist to catch for hand-written
  architecture docs — it relocates the accuracy burden into `///` comments rather than removing it. Net:
  the structural-skeleton half looks genuinely promising as a future direction; the prose-anchor half
  does not remove the hand-authorship/accuracy burden the original idea hoped to retire. Still
  exploratory; no decision to build either half yet.
- **No release packaging** — `install.ps1` covers installation from a clone, and
  `.github/workflows/build.yml` covers per-repository CI, but Anneal itself does not publish an
  artifact.
- **Cache probe results by input hash** — key each model-backed operation's result on a hash of its
  inputs so a CI re-run replays the previous answer instead of re-asking. That makes a
  non-deterministic operation reproducible inside a gate, and stops the cost of re-running the gate
  scaling with the number of runs.
- **An atomic, addressable rule library is a candidate future replacement for file-level tiering** —  today, relevance to a given invocation is approximated by splitting rules across separate files
  (agent prompt, standard, skill) and loading whole files on demand, per `prompt-authoring.md`.
  `PROCESS-I2` (no verbatim rule duplication across files), `PROCESS-03` (`NoOrphanedStandards`, no
  un-loadable standard), and `PROCESS-06` (the per-invocation budget ceiling) are the curation rules
  that keep that approximation honest — they exist because the file is the unit of truth today, not
  because duplication or drift is a goal in itself. A true rule library — atomic, individually
  addressable and taggable by scope — could inject exactly the relevant subset per invocation and
  retire all three checks by making the failure they guard against structurally impossible rather than
  mechanically caught after the fact. Not designed and not scheduled: file-tiering is not yet under
  real pressure, and this is recorded as a direction, not a plan.
  **Design sketch (2026-08-13, design-only per this item's own gate):** rules would move from
  whole-file units to atomic files under a rule directory, each with front matter naming a stable id,
  the tags describing which agent/worker/situation it is relevant to, and (where it is the operative
  form of a contract clause) the clause it belongs to. A compiled selector — deterministic tag-matching,
  never model-judged, consistent with the model-never-decides-sequencing shape the rest of the process
  already uses — would assemble exactly the tagged subset for a given invocation instead of whole
  agent-prompt/standard files. This would re-express, not simply retire, the three curation checks:
  `PROCESS-I2` becomes structural rather than a phrase-similarity heuristic (two rule ids cannot
  collide, and a citation is by id rather than restatement, so `NormativeRulesHaveOneOwner`'s current
  bold-phrase scan is replaced by an exact uniqueness check); `PROCESS-03` becomes "every rule id is
  tagged into at least one reachable selection path" — the same reachability shape, finer grain;
  `PROCESS-06` gets *more* accurate rather than disappearing, computing the exact worst-case selection
  per invocation context instead of today's "largest file" proxy, which is likely a real win given
  `prompt-authoring.md` already records the current worst case at 16,419 of 20,000 tokens with thin
  headroom. Against this: the design is **not** trivial or low-risk, so per this item's own gate it
  should not be implemented now. Two concrete risks make it a Structural Change, not a Small Fix: (1)
  splitting a standard's flowing prose into small tagged atoms risks stripping exactly the *why*
  context `prompt-authoring.md`'s "When a Why Earns Its Place" section requires travel with a rule, or
  forces duplicating that context across atoms — the opposite of what the change is meant to buy; (2)
  it requires a new manifest/tag vocabulary, a new compiled selector component, and rewriting all three
  existing mechanical checks at once, which is real machinery for a corpus this repository's own
  `process.md` contract check currently measures at 9 standards against 2 agent prompts — small enough
  that file-tiering is not yet the bottleneck the backlog note already says it is not. Recommendation:
  keep as a recorded direction; revisit once the standards/prompt corpus grows enough that PROCESS-06's
  headroom is under real pressure, the same trigger condition the skill-corpus re-validation item above
  already names for a comparable question.
- **Changing the compiled-in default model candidates needs a Toolkit release** — a role now names an
  ordered list of candidates and resolves to the first the account is offered, so a single
  retirement no longer breaks every repository that has not written its own `.anneal/config.json`:
  the rearguard candidate answers instead. What still needs a Toolkit build, publish and restore is
  changing the compiled-in *candidates* — adding a newly released model to the front of a tier, or
  replacing a list whose every entry has been retired. Those repositories keep the old list until
  then. Investigated for a clean decoupling mechanism: none exists that does not cut against the
  architecture's own decisions in `model-seam.md` — a network-fetched manifest would give a
  deterministic operation a network dependency it does not have today (`model-seam.md`'s own
  *Availability is asked lazily* decision keeps that separation deliberately), and any out-of-band
  update channel is still a release process, only of something smaller than the Toolkit. The existing
  per-repository override (`.anneal/config.json`, per *Model configuration is data, not code* in
  `model-seam.md`) already solves the case a repository can act on itself; what remains is only the
  shipped default, which a Toolkit release exists to update. Sits awkwardly beside that decision but
  is not a design gap — this is an accepted release-process cost, not a build item.
- **Give the remaining single-name compiled-in defaults a rearguard** — two shipped defaults still
  name one external identifier each, so each is a dead man's switch of the kind the *No compiled-in
  default may name a single external identifier* constraint describes. The Copilot SDK's
  `is_override` tool key has no repository override at all: if it is renamed, every granted tool
  collides with a built-in and presents as a tool that is never called. The `trx` result
  format and the xUnit `Fact` and `Theory` attribute names are overridable by a discovery profile,
  but a repository that has not written one inherits the single names. Neither is held by anything but
  this note.
- **Document the process flowchart once the compiled catalog stabilizes** — the bunny-ears/toolkit-
  belly shape (two conversational agents feeding a Router that selects among a catalog of compiled
  workers) has no written home yet; `process.md`'s diagram covers only what has landed so far.
  Deferred until the catalog stops changing shape stage to stage.
- **Design the origination path before self-triggered work is enabled** — a Maintenance sweep,
  architectural review, or documentation pass the catalog proposes on its own needs a cadence guard,
  so noticing does not become scheduled busywork, and a merge gate — branch, review, and test before
  reaching `main` — since reversibility, not origin, is what makes autonomous initiative safe. Not
  scheduled; the router/catalog work lands first. **A candidate shape for the oracle's input, to weigh
  when this is picked up**: recent invocation outcomes (`dotnet anneal stats`), the last 10-20 commit
  subjects (trajectory, not just current state), and the standing constraints/product purpose
  (`.anneal/work/constraints.md` and `.anneal/governance/tenets.md` now serve this role) —
  grounding a proposed next step in what actually happened and what must keep holding, rather than in
  aspiration alone. Not a design; a seed to start from.
- **Design a way to periodically re-validate filed skills, not just accumulate them** — `skills.md`
  has a filing path (`file-skill`) and a search path (`search-skills`) but no re-validation path: once
  a lesson is filed, nothing ever checks whether it still holds, or whether several narrow skills filed
  separately over time have turned out to be instances of one broader pattern worth consolidating. This
  is a capability a human maintainer structurally lacks — accumulated notes are rarely re-walked and
  almost never pruned or merged — but an agent can mechanically enumerate every filed skill and ask both
  "does this still describe the current code and decisions?" and "do three of these now read as one
  general practice?", correcting, retiring, or consolidating entries rather than letting the corpus
  monotonically grow. Neither question can be answered by inspecting one skill in isolation, and the
  consolidation half specifically can only be recognized in hindsight, once enough separately-filed
  entries exist to compare - designing this against today's single-entry corpus would be guessing at a
  shape rather than observing one. Whether this is a `maintain`-style bounded sweep, a new operation, or
  a check folded into an existing review pass is undecided; needs its own `helper` boundary-work
  pass once real entries accumulate.
- **The same staleness risk applies to `.anneal/work/backlog.md` and `.anneal/work/active-plan.md` themselves, not only the skills
  corpus** — an item can be silently resolved as a side effect of unrelated landed work (a new
  operation absorbs what a backlog item asked for, an architectural pivot removes an assumption an
  `active-plan.md` stage depended on) and nothing re-reads the older entries against what has since
  landed. Both files are append-mostly in practice: entries get added when noticed and removed only
  when someone happens to work the exact item, never swept as a batch. The failure mode is the same
  shape as the skills one above — a human maintainer rarely re-walks a backlog after a large change,
  but an agent can mechanically diff each entry's premise against current `.anneal/architecture/` and
  recent commits. Whether this becomes one general "re-validate accumulated notes" sweep covering
  skills, `.anneal/work/backlog.md`, and `active-plan.md` together, or three narrower checks, is exactly the kind of
  question the skills item above says can only be answered once more experience of *doing* one such
  sweep exists — do not design the general version speculatively.
- **Reconsider `system-contracts.md`'s cross-cutting/shared-intermediate-node placement guidance
  against the current shallow tree** — two independent reviews split on this: the root-vs-child clause
  ownership rule is real and already exercised (`prompt-authoring.md`, `toolkit/*.md` own promises below
  their system root), but the deeper elaboration for cross-cutting promises shared by siblings at an
  intermediate machinery node may be ahead of Anneal's current shallow, mostly-flat tree. Revisit once
  the tree actually grows a case that needs it, rather than trimming blind.

## Lower priority: the prose-agent half of `install.ps1`'s payload is being retired

`install.ps1` still copies three things: `.github/agents`, `.github/skills` (the prose CLI-harness
kind, not `.anneal/skills`), and `.github/template`. Only the first two are infrastructure for the
prose-agent system the Toolkit is absorbing — investing further in *them* polishes a shrinking
surface rather than growing the compiled one. `.github/template` is no longer part of that shrinking
surface: it was cut down to just its `.anneal/` working-file skeleton, which is the future
`--onboarding` CLI's resource data (see the backlog item below), not legacy weight. These stay
recorded, but sit last on purpose; do not schedule ahead of Toolkit-facing work without asking first.

- **Write a version marker on install** — record the Anneal version into the target repository so an
  upgrade knows what it is upgrading from, and so `-Prune` can match an installed manifest rather
  than a list of retired names.
- **Back up or diff before overwriting** — give `install.ps1 -Force` a way to preserve locally edited
  standards, or at minimum report what it replaced.
- Design and build an anneal onboarding CLI process that replaces install.ps1's template-copy model by using the working-file skeletons under .github/template/.anneal/ as resource data to scaffold a new repository's .anneal/ tree directly.
