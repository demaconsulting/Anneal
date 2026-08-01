# Repository Map

Authoritative list of files the template provides. The `template-sync` agent uses this map to audit
and scaffold repositories.

Files present in a repository but absent from this map are **not** deviations — repositories are
expected to contain content the template does not describe.

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
| `AGENTS.md` | Agent instructions; installed from the `agents/` payload, not this template |
| `.editorconfig` | Code formatting rules |
| `.cspell.yaml` | Spell-check dictionary and configuration |
| `.markdownlint-cli2.yaml` | Markdown formatting rules |
| `.yamllint.yaml` | YAML lint configuration |
| `.yamlfix.toml` | YAML auto-fix configuration |
| `.gitignore` | Ignored paths |
| `package.json` | Node dependencies for markdown and spell tooling |
| `pip-requirements.txt` | Python dependencies for YAML tooling |
| `fix.ps1` | Applies all auto-fixers; always exits 0 |
| `lint.ps1` | Runs all lint checks; exits 1 on failure |
| `check-contracts.ps1` | Verifies every contract clause names a test that exists and passed |
| `build.ps1` | Builds the solution and runs all tests |
| `{ProjectName}.slnx` | Solution file (.NET repositories) |

## Architecture Tree

| File | Level | Required |
| --- | --- | --- |
| `docs/architecture/overview.md` | 1 | Yes — exactly one |
| `docs/architecture/{system-name}.md` | 2 | Yes — one per system |
| `docs/architecture/{system-name}/{section-name}.md` | 3 | No — exceptional; most systems have none |

Level 3 documents are created only when the subject meets a creation test in
`architecture-documentation.md`, and are deleted in the same change that obsoletes them.

## Source and Tests

| Path | Purpose |
| --- | --- |
| `src/{SystemName}/` | One folder per system |
| `test/{SystemName}.Tests/Contract/` | Contract tests — durable, boundary-only, named in clauses |
| `test/{SystemName}.Tests/` | Interior tests — disposable, free to use internals |

There is no per-subsystem or per-unit artifact tree. Interior structure under `src/{SystemName}/` is
organized however the code reads best and carries no documentation obligation.

## Not Part of This Template

Do not scaffold requirements, design, or verification artifact trees below system level, a formal
review configuration, or an architecture model. This process has none of them; their absence is
deliberate.
