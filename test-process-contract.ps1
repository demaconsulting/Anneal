# test-process-contract.ps1
#
# PURPOSE:
#   Verifies the contract clauses of the Process system, as declared in
#   .anneal/architecture/process.md.
#
#   The Process system is the payload this repository ships: the agent prompt,
#   the standards, and the skills. Almost every rule in it is held by
#   prompt and review rather than by a mechanism, so the few properties that CAN
#   be checked mechanically are checked here - a dangling reference or a renamed
#   standard degrades an agent silently in whichever repository the payload was
#   installed into, which is the failure mode furthest from its cause.
#
#   Each case is named exactly as the clause that names it, so that
#   "dotnet anneal check-contracts" links the two. The tally this suite writes into
#   artifacts/tests/ is what that check reads for outcomes.
#
# USAGE:
#   pwsh ./test-process-contract.ps1
#   pwsh ./test-process-contract.ps1 -Filter Budget

[CmdletBinding()]
param(
    # Run only cases whose name contains this substring.
    [string] $Filter = ""
)

$ErrorActionPreference = "Stop"

$script:Root = $PSScriptRoot
$script:Passed = 0
$script:Failed = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()
$script:Outcomes = [System.Collections.Generic.List[string]]::new()
$script:Results = Join-Path $PSScriptRoot "artifacts/tests/process-contract.txt"

# ==============================================================================
# HARNESS
# ==============================================================================

function Repo-Path {
    param([string] $Relative)
    return (Join-Path $script:Root $Relative)
}

function Get-AgentFile {
    return @(Get-ChildItem -LiteralPath (Repo-Path ".github/agents") -Filter "*.agent.md" -File | Sort-Object Name)
}

function Get-StandardFile {
    return @(Get-ChildItem -LiteralPath (Repo-Path ".github/standards") -Filter "*.md" -File | Sort-Object Name)
}

function Read-Text {
    param([string] $Path)
    return ([System.IO.File]::ReadAllText($Path) -replace "`r`n", "`n")
}

# The body returns a list of problem strings; an empty list is a pass. Notes are
# printed and written to the tally on success as well, because a measurement is
# only useful while it is still visible.
function Test-Case {
    param(
        [string] $Name,
        [scriptblock] $Body
    )

    if ($Filter -and $Name -notlike "*$Filter*") { return }

    $problems = [System.Collections.Generic.List[string]]::new()
    $script:Notes = [System.Collections.Generic.List[string]]::new()

    try {
        $returned = & $Body
        foreach ($item in @($returned)) {
            if ($null -ne $item -and "$item" -ne "") { $problems.Add("$item") }
        }
    }
    catch {
        $problems.Add("threw: $($_.Exception.Message)")
    }

    if ($problems.Count -eq 0) {
        $script:Passed++
        $script:Outcomes.Add("Passed $Name")
        Write-Host "  PASS  $Name" -ForegroundColor Green
    }
    else {
        $script:Failed++
        $script:Outcomes.Add("Failed $Name")
        $script:Failures.Add($Name)
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        foreach ($problem in $problems) { Write-Host "          $problem" -ForegroundColor Red }
    }

    foreach ($note in $script:Notes) {
        Write-Host "        # $note" -ForegroundColor DarkGray
        $script:Outcomes.Add("# $Name : $note")
    }
}

function Add-Note {
    param([string] $Text)
    $script:Notes.Add($Text)
}

# ==============================================================================
# SHARED PARSING
# ==============================================================================

# Front matter as the agent runtime reads it: a --- fenced block at the very top,
# one key per line, continuation lines folded into the value above them.
function Get-FrontMatter {
    param([string] $Text)

    $lines = $Text -split "`n"
    if ($lines.Count -eq 0 -or $lines[0].TrimEnd() -ne "---") {
        return @{ Ok = $false; Error = "no front matter block opens the file"; Keys = @{} }
    }

    $close = -1
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].TrimEnd() -eq "---") { $close = $i; break }
    }
    if ($close -lt 0) {
        return @{ Ok = $false; Error = "front matter block is never closed by ---"; Keys = @{} }
    }

    $keys = @{}
    $order = [System.Collections.Generic.List[string]]::new()
    $current = $null
    for ($i = 1; $i -lt $close; $i++) {
        $line = $lines[$i]
        if ($line.Trim() -eq "") { continue }

        if ($line -match '^([A-Za-z][A-Za-z0-9_-]*):\s?(.*)$') {
            $current = $Matches[1]
            $keys[$current] = $Matches[2].Trim()
            $order.Add($current)
        }
        elseif ($line -match '^\s+\S' -and $current) {
            $keys[$current] = ($keys[$current] + " " + $line.Trim()).Trim()
        }
        else {
            return @{ Ok = $false; Error = "front matter line $($i + 1) is neither a key nor a continuation: '$line'"; Keys = $keys }
        }
    }

    return @{ Ok = $true; Error = ""; Keys = $keys; Order = $order }
}

