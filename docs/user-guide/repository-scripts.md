# Repository Scripts

Anneal expects the repository it runs against to supply three PowerShell scripts. Each plays a
distinct role in the change pipeline, and each is invoked automatically by workers at the right
point in a change cycle.

## The Three Scripts

### `fix.ps1` — auto-fix

Runs auto-formatters and auto-fixable linters in place. **Must be idempotent**: running it twice on
already-clean code must be a no-op. Anneal runs `fix.ps1` early in a change cycle to apply
formatting before it evaluates whether the repository is clean.

The script owns no correctness gate. A failing formatter exit code will surface, but the script's
job is mutation, not checking — checking is `lint.ps1`'s job.

### `build.ps1` — compile and test

Compiles the project and runs its full test suite. Anneal runs `build.ps1` after every change to
verify nothing is broken. It must **exit non-zero** on any compiler error or test failure.

This is the costliest of the three scripts. Keep it focused on what cannot be checked statically:
compilation and execution of tests.

### `lint.ps1` — deterministic checks

Runs static analysis, style checks, and any other checks that are fast, repeatable, and pure (no
side effects). Anneal runs `lint.ps1` as a gate before proposing or accepting a change.

Must **exit non-zero** if any check fails. Must not modify files — modifications belong in
`fix.ps1`. Because Anneal relies on this script to provide a binary pass/fail signal, a lint script
that exits zero despite failures silently disables the gate.

## Configuring Non-Default Script Names

By default Anneal looks for `fix.ps1`, `build.ps1`, and `lint.ps1` at the repository root. When
your repository uses different names, add a `scripts` section to `.anneal/config.json`:

```json
{
  "scripts": {
    "fix":   "tools/auto-fix.ps1",
    "build": "tools/ci-build.ps1",
    "lint":  "tools/static-analysis.ps1"
  }
}
```

All three keys are optional. Omitting a key makes Anneal fall back to the default name if the
default file exists on disk, or treat the step as absent (passing trivially) if it does not.
Setting a key to an empty string explicitly marks the step as absent — the same outcome as the
default file not existing.

Anneal reads these keys from the `scripts` section via `ScriptConfiguration.Load`, which is the
same configuration file (`/.anneal/config.json`) that `ModelConfiguration` and
`ContractCheckConfiguration` read. A file that exists but cannot be parsed is an error; a missing
file is not.

> **Your scripts, your rules.** Anneal invokes these scripts and interprets their exit code. What
> they actually do — which tools they call, which flags they pass, how they report errors — is
> entirely your responsibility. The examples in the appendix below are illustrative hints, not a
> prescribed toolchain.

---

## Appendix: Bare-Minimum Example Scripts

The following scripts demonstrate the minimal structure Anneal needs. They use .NET CLI commands as
a concrete example; your repository's real scripts will differ. **The content of these files is
owned entirely by the downstream repository.**

### `fix.ps1`

```powershell
#!/usr/bin/env pwsh
# Auto-fix: apply code formatting in place.
# Replace this with whatever formatter(s) your repository uses.
dotnet format
```

### `build.ps1`

```powershell
#!/usr/bin/env pwsh
# Build and test: compiles all projects and runs the full test suite.
# Exits non-zero automatically on any compiler error or test failure.
dotnet test --configuration Release
```

### `lint.ps1`

```powershell
#!/usr/bin/env pwsh
# Lint: run static checks without modifying files.
# Exit non-zero if any check fails so Anneal can gate on the result.
dotnet format --verify-no-changes
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

These are the smallest possible scripts that satisfy the contract (idempotent fix, non-zero on
build/test failure, non-zero on lint failure). Real scripts in a real repository will invoke
additional tools, set culture/encoding, handle parallel runs, and so on — none of that is Anneal's
concern.
