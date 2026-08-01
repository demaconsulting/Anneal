# Getting Started

How to install the Anneal process into a repository and make your first change with it.

## Install the Agents

```pwsh
pwsh ./install.ps1 -TargetRepository ../my-product
```

This lays down the whole payload:

```text
agents/AGENTS.md            → AGENTS.md
agents/.github/agents/      → .github/agents/
agents/.github/skills/      → .github/skills/
agents/.github/standards/   → .github/standards/
template/                   → .github/template/
```

It refuses to overwrite existing files unless you pass `-Force`, so re-running it after an upgrade
will not silently discard local edits.

Then open `AGENTS.md` and replace the `TODO` values in the **Project Overview** section. Everything
else in that file is process definition and should not be edited per-repository — if a rule does not
fit your repository, change it in Anneal so every repository gets the fix.

### Why the Template Is Vendored

`template-sync` and `architecture-design` read the canonical template. `AGENTS.md` resolves it from a
vendored `.github/template/` first, then from `template-url`.

**Until Anneal is published, the URL is not reachable**, so without the vendored copy those agents
can only report INCOMPLETE. `install.ps1` vendors it for you. Doing so also pins the template to the
agent versions installed alongside it, which is worth having even once the URL works.

## Lay Down the Structure

For a repository that does not yet have the layout, run:

```text
@template-sync Scaffold
```

This creates the files listed in `template/repository-map.md` that do not already exist. It never
overwrites anything.

You can also copy `template/` by hand. The pieces that matter are `docs/architecture/`,
`check-contracts.ps1`, and the `Contract/` subfolder under your test project.

## Establish the Architecture Tree

For a new repository, or one whose system boundaries have drifted:

```text
@architecture-design
```

This is an interactive interview. It asks one question at a time, shows you the system tree as it
develops, and writes `docs/architecture/` when you confirm you are done.

The interview spends most of its time on **system boundaries**, because a boundary is where a
contract lives, and a contract is what makes everything inside it free to change. Boundaries in the
wrong place are the one mistake that is expensive to correct later. Expect questions like "what could
be replaced wholesale without the rest noticing?" — answer them concretely.

It will write contract clauses naming tests that do not exist yet, and list them as implementation
obligations. That is intended.

### Doing It By Hand

If you would rather write the tree yourself, copy the three templates and read
[Authoring](authoring.md):

```text
template/docs/architecture/overview.md                     → docs/architecture/overview.md
template/docs/architecture/system-name.md                  → docs/architecture/{system}.md
template/docs/architecture/system-name/section-name.md     → only if genuinely needed
```

Delete every `TEMPLATE-DIRECTIVE` comment block as you fill each section in.

## Verify the Setup

```pwsh
pwsh ./build.ps1
pwsh ./check-contracts.ps1
```

Run `build.ps1` first — the pass check reads its TRX results, and without them it can only report
that it verified nothing. The scaffolded test stubs fail deliberately until you write them, so a red
first run is expected here.

Expect warnings about unfulfilled `TODO` obligations at this stage — those are the contract tests you
have not written yet. Errors mean something is actually wrong: a system document with no `## Contract`
section, a clause ID that does not parse (usually a `{SYSTEM}` placeholder you have not replaced), a
duplicate ID, a clause with no test named, or a named test that is not a real test method under
`test/{System}.Tests/Contract/`.

## Your First Change

```text
@dispatch add a --verbose flag to the CLI
```

`dispatch` classifies the change tier, routes it, and reports what it did. Read its report: the
**Tier** and **Tier Rationale** fields tell you whether the process understood your change the way
you did. If the tier looks wrong, that is worth correcting immediately — misclassification is the
main way this process degrades.

For a change where you already know the approach and it clearly touches nothing outside a system,
skip the routing:

```text
@developer extract the retry logic in HttpClient into its own class
```

## Before Opening a Pull Request

```text
@lint-fix
```

Runs `lint.ps1` in a loop until the repository is clean. Lint compliance is deliberately a pre-PR
step, not something every agent worries about on every edit.

## Next

[Common Tasks](common-tasks.md) is the page to keep open while working — the prompt for each
day-to-day job. [Workflow](workflow.md) explains change tiers properly, including the failure modes
that make this process quietly turn back into a heavyweight one.