# Fenced code blocks are removed before path extraction: report templates and
# worked examples are full of {placeholder} rows and table pipes that name no
# file, and treating them as references would make the check noise.
function Remove-FencedBlock {
    param([string] $Text)

    $out = [System.Collections.Generic.List[string]]::new()
    $inFence = $false
    foreach ($line in ($Text -split "`n")) {
        if ($line -match '^\s*```') { $inFence = -not $inFence; continue }
        if (-not $inFence) { $out.Add($line) }
    }
    return $out
}

# One record per path-shaped token in an inline code span or a markdown link
# destination, carrying the line it was found on so a failure names a location
# rather than a string.
function Get-PathToken {
    param([string] $Path)

    $lines = Remove-FencedBlock -Text (Read-Text $Path)
    $tokens = [System.Collections.Generic.List[object]]::new()

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $spans = [System.Collections.Generic.List[string]]::new()

        foreach ($m in [regex]::Matches($line, '`([^`]+)`')) { $spans.Add($m.Groups[1].Value) }
        foreach ($m in [regex]::Matches($line, '\]\(([^)\s]+)\)')) { $spans.Add($m.Groups[1].Value) }

        foreach ($span in $spans) {
            foreach ($word in ($span -split '\s+')) {
                # Trailing sentence punctuation is stripped; a LEADING dot is
                # part of the path, as every .github/ reference depends on.
                $word = $word.Trim().TrimStart('"', "'", '*', '(').TrimEnd(',', ';', ':', '.', '"', "'", '*', ')')
                if ($word -eq "") { continue }
                # A path is a slash-separated run of path characters, or a bare
                # filename with an extension. Anything else is prose or code.
                if ($word -notmatch '^\.{0,2}/?([A-Za-z0-9_.{}\-]+/)*[A-Za-z0-9_.{}\-]*$') { continue }
                if ($word -notmatch '/' -and $word -notmatch '^[A-Za-z0-9_.{}\-]+\.[A-Za-z0-9]+$') { continue }
                if ($word -eq "/" -or $word -eq "./") { continue }
                $tokens.Add([pscustomobject]@{
                        Token = ($word -replace '^\./', '')
                        Line  = $i + 1
                    })
            }
        }
    }

    return $tokens
}

# The destinations install.ps1 copies. Derived rather than hard-coded so that a
# new payload directory is classified as payload without editing this file.
function Get-PayloadDestination {
    $text = Read-Text (Repo-Path "install.ps1")
    if ($text -notmatch '(?s)\$payload\s*=\s*@\((.*?)\n\)') {
        throw "could not find the `$payload list in install.ps1"
    }
    $block = $Matches[1]
    return @([regex]::Matches($block, 'To\s*=\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
}

# Every payload file that carries prose an agent reads. Fenced blocks are
# deliberately NOT stripped here, because the report templates - which live
# inside fences - are exactly where the mode and scope declaration lines are.
# The template tree is walked whole rather than by named file: everything under
# .github/template/ ships to every installed repository, so a claim it makes
# about the vocabulary is as binding as one an agent prompt makes, and a new
# template document is covered the day it is added.
function Get-PayloadTextFile {
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-AgentFile) { $paths.Add($file.FullName) }
    foreach ($file in Get-StandardFile) { $paths.Add($file.FullName) }
    $skills = @(Get-ChildItem -LiteralPath (Repo-Path ".github/skills") -Filter "*.md" -File -Recurse | Sort-Object FullName)
    foreach ($file in $skills) { $paths.Add($file.FullName) }
    $template = @(Get-ChildItem -LiteralPath (Repo-Path ".github/template") -Filter "*.md" -File -Recurse | Sort-Object FullName)
    foreach ($file in $template) { $paths.Add($file.FullName) }
    return $paths
}

# The work modes, read from the table under '# Work Modes' in the standard that
# owns them. Read rather than listed, so that the check closes over the
# vocabulary itself and not over a second copy of it that could drift.
function Get-DefinedMode {
    param([string] $Path)

    $modes = [System.Collections.Generic.List[string]]::new()
    $inSection = $false
    foreach ($line in ((Read-Text $Path) -split "`n")) {
        if ($line -match '^#\s') {
            $inSection = ($line -match '^#\s*Work Modes\s*$')
            continue
        }
        if (-not $inSection) { continue }
        if ($line -match '^\s*\|\s*\*\*([^*|]+)\*\*\s*\|') { $modes.Add($Matches[1].Trim()) }
    }
    return $modes
}

# The scope vocabulary, read from the top-level '# {Name}' headings of the same
# standard that immediately follow the classifying-question section — Small Fix,
# Contract Change, Structural Change. Read rather than listed, so a rename in the
# standard is what the check sees too.
function Get-DefinedScope {
    param([string] $Path)

    $scopes = [System.Collections.Generic.List[string]]::new()
    $inScopeSection = $false
    foreach ($line in ((Read-Text $Path) -split "`n")) {
        if ($line -match '^#\s*The Classifying Question') { $inScopeSection = $true; continue }
        if ($line -match '^#\s*Discipline') { $inScopeSection = $false; continue }
        if (-not $inScopeSection) { continue }
        if ($line -match '^#\s+(.+?)\s*$') { $scopes.Add($Matches[1].Trim()) }
    }
    return $scopes
}

# The Effort vocabulary, read from the '# Effort' section's own table in the
# same standard - Small, Medium, Large, Massive - including its
# '## Massive Effort Must Be Decomposed' subsection (a second-level heading, not
# a new top-level section), and stopping at the next top-level '# ' heading.
# Read rather than listed, so a rename in the standard is what the check sees
# too.
function Get-DefinedEffort {
    param([string] $Path)

    $efforts = [System.Collections.Generic.List[string]]::new()
    $inSection = $false
    $sawSeparator = $false
    foreach ($line in ((Read-Text $Path) -split "`n")) {
        if ($line -match '^#\s') {
            if ($line -match '^#\s*Effort\s*$') { $inSection = $true; $sawSeparator = $false; continue }
            if ($inSection) { break }
            continue
        }
        if (-not $inSection) { continue }
        if ($line -match '^\s*\|\s*-{2,}\s*\|') { $sawSeparator = $true; continue }
        if (-not $sawSeparator) { continue }
        if ($line -match '^\s*\|\s*([^|]+?)\s*\|') { $efforts.Add($Matches[1].Trim()) }
    }
    return $efforts
}

# ==============================================================================
# CASES
# ==============================================================================

Write-Host "Testing: Process contract (.anneal/architecture/process.md)"

# --- PROCESS-01 ---------------------------------------------------------------
# The directory is iterated rather than listed, so a ninth agent is covered the
# day it is added rather than the day someone remembers to add it here.
Test-Case -Name "AgentFrontMatterIsWellFormed" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()
    $flags = @("user-invocable", "disable-model-invocation")

    foreach ($file in Get-AgentFile) {
        $expected = $file.Name -replace '\.agent\.md$', ''
        $front = Get-FrontMatter -Text (Read-Text $file.FullName)

        if (-not $front.Ok) {
            $problems.Add("$($file.Name): $($front.Error)")
            continue
        }

        $keys = $front.Keys

        if (-not $keys.ContainsKey("name")) {
            $problems.Add("$($file.Name): front matter has no 'name'")
        }
        elseif ($keys["name"] -eq "") {
            $problems.Add("$($file.Name): 'name' is empty")
        }
        elseif ($keys["name"] -ne $expected) {
            $problems.Add("$($file.Name): 'name' is '$($keys["name"])' but the filename requires '$expected'")
        }

        if (-not $keys.ContainsKey("description")) {
            $problems.Add("$($file.Name): front matter has no 'description'")
        }
        elseif ($keys["description"] -eq "") {
            $problems.Add("$($file.Name): 'description' is empty")
        }

        $present = @($flags | Where-Object { $keys.ContainsKey($_) })
        if ($present.Count -eq 0) {
            $problems.Add("$($file.Name): carries neither 'user-invocable' nor 'disable-model-invocation', so how it is reached is unstated")
        }

        foreach ($flag in $present) {
            if ($keys[$flag] -cnotin @("true", "false")) {
                $problems.Add("$($file.Name): '$flag' is '$($keys[$flag])'; a literal true or false is required")
            }
        }
    }

    Add-Note "$((Get-AgentFile).Count) agent prompts checked"
    return $problems
}

