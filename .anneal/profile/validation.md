# Validation

The commands that prove a change to this repository is sound — the file an agent or a CI step reads
to know how to check its own work, distinct from `technology.md`'s facts about what the codebase is
built from.

Descriptive, evolvable, but named as a scope tripwire: any change here escalates to at least Contract
Change scope.

To work on Anneal itself (not a repository that has installed it):

```pwsh
pwsh ./fix.ps1
pwsh ./build.ps1
pwsh ./lint.ps1
```

The order matters. `fix.ps1` applies all available fixers silently and always exits 0. `build.ps1`
runs every test suite and records the results that `lint.ps1` reads when it checks that each promise
still names a passing test — so `build.ps1` must run before `lint.ps1`, or the pass check has no
results to read. `lint.ps1` exits 1 on failure and is the gate CI runs; it includes
`dotnet anneal check-contracts`, described in the `check-contracts` skill.
