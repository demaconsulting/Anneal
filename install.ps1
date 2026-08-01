# install.ps1
#
# PURPOSE:
#   Installs the Anneal payload into a target repository.
#
#   The payload is the contents of agents/ PLUS a vendored copy of template/ at
#   .github/template/. The vendored copy is not optional in practice: AGENTS.md
#   resolves the template from .github/template/ first and from template-url
#   second, and until Anneal is published that URL is unreachable. Without the
#   vendored copy, template-sync and software-architect can only report
#   INCOMPLETE. Vendoring also pins the template to the agent versions installed
#   beside it, so the two cannot drift apart.
#
# USAGE:
#   pwsh ./install.ps1 -TargetRepository ../my-product
#   pwsh ./install.ps1 -TargetRepository ../my-product -Force
#
# EXIT CODES:
#   0 - installed
#   1 - target missing, or an existing file would have been overwritten

[CmdletBinding()]
param(
    # Root of the repository to install into.
    [Parameter(Mandatory = $true)]
    [string] $TargetRepository,

    # Overwrite files that already exist. Off by default so that a re-run cannot
    # silently discard local edits to AGENTS.md or a customized standard.
    [switch] $Force
)

$ErrorActionPreference = "Stop"

$sourceRoot = $PSScriptRoot

if (-not (Test-Path -LiteralPath $TargetRepository)) {
    Write-Host "error: target repository not found: $TargetRepository" -ForegroundColor Red
    exit 1
}

$target = (Resolve-Path -LiteralPath $TargetRepository).Path

# Source path relative to this repository, destination relative to the target.
$payload = @(
    @{ From = "agents/AGENTS.md";          To = "AGENTS.md" }
    @{ From = "agents/.github/agents";     To = ".github/agents" }
    @{ From = "agents/.github/skills";     To = ".github/skills" }
    @{ From = "agents/.github/standards";  To = ".github/standards" }
    @{ From = "template";                  To = ".github/template" }
)

# ==============================================================================
# PLAN
# Every file is resolved and checked for collision before anything is written,
# so a conflict halfway through cannot leave the target half-installed.
# ==============================================================================

$planned = [System.Collections.Generic.List[object]]::new()
$conflicts = [System.Collections.Generic.List[string]]::new()

foreach ($entry in $payload) {
    $from = Join-Path $sourceRoot $entry.From

    if (-not (Test-Path -LiteralPath $from)) {
        Write-Host "error: payload missing from this repository: $($entry.From)" -ForegroundColor Red
        exit 1
    }

    $item = Get-Item -LiteralPath $from

    if ($item.PSIsContainer) {
        $files = Get-ChildItem -LiteralPath $from -Recurse -File -Force |
            Where-Object { $_.FullName -notmatch '[/\\](bin|obj|node_modules|\.venv|artifacts)[/\\]' }

        foreach ($file in $files) {
            $relative = $file.FullName.Substring($item.FullName.Length).TrimStart('\', '/')
            $planned.Add([pscustomobject]@{
                    Source      = $file.FullName
                    Destination = Join-Path $target (Join-Path $entry.To $relative)
                })
        }
    }
    else {
        $planned.Add([pscustomobject]@{
                Source      = $item.FullName
                Destination = Join-Path $target $entry.To
            })
    }
}

foreach ($file in $planned) {
    if ((Test-Path -LiteralPath $file.Destination) -and -not $Force) {
        $conflicts.Add($file.Destination.Substring($target.Length).TrimStart('\', '/'))
    }
}

if ($conflicts.Count -gt 0) {
    Write-Host "error: $($conflicts.Count) file(s) already exist. Re-run with -Force to overwrite:" -ForegroundColor Red
    foreach ($conflict in $conflicts) { Write-Host "  $conflict" -ForegroundColor Red }
    exit 1
}

# ==============================================================================
# INSTALL
# ==============================================================================

foreach ($file in $planned) {
    $directory = Split-Path -Parent $file.Destination
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    Copy-Item -LiteralPath $file.Source -Destination $file.Destination -Force
}

Write-Host "Installed $($planned.Count) file(s) into $target"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Replace the TODO values in AGENTS.md under 'Project Overview'."
Write-Host "  2. Run @template-sync Scaffold to lay down the repository structure."
Write-Host "  3. Run @software-architect to establish the architecture tree."

exit 0
