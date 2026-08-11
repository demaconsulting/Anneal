---
level: system
covers:
  - .github/template/.anneal/**
---

[← Architecture Overview](./overview.md)

# Template

Template ships the `.anneal/` working-file skeleton a new repository needs to start following the
Anneal process. It is a resource for a not-yet-built onboarding CLI, not a full repository layout.

The shipped tree contains governance scaffolds under `.anneal/governance/`, architecture scaffolds
under `.anneal/architecture/`, and work scaffolds under `.anneal/work/`. Nothing else is shipped:
scripts, configuration files, source trees, and document pipelines that the template once carried
have been retired; their guidance lives in `docs/user-guide/repository-scripts.md`.

The boundary against [Process](./process.md) is unchanged: **Process owns what agent prompts and
standards say; Template owns that the `.anneal/` skeleton, including canonical locations, is present
in the shipped tree.**

## Contract

### Provides

- **TEMPLATE-06** — Ships template documents whose placeholders and directives are
  machine-recognizable, so an agent filling one in can confirm none remain.
  *Verified by:* `DirectivesAreRecognizable`

### Requires

- **[Process](./process.md)** — the standards the skeleton expects to be installed alongside the
  agent prompts.

### Invariants

None.

## Composition

The template is a single part: the `.anneal/` working-file skeleton. It contains:

- `.anneal/governance/` — `assumptions.md` and `tenets.md` scaffolds
- `.anneal/architecture/` — `overview.md` and `system-name.md` scaffolds
- `.anneal/work/` — `active-plan.md`, `backlog.md`, and `constraints.md` scaffolds

Each file ships with `TEMPLATE-DIRECTIVE` blocks that instruct an agent filling it in, then are
deleted. Shipping prose examples was rejected because an example that is merely edited leaves its own
assumptions behind, and nothing marks where they were.

## Decisions

**Payload reduced to the `.anneal/` skeleton** — scripts (`build.ps1`, `lint.ps1`, `fix.ps1`),
configuration files, source trees, document pipelines, and the `repository-map.md` that once
described them were retired. Their guidance is preserved in `docs/user-guide/repository-scripts.md`.
The retained skeleton is the resource a future onboarding CLI will read to scaffold a new
repository's working files.
