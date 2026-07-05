$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$launchSettingsPath = Join-Path $root "Properties\launchSettings.json"
$projectPath = Join-Path $root "wslc-desktop.csproj"
$devScriptPath = Join-Path $PSScriptRoot "Start-Development.ps1"
$packageScriptPath = Join-Path $PSScriptRoot "Start-PackagedDebug.ps1"
$installCertScriptPath = Join-Path $PSScriptRoot "Install-DevelopmentCertificate.ps1"
$signScriptPath = Join-Path $PSScriptRoot "Sign-DevelopmentOutput.ps1"
$workflowPath = Join-Path $root "docs\DEVELOPMENT_WORKFLOW.md"

if (-not (Test-Path $launchSettingsPath)) {
    throw "Missing Properties\launchSettings.json"
}

if (-not (Test-Path $projectPath)) {
    throw "Missing wslc-desktop.csproj"
}

$launchSettings = Get-Content -Raw $launchSettingsPath
$project = Get-Content -Raw $projectPath
$projectXml = [xml]$project
$packageIndex = $launchSettings.IndexOf('"wslc-desktop (Package)"', [StringComparison]::Ordinal)
$unpackagedIndex = $launchSettings.IndexOf('"wslc-desktop (Unpackaged)"', [StringComparison]::Ordinal)

if ($packageIndex -lt 0) {
    throw "launchSettings.json must contain the packaged development profile."
}

if ($unpackagedIndex -lt 0) {
    throw "launchSettings.json must retain the unpackaged investigation profile."
}

if ($packageIndex -gt $unpackagedIndex) {
    throw "The packaged profile must be listed before the unpackaged profile so plain dotnet run follows the signed packaged debug path."
}

if ($launchSettings -notmatch '"wslc-desktop \(Unpackaged\)"\s*:\s*\{\s*"commandName"\s*:\s*"Project"') {
    throw "The unpackaged profile must use commandName Project."
}

if ($launchSettings -notmatch '"wslc-desktop \(Package\)"\s*:\s*\{\s*"commandName"\s*:\s*"MsixPackage"') {
    throw "The packaged profile must use commandName MsixPackage."
}

$enableWinAppRunSupport = $projectXml.SelectNodes("/Project/PropertyGroup/EnableWinAppRunSupport") |
    Select-Object -First 1 -ExpandProperty InnerText

if ($null -ne $enableWinAppRunSupport) {
    throw "wslc-desktop.csproj must not disable WinApp Run Support; signed packaged debug is the supported dotnet run path."
}

if ($project -notmatch 'Name="SignDevelopmentLooseLayout"' -or $project -notmatch 'Sign-DevelopmentOutput\.ps1') {
    throw "wslc-desktop.csproj must expose a development loose-layout signing target."
}

foreach ($path in @($devScriptPath, $packageScriptPath, $installCertScriptPath, $signScriptPath, $workflowPath)) {
    if (-not (Test-Path $path)) {
        throw "Missing $($path.Substring($root.Length + 1))"
    }
}

$devScript = Get-Content -Raw $devScriptPath
$packageScript = Get-Content -Raw $packageScriptPath
$workflow = Get-Content -Raw $workflowPath

if ($devScript -notmatch 'Start-PackagedDebug\.ps1') {
    throw "Start-Development.ps1 must use the signed packaged debug script."
}

if ($packageScript -notmatch 'Sign-DevelopmentOutput\.ps1' -or $packageScript -notmatch 'winapp run' -or $packageScript -notmatch '--debug-output') {
    throw "Start-PackagedDebug.ps1 must sign the loose layout and use winapp run --debug-output for package diagnostics."
}

foreach ($needle in @("Start-Development.ps1", "dotnet run", "wslc-desktop (Unpackaged)", "wslc-desktop (Package)", "WDAC", "winapp run --debug-output", "Install-DevelopmentCertificate.ps1", "0x800711C7")) {
    if ($workflow -notmatch [regex]::Escape($needle)) {
        throw "DEVELOPMENT_WORKFLOW.md must document $needle."
    }
}

"RUN_PROFILES_OK"
