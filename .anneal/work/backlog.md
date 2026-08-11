---
description: Wanted, not yet scheduled work items.
maintenance: Appended by intake when a work item is admitted as backlog-worthy; entries are removed or reworded directly once resolved, invalidated, or groomed.
---

# Backlog

Items here **complete** — each one finishes and stays finished. Standing properties the system must
always satisfy hold rather than complete, so they go in [constraints.md](constraints.md) instead,
where `helper` will read them during boundary work. See the Intake admission test in
`change-classification.md`.

- **Retired: a compiled `WorkTypeRouter` ahead of `route`'s `ScopeRouter`.** Considered twice
  (once mid-session, once independently revisited) and rejected both times for the same reason:
  `route`'s oracle earns a model call because Scope is genuinely undiscovered until the repository is
  read — no one states "this touches a published contract" before checking. Mode is not that kind of
  fact. Whichever agent is holding the conversation with the requester already knows whether this is
  Intake, Change, Maintenance, or Migration, because the requester said so or it is obvious from
  context; a separate oracle re-deriving it would spend a model call to reproduce information already
  in hand, with the added risk of disagreeing with what the requester already stated. The correct
  pattern is the one `dispatch.agent.md` already used: the conversational agent picks the CLI verb
  directly (`route`/`maintain`/`stage-contract`/the future `intake`), and that stays true once the
  conversational agent itself compiles — its own model call, at the moment it decides to act, names
  the verb and arguments directly, rather than asking a second question of a second component.
- **Document failure and recovery paths for each agent** — the user guide covers the success path and
  a general repair pass, but each agent's INCOMPLETE and FAILED outcomes deserve worked examples.
- **Check the mechanical architecture rules in `dotnet anneal check-contracts`** — `level:`/`covers:`
  front matter presence and `definition.yaml` agreeing with the markdown files beside it are both
  MANDATORY, both deterministic, and both have drifted in practice. Extend the existing check rather
  than adding a second one; weigh against the cost that "the **only** mechanically enforced
  relationship" is asserted in six files and would all need revisiting. The navigation rules are not
  candidates: a child linking its parent is a SHOULD, and no rule yet forbids linking more than one
  level down.
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
- **Changing the default model candidates needs a Toolkit release** — a role now names an ordered
  list of candidates and resolves to the first the account is offered, so a single
  retirement no longer breaks every repository that has not written its own `.anneal/config.json`:
  the rearguard candidate answers instead. What still needs a Toolkit build, publish and restore is
  changing the *candidates* — adding a newly released model to the front of a tier, or replacing a
  list whose every entry has been retired. Those repositories keep the old list until then, which
  still sits awkwardly beside the *Model configuration is data, not code* decision in `toolkit.md`.
  Narrowed, not solved.
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