# --- PROCESS-02 ---------------------------------------------------------------
# Two assertions over the same token stream. A payload reference must resolve on
# disk. Every other path must belong to the layout the process defines - what
# Template ships, plus the files and directories every installed repository
# carries. That is membership, not existence, which is why
# .anneal/work/active-plan.md passes while absent from this repository.
Test-Case -Name "AgentReferencesResolve" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $payloadRoots = Get-PayloadDestination
    $payloadDirs = @($payloadRoots | Where-Object { $_ -like "*/*" -or $_ -notmatch '\.' })
    $payloadFiles = @($payloadRoots | Where-Object { $payloadDirs -notcontains $_ })

    # Every file the payload ships, indexed by bare name so that a standard cited
    # as `system-contracts.md` resolves exactly as the full path does.
    $shipped = @{}
    foreach ($dir in $payloadDirs) {
        $full = Repo-Path $dir
        if (-not (Test-Path -LiteralPath $full)) { continue }
        # -Force: include dot-prefixed files that PowerShell treats as hidden on Linux.
        foreach ($item in (Get-ChildItem -LiteralPath $full -Recurse -File -Force)) {
            $shipped[$item.Name] = $true
        }
    }

    # The file extensions the installed layout actually uses, so that a token whose
    # extension belongs to no layout file can be recognised as prose. The empty
    # extension is included deliberately: a token with no extension names a
    # directory, which is always a path.
    $knownExtensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [void]$knownExtensions.Add("")
    [void]$knownExtensions.Add(".md")
    foreach ($dir in $payloadDirs) {
        $full = Repo-Path $dir
        if (-not (Test-Path -LiteralPath $full)) { continue }
        # -Force: include dot-prefixed files that PowerShell treats as hidden on Linux.
        foreach ($item in (Get-ChildItem -LiteralPath $full -Recurse -File -Force)) {
            [void]$knownExtensions.Add([System.IO.Path]::GetExtension($item.Name))
        }
    }
    foreach ($file in $payloadFiles) {
        [void]$knownExtensions.Add([System.IO.Path]::GetExtension($file))
    }

    # .anneal/architecture/{system}.md is in this set too, but every token carrying a
    # placeholder is excluded before we get here.
    $requiredFiles = @(
        "README.md", ".anneal/work/backlog.md", ".anneal/work/constraints.md",
        ".anneal/governance/assumptions.md", ".anneal/governance/tenets.md", ".anneal/governance/vision.md",
        ".anneal/work/active-plan.md", ".anneal/architecture/overview.md"
    )
    # The layout the profile's layout.md requires of every
    # installed repository. A directory names a location rather than a file, so it
    # is checked against this list rather than against the file list above.
    $requiredDirs = @(
        "docs", "src", "test",
        ".anneal", ".anneal/architecture", ".anneal/governance", ".anneal/profile", ".anneal/work"
    )

    foreach ($file in Get-AgentFile) {
        foreach ($record in (Get-PathToken -Path $file.FullName)) {
            $token = $record.Token
            $where = "$($file.Name):$($record.Line)"
            $bare = $token -replace '/$', ''

            # A token carrying a {placeholder} segment describes the SHAPE of a
            # tree, not a file. .anneal/architecture/{system}/{subsystem}.md is the
            # documented layout; a concrete path under .anneal/architecture/{system}/
            # is a reference to one repository's document and is a violation.
            if ($token -match '[{}]') { continue }

            # Build output is produced by the tooling rather than defined by the
            # layout, so PROCESS-02 leaves it out. Scripts and tooling
            # configuration are NOT exempt: they resolve as members of the layout
            # Template ships.
            #
            # A token with no slash whose extension is not one the installed layout
            # uses is prose, not a path: TODO.InstallCopiesPayloadOnly names a
            # planned test. A token containing a slash is always treated as a path,
            # so an unfamiliar extension under a real directory cannot slip through
            # unchecked.
            if ($bare -notmatch '/' -and $knownExtensions -notcontains [System.IO.Path]::GetExtension($bare)) { continue }
            if ($bare -eq "artifacts" -or $bare -like "artifacts/*") { continue }

            $isPayload = $false
            foreach ($root in $payloadDirs) { if ($bare -eq $root -or $bare -like "$root/*") { $isPayload = $true } }
            foreach ($root in $payloadFiles) { if ($bare -eq $root) { $isPayload = $true } }

            if ($isPayload) {
                # Assertion 1, written full.
                if (-not (Test-Path -LiteralPath (Repo-Path $bare))) {
                    $problems.Add("$where names '$token', which the payload does not contain")
                }
                continue
            }

            if ($bare -notmatch '/') {
                # Assertion 1, written bare: a standard, skill, or agent prompt
                # cited by filename alone.
                if ($shipped.ContainsKey($bare)) { continue }
                if ($requiredFiles -contains $bare) { continue }
                if ($requiredDirs -contains $bare) { continue }
                # The same required file cited by its bare name, as overview.md is.
                if (@($requiredFiles | ForEach-Object { Split-Path $_ -Leaf }) -contains $bare) { continue }
                $problems.Add("$where names '$token', which is neither a payload file nor a file the process requires every repository to have")
                continue
            }

            # Assertion 2: a repository path.
            if ($requiredFiles -contains $bare) { continue }
            if ($requiredDirs -contains $bare) { continue }
            $problems.Add("$where names '$token', which the process does not require every installed repository to have")
        }
    }

    # Regression guard: the shipped-file index must include files that live inside
    # dot-prefixed directories. It cannot fail on Windows, where a leading dot means
    # nothing and the index is complete with or without -Force; it fails on Linux,
    # where PowerShell treats dot-prefixed names as hidden and omits them unless
    # -Force is passed. That asymmetry is the point - this guard exists because the
    # omission is invisible to the developer and only ever surfaces in CI.
    #
    # overview.md lives under .github/template/.anneal/architecture/, so reaching it
    # requires traversing the dot-prefixed .anneal directory. If -Force were removed
    # from the enumeration above, this file would disappear from $shipped on Linux
    # and the guard would fire.
    if (-not $shipped.ContainsKey("overview.md")) {
        $problems.Add("shipped-file index is missing 'overview.md' (file inside dot-prefixed .anneal directory not enumerated - check -Force on Get-ChildItem)")
    }

    return $problems
}

