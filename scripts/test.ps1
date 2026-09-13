[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK or a newer SDK, then restart PowerShell.'
}

$testProjects = @(
    'tests/HealthBreak.Core.Tests/HealthBreak.Core.Tests.csproj',
    'tests/HealthBreak.Data.Tests/HealthBreak.Data.Tests.csproj'
)
foreach ($relativePath in $testProjects) {
    $projectFile = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) { throw "Missing test project: $projectFile" }
    if (-not $NoRestore) {
        & dotnet restore $projectFile --configfile (Join-Path $projectRoot 'NuGet.Config')
        if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed for $relativePath" }
    }
    & dotnet test $projectFile -c $Configuration --no-restore -p:RollForward=Major --logger 'console;verbosity=normal' --logger 'trx' --results-directory (Join-Path $projectRoot 'artifacts/TestResults')
    if ($LASTEXITCODE -ne 0) { throw "Tests failed for $relativePath" }
}
Write-Host 'Core and SQLite tests passed. Continue with the interactive Windows checklist in docs/TESTING.md.'
