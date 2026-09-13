[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src/HealthBreak.App/HealthBreak.App.csproj'
$buildDirectory = Join-Path $projectRoot "src/HealthBreak.App/bin/x64/$Configuration/net8.0-windows10.0.19041.0/win-x64"
$outputDirectory = Join-Path $projectRoot 'artifacts/HealthBreak-win-x64'
$archivePath = Join-Path $projectRoot 'artifacts/HealthBreak-win-x64.zip'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK or a newer SDK, then restart PowerShell.'
}

if (-not $NoRestore) {
    & dotnet restore $projectFile --configfile (Join-Path $projectRoot 'NuGet.Config') -r win-x64 -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed. Check the error above and network access.' }
}
& dotnet build $projectFile -c $Configuration -r win-x64 -p:Platform=x64 --self-contained true -p:WindowsAppSDKSelfContained=true --no-restore
if ($LASTEXITCODE -ne 0) { throw 'HealthBreak release build failed.' }

$resolvedRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedOutput = [IO.Path]::GetFullPath($outputDirectory)
if (-not $resolvedOutput.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace a directory outside the project: $resolvedOutput"
}
if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
Copy-Item -Path (Join-Path $buildDirectory '*') -Destination $resolvedOutput -Recurse -Force

@'
HEALTHBREAK - URUCHOMIENIE

1. Rozpakuj caly plik ZIP do wybranego katalogu.
2. Uruchom HealthBreak.App.exe.
3. Windows SmartScreen moze pokazac ostrzezenie, poniewaz wersja hackathonowa nie ma komercyjnego podpisu. Wybierz "Wiecej informacji", a nastepnie "Uruchom mimo to" tylko wtedy, gdy plik pochodzi z zaufanego zrodla.
4. Zamkniecie glownego okna pozostawia aplikacje w zasobniku. Aby calkowicie zakonczyc program, wybierz Exit z menu ikony HealthBreak obok zegara.

Aplikacja dziala lokalnie i nie wymaga instalacji .NET ani dostepu do internetu.
Nie przenos ani nie usuwaj pojedynczych plikow z rozpakowanego katalogu.
'@ | Set-Content -LiteralPath (Join-Path $resolvedOutput 'URUCHOMIENIE.txt') -Encoding UTF8

$requiredFiles = @(
    'HealthBreak.App.exe',
    'Assets/HealthBreak.ico',
    'HealthBreak.App.pri',
    'App.xbf',
    'MainWindow.xbf',
    'Views/SettingsView.xbf',
    'Views/BreakWindow.xbf',
    'Resources/Theme.xbf'
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedOutput $relativePath) -PathType Leaf)) {
        throw "Published folder is missing required WinUI resource: $relativePath"
    }
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -LiteralPath $resolvedOutput -DestinationPath $archivePath -CompressionLevel Optimal
Write-Host "Ready: $outputDirectory"
Write-Host "Discord package: $archivePath"
Write-Host 'Distribute the complete folder. Start HealthBreak.App.exe; no SDK is needed on the target PC.'