# --- PROCESS-03 ---------------------------------------------------------------
# Two loading surfaces count: an agent prompt naming a standard, or a compiled
# worker's fixed standards array in source. Five of the eight standards are
# worker-array-only, which process.md Composition records as correct: they are
# product-code standards that the one process agent never names at authoring time.
Test-Case -Name "NoOrphanedStandards" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $agentText = @{}
    foreach ($file in Get-AgentFile) { $agentText[$file.Name] = Read-Text $file.FullName }

    $workerFiles = @(Get-ChildItem -LiteralPath (Repo-Path "src") -Filter "*.cs" -File -Recurse |
        Where-Object { $_.DirectoryName -match '[\\/]Workers[\\/]?$' } | Sort-Object FullName)
    $workerText = ($workerFiles | ForEach-Object { Read-Text $_.FullName }) -join "`n"

    foreach ($standard in Get-StandardFile) {
        $reached = @()
        if ($workerText -match [regex]::Escape($standard.Name)) { $reached += "a compiled worker's standards array" }
        foreach ($name in ($agentText.Keys | Sort-Object)) {
            if ($agentText[$name] -match [regex]::Escape($standard.Name)) { $reached += $name }
        }

        if ($reached.Count -eq 0) {
            $problems.Add("$($standard.Name) is named by no agent prompt and by no compiled worker's standards array, so nothing loads it")
        }
    }

    Add-Note "$((Get-StandardFile).Count) standards checked against $($agentText.Count) prompts and $($workerFiles.Count) worker source files"
    return $problems
}

