# Getting Started

How to install the Anneal process into a repository and make your first change with it.

## Install the Agents

```pwsh
pwsh ./install.ps1 -TargetRepository ../my-product
```

This lays down the whole payload:

```text
.github/template/AGENTS.pristine.md  → AGENTS.md
.github/agents/                      → .github/agents/
.github/skills/                      → .github/skills/
.github/standards/                   → .github/standards/
.github/template/                    → .github/template/
```

It refuses to overwrite existing files unless you pass `-Force`, so re-running it after an upgrade
will not silently discard local edits.

`AGENTS.md` needs no editing. It holds no project-specific values — what your product is and what it
is written in belong in `README.md`, which the agents read as level 0 of the architecture tree. That
is what makes `-Force` safe on it: an upgrade replaces the file outright and you gain the process
improvements without a merge. If a rule does not fit your repository, change it in Anneal so every
repository gets the fix.

### Why the Template Is Vendored

`template-sync` and `architecture-design` read the canonical template. `AGENTS.md` resolves it from a
vendored `.github/template/` first, then from `template-url`.

The URL is reachable, so those agents can fall back to it. Prefer the vendored copy regardless:
`install.ps1` writes it for you, it needs no network, and it pins the template to the agent versions
installed alongside it. With neither available, those agents can only report INCOMPLETE.

## Lay Down the Structure

For a repository that does not yet have the layout, run:

```text
@helper scaffold the repository structure from the template
```

This creates the files listed in `.github/template/repository-map.md` that do not already exist. It
never overwrites anything.

You can also copy `.github/template/` by hand. The pieces that matter are `docs/architecture/`,
`check-contracts.ps1`, and the `Contract/` subfolder under your test project.

## Establish the Architecture Tree

For a new repository, or one whose system boundaries have drifted:

```text
@architecture-design
```

This is an interactive interview, and one of only two agents you invoke by name — `helper` sends you
here rather than running it, because an interview needs you present. It asks one question at a time,
shows you the system tree as it develops, and writes `docs/architecture/` when you confirm you are
done.

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
.github/template/docs/architecture/overview.md                     → docs/architecture/overview.md
.github/template/docs/architecture/system-name.md                  → docs/architecture/{system}.md
.github/template/docs/architecture/system-name/section-name.md     → only if genuinely needed
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

Expect warnings about unfulfilled obligations at this stage — those are the contract tests you
have not written yet. Errors mean something is actually wrong: a system document with no `## Contract`
section, a clause ID that does not parse (usually a `{SYSTEM}` placeholder you have not replaced), a
duplicate ID, a clause with no test named, or a named test that is not a real test method under
`test/{System}.Tests/Contract/`.

## Your First Change

Everything from here goes to `helper`. Say what you want in your own words:

```text
@helper add a --verbose flag to the CLI
```

It works out how far the change reaches, routes it, and reports what it did. Read the report: the
**Tier** and **Tier Rationale** fields tell you whether the process understood your change the way
you did. If the tier looks wrong, that is worth correcting immediately — misclassification is the
main way this process degrades.

You do not have to know what you want first. When you would rather talk it through, say so and it
asks until the work is clear, confirms it back, and routes only once you agree:

```text
@helper the worker keeps losing pushes when the network drops and I'm not sure what we want instead
```

A request that is already clear is not slowed down by this — it is routed straight through with a
sentence saying so.

## Before Opening a Pull Request

```text
@helper get this ready for review
```

Runs `lint.ps1` in a loop until the repository is clean. Lint compliance is deliberately a pre-PR
step, not something every agent worries about on every edit.

## Next

[Common Tasks](common-tasks.md) is the page to keep open while working — the prompt for each
day-to-day job. [Workflow](workflow.md) explains change tiers properly, including the failure modes
that make this process quietly turn back into a heavyweight one.
