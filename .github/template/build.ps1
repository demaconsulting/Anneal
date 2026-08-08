# build.ps1
#
# PURPOSE:
#   Builds the solution and runs all tests, emitting TRX results that
#   `dotnet anneal check-contracts` reads to verify every contract clause is
#   backed by a test that actually passed.
#
#   Results are written to artifacts/tests so they match the default
#   -TestResults glob. Changing that path means changing both, or the pass
#   check silently stops verifying anything.
#
#   The results directory is cleared first. Results accumulate otherwise, and
#   the contract check would be reading an outcome from a previous run
#   alongside the current one.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to add project-specific steps.
#
# MODIFICATION POLICY:
#   Only modify this file to add project-specific operations at the designated
#   [PROJECT-SPECIFIC] extension points, or to update tool versions as needed.
#
# TODO: Replace {ProjectName} with your actual solution/project name.

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

# [PROJECT-SPECIFIC] Add additional build steps here (e.g., packaging, publishing).

exit ($buildError ? 1 : 0)