# --- PROCESS-06 ---------------------------------------------------------------
# The set is selected by size, never listed: a new standard larger than the
# current fourth displaces it automatically, which is the whole point of the
# clause. The ceiling is read from the document that declares it, so raising it
# there cannot leave a stale literal here.
Test-Case -Name "WorstCaseInvocationWithinBudget" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    # The counting method prompt-authoring.md declares: normalize CRLF to LF
    # FIRST, then count UTF-8 bytes and divide by four. Counting before
    # normalizing inflates the total by one token per four lines and makes CI and
    # a Windows checkout of the same commit disagree.
    function Get-TokenCount {
        param([string] $Path)
        $text = [System.IO.File]::ReadAllText($Path) -replace "`r`n", "`n"
        $bytes = [System.Text.Encoding]::UTF8.GetByteCount($text)
        return [int][math]::Round($bytes / 4.0, [System.MidpointRounding]::AwayFromZero)
    }

    $authoring = Repo-Path ".anneal/architecture/process/prompt-authoring.md"
    $text = Read-Text $authoring
    if ($text -notmatch '(?m)\*\*The context budget is ([\d,]+) tokens\*\*') {
        $problems.Add("prompt-authoring.md no longer declares the context budget in the form '**The context budget is N tokens**'; the ceiling has no readable owner")
        return $problems
    }
    $ceiling = [int]($Matches[1] -replace ',', '')

    $agents = @(Get-AgentFile | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Tokens = (Get-TokenCount $_.FullName) } })
    $standards = @(Get-StandardFile | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Tokens = (Get-TokenCount $_.FullName) } })

    $selected = [System.Collections.Generic.List[object]]::new()
    foreach ($item in @($agents | Sort-Object Tokens -Descending | Select-Object -First 1)) { $selected.Add($item) }
    foreach ($item in @($standards | Sort-Object Tokens -Descending | Select-Object -First 4)) { $selected.Add($item) }

    $total = ($selected | Measure-Object -Property Tokens -Sum).Sum

    # Emitted on success as well as failure: on failure the breakdown is the only
    # actionable half, and on success it is how the table in prompt-authoring.md
    # gets refreshed deliberately rather than rotting.
    foreach ($item in $selected) { Add-Note "$($item.Name) = $($item.Tokens)" }
    Add-Note "worst case = $total of $ceiling"

    if ($total -gt $ceiling) {
        $problems.Add("the worst-case invocation is $total tokens against a ceiling of $ceiling declared in prompt-authoring.md")
    }

    return $problems
}

# --- PROCESS-07 ---------------------------------------------------------------
# The vocabulary is read from change-classification.md, never listed here: a mode
# added there is covered the day it is added, and a mode renamed there fails
# every payload file still carrying the old name. Two shapes are read, because
# they fail differently - a report template can declare a mode that no longer
# exists, and prose can name one that never did.
Test-Case -Name "ModeVocabularyIsClosed" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    # Sentence-initial function words that legitimately precede the bare noun
    # "mode" in ordinary English - "The mode and scope decide the workflow". They
    # are exempted by name rather than by weakening the capture, because a
    # narrower pattern would also stop seeing a genuinely undefined mode name.
    # Not one of these words could plausibly name a mode, so the list gives up
    # no coverage. The check has two other limits, both deliberate: the capture
    # requires an initial capital, so a lower-case "patch mode" is not seen, and
    # it reads only the payload files Get-PayloadTextFile returns.
    $functionWords = @(
        "The", "This", "That", "A", "An", "Each", "Every", "Any", "No", "One",
        "Another", "Either", "Both", "Such", "Its", "Their", "Which",
        "Whichever", "What", "Whatever", "Some", "Same", "Only"
    )

    $classification = Repo-Path ".github/standards/change-classification.md"
    $modes = Get-DefinedMode -Path $classification

    # Fail closed: if the table stops parsing, every name below would be
    # "undefined" or - worse, had this been written as a skip - nothing would be
    # checked at all and the clause would quietly stop being verified.
    if ($modes.Count -lt 2) {
        $problems.Add("only $($modes.Count) work mode(s) parsed from the '# Work Modes' table in change-classification.md; the vocabulary has no readable owner")
        return $problems
    }

    $files = Get-PayloadTextFile
    foreach ($path in $files) {
        $name = Split-Path $path -Leaf
        $number = 0
        foreach ($line in ((Read-Text $path) -split "`n")) {
            $number++

            # A report template's mode field: every alternative of its (A|B|C)
            # group and every backticked token on the line is a claim about the
            # vocabulary.
            if ($line -match '^\s*(?:-\s*)?\*\*Mode\*\*\s*(?::|—|-)') {
                $tokens = [System.Collections.Generic.List[string]]::new()
                foreach ($group in [regex]::Matches($line, '\(([^)]*)\)')) {
                    foreach ($alternative in ($group.Groups[1].Value -split '\|')) { $tokens.Add($alternative.Trim()) }
                }
                foreach ($span in [regex]::Matches($line, '`([^`]+)`')) { $tokens.Add($span.Groups[1].Value.Trim()) }

                foreach ($token in $tokens) {
                    if ($token -eq "" -or $token -eq "n/a") { continue }
                    if ($modes -contains $token) { continue }
                    $problems.Add("$name line ${number}: the mode field names '$token', which change-classification.md does not define")
                }
            }

            # The phrase form, wherever it appears: "Migration mode", "Change mode".
            foreach ($match in [regex]::Matches($line, '\b([A-Z][A-Za-z]*)\s+[Mm]ode\b')) {
                $word = $match.Groups[1].Value
                if ($modes -contains $word) { continue }
                if ($functionWords -contains $word) { continue }
                $problems.Add("$name line ${number}: '$word mode' names a mode change-classification.md does not define")
            }
        }
    }

    Add-Note "modes: $($modes -join ', ')"
    Add-Note "payload files scanned: $($files.Count)"
    return $problems
}

