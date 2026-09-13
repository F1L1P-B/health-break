[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$DataDir,
    [switch]$Background,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src/HealthBreak.App/HealthBreak.App.csproj'
$outputDirectory = Join-Path $projectRoot "src/HealthBreak.App/bin/x64/$Configuration/net8.0-windows10.0.19041.0/win-x64"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK or a newer SDK, then restart PowerShell.'
}

if (-not $NoRestore) {
    & dotnet restore $projectFile --configfile (Join-Path $projectRoot 'NuGet.Config') -r win-x64 -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed. Check the error above and network access.' }
}
& dotnet build $projectFile -c $Configuration -r win-x64 -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw 'HealthBreak build failed.' }

$executable = Join-Path $outputDirectory 'HealthBreak.App.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Missing executable: $executable" }
$applicationArguments = @()
if ($DataDir) { $applicationArguments += @('--data-dir', [System.IO.Path]::GetFullPath($DataDir)) }
if ($Background) { $applicationArguments += '--background' }
& $executable @applicationArguments
