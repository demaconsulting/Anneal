# test-check-contracts.ps1
#
# PURPOSE:
#   Exercises template/check-contracts.ps1 against purpose-built fixture
#   repositories, one per documented failure mode.
#
#   check-contracts.ps1 is the only mechanically enforced relationship in this
#   process - README feature 2 is a promise about this script. Every other rule
#   Anneal ships is held by prompt and review, so this is the one place where a
#   regression is silent rather than visible to a reader. The skill file
#   documents eleven distinct failures; this proves each one actually fires, and
#   that the passing case still passes.
#
#   Fixtures are written to a temporary directory and deleted afterwards. No
#   .NET toolchain is required: TRX files are written directly, which also lets
#   a case control result age and outcome precisely.
#
# USAGE:
#   pwsh ./test-check-contracts.ps1
#   pwsh ./test-check-contracts.ps1 -Filter stale

[CmdletBinding()]
param(
    # Run only cases whose name contains this substring.
    [string] $Filter = ""
)

$ErrorActionPreference = "Stop"

$script:Checker = Join-Path $PSScriptRoot "template/check-contracts.ps1"
$script:Passed = 0
$script:Failed = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path $script:Checker)) {
    Write-Host "check-contracts.ps1 not found at $script:Checker" -ForegroundColor Red
    exit 1
}

# ==============================================================================
# FIXTURE CONSTRUCTION
# ==============================================================================

function New-Repo {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("anneal-fixture-" + [guid]::NewGuid().ToString("N").Substring(0, 12))
    New-Item -ItemType Directory -Path (Join-Path $root "docs/architecture") -Force | Out-Null
    return $root
}

function Set-RepoFile {
    param([string] $Repo, [string] $Path, [string] $Content)

    $full = Join-Path $Repo $Path
    New-Item -ItemType Directory -Path (Split-Path $full -Parent) -Force | Out-Null
    Set-Content -LiteralPath $full -Value $Content -Encoding utf8
    return $full
}

function Set-SystemDoc {
    param([string] $Repo, [string] $Name = "ingest.md", [string] $Body)

    Set-RepoFile -Repo $Repo -Path "docs/architecture/$Name" -Content $Body | Out-Null
}

