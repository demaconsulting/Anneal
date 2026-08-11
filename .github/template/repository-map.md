# Repository Map

Authoritative list of files the template provides.

Files present in a repository but absent from this map are **not** deviations — repositories are
expected to contain content the template does not describe.

`.github/agents/`, `.github/skills/`, and `.github/standards/` are installed from the payload rather
than described by this map. They are deliberately absent from it.

## Placeholders

| Placeholder | Casing | Used in |
| --- | --- | --- |
| `{system-name}` | kebab-case | documentation paths and file names |
| `{section-name}` | kebab-case | section document file names |
| `{SystemName}` | source language casing | source and test paths, namespaces, type names |
| `{ProjectName}` | PascalCase | solution file name |

## Root

| File | Purpose |
| --- | --- |
| `README.md` | Level 0 of the architecture tree; product purpose, entry point, and the design's assumptions |
| `BACKLOG.md` | Redirect stub pointing to `.anneal/work/backlog.md` |
| `CONSTRAINTS.md` | Redirect stub pointing to `.anneal/work/constraints.md` |
| `.editorconfig` | Code formatting rules |
| `.cspell.yaml` | Spell-check dictionary and configuration |
| `.markdownlint-cli2.yaml` | Markdown formatting rules |
| `.yamllint.yaml` | YAML lint configuration |
| `.yamlfix.toml` | YAML auto-fix configuration |
| `.gitignore` | Ignored paths |
| `.gitattributes` | Line-ending and diff attributes |
| `package.json` | Node dependencies for markdown, spell, and diagram tooling |
| `pip-requirements.txt` | Python dependencies for YAML tooling |
| `.config/dotnet-tools.json` | .NET local tools: Pandoc and WeasyPrint, used to compile documents |
| `fix.ps1` | Applies all auto-fixers; always exits 0 |
| `lint.ps1` | Runs all lint checks; exits 1 on failure |
| `build.ps1` | Builds the solution and runs all tests |
| `.github/workflows/build.yml` | CI gate; runs `build.ps1` **before** `lint.ps1` so the pass check has results |
| `{ProjectName}.slnx` | Solution file (.NET repositories) |

## Working Files (`.anneal/`)

The `.anneal/` folder holds all working files for the process. These are the canonical locations;
the root stubs above redirect to them for backward compatibility.

### `.anneal/work/`

| File | What belongs in it | Read by |
| --- | --- | --- |
| `.anneal/work/constraints.md` | Durable conditions the architecture must satisfy, split into **Satisfied** and **Not Yet Satisfied**. Each entry states the condition; a Not Yet Satisfied entry may also state why the current shape blocks it. | `helper` before re-cutting; `route`'s Structural Change worker at Structural Change |
| `.anneal/work/backlog.md` | Wanted, not yet scheduled. Work that completes, rather than a property that holds. | Nobody automatically — it exists so an Intake item is not silently dropped |
| `.anneal/work/active-plan.md` | The stages of an approved migration; present only while a migration is in flight. | Migration workers, stage by stage |

In Intake mode, `helper` invokes compiled `intake`, which appends directly to `.anneal/work/backlog.md`
for a backlog item; for an assumption or constraint, it reports the proposed bullet for user
admission and leaves the governance and constraints files unchanged. The Intake admission test in
`change-classification.md` decides which path an item takes. Once a user has confirmed exact wording,
`dotnet anneal admit-constraint` performs the deterministic write
with no further model judgement. For assumptions (and everything else under `.anneal/governance/` —
vision and tenets), there is no admit action; the agent proposes exact wording and escalates, and a
human edits the file by hand.

A constraint is never deleted for being met — it moves to **Satisfied** and stays as the guard rail
against regressing it. It is removed only when the condition stops being required. Backlog entries
are deleted when they ship or stop being wanted.

Neither is a plan, and neither is scheduled. There is deliberately no `ROADMAP.md`: scheduling is
what milestones and project boards do better, and a scheduled-work file goes stale faster than
anything else in a repository.

### `.anneal/governance/`