# --- PROCESS-09 ---------------------------------------------------------------
# Unlike the retired ordinal scale, a Scope name is an ordinary English phrase
# ("Contract Change", "Structural Change") that appears constantly in prose with
# no special meaning, so scanning free text for it would manufacture false
# positives rather than catch a real vocabulary break. The one site that is both
# safe and load-bearing is a report template's own **Scope** field, which is
# always a closed enumeration and is exactly what an agent reads to decide how to
# act.
Test-Case -Name "ScopeVocabularyIsClosed" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $classification = Repo-Path ".github/standards/change-classification.md"
    $scopes = Get-DefinedScope -Path $classification

    # Fail closed, for the same reason the mode case does.
    if ($scopes.Count -lt 2) {
        $problems.Add("only $($scopes.Count) scope heading(s) parsed from change-classification.md between '# The Classifying Question (Change Mode)' and '# Discipline (MANDATORY)'; the vocabulary has no readable owner")
        return $problems
    }

    $sites = 0
    $files = Get-PayloadTextFile
    foreach ($path in $files) {
        $name = Split-Path $path -Leaf
        $number = 0
        foreach ($line in ((Read-Text $path) -split "`n")) {
            $number++

            # A report template's scope field: every alternative of its (A|B|C)
            # group and every backticked token on the line is a claim about the
            # vocabulary. '**Scope Verdict**' and '**Scope Deviations**' are
            # different fields and deliberately do not match.
            if ($line -match '^\s*(?:-\s*)?\*\*Scope\*\*\s*(?::|—|-)') {
                $sites++
                $tokens = [System.Collections.Generic.List[string]]::new()
                foreach ($group in [regex]::Matches($line, '\(([^)]*)\)')) {
                    # A parenthesized group is only an alternation ('A|B|C') when it
                    # actually contains one; a bare qualifier like '(fixed by mode)'
                    # attached to a scope name is not itself a claim about the
                    # vocabulary and must not be checked as one.
                    if ($group.Groups[1].Value -notmatch '\|') { continue }
                    foreach ($alternative in ($group.Groups[1].Value -split '\|')) { $tokens.Add($alternative.Trim()) }
                }
                foreach ($span in [regex]::Matches($line, '`([^`]+)`')) { $tokens.Add($span.Groups[1].Value.Trim()) }

                foreach ($token in $tokens) {
                    if ($token -eq "" -or $token -eq "n/a") { continue }
                    if ($scopes -contains $token) { continue }
                    # A backticked span may carry a scope name plus a trailing
                    # qualifier, e.g. `Small Fix (fixed by mode)` - only the leading
                    # scope name is the claim being checked.
                    if ($token -match '^(Small Fix|Contract Change|Structural Change)\b') { continue }
                    $problems.Add("$name line ${number}: the scope field names '$token', which change-classification.md does not define")
                }
            }
        }
    }

    Add-Note "scopes: $($scopes -join ', ')"
    Add-Note "scope field sites checked: $sites across $($files.Count) payload files"
    return $problems
}

