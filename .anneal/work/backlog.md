# Backlog

Wanted, not yet scheduled.

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
- **Rename the level-3 "section document" concept** — "section" also means a markdown heading block,
  and the two meanings collide throughout the standards and the template.
- **Document failure and recovery paths for each agent** — the user guide covers the success path and
  a general repair pass, but each agent's INCOMPLETE and FAILED outcomes deserve worked examples.
- **Check sub-agent handoff coverage mechanically** — parse each agent's `**Result**` values and
  verify every calling agent handles all of them. The one defect class in the prompt files that a
  script can catch; a missing INCOMPLETE branch in `dispatch` survived five manual review rounds.
- **Check the mechanical architecture rules in `dotnet anneal check-contracts`** — `level:`/`covers:`
  front matter presence and `definition.yaml` agreeing with the markdown files beside it are both
  MANDATORY, both deterministic, and both have drifted in practice. Extend the existing check rather
  than adding a second one; weigh against the cost that "the **only** mechanically enforced
  relationship" is asserted in six files and would all need revisiting. The navigation rules are not
  candidates: a child linking its parent is a SHOULD, and no rule yet forbids linking more than one
  level down.
- **Detect drift between paired root and template files** — Anneal now keeps most root files twice,
  and the `AGENTS.md` pair is checked mechanically because it is the one pair that must be exactly
  equal. The rest legitimately differ, so identity comparison is useless and an allowlist of permitted
  divergences would not help either: `.cspell.yaml` would sit on that list, and the drift found in
  it was a missing policy comment inside a file that was supposed to differ. Until something
  better exists, the **Template Stewardship** section of `AGENTS.md` carries this as prose, and prose
  is exactly what failed the first time.
- **Update `AGENTS.md` downstream without a reinstall** — `template-sync` Patch inserts missing
  sections but cannot update content that changed inside a section that already exists, which is most
  process edits. `AGENTS.md` now carries no customization, so `install.ps1 -Force` replaces it safely
  and is the recommended route; a `template-sync` mode that refreshes an uncustomized mapped file
  wholesale would remove the need to know that.
- **Reconcile the paired root and template files once** — moving to a self-hosted layout made the
  pairing visible but did not audit it. `.cspell.yaml` and `.markdownlint-cli2.yaml` have been examined;
  the former's divergence was real drift, the latter's was the template already ahead (`MD013: false`),
  now adopted. `lint.ps1`, `fix.ps1`, `.yamllint.yaml`, `.yamlfix.toml` and `.gitignore` remain
  unexamined. Classify each as flows-to-template, adopt-from-template, or deliberately divergent, and fix
  the first two.
- **No release packaging** — `install.ps1` covers installation from a clone, and
  `.github/workflows/build.yml` covers per-repository CI, but Anneal itself does not publish an
  artifact.
- **Decide whether a narrow class of trivial edits may run inline in `dispatch`** — Step 3 is now
  unconditional: `dispatch` always calls `apply` and never edits directly. That is the safe default,
  because `dispatch` cannot know *in advance* that a sub-agent would add nothing — `apply` selects
  standards by file type and descends the architecture tree, neither of which `dispatch` does, so
  "it added nothing" is only ever available after the work and cannot gate the decision to skip it.
  The reason to revisit it is cost: `fix.ps1` takes 11.3s and `lint.ps1` 7.5s in this repository, so
  a three-agent chain spends the large majority of its wall-clock on model reasoning in separate
  contexts rather than on tooling, and a two-line change measured 4m53s end to end; `helper` also
  routed a five-line Small Fix change through the full `dispatch` chain that `AGENTS.md` says goes to
  `apply` alone. Any exemption must be checkable before the work rather than after; the shape
  discussed was mode is Maintenance or Change/Small Fix, the routed request already names the exact
  files and the exact final text, no file created or deleted, no test touched, nothing under `src/`,
  plus a requirement that `dispatch` record `performed inline` with its reason and run the gates
  itself. Weigh against the risk that decided the default in the first place: a self-granted exemption
  does not stay narrow, and unless the boundary is stated precisely it degrades into "whenever
  `dispatch` is confident", which is how a process quietly stops being followed.
- **Cache probe results by input hash** — key each model-backed operation's result on a hash of its
  inputs so a CI re-run replays the previous answer instead of re-asking. That makes a
  non-deterministic operation reproducible inside a gate, and stops the cost of re-running the gate
  scaling with the number of runs.
- **Escalated or out-of-scope worker diffs should be preserved structurally, not left as bare
  working-tree state** — when `route` or a dispatched worker escalates or produces changes beyond
  what was asked, the uncommitted diff currently just sits in the working tree for a human or `helper`
  to triage by eye. That is a real incident risk: reverting or deleting part of it by hand, on an
  unverified assumption about which parts are in scope, can permanently discard real work with no
  recovery path, since nothing was ever committed or staged. `route`/`dispatch` (or the calling agent)
  should save a snapshot — a stash, or a patch file under `.agent-logs/` or `artifacts/` — before any
  human-directed partial revert touches an escalated diff, so a wrong call is recoverable rather than
  destructive.
- **Changing the default model candidates needs a Toolkit release** — a role now names an ordered
  list of candidates and resolves to the first the account is offered, so a single
  retirement no longer breaks every repository that has not written its own `.anneal/config.json`:
  the rearguard candidate answers instead. What still needs a Toolkit build, publish and restore is
  changing the *candidates* — adding a newly released model to the front of a tier, or replacing a
  list whose every entry has been retired. Those repositories keep the old list until then, which
  still sits awkwardly beside the *Model configuration is data, not code* decision in `toolkit.md`.
  Narrowed, not solved.
- **Retire `agent-metrics.ps1` once its corpus is empty** — it scrapes `.agent-logs/*.md` prose
  reports with regular expressions, a pattern `toolkit/runtime.md` already names as the mistake
  `TOOLKIT-08`'s structured `InvocationRecord` exists to avoid. It is not gated by CI, lint, or
  build, and has no recorded retirement condition anywhere, unlike `lint-fix.agent.md`'s. The
  condition: once every remaining prose agent is absorbed into a compiled operation, nothing writes
  `.agent-logs/*.md` anymore and `dotnet anneal stats` covers the whole corpus `InvocationRecord`
  already carries. Delete the script and this backlog item together at that point.
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

## Lower priority: `install.ps1`'s own payload is being retired

`install.ps1`'s entire payload — `.github/agents`, `.github/skills` (the prose CLI-harness kind, not
`.anneal/skills`), `.github/template`, and `AGENTS.md` — is infrastructure for the prose-agent system
the Toolkit is absorbing. Investing further in the installer polishes a shrinking surface rather than
growing the compiled one. These stay recorded, but sit last on purpose; do not schedule ahead of
Toolkit-facing work without asking first.

- **Write a version marker on install** — record the Anneal version into the target repository so an
  upgrade knows what it is upgrading from, so `template-sync` can report drift against a known
  baseline, and so `-Prune` can match an installed manifest rather than a list of retired names.
- **Back up or diff before overwriting** — give `install.ps1 -Force` a way to preserve locally edited
  standards, or at minimum report what it replaced. `AGENTS.md` no longer needs this: it carries no
  per-repository values, so replacing it outright is the intended upgrade path.
