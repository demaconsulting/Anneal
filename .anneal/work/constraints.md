---
description: Conditions this architecture must satisfy; read by helper before re-cutting system boundaries and by route's Structural Change worker before a Structural Change.
maintenance: Appended by admit-constraint after the user approves exact bullet wording; entries are never deleted for being satisfied, only moved between sections.
---

# Constraints

Conditions this architecture must satisfy.

A satisfied constraint is not finished business — it is the reason the current shape is the shape it
is, and the guard rail that stops the next re-cut from silently regressing it. Entries are never
deleted for being met. Remove one only when the condition stops being **required**, which is a
decision, not bookkeeping.

An entry belongs here only if it **holds** rather than **completes** — a standing property like
"supports .NET Standard 2.0", not a unit of work like "add a `--version` flag". The Intake admission
test, the rule on who may admit an entry here, and the rule on what an entry may say all live in
`change-classification.md`. Work that finishes goes in [backlog.md](backlog.md).

A belief the world could prove wrong is an **assumption** rather than a constraint; those live in
[assumptions.md](../governance/assumptions.md).

## Satisfied

Conditions the current design meets. Breaking one is a regression, not a trade-off to be made
quietly.

- **Installation is by a provided script** — a target repository adopts the process by running a
  single provided command, not by cloning files into place, hand-editing a project file, or following
  a multi-step manual setup. Today that command is `install.ps1`; a future `dotnet tool install` plus
  `dotnet anneal --onboarding` (see "Not Yet Satisfied" below) satisfies this identically — the
  constraint is the single-command property, not the script.
- **Every rule has exactly one owning file** — this is `PROCESS-I2` in
  [process.md](../architecture/process.md); that clause is the full statement, and other files
  reference it rather than restating it here.
- **Agent prompts and standards stay within a per-invocation context budget** — the worst-case prompt
  load stays under the ceiling declared and counted in
  [prompt-authoring.md](../architecture/process/prompt-authoring.md).
