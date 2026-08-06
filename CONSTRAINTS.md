# Constraints

Conditions this architecture must satisfy. `architecture-design` reads this before re-cutting system
boundaries, and `architecture-update` reads it before a Tier 2 change.

A satisfied constraint is not finished business — it is the reason the current shape is the shape it
is, and the guard rail that stops the next re-cut from silently regressing it. Entries are never
deleted for being met. Remove one only when the condition stops being **required**, which is a
decision, not bookkeeping.

An entry belongs here only if it **holds** rather than **completes** — a standing property like
"supports .NET Standard 2.0", not a unit of work like "add a `--version` flag". The Intake admission
test, the rule on who may admit an entry here, and the rule on what an entry may say all live in
`change-classification.md`. Work that finishes goes in [BACKLOG.md](BACKLOG.md).

A belief the world could prove wrong is an **assumption** rather than a constraint; those live in the
Assumptions section of [README.md](README.md).

## Satisfied

Conditions the current design meets. Breaking one is a regression, not a trade-off to be made
quietly.

- **Installation is by a provided script** — a target repository adopts the process by running
  `install.ps1`, not by cloning files into place, hand-editing a project file, or following a
  multi-step manual setup.
- **Every rule has exactly one owning file** — this is `PROCESS-I2` in
  [process.md](docs/architecture/process.md); that clause is the full statement, and other files
  reference it rather than restating it here.
- **Agent prompts and standards stay within a per-invocation context budget** — the worst-case prompt
  load stays under the ceiling declared and counted in
  [prompt-authoring.md](docs/architecture/process/prompt-authoring.md).
- **A removed or renamed Anneal-owned agent must stop being selectable after upgrade** — a target
  repository updated to a payload that no longer ships an agent must not continue offering that
  retired agent as an invocation target.
- **Installer deletion requires explicit confirmation** — Anneal must not delete a target repository
  file during installation or upgrade unless the user first confirms that file's deletion.
- **The template must stay valid for a C# product repository regardless of Anneal's own needs** —
  this is `TEMPLATE-I1` in [template.md](docs/architecture/template.md); that clause is the full
  statement, and `AGENTS.md` § Template Stewardship references it rather than restating it here.
- **An installed payload must be identifiable by version** — a payload states which Anneal version it
  was built from, so what an upgrade is upgrading from is read rather than inferred from the payload's
  contents, and a record a run leaves behind can be attributed to the version that produced it.
- **Every commit leaves Anneal able to develop Anneal** — Anneal is built by the generation of
  itself that is currently installed, so a change that breaks the agents, scripts or tool doing the
  work halts development rather than advancing it. This holds after every commit, not merely at a
  stage boundary, and it is what bounds the content of a migration step;
  [MIGRATION.md](MIGRATION.md) names it as a step invariant rather than restating the condition.
- **The basis of a model-backed judgement is captured at the time or lost** — a verdict on unchanged
  input is expected to be stable, but the reasoning behind it, the data the model was shown and the
  exact question it was asked are not recoverable by re-running. Without them a wrong verdict cannot
  be diagnosed as a bad question rather than a bad answer. `TOOLKIT-11` absorbed this at stage S2 of
  [MIGRATION.md](MIGRATION.md).

## Not Yet Satisfied

Conditions the current decomposition gets in the way of. These are the pressure that argues for a
re-cut. An entry moves up to **Satisfied** when a change absorbs it.

- **Evidence of agent behavior must survive local report cleanup** — today `.agent-logs/` is the only
  record of which agents ran, what verdicts they returned, and where they failed; it is gitignored, so
  fresh clones and CI cannot audit that behavior, but automation must not delete it unless a
  user-admitted replacement records the same facts.
- **Upgrading an installed payload must not silently destroy local customization** — `install.ps1
  -Force` overwrites every payload-owned file with no backup and no diff, including a customized
  `AGENTS.md` and any locally edited standard.
- **A judging agent must show the basis for its verdict before stating it** — no agent prompt in the
  payload obliges one to, and `tier-check`'s report template places `Required Fixes` ahead of every
  section carrying what that verdict rests on, so a universally-quantified negative about a file the
  agent never opened reads exactly like a checked finding. The paired belief is in the Assumptions
  section of [README.md](README.md); how a prompt demands the basis is owned by
  [prompt-authoring.md](docs/architecture/process/prompt-authoring.md). `TOOLKIT-03` absorbs the
  mechanical half at stage S1a of [MIGRATION.md](MIGRATION.md) — whether a cited quote really is at the
  line named — leaving the prompt obligation to state the basis still outstanding.
- **No compiled-in default may name a single external identifier whose retirement breaks every
  repository that has not overridden it** — a default naming one provider-side name works until that
  name is retired, and then fails everywhere at once with only a release to fix it.
- **A repository's own pinned model names are not guaranteed to stay valid** — `.anneal/config.json`
  names specific models by string, discovered at stage S1b of [MIGRATION.md](MIGRATION.md) when the
  shipped defaults had already rotted before their first use. This recurs per repository and cannot
  be fixed once: a vendor retiring a name breaks that repository's Toolkit invocations with nothing in
  the process noticing or repairing it, independently of whether Anneal's own compiled defaults (the
  entry above) have been addressed.
