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
#   4. Discovery found at least one test declaration, when a clause needs one
#   5. Every named test is declared as a test in a contract test location
#   6. Every named test passed, according to the MOST RECENT result for it
#   7. Test results are not older than the test sources they describe
#
# FAIL-CLOSED:
#   A clause the parser cannot understand is an error, never a silent skip.
#   Anything that looks like a clause but does not parse would otherwise vanish
#   from the report while appearing to pass, which is worse than no check.
#
#   Discovery is held to the same rule (check 4). A run that finds no test
#   declarations anywhere has learned something, and reporting each clause as a
#   missing test would send a reader off to write tests that already exist.
#
# EXIT CODES:
#   0 - all checks passed (warnings do not fail)
#   1 - one or more errors
#
# MODIFICATION POLICY:
#   Configure before editing. The parameters below cover the four things that
#   vary between test frameworks - which files are searched, what a test
#   declaration looks like, what marks a declaration as a boundary test, and what
#   form a recorded result takes - and their defaults describe a C# xUnit
#   repository, so a caller that supplies none of them gets the C# behavior.
#   Only modify this file to add a result format -TestResultFormat does not yet
#   offer, or for a layout no combination of the parameters can express.

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
    # interior test cannot quietly stand in for a boundary one. Set it to an
    # empty string for a repository whose layout has no interior/boundary split -
    # every discovered declaration then counts as a boundary test.
    [string] $ContractTestFolder = "Contract",

    # Attribute names that mark a method as a test. Applies to the default
    # declaration shape only; -TestDeclarationPattern replaces it outright.
    # Without it a clause could be satisfied by any identifier that merely
    # appears in the test sources.
    [string[]] $TestAttributes = @("Fact", "Theory"),

    # Regex matched line by line against each test source, with a named capture
    # group 'name' holding the declared test name - for a suite whose tests are
    # named cases rather than attribute-marked methods, such as
    #   ^\s*Test-Case\s+-Name\s+"(?<name>[^"]+)"
    # Empty selects the default shape: an attribute from -TestAttributes followed
    # by a method declaration, with comments stripped first. A custom pattern is
    # matched against the file as written, so anchor it with ^\s* if a
    # commented-out declaration must not count as a living test.
    [string] $TestDeclarationPattern = "",

    # Glob for test result files, matched against the full repository-relative
    # path. Missing results downgrade the pass check to a warning.
    [string] $TestResults = "artifacts/**/*.trx",

    # Form of the result files named by -TestResults:
    #   trx  - Visual Studio test results (the C# default)
    #   text - one result per line, an outcome token then the test name, as in
    #          "Passed clean repository passes". Blank lines and lines opening
    #          with # are ignored. The name is taken whole, so a case name may
    #          contain spaces and punctuation.
    [ValidateSet("trx", "text")]
    [string] $TestResultFormat = "trx",

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
# A clause is satisfied only by a real declared test - by default an
# attribute-marked method declaration - located in a contract test location.
# Matching bare identifiers against the test sources was far too generous: a
# private helper, or even a string literal, could keep a clause's promise alive
# after its test was gone.
#
# Declarations are recorded with their location so the check can tell "no such
# test" apart from "that test exists, but it is an interior test".
# ==============================================================================

# Directories whose contents are build output or vendored code rather than test
# sources. Pruned during the walk, not filtered after it: a test root of "." in a
# repository carrying node_modules costs seconds per pass otherwise.
$script:ExcludedDirectories = @('bin', 'obj', 'node_modules', '.venv', '.git')

# Hidden entries are skipped, exactly as Get-ChildItem without -Force skips
# them. Walking them would be a fail-OPEN change: a stale copy of a deleted test
# under a hidden directory would keep its clause alive, which is what the
# declaration check exists to prevent.
function Test-HiddenEntry {
    param([System.IO.FileSystemInfo] $Entry)

    if ($Entry.Attributes.HasFlag([System.IO.FileAttributes]::Hidden)) { return $true }

    # Windows decides by attribute alone; the Unix-like platforms treat a
    # dot-prefixed name as hidden, and so does PowerShell's own enumeration.
    return (-not $IsWindows) -and $Entry.Name.StartsWith('.')
}

