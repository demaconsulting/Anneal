# test-check-contracts.ps1
#
# PURPOSE:
#   Exercises .github/template/check-contracts.ps1 against purpose-built fixture
#   repositories, one per documented failure mode.
#
#   check-contracts.ps1 is the only mechanically enforced relationship in this
#   process - README feature 2 is a promise about this script. Every other rule
#   Anneal ships is held by prompt and review, so this is the one place where a
#   regression is silent rather than visible to a reader. The skill file
#   documents twelve distinct failures; this proves each one actually fires, and
#   that the passing case still passes.
#
#   Fixtures are written to a temporary directory and deleted afterwards. No
#   .NET toolchain is required: result files are written directly, which also
#   lets a case control result age and outcome precisely.
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

$script:Checker = Join-Path $PSScriptRoot ".github/template/check-contracts.ps1"
$script:Passed = 0
$script:Failed = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()

# Anneal is its own second consumer of check-contracts.ps1: the clauses in
# docs/architecture/contract-check.md name the cases in this file, so this run is
# also the test run whose outcomes that check reads. Written in the text result
# format rather than TRX because nothing here produces TRX.
$script:Results = Join-Path $PSScriptRoot "artifacts/tests/check-contracts.txt"
$script:Outcomes = [System.Collections.Generic.List[string]]::new()

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

    # Build realistic TRX with testId GUIDs and TestDefinitions, matching the
    # shape dotnet test actually produces.
    $results = @()
    $definitions = @()
    foreach ($entry in $Outcomes) {
        $parts = $entry -split '=', 2
        $testName = $parts[0]
        $outcome = $parts[1]

        # Deterministic GUID derived from the test name
        $md5 = [System.Security.Cryptography.MD5]::Create()
        $hash = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($testName))
        $testId = (New-Object Guid (,$hash)).ToString()

        # Derive className from the qualified name (everything before the last dot)
        $lastDot = $testName.LastIndexOf('.')
        if ($lastDot -gt 0) {
            $className = $testName.Substring(0, $lastDot)
            $methodName = $testName.Substring($lastDot + 1)
        } else {
            $className = ""
            $methodName = $testName
        }

        $results += "    <UnitTestResult testId=`"$testId`" testName=`"$testName`" outcome=`"$outcome`" />"
        $definitions += @"
    <UnitTest name="$testName" id="$testId">
      <TestMethod className="$className" name="$methodName" />
    </UnitTest>
"@
    }

    $rowsBlock = $results -join "`n"
    $defsBlock = $definitions -join "`n"

    $xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
$rowsBlock
  </Results>
  <TestDefinitions>
$defsBlock
  </TestDefinitions>
</TestRun>
"@

    $full = Set-RepoFile -Repo $Repo -Path $File -Content $xml

    # Results must post-date the test sources or check 6 reports them stale, so
    # unless a case is deliberately testing staleness the results are stamped
    # into the near future.
    $stamp = if ($Written -eq [datetime]::MinValue) { (Get-Date).ToUniversalTime().AddMinutes(5) } else { $Written }
    [System.IO.File]::SetLastWriteTimeUtc($full, $stamp)
}

# The non-TRX result form: one result per line, an outcome token then the test
# name. Outcomes is a list of "Test name=Outcome" strings, split on the LAST '='
# so a case name may itself contain one.
function Set-TextResults {
    param(
        [string] $Repo,
        [string] $File = "results/tests.txt",
        [string[]] $Outcomes,
        [datetime] $Written = [datetime]::MinValue
    )

    $lines = ($Outcomes | ForEach-Object {
            $split = $_.LastIndexOf('=')
            "$($_.Substring($split + 1)) $($_.Substring(0, $split))"
        }) -join "`n"

    $full = Set-RepoFile -Repo $Repo -Path $File -Content "# outcome name`n$lines"

    $stamp = if ($Written -eq [datetime]::MinValue) { (Get-Date).ToUniversalTime().AddMinutes(5) } else { $Written }
    [System.IO.File]::SetLastWriteTimeUtc($full, $stamp)
}

