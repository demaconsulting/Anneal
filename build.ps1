# build.ps1
#
# PURPOSE:
#   Builds the solution and runs all tests, emitting TRX results that
#   check-contracts.ps1 reads to verify every contract clause is backed by a
#   test that actually passed.
#
#   Results are written to artifacts/tests so they match the default
#   -TestResults glob in check-contracts.ps1. Changing that path means changing
#   both, or the pass check silently stops verifying anything.
#
#   The results directory is cleared first. Results accumulate otherwise, and
#   check-contracts.ps1 would be reading an outcome from a previous run
#   alongside the current one.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to add project-specific steps.
#
# MODIFICATION POLICY:
#   Only modify this file to add project-specific operations at the designated
#   [PROJECT-SPECIFIC] extension points, or to update tool versions as needed.

$buildError = $false

Write-Host "Restoring dependencies..."
dotnet restore
if ($LASTEXITCODE -ne 0) { $buildError = $true }

Write-Host "Building..."
dotnet build --no-restore --configuration Release
if ($LASTEXITCODE -ne 0) { $buildError = $true }

Write-Host "Running tests..."
if (Test-Path "artifacts/tests") { Remove-Item "artifacts/tests" -Recurse -Force }
dotnet test --no-build --configuration Release --logger trx --results-directory artifacts/tests
if ($LASTEXITCODE -ne 0) { $buildError = $true }

# [PROJECT-SPECIFIC] Anneal's PowerShell suites.
# Most of what Anneal ships is prose and scripts rather than compiled code, so
# two of its three contracts are verified by suites written in PowerShell. They
# run here rather than anywhere else because "all tests" is what this script
# promises, and because both write their results into artifacts/tests alongside
# the TRX files - which is cleared above, so they must run after that point and
# not before.
Write-Host "Running the PowerShell suites..."
pwsh -NoProfile -File ./test-check-contracts.ps1
if ($LASTEXITCODE -ne 0) { $buildError = $true }

pwsh -NoProfile -File ./test-process-contract.ps1
if ($LASTEXITCODE -ne 0) { $buildError = $true }

exit ($buildError ? 1 : 0)
