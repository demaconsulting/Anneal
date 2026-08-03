# test-process-contract.ps1
#
# PURPOSE:
#   Verifies the contract clauses of the Process system, as declared in
#   docs/architecture/process.md.
#
#   The Process system is the payload this repository ships: the agent prompts,
#   the standards, the skills, and AGENTS.md. Almost every rule in it is held by
#   prompt and review rather than by a mechanism, so the few properties that CAN
#   be checked mechanically are checked here - a dangling reference or a renamed
#   standard degrades an agent silently in whichever repository the payload was
#   installed into, which is the failure mode furthest from its cause.
#
#   Each case is named exactly as the clause that names it, so that
#   check-contracts.ps1 links the two. The tally this suite writes into
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
# inside fences - are exactly where the mode and tier declaration lines are.
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
    $paths.Add((Repo-Path "AGENTS.md"))
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

# The tier scale, read from the '# Tier {n} - {Qualifier} Change' headings of the
# same standard. The trailing word 'Change' is part of the heading's sentence,
# not of the name, so 'Interior Change' yields the qualifier 'Interior'.
function Get-DefinedTier {
    param([string] $Path)

    $tiers = [ordered]@{}
    foreach ($line in ((Read-Text $Path) -split "`n")) {
        if ($line -match '^#\s*Tier\s+(\d+)\s*—\s*(.+?)\s*$') {
            $tiers[$Matches[1]] = ($Matches[2] -replace '\s+Change$', '')
        }
    }
    return $tiers
}

# ==============================================================================
# CASES
# ==============================================================================

Write-Host "Testing: Process contract (docs/architecture/process.md)"

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
# carries. That is membership, not existence, which is why MIGRATION.md passes
# while absent from this repository.
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
        foreach ($item in (Get-ChildItem -LiteralPath $full -Recurse -File)) {
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
        foreach ($item in (Get-ChildItem -LiteralPath $full -Recurse -File)) {
            [void]$knownExtensions.Add([System.IO.Path]::GetExtension($item.Name))
        }
    }
    foreach ($file in $payloadFiles) {
        [void]$knownExtensions.Add([System.IO.Path]::GetExtension($file))
    }

    # docs/architecture/{system}.md is in this set too, but every token carrying a
    # placeholder is excluded before we get here.
    $requiredFiles = @(
        "README.md", "BACKLOG.md", "CONSTRAINTS.md", "MIGRATION.md",
        "docs/architecture/overview.md"
    )
    # The layout the Project Structure section of AGENTS.md requires of every
    # installed repository. A directory names a location rather than a file, so it
    # is checked against this list rather than against the file list above.
    $requiredDirs = @("docs", "docs/architecture", "src", "test")

    foreach ($file in Get-AgentFile) {
        foreach ($record in (Get-PathToken -Path $file.FullName)) {
            $token = $record.Token
            $where = "$($file.Name):$($record.Line)"
            $bare = $token -replace '/$', ''

            # A token carrying a {placeholder} segment describes the SHAPE of a
            # tree, not a file. docs/architecture/{system}/{section}.md is the
            # documented layout; a concrete path under docs/architecture/{system}/
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

    return $problems
}

# --- PROCESS-03 ---------------------------------------------------------------
# Two loading surfaces count. Five of the eight standards are matrix-only, which
# process.md Composition records as correct: they are product-code standards that
# no process agent names at authoring time.
Test-Case -Name "NoOrphanedStandards" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $agentText = @{}
    foreach ($file in Get-AgentFile) { $agentText[$file.Name] = Read-Text $file.FullName }

    $agents = Read-Text (Repo-Path "AGENTS.md")
    $lines = $agents -split "`n"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^#+\s*Standards Application') { $start = $i; break }
    }
    if ($start -lt 0) {
        $problems.Add("AGENTS.md has no Standards Application section, so the matrix surface cannot be read")
        return $problems
    }
    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^#\s') { $end = $i; break }
    }
    $matrix = ($lines[$start..($end - 1)] -join "`n")

    foreach ($standard in Get-StandardFile) {
        $reached = @()
        if ($matrix -match [regex]::Escape($standard.Name)) { $reached += "the Standards Application matrix" }
        foreach ($name in ($agentText.Keys | Sort-Object)) {
            if ($agentText[$name] -match [regex]::Escape($standard.Name)) { $reached += $name }
        }

        if ($reached.Count -eq 0) {
            $problems.Add("$($standard.Name) is named by no agent prompt and by no entry in the Standards Application matrix, so nothing loads it")
        }
    }

    Add-Note "$((Get-StandardFile).Count) standards checked against $($agentText.Count) prompts and the matrix"
    return $problems
}

