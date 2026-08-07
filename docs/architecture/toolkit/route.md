---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/RouteOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/RouteReport.cs
---

[← Toolkit](../toolkit.md)

# Route

`route` is the first action that ever constructs a `Process.Router` outside a throwaway test harness.
Every worker the migration has landed so far — Small Fix, Contract Change, Structural Change — had only
ever run inside interior tests against a fake endpoint. `route` hands a real work item to a real
`Router`, built with the production worker catalog, and runs whichever compiled worker the routing
oracle selects.

It takes the work item as its first argument and any further arguments as changed-file hints, mirroring
`Router.RunAsync`'s own two parameters. What comes back is a projection of the internal `RouterOutcome`
into the public `RouteReport`: the files a completing worker changed and its summary on success, or what
the run tried, learned, and recommends when no worker completed the work.

## Contract

### Provides

- **TOOLKIT-23** — `route` routes a real work item to this repository's own compiled worker catalog —
  `small-fix`, `contract-change`, `structural-change` — through a real Router, and runs whichever
  worker the routing oracle selects. It succeeds when a selected worker completes the work, escalates
  when the routing oracle or a worker names a step only a person can take, and fails when no route
  exists, a routing budget is exhausted, or the selected worker could not complete the work.
  *Verified by:* `ToolkitContractTests.RouteRunsTheSelectedCompiledWorker`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every operation is built
  from, and the escalation outcome this operation reports through.
- **[Model Seam](./model-seam.md)** — every model call the route oracle, any research pass, and the
  selected worker make.
- **[Process](../process.md)** — `Router`, `WorkerDescriptor`/`WorkerCatalogEntry`, and the three
  compiled workers this operation assembles into a production catalog.

## Decisions

**The action name, argument shape, charters, and worker catalog keys were this pass's own judgement
call** — `MIGRATION.md`'s S10 entry names exactly this and delegates the specifics to whoever lands the
stage. `route` was chosen over `develop` or `work` because it reads plainly as "hand this repository a
real piece of work and let the routing oracle decide", which is the whole of what the action does. The
work item is a single positional argument rather than a flag, matching every other action's own
positional style (`probe-rule-owner <rule>`); changed-file hints follow it positionally because
`Router.RunAsync` treats them as an optional list, not a named option.

**Every charter is authored fresh, not lifted from a prose agent** — unlike `lint-fix`, which duplicated
`lint-fix.agent.md`'s own guidance because a prose equivalent already existed, the Router and its three
workers have no prose predecessor: `dispatch` and `apply` play a comparable role today, but their
instructions are written for a conversational agent reading a whole standards tree, not for the bounded
typed questions a route oracle and a worker's own primitives answer. The route charter names each
catalog worker by its exact key so the oracle's answer and `Router`'s own catalog lookup agree by
construction.

**The production catalog registers workers under the same keys their own interior tests already use** —
`small-fix`, `contract-change`, `structural-change` — so a worker's own test fixtures, this operation's
catalog, and the route charter's own prose all name the same three strings. No fourth key was invented
and none was renamed.

**This operation declares `OperationCategory.Authoring` unconditionally** — including on a run that ends
up only researching, refusing to route, or escalating — because the action as a whole is capable of
writing to the repository, matching `lint-fix`'s own reasoning: a caller must not have to know which
path a given invocation happened to take before it can know whether a failure of this action gates a
build.
