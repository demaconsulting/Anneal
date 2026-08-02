# Reference

Every agent, skill, and standard: what it does, when to invoke it, what it produces, and when not to
use it — plus where each kind of information belongs and what the vocabulary means.

## Where Information Lives

Every kind of information this process produces has exactly one home. If something seems to fit two
rows, it is stated at the wrong altitude; pick the row and delete the other copy.

| Information | Home | Owner |
| --- | --- | --- |
| What the product gives a person — features, requirements | `README.md` | `architecture-documentation.md` |
| What the design takes on faith and cannot guarantee | `README.md` § Assumptions | `architecture-documentation.md` |
| Standing statements only a decision could change | `CONSTRAINTS.md` | `change-classification.md` |
| Wanted work that would finish and stay finished | `BACKLOG.md` | `change-classification.md` |
| What systems exist and how they interact | `docs/architecture/overview.md` | `architecture-documentation.md` |
| What a system promises other code | `docs/architecture/{system}.md` § Contract | `system-contracts.md` |
| Why a system is composed the way it is | `docs/architecture/{system}.md` | `architecture-documentation.md` |
| One non-obvious specific, in depth | `docs/architecture/{system}/{section}.md` | `architecture-documentation.md` |
| How a unit works | the code and its doc comments | `coding-principles.md` |
| The stages of a migration in flight | `MIGRATION.md` | `change-classification.md` |
| Why a change was made, and all history | git history | `technical-documentation.md` |
| What an agent did on one run | `.agent-logs/` (untracked) | `AGENTS.md` |

The first four rows are the ones people get wrong, and one question separates them: *does it hold, or
does it complete?* Something that completes is backlog. Something that holds is a constraint if only
a decision could change it, and an assumption if reality could prove it wrong on its own.

`MIGRATION.md` exists only while a migration is in flight; its presence is the signal, so it is
deleted in the migration's final commit. Everything else in the table is permanent or absent.

## Vocabulary

One-line glosses, so terms are not guessed at. The owning file has the actual rule.

| Term | Means | Defined by |
| --- | --- | --- |
| **Mode** | What kind of work this is: Intake, Change, Maintenance, or Migration | `change-classification.md` |
| **Tier** | How far a Change reaches into published contracts: 0, 1, or 2 | `change-classification.md` |
| **Level** | An altitude in the architecture tree, 0 to 3 | `architecture-documentation.md` |
| **System** | A unit with a contract, replaceable whole without the rest noticing | `architecture-documentation.md` |
| **Contract** | What a system promises other code, in observable terms | `system-contracts.md` |
| **Clause** | One numbered promise within a contract, verified by a named test | `system-contracts.md` |
| **Product contract** | Level 0 read as a promise to a person, not to other code | `architecture-documentation.md` |
| **Breaking** | Narrowing or removing an existing promise, at any level | `system-contracts.md` |
| **Boundary vs interior** | Whether a change shows across a contract or only inside it | `change-classification.md` |
| **Planned clause** | A clause for a system not yet built, carrying an exit condition | `system-contracts.md` |
| **Prune** | Deleting a document in the change that stops it earning its place | `architecture-documentation.md` |
| **Re-cut** | Redrawing system boundaries on a repository that already has some | `architecture-design.agent.md` |

## Agents

You invoke two of these. `helper` is the front door for everything you want done, and
`architecture-design` is a sitting-down-to-design interview you start deliberately. The rest run as
sub-agents, reached by describing what you want rather than by naming them — so the routing table
below is worth understanding, but not worth memorizing.

All agents write a report to `.agent-logs/{agent-name}-{subject}-{unique-id}.md` and return a summary
whose first field is `**Result**: (SUCCEEDED|FAILED|INCOMPLETE)`.

### `helper`

The front door. Everything except designing boundaries starts here.

- **Invoke**: `@helper <what is on your mind>`
- **Does**: asks one question at a time until the work is clear, establishes what a consumer would
  observe afterwards and whether you want it built or recorded, confirms the shape back to you, and
  routes only once you agree
- **Produces**: the confirmed request, its consumer-observable effect, the classification it expects
  `dispatch` to reach, and the bound for a tidy-up
- **Never**: writes code, documentation, or a register entry — routing is its whole output
- **Not for**: establishing or re-cutting system boundaries, which is `architecture-design`