- **A removed or renamed Anneal-owned agent must stop being selectable after upgrade** — while
  installation works by copying agent files into a target repository (today's `install.ps1`), a
  payload that no longer ships an agent must not leave that repository still offering it as an
  invocation target; `-Prune` is the current mechanism. This condition is scoped to file-copying
  installation: once distribution moves to `dotnet tool install` plus a `dotnet anneal --onboarding`
  step (see "Not Yet Satisfied" below), no file is ever copied into a target repository to go stale,
  and the failure this guards against stops being possible rather than needing a mechanism to catch
  it. Retire this entry — do not look for a fancier replacement mechanism — when that transition
  lands.
- **Installer deletion requires explicit confirmation** — Anneal must not delete a target repository
  file during installation or upgrade unless the user first confirms that file's deletion; `-Prune`'s
  per-file confirmation prompt is the current mechanism. Like the entry above, this is scoped to
  today's file-copying `install.ps1`: once distribution moves to `dotnet tool install` plus a
  `dotnet anneal --onboarding` step, there is no target-repository file being deleted during install or
  upgrade to confirm. Retire this entry when that transition lands rather than carrying the
  confirmation prompt forward into a mechanism that has nothing left to delete.
- **The template must stay valid for a C# product repository regardless of Anneal's own needs** —
  this is `TEMPLATE-I1` in [template.md](../architecture/template.md); that clause is the full
  statement.
- **An installed payload must be identifiable by version** — a payload states which Anneal version it
  was built from, so what an upgrade is upgrading from is read rather than inferred from the payload's
  contents, and a record a run leaves behind can be attributed to the version that produced it.
- **Every commit leaves Anneal able to develop Anneal** — Anneal is built by the generation of
  itself that is currently installed, so a change that breaks the agents, scripts or tool doing the
  work halts development rather than advancing it. This holds after every commit, not merely at a
  stage boundary, and it is what bounds the content of a migration step;
  [active-plan.md](active-plan.md) names it as a step invariant rather than restating the condition.
- **The basis of a model-backed judgement is captured at the time or lost** — a verdict on unchanged
  input is expected to be stable, but the reasoning behind it, the data the model was shown and the
  exact question it was asked are not recoverable by re-running. Without them a wrong verdict cannot
  be diagnosed as a bad question rather than a bad answer. `TOOLKIT-11` absorbs this.
- **Evidence of agent behavior persists across sessions without auto-erasure** — `.anneal/logs/records/`
  and `.anneal/logs/transcripts/` accumulate every invocation and model interaction; nothing in the
  toolchain ever clears them, and `dotnet anneal stats` reads the record store directly rather than
  scraping prose reports. This does not require the evidence be committed or visible to a fresh
  clone — only that a running repository's own history is never silently discarded.
- A back-end operation never blocks waiting on interactive input mid-run -- every operation resolves to a terminal report. Ambiguity is returned as data (an Unknowns list) for whatever front-end is calling it to resolve and re-invoke with; it is never surfaced as a prompt the operation itself waits on. This holds regardless of whether front-end and back-end share an executable: the dependency direction is fixed -- back-end has no dependency on a front-end being present to complete an operation.

## Not Yet Satisfied

Conditions the current decomposition gets in the way of. These are the pressure that argues for a
re-cut. An entry moves up to **Satisfied** when a change absorbs it.

- **`install.ps1` installs the Toolkit as a real dotnet tool dependency and runs an interactive
  onboarding step** — the current installer copies payload files but does not register the Toolkit
  as a dotnet tool in the target repository's tool manifest, and runs no guided first-run step.
  This is a deliberately deferred future direction, not being built now.

- **Upgrading an installed payload must not silently destroy local customization** — `install.ps1
  -Force` overwrites every payload-owned file with no backup and no diff, including a locally edited
  standard.
- **A judging agent must show the basis for its verdict before stating it** — the original violation
  was `dispatch.agent.md` (now retired), whose report template placed `Result` ahead of every section
  carrying what that verdict rested on. The authoring obligation is now stated as a mandatory rule:
  every judging prompt must require basis before conclusion, per *Evidence before verdict* in
  [prompt-authoring.md](../architecture/process/prompt-authoring.md). The paired belief is in
  [assumptions.md](../governance/assumptions.md). `TOOLKIT-03` absorbs the mechanical half —
  whether a cited quote really is at the line named — leaving the prompt obligation to demand the
  basis still outstanding.
- **No Anneal-shipped default may be a single external-name dead man's switch** — if a compiled-in
  provider, tool, or framework identifier is retired or renamed, a repository still using Anneal's
  shipped default must degrade or redirect rather than every non-overriding repository failing at once
  until a Toolkit release lands.
- **Repository-owned model pins remain a per-repository staleness risk independent of the entry above**
  — `.anneal/config.json` can name candidates that later stop being offered even when Anneal's shipped
  defaults are healthy. That failure is local to the repository and is repaired by updating that
  repository's own configuration, never by an Anneal release: the two entries share a mitigation
  mechanism (an ordered candidate list, resolved to the first the account is offered) but not an owner
  or a fix path, so both are kept.
- **A prose claim about current behavior, living outside any `covers:`-matched architecture document,
  is not checked by either verification path** — narrowed from its original scope: a `covers:`-matched
  architecture document's own claims about the code it names are now checked mechanically for routed
  Change work by `GeneralWorker`'s documentation/verifier path, and for Maintenance by the explicit
  finish-time agreement gate (`TOOLKIT-57`). What
  remains open is a claim that lives somewhere `covers:` does not reach — a routing table row, a
  diagram edge, a cross-reference in a standard — where no script parses it against the behavior it
  names, and inspection only catches it if the change under review happens to touch that sentence.
  Found twice in one session (a routing table and a diagram both went stale when `dispatch` was
  rewritten at S11, undetected by `scope-check` at the time) before a later, unrelated review caught
  both by hand.
- **The user's admission is required before an agent may append to `.anneal/work/constraints.md` or
  any file under `.anneal/governance/`** — the full rule, including the deterministic `admit-constraint`
  action once wording is confirmed, is owned by
  [change-classification.md](../../.github/standards/change-classification.md); this entry exists only
  so a re-cut reading this file does not miss that the write path itself is a standing property, not
  merely a procedural note.
