# Repository Map

Authoritative list of files the template provides. The `template-sync` agent uses this map to audit
and scaffold repositories.

Files present in a repository but absent from this map are **not** deviations — repositories are
expected to contain content the template does not describe.

`AGENTS.md`, `.github/agents/`, and `.github/standards/` are installed from the `agents/` payload
rather than from this template. They are deliberately absent from the map and are out of scope for
`template-sync`.

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
| `README.md` | Level 0 of the architecture tree; product purpose and entry point |
| `CONSTRAINTS.md` | Conditions the architecture must satisfy, met and unmet (see below) |
| `BACKLOG.md` | Wanted but unscheduled work (see below) |
| `.editorconfig` | Code formatting rules |
| `.cspell.yaml` | Spell-check dictionary and configuration |
| `.markdownlint-cli2.yaml` | Markdown formatting rules |
| `.yamllint.yaml` | YAML lint configuration |
| `.yamlfix.toml` | YAML auto-fix configuration |
| `.gitignore` | Ignored paths |
| `.gitattributes` | Line-ending and diff attributes |
| `package.json` | Node dependencies for markdown and spell tooling |
| `pip-requirements.txt` | Python dependencies for YAML tooling |
| `fix.ps1` | Applies all auto-fixers; always exits 0 |
| `lint.ps1` | Runs all lint checks; exits 1 on failure |
| `check-contracts.ps1` | Verifies every contract clause names a test that exists and passed |
| `build.ps1` | Builds the solution and runs all tests |
| `.github/workflows/build.yml` | CI gate; runs `build.ps1` **before** `lint.ps1` so the pass check has results |
| `{ProjectName}.slnx` | Solution file (.NET repositories) |

## Constraints and Backlog

Two root-level files. `evolve` writes to both in Intake mode, at a cost of one bullet; the Intake
admission test in `change-classification.md` decides which one an item goes in:

| File | What belongs in it | Read by |
| --- | --- | --- |
| `CONSTRAINTS.md` | Durable conditions the architecture must satisfy, split into **Satisfied** and **Not Yet Satisfied**. Each entry says the condition and either what upholds it or why the current shape blocks it. | `architecture-design` before re-cutting; `architecture-update` at Tier 2 |
| `BACKLOG.md` | Wanted, not yet scheduled. Work that completes, rather than a property that holds. | Nobody automatically — it exists so an Intake item is not silently dropped |

Neither is a plan, and neither is scheduled. There is deliberately no `ROADMAP.md`: scheduling is
what milestones and project boards do better, and a scheduled-work file goes stale faster than
anything else in a repository.

A constraint is never deleted for being met — it moves to **Satisfied** and stays as the guard rail
against regressing it. It is removed only when the condition stops being required. Backlog entries
are deleted when they ship or stop being wanted.

## Architecture Tree

| File | Level | Required |
| --- | --- | --- |
| `docs/architecture/overview.md` | 1 | Yes — exactly one |
| `docs/architecture/{system-name}.md` | 2 | Yes — one per system |
| `docs/architecture/{system-name}/{section-name}.md` | 3 | No — exceptional; most systems have none |

Everything under `docs/` is a document collection that compiles to a PDF, so only files belonging in
that document go there. The intake registers above are working files and live at the root.

Level 3 documents are created only when the subject meets a creation test in
`architecture-documentation.md`, and are deleted in the same change that obsoletes them.

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
