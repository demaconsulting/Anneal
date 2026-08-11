# install.ps1
#
# PURPOSE:
#   Installs the Anneal payload into a target repository.
#
#   This repository is laid out exactly as an installed repository is, so every
#   payload path below is its own destination.
#
#   The payload is the contents of .github/ PLUS a vendored copy of the template
#   at .github/template/. Anneal resolves the template from .github/template/
#   first and from template-url second, so the vendored copy is preferred rather
#   than required: it needs no network, and it pins the template to the agent
#   versions installed beside it, so the two cannot drift apart.
#
# USAGE:
#   pwsh ./install.ps1 -TargetRepository ../my-product
#   pwsh ./install.ps1 -TargetRepository ../my-product -Force
#   pwsh ./install.ps1 -TargetRepository ../my-product -Force -Prune
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
    # silently discard local edits to a customized standard.
    [switch] $Force,

    # Review files in the payload directories that this payload does not
    # provide, and delete the ones confirmed. Off by default, and never silent:
    # the files are listed and confirmed before anything is removed, because a
    # repository is free to keep its own agents and standards alongside these.
    [switch] $Prune
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
    @{ From = ".github/agents";                      To = ".github/agents" }
    @{ From = ".github/skills";                      To = ".github/skills" }
    @{ From = ".github/standards";                   To = ".github/standards" }
    @{ From = ".github/template";                    To = ".github/template" }
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

# ==============================================================================
# PRUNE
# Files under the payload directories that this payload does not provide are
# either ours from an older version or the repository's own. The two are told
# apart by retired-payload.txt, and neither is deleted without confirmation.
# ==============================================================================

$owned = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $planned) { [void] $owned.Add([System.IO.Path]::GetFullPath($file.Destination)) }

$retiredList = Join-Path $sourceRoot "retired-payload.txt"
$retired = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $retiredList) {
    Get-Content -LiteralPath $retiredList |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith("#") } |
        ForEach-Object { [void] $retired.Add([System.IO.Path]::GetFullPath((Join-Path $target $_))) }
}

$stale = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $payload) {
    if (-not (Get-Item -LiteralPath (Join-Path $sourceRoot $entry.From)).PSIsContainer) { continue }

    $directory = Join-Path $target $entry.To
    if (-not (Test-Path -LiteralPath $directory)) { continue }

    foreach ($file in (Get-ChildItem -LiteralPath $directory -Recurse -File -Force)) {
        $full = [System.IO.Path]::GetFullPath($file.FullName)
        if ($owned.Contains($full)) { continue }

        $stale.Add([pscustomobject]@{
                Path     = $full
                Relative = $full.Substring($target.Length).TrimStart('\', '/')
                Retired  = $retired.Contains($full)
            })
    }
}

if ($stale.Count -gt 0 -and -not $Prune) {
    Write-Host ""
    Write-Host "$($stale.Count) file(s) under the payload directories are not part of this payload." -ForegroundColor Yellow
    Write-Host "Re-run with -Prune to review and remove them. A stale agent file still gets picked." -ForegroundColor Yellow
}

if ($stale.Count -gt 0 -and $Prune) {
    $groups = @(
        @{ Files = @($stale | Where-Object Retired); Title = "Retired by Anneal — renamed or removed in a later version:" }
        @{ Files = @($stale | Where-Object { -not $_.Retired }); Title = "Not recognized — this repository may have added these itself:" }
    )

    $removed = 0

    foreach ($group in $groups) {
        if ($group.Files.Count -eq 0) { continue }

        Write-Host ""
        Write-Host $group.Title -ForegroundColor Yellow
        foreach ($file in $group.Files) { Write-Host "  $($file.Relative)" }

        $answer = Read-Host "Delete these $($group.Files.Count) file(s)? [y/N]"
        if ($answer -eq "y" -or $answer -eq "yes") {
            foreach ($file in $group.Files) {
                Remove-Item -LiteralPath $file.Path -Force
                $removed++
            }
        }
        else {
            Write-Host "Kept." -ForegroundColor DarkGray
        }
    }

    Write-Host ""
    Write-Host "Pruned $removed file(s)."
}

if ($stale.Count -eq 0 -and $Prune) {
    Write-Host ""
    Write-Host "Nothing to prune."
}

Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Fill in README.md."
Write-Host "  2. Run @helper scaffold the repository structure from the template."
Write-Host "  3. Ask @helper to establish the architecture tree."
Write-Host ""
Write-Host "  @helper is the only agent you invoke yourself; the rest run as sub-agents."

exit 0
