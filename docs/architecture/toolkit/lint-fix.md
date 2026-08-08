---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/LintFixOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/LintFixReport.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/PowerShellScripts.cs
---

[← Toolkit](../toolkit.md)

# LintFix

`lint-fix` drives the repository to a clean `pwsh ./lint.ps1`, or reports why it could not. It runs
`fix.ps1` first, then loops within a bounded budget: run `lint.ps1`, and while it is failing, give a
worker the lint output and the fix guidance and let it edit files. It is the first prose agent
compiled into a process, and it was chosen for that because its success is decided by an exit code
rather than by a judgement — a pathfinder whose success cannot be checked mechanically proves nothing,
because a failure could be the machinery or the judgement with no way to tell which.

The state flow is code and the guidance is data. Running the scripts, counting the budget, reading the
exit code and deciding the outcome are decided here; what to do about a particular lint failure is a
block of text composed into the worker's prompt, exactly as the prose agent stated it. The scripts are
control flow and never tools: the worker is granted the read and edit groups and nothing else, so it
cannot run a command and then report plausibly that it did not.

Four outcomes, not two. A clean lint succeeds and an exhausted budget fails, but a repair that
genuinely requires a protected configuration file *escalates* — the worker is refused the write, the
refusal is a recorded fact rather than a self-report, and the operation says so rather than grinding
its budget editing sources to work around a misconfigured linter. A contract-check failure is the
fourth case and is not a lint issue at all, so it is identified structurally in the lint output and
stops the run: a clause naming a test that does not exist is a semantic disagreement between a
contract and a suite, and a worker told to fix lint would resolve it by renaming one of them.

## Contract

### Provides

- **TOOLKIT-19** — `lint-fix` drives the repository to a clean lint or reports why it could not: it
  succeeds when `lint.ps1` exits zero, escalates when a repair needs a protected configuration file or
  repository script changed, and fails when its bounded budget is exhausted.
  *Verified by:* `ToolkitContractTests.LintFixDrivesTheRepositoryCleanOrReportsWhyNot`
- **TOOLKIT-24** — every script run through `PowerShellScripts` has `ANNEAL_TOOLKIT=1` set in its own
  environment, so a repository script can tell it is running as this process's own child rather than
  from a person's direct invocation, and change its own behavior accordingly (for example, skipping a
  step that would collide with the Toolkit package currently running it).
  *Verified by:* `Toolkit24ScriptEnvironmentContractTests.ScriptsRunUnderTheToolkitSeeTheAnnealToolkitVariable`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every operation is built
  from, and the escalation outcome this operation reports through.
- **[Model Seam](./model-seam.md)** — the worker conversation, the group-scoped tool grant its edits
  go through, and the transcription that makes a refused write a recorded fact.
- **PowerShell** — `pwsh`, which runs the repository's own `fix.ps1` and `lint.ps1`.

## Decisions

**The fix guidance is duplicated, deliberately and temporarily** — the guidance in this operation is
lifted from `.github/agents/lint-fix.agent.md`, which stays in the payload untouched while the
compiled process is proven against this repository. Two copies of a rule can drift, and that is a real
cost; it was accepted for migration stage S6 because the alternative — retiring the prose agent before
the compiled one had driven this repository clean — would have removed the fallback exactly when it
was most likely to be needed. S6's exit condition was met, and a follow-up Change retired the prose
agent (deleted `.github/agents/lint-fix.agent.md`) and this duplication along with it.

**The budget is five iterations, matching the prose agent** — this stage is a compilation of that
agent rather than a redesign of it, so its bound is carried over rather than re-derived. That there is
a bound at all is the point: without one, a worker that cannot fix a failure re-reads the same output
forever, and the run's cost is unbounded while its progress is zero.

**Escalation is decided by an observed refusal followed by no progress, not by asking the model** —
the operation escalates when a worker was refused a protected path and the iteration after that
refusal left the lint output unchanged. A refusal alone is not enough, because a worker may be denied
one path and repair the issue another way; a model's own claim that it needs the file is not enough
either, because that is precisely the self-report the refusal exists to replace. Only a refused write
to a protected path counts: a worker denied a path outside the repository asked for the wrong thing
and can ask again, and escalating on that would tell the user a protected file needs their approval
when none does.

**Cancelling a run kills the script, not just the wait for it** — `fix.ps1` edits files and
`lint.ps1` starts a build, so a script left running after its caller withdrew keeps changing the
repository on behalf of a run that no longer exists. Cancellation stops the whole process tree and
waits for it to be gone before the caller is told the run stopped, because these scripts start
linters and the dotnet CLI as children of their own.

**`ANNEAL_TOOLKIT` is set on every script, not just this repository's own** — found by a real
self-collision hazard, not a hypothetical one: Anneal's own `build.ps1` refreshes the local
`demaconsulting.anneal.toolkit` tool package, and a `route` run against this repository is that exact
package running live. A CLI switch was considered and rejected: `RunRepositoryScript`'s delegate
signature carries no argument-passing hook, so a switch would need extending that whole seam for one
repository's own defense. An environment variable needs no signature change and works for any
repository's own scripts, not only this one's, so `build.ps1` here checks for it directly and skips
its own package-refresh step when it is present.