# --- PROCESS-04 ---------------------------------------------------------------
# Only the first field is asserted. The enumerated values are owned by AGENTS.md
# and are not what this clause promises.
Test-Case -Name "ReportTemplateShapeIsUniform" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    foreach ($file in Get-AgentFile) {
        $lines = (Read-Text $file.FullName) -split "`n"

        $heading = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^#+\s*Report Template\s*$') { $heading = $i; break }
        }
        if ($heading -lt 0) {
            $problems.Add("$($file.Name): no '# Report Template' section")
            continue
        }

        $open = -1
        for ($i = $heading + 1; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^#\s') { break }
            if ($lines[$i] -match '^\s*```') { $open = $i; break }
        }
        if ($open -lt 0) {
            $problems.Add("$($file.Name): the Report Template section contains no fenced code block")
            continue
        }

        $first = $null
        for ($i = $open + 1; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^\s*```') { break }
            if ($lines[$i] -match '^\*\*([^*]+)\*\*\s*:') { $first = $Matches[1].Trim(); break }
        }

        if (-not $first) {
            $problems.Add("$($file.Name): the report template declares no bolded metadata field")
        }
        elseif ($first -ne "Result") {
            $problems.Add("$($file.Name): the first metadata field is '**$first**'; the contract requires '**Result**'")
        }
    }

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

    $authoring = Repo-Path "docs/architecture/process/prompt-authoring.md"
    $text = Read-Text $authoring
    if ($text -notmatch '(?m)\*\*The context budget is ([\d,]+) tokens\*\*') {
        $problems.Add("prompt-authoring.md no longer declares the context budget in the form '**The context budget is N tokens**'; the ceiling has no readable owner")
        return $problems
    }
    $ceiling = [int]($Matches[1] -replace ',', '')

    $agents = @(Get-AgentFile | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Tokens = (Get-TokenCount $_.FullName) } })
    $standards = @(Get-StandardFile | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Tokens = (Get-TokenCount $_.FullName) } })

    $selected = [System.Collections.Generic.List[object]]::new()
    $selected.Add([pscustomobject]@{ Name = "AGENTS.md"; Tokens = (Get-TokenCount (Repo-Path "AGENTS.md")) })
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
    # "mode" in ordinary English - "The mode and tier decide the workflow". They
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

# --- PROCESS-08 ---------------------------------------------------------------
# The same comparison lint.ps1 makes, including its trimming; a different trim
# reports a phantom one-line drift.
Test-Case -Name "AgentsFileMatchesPristine" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $marker = "# Template Stewardship (This Repository Only)"
    $rootPath = Repo-Path "AGENTS.md"
    $pristinePath = Repo-Path ".github/template/AGENTS.pristine.md"

    foreach ($path in @($rootPath, $pristinePath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            $problems.Add("expected $path to exist")
        }
    }
    if ($problems.Count -gt 0) { return $problems }

    $rootLines = @(Get-Content -LiteralPath $rootPath)
    $index = $rootLines.IndexOf($marker)
    if ($index -lt 0) {
        $problems.Add("AGENTS.md is missing the '$marker' section, so what is shared with the pristine copy is undefined")
        return $problems
    }

    $shared = @($rootLines[0..($index - 1)])
    while ($shared.Count -gt 0 -and $shared[-1] -eq "") { $shared = $shared[0..($shared.Count - 2)] }

    $pristineLines = @(Get-Content -LiteralPath $pristinePath)
    while ($pristineLines.Count -gt 0 -and $pristineLines[-1] -eq "") {
        $pristineLines = $pristineLines[0..($pristineLines.Count - 2)]
    }

    $limit = [math]::Min($shared.Count, $pristineLines.Count)
    for ($i = 0; $i -lt $limit; $i++) {
        if ($shared[$i] -cne $pristineLines[$i]) {
            $problems.Add("AGENTS.md and AGENTS.pristine.md first differ at line $($i + 1): '$($shared[$i])' versus '$($pristineLines[$i])'")
            return $problems
        }
    }
    if ($shared.Count -ne $pristineLines.Count) {
        $longer = if ($shared.Count -gt $pristineLines.Count) { "AGENTS.md" } else { "AGENTS.pristine.md" }
        $extra = if ($shared.Count -gt $pristineLines.Count) { $shared[$limit] } else { $pristineLines[$limit] }
        $problems.Add("AGENTS.md and AGENTS.pristine.md agree for $limit lines, then $longer continues with '$extra'")
    }

    return $problems
}

# --- PROCESS-09 ---------------------------------------------------------------
# Three shapes, one vocabulary. The ordinal pass catches a scale that has been
# extended; the qualifier pass catches one that has been re-labelled; the field
# pass catches a report template that offers a tier no document defines. A
# non-digit placeholder such as 'Tier N' is not an ordinal claim and is not
# matched by any of them.
Test-Case -Name "TierVocabularyIsClosed" -Body {
    $problems = [System.Collections.Generic.List[string]]::new()

    $classification = Repo-Path ".github/standards/change-classification.md"
    $tiers = Get-DefinedTier -Path $classification

    # Fail closed, for the same reason the mode case does.
    if ($tiers.Count -lt 2) {
        $problems.Add("only $($tiers.Count) tier heading(s) parsed from change-classification.md in the form '# Tier {n} — {Qualifier} Change'; the scale has no readable owner")
        return $problems
    }
    $ordinals = @($tiers.Keys)

    $sites = 0
    $files = Get-PayloadTextFile
    foreach ($path in $files) {
        $name = Split-Path $path -Leaf
        $number = 0
        foreach ($line in ((Read-Text $path) -split "`n")) {
            $number++

            # Ordinals, including runs: 'Tier 1/2', 'Tier 1 or 2', 'Tier 1, 2'.
            foreach ($match in [regex]::Matches($line, '(?i)\b[Tt]iers?\s+(\d+(?:\s*(?:/|,|\s(?:or|and)\s)\s*\d+)*)')) {
                $sites++
                foreach ($digit in [regex]::Matches($match.Groups[1].Value, '\d+')) {
                    if ($ordinals -contains $digit.Value) { continue }
                    $problems.Add("$name line ${number}: 'Tier $($digit.Value)' is not an ordinal change-classification.md defines")
                }
            }

            # Qualifiers, compared exactly: a re-labelled tier is as much a
            # vocabulary break as an invented one.
            foreach ($match in [regex]::Matches($line, '\b[Tt]ier\s+(\d+)\s*\(([^)]*)\)')) {
                $sites++
                $ordinal = $match.Groups[1].Value
                $qualifier = $match.Groups[2].Value
                if ($ordinals -notcontains $ordinal) { continue }
                if ($tiers[$ordinal] -cne $qualifier) {
                    $problems.Add("$name line ${number}: 'Tier $ordinal ($qualifier)' contradicts change-classification.md, which names Tier $ordinal '$($tiers[$ordinal])'")
                }
            }

            # A report template's tier field. '**Tier Verdict**' is a different
            # field and deliberately does not match.
            if ($line -match '^\s*(?:-\s*)?\*\*Tier\*\*\s*(?::|—|-)') {
                $sites++
                foreach ($digit in [regex]::Matches($line, '\d')) {
                    if ($ordinals -contains $digit.Value) { continue }
                    $problems.Add("$name line ${number}: the tier field offers '$($digit.Value)', which is not an ordinal change-classification.md defines")
                }
            }
        }
    }

    $named = @(foreach ($ordinal in $ordinals) { "$ordinal ($($tiers[$ordinal]))" })
    Add-Note "tiers: $($named -join ', ')"
    Add-Note "tier sites checked: $sites across $($files.Count) payload files"
    return $problems
}

