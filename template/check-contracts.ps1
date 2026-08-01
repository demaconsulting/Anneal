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
#   1. Every system document declares a "## Contract" section
#   2. Every clause in Provides/Invariants carries a well-formed, unique ID
#   3. Every clause names at least one verifying test
#   4. Every named test is declared as a test method in a contract test location
#   5. Every named test passed, according to the MOST RECENT result for it
#   6. Test results are not older than the test sources they describe
#
# FAIL-CLOSED:
#   A clause the parser cannot understand is an error, never a silent skip.
#   Anything that looks like a clause but does not parse would otherwise vanish
#   from the report while appearing to pass, which is worse than no check.
#
# EXIT CODES:
#   0 - all checks passed (warnings do not fail)
#   1 - one or more errors
#
# MODIFICATION POLICY:
#   Only modify to adjust paths for a non-standard repository layout, to widen
#   -TestFilePatterns and -TestAttributes for an additional language, or to add
#   support for an additional test result format.

[CmdletBinding()]
param(
    # Root of the architecture tree containing system documents.
    [string] $ArchitectureRoot = "docs/architecture",

    # Roots searched for test source files that define the named tests.
    [string[]] $TestRoots = @("test", "tests"),

    # File patterns searched for test declarations. Widen this to teach the
    # check about an additional language.
    [string[]] $TestFilePatterns = @("*.cs"),

    # Directory name marking the contract test location. Contract tests are
    # required to live here so that their durable status is visible, and so an
    # interior test cannot quietly stand in for a boundary one.
    [string] $ContractTestFolder = "Contract",

    # Attribute names that mark a method as a test. Widen this for another
    # framework or language; without it a clause could be satisfied by any
    # identifier that merely appears in the test sources.
    [string[]] $TestAttributes = @("Fact", "Theory"),

    # Glob for test result files, matched against the full repository-relative
    # path. Missing results downgrade the pass check to a warning.
    [string] $TestResults = "artifacts/**/*.trx",

    # Treat unfulfilled TODO obligations, and absent test results, as errors
    # rather than warnings.
    [switch] $Strict
)

$ErrorActionPreference = "Stop"

# Arguments to a script file arrive as plain strings, so "-TestRoots test,tests"
# binds as a single element rather than two. Split them so the invocations shown
# in the user guide behave as written.
function Expand-ListArgument {
    param([string[]] $Values)

    return @($Values |
        ForEach-Object { $_ -split ',' } |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ })
}

$TestRoots = Expand-ListArgument -Values $TestRoots
$TestFilePatterns = Expand-ListArgument -Values $TestFilePatterns

$errors = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

# ==============================================================================
# EXTRACT CLAUSES
# A clause is a list item opening with a bolded ID, optionally followed within
# the same list item by a "*Verified by:*" line naming backticked test names.
#
# Parsing fails CLOSED. A system document with no contract, or a bolded list
# item under Provides/Invariants whose ID does not parse, is reported as an
# error. Previously such items were skipped in silence, so a renamed heading or
# a hyphenated system prefix removed the clause from the check while the run
# still reported success.
# ==============================================================================

# Subsections whose bolded list items are clauses. "Requires" is excluded: its
# entries name depended-upon behavior and legitimately carry no ID.
$script:ClauseSections = @('Provides', 'Invariants')

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
        $sawContract = $false
        $inFence = $false
        $section = ''
        $current = $null

        foreach ($line in $lines) {
            # Fenced blocks hold examples and templates, not live clauses.
            if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
            if ($inFence) { continue }

            # A level 2 heading other than Contract closes the contract block.
            if ($line -match '^##\s+(.+?)\s*$') {
                if ($current) { $clauses.Add($current); $current = $null }
                $inContract = ($Matches[1] -eq 'Contract')
                if ($inContract) { $sawContract = $true }
                $section = ''
                continue
            }

            # A level 3 heading selects the subsection within the contract.
            if ($line -match '^###\s+(.+?)\s*$') {
                if ($current) { $clauses.Add($current); $current = $null }
                $section = $Matches[1]
                continue
            }

            if (-not $inContract) { continue }

            if ($line -match '^\s*-\s+\*\*([^*]+)\*\*') {
                $label = $Matches[1].Trim()

                if ($section -notin $script:ClauseSections) { continue }

                if ($label -notmatch '^[A-Za-z][A-Za-z0-9]*(-[A-Za-z][A-Za-z0-9]*)*-I?\d+$') {
                    if ($current) { $clauses.Add($current); $current = $null }
                    $script:errors.Add("$($doc.Name): '$label' under '$section' is not a well-formed clause ID (expected {SYSTEM}-nn or {SYSTEM}-In)")
                    continue
                }

                if ($current) { $clauses.Add($current) }
                $current = [pscustomobject]@{
                    Id    = $label
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
        }

        if ($current) { $clauses.Add($current) }

        if (-not $sawContract) {
            $script:errors.Add("$($doc.Name): system document has no '## Contract' section")
        }
    }

    return $clauses
}