It does not insist on a conversation. A request that is already clear, or a failure report that
already names its fix, is routed immediately with a sentence saying so.

Only you can invoke it. It is deliberately not model-invocable, because a sub-agent has no live user
to interview and could only guess at the answers.

Returns INCOMPLETE when the conversation stops making progress without a decision, rather than
routing on a guess.

### `dispatch`

The entry point for any non-trivial change.

- **Reached via**: `helper`, for any work you want done
- **Does**: determines the work mode first, then for a Change classifies the tier, routes to the
  minimum set of agents, and allows one documentation repair and one code repair
- **Modes**: Change is the default; **Intake** files a need and stops; **Maintenance** runs a bounded
  tidy; **Migration** it refuses to enter unless `MIGRATION.md` already holds an approved proposal.
  See [Common Tasks](common-tasks.md) for the prompt to use for each.
- **Produces**: mode and tier with rationale, contract impact, sub-agent reports, documentation
  changes
- **Sub-agents**: `architecture-update` (Tier 1 and 2 only), `apply`, `tier-check` (Tier 1 and 2
  only)
- **Not for**: trivial interior work, lint-only cleanup, or reshaping system boundaries — `helper`
  sends those to `apply`, `lint-fix`, and `architecture-design` instead

Returns INCOMPLETE when the tier cannot be determined without information only you can supply, which
`helper` is meant to have settled before calling it.

### `apply`

The core working agent. Most changes need this and nothing else.

- **Reached via**: `helper`, once the approach is settled or a finding names the fix
- **Does**: descends the architecture tree for context, loads the relevant standards, declares scope,
  implements, runs `fix.ps1`, `build.ps1`, and `check-contracts.ps1`
- **Produces**: files changed with interior/boundary classification, contract test status, interior
  test changes, build results, scope deviations
- **Not for**: changes needing a contract update that has not been made — it reports INCOMPLETE
  rather than writing the contract itself, deliberately

### `architecture-update`

Owns `docs/architecture/` and the contracts inside it.

- **Reached via**: `dispatch`, on a Tier 1 or Tier 2 change
- **Does**: locates the single correct level for a change, updates the contract **before**
  implementation, updates the tree, and **prunes** section documents that no longer earn their place
- **Produces**: contract changes with breaking flags, tree changes, prune results, ownership check,
  implementation obligations for `apply`
- **Never**: writes implementation code, documents interior structure below the decomposition
  rationale, or creates a requirement below system level

Pruning is half of this agent's purpose. Its report lists every section document examined with a
kept-or-deleted verdict and the reason.

### `tier-check`

Verifies a completed change against its declared tier. Deliberately narrow.

- **Reached via**: `dispatch`, after implementation; or `helper`, to verify finished work
- **Does**: runs `check-contracts.ps1`, then judges what a script cannot — undeclared boundary
  behavior, tier honesty, tree accuracy, level ownership, orphaned documents
- **Produces**: priority-ordered required fixes with specific actions
- **Explicitly ignores**: per-unit artifacts (they do not exist here), missing docs on interior
  changes, deleted interior tests, coverage percentages, lint and formatting, pre-existing issues in
  untouched files

Length observations and drift flags are **advisory** and never fail the result.

### `architecture-design`

Interactive interview that establishes or re-cuts system boundaries.

- **Invoke**: `@architecture-design` — then answer its questions. One of the two agents you invoke
  directly, and deliberately not model-invocable: called headless it would have nobody to interview
  and would invent the answers, so `helper` sends you here by name rather than calling it
- **Does**: asks one question at a time, shows the tree and concerns every few questions, focuses on
  boundaries, writes `docs/architecture/` and `README.md` when you confirm
- **On an existing repository**: reads the current tree first and refines it rather than replacing
  it. Decisions, `README.md`, and still-valid contract clauses carry across; documents for dropped
  systems are deleted and reported. It also writes `MIGRATION.md` — the stages the move will land in,
  which is the approved proposal every Migration commit then references
- **Produces**: the initial tree, contract clauses naming tests that do not yet exist, and those
  tests listed as implementation obligations
- **Not for**: ordinary change — that starts at `helper`

### `lint-fix`

Pre-pull-request sweep.

