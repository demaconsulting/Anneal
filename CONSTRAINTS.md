# Constraints

Conditions this architecture must satisfy. `architecture-design` reads this before re-cutting system
boundaries, and `architecture-update` reads it before a Tier 2 change.

A satisfied constraint is not finished business — it is the reason the current shape is the shape it
is, and the guard rail that stops the next re-cut from silently regressing it. Entries are never
deleted for being met. Remove one only when the condition stops being **required**, which is a
decision, not bookkeeping.

An entry belongs here only if it **holds** rather than **completes** — a standing property like
"supports .NET Standard 2.0", not a unit of work like "add a `--version` flag". See the Intake
admission test in `change-classification.md`. Work that finishes goes in [BACKLOG.md](BACKLOG.md).

A belief the world could prove wrong is an **assumption** rather than a constraint; those live in the
Assumptions section of [README.md](README.md).

## Satisfied

Conditions the current design meets. Breaking one is a regression, not a trade-off to be made
quietly.

- **The payload installs by file copy alone** — no build step, no package manager, and no runtime
  dependency added to the target repository. `install.ps1` copies directories, which is why renaming
  an agent needs no script change.
- **Every rule has exactly one owning file** — other files point at it rather than restating it. A
  rule stated twice drifts, and the drift is silent.
- **Agent prompts and standards stay within a per-invocation context budget** — `AGENTS.md` loads on
  every invocation, so it carries routing and defers detail to standards loaded on demand. The ceiling
  and the method it is counted by are owned by
  [prompt-authoring.md](docs/architecture/process/prompt-authoring.md).
- **The process is enforceable by one mechanical check** — `check-contracts.ps1` is the only thing
  that must pass; everything else is judgement recorded in a report.
- **A removed or renamed agent must stop being selectable in a target repository** — `install.ps1
  -Prune` lists payload-directory files the payload does not provide, separates the ones
  `retired-payload.txt` names as ours from ones the repository added itself, and deletes only what
  the user confirms.
- **The template must stay valid for a C# product repository regardless of Anneal's own needs** —
  Anneal has no `src/`, no solution and no xUnit tests, so its root `lint.ps1`, `fix.ps1` and absent
  `build.ps1` legitimately differ from their template counterparts. Syncing them to match Anneal
  would break every downstream repository. Divergence in this direction is by design, not drift.

## Not Yet Satisfied

Conditions the current decomposition gets in the way of. These are the pressure that argues for a
re-cut. An entry moves up to **Satisfied** when a change absorbs it.

- **Upgrading an installed payload must not silently destroy local customization** — `install.ps1
  -Force` overwrites every payload-owned file with no backup and no diff, including a customized
  `AGENTS.md` and any locally edited standard.
- **An installed payload must be identifiable by version** — nothing written into a target repository
  records which Anneal version produced it, so an upgrade cannot tell what it is upgrading from.
- **A judging agent must show the basis for its verdict before stating it** — no agent prompt in the
  payload obliges one to, and `tier-check`'s report template places `Required Fixes` ahead of every
  section carrying what that verdict rests on, so a universally-quantified negative about a file the
  agent never opened reads exactly like a checked finding. The paired belief is in the Assumptions
  section of [README.md](README.md); how a prompt demands the basis is owned by
  [prompt-authoring.md](docs/architecture/process/prompt-authoring.md).