# A conventional system document: two provided clauses and one invariant, plus a
# Requires subsection whose bolded entries deliberately carry no clause ID.
function Get-StandardContract {
    return @'
---
level: system
covers:
  - src/Ingest/**
---

# Ingest

## Contract

### Provides

- **INGEST-01** - Accepts records and returns 202 once durably queued.
  *Verified by:* `AcceptedRecordIsDurable`

### Requires

- **Store** - durable append with at-least-once delivery.

### Invariants

- **INGEST-I1** - Records are queued in arrival order.
  *Verified by:* `PreservesPerConnectionOrder`

## Decisions

Nothing yet.
'@
}

function Set-ContractTests {
    param([string] $Repo, [string] $Body)

    Set-RepoFile -Repo $Repo -Path "test/Ingest.Tests/Contract/IngestContractTests.cs" -Content $Body | Out-Null
}

function Get-StandardTests {
    return @'
namespace Ingest.Tests.Contract;

public class IngestContractTests
{
    [Fact]
    public void AcceptedRecordIsDurable()
    {
    }

    [Fact]
    public void PreservesPerConnectionOrder()
    {
    }
}
'@
}

# Outcomes is an ordered list of "TestName=Outcome" strings. Names may be fully
# qualified or bare, and may carry a data-driven suffix such as "Name(x: 1)".
function Set-Trx {
    param(
        [string] $Repo,
        [string] $File = "artifacts/tests/results.trx",
        [string[]] $Outcomes,
        [datetime] $Written = [datetime]::MinValue
    )

    $rows = ($Outcomes | ForEach-Object {
            $parts = $_ -split '=', 2
            "    <UnitTestResult testName=`"$($parts[0])`" outcome=`"$($parts[1])`" />"
        }) -join "`n"

    $xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
$rows
  </Results>
</TestRun>
"@

    $full = Set-RepoFile -Repo $Repo -Path $File -Content $xml

    # Results must post-date the test sources or check 6 reports them stale, so
    # unless a case is deliberately testing staleness the results are stamped
    # into the near future.
    $stamp = if ($Written -eq [datetime]::MinValue) { (Get-Date).ToUniversalTime().AddMinutes(5) } else { $Written }
    [System.IO.File]::SetLastWriteTimeUtc($full, $stamp)
}

# ==============================================================================
# ASSERTION
# ==============================================================================

function Invoke-Checker {
    param([string] $Repo, [switch] $Strict)

    Push-Location $Repo
    try {
        $arguments = @("-NoProfile", "-File", $script:Checker)
        if ($Strict) { $arguments += "-Strict" }
        $output = & pwsh @arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    finally {
        Pop-Location
    }
}

function Test-Case {
    param(
        [string] $Name,
        [string] $Repo,
        [int] $ExpectExit,
        [string[]] $Expect = @(),
        [string[]] $Reject = @(),
        [switch] $Strict
    )

    if ($Filter -and $Name -notlike "*$Filter*") {
        Remove-Item -LiteralPath $Repo -Recurse -Force -ErrorAction SilentlyContinue
        return
    }

    $result = Invoke-Checker -Repo $Repo -Strict:$Strict
    $problems = [System.Collections.Generic.List[string]]::new()

    if ($result.ExitCode -ne $ExpectExit) {
        $problems.Add("expected exit $ExpectExit, got $($result.ExitCode)")
    }
    foreach ($pattern in $Expect) {
        if ($result.Output -notmatch [regex]::Escape($pattern)) {
            $problems.Add("expected output to contain '$pattern'")
        }
    }
    foreach ($pattern in $Reject) {
        if ($result.Output -match [regex]::Escape($pattern)) {
            $problems.Add("expected output NOT to contain '$pattern'")
        }
    }

    if ($problems.Count -eq 0) {
        $script:Passed++
        Write-Host "  PASS  $Name" -ForegroundColor Green
    }
    else {
        $script:Failed++
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        foreach ($problem in $problems) { Write-Host "          $problem" -ForegroundColor Red }
        $script:Failures.Add($Name)
        Write-Host ($result.Output.TrimEnd() -split "`n" | ForEach-Object { "          | $_" }) -Separator "`n" -ForegroundColor DarkGray
    }

    Remove-Item -LiteralPath $Repo -Recurse -Force -ErrorAction SilentlyContinue
}

# ==============================================================================
# CASES
# ==============================================================================

Write-Host "Testing: check-contracts.ps1"

# --- The passing case -------------------------------------------------------
# Everything else in this file asserts that a failure fires. This asserts that a
# correct repository is not flagged - without it, a checker that failed on
# everything would score perfectly.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("Ingest.Tests.Contract.IngestContractTests.AcceptedRecordIsDurable=Passed",
    "Ingest.Tests.Contract.IngestContractTests.PreservesPerConnectionOrder=Passed")
Test-Case -Name "clean repository passes" -Repo $repo -ExpectExit 0 `
    -Expect @("2 clauses, 2 test links checked.") -Reject @("error:", "warning:")

# --- Failure: no ## Contract section ----------------------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body "# Ingest`n`n## Decisions`n`nNothing.`n"
Test-Case -Name "system document with no Contract section" -Repo $repo -ExpectExit 1 `
    -Expect @("has no '## Contract' section")

# --- Failure: malformed clause ID -------------------------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body "# Ingest`n`n## Contract`n`n### Provides`n`n- **{SYSTEM}-01** - unresolved placeholder.`n"
Test-Case -Name "unresolved placeholder is not a well-formed ID" -Repo $repo -ExpectExit 1 `
    -Expect @("is not a well-formed clause ID")

# --- Failure: duplicate clause ID -------------------------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-SystemDoc -Repo $repo -Name "store.md" -Body (Get-StandardContract).Replace("Ingest", "Store")
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "duplicate clause ID across two documents" -Repo $repo -ExpectExit 1 `
    -Expect @("Duplicate clause ID 'INGEST-01'")

# --- Failure: clause names no test ------------------------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body "# Ingest`n`n## Contract`n`n### Provides`n`n- **INGEST-01** - a promise with no verification.`n"
Test-Case -Name "clause naming no verifying test" -Repo $repo -ExpectExit 1 `
    -Expect @("names no verifying test")

# --- Failure: named test does not exist -------------------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests).Replace("AcceptedRecordIsDurable", "RenamedAwayFromTheClause")
Test-Case -Name "clause naming a test that was renamed away" -Repo $repo -ExpectExit 1 `
    -Expect @("which is not declared as a test method")

# --- Failure: the name survives only in a comment ---------------------------
# Documented in SKILL.md: comments are stripped before matching, so a commented
# out declaration cannot keep a deleted promise alive. The commented lines sit
# directly beneath a real attribute, which is the case that would otherwise
# resolve - a doc comment naming the clause's test is the common form.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body @'
namespace Ingest.Tests.Contract;

public class IngestContractTests
{
    [Fact]
    // public void AcceptedRecordIsDurable()
    public void SomethingElseEntirely()
    {
    }

    [Fact]
    /* public void PreservesPerConnectionOrder() */
    public void AlsoSomethingElse()
    {
    }
}
'@
Test-Case -Name "test surviving only in a comment does not satisfy a clause" -Repo $repo -ExpectExit 1 `
    -Expect @("clause INGEST-01 names test 'AcceptedRecordIsDurable' which is not declared as a test method",
    "clause INGEST-I1 names test 'PreservesPerConnectionOrder' which is not declared as a test method")

# --- Failure: the named test is an interior test ----------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests).Replace("AcceptedRecordIsDurable", "PlaceholderTest")
Set-RepoFile -Repo $repo -Path "test/Ingest.Tests/InteriorTests.cs" -Content @'
namespace Ingest.Tests;

public class InteriorTests
{
    [Fact]
    public void AcceptedRecordIsDurable()
    {
    }
}
'@ | Out-Null
Test-Case -Name "clause pointing at an interior test" -Repo $repo -ExpectExit 1 `
    -Expect @("is not in a 'Contract' folder", "contract tests must be boundary tests")

