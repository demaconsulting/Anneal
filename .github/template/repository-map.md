# Repository Map

Authoritative list of files the template provides. The `template-sync` agent uses this map to audit
and scaffold repositories.

Files present in a repository but absent from this map are **not** deviations — repositories are
expected to contain content the template does not describe.

`.github/agents/`, `.github/skills/`, and `.github/standards/` are installed from the payload rather
than described by this map. They are deliberately absent from it and out of scope for
`template-sync`.

`AGENTS.md` **is** in the map, so `template-sync` can carry process updates into a repository that
already has one. Two things about it are unlike every other mapped file:

- It is stored here as `AGENTS.pristine.md`. A second file literally named `AGENTS.md` inside a
  repository can be picked up as instructions for that repository, which is exactly wrong for a
  template artifact full of placeholders. It installs to the root under its real name.
- It carries **no per-repository customization** — project facts live in `README.md` — so unlike
  every other mapped file it is safe to overwrite wholesale, and `install.ps1 -Force` does.

That last point matters, because the Patch operation only inserts **missing sections**: it cannot
update content that changed inside a section which already exists. For most mapped files that is
the safe behavior. For `AGENTS.md` it is not sufficient, so treat a wholesale replacement as the
way to update it and Patch as a fallback.

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
| `AGENTS.md` | Process instructions; stored here as `AGENTS.pristine.md`, carries no customization |
| `README.md` | Level 0 of the architecture tree; product purpose, entry point, and the design's assumptions |
| `CONSTRAINTS.md` | Conditions the architecture must satisfy, met and unmet (see below) |
| `BACKLOG.md` | Wanted but unscheduled work (see below) |
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
| `check-contracts.ps1` | Verifies every contract clause names a test that exists and passed |
| `build.ps1` | Builds the solution and runs all tests |
| `.github/workflows/build.yml` | CI gate; runs `build.ps1` **before** `lint.ps1` so the pass check has results |
| `{ProjectName}.slnx` | Solution file (.NET repositories) |

## Constraints and Backlog

Two root-level files. In Intake mode, `dispatch` writes to `BACKLOG.md` and README assumptions; for a
constraint, it reports the proposed bullet for user admission. The Intake admission test in
`change-classification.md` decides which path an item takes:

| File | What belongs in it | Read by |
| --- | --- | --- |
| `CONSTRAINTS.md` | Durable conditions the architecture must satisfy, split into **Satisfied** and **Not Yet Satisfied**. Each entry states the condition; a Not Yet Satisfied entry may also state why the current shape blocks it. | `architecture-design` before re-cutting; `architecture-update` at Tier 2 |
| `BACKLOG.md` | Wanted, not yet scheduled. Work that completes, rather than a property that holds. | Nobody automatically — it exists so an Intake item is not silently dropped |

Neither is a plan, and neither is scheduled. There is deliberately no `ROADMAP.md`: scheduling is
what milestones and project boards do better, and a scheduled-work file goes stale faster than
anything else in a repository.

A constraint is never deleted for being met — it moves to **Satisfied** and stays as the guard rail
against regressing it. It is removed only when the condition stops being required. Backlog entries
are deleted when they ship or stop being wanted.

`MIGRATION.md` is a third root-level working file and is **deliberately absent from the map above**.
It holds the stages of an approved migration, exists only while one is landing, and is deleted in its
final commit — so scaffolding one into a repository would assert a migration that is not happening.
`change-classification.md` owns it.

## Architecture Tree

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
never published. The intake registers above are working files and live at the root.

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

`check-contracts.ps1` requires that every test named by a clause is declared as a test method
**inside a `Contract/` folder**. A clause pointing at an interior test is an error, not a style
preference — an interior test can be rewritten freely, so it cannot carry a durable promise.

There is no per-subsystem or per-unit artifact tree. Interior structure under `src/{SystemName}/` is
organized however the code reads best and carries no documentation obligation.

## Not Part of This Template

Do not scaffold requirements, design, or verification artifact trees below system level, a formal
review configuration, or an architecture model. This process has none of them; their absence is
deliberate.
