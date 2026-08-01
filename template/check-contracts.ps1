# check-contracts.ps1
#
# PURPOSE:
#   Deterministically verifies that every system contract clause in
#   docs/architecture/*.md names a test that actually exists - and, when test
#   results are available, that the test passed.
#
#   A clause naming a test is a promise. Without this check that promise is
#   unenforced prose: rename or delete the test and nothing breaks, so the
#   contract silently rots. This script is the cheapest possible insurance
#   against that.
#
# CHECKS:
#   1. Clause IDs are unique across the repository
#   2. Every clause names at least one verifying test
#   3. Every named test exists in the test sources
#   4. Every named test passed, when .trx results are present
#
# EXIT CODES:
#   0 - all checks passed (warnings do not fail)
#   1 - one or more errors
#
# MODIFICATION POLICY:
#   Only modify to adjust paths for a non-standard repository layout, or to add
#   support for an additional test result format.

[CmdletBinding()]
param(
    # Root of the architecture tree containing system documents.
    [string] $ArchitectureRoot = "docs/architecture",

    # Roots searched for test source files that define the named tests.
    [string[]] $TestRoots = @("test", "tests"),

    # Glob for test result files. Missing results downgrade check 4 to a notice.
    [string] $TestResults = "artifacts/**/*.trx",

    # Treat unfulfilled TODO obligations as errors rather than warnings.
    [switch] $Strict
)

$ErrorActionPreference = "Stop"

$errors = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

# ==============================================================================
# EXTRACT CLAUSES
# A clause is a list item opening with a bolded ID, optionally followed within
# the same list item by a "*Verified by:*" line naming backticked test names.
# ==============================================================================

function Get-ContractClauses {
    param([string] $Root)

    $clauses = [System.Collections.Generic.List[object]]::new()

    if (-not (Test-Path $Root)) { return $clauses }

    # Only level 2 system documents carry contracts; overview and section
    # documents never do, so scanning the whole tree would invite false matches.
    $systemDocs = Get-ChildItem -Path $Root -Filter "*.md" -File |
        Where-Object { $_.Name -ne "overview.md" }

    foreach ($doc in $systemDocs) {
        $lines = Get-Content -LiteralPath $doc.FullName
        $inContract = $false
        $current = $null

        foreach ($line in $lines) {
            # A top-level heading other than Contract closes the contract block.
            if ($line -match '^##\s+(.+?)\s*$') {
                if ($current) { $clauses.Add($current); $current = $null }
                $inContract = ($Matches[1] -eq 'Contract')
                continue
            }

            if (-not $inContract) { continue }

            if ($line -match '^\s*-\s+\*\*([A-Za-z][A-Za-z0-9]*-I?\d+)\*\*') {
                if ($current) { $clauses.Add($current) }
                $current = [pscustomobject]@{
                    Id    = $Matches[1]
                    File  = $doc.Name
                    Tests = [System.Collections.Generic.List[string]]::new()
                }
                continue
            }

            if ($current -and $line -match '\*Verified by:\*\s*(.+)$') {
                foreach ($m in [regex]::Matches($Matches[1], '`([^`]+)`')) {
                    $current.Tests.Add($m.Groups[1].Value.Trim())
                }
                continue
            }

            # A blank line inside the contract block ends the current list item
            # only if the next content is not a continuation; treat the next
            # clause or heading as the real terminator instead, so multi-line
            # clause prose is tolerated.
        }

        if ($current) { $clauses.Add($current) }
    }

    return $clauses
}

# ==============================================================================
# RESOLVE TESTS
# Test names are matched by their leaf identifier so that a clause may name
# either a bare method name or a fully qualified one.
# ==============================================================================