# --- Failure: the test ran and did not pass ---------------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Failed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "clause whose test most recently failed" -Repo $repo -ExpectExit 1 `
    -Expect @("whose most recent result is 'Failed'")

# --- Failure: the test is declared but never ran ----------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed")
Test-Case -Name "declared test that did not run" -Repo $repo -ExpectExit 1 `
    -Expect @("which has no result - it did not run")

# --- Failure: results predate the tests -------------------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed") `
    -Written (Get-Date).ToUniversalTime().AddDays(-2)
Test-Case -Name "stale results are rejected" -Repo $repo -ExpectExit 1 `
    -Expect @("Test results are stale")

# --- A stale PASS must not vouch for a fresh FAIL ---------------------------
# The newest result for a test wins. Unioning passes across result files let an
# old run keep a currently failing clause green.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -File "artifacts/tests/old.trx" `
    -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed") `
    -Written (Get-Date).ToUniversalTime().AddMinutes(4)
Set-Trx -Repo $repo -File "artifacts/tests/new.trx" `
    -Outcomes @("AcceptedRecordIsDurable=Failed", "PreservesPerConnectionOrder=Passed") `
    -Written (Get-Date).ToUniversalTime().AddMinutes(9)
Test-Case -Name "older passing run cannot vouch for a newer failure" -Repo $repo -ExpectExit 1 `
    -Expect @("whose most recent result is 'Failed'")

# --- A failing case must not be masked by a passing sibling -----------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable(size: 1)=Failed",
    "AcceptedRecordIsDurable(size: 2)=Passed",
    "PreservesPerConnectionOrder=Passed")
Test-Case -Name "one failing data-driven case fails the clause" -Repo $repo -ExpectExit 1 `
    -Expect @("whose most recent result is 'Failed'")

# --- TODO obligations: warning by default, error under -Strict --------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract).Replace("``AcceptedRecordIsDurable``", "``TODO_AcceptedRecordIsDurable``")
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("PreservesPerConnectionOrder=Passed")
Test-Case -Name "TODO obligation is a warning by default" -Repo $repo -ExpectExit 0 `
    -Expect @("warning: ", "unfulfilled test obligation") -Reject @("error:")

$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract).Replace("``AcceptedRecordIsDurable``", "``TODO_AcceptedRecordIsDurable``")
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("PreservesPerConnectionOrder=Passed")
Test-Case -Name "TODO obligation is an error under -Strict" -Repo $repo -ExpectExit 1 -Strict `
    -Expect @("error: ", "unfulfilled test obligation")

