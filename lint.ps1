# lint.ps1
#
# PURPOSE:
#   Runs all lint checks and reports failures. Exits 1 on error.
#   Used by CI/CD as the merge gate and by the lint-fix agent
#   during pre-PR cleanup.
#
#   To auto-fix formatting issues, run fix.ps1 instead.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to find the designated locations
#   for adding project-specific lint checks.
#
# MODIFICATION POLICY:
#   Only modify this file to add project-specific operations at the designated
#   [PROJECT-SPECIFIC] extension points, or to update tool versions as needed.

# ==============================================================================
# HELPER FUNCTIONS
# ==============================================================================

function Get-VenvActivateScript {
    if (Test-Path ".venv/Scripts/Activate.ps1") { return ".venv/Scripts/Activate.ps1" }  # Windows
    if (Test-Path ".venv/bin/Activate.ps1") { return ".venv/bin/Activate.ps1" }          # Linux/macOS
    return $null
}

function Initialize-PythonVenv {
    if (-not (Test-Path ".venv")) {
        python -m venv .venv
        if ($LASTEXITCODE -ne 0) { return $false }
    }

    $activateScript = Get-VenvActivateScript
    if (-not $activateScript) { return $false }
    & $activateScript
    if (-not (Get-Command deactivate -ErrorAction SilentlyContinue)) { return $false }

    $installSucceeded = $false
    try {
        pip install -r pip-requirements.txt --quiet --disable-pip-version-check
        $installSucceeded = $LASTEXITCODE -eq 0
        return $installSucceeded
    }
    finally {
        if (-not $installSucceeded -and (Get-Command deactivate -ErrorAction SilentlyContinue)) {
            deactivate 2>$null
        }
    }
}

# ==============================================================================
# LINT CHECKS
# Runs all lint checks. Exits 1 if any check fails.
# ==============================================================================

$lintError = $false

# --- PYTHON SECTION ---
# Sets up a virtual environment and runs yamllint.
Write-Host "Linting: YAML..."
$skipPython = -not (Initialize-PythonVenv)
if ($skipPython) { $lintError = $true }

if (-not $skipPython) {
    yamllint .
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
    deactivate
}

# [PROJECT-SPECIFIC] Add additional Python-based lint checks here.
# Example:
#   if (-not $skipPython) {
#       flake8 src/
#       if ($LASTEXITCODE -ne 0) { $lintError = $true }
#   }

# --- NPM SECTION ---
# Installs npm dependencies and runs cspell and markdownlint-cli2.
Write-Host "Linting: spelling and markdown..."
$skipNpm = $false
$env:PUPPETEER_SKIP_DOWNLOAD = "true"
npm install --silent
if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipNpm = $true }

if (-not $skipNpm) {
    # --dot is required: without it the glob skips dot-directories entirely, so
    # .github/ - agents, standards, and the vendored template - goes unchecked.
    npx cspell --no-progress --no-color --quiet --dot "**/*.{md,yaml,yml,json,cs,txt}"
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    npx markdownlint-cli2 "**/*.md"
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
}

# [PROJECT-SPECIFIC] Add additional npm-based lint checks here.
# Example (ESLint for TypeScript):
#   if (-not $skipNpm) {
#       npx eslint "src/**/*.ts"
#       if ($LASTEXITCODE -ne 0) { $lintError = $true }
#   }

# [PROJECT-SPECIFIC] System contracts.
# Anneal runs the script it ships, in place from the template directory, because
# it holds only one copy of it. It is now checked against two profiles at once,
# because it holds two kinds of test and no single set of parameters describes
# both: the Toolkit's clauses are verified by C# boundary tests under a Contract/
# folder recorded in TRX, while the ContractCheck and Process clauses are
# verified by named cases in flat root-level PowerShell suites, declared by a
# quoted name rather than an attribute-marked method, recorded as the text tally
# those suites write. -ContractTestFolder alone would have to be both 'Contract'
# and empty. Every one of those differences is a profile field, so the shipped
# script stays untouched.
#
# The results are produced by build.ps1 - dotnet test plus the two PowerShell
# suites - which CI runs before this script for exactly that reason. Without them
# the pass check reports a warning and verifies existence only.
#
# The profiles are newline-joined into one argument on purpose: under
# `pwsh -File` every argument is a literal string and only the FIRST value of an
# array parameter binds, so a second profile passed as its own argument would be
# discarded and half the contract would go unchecked. Fields within a profile are
# comma-joined for the same reason, and the script splits both itself.
Write-Host "Linting: system contracts..."
$contractProfiles = @(
    "TestRoots=test;TestFilePatterns=*.cs;ContractTestFolder=Contract;TestResults=artifacts/tests/*.trx;TestResultFormat=trx",
    'TestRoots=.;TestFilePatterns=test-check-contracts.ps1,test-process-contract.ps1;TestDeclarationPattern=^\s*Test-Case\s+-Name\s+"(?<name>[^"]+)";ContractTestFolder=;TestResults=artifacts/tests/*.txt;TestResultFormat=text'
) -join "`n"

pwsh -NoProfile -File .github/template/check-contracts.ps1 -TestProfiles $contractProfiles
if ($LASTEXITCODE -ne 0) { $lintError = $true }

# [PROJECT-SPECIFIC] AGENTS.md drift.
# Anneal ships the process it uses, so AGENTS.md exists twice: this repository's
# copy, and the pristine copy installed into other repositories. The pristine one
# carries no per-repository customization, so the two must be identical apart from
# the one section that only makes sense here. A reminder would not hold - the
# template and root .cspell.yaml drifted exactly that way - so it is checked.
Write-Host "Linting: AGENTS.md against the pristine copy..."

$annealSection = "# Template Stewardship (This Repository Only)"
$rootAgents = "AGENTS.md"
$pristineAgents = ".github/template/AGENTS.pristine.md"

if (-not (Test-Path $rootAgents) -or -not (Test-Path $pristineAgents)) {
    Write-Host "error: expected both $rootAgents and $pristineAgents" -ForegroundColor Red
    $lintError = $true
}
else {
    $rootLines = @(Get-Content $rootAgents)
    $marker = $rootLines.IndexOf($annealSection)

    if ($marker -lt 0) {
        Write-Host "error: $rootAgents is missing the '$annealSection' section" -ForegroundColor Red
        $lintError = $true
    }
    else {
        # Everything above the Anneal-only section must match the pristine copy.
        $shared = $rootLines[0..($marker - 1)]
        while ($shared.Count -gt 0 -and $shared[-1] -eq "") { $shared = $shared[0..($shared.Count - 2)] }

        $pristineLines = @(Get-Content $pristineAgents)
        while ($pristineLines.Count -gt 0 -and $pristineLines[-1] -eq "") {
            $pristineLines = $pristineLines[0..($pristineLines.Count - 2)]
        }

        $drift = Compare-Object $shared $pristineLines
        if ($drift) {
            Write-Host "error: $rootAgents has drifted from $pristineAgents" -ForegroundColor Red
            Write-Host "  Everything before '$annealSection' must match the pristine copy exactly." -ForegroundColor Red
            foreach ($line in $drift | Select-Object -First 10) {
                $side = if ($line.SideIndicator -eq "<=") { "only in $rootAgents" } else { "only in $pristineAgents" }
                Write-Host "  $side : $($line.InputObject)" -ForegroundColor Red
            }
            $lintError = $true
        }
    }
}

exit ($lintError ? 1 : 0)
