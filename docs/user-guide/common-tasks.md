# Common Tasks

The day-to-day jobs, and how to ask for each one. [Workflow](workflow.md) explains *why* the routing
works this way; this page is the lookup table.

| You want to | Use |
| --- | --- |
| Record something you are not acting on now | `evolve` (Intake) |
| Make a change and not think about how big it is | `evolve` |
| Make a small change you already understand | `developer` |
| Update a contract or the tree without writing code | `architecture-update` |
| Split or add a system | `evolve` (Tier 2) |
| Tidy code without changing behavior | `evolve` (Maintenance) |
| Reshape system boundaries | `architecture-design` |
| Decide what to work on next | `BACKLOG.md`, `CONSTRAINTS.md` |
| Get a change ready for review | `lint-fix`, `tier-check` |
| Recover from a failed check | [When a Check Fails](#when-a-check-fails) |
| Check a repository against the template | `template-sync` |

One-time setup — installing, scaffolding, and building the first architecture tree — is in
[Getting Started](getting-started.md).

## Record Something You Are Not Acting On Now

Intake mode. The cheapest thing in the process, deliberately: if filing a need costs anything, needs
stop being filed.

```text
@evolve file this for later: we will eventually need an S3 storage backend
```

- **You get**: one bullet appended to `BACKLOG.md`, or to the **Not Yet Satisfied** section of
  `CONSTRAINTS.md` if the need is a standing property the system must always satisfy rather than a
  piece of work that finishes. No code, no tests, no contract.
- `evolve` stops there. Nothing is implemented and nothing is scheduled.

## Make a Change

The default. Use this whenever you are not certain how far a change reaches — deciding that is
`evolve`'s main job.

```text
@evolve add a --verbose flag to the CLI
@evolve return 404 instead of 400 when a record is missing
@evolve split Storage into Storage and Cache
```

- **You get**: a declared tier with its rationale, then only the agents that tier needs —
  `architecture-update` first on Tier 1 and 2, then `developer`, then `tier-check`.
- **Read the Tier and Tier Rationale fields.** If the tier looks wrong, say so immediately;
  misclassification is the main way this process degrades.
- **Check the Breaking field.** Narrowing or removing a clause breaks consumers who relied on it, and
  that is a decision for you rather than the agent.

## Make a Small Change You Already Understand

Skip the routing when the work is obviously interior and you know the approach.

```text
@developer extract the retry logic in HttpClient into its own class
```

- **You get**: the change, with `fix.ps1` and `build.ps1` run for you. `check-contracts.ps1` runs
  only on Tier 1 and 2, so an interior change like this one is verified by the build and its tests.
- If it turns out to need a contract change, `developer` reports INCOMPLETE rather than writing the
  contract itself. That is deliberate — go through `evolve` instead.

## Tidy Code Without Changing Behavior

Maintenance mode. **Bound it before it starts** — a file set, the kinds of edit allowed, and a
stopping point. Open-ended "improve the code" is not a task and will be refused.

```text
@evolve maintenance in src/Storage: delete dead code and extract duplicated retry
logic, interior code and interior tests only, stop when those two are done
```

- **You get**: interior code and interior test changes only, and no documentation.
- Maintenance is Tier 0 by definition. If the work would change a contract it has stopped being
  maintenance, and the agent stops and asks you to re-classify it as a Change.
- An architectural problem found during maintenance is *reported*, never acted on.

## Update the Architecture Without Writing Code

When the contract or the tree must change but no implementation follows it — designing before
building, or correcting documentation that has drifted from what the code actually promises.

```text
@architecture-update add a clause to Storage: blobs over 10 MB are rejected
@architecture-update the Cache contract no longer matches what the code promises
```

- **You get**: the change placed at exactly one level of the tree, a prune check on the affected
  system, and each new clause naming the test that will verify it.
- Because no implementation follows, that test name carries a `TODO` marker, which
  `check-contracts.ps1` reports as an unfulfilled obligation rather than an error. Naming a real test
  that does not exist yet **is** an error, so the marker is what keeps the repository green.
- It never writes implementation code. New clauses come back as implementation obligations for
  `developer`, which is why the contract ends up written before the code rather than after it.
- **The second prompt usually comes back INCOMPLETE with a question, and that is the correct
  answer.** When a contract and the code disagree, one of them is a defect, and the agent will not
  guess which. Say which side is authoritative — "the contract is right, the code is wrong" sends it
  back as a Tier 0 bug fix through `evolve`; "the code is right" makes it a real contract change.
- `evolve` runs this for you on Tier 1 and 2. Invoke it directly only when you want the documentation
  to move without a change following it.

## Split or Add a System

A Tier 2 change. It costs more than the others because the system inventory itself moves.

```text
@evolve split Storage into Storage and Cache
```

- **You get**: `architecture-update` first, rewriting `overview.md` and every affected `{system}.md`,
  re-homing surviving clauses onto the new system's identifiers, and pruning section documents across
  both. Then `developer` moves `src/` and `test/` to match. Then `tier-check`.
- **Check the clause table in the architecture report.** Every promise the old system made must appear
  against the system that now keeps it, with the old identifier recorded. A promise that vanishes in a
  split is a silent breaking change.
- Contract tests move with their clauses, into the new `test/{System}.Tests/Contract/` folder. The
  `covers` front matter points at the new source paths.
- If the re-cut is larger than one or two systems, this is a **Migration**, not a Tier 2 change — go
  to `architecture-design` for a proposal instead.

## Reshape System Boundaries

When the systems themselves are wrong — not the code inside them.

```text
@architecture-design
```

- **You get**: an interview, one question at a time, showing the tree as it develops, then
  `docs/architecture/` written when you confirm.
- **On an existing repository it reads the current tree first** and interviews you about what is
  wrong with it, rather than starting from a blank sheet. Decisions, `README.md`, and contract
  clauses that still hold carry across; documents for systems the new tree drops are deleted and
  listed in the report.
- It reads `CONSTRAINTS.md` too: **Satisfied** entries are what the new tree must not regress,
  **Not Yet Satisfied** entries are usually why you are here.
- This is also how a **Migration** proposal is produced. The tree it proposes plus the stages it
  would land in is what you approve; no agent enters Migration on its own.

## Decide What to Work on Next

Two files, both plain markdown, both meant to be read by a person.

- `BACKLOG.md` — wanted, not scheduled. Work that finishes: "add a `--version` flag".
- `CONSTRAINTS.md` — standing properties the architecture must satisfy: "runs on Windows", "supports
  .NET Standard 2.0". **Not Yet Satisfied** is the pressure arguing for a re-cut; **Satisfied** is what
  the current shape is protecting.

A constraint is never deleted for being met — it moves to **Satisfied** and stays as the guard rail
against regressing it.

## Get a Change Ready for Review

```text
@lint-fix
```

- **You get**: `fix.ps1`, then `lint.ps1` looped until clean, up to five passes. It never refactors
  and never makes functional changes.

To check a change you made outside `evolve`:

```text
@tier-check added the --verbose flag to the CLI
```

- **You get**: `check-contracts.ps1`, plus what a script cannot judge — undeclared boundary
  behavior, tier honesty, tree accuracy, orphaned documents.

[Workflow](workflow.md#reviewing-a-pull-request) has the four questions to ask when reviewing someone
else's pull request.

## When a Check Fails

### `evolve` reported FAILED

`evolve` gets [exactly one repair pass](workflow.md#the-repair-pass). Once it is spent, the agent
stops and reports rather than looping. What to do next:

- **Read the Required Fixes in the tier-check report.** They are priority-ordered with a specific
  action each, because the repair pass has to be spent well.
- If the finding is about the **documentation** — wrong tier, a missing clause, a stale tree — re-run
  `evolve`, which routes those back through `architecture-update`. `developer` is not allowed to edit
  `docs/architecture/`, so sending a documentation finding to it directly cannot work.
- If the finding is a genuine implementation gap, `@developer` it directly with the finding quoted.
  You do not need to re-enter `evolve` for a fix you already understand.
- Do not re-run `evolve` on the same request hoping for a different route. It classifies from the
  request, so the same request produces the same tier.

### `check-contracts.ps1` failed

Run `pwsh ./build.ps1` first — the check reads test results, and without them it cannot verify that
anything passed. Under `-Strict`, which CI and `tier-check` use, missing results are an error rather
than a warning, so a stale `artifacts/` is the most common cause of a failure that has nothing to do
with your change.

Every message the script emits has a specific fix, and the **check-contracts** skill in
`.github/skills/` holds the authoritative list — ask any agent to work through it, or read it
directly. The failures are all one of three kinds: the clause names no test, it names a test that
does not exist or is not a real test method, or the test exists but sits outside a `Contract/`
folder and so cannot carry a durable promise.

**Never fix any of them by editing the clause to match the code.** That converts a defect into a
promise, and it defeats the only check in this process a machine performs.

## Check a Repository Against the Template

```text
@template-sync
@template-sync Patch
```

- **You get**: Audit by default, which only reports. `Scaffold` creates missing files; `Patch` inserts
  missing sections into files that already exist.
- Neither mode deletes content or overwrites hand-written architectural reasoning.

## Upgrade Anneal in a Repository

```pwsh
pwsh ./install.ps1 -TargetRepository ../my-product -Force
```

- `-Force` is required to replace the installed agents. It overwrites **every** file the payload
  owns: `AGENTS.md`, `.github/agents/`, `.github/skills/`, `.github/standards/`, and the vendored
  `.github/template/`. There is no backup and no diff. Commit first, and expect to restore your
  `AGENTS.md` Project Overview values and any locally edited standard from the diff afterwards.
- `install.ps1` only writes files. Agents removed or renamed upstream are left behind in
  `.github/agents/`, so delete any that no longer exist in the new version — a stale agent file still
  works and will still be picked. Compare `.github/agents/` against the Anneal `agents/.github/agents/`
  directory to find them. If you installed before the renames, delete `architect.agent.md`,
  `software-architect.agent.md`, and `contract-check.agent.md`; they are now `architecture-update`,
  `architecture-design`, and `tier-check`. Delete the stale standard `change-tiers.md` too; it is now
  `change-classification.md`.
- Then run **`@template-sync Scaffold`** to create template files added since you installed, followed
  by `@template-sync Patch` to insert new sections into files you already have. Scaffold is the one
  that matters: `Patch` only touches files that already exist, so on its own it will never create a
  newly-introduced file such as `CONSTRAINTS.md`.