# --- A real test containing 'Todo' is checked, not exempted -----------------
# The TODO match is case-sensitive on purpose: exempting TodoItemsAreReturned
# would silently drop the only enforced check in the process.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract).Replace("``AcceptedRecordIsDurable``", "``TodoItemsAreReturned``")
Set-ContractTests -Repo $repo -Body (Get-StandardTests).Replace("AcceptedRecordIsDurable", "TodoItemsAreReturned")
Set-Trx -Repo $repo -Outcomes @("TodoItemsAreReturned=Failed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "a genuine test named Todo... is checked normally" -Repo $repo -ExpectExit 1 `
    -Expect @("whose most recent result is 'Failed'") -Reject @("unfulfilled test obligation")

# --- Missing results: warning by default, error under -Strict ---------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Test-Case -Name "absent results warn by default" -Repo $repo -ExpectExit 0 `
    -Expect @("No test results matching") -Reject @("error:")

$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Test-Case -Name "absent results are an error under -Strict" -Repo $repo -ExpectExit 1 -Strict `
    -Expect @("error: ", "No test results matching")

# --- Clauses inside fenced blocks are examples, not live promises -----------
# system-contracts.md and the templates carry fenced contract examples. If the
# parser read them, every repository would inherit their clauses.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body @'
# Ingest

## Contract

### Provides

- **INGEST-01** - Accepts records.
  *Verified by:* `AcceptedRecordIsDurable`

## Decisions

An example of the shape, which is not a live clause:

```markdown
### Provides

- **EXAMPLE-99** - illustrative only.
```
'@
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed")
Test-Case -Name "fenced example clauses are ignored" -Repo $repo -ExpectExit 0 `
    -Expect @("1 clauses, 1 test links checked.") -Reject @("EXAMPLE-99", "error:")

# --- Requires entries carry no ID and must not be flagged -------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "Requires entries are not treated as clauses" -Repo $repo -ExpectExit 0 `
    -Reject @("'Store' under", "error:")

# --- overview.md never carries a contract -----------------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-SystemDoc -Repo $repo -Name "overview.md" -Body "# Architecture Overview`n`nNo contract here.`n"
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "overview.md is exempt from the contract requirement" -Repo $repo -ExpectExit 0 `
    -Reject @("overview.md", "error:")

# --- An empty tree is not a failure -----------------------------------------
$repo = New-Repo
Test-Case -Name "repository with no clauses reports nothing to check" -Repo $repo -ExpectExit 0 `
    -Expect @("nothing to check") -Reject @("error:")

# --- A clause naming a prefix of a real test is not satisfied ---------------
# Matching was once substring-based, which let a clause point at a name that
# merely appeared inside a longer one.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract).Replace("``AcceptedRecordIsDurable``", "``AcceptedRecord``")
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "a clause naming a prefix of a real test is not satisfied" -Repo $repo -ExpectExit 1 `
    -Expect @("clause INGEST-01 names test 'AcceptedRecord' which is not declared as a test method")

# --- A result whose name merely ends with the clause's test does not count ---
# Qualified names are matched on a dot boundary, so OtherAcceptedRecordIsDurable
# cannot report a result on behalf of AcceptedRecordIsDurable.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("Ingest.Tests.Contract.OtherAcceptedRecordIsDurable=Passed",
    "PreservesPerConnectionOrder=Passed")
Test-Case -Name "a result matching only as a suffix does not count" -Repo $repo -ExpectExit 1 `
    -Expect @("clause INGEST-01 names test 'AcceptedRecordIsDurable' which has no result - it did not run")

# --- Results outside the configured location are not found ------------------
# The whole glob is honored, not just its leaf, so a stray .trx elsewhere in the
# tree cannot satisfy the pass check.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -File "other/tests/results.trx" `
    -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "a .trx outside the configured location is ignored" -Repo $repo -ExpectExit 0 `
    -Expect @("No test results matching") -Reject @("error:")

# ==============================================================================
# REPORT
# ==============================================================================

Write-Host ""
Write-Host "  $script:Passed passed, $script:Failed failed." -ForegroundColor ($script:Failed -gt 0 ? "Red" : "Green")
foreach ($failure in $script:Failures) { Write-Host "  failed: $failure" -ForegroundColor Red }

exit ($script:Failed -gt 0 ? 1 : 0)
