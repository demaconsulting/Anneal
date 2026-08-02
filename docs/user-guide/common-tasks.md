# Common Tasks

The day-to-day jobs, and how to ask for each one. [Workflow](workflow.md) explains *why* the routing
works this way; this page is the lookup table.

**You ask `helper` for all of it.** Say what you want in your own words and it works out the rest, so
the right-hand column below is what happens underneath rather than something to choose between —
worth understanding when you read a report, not worth memorizing. The one exception is
`architecture-design`, which you invoke yourself because it interviews you.

| You want to | Handled by |
| --- | --- |
| Talk something through before committing to it | `helper` itself |
| Record something you are not acting on now | `dispatch` (Intake) |
| Make a change and not think about how big it is | `dispatch` |
| Make a small change you already understand | `apply` |
| Update a contract or the tree without writing code | `architecture-update` |
| Split or add a system | `dispatch` (Tier 2) |
| Tidy code without changing behavior | `dispatch` (Maintenance) |
| Reshape system boundaries | `@architecture-design` — you invoke this one |
| Land a stage of an approved migration | `apply` |
| Decide what to work on next | `BACKLOG.md`, `CONSTRAINTS.md` |
| Get a change ready for review | `lint-fix`, `tier-check` |
| Recover from a failed check | [When a Check Fails](#when-a-check-fails) |
| Check a repository against the template | `template-sync` |

One-time setup — installing, scaffolding, and building the first architecture tree — is in
[Getting Started](getting-started.md).

## Talk Something Through Before Committing To It

You do not have to know what you want before you start. Describe the situation and let the
conversation find the job.

```text
@helper I think the worker is losing pushes when the network drops, but I'm not sure what we want it
to do instead
```

- **You get**: a conversation. It asks one question at a time, works out what someone outside the code
  would observe afterwards, says the shape of the work back to you, and waits for you to agree.
- **Then**: it routes to the agent that does the work. It writes nothing itself.
- **Why it exists**: narrative requests systematically read as smaller than they are. "Make it retry
  failed pushes" sounds like a repair and is new observable behavior, which is a contract change. This
  is the cheapest place to catch that, because catching it later costs a repair cycle.
- **It will not interrogate you.** A request that is already clear is routed straight through, with a
  sentence saying so. The examples on the rest of this page are all clear enough to go straight
  through.

## Record Something You Are Not Acting On Now

Intake mode. The cheapest thing in the process, deliberately: if filing a need costs anything, needs
stop being filed.

```text
@helper file this for later: we will eventually need an S3 storage backend
```

- **You get**: one bullet appended to whichever of three destinations fits — `BACKLOG.md` for work
  that finishes, the **Not Yet Satisfied** section of `CONSTRAINTS.md` for a standing property the
  system must satisfy, or the **Assumptions** section of `README.md` for a belief the design rests on
  that reality could disprove. No code, no tests, no contract.
- `dispatch` stops there. Nothing is implemented and nothing is scheduled.

## Make a Change

The default. Use this whenever you are not certain how far a change reaches — deciding that is
`dispatch`'s main job.

```text
@helper add a --verbose flag to the CLI
@helper return 404 instead of 400 when a record is missing
@helper split Storage into Storage and Cache
```

- **You get**: a declared tier with its rationale, then only the agents that tier needs —
  `architecture-update` first on Tier 1 and 2, then `apply`, then `tier-check`.
- **Read the Tier and Tier Rationale fields.** If the tier looks wrong, say so immediately;
  misclassification is the main way this process degrades.
- **Check the Breaking field.** Narrowing or removing a clause breaks consumers who relied on it, and
  that is a decision for you rather than the agent.

## Make a Small Change You Already Understand

Skip the routing when the work is obviously interior and you know the approach.

```text
@helper extract the retry logic in HttpClient into its own class
```

- **You get**: the change, with `fix.ps1` and `build.ps1` run for you. `check-contracts.ps1` runs
  only on Tier 1 and 2, so an interior change like this one is verified by the build and its tests.
- If it turns out to need a contract change, `apply` reports INCOMPLETE rather than writing the
  contract itself. That is deliberate — the work goes back through classification instead.

## Tidy Code Without Changing Behavior

Maintenance mode. **Bound it before it starts** — a file set, the kinds of edit allowed, and a
stopping point. Open-ended "improve the code" is not a task and will be refused.

```text
@helper maintenance in src/Storage: delete dead code and extract duplicated retry
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
@helper add a clause to Storage: blobs over 10 MB are rejected
@helper the Cache contract no longer matches what the code promises
```

- **You get**: the change placed at exactly one level of the tree, a prune check on the affected
  system, and each new clause naming the test that will verify it.
- Because no implementation follows, that test name carries a `TODO` marker, which
  `check-contracts.ps1` reports as an unfulfilled obligation rather than an error. Naming a real test
  that does not exist yet **is** an error, so the marker is what keeps the repository green.
- It never writes implementation code. New clauses come back as implementation obligations for
  `apply`, which is why the contract ends up written before the code rather than after it.
- **The second prompt usually comes back INCOMPLETE with a question, and that is the correct
  answer.** When a contract and the code disagree, one of them is a defect, and the agent will not
  guess which. Say which side is authoritative — "the contract is right, the code is wrong" makes it a
  Tier 0 bug fix; "the code is right" makes it a real contract change.
- `dispatch` runs this for you on Tier 1 and 2. It runs on its own only when you want the
  documentation to move without a change following it.

## Split or Add a System

A Tier 2 change. It costs more than the others because the system inventory itself moves.

```text
@helper split Storage into Storage and Cache
```

- **You get**: `architecture-update` first, rewriting `overview.md` and every affected `{system}.md`,
  re-homing surviving clauses onto the new system's identifiers, and pruning section documents across
  both. Then `apply` moves `src/` and `test/` to match. Then `tier-check`.
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
  would land in is what you approve; the stages are written to `MIGRATION.md` so the commits that
  follow can point at them. No agent enters Migration on its own.

## Land a Stage of an Approved Migration

Only after `architecture-design` has written the target tree and `MIGRATION.md`.

```text
@helper land stage 2 of MIGRATION.md: move Cache out of Storage, leaving Storage working
```

- **This lands through `apply`, not `dispatch`.** The tree is already written and approved, so there
  is nothing left to classify; each stage is bounded implementation work, and saying it is a migration
  stage is what routes it that way.
- **Give it the stage's bound explicitly**, the way you would bound a Maintenance task. The stage
  boundary is the point — what it must leave working is in `MIGRATION.md`.
- Every commit says Migration mode and references `MIGRATION.md`. Staging is required here — the rule
  against splitting a change to stay at a lower tier is about evasion, and does not bind here.
- **Do not run `tier-check` between stages.** It checks contracts with `-Strict`, which treats a
  planned clause with no test yet as an error — correct at the end of a change, wrong halfway through
  a migration where later stages have not landed. Run it after the final stage.
- **Delete `MIGRATION.md` in that final commit.** While it exists, the repository is claiming a
  migration is still in flight.

## Decide What to Work on Next

Three places, all plain markdown, all meant to be read by a person.

- `BACKLOG.md` — wanted, not scheduled. Work that finishes: "add a `--version` flag".
- `CONSTRAINTS.md` — standing properties the architecture must satisfy: "runs on Windows", "supports
  .NET Standard 2.0". **Not Yet Satisfied** is the pressure arguing for a re-cut; **Satisfied** is what
  the current shape is protecting.
- The **Assumptions** section of `README.md` — what the design takes on faith: "our users have
  outbound internet access". These change when reality disproves them, not when someone decides.

A constraint is never deleted for being met — it moves to **Satisfied** and stays as the guard rail
against regressing it. A disproved assumption is a reason to re-cut, so take it to
`@architecture-design` rather than quietly rewording it.

## Get a Change Ready for Review

```text
@helper get this ready for review
```

- **You get**: `fix.ps1`, then `lint.ps1` looped until clean, up to five passes. It never refactors
  and never makes functional changes.

To check a change you made outside `dispatch`:

```text
@helper check the --verbose flag change I just finished
```

- **You get**: `check-contracts.ps1`, plus what a script cannot judge — undeclared boundary
  behavior, tier honesty, tree accuracy, orphaned documents.

[Workflow](workflow.md#reviewing-a-pull-request) has the four questions to ask when reviewing someone
else's pull request.

## When a Check Fails

### `dispatch` reported FAILED

`dispatch` allows [one documentation repair and one code repair](workflow.md#the-repair-pass). Once
they are spent, or once a repair fails to clear its finding, the agent stops and reports rather than
looping. What to do next:

- **Read the Required Fixes in the tier-check report.** They are priority-ordered with a specific
  action each, because the repairs have to be spent well.
- **Read the Repairs Used field.** It says whether the documentation repair, the code repair, or both
  were consumed, which tells you where the change got stuck.
- **Read the Residual field.** `gate` means something is actually red — a build, a contract check, a
  test. `findings-only` means every gate passed and what remains is a finding the agent has already
  written the fix for, which is usually a single edit rather than a re-run.
- If the finding is about the **documentation** — wrong tier, a missing clause, a stale tree — take it
  back to `helper`, which routes those through `architecture-update`. `apply` is not allowed to edit
  `docs/architecture/`, so sending a documentation finding to it cannot work.
- If the finding is a genuine implementation gap, quote it to `helper`. A finding that already names
  its fix goes straight to `apply` without a conversation — it does not re-classify work you have
  already had diagnosed.
- Do not repeat the same request hoping for a different route. Classification comes from the request,
  so the same request produces the same tier.

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
@helper check this repository against the template
@helper patch in template sections I am missing
```

- **You get**: Audit by default, which only reports. `Scaffold` creates missing files; `Patch` inserts
  missing sections into files that already exist.
- Neither mode deletes content or overwrites hand-written architectural reasoning.

## Upgrade Anneal in a Repository

```pwsh
pwsh ./install.ps1 -TargetRepository ../my-product -Force -Prune
```

- `-Force` is required to replace the installed agents. It overwrites **every** file the payload
  owns: `AGENTS.md`, `.github/agents/`, `.github/skills/`, `.github/standards/`, and the vendored
  `.github/template/`. There is no backup and no diff. Commit first, and expect to restore any
  locally edited standard from the diff afterwards. `AGENTS.md` needs no such care — it carries no
  per-repository values, so replacing it outright is how you get process updates.
- `install.ps1` only writes files, so an agent renamed or removed upstream is left behind — and a
  stale agent file still works and still gets picked. **`-Prune` finds them.** It lists every file in
  the payload directories that this payload does not provide, split into ones Anneal retired and ones
  it does not recognize, and deletes only what you confirm. Answer each group separately: the
  unrecognized group is where your own agents and standards appear, and they are yours to keep.
- Without `-Prune` the installer still counts those files and says so, so an upgrade never leaves a
  stale agent behind silently.
- Then run **`@helper scaffold the template files I am missing`** to create template files added since
  you installed, followed by `@helper patch in template sections I am missing` to insert new sections
  into files you already have. Scaffold is the one
  that matters: `Patch` only touches files that already exist, so on its own it will never create a
  newly-introduced file such as `CONSTRAINTS.md`.

### Upgrading From a Pre-`README.md` Install

Earlier versions kept project facts in a **Project Overview** section of `AGENTS.md`. That section is
gone, and `-Force` will remove it along with the values you filled in. Before upgrading, move them
into `README.md`: the name, description and owner belong in the title, opening paragraphs and
license, and the languages and platform belong in a `## Technology` section, which is what the agents
now read when choosing standards.

The vendored `.github/template/` is also where `AGENTS.md` is resolved from, and the fallback
`template-url` moved when the template did. An install that still has its vendored copy is
unaffected; one that deleted it should re-run the installer to restore it.