# Marks a directory hidden in the platform's own terms: Windows decides by
# attribute, the Unix-like platforms by a dot-prefixed name. Both are needed so
# one fixture asserts the same thing everywhere.
function Set-HiddenDirectory {
    param([string] $Repo, [string] $Path)

    $full = Join-Path $Repo $Path
    New-Item -ItemType Directory -Path $full -Force | Out-Null
    if ($IsWindows) {
        $item = Get-Item -LiteralPath $full -Force
        $item.Attributes = $item.Attributes -bor [System.IO.FileAttributes]::Hidden
    }
}

# The arguments a fixture-case repository needs: its tests are named cases in a
# PowerShell script at the repository root, and its results are not TRX.
function Get-FixtureCaseArguments {
    return @(
        "-TestRoots", ".",
        "-TestFilePatterns", "*.ps1",
        "-TestDeclarationPattern", '^\s*Test-Case\s+-Name\s+"(?<name>[^"]+)"',
        "-ContractTestFolder", "",
        "-TestResults", "results/*.txt",
        "-TestResultFormat", "text")
}

# ==============================================================================
# ASSERTION
# ==============================================================================

function Invoke-Checker {
    param([string] $Repo, [string[]] $Arguments = @(), [switch] $Strict)

    Push-Location $Repo
    try {
        $invocation = @("-NoProfile", "-File", $script:Checker) + $Arguments
        if ($Strict) { $invocation += "-Strict" }
        $output = & pwsh @invocation 2>&1 | Out-String
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
        [string[]] $Arguments = @(),
        [string[]] $Expect = @(),
        [string[]] $Reject = @(),
        [switch] $Strict
    )

    if ($Filter -and $Name -notlike "*$Filter*") {
        Remove-Item -LiteralPath $Repo -Recurse -Force -ErrorAction SilentlyContinue
        return
    }

    $result = Invoke-Checker -Repo $Repo -Arguments $Arguments -Strict:$Strict
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
        $script:Outcomes.Add("Passed $Name")
        Write-Host "  PASS  $Name" -ForegroundColor Green
    }
    else {
        $script:Failed++
        $script:Outcomes.Add("Failed $Name")
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

# One repository proving both halves of CONTRACT-CHECK-10, because a clause can
# name only one verifier: the placeholder form IS reported (and is an error under
# -Strict), and a verifier that is near-miss but not the placeholder form is NOT.
# Each of the three near-miss witnesses differs from `TODO_` at the start in
# exactly one dimension, so each one alone would become an obligation if the
# detector lost that dimension:
#   INGEST-02  Todo_ItemsAreReturned  - right shape, wrong case  -> bites if the
#                                       match stops being case-sensitive
#   INGEST-I1  List_TODO_Items        - right shape, not at the start -> bites if
#                                       the match loses its '^' anchor
#   INGEST-I2  TODOItemsAreReturned   - uppercase TODO at the start with no
#                                       separator -> bites if the match drops the
#                                       '[._]' separator class
# All three are genuine, declared, passing tests and must be checked normally, so
# none of the names may appear in the output at all.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body @'
---
level: system
covers:
  - src/Ingest/**
---

# Ingest

## Contract

### Provides

- **INGEST-01** - Accepts records and returns 202 once durably queued.
  *Verified by:* `TODO_AcceptedRecordIsDurable`

- **INGEST-02** - Returns the outstanding work queue.
  *Verified by:* `Todo_ItemsAreReturned`

### Invariants

- **INGEST-I1** - Records are queued in arrival order.
  *Verified by:* `List_TODO_Items`

- **INGEST-I2** - The queue is never reordered after acceptance.
  *Verified by:* `TODOItemsAreReturned`

## Decisions

Nothing yet.
'@
Set-ContractTests -Repo $repo -Body @'
namespace Ingest.Tests.Contract;

public class IngestContractTests
{
    [Fact]
    public void Todo_ItemsAreReturned()
    {
    }

    [Fact]
    public void List_TODO_Items()
    {
    }

    [Fact]
    public void TODOItemsAreReturned()
    {
    }
}
'@
Set-Trx -Repo $repo -Outcomes @("Todo_ItemsAreReturned=Passed", "List_TODO_Items=Passed",
    "TODOItemsAreReturned=Passed")
Test-Case -Name "a planned obligation is an error under -Strict" -Repo $repo -ExpectExit 1 -Strict `
    -Expect @("4 clauses, 4 test links checked.",
        "error: ",
        "unfulfilled test obligation 'TODO_AcceptedRecordIsDurable'") `
    -Reject @("Todo_ItemsAreReturned", "List_TODO_Items", "TODOItemsAreReturned")

# --- A real test containing 'Todo' is checked, not exempted -----------------
# End-to-end shape check, not the discrimination proof: a genuine failing test
# whose name merely contains 'Todo' is reported as a failure rather than excused
# as an obligation. What makes the detector case-sensitive, anchored and
# separator-bearing is proven by the three near-miss witnesses above.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract).Replace("``AcceptedRecordIsDurable``", "``TodoItemsAreReturned``")
Set-ContractTests -Repo $repo -Body (Get-StandardTests).Replace("AcceptedRecordIsDurable", "TodoItemsAreReturned")
Set-Trx -Repo $repo -Outcomes @("TodoItemsAreReturned=Failed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "a genuine test named Todo... is checked normally" -Repo $repo -ExpectExit 1 `
    -Expect @("whose most recent result is 'Failed'") -Reject @("unfulfilled test obligation")

# --- A genuine test whose name contains uppercase TODO is checked, not exempted
# The obligation is the placeholder form, not the word: a case actually named
# "TODO obligation is an error", declared and passing, is a real verifier - and
# so is every clause verified by a suite file that happens to be called
# TODO-suite.ps1. Matching the whole verifier string reported both as unfulfilled
# obligations, which is a clause passing on a promise nobody checked.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body @'
# Ingest

## Contract

### Provides

- **INGEST-01** - Accepts records.
  *Verified by:* `TODO-suite.ps1: "TODO obligation is an error"`
'@
Set-RepoFile -Repo $repo -Path "TODO-suite.ps1" -Content @'
Test-Case -Name "TODO obligation is an error" -ExpectExit 1
'@ | Out-Null
Set-TextResults -Repo $repo -Outcomes @("TODO obligation is an error=Passed")
Test-Case -Name "a genuine test named TODO... is checked normally" -Repo $repo -ExpectExit 0 -Strict `
    -Arguments (Get-FixtureCaseArguments) `
    -Expect @("1 clauses, 1 test links checked.") `
    -Reject @("error:", "warning:", "unfulfilled test obligation")

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

# --- A repository that is neither C# nor xUnit is checked through patterns ----
# The four things that vary between frameworks are all supplied here: the files
# searched, the declaration shape, the absence of an interior/boundary split in
# the layout, and the result format. Nothing in the fixture is C#.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body @'
---
level: system
covers:
  - suite.ps1
---

# Ingest

## Contract

### Provides

- **INGEST-01** - Accepts records and returns 202 once durably queued.
  *Verified by:* `suite.ps1: "accepted record is durable"`

### Invariants

- **INGEST-I1** - Records are queued in arrival order.
  *Verified by:* `suite.ps1: "records keep arrival order"`

## Decisions

Nothing yet.
'@
Set-RepoFile -Repo $repo -Path "suite.ps1" -Content @'
# A fixture suite: a case is named by a quoted argument, never by an identifier
# in declaration position, so no attribute-and-method pattern can reach it.
Test-Case -Name "accepted record is durable" -ExpectExit 0
Test-Case -Name "records keep arrival order" -ExpectExit 0
'@ | Out-Null
Set-TextResults -Repo $repo -Outcomes @("accepted record is durable=Passed", "records keep arrival order=Passed")
Test-Case -Name "a fixture-case repository is checked through discovery patterns" -Repo $repo -ExpectExit 0 `
    -Arguments (Get-FixtureCaseArguments) `
    -Expect @("2 clauses, 2 test links checked.") -Reject @("error:", "warning:")

# --- A stale non-TRX result is still stale ----------------------------------
# Check 7 is a property of the run, not of the format: a result file written
# before the suite it describes cannot vouch for it whatever its shape.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body @'
# Ingest

## Contract

### Provides

- **INGEST-01** - Accepts records.
  *Verified by:* `suite.ps1: "accepted record is durable"`
'@
Set-RepoFile -Repo $repo -Path "suite.ps1" -Content @'
Test-Case -Name "accepted record is durable" -ExpectExit 0
'@ | Out-Null
Set-TextResults -Repo $repo -Outcomes @("accepted record is durable=Passed") `
    -Written (Get-Date).ToUniversalTime().AddDays(-2)
Test-Case -Name "a stale result in the text format is rejected" -Repo $repo -ExpectExit 1 `
    -Arguments (Get-FixtureCaseArguments) -Expect @("Test results are stale")

# --- A failing non-TRX result fails its clause -------------------------------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body @'
# Ingest

## Contract

### Provides

- **INGEST-01** - Accepts records.
  *Verified by:* `suite.ps1: "accepted record is durable"`
'@
Set-RepoFile -Repo $repo -Path "suite.ps1" -Content @'
Test-Case -Name "accepted record is durable" -ExpectExit 0
'@ | Out-Null
Set-TextResults -Repo $repo -Outcomes @("accepted record is durable=Failed")
Test-Case -Name "a failing result in the text format fails its clause" -Repo $repo -ExpectExit 1 `
    -Arguments (Get-FixtureCaseArguments) -Expect @("whose most recent result is 'Failed'")

# --- Discovery that matches nothing is reported once, as itself --------------
# The repository is entirely well-formed; only the patterns are wrong. Reporting
# a missing test per clause would send a reader off to write tests that already
# exist, which is why the per-clause errors are replaced rather than joined.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "discovery that matches nothing is its own failure" -Repo $repo -ExpectExit 1 `
    -Arguments @("-TestRoots", "no-such-directory") `
    -Expect @("No test declarations found in 'no-such-directory' matching '*.cs'") `
    -Reject @("is not declared as a test method")

# --- ... and the same holds when the file patterns are the wrong ones --------
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Test-Case -Name "file patterns matching no file are a discovery failure" -Repo $repo -ExpectExit 1 `
    -Arguments @("-TestFilePatterns", "*.nope") `
    -Expect @("No test declarations found", "*.nope") `
    -Reject @("is not declared as a test method")

# --- Bootstrap: planned clauses with no tests yet are not a discovery failure -
# The escape hatch that keeps the check usable while a tree is being written. A
# clause naming a TODO obligation is not expected to resolve to anything, so a
# repository with no test sources at all stays green until it claims otherwise.
$repo = New-Repo
$planned = (Get-StandardContract).Replace("``AcceptedRecordIsDurable``", "``TODO_AcceptedRecordIsDurable``")
$planned = $planned.Replace("``PreservesPerConnectionOrder``", "``TODO_PreservesPerConnectionOrder``")
Set-SystemDoc -Repo $repo -Body $planned
Test-Case -Name "a tree of planned clauses with no test sources is not a discovery failure" -Repo $repo -ExpectExit 0 `
    -Expect @("unfulfilled test obligation") -Reject @("error:", "No test declarations found")

# --- A stale copy under a hidden directory does not keep a clause alive -------
# Discovery skips hidden directories, as Get-ChildItem without -Force does.
# Walking them is the fail-open direction: the live test was deleted, and only a
# copy under test/.old still declares it.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests).Replace("AcceptedRecordIsDurable", "SomeOtherTest")
Set-RepoFile -Repo $repo -Path "test/.old/Contract/OldTests.cs" -Content (Get-StandardTests) | Out-Null
Set-HiddenDirectory -Repo $repo -Path "test/.old"
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "a hidden directory does not supply test declarations" -Repo $repo -ExpectExit 1 `
    -Expect @("names test 'AcceptedRecordIsDurable' which is not declared as a test method")

# --- A wildcard test root is expanded, not thrown at --------------------------
# Undocumented but previously working input, and it reaches every downstream
# caller: a glob root must produce a report rather than a raw PowerShell error.
$repo = New-Repo
Set-SystemDoc -Repo $repo -Body (Get-StandardContract)
Set-ContractTests -Repo $repo -Body (Get-StandardTests)
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed")
Test-Case -Name "a wildcard test root is expanded" -Repo $repo -ExpectExit 0 `
    -Arguments @("-TestRoots", "test/*") `
    -Expect @("2 clauses, 2 test links checked.") -Reject @("error:", "warning:")

# A profile pair for a repository holding both kinds of test at once: C# boundary
# tests recorded in TRX, and a root-level PowerShell suite of named cases
# recording a text tally. Kept as constants because most of the profile cases
# below vary exactly one field of one of them, and the case reads better when
# what it changed is the only thing written out.
$script:CSharpProfile = "TestRoots=test;TestFilePatterns=*.cs;ContractTestFolder=Contract;TestResults=artifacts/tests/*.trx;TestResultFormat=trx"
$script:ScriptProfile = 'TestRoots=.;TestFilePatterns=suite.ps1;TestDeclarationPattern=^\s*Test-Case\s+-Name\s+"(?<name>[^"]+)";ContractTestFolder=;TestResults=results/*.txt;TestResultFormat=text'

# Profiles reach the script as ONE argument holding newline-separated records:
# under `pwsh -File` a second value of an array parameter binds positionally and
# is discarded, so passing them as separate arguments would silently check only
# the first framework.
function Get-ProfileArguments {
    param([string[]] $Records)

    return @("-TestProfiles", ($Records -join "`n"))
}

# A repository whose contract is verified in two languages at once: one clause by
# a C# boundary test, one by a named case in a PowerShell suite.
function Set-MixedRepository {
    param([string] $Repo)

    Set-SystemDoc -Repo $Repo -Body @'
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

### Invariants

- **INGEST-I1** - Records are queued in arrival order.
  *Verified by:* `suite.ps1: "records keep arrival order"`

## Decisions

Nothing yet.
'@

    Set-ContractTests -Repo $Repo -Body @'
namespace Ingest.Tests.Contract;

public class IngestContractTests
{
    [Fact]
    public void AcceptedRecordIsDurable()
    {
    }
}
'@

    Set-RepoFile -Repo $Repo -Path "suite.ps1" -Content @'
Test-Case -Name "records keep arrival order" -ExpectExit 0
'@ | Out-Null
}

# --- Two profiles resolve clauses in two languages in one invocation ----------
# The case the profile facility exists for: no combination of the single-profile
# parameters expresses this repository, because -ContractTestFolder must be
# 'Contract' for the C# test and empty for the flat suite at the same time.
$repo = New-Repo
Set-MixedRepository -Repo $repo
Set-Trx -Repo $repo -Outcomes @("Ingest.Tests.Contract.IngestContractTests.AcceptedRecordIsDurable=Passed")
Set-TextResults -Repo $repo -Outcomes @("records keep arrival order=Passed")
Test-Case -Name "two discovery profiles resolve clauses in both languages" -Repo $repo -ExpectExit 0 `
    -Arguments (Get-ProfileArguments @($script:CSharpProfile, $script:ScriptProfile)) `
    -Expect @("2 clauses, 2 test links checked.") -Reject @("error:", "warning:")

# --- A profile that matches nothing is an error, not a silent skip ------------
# The fail-closed property that makes the facility safe to add: the C# profile
# still discovers everything it did before, so a whole-run emptiness check would
# report success while one framework went entirely unchecked.
$repo = New-Repo
Set-MixedRepository -Repo $repo
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed")
Set-TextResults -Repo $repo -Outcomes @("records keep arrival order=Passed")
Test-Case -Name "a profile matching no test declarations is an error" -Repo $repo -ExpectExit 1 `
    -Arguments (Get-ProfileArguments @($script:CSharpProfile,
        ($script:ScriptProfile -replace 'TestFilePatterns=suite\.ps1', 'TestFilePatterns=renamed.ps1'))) `
    -Expect @("profile 2: No test declarations found in '.' matching 'renamed.ps1'")

# --- A failing test in the second profile still fails its clause --------------
# Results pool across profiles, so this proves the pooling did not lose the
# outcome of the framework that is not the first one.
$repo = New-Repo
Set-MixedRepository -Repo $repo
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed")
Set-TextResults -Repo $repo -Outcomes @("records keep arrival order=Failed")
Test-Case -Name "a failing test in the second profile fails its clause" -Repo $repo -ExpectExit 1 `
    -Arguments (Get-ProfileArguments @($script:CSharpProfile, $script:ScriptProfile)) `
    -Expect @("clause INGEST-I1 names test 'suite.ps1: `"records keep arrival order`"' whose most recent result is 'Failed'")

# --- Missing results are reported against the profile that expected them ------
$repo = New-Repo
Set-MixedRepository -Repo $repo
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed")
Test-Case -Name "results missing for one profile are reported against that profile" -Repo $repo -ExpectExit 1 `
    -Arguments (Get-ProfileArguments @($script:CSharpProfile, $script:ScriptProfile)) `
    -Expect @("profile 2: No test results matching 'results/*.txt'",
    "which has no result - it did not run")

# --- Staleness is judged within a profile, not across them --------------------
$repo = New-Repo
Set-MixedRepository -Repo $repo
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed")
Set-TextResults -Repo $repo -Outcomes @("records keep arrival order=Passed") `
    -Written (Get-Date).ToUniversalTime().AddDays(-2)
Test-Case -Name "a stale result in one profile is rejected while the other is fresh" -Repo $repo -ExpectExit 1 `
    -Arguments (Get-ProfileArguments @($script:CSharpProfile, $script:ScriptProfile)) `
    -Expect @("profile 2: Test results are stale", "suite.ps1")

# --- A misspelled profile field is rejected rather than defaulted -------------
# The whole point of a closed field set: a field name the script does not know
# would otherwise take its default silently, and the profile would check
# something other than what the call site says it checks.
$repo = New-Repo
Set-MixedRepository -Repo $repo
Set-Trx -Repo $repo -Outcomes @("AcceptedRecordIsDurable=Passed")
Test-Case -Name "an unknown profile field is rejected" -Repo $repo -ExpectExit 1 `
    -Arguments (Get-ProfileArguments @("TestRoots=test;TestFilePattern=*.cs")) `
    -Expect @("profile 1: unknown field 'TestFilePattern'")

# --- A field that is not Key=Value is rejected -------------------------------
$repo = New-Repo
Set-MixedRepository -Repo $repo
Test-Case -Name "a profile field that is not Key=Value is rejected" -Repo $repo -ExpectExit 1 `
    -Arguments (Get-ProfileArguments @("TestRoots=test;*.cs")) `
    -Expect @("profile 1: '*.cs' is not a Key=Value field")

# --- A result format no reader implements is rejected ------------------------
# -TestResultFormat is a ValidateSet on the parameter; a profile field has to
# make the same rejection itself or the profile form would be the looser one.
$repo = New-Repo
Set-MixedRepository -Repo $repo
Test-Case -Name "an unknown result format in a profile is rejected" -Repo $repo -ExpectExit 1 `
    -Arguments (Get-ProfileArguments @("TestRoots=test;TestResultFormat=junit")) `
    -Expect @("profile 1: TestResultFormat 'junit' is not one of: trx, text")

# --- Profiles and the parameters they replace cannot both be supplied ---------
# Rejected rather than merged: whichever won would be invisible at the call
# site, and the call site is where a repository's layout is meant to be readable.
$repo = New-Repo
Set-MixedRepository -Repo $repo
Test-Case -Name "profiles cannot be combined with the parameters they replace" -Repo $repo -ExpectExit 1 `
    -Arguments ((Get-ProfileArguments @($script:CSharpProfile)) + @("-TestRoots", "test")) `
    -Expect @("-TestProfiles cannot be combined with -TestRoots")

# ==============================================================================
# REPORT
# ==============================================================================

Write-Host ""
Write-Host "  $script:Passed passed, $script:Failed failed." -ForegroundColor ($script:Failed -gt 0 ? "Red" : "Green")
foreach ($failure in $script:Failures) { Write-Host "  failed: $failure" -ForegroundColor Red }

# A filtered run has not exercised every case, so recording it would leave the
# contract check reporting the unrun ones as having no result - which is exactly
# what it should report, but for the wrong reason.
if (-not $Filter) {
    New-Item -ItemType Directory -Path (Split-Path $script:Results -Parent) -Force | Out-Null
    Set-Content -LiteralPath $script:Results -Value $script:Outcomes -Encoding utf8
    Write-Host "  Results written to $script:Results"
}

exit ($script:Failed -gt 0 ? 1 : 0)
