# Reference

Every agent, skill, and standard: what it does, when to invoke it, what it produces, and when not to
use it.

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
| `csharp-language.md` | C# implementation standards |
| `csharp-testing.md` | C# test framework and layout standards |

## Skills

Skills live in `.github/skills/` and are loaded on demand, when the situation they describe arises.
They sit between an agent prompt and a standard: cheaper than prompt text, which is paid on every
invocation, and more procedural than a standard, which describes what good output looks like.

### `check-contracts`

The runbook for `check-contracts.ps1`.

- **Covers**: which mode to run for each tier, when to use `-Strict`, and the correct fix for every
  failure the script emits
- **Used by**: `developer` (Step 7) and `contract-check` (Step 2), which reference it rather than
  restating the procedure
- **Does not cover**: the script's parameters — those live in the script's own header, so the two
  cannot drift

## Scripts

| Script | Purpose | Exit |
| --- | --- | --- |
| `fix.ps1` | Applies all auto-fixers silently | Always 0 |
| `lint.ps1` | All lint checks, including `check-contracts.ps1` | 1 on failure |
| `check-contracts.ps1` | Verifies clause-to-test links | 1 on error |
| `build.ps1` | Builds and runs all tests, emitting TRX to `artifacts/tests` | Non-zero on failure |
| `install.ps1` | Installs the payload and vendors the template into a target repository | 1 on conflict |

`build.ps1` clears `artifacts/tests` before each run so results cannot accumulate, and CI runs it
**before** `lint.ps1` so the pass verification has results to read.

### `check-contracts.ps1`

The only mechanically enforced relationship in the process. It parses `## Contract` sections from
`docs/architecture/*.md` and checks:

1. Every system document declares a `## Contract` section
2. Every bolded item under `Provides` or `Invariants` carries a well-formed, unique clause ID
3. Every clause names at least one verifying test
4. Every named test is declared as a test method inside a `Contract/` folder
5. Every named test's **most recent** result is `Passed`
6. Those results are not older than the test sources they describe

It **fails closed**. A renamed heading or an ID that does not parse is an error, not a skip — a check
that quietly stops looking while reporting success is worse than no check at all. `Requires` entries
are exempt: they name depended-upon behavior and legitimately carry no ID.

Test names may be bare or fully qualified, and parameterized suffixes are stripped. Comments are
stripped from test sources before matching, and a clause is satisfied only by an attribute-marked
method declaration, so neither a doc comment, a private helper, nor a string literal can keep a
deleted promise alive. Requiring the `Contract/` location stops a disposable interior test from
standing in for a durable boundary one.

Check 5 resolves each test to the outcome from the newest result file that mentions it, rather than
accepting any historical pass. Because `artifacts/` is git-ignored, results used to accumulate
locally, and an old pass could vouch for a test failing today. Check 6 catches the other direction: a
result recorded before the test last changed.

Clauses whose test name contains an uppercase `TODO` are **warnings** — unfulfilled obligations — so a
contract can be written before its tests exist. The match is case-sensitive, so a real test named
`TodoItemsAreReturned` is checked normally. `contract-check` runs `-Strict` on Tier 1 and Tier 2
changes, which promotes obligations, and absent test results, to errors once implementation is
complete.

```pwsh
pwsh ./check-contracts.ps1
pwsh ./check-contracts.ps1 -Strict
pwsh ./check-contracts.ps1 -TestRoots test,integration -TestResults "out/**/*.trx"
pwsh ./check-contracts.ps1 -TestFilePatterns *.cs,*.fs -TestAttributes Fact,Theory,Test
```

`-TestResults` is matched against the whole repository-relative path, not just the file name, so
results outside the configured location are ignored rather than silently consumed.

**Never resolve a failure by editing the clause to match the code.** Fix the test name, or make the
contract change deliberately.