function Get-TestSourceFiles {
    param([string[]] $Roots, [string[]] $Patterns)

    $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    $pending = [System.Collections.Generic.Queue[string]]::new()

    foreach ($root in $Roots) {
        if (-not (Test-Path $root)) { continue }

        # Resolve-Path without -LiteralPath: a root may be a wildcard such as
        # "test/*", which the previous Get-ChildItem -Path form expanded and
        # -LiteralPath throws on.
        foreach ($resolved in @(Resolve-Path -Path $root -ErrorAction SilentlyContinue)) {
            if (Test-Path -LiteralPath $resolved.Path -PathType Container) { $pending.Enqueue($resolved.Path) }
        }

        while ($pending.Count -gt 0) {
            $directory = [System.IO.DirectoryInfo]::new($pending.Dequeue())

            foreach ($child in $directory.EnumerateDirectories()) {
                if ($script:ExcludedDirectories -contains $child.Name) { continue }
                if (Test-HiddenEntry -Entry $child) { continue }
                $pending.Enqueue($child.FullName)
            }

            foreach ($file in $directory.EnumerateFiles()) {
                # -like rather than an enumeration filter: the platform's own
                # matching would let "*.cs" reach a .csproj through its short name.
                if (-not ($Patterns | Where-Object { $file.Name -like $_ })) { continue }
                if (Test-HiddenEntry -Entry $file) { continue }
                if ($seen.Add($file.FullName)) { $files.Add($file) }
            }
        }
    }

    return $files
}

# The default shape: a method declaration preceded by a run of attribute lines,
# at least one of which names a test attribute. Comments are stripped first
# because doc comments routinely mention the test name of the clause they prove,
# so leaving them in would defeat the existence check.
function Get-AttributeDeclaration {
    param([string] $Text, [string] $AttributePattern)

    $names = [System.Collections.Generic.List[string]]::new()

    $Text = [regex]::Replace($Text, '/\*[\s\S]*?\*/', ' ')
    $Text = [regex]::Replace($Text, '//[^\r\n]*', ' ')

    $pending = $false
    foreach ($line in ($Text -split '\r?\n')) {
        # Attribute lines accumulate: [Theory] followed by [InlineData]
        # must not clear the pending test marker.
        if ($line -match '^\s*\[') {
            if ($line -match $AttributePattern) { $pending = $true }
            if ($line -notmatch '\]\s*\S') { continue }
        }

        if (-not $pending) { continue }
        if ($line -match '^\s*$') { continue }

        if ($line -match '\b([A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(') {
            $names.Add($Matches[1])
            $pending = $false
            continue
        }

        # A non-blank, non-attribute line that is not a declaration ends
        # the attribute run.
        $pending = $false
    }

    return $names
}

# The caller-supplied shape, for a suite whose tests are not attribute-marked
# methods. Matched line by line so that a pattern can anchor itself against
# commented-out declarations, which no generic comment stripper could do across
# every language this parameter is meant to reach.
function Get-PatternDeclaration {
    param([string] $Text, [string] $Pattern)

    $names = [System.Collections.Generic.List[string]]::new()

    foreach ($line in ($Text -split '\r?\n')) {
        foreach ($match in [regex]::Matches($line, $Pattern)) {
            $name = $match.Groups['name'].Value.Trim()
            if ($name) { $names.Add($name) }
        }
    }

    return $names
}

