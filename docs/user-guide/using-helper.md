# Using Helper

`@helper` is the front door for everything you want done. Say what you want in your own words and it
works out the rest — how far the change reaches, what to route it through, and whether to ask you a
question first.

## How to Ask

Name the **outcome** you want. Do not name agents, modes, tiers, or documents — those are decided
for you.

A request that is already clear is routed straight through with a sentence saying so. When you arrive
with a problem rather than a solution, it asks one question at a time until the work is clear,
confirms the shape back to you, and routes only once you agree.

## Example Prompts

```text
@helper add a --verbose flag to the CLI
```

```text
@helper return 404 instead of 400 when a record is missing
```

```text
@helper the worker keeps losing pushes when the network drops and I'm not sure what we want instead
```

```text
@helper fix the crash when the input file is empty
```

```text
@helper tidy src/Storage: delete dead code and extract the duplicated retry logic, nothing else,
and stop when those two are done
```

```text
@helper file this for later: we will eventually need an S3 storage backend
```

```text
@helper check this repository against the template
```

```text
@helper get this ready for review
```

That last one runs auto-fixers and lint in a loop until the repository is clean. It never refactors
and never makes functional changes.

If your repository has no system boundaries yet, or they need redrawing, `@helper` will send you to
`@architecture-design`. See [First Run](first-run.md) for what that conversation is like.

## When the Result Is Not What You Asked For

If what comes back does not match what you asked for — including a result that is unfinished rather
than done — paste it to `@helper` and say what you expected. It will work out the next step.
