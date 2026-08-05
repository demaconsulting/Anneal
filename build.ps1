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
#
# test-check-contracts.ps1 is the exception: it now drives the check as the
# "dotnet anneal check-contracts" operation, so it runs after the Toolkit tool is
# refreshed below rather than here.
Write-Host "Running the PowerShell suites..."
pwsh -NoProfile -File ./test-process-contract.ps1
if ($LASTEXITCODE -ne 0) { $buildError = $true }

# [PROJECT-SPECIFIC] Make the Toolkit invocable as "dotnet anneal".
# The Toolkit is acquired here the same way a downstream repository acquires it:
# packed, then restored through the tool manifest. A packaging or manifest fault
# therefore surfaces on this build rather than at somebody else's install.
#
# The manifest names a version that is published nowhere, so "dotnet tool restore"
# on its own cannot find it on a fresh clone - the package has to be built first,
# which is what the pack below does. Publishing the Toolkit ends that ordering
# requirement.
#
# The reinstall is gated on the payload, because NuGet caches a package by version
# and this version is reused across builds: a rebuilt payload would otherwise be
# ignored and the installed tool would stay stale without saying so. Only this
# package's cache entry is evicted. Clearing the NuGet cache wholesale would take
# unrelated packages with it.
Write-Host "Refreshing the Toolkit tool..."
$toolkitPackage = "demaconsulting.anneal.toolkit"
$toolkitProject = Join-Path $PSScriptRoot "src/DemaConsulting.Anneal.Toolkit/DemaConsulting.Anneal.Toolkit.csproj"
$toolkitFeed = Join-Path $PSScriptRoot "artifacts/toolkit-feed"
$toolkitStamp = Join-Path $PSScriptRoot "artifacts/toolkit-installed.sha256"

dotnet pack $toolkitProject --no-build --configuration Release --output $toolkitFeed
$packed = $LASTEXITCODE -eq 0
if (-not $packed) { $buildError = $true }

# What the gate compares is the assembly that was just built against the one recorded
# when the tool was last installed. The build is deterministic, so unchanged source
# hashes the same and the install is skipped; an edit changes the hash whether or not
# it has been committed, so the tool follows the working tree rather than the history.
# Nothing here reads git, so a dirty tree, a detached HEAD and a clone with no commits
# are all the same case. The packed .nupkg is not the thing hashed: a zip carries entry
# timestamps, so its bytes move on every pack and the gate would never hold.
#
# Which assembly that is comes from the project's target framework, not from a search of
# bin/Release: output left there by an earlier target framework sits beside the current
# one under the same file name, so a search can hash a payload nothing packed and then
# report the tool as current. Ambiguity is not settled by sorting - a target framework
# that cannot be read as exactly one value leaves the assembly unidentified, which is
# the case below.
#
# A recorded hash only means the payload was installed once. Ask the tool whether it is
# still there, so a stamp that outlives the installation reinstalls instead of trusting
# itself.
$toolkitTfm = if (Test-Path -LiteralPath $toolkitProject)
{
    ([xml](Get-Content -LiteralPath $toolkitProject -Raw)).Project.PropertyGroup.TargetFramework |
        Where-Object { $_ } | Select-Object -First 1
}
else { "" }
$toolkitAssembly = if ($toolkitTfm)
{
    Join-Path $PSScriptRoot "src/DemaConsulting.Anneal.Toolkit/bin/Release/$toolkitTfm/DemaConsulting.Anneal.Toolkit.dll"
}
else { "" }
$built = if ($toolkitAssembly -and (Test-Path -LiteralPath $toolkitAssembly))
{
    (Get-FileHash -LiteralPath $toolkitAssembly -Algorithm SHA256).Hash
}
else { "" }
$stamped = if (Test-Path $toolkitStamp) { (Get-Content $toolkitStamp -Raw).Trim() } else { "" }

& dotnet anneal version *> $null
$present = $LASTEXITCODE -eq 0

if (-not $packed)
{
    # The pack has already failed the build. Restoring anyway would install whatever the
    # feed already holds, which is the stale payload this gate exists to prevent.
    Write-Host "  The Toolkit package was not produced, so the tool is left as it is."
}
elseif (-not $built)
{
    # Same reasoning as the global-packages branch below: a gate that cannot establish what
    # was built cannot promise the installed tool matches the source, so it says so and fails
    # rather than installing on a claim it cannot support.
    Write-Host "  Could not hash the built Toolkit assembly, so the installed tool cannot be shown to"
    Write-Host "  match the source. Expected one target framework in $toolkitProject and its output at:"
    Write-Host "  $(if ($toolkitAssembly) { $toolkitAssembly } else { '<target framework unresolved>' })"
    $buildError = $true
}
elseif ($present -and $built -eq $stamped)
{
    Write-Host "  Toolkit is current."
}
else
{
    # The stamp describes an installation that is about to be replaced. Drop it first so
    # that an interrupted or failed install leaves no claim behind.
    if (Test-Path $toolkitStamp) { Remove-Item $toolkitStamp -Force }

    # --force-english-output because the label is otherwise translated and the parse
    # would read the wrong thing without saying so. A path that does not resolve fails
    # the build: continuing would install over a cached package of the same version,
    # which is the stale payload this gate exists to prevent.
    $locals = ((& dotnet nuget locals global-packages --list --force-english-output) -join "`n")
    $globalPackages = if ($locals -match 'global-packages:\s*(\S.*)') { $Matches[1].Trim() } else { "" }

    if (-not $globalPackages -or -not (Test-Path -LiteralPath $globalPackages))
    {
        Write-Host "  Could not resolve the NuGet global packages folder, so the cached Toolkit"
        Write-Host "  package cannot be evicted and the installed tool would be whatever is already"
        Write-Host "  cached. Reported by 'dotnet nuget locals global-packages --list':"
        Write-Host "  $locals"
        $buildError = $true
    }
    else
    {
        $cached = Join-Path $globalPackages $toolkitPackage
        if (Test-Path $cached) { Remove-Item $cached -Recurse -Force }

        dotnet tool restore --add-source $toolkitFeed
        if ($LASTEXITCODE -ne 0) { $buildError = $true }
        else { Set-Content -Path $toolkitStamp -Value $built }
    }
}

# [PROJECT-SPECIFIC] The contract-check suite, driven against the Toolkit.
# test-check-contracts.ps1 exercises the CONTRACT-CHECK-* fixtures against the
# "dotnet anneal check-contracts" operation, so it can only run once the tool is
# installed - which the refresh above has just done. Like the other suites it
# writes its tally into artifacts/tests, which was cleared earlier in this script.
Write-Host "Running the contract-check suite..."
pwsh -NoProfile -File ./test-check-contracts.ps1
if ($LASTEXITCODE -ne 0) { $buildError = $true }

exit ($buildError ? 1 : 0)