function Get-TestDeclarations {
    param(
        [string[]] $Roots,
        [string[]] $Patterns,
        [string[]] $Attributes,
        [string] $ContractFolder,
        [string] $DeclarationPattern
    )

    $declarations = @{}
    $attributePattern = '(?<![\w])(' + (($Attributes | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')(?![\w])'

    # An empty contract folder means the repository has no interior/boundary
    # split in its layout, so location cannot disqualify a declaration.
    $splitByLocation = -not [string]::IsNullOrWhiteSpace($ContractFolder)
    $contractPattern = if ($splitByLocation) { '[/\\]' + [regex]::Escape($ContractFolder) + '[/\\]' } else { $null }

    foreach ($file in (Get-TestSourceFiles -Roots $Roots -Patterns $Patterns)) {
        $isContract = (-not $splitByLocation) -or ($file.FullName -match $contractPattern)

        $text = [System.IO.File]::ReadAllText($file.FullName)
        $names = if ($DeclarationPattern) {
            Get-PatternDeclaration -Text $text -Pattern $DeclarationPattern
        }
        else {
            Get-AttributeDeclaration -Text $text -AttributePattern $attributePattern
        }

        foreach ($name in $names) {
            if (-not $declarations.ContainsKey($name)) {
                $declarations[$name] = [pscustomobject]@{ InContract = $false; Files = [System.Collections.Generic.List[string]]::new() }
            }
            if ($isContract) { $declarations[$name].InContract = $true }
            [void]$declarations[$name].Files.Add($file.Name)
        }
    }

    return $declarations
}

# A verifier is either a code identifier, possibly namespace-qualified, or a
# named case quoted inside a file reference such as
# `suite.ps1: "clean repository passes"`. The quoted form is taken whole:
# splitting it on ':' leaves a fragment of the case name that no declaration can
# match, which reads as a missing test rather than as the misreading it is.
function Resolve-VerifierName {
    param([string] $Verifier)

    if ($Verifier -match '"([^"]+)"') { return $Matches[1] }
    return ($Verifier -split '[.:]')[-1]
}

# A planned obligation is written in the placeholder form - TODO. or TODO_
# followed by the name the test will take - so that is what is matched, not the
# verifier string as a whole. Case-sensitive, and anchored: a real test named
# TodoItemsAreReturned is not an obligation, nor is a genuine case named
# `suite.ps1: "TODO obligation is an error"`, nor is every clause verified by a
# suite file that happens to be called TODO-suite.ps1. Exempting any of those
# would silently drop the only enforced check in the process.
function Test-PlannedObligation {
    param([string] $Verifier)

    return $Verifier -cmatch '^\s*TODO[._]'
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

function Read-TrxResult {
    param([System.IO.FileInfo] $File)

    $records = [System.Collections.Generic.List[object]]::new()

    try {
        [xml]$trx = Get-Content -LiteralPath $File.FullName -Raw
    }
    catch {
        $script:warnings.Add("Could not parse test results: $($File.Name)")
        return $records
    }

    foreach ($result in $trx.TestRun.Results.UnitTestResult) {
        # A data-driven case is recorded as "Name(size: 1)"; the clause names the
        # method, so the arguments are dropped and the cases merge.
        $name = ($result.testName -split '\(')[0].Trim()
        if (-not $name) { continue }
        $records.Add([pscustomobject]@{ Name = $name; Outcome = $result.outcome })
    }

    return $records
}

function Read-TextResult {
    param([System.IO.FileInfo] $File)

    $records = [System.Collections.Generic.List[object]]::new()

    foreach ($line in [System.IO.File]::ReadAllLines($File.FullName)) {
        if ($line -match '^\s*(#|$)') { continue }

        # The rest of the line after the outcome is the name, taken whole: a
        # named case is not an identifier and may hold spaces and punctuation.
        if ($line -notmatch '^\s*(?<outcome>\S+)\s+(?<name>\S.*?)\s*$') {
            $script:warnings.Add("Could not parse result line in $($File.Name): $line")
            continue
        }

        $records.Add([pscustomobject]@{ Name = $Matches['name']; Outcome = $Matches['outcome'] })
    }

    return $records
}

function Get-TestOutcomes {
    param([string] $Pattern, [string] $Format)

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

        $records = if ($Format -eq 'text') { Read-TextResult -File $file } else { Read-TrxResult -File $file }

        foreach ($record in $records) {
            $existing = $outcomes[$record.Name]
            if ($existing -and $existing.Time -gt $file.LastWriteTimeUtc) { continue }

            # Within one run a data-driven test yields several results; a single
            # failing case must not be overwritten by a passing sibling.
            if ($existing -and $existing.Time -eq $file.LastWriteTimeUtc -and $existing.Outcome -ne 'Passed') { continue }

            $outcomes[$record.Name] = [pscustomobject]@{ Outcome = $record.Outcome; Time = $file.LastWriteTimeUtc }
        }
    }

    return [pscustomobject]@{ Found = $files.Count -gt 0; Outcomes = $outcomes; Newest = $newest }
}

function Get-NewestTestSource {
    param([string[]] $Roots, [string[]] $Patterns)

    $newest = $null
    foreach ($file in (Get-TestSourceFiles -Roots $Roots -Patterns $Patterns)) {
        if (-not $newest -or $file.LastWriteTimeUtc -gt $newest.LastWriteTimeUtc) { $newest = $file }
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

# --- Check 4: discovery found tests to check against ---
# Reported once, naming what matched nothing. Reporting each clause as a missing
# test instead would describe the wrong repair - the tests exist, the patterns
# point elsewhere - and a run whose clauses are all planned obligations is
# exempt, because a tree being bootstrapped is not expected to resolve anything.
$declarations = Get-TestDeclarations -Roots $TestRoots -Patterns $TestFilePatterns `
    -Attributes $TestAttributes -ContractFolder $ContractTestFolder `
    -DeclarationPattern $TestDeclarationPattern

$required = [System.Collections.Generic.List[string]]::new()
foreach ($clause in $clauses) {
    foreach ($test in $clause.Tests) {
        if (-not (Test-PlannedObligation -Verifier $test)) { $required.Add($test) }
    }
}

$discoveryFailed = ($declarations.Count -eq 0) -and ($required.Count -gt 0)

if ($discoveryFailed) {
    $shape = if ($TestDeclarationPattern) { $TestDeclarationPattern } else { "attribute-marked methods ($($TestAttributes -join ', '))" }
    $errors.Add("No test declarations found in '$($TestRoots -join ', ')' matching '$($TestFilePatterns -join ', ')' - $($required.Count) verifiers need one, so fix the discovery patterns rather than the tests. Declaration shape: $shape")
}

# --- Check 5: every named test is a declared test in a contract test location ---
$unresolved = [System.Collections.Generic.HashSet[string]]::new()

foreach ($clause in $clauses) {
    foreach ($test in $clause.Tests) {
        $leaf = Resolve-VerifierName -Verifier $test

        # The obligation is the placeholder form, not any verifier mentioning
        # TODO: a genuine test whose name carries the word is checked normally.
        if (Test-PlannedObligation -Verifier $test) {
            $message = "$($clause.File): clause $($clause.Id) has an unfulfilled test obligation '$test'"
            if ($Strict) { $errors.Add($message) } else { $warnings.Add($message) }
            [void]$unresolved.Add($test)
            continue
        }

        $declaration = $declarations[$leaf]

        if (-not $declaration) {
            [void]$unresolved.Add($test)
            # Suppressed when discovery itself failed: check 4 has already said
            # why, and repeating it per clause would bury that finding.
            if (-not $discoveryFailed) {
                $errors.Add("$($clause.File): clause $($clause.Id) names test '$test' which is not declared as a test method in $($TestRoots -join ', ')")
            }
            continue
        }

        if (-not $declaration.InContract) {
            [void]$unresolved.Add($test)
            $errors.Add("$($clause.File): clause $($clause.Id) names test '$test' which is not in a '$ContractTestFolder' folder (found in $($declaration.Files -join ', ')) - contract tests must be boundary tests")
        }
    }
}

# --- Check 6: every named test passed, according to its most recent result ---
$results = Get-TestOutcomes -Pattern $TestResults -Format $TestResultFormat

if (-not $results.Found) {
    $message = "No test results matching '$TestResults' - run build.ps1 first; pass verification was skipped"
    if ($Strict) { $errors.Add($message) } else { $warnings.Add($message) }
}
else {
    # --- Check 7: results must not predate the tests they claim to describe ---
    # Stale results are worse than absent ones: they report a clause as verified
    # using an outcome recorded before the test was last changed.
    $newestSource = Get-NewestTestSource -Roots $TestRoots -Patterns $TestFilePatterns
    if ($newestSource -and $newestSource.LastWriteTimeUtc -gt $results.Newest) {
        $errors.Add("Test results are stale: '$($newestSource.Name)' changed after the newest result matching '$TestResults'. Re-run the tests.")
    }

    foreach ($clause in $clauses) {
        foreach ($test in $clause.Tests) {
            $leaf = Resolve-VerifierName -Verifier $test

            # Already reported by check 5; saying so twice buries the findings
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
