#!/usr/bin/env pwsh
#
# SYNOPSIS
#   Compiles one document collection under docs/ into HTML and then PDF.
#
# DESCRIPTION
#   Every folder under docs/ is a document, not a loose page. This script is the
#   single implementation of that build; each document's build.bat calls it so
#   that the command lives in exactly one place.
#
#   The HTML name is derived from the folder. The PDF name cannot be derived --
#   "FileAssert Software Design Document" is the title of a document published as
#   "FileAssert Software Design.pdf" -- so the published name is passed in.

[CmdletBinding()]
param(
    # Folder name under docs/, for example "user-guide".
    [Parameter(Mandatory = $true)]
    [string]$Document,

    # Published PDF name, without the .pdf extension.
    [Parameter(Mandatory = $true)]
    [string]$Name,

    # Version stamped into the document. Defaults to a local working build.
    [string]$Version = "0.0.0-local",

    # Publication date stamped into the document.
    [string]$Date = (Get-Date -Format "yyyy-MM-dd"),

    # Skip 'dotnet tool restore' and 'npm install' when they have already run.
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

# Paths are relative to the repository root because definition.yaml resolves
# resource-path and input-files from there.
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    $definition = "docs/$Document/definition.yaml"
    if (-not (Test-Path $definition)) {
        Write-Host "error: no such document: $definition" -ForegroundColor Red
        Write-Host "  Every folder under docs/ needs a definition.yaml listing its input files." -ForegroundColor Red
        exit 1
    }

    if (-not $NoRestore) {
        Write-Host "Restoring tools..." -ForegroundColor Cyan
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        npm install --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    New-Item -ItemType Directory -Force -Path "docs/$Document/generated" | Out-Null
    New-Item -ItemType Directory -Force -Path "docs/generated" | Out-Null

    # mermaid-filter ships a .cmd shim on Windows and an extensionless script elsewhere.
    $mermaidFilter = if ($IsWindows) {
        "node_modules/.bin/mermaid-filter.cmd"
    } else {
        "node_modules/.bin/mermaid-filter"
    }

    $html = "docs/$Document/generated/$Document.html"
    $pdf = "docs/generated/$Name.pdf"

    Write-Host "Generating $html..." -ForegroundColor Cyan
    $pandocArgs = @(
        "--defaults", $definition
        "--lua-filter", "docs/template/collection-links.lua"
        "--metadata", "version=$Version"
        "--metadata", "date=$Date"
        "--output", $html
    )
    if (Test-Path $mermaidFilter) {
        $pandocArgs = @("--filter", $mermaidFilter) + $pandocArgs
    }

    dotnet pandoc @pandocArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Generating $pdf..." -ForegroundColor Cyan
    dotnet weasyprint --pdf-variant pdf/a-3u $html $pdf
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Built $pdf" -ForegroundColor Green
} finally {
    Pop-Location
}