| File | What belongs in it |
| --- | --- |
| `.anneal/governance/assumptions.md` | Curated, disprovable beliefs the design rests on; those that would falsify the whole decomposition if proven wrong. `helper` reads this before re-cutting. |
| `.anneal/governance/tenets.md` | The values that shape decisions in this repository, in priority order when they conflict. |

### `.anneal/architecture/`

The architecture tree lives here rather than under `docs/`. A separate `docs/` collection compiles
the tree for publishing, but the source of record is `.anneal/architecture/`.

| File | Level | Required |
| --- | --- | --- |
| `.anneal/architecture/overview.md` | 1 | Yes — exactly one |
| `.anneal/architecture/{system-name}.md` | 2 | Yes — one per system |
| `.anneal/architecture/{system-name}/{section-name}.md` | 3+ | No — created when subdividing benefits clarity, conformity, or size |

Level 3+ documents are created only when subdividing benefits the organization (clarity, conformity,
or size), per `architecture-documentation.md`, and are deleted in the same change that obsoletes
them.

### `.anneal/skills/`

| Path | Purpose |
| --- | --- |
| `.anneal/skills/{skill-name}.md` | A loaded-on-demand procedure note; agents load it when the situation it describes arises. |

Skills are created and retired by the process, not scaffolded upfront. A new repository starts with
no skills files.

## Architecture Document Collection

The publishable document collection under `docs/architecture/` compiles the `.anneal/architecture/`
source into a PDF.

| File | Level | Required |
| --- | --- | --- |
| `docs/architecture/definition.yaml` | — | Yes — lists the documents below, in reading order |
| `docs/architecture/title.txt` | — | Yes — title-page metadata |
| `docs/architecture/build.bat` | — | Yes — compiles the tree into a PDF |
| `docs/architecture/overview.md` | 1 | Yes — exactly one |
| `docs/architecture/{system-name}.md` | 2 | Yes — one per system |
| `docs/architecture/{system-name}/{section-name}.md` | 3+ | No — created when subdividing benefits clarity, conformity, or size |

Everything under `docs/` is a **document collection** that compiles to a PDF, so only files belonging
to that document go there. A loose markdown file dropped into `docs/` is part of no document and is
never published.

`technical-documentation.md` owns the shape of a collection. Each one holds:

| File | Purpose |
| --- | --- |
| `definition.yaml` | Input files in reading order, plus the Pandoc template and options |
| `title.txt` | Title-page metadata |
| `build.bat` | Builds this document; calls `docs/build-doc.ps1` |
| `generated/` | Intermediate HTML and diagrams; git-ignored |

Shared build inputs live in `docs/template/`, and the published PDFs in `docs/generated/`:

| File | Purpose |
| --- | --- |
| `docs/build-doc.ps1` | The one implementation of the document build |
| `docs/template/template.html` | Pandoc HTML template: title page, headers, print styles |
| `docs/template/collection-links.lua` | Rewrites links between documents into cross-references |
| `docs/template/README.md` | What these shared files are for |

Level 3+ documents are created only when subdividing benefits the organization (clarity, conformity, or size), per
`architecture-documentation.md`, and are deleted in the same change that obsoletes them. Either way
`docs/architecture/definition.yaml` is edited in that same change, or the document is not published.

## Source and Tests

| Path | Purpose |
| --- | --- |
| `src/{SystemName}/` | One folder per system |
| `test/{SystemName}.Tests/Contract/` | Contract tests — durable, boundary-only, named in clauses |
| `test/{SystemName}.Tests/` | Interior tests — disposable, free to use internals |

`dotnet anneal check-contracts` requires that every test named by a clause is declared as a test method
**inside a `Contract/` folder**. A clause pointing at an interior test is an error, not a style
preference — an interior test can be rewritten freely, so it cannot carry a durable promise.

There is no per-subsystem or per-unit artifact tree. Interior structure under `src/{SystemName}/` is
organized however the code reads best and carries no documentation obligation.

## Not Part of This Template

Do not scaffold requirements, design, or verification artifact trees below system level, a formal
review configuration, or an architecture model. This process has none of them; their absence is
deliberate.