# --- PROCESS-10 ---------------------------------------------------------------
# The same closure PROCESS-09 gives Scope, extended to the Effort axis: an
# Effort name ("Small", "Medium", "Large", "Massive") appears constantly in
# ordinary prose ("Small Fix", "a large mechanical rename") with no special
# meaning there, so the one safe and load-bearing site is again a report
# template's own **Effort** field, which is always a closed enumeration.
Test-Case -Name "EffortVocabularyIsClosed" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $classification = Repo-Path ".github/standards/change-classification.md"
    $efforts = Get-DefinedEffort -Path $classification

    # Fail closed, for the same reason the scope case does.
    if ($efforts.Count -lt 2) {
        $problems.Add("only $($efforts.Count) effort row(s) parsed from change-classification.md's '# Effort' table; the vocabulary has no readable owner")
        return $problems
    }

    $sites = 0
    $files = Get-PayloadTextFile
    foreach ($path in $files) {
        $name = Split-Path $path -Leaf
        $number = 0
        foreach ($line in ((Read-Text $path) -split "`n")) {
            $number++

            # A report template's effort field: every alternative of its (A|B|C)
            # group and every backticked token on the line is a claim about the
            # vocabulary.
            if ($line -match '^\s*(?:-\s*)?\*\*Effort\*\*\s*(?::|—|-)') {
                $sites++
                $tokens = [System.Collections.Generic.List[string]]::new()
                foreach ($group in [regex]::Matches($line, '\(([^)]*)\)')) {
                    # A parenthesized group is only an alternation ('A|B|C') when it
                    # actually contains one; a bare qualifier attached to an effort
                    # name is not itself a claim about the vocabulary and must not
                    # be checked as one.
                    if ($group.Groups[1].Value -notmatch '\|') { continue }
                    foreach ($alternative in ($group.Groups[1].Value -split '\|')) { $tokens.Add($alternative.Trim()) }
                }
                foreach ($span in [regex]::Matches($line, '`([^`]+)`')) { $tokens.Add($span.Groups[1].Value.Trim()) }

                foreach ($token in $tokens) {
                    if ($token -eq "" -or $token -eq "n/a") { continue }
                    if ($efforts -contains $token) { continue }
                    $problems.Add("$name line ${number}: the effort field names '$token', which change-classification.md does not define")
                }
            }
        }
    }

    Add-Note "efforts: $($efforts -join ', ')"
    Add-Note "effort field sites checked: $sites across $($files.Count) payload files"
    return $problems
}

# --- PROCESS-I1 ---------------------------------------------------------------
# Only one agent file exists in the payload today, so the check is exactly what
# the contract requires: every agent's front matter declares user-invocable
# true or false, and exactly one is true.
Test-Case -Name "EntryPointsAreExactlyOne" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $entryPoints = [System.Collections.Generic.List[string]]::new()

    foreach ($file in Get-AgentFile) {
        $name = $file.Name -replace '\.agent\.md$', ''
        $front = Get-FrontMatter -Text (Read-Text $file.FullName)
        if (-not $front.Ok) {
            $problems.Add("$($file.Name): $($front.Error)")
            continue
        }

        $userInvocable = $front.Keys["user-invocable"]
        $modelDisabled = $front.Keys["disable-model-invocation"]

        if ($userInvocable -eq "true") {
            $entryPoints.Add($name)
        }
        elseif ($userInvocable -ne "false") {
            $problems.Add("$($file.Name): 'user-invocable' is '$userInvocable'; every agent must declare true or false")
        }
        elseif ($modelDisabled -eq "true") {
            $problems.Add("$($file.Name): is not user-invocable and disables model invocation, so no route reaches it")
        }
    }

    if ($entryPoints.Count -ne 1) {
        $problems.Add("$($entryPoints.Count) agents are user-invocable ($($entryPoints -join ', ')); the contract allows exactly one")
    }

    Add-Note "entry points: $($entryPoints -join ', ')"
    return $problems
}