- **Reached via**: `helper` — say you are getting a change ready for review
- **Does**: runs `fix.ps1`, then loops `lint.ps1` fixing issues, up to five iterations
- **Never**: refactors, makes functional changes, or "fixes" a contract-to-test failure — that is
  semantic and belongs to `architecture-update` or `apply`

### `template-sync`

Compares a repository against `.github/template/`.

- **Reached via**: `helper` — say you want the repository checked against the template
- **Modes**: Audit reports only; Scaffold creates missing files; Patch inserts missing sections into
  existing files
- **Never**: deletes content without a template counterpart, or overwrites hand-written architectural
  reasoning

There is deliberately no Recreate mode — rebuilding a document from a template flattens reasoning
into boilerplate.

## Standards

Loaded selectively per task using the matrix in `AGENTS.md`. Load only what the task needs.

| Standard | Owns |
| --- | --- |
| `architecture-documentation.md` | The four levels, exclusive ownership, the one-file test, document length |
| `system-contracts.md` | Contract placement and structure, clause rules, enforcement, identifiers, sizing |
| `change-classification.md` | The classifying question, tier obligations, tier discipline, worked examples |
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

- **Covers**: which invocation to use for each tier, when to use `-Strict`, and the correct fix for
  every failure the script emits
- **Used by**: `apply` (Step 7) and `tier-check` (Step 3), which reference it rather than
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
| `docs/build-doc.ps1` | Compiles one document collection to HTML and then PDF | Non-zero on failure |
| `install.ps1` | Installs the payload and vendors the template into a target repository | 1 on conflict |

`build.ps1` clears `artifacts/tests` before each run so results cannot accumulate, and CI runs it
**before** `lint.ps1` so the pass verification has results to read.

Each document under `docs/` has a `build.bat` that calls `docs/build-doc.ps1` with its folder and its
published name. The name is passed rather than derived because a document's title and its published
file name legitimately differ. Pass `-NoRestore` where the tools are already restored.

`install.ps1 -Prune` reviews files in the payload directories that the payload does not provide. It
separates the ones Anneal retired, listed in `retired-payload.txt`, from ones it does not recognize,
and deletes only what you confirm — a repository is free to keep its own agents beside these. Without
`-Prune` it reports the count rather than acting, because a stale agent file still gets picked.

### `check-contracts.ps1`

The only mechanically enforced relationship in the process. `system-contracts.md` lists what it
rejects and the **check-contracts** skill gives the fix for each; this section covers only what a
user running it needs to know.

It **fails closed**. A renamed heading or an ID that does not parse is an error, not a skip — a check
that quietly stops looking while reporting success is worse than no check at all. `Requires` entries
are exempt: they name depended-upon behavior and legitimately carry no ID.

Test names may be bare or fully qualified, and parameterized suffixes are stripped. Comments are
stripped from test sources before matching, and a clause is satisfied only by an attribute-marked
method declaration, so neither a doc comment, a private helper, nor a string literal can keep a
deleted promise alive. Requiring the `Contract/` location stops a disposable interior test from
standing in for a durable boundary one.

The pass check resolves each test to the outcome from the newest result file that mentions it, rather
than accepting any historical pass. Because `artifacts/` is git-ignored, results used to accumulate
locally, and an old pass could vouch for a test failing today. Staleness catches the other direction:
a result recorded before the test last changed.

Clauses whose test name contains an uppercase `TODO` are **warnings** — unfulfilled obligations — so a
contract can be written before its tests exist. The match is case-sensitive, so a real test named
`TodoItemsAreReturned` is checked normally. `tier-check` runs `-Strict` on Tier 1 and Tier 2
changes, which promotes obligations, and absent test results, to errors once implementation is
complete.

```pwsh
pwsh ./check-contracts.ps1
pwsh ./check-contracts.ps1 -Strict
pwsh ./check-contracts.ps1 -TestRoots test,integration -TestResults "out/**/*.trx"
pwsh ./check-contracts.ps1 -TestFilePatterns *.cs,*.fs -TestAttributes Fact,Theory,Test
```

Run `pwsh ./check-contracts.ps1 -?` for the full parameter list and defaults. `-TestResults` is
matched against the whole repository-relative path, not just the file name, so results outside the
configured location are ignored rather than silently consumed.

**Never resolve a failure by editing the clause to match the code.** Fix the test name, or make the
contract change deliberately.