function Get-TestSourceText {
    param([string[]] $Roots)

    $text = [System.Text.StringBuilder]::new()
    foreach ($root in $Roots) {
        if (-not (Test-Path $root)) { continue }
        Get-ChildItem -Path $root -Recurse -File -Include *.cs, *.cpp, *.hpp, *.h, *.py, *.ts, *.js |
            Where-Object { $_.FullName -notmatch '[/\\](bin|obj|node_modules|\.venv)[/\\]' } |
            ForEach-Object { [void]$text.AppendLine([System.IO.File]::ReadAllText($_.FullName)) }
    }
    return $text.ToString()
}

function Get-PassedTestNames {
    param([string] $Pattern)

    $names = [System.Collections.Generic.HashSet[string]]::new()
    $found = $false

    $files = @(Get-ChildItem -Path . -Recurse -File -Filter (Split-Path $Pattern -Leaf) -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[/\\](node_modules|\.venv)[/\\]' })

    foreach ($file in $files) {
        $found = $true
        try {
            [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
        }
        catch {
            $script:warnings.Add("Could not parse test results: $($file.Name)")
            continue
        }

        foreach ($result in $trx.TestRun.Results.UnitTestResult) {
            if ($result.outcome -eq 'Passed') {
                [void]$names.Add(($result.testName -split '\(')[0].Trim())
            }
        }
    }

    return [pscustomobject]@{ Found = $found; Names = $names }
}

# ==============================================================================
# RUN CHECKS
# ==============================================================================

Write-Host "Checking: system contracts..."

$clauses = Get-ContractClauses -Root $ArchitectureRoot

if ($clauses.Count -eq 0) {
    Write-Host "  No contract clauses found under $ArchitectureRoot - nothing to check."
    exit 0
}

# --- Check 1: unique clause IDs ---
$clauses | Group-Object Id | Where-Object { $_.Count -gt 1 } | ForEach-Object {
    $files = ($_.Group | ForEach-Object { $_.File }) -join ", "
    $errors.Add("Duplicate clause ID '$($_.Name)' in: $files")
}

# --- Check 2: every clause names a test ---
foreach ($clause in $clauses) {
    if ($clause.Tests.Count -eq 0) {
        $errors.Add("$($clause.File): clause $($clause.Id) names no verifying test")
    }
}

# --- Check 3: every named test exists in the test sources ---
$sourceText = Get-TestSourceText -Roots $TestRoots

foreach ($clause in $clauses) {
    foreach ($test in $clause.Tests) {
        $leaf = ($test -split '[.:]')[-1]

        if ($test -match 'TODO') {
            $message = "$($clause.File): clause $($clause.Id) has an unfulfilled test obligation '$test'"
            if ($Strict) { $errors.Add($message) } else { $warnings.Add($message) }
            continue
        }

        if ($sourceText -notmatch [regex]::Escape($leaf)) {
            $errors.Add("$($clause.File): clause $($clause.Id) names test '$test' which does not exist in $($TestRoots -join ', ')")
        }
    }
}

# --- Check 4: every named test passed, when results are available ---
$passed = Get-PassedTestNames -Pattern $TestResults

if (-not $passed.Found) {
    Write-Host "  No test results matching '$TestResults' - skipping pass verification."
}
else {
    foreach ($clause in $clauses) {
        foreach ($test in $clause.Tests) {
            $leaf = ($test -split '[.:]')[-1]
            if ($test -match 'TODO') { continue }

            $matched = $passed.Names | Where-Object { $_ -eq $leaf -or $_.EndsWith(".$leaf") }
            if (-not $matched) {
                $errors.Add("$($clause.File): clause $($clause.Id) names test '$test' which did not pass")
            }
        }
    }
}

# ==============================================================================
# REPORT
# ==============================================================================

$testCount = ($clauses | ForEach-Object { $_.Tests.Count } | Measure-Object -Sum).Sum
Write-Host "  $($clauses.Count) clauses, $testCount test links checked."

foreach ($warning in $warnings) { Write-Host "  warning: $warning" -ForegroundColor Yellow }
foreach ($item in $errors) { Write-Host "  error: $item" -ForegroundColor Red }

exit ($errors.Count -gt 0 ? 1 : 0)
