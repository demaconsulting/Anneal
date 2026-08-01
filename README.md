# Agents2

Agent definitions, standards, and a repository template for **evolutionary** software development.

This is a sibling to [Agents](https://github.com/demaconsulting/Agents), which targets IEC 62304
regulated development. Both produce well-documented software; they differ in what they optimize for.
`Agents` optimizes for auditability and produces a densely cross-referenced artifact set. That
cross-referencing is exactly what makes a design expensive to change afterwards. `Agents2` optimizes
for **sustained change** — designs that must keep moving after the first version ships.

> **Not a regulated-development process.** `Agents2` does not produce IEC 62304 or equivalent
> compliance evidence. Use `Agents` where that is required.

## The Central Idea

> Documentation work is triggered by **contract change**, never by file change.

Each system publishes a contract — what consumers outside it may rely on. Everything below that
boundary is free to change without documentation cost.

Three things follow from that, and together they are the whole process:

- **Progressive disclosure.** Documentation is a descent, not a pile. Each level answers a different
  question at a different altitude, and no level restates the one below it. Any change should require
  editing exactly one documentation file.
- **Contracts at system level only.** There are no requirements, design documents, or verification
  documents below system level. Interior intent lives in doc comments, next to the code it describes.
- **Two test lifecycles.** Contract tests are durable and survive refactoring untouched. Interior
  tests are disposable and are deleted without ceremony when their subject changes.

## The Architecture Tree

| Level | File | Altitude | Answers |
| --- | --- | --- | --- |
| 0 | `README.md` | 50,000 ft | What is this product and why does it exist? |
| 1 | `docs/architecture/overview.md` | 20,000 ft | What systems exist and how do they interact? |
| 2 | `docs/architecture/{system}.md` | 10,000 ft | What does this system promise, and how is it composed? |
| 3 | `docs/architecture/{system}/{section}.md` | 2,000 ft | How does this one non-obvious specific work? |

Level 3 is exceptional. Most systems have none, and the `architect` agent prunes those that stop
earning their place.

## Change Tiers

Classification happens before work starts, and it decides how much process applies:

| Tier | Trigger | Documentation | Agents |
| --- | --- | --- | --- |
| 0 | Nothing outside the system observes a difference | None | `developer` |
| 1 | A contract clause changes | `{system}.md` | `architect` → `developer` → `contract-check` |
| 2 | Systems or their interactions change | `overview.md` + affected | `architect` → `developer` → `contract-check` |

Tier 0 should be the large majority of changes. A process where it is rare has its contracts pitched
at the wrong altitude.

## Repository Layout

- **`agents/`** — the drop-in payload: `AGENTS.md`, `.github/agents/`, `.github/standards/`
- **`template/`** — the canonical repository layout and file templates

## Installation

Copy the contents of `agents/` into the root of the target repository and commit them. Then open
`AGENTS.md` and replace the `TODO` placeholders in the **Project Overview** section.

For a new repository, run `@software-architect` to interview and generate the architecture tree, or
`@template-sync Scaffold` to lay down the structure from `template/`.

## Agents

| Agent | Purpose |
| --- | --- |
| `evolve` | Entry point for any non-trivial change — classifies the tier and routes to the minimum process |
| `developer` | Core working agent — implements against contracts and applies language standards |
| `architect` | Owns the architecture tree and system contracts; updates the right level and prunes stale documents |
| `contract-check` | Verifies a completed change against its declared tier |
| `software-architect` | Interactive interview that bootstraps or re-cuts the architecture tree |
| `lint-fix` | Pre-PR sweep — loops `lint.ps1` until the repository is clean |
| `template-sync` | Audits or scaffolds repository layout against `template/` |

## Standards

| Standard | Covers |
| --- | --- |
| `architecture-documentation.md` | Level ownership, creation and deletion tests, drift anchors, size budgets |
| `system-contracts.md` | Contract structure, clause rules, identifier discipline |
| `change-tiers.md` | Tier classification and the obligations of each |
| `coding-principles.md` | Literate coding, API documentation, universal design principles |
| `testing-principles.md` | Contract versus interior test lifecycles, AAA, coverage expectations |
| `technical-documentation.md` | README and general markdown conventions |
| `csharp-language.md`, `cpp-language.md` | Language-specific implementation standards |
| `csharp-testing.md`, `cpp-testing.md` | Language-specific testing standards |

## What Is Deliberately Absent

Compared to `Agents`, this process has no per-unit requirements, no per-unit or per-subsystem design
documents, no verification design documents, no SysML2 model, no formal review tracking, and no
multi-retry orchestration state machine. Each was removed because its cost is paid on **every**
subsequent change, and evolutionary work pays that cost repeatedly.

## License

[MIT](LICENSE)
