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

## A Worked Example

Two changes to the same imagined product — a small orders API. The first is invisible outside the
code and costs no documentation work at all. The second changes what the repository promises, and
moves the promise before it moves the code. The contrast is the point: most changes look like the
first, and a few look like the second.

### Change 1 — Retry backoff, invisible from outside

The outbound HTTP client currently retries transient errors with a linear backoff. You want to
switch it to exponential, capped at 30 seconds. Callers see the same "retries on transient errors"
behavior; only the timing changes.

```text
@helper switch the outbound retry from linear to exponential backoff, cap at 30 seconds
```

`@helper` confirms the shape back in one sentence: this is interior to the HTTP client, no contract
clause moves, three files change. You confirm.

The resulting diff:

```text
 src/Orders.Api/Http/RetryPolicy.cs             (rewritten)
 src/Orders.Api/Http/HttpClientModule.cs        (2 lines)
 test/Orders.Api.Tests/Http/RetryPolicyTests.cs (rewritten)
```

Nothing under `docs/architecture/` is touched. That is the correct outcome, not an omission — the
backoff algorithm was never something the repository promised, only something it currently did. If
callers had been depending on the timing, that would have been a different change and it would have
started somewhere else.

You run:

```pwsh
pwsh ./build.ps1
pwsh ./lint.ps1
```

Both pass. Done.

### Change 2 — Omit stale orders, moves a published promise

The search endpoint currently returns every matching order. You want to narrow that: orders older
than 90 days should no longer appear.

```text
@helper search should omit orders older than 90 days
```

`@helper` recognizes that this changes what the Search system promises to its callers — its
behavior on the same input actually differs — and sends you briefly to `@architecture-design`
before any code is written. The exchange is short:

```text
Search currently promises: "returns every order matching the filter, most recent first".
This narrows that promise. Proposed new clause:

  SEARCH-04: Search omits orders whose OrderDate is more than 90 days before today.
  Verified by test: SearchOmitsStaleOrders

Confirm and I will land the clause; implementation follows.
```

You confirm. The clause lands in `docs/architecture/search.md`, and the test it names is recorded
as an outstanding implementation obligation — a promise written before the code exists to keep it.
`@helper` picks up from there.

The diff on the implementation pass:

```text
 docs/architecture/search.md                                      (1 clause added)
 src/Orders.Api/Search/OrderSearch.cs                             (12 lines)
 test/Orders.Api.Tests/Search/Contract/SearchOmitsStaleOrders.cs  (added)
```

The new test lives under `Contract/` — that is where tests written against a system's public
surface belong. Interior unit tests stay wherever they already are, and are not touched by this
change.

You run `pwsh ./build.ps1` and `pwsh ./lint.ps1`. Both pass. Done.

## What "The Build Fails" Looks Like

Almost every check in this process is a matter of review and prompt: whether a change is scoped
correctly, whether a clause is pitched at the right level, whether an interior test has earned its
keep. One relationship, and only one, is enforced by machine — **every clause your systems publish
must name a test that exists, sits at the system's public boundary, and passed the last time the
tests ran.**

Suppose, in Change 2 above, the test file had been named `OmitsOldOrdersTest.cs` while the clause
still named `SearchOmitsStaleOrders`. Running `pwsh ./lint.ps1` prints, alongside its other output,
a message like:

```text
docs/architecture/search.md: clause SEARCH-04 names test 'SearchOmitsStaleOrders'
  which is not declared as a test method in test/
```

The build exits non-zero, and CI blocks the change on the same failure. The fix is *not* to edit
the clause to match the test's name — that silently narrows what your repository claims. Either
rename the test to match the clause, or, if the clause is wrong, take the mismatch back through
`@helper` and let it move the promise properly.

Two other messages from the same check are worth recognizing on sight:

```text
docs/architecture/search.md: clause SEARCH-04 names test 'SearchOmitsStaleOrders'
  whose most recent result is 'Failed'
```

The test exists and ran, and did not pass. The code does not yet do what the clause claims — write
the code, don't weaken the clause.

```text
docs/architecture/search.md: clause SEARCH-04 names test 'SearchOmitsStaleOrders'
  which is not in a 'Contract' folder
```

The test exists, but sits with the interior tests. A published promise cannot rest on a test
written against private wiring, because that wiring is free to change without warning. Move the
test into the `Contract/` folder for that system and rewrite it to touch only the public surface.

Everything else — how a request is routed, whether a change deserves a re-cut of the system
boundaries, whether an old interior test has stopped earning its place — is judgement that
`@helper` exercises with you, not a rule the machine enforces.

## When the Result Is Not What You Asked For

If what comes back does not match what you asked for — including a result that is unfinished rather
than done — paste it to `@helper` and say what you expected. It will work out the next step.
