# agent-metrics.ps1
#
# PURPOSE:
#   Harvests .agent-logs/*.md into a bounded summary of agent behavior.
#   Output length is effectively constant regardless of corpus size —
#   it summarizes, never lists.
#
#   This is a diagnostic, not a gate. It never modifies the corpus,
#   has no pass/fail, and is not wired into CI or lint.
#
# USAGE:
#   pwsh ./agent-metrics.ps1
#   pwsh ./agent-metrics.ps1 -Path .agent-logs

[CmdletBinding()]
param(
    # Directory containing agent report markdown files.
    [string] $Path = ".agent-logs"
)

$ErrorActionPreference = "Stop"

# --- Resolve known agent names from .github/agents/*.agent.md ---
# Used for longest-prefix matching against report filenames.
$knownAgents = @()
$agentsDir = Join-Path $PSScriptRoot ".github" "agents"
if (Test-Path $agentsDir) {
    $knownAgents = @(Get-ChildItem -Path $agentsDir -Filter "*.agent.md" -File -ErrorAction SilentlyContinue |
        ForEach-Object { $_.BaseName -replace '\.agent$', '' } |
        Sort-Object { $_.Length } -Descending)
}

# --- Guard: missing or empty corpus ---
if (-not (Test-Path $Path)) {
    Write-Host "agent-metrics: no corpus directory at '$Path'. Nothing to report."
    exit 0
}

$reports = @(Get-ChildItem -Path $Path -Filter "*.md" -File -ErrorAction SilentlyContinue)
if ($reports.Count -eq 0) {
    Write-Host "agent-metrics: corpus at '$Path' is empty. Nothing to report."
    exit 0
}

# ==============================================================================
# HARVEST
# Parse each report filename and header fields into a compact record.
# ==============================================================================

$records = @()
foreach ($file in $reports) {
    $record = @{
        Agent   = ""
        Subject = ""
        Result  = ""
        Tier    = ""
        Mode    = ""
        Repairs = ""
        Size    = $file.Length
        Time    = $file.LastWriteTime
    }

    # Parse filename: {agent}-{subject}-{id}.md
    # Use longest-prefix match against known agent names, fall back to first token.
    $baseName = $file.BaseName
    $matched = $false
    foreach ($agentName in $knownAgents) {
        if ($baseName -eq $agentName -or $baseName.StartsWith("$agentName-")) {
            $record.Agent = $agentName
            $remainder = $baseName.Substring($agentName.Length).TrimStart('-')
            if ($remainder -ne "") {
                $rParts = $remainder -split "-"
                if ($rParts.Count -ge 2) {
                    $record.Subject = ($rParts[0..($rParts.Count - 2)] -join "-")
                }
            }
            $matched = $true
            break
        }
    }
    if (-not $matched) {
        $parts = $baseName -split "-"
        if ($parts.Count -ge 3) {
            $record.Agent = $parts[0]
            $record.Subject = ($parts[1..($parts.Count - 2)] -join "-")
        }
        else {
            $record.Agent = $baseName
        }
    }

    # Read only the first 30 lines to find header fields
    $head = Get-Content $file.FullName -TotalCount 30 -ErrorAction SilentlyContinue
    if ($head) {
        foreach ($line in $head) {
            if ($line -match '^\*\*Result\*\*:\s*(.+)') { $record.Result = $Matches[1].Trim() }
            elseif ($line -match '^\*\*Tier\*\*:\s*(.+)') { $record.Tier = $Matches[1].Trim() }
            elseif ($line -match '^\*\*Mode\*\*:\s*(.+)') { $record.Mode = $Matches[1].Trim() }
            elseif ($line -match '^\*\*Repairs Used\*\*:\s*(.+)') { $record.Repairs = $Matches[1].Trim() }
        }
    }

    $records += [PSCustomObject]$record
}

# ==============================================================================
# SUMMARIZE — bounded output regardless of corpus size
# ==============================================================================

Write-Host ""
Write-Host "=== Agent Metrics Summary ==="
Write-Host ""

# --- Corpus Span and Volume ---
$earliest = ($records | Sort-Object Time | Select-Object -First 1).Time
$latest = ($records | Sort-Object Time -Descending | Select-Object -First 1).Time
$span = $latest - $earliest
$totalSize = ($records | Measure-Object -Property Size -Sum).Sum
Write-Host "Corpus: $($records.Count) reports, $('{0:N0}' -f ($totalSize / 1KB)) KB total"
Write-Host "Span:   $($earliest.ToString('yyyy-MM-dd HH:mm')) to $($latest.ToString('yyyy-MM-dd HH:mm')) ($([math]::Round($span.TotalHours, 1)) hours)"
Write-Host ""

# --- Outcome Distribution Per Agent ---
Write-Host "--- Outcome Distribution ---"
$agents = $records | Group-Object Agent | Sort-Object Count -Descending
foreach ($group in $agents) {
    $outcomes = $group.Group | Group-Object Result
    $parts = @()
    foreach ($o in ($outcomes | Sort-Object Count -Descending)) {
        $label = if ($o.Name -eq "") { "unknown" } else { $o.Name }
        $parts += "$($label):$($o.Count)"
    }
    Write-Host ("  {0,-25} n={1,-4} {2}" -f $group.Name, $group.Count, ($parts -join "  "))
}
Write-Host ""

# --- Repair Loops ---
# Reports sharing a subject indicate verification failed and forced another pass.
Write-Host "--- Repair Loops (subjects with >1 report) ---"
$subjects = $records | Where-Object { $_.Subject -ne "" } | Group-Object Subject |
    Where-Object { $_.Count -gt 1 } | Sort-Object Count -Descending |
    Select-Object -First 10
if ($subjects.Count -eq 0) {
    Write-Host "  (none detected)"
}
else {
    foreach ($s in $subjects) {
        $agents_involved = ($s.Group | Select-Object -ExpandProperty Agent -Unique) -join ","
        Write-Host ("  {0,-45} passes={1}  agents={2}" -f $s.Name, $s.Count, $agents_involved)
    }
}
Write-Host ""

# --- Verbosity Trend Per Agent ---
# Compare mean report size: first half vs second half of corpus (by time).
Write-Host "--- Verbosity Trend (early-half vs late-half mean size) ---"
$sorted = $records | Sort-Object Time
$mid = [math]::Floor($sorted.Count / 2)
if ($mid -gt 0) {
    $earlyHalf = $sorted[0..($mid - 1)]
    $lateHalf = $sorted[$mid..($sorted.Count - 1)]

    $agentNames = $records | Select-Object -ExpandProperty Agent -Unique | Sort-Object
    foreach ($name in $agentNames) {
        $early = @($earlyHalf | Where-Object { $_.Agent -eq $name })
        $late = @($lateHalf | Where-Object { $_.Agent -eq $name })
        if ($early.Count -ge 2 -and $late.Count -ge 2) {
            $earlyMean = ($early | Measure-Object -Property Size -Average).Average
            $lateMean = ($late | Measure-Object -Property Size -Average).Average
            if ($earlyMean -gt 0) {
                $pct = [math]::Round((($lateMean - $earlyMean) / $earlyMean) * 100, 0)
                $arrow = if ($pct -gt 0) { "+$pct%" } else { "$pct%" }
                Write-Host ("  {0,-25} early={1,6:N0}B  late={2,6:N0}B  change={3}" -f $name, $earlyMean, $lateMean, $arrow)
            }
        }
    }
}
Write-Host ""
Write-Host "=== End ==="