# --- PROCESS-I2 ---------------------------------------------------------------
# A heuristic structural check: every normative passage that re-states a rule
# must cite the owning file by name. The check does not attempt semantic
# near-duplicate detection (which would produce too many false positives across
# a corpus where procedural phrases recur by design), but it does verify the
# structural pattern the invariant demands: a file that quotes or re-states a
# rule from another file must name that file inline as a reference, not import
# the prose without attribution. The mechanical test is therefore: no agent
# prompt or standard contains a bolded normative rule that also appears
# word-for-word in a different file without the second file citing the first by
# name on the same line or in the immediately preceding sentence.
#
# Practically: scan for exact bold-phrase matches across files. If a bolded
# phrase from file A matches one from file B, at least one of the two occurrences
# must be accompanied (same line or one line earlier) by a reference to the other
# file's name.
Test-Case -Name "NormativeRulesHaveOneOwner" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $files = [System.Collections.Generic.List[object]]::new()
    foreach ($f in Get-AgentFile)   { $files.Add([pscustomobject]@{ Name = $f.Name; Path = $f.FullName }) }
    foreach ($f in Get-StandardFile) { $files.Add([pscustomobject]@{ Name = $f.Name; Path = $f.FullName }) }

    # Extract bolded rule phrases (** ... **) longer than five words per file,
    # excluding fenced code block content where bold is markup, not prose.
    function Get-BoldPhrases {
        param([string] $Path)
        $lines = (Remove-FencedBlock -Text (Read-Text $Path))
        $phrases = [System.Collections.Generic.List[object]]::new()
        $lineNum = 0
        foreach ($line in $lines) {
            $lineNum++
            foreach ($m in [regex]::Matches($line, '\*\*([^*]{20,})\*\*')) {
                $phrase = $m.Groups[1].Value.Trim()
                if (($phrase -split '\s+').Count -ge 5) {
                    $phrases.Add([pscustomobject]@{ Phrase = $phrase; Line = $lineNum; LineText = $line })
                }
            }
        }
        return $phrases
    }

    # Build phrase index
    $index = @{}  # phrase -> list of {File, Line, LineText}
    foreach ($entry in $files) {
        foreach ($record in (Get-BoldPhrases -Path $entry.Path)) {
            if (-not $index.ContainsKey($record.Phrase)) {
                $index[$record.Phrase] = [System.Collections.Generic.List[object]]::new()
            }
            $index[$record.Phrase].Add([pscustomobject]@{
                File = $entry.Name; Line = $record.Line; LineText = $record.LineText
            })
        }
    }

    # Any phrase appearing in two or more files is a candidate restatement.
    # It is acceptable only if at least one occurrence cites the other file.
    foreach ($phrase in $index.Keys) {
        $occurrences = $index[$phrase]
        if ($occurrences.Count -lt 2) { continue }

        # A phrase repeated within a single file is not a cross-file restatement -
        # PROCESS-I2 is about a rule owned by one file being re-stated by another,
        # not a document's own rhetorical callback to itself.
        $fileNames = @($occurrences | ForEach-Object { $_.File })
        $distinctFiles = @($fileNames | Select-Object -Unique)
        if ($distinctFiles.Count -lt 2) { continue }

        $anyCite = $false
        foreach ($occ in $occurrences) {
            $others = @($fileNames | Where-Object { $_ -ne $occ.File })
            foreach ($other in $others) {
                $bare = $other -replace '\.agent\.md$', '' -replace '\.md$', ''
                if ($occ.LineText -match [regex]::Escape($bare) -or
                    $occ.LineText -match [regex]::Escape($other)) {
                    $anyCite = $true; break
                }
            }
            if ($anyCite) { break }
        }

        if (-not $anyCite) {
            $where = ($occurrences | ForEach-Object { "$($_.File):$($_.Line)" }) -join ", "
            $problems.Add("bolded rule appears in multiple files without cross-citation ($where): '**$($phrase.Substring(0, [math]::Min(60, $phrase.Length)))**'")
        }
    }

    Add-Note "files scanned: $($files.Count); bold phrases with 5+ words checked"
    return $problems
}

# ==============================================================================
# TEMPLATE CONTRACT CASES
# ==============================================================================

Write-Host ""
Write-Host "Testing: Template contract (.anneal/architecture/template.md)"


# --- TEMPLATE-06 --------------------------------------------------------------
# Every placeholder/directive in a template file must use the recognizable
# HTML-comment form: <!-- TEMPLATE-DIRECTIVE: ... --> or the {placeholder}
# token form declared in repository-map.md. Any other ad-hoc form fails.
Test-Case -Name "DirectivesAreRecognizable" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $templateRoot = Repo-Path ".github/template"
    $mdFiles = @(Get-ChildItem -LiteralPath $templateRoot -Filter "*.md" -Recurse -File -Force)

    foreach ($file in $mdFiles) {
        $rel = $file.FullName.Substring($templateRoot.Length).TrimStart('\', '/') -replace '\\', '/'
        $text = Read-Text $file.FullName
        $lines = $text -split "`n"
        $lineNum = 0

        foreach ($line in $lines) {
            $lineNum++
            # Lines that contain "TEMPLATE" in a context that is NOT the recognized forms
            if ($line -notmatch 'TEMPLATE') { continue }
            # Recognized form 1: <!-- TEMPLATE-DIRECTIVE: ... -->
            if ($line -match '<!--\s*TEMPLATE-DIRECTIVE:') { continue }
            # Recognized form 2: a {placeholder} token from the map
            # (the line mentions TEMPLATE only inside a placeholder or a code span)
            if ($line -match '`[^`]*TEMPLATE[^`]*`') { continue }
            if ($line -match '\{[^}]*TEMPLATE[^}]*\}') { continue }
            # Narrative references to the word "template" in prose are expected
            if ($line -match '\btemplate\b' -and $line -notmatch '\bTEMPLATE-') { continue }
            # A TEMPLATE-DIRECTIVE appearing outside a comment is suspicious
            if ($line -match 'TEMPLATE-DIRECTIVE' -and $line -notmatch '<!--') {
                $problems.Add("$rel line ${lineNum}: TEMPLATE-DIRECTIVE found outside HTML comment: $($line.Trim())")
            }
        }
    }

    Add-Note "template .md files checked: $($mdFiles.Count)"
    return $problems
}

# ==============================================================================
# REPORT
# ==============================================================================

Write-Host ""
Write-Host "  $script:Passed passed, $script:Failed failed." -ForegroundColor ($script:Failed -gt 0 ? "Red" : "Green")
foreach ($failure in $script:Failures) { Write-Host "  failed: $failure" -ForegroundColor Red }

# A filtered run has not exercised every case, so recording it would leave the
# contract check reporting the unrun ones as having no result - which is what it
# should report, but for the wrong reason.
if (-not $Filter) {
    New-Item -ItemType Directory -Path (Split-Path $script:Results -Parent) -Force | Out-Null
    Set-Content -LiteralPath $script:Results -Value $script:Outcomes -Encoding utf8
    Write-Host "  Results written to $script:Results"
}

exit ($script:Failed -gt 0 ? 1 : 0)