# --- PROCESS-I1 ---------------------------------------------------------------
# A count alone would pass a repository where the two entry points had swapped
# with the two AGENTS.md names as non-delegatable, so the identities are checked
# against that section rather than assumed.
Test-Case -Name "EntryPointsAreExactlyTwo" -Body {
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

    # The two AGENTS.md names as undelegatable, read from the section that says so.
    $lines = (Read-Text (Repo-Path "AGENTS.md")) -split "`n"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^#+\s*Agent Delegation Guidelines') { $start = $i; break }
    }
    if ($start -lt 0) {
        $problems.Add("AGENTS.md has no Agent Delegation Guidelines section to cross-check the entry points against")
        return $problems
    }
    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^#\s') { $end = $i; break }
    }
    $section = ($lines[$start..($end - 1)] -join " ")

    if ($section -notmatch 'cannot be delegated to[^.]*') {
        $problems.Add("the Agent Delegation Guidelines section names no agents as undelegatable, so the entry points cannot be cross-checked")
        return $problems
    }
    $sentence = $Matches[0]
    $declared = @([regex]::Matches($sentence, '`([a-z0-9-]+)`') | ForEach-Object { $_.Groups[1].Value })

    $onlyFront = @($entryPoints | Where-Object { $declared -notcontains $_ })
    $onlyAgents = @($declared | Where-Object { $entryPoints -notcontains $_ })

    foreach ($name in $onlyFront) {
        $problems.Add("'$name' is user-invocable in front matter but AGENTS.md does not name it as undelegatable")
    }
    foreach ($name in $onlyAgents) {
        $problems.Add("AGENTS.md names '$name' as undelegatable but its front matter is not user-invocable: true")
    }

    if ($entryPoints.Count -ne 2) {
        $problems.Add("$($entryPoints.Count) agents are user-invocable ($($entryPoints -join ', ')); the contract allows exactly two")
    }

    Add-Note "entry points: $($entryPoints -join ', ')"
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