# ==============================================================================
# RESOLVE TEST DECLARATIONS
# A clause is satisfied only by a real test method - an attribute-marked method
# declaration - located in a contract test folder. Matching bare identifiers
# against the test sources was far too generous: a private helper, or even a
# string literal, could keep a clause's promise alive after its test was gone.
#
# Declarations are recorded with their location so the check can tell "no such
# test" apart from "that test exists, but it is an interior test".
# ==============================================================================

function Get-TestDeclarations {
    param([string[]] $Roots, [string[]] $Patterns, [string[]] $Attributes, [string] $ContractFolder)

    $declarations = @{}
    $attributePattern = '(?<![\w])(' + (($Attributes | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')(?![\w])'
    $contractPattern = '[/\\]' + [regex]::Escape($ContractFolder) + '[/\\]'

    foreach ($root in $Roots) {
        if (-not (Test-Path $root)) { continue }

        $files = Get-ChildItem -Path $root -Recurse -File -Include $Patterns |
            Where-Object { $_.FullName -notmatch '[/\\](bin|obj|node_modules|\.venv)[/\\]' }

        foreach ($file in $files) {
            $isContract = $file.FullName -match $contractPattern

            # Doc comments routinely mention the test name of the clause they
            # prove, so leaving comments in would defeat the existence check.
            $text = [System.IO.File]::ReadAllText($file.FullName)
            $text = [regex]::Replace($text, '/\*[\s\S]*?\*/', ' ')
            $text = [regex]::Replace($text, '//[^\r\n]*', ' ')

            $pending = $false
            foreach ($line in ($text -split '\r?\n')) {
                # Attribute lines accumulate: [Theory] followed by [InlineData]
                # must not clear the pending test marker.
                if ($line -match '^\s*\[') {
                    if ($line -match $attributePattern) { $pending = $true }
                    if ($line -notmatch '\]\s*\S') { continue }
                }

                if (-not $pending) { continue }
                if ($line -match '^\s*$') { continue }

                if ($line -match '\b([A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(') {
                    $name = $Matches[1]
                    if (-not $declarations.ContainsKey($name)) {
                        $declarations[$name] = [pscustomobject]@{ InContract = $false; Files = [System.Collections.Generic.List[string]]::new() }
                    }
                    if ($isContract) { $declarations[$name].InContract = $true }
                    [void]$declarations[$name].Files.Add($file.Name)
                    $pending = $false
                    continue
                }

                # A non-blank, non-attribute line that is not a declaration ends
                # the attribute run.
                $pending = $false
            }
        }
    }

    return $declarations
}

# ==============================================================================
# RESOLVE TEST RESULTS
# The whole glob is honored, not just its leaf. Matching only the file name
# would let a stray .trx anywhere in the tree satisfy the check, which is how
# stale results silently mark a failing clause as passing.
# ==============================================================================

function Convert-GlobToRegex {
    param([string] $Glob)

    $normalized = ($Glob -replace '\\', '/').TrimStart('./')
    $escaped = [regex]::Escape($normalized)

    # [regex]::Escape renders '*' as '\*'; expand the glob forms longest-first.
    $pattern = $escaped -replace '(\\\*){2}/', '(?:[^/]+/)*'
    $pattern = $pattern -replace '(\\\*){2}', '.*'
    $pattern = $pattern -replace '\\\*', '[^/]*'

    return '^' + $pattern + '$'
}

function Get-TestResultFiles {
    param([string] $Pattern)

    $regex = Convert-GlobToRegex -Glob $Pattern
    $root = (Get-Location).Path

    return @(Get-ChildItem -Path . -Recurse -File -Filter (Split-Path $Pattern -Leaf) -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[/\\](node_modules|\.venv)[/\\]' } |
        Where-Object {
            $relative = $_.FullName.Substring($root.Length).TrimStart('\', '/') -replace '\\', '/'
            $relative -match $regex
        })
}

function Get-TestOutcomes {
    param([string] $Pattern)

    # Keyed by fully qualified test name, holding the outcome from the NEWEST
    # result file that mentions it. Unioning "Passed" names across every result
    # file let a stale run vouch for a test that fails today - and because
    # artifacts/ is git-ignored and never cleaned, that was the normal local
    # state rather than an edge case.
    $outcomes = @{}
    $files = Get-TestResultFiles -Pattern $Pattern
    $newest = [datetime]::MinValue

    foreach ($file in ($files | Sort-Object LastWriteTimeUtc)) {
        if ($file.LastWriteTimeUtc -gt $newest) { $newest = $file.LastWriteTimeUtc }
        try {
            [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
        }
        catch {
            $script:warnings.Add("Could not parse test results: $($file.Name)")
            continue
        }

        foreach ($result in $trx.TestRun.Results.UnitTestResult) {
            $name = ($result.testName -split '\(')[0].Trim()
            if (-not $name) { continue }

            $existing = $outcomes[$name]
            if ($existing -and $existing.Time -gt $file.LastWriteTimeUtc) { continue }

            # Within one run a data-driven test yields several results; a single
            # failing case must not be overwritten by a passing sibling.
            if ($existing -and $existing.Time -eq $file.LastWriteTimeUtc -and $existing.Outcome -ne 'Passed') { continue }

            $outcomes[$name] = [pscustomobject]@{ Outcome = $result.outcome; Time = $file.LastWriteTimeUtc }
        }
    }

    return [pscustomobject]@{ Found = $files.Count -gt 0; Outcomes = $outcomes; Newest = $newest }
}

function Get-NewestTestSource {
    param([string[]] $Roots, [string[]] $Patterns)

    $newest = $null
    foreach ($root in $Roots) {
        if (-not (Test-Path $root)) { continue }
        Get-ChildItem -Path $root -Recurse -File -Include $Patterns |
            Where-Object { $_.FullName -notmatch '[/\\](bin|obj|node_modules|\.venv)[/\\]' } |
            ForEach-Object {
                if (-not $newest -or $_.LastWriteTimeUtc -gt $newest.LastWriteTimeUtc) { $newest = $_ }
            }
    }
    return $newest
}

# ==============================================================================
# RUN CHECKS
# ==============================================================================

Write-Host "Checking: system contracts..."

$clauses = Get-ContractClauses -Root $ArchitectureRoot

if ($clauses.Count -eq 0 -and $errors.Count -eq 0) {
    Write-Host "  No contract clauses found under $ArchitectureRoot - nothing to check."
    exit 0
}

# --- Check 2 (continued): clause IDs are unique ---
$clauses | Group-Object Id | Where-Object { $_.Count -gt 1 } | ForEach-Object {
    $files = ($_.Group | ForEach-Object { $_.File }) -join ", "
    $errors.Add("Duplicate clause ID '$($_.Name)' in: $files")
}

# --- Check 3: every clause names a test ---
foreach ($clause in $clauses) {
    if ($clause.Tests.Count -eq 0) {
        $errors.Add("$($clause.File): clause $($clause.Id) names no verifying test")
    }
}

# --- Check 4: every named test is a test method in a contract test location ---
$declarations = Get-TestDeclarations -Roots $TestRoots -Patterns $TestFilePatterns `
    -Attributes $TestAttributes -ContractFolder $ContractTestFolder
$unresolved = [System.Collections.Generic.HashSet[string]]::new()

foreach ($clause in $clauses) {
    foreach ($test in $clause.Tests) {
        $leaf = ($test -split '[.:]')[-1]

        # Case-sensitive: a real test named TodoItemsAreReturned is not an
        # unfulfilled obligation, and exempting it would silently drop the only
        # enforced check in the process.
        if ($test -cmatch 'TODO') {
            $message = "$($clause.File): clause $($clause.Id) has an unfulfilled test obligation '$test'"
            if ($Strict) { $errors.Add($message) } else { $warnings.Add($message) }
            [void]$unresolved.Add($test)
            continue
        }

        $declaration = $declarations[$leaf]

        if (-not $declaration) {
            [void]$unresolved.Add($test)
            $errors.Add("$($clause.File): clause $($clause.Id) names test '$test' which is not declared as a test method in $($TestRoots -join ', ')")
            continue
        }

        if (-not $declaration.InContract) {
            [void]$unresolved.Add($test)
            $errors.Add("$($clause.File): clause $($clause.Id) names test '$test' which is not in a '$ContractTestFolder' folder (found in $($declaration.Files -join ', ')) - contract tests must be boundary tests")
        }
    }
}

# --- Check 5: every named test passed, according to its most recent result ---
$results = Get-TestOutcomes -Pattern $TestResults

if (-not $results.Found) {
    $message = "No test results matching '$TestResults' - run build.ps1 first; pass verification was skipped"
    if ($Strict) { $errors.Add($message) } else { $warnings.Add($message) }
}
else {
    # --- Check 6: results must not predate the tests they claim to describe ---
    # Stale results are worse than absent ones: they report a clause as verified
    # using an outcome recorded before the test was last changed.
    $newestSource = Get-NewestTestSource -Roots $TestRoots -Patterns $TestFilePatterns
    if ($newestSource -and $newestSource.LastWriteTimeUtc -gt $results.Newest) {
        $errors.Add("Test results are stale: '$($newestSource.Name)' changed after the newest result matching '$TestResults'. Re-run the tests.")
    }

    foreach ($clause in $clauses) {
        foreach ($test in $clause.Tests) {
            $leaf = ($test -split '[.:]')[-1]

            # Already reported by check 4; saying so twice buries the findings
            # that need separate action.
            if ($unresolved.Contains($test)) { continue }

            $matched = @($results.Outcomes.Keys | Where-Object { $_ -eq $leaf -or $_.EndsWith(".$leaf") })

            if ($matched.Count -eq 0) {
                $errors.Add("$($clause.File): clause $($clause.Id) names test '$test' which has no result - it did not run")
                continue
            }

            $failed = @($matched | Where-Object { $results.Outcomes[$_].Outcome -ne 'Passed' })
            if ($failed.Count -gt 0) {
                $outcome = $results.Outcomes[$failed[0]].Outcome
                $errors.Add("$($clause.File): clause $($clause.Id) names test '$test' whose most recent result is '$outcome'")
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
