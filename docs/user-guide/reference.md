# Reference

Every agent and standard: what it does, when to invoke it, what it produces, and when not to use it.

## Agents

All agents write a report to `.agent-logs/{agent-name}-{subject}-{unique-id}.md` and return a summary
whose first field is `**Result**: (SUCCEEDED|FAILED|INCOMPLETE)`.

### `evolve`

The entry point for any non-trivial change.

- **Invoke**: `@evolve <what you want done>`
- **Does**: classifies the change tier, routes to the minimum set of agents, allows one repair pass
- **Produces**: tier and rationale, contract impact, sub-agent reports, documentation changes
- **Sub-agents**: `architect` (Tier 1 and 2 only), `developer`, `contract-check`
- **Not for**: trivial interior work (`developer` is faster), lint-only cleanup (`lint-fix`), or
  reshaping system boundaries (`software-architect`)

Returns INCOMPLETE when the tier cannot be determined without information only you can supply.

### `developer`

The core working agent. Most changes need this and nothing else.

- **Invoke**: `@developer <change with a known approach>`
- **Does**: descends the architecture tree for context, loads the relevant standards, declares scope,
  implements, runs `fix.ps1`, `build.ps1`, and `check-contracts.ps1`
- **Produces**: files changed with interior/boundary classification, contract test status, interior
  test changes, build results, scope deviations
- **Not for**: changes needing a contract update that has not been made — it reports INCOMPLETE
  rather than writing the contract itself, deliberately

### `architect`

Owns `docs/architecture/` and the contracts inside it.

- **Invoke**: `@architect <contract or structural change>`
- **Does**: locates the single correct level for a change, updates the contract **before**
  implementation, updates the tree, and **prunes** section documents that no longer earn their place
- **Produces**: contract changes with breaking flags, tree changes, prune results, ownership check,
  implementation obligations for `developer`
- **Never**: writes implementation code, documents interior structure below the decomposition
  rationale, or creates a requirement below system level

Pruning is half of this agent's purpose. Its report lists every section document examined with a
kept-or-deleted verdict and the reason.

### `contract-check`

Verifies a completed change against its declared tier. Deliberately narrow.

- **Invoke**: `@contract-check <what was changed>`
- **Does**: runs `check-contracts.ps1`, then judges what a script cannot — undeclared boundary
  behavior, tier honesty, tree accuracy, level ownership, orphaned documents
- **Produces**: priority-ordered required fixes with specific actions
- **Explicitly ignores**: per-unit artifacts (they do not exist here), missing docs on interior
  changes, deleted interior tests, coverage percentages, lint and formatting, pre-existing issues in
  untouched files

Size budget overruns and drift flags are **advisory** and never fail the result.

### `software-architect`

Interactive interview that establishes or re-cuts system boundaries.

- **Invoke**: `@software-architect` — then answer its questions
- **Does**: asks one question at a time, shows the tree and concerns every few questions, focuses on
  boundaries, writes `docs/architecture/` and `README.md` when you confirm
- **Produces**: the initial tree, contract clauses naming tests that do not yet exist, and those
  tests listed as implementation obligations
- **Not for**: ordinary change — use `evolve`

### `lint-fix`

Pre-pull-request sweep.

- **Invoke**: `@lint-fix`
- **Does**: runs `fix.ps1`, then loops `lint.ps1` fixing issues, up to five iterations
- **Never**: refactors, makes functional changes, or "fixes" a contract-to-test failure — that is
  semantic and belongs to `architect` or `developer`

### `template-sync`

Compares a repository against `template/`.

- **Invoke**: `@template-sync [Audit|Scaffold|Sync]` (default Audit)
- **Modes**: Audit reports only; Scaffold creates missing files; Sync patches missing sections into
  existing files
- **Never**: deletes content without a template counterpart, or overwrites hand-written architectural
  reasoning

There is deliberately no Recreate mode — rebuilding a document from a template flattens reasoning
into boilerplate.

## Standards

Loaded selectively per task using the matrix in `AGENTS.md`. Load only what the task needs.

| Standard | Owns |
| --- | --- |
| `architecture-documentation.md` | The four levels, exclusive ownership, the one-file test, size budgets |
| `system-contracts.md` | Contract placement and structure, clause rules, enforcement, identifiers, sizing |
| `change-tiers.md` | The classifying question, tier obligations, tier discipline, worked examples |
| `coding-principles.md` | Literate coding, API documentation, design principles, anti-patterns |
| `testing-principles.md` | Contract versus interior test lifecycles, AAA, coverage expectations |
| `technical-documentation.md` | README and general markdown conventions, links, user guides |
| `csharp-language.md`, `cpp-language.md` | Language-specific implementation standards |
| `csharp-testing.md`, `cpp-testing.md` | Language-specific test framework and layout standards |

## Scripts

| Script | Purpose | Exit |
| --- | --- | --- |
| `fix.ps1` | Applies all auto-fixers silently | Always 0 |
| `lint.ps1` | All lint checks, including `check-contracts.ps1` | 1 on failure |
| `check-contracts.ps1` | Verifies clause-to-test links | 1 on error |
| `build.ps1` | Builds and runs all tests | Non-zero on failure |

### `check-contracts.ps1`

The only mechanically enforced relationship in the process. It parses `## Contract` sections from
`docs/architecture/*.md` and checks:

1. Clause IDs are unique across the repository
2. Every clause names at least one verifying test
3. Every named test exists in the test sources
4. Every named test passed, when `.trx` results are present

Test names may be bare or fully qualified, and parameterized suffixes are stripped. Bolded IDs
outside the `## Contract` block are ignored.

Clauses whose test name contains `TODO` are **warnings** — unfulfilled obligations — so a contract can
be written before its tests exist. Use `-Strict` to promote them to errors once implementation is
complete.

```pwsh
pwsh ./check-contracts.ps1
pwsh ./check-contracts.ps1 -Strict
pwsh ./check-contracts.ps1 -TestRoots test,integration -TestResults "out/**/*.trx"
```

**Never resolve a failure by editing the clause to match the code.** Fix the test name, or make the
contract change deliberately.
