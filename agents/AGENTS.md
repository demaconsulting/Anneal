# Project Overview

> **Downstream customization required**: Replace the `TODO` values below with
> values specific to the target repository.

- **project-name**: TODO — repository/project name
- **organization**: TODO — organization name
- **description**: TODO — full project description, may be multiple sentences
- **languages**: TODO — programming languages used (e.g., `C#`, `C++`)
- **technologies**: TODO — key technologies and frameworks (e.g., `.NET`, `CMake`)

# Process Model

This repository follows an **evolutionary** development process. Its central rule:

> Documentation work is triggered by **contract change**, never by file change.

Each system publishes a contract — what consumers outside it may rely on. Interior structure below
that boundary is free to change without documentation cost. That freedom is the point: it is what
lets a design keep moving after the first version ships.

There are deliberately **no requirements, design documents, or verification documents below system
level**. If you find yourself creating one, stop — that is the heavyweight process reasserting
itself.

> **Not a regulated-development process.** This process does not produce IEC 62304 or equivalent
> compliance evidence. Repositories needing that use the
> [Agents](https://github.com/demaconsulting/Agents) process instead.

# Architecture Tree (Progressive Disclosure)

Documentation is a descent, not a pile. Read only as far down as your task requires.

| Level | File | Altitude | Answers |
| --- | --- | --- | --- |
| 0 | `README.md` | 50,000 ft | What is this product and why does it exist? |
| 1 | `docs/architecture/overview.md` | 20,000 ft | What systems exist and how do they interact? |
| 2 | `docs/architecture/{system}.md` | 10,000 ft | What does this system promise, and how is it composed? |
| 3 | `docs/architecture/{system}/{section}.md` | 2,000 ft | How does this one non-obvious specific work? |

Level 3 is exceptional. Most systems have none.

**Each level owns content no other level restates.** A parent names its children and gives each a
one-line role; it never summarizes them. Any change should require editing exactly one documentation
file.

# Project Structure

```text
├── docs/
│   └── architecture/
│       ├── overview.md
│       ├── {system}.md
│       └── {system}/
│           └── {section}.md
├── src/
│   └── {System}/
└── test/
    └── {System}.Tests/
        └── Contract/
```

# Change Tiers (ALL Agents)

Classify before working. Read `.github/standards/change-tiers.md` for the full rules.

- **Tier 0** — nothing outside the system observes a difference. No documentation update.
  `developer` alone. This should be most changes.
- **Tier 1** — a contract clause is added, narrowed, removed, or redefined. Update
  `docs/architecture/{system}.md` only. `architect` first, then `developer`.
- **Tier 2** — the set of systems, or the interaction between them, changes. Update
  `overview.md` plus affected system documents. `architect` first, then `developer`.

Tiers may be raised mid-flight, never silently lowered. Never split a change across commits to stay
at a lower tier.

# Test Lifecycles (ALL Agents)

- **Contract tests** are durable. They exercise a system only through its public boundary, are named
  in the clause they verify, and must survive Tier 0 changes untouched.
- **Interior tests** are disposable. Delete or rewrite them freely when the code they cover is
  restructured. They need no clause and no justification.

The clause-to-test link is the **only** mechanically enforced relationship in this process.
`check-contracts.ps1` — run by `lint.ps1` — fails CI when a clause names no test, names a test that
does not exist, or names a test that did not pass. Everything else here is judgement, deliberately.

# Standards Application (ALL Agents Must Follow)

Read the relevant standards from `.github/standards/` before working. Load only what your task
needs:

- **Any code**: `coding-principles.md`
- **C# code**: `coding-principles.md`, `csharp-language.md`
- **C++ code**: `coding-principles.md`, `cpp-language.md`
- **Any tests**: `testing-principles.md`
- **C# tests**: `testing-principles.md`, `csharp-testing.md`
- **C++ tests**: `testing-principles.md`, `cpp-testing.md`
- **Architecture documents**: `architecture-documentation.md`
- **System contracts**: `system-contracts.md`
- **Classifying a change**: `change-tiers.md`
- **Any other documentation**: `technical-documentation.md`

# Agent Delegation Guidelines

The default agent handles simple, well-understood tasks directly. Delegate only for:

- **Any non-trivial change** → `evolve` (classifies the tier and routes to the minimum process)
- **Scoped implementation with a known approach** → `developer`
- **Contract or architecture tree changes** → `architect`
- **Verifying a completed change against its tier** → `contract-check`
- **Bootstrapping or re-cutting system boundaries** → `software-architect`
- **Pre-PR lint cleanup** → `lint-fix`
- **Repository layout versus template** → `template-sync`

# Agent Reporting (Specialized Agents Must Follow)

Specialized agents MUST generate a completion report:

1. Save to `.agent-logs/{agent-name}-{subject}-{unique-id}.md` where `{subject}` is a kebab-case
   task summary (max 5 words) and `{unique-id}` is a short unique suffix
2. Start with `**Result**: (SUCCEEDED|FAILED|INCOMPLETE)` as the first metadata field
3. Include the agent-specific report sections defined in each agent's prompt
4. Return the summary to the caller

Result semantics:

- **SUCCEEDED**: work completed and the checks applicable to that agent's scope passed
- **FAILED**: work could not be completed, or checks failed
- **INCOMPLETE**: work cannot proceed without information only the user can provide

# Scope Discipline (ALL Agents Must Follow)

- **Declare scope upfront** — list files to be changed before changing them
- **Minimum necessary changes** — only files directly required by the task
- **No speculative refactoring** — do not refactor adjacent code unless asked
- **No drive-by fixes** — document pre-existing issues in the report; do not fix them
- **No generated file access** — files inside any `generated/` folder are build outputs

# Documentation Discipline (ALL Agents Must Follow)

- **Do not write documentation an agent was not obliged to write.** Documentation nobody asked for is
  future maintenance debt paid by every subsequent change.
- **Delete in the same change that obsoletes.** Pruning is never deferred.
- **Never restate a lower level's content at a higher level.**
- **Never create a requirement below system level.**

# Language and Spelling (ALL Agents)

Always use **US English** spelling in all output.

# Reference Template

- **template-url**: `https://github.com/demaconsulting/Agents2/raw/refs/heads/main/template`
- **Repository map**: `{template-url}/repository-map.md`
- **Template files**: `{template-url}/{file-path}` for files described in the map

# Key Configuration Files

- **`.editorconfig`** — code formatting rules
- **`.cspell.yaml`** — spell-check configuration and technical term dictionary
- **`.markdownlint-cli2.yaml`** — markdown formatting rules
- **`.yamllint.yaml`** — YAML formatting configuration
- **`package.json`** — Node.js dependencies for formatting tools
- **`pip-requirements.txt`** — Python dependencies for yamllint and yamlfix
- **`fix.ps1`** — applies all auto-fixers silently; always exits 0
- **`lint.ps1`** — runs all lint checks; exits 1 on failure
- **`check-contracts.ps1`** — verifies every contract clause names a test that exists and passed
- **`build.ps1`** — builds the solution and runs all tests

# Protected Configuration Files

These contain deliberate configuration with documented intent. Do not modify unless the task
explicitly requires it and the change preserves the documented intent:

- `.cspell.yaml`, `.editorconfig`, `.markdownlint-cli2.yaml`, `.yamllint.yaml`
- `fix.ps1`, `lint.ps1`, `build.ps1`, `check-contracts.ps1`

# Formatting (After Making Changes)

```pwsh
pwsh ./fix.ps1
```

This applies all available fixers silently and **always exits 0** — agents do not respond to its
output. Full lint compliance is a **pre-PR responsibility**: invoke `lint-fix` once before opening a
pull request.
