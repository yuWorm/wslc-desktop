$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$stopScriptPath = Join-Path $PSScriptRoot "Stop-DevelopmentProcesses.ps1"
$packageScriptPath = Join-Path $PSScriptRoot "Start-PackagedDebug.ps1"
$projectPath = Join-Path $root "wslc-desktop.csproj"

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

if (-not (Test-Path $stopScriptPath)) {
    throw "Missing scripts\Stop-DevelopmentProcesses.ps1"
}

if (-not (Test-Path $packageScriptPath)) {
    throw "Missing scripts\Start-PackagedDebug.ps1"
}

if (-not (Test-Path $projectPath)) {
    throw "Missing wslc-desktop.csproj"
}

$stopScript = Get-Content -Raw $stopScriptPath
$packageScript = Get-Content -Raw $packageScriptPath
$project = Get-Content -Raw $projectPath

Assert-Contains $stopScript "SupportsShouldProcess" "Stop-DevelopmentProcesses.ps1 must support -WhatIf."
Assert-Contains $stopScript "wslc-desktop" "Stop-DevelopmentProcesses.ps1 must target the development GUI process."
Assert-Contains $stopScript "wslcd-desktop" "Stop-DevelopmentProcesses.ps1 must target the development daemon process."
Assert-Contains $stopScript "Get-CimInstance\s+Win32_Process" "Stop-DevelopmentProcesses.ps1 must inspect command lines for loose-layout child processes."
Assert-Contains $stopScript "StartsWith" "Stop-DevelopmentProcesses.ps1 must scope process paths to this repository."
Assert-Contains $stopScript "Stop-Process\s+-Id" "Stop-DevelopmentProcesses.ps1 must stop matching processes by PID."
Assert-Contains $packageScript "Find-PackageManifest" "Start-PackagedDebug.ps1 must use deterministic manifest selection."
Assert-Contains $packageScript 'bin\\\$Platform\\\$Configuration' "Start-PackagedDebug.ps1 must prefer the current platform/configuration output."
Assert-Contains $project 'Name="StopDevelopmentProcessesBeforeWinAppRun"' "wslc-desktop.csproj must stop stale development processes before dotnet run launches WinApp."
Assert-Contains $project 'BeforeTargets="_WinAppCopyContentToLooseLayout;_WinAppBuildRunArgs;RunPackagedApp"' "StopDevelopmentProcessesBeforeWinAppRun must run before WinApp loose-layout copy and launch."
Assert-Contains $project 'Stop-DevelopmentProcesses\.ps1' "StopDevelopmentProcessesBeforeWinAppRun must call the repository-scoped cleanup script."
Assert-Contains $project 'Name="SignDevelopmentLooseLayout"[\s\S]*AfterTargets="CopyWslcdToOutput"' "SignDevelopmentLooseLayout must run after wslcd output is copied."
Assert-Contains $project 'Name="SignDevelopmentLooseLayout"[\s\S]*BeforeTargets="_WinAppPrepareRunArguments;RunPackagedApp"' "SignDevelopmentLooseLayout must also be available before dotnet run launches WinApp."
Assert-Contains $project 'Sign-DevelopmentOutput\.ps1&quot; -Path &quot;\$\(OutDir\)\.&quot;' "SignDevelopmentLooseLayout must sign the WinApp input directory."
Assert-Contains $project 'Sign-DevelopmentOutput\.ps1&quot; -Path &quot;\$\(WinAppLooseLayoutPath\)\.&quot;' "SignDevelopmentLooseLayout must sign the loose AppX output."

if ($packageScript -match 'if\s*\(\$_.FullName\s*-match\s*"\\\\AppX\\\\"\)\s*\{\s*0\s*\}') {
    throw "Start-PackagedDebug.ps1 must not prefer stale AppX cache manifests over the fresh build output."
}

$stopCallIndex = $packageScript.IndexOf("Stop-DevelopmentProcesses.ps1", [StringComparison]::Ordinal)
$buildIndex = $packageScript.IndexOf("& dotnet @buildArgs", [StringComparison]::Ordinal)

if ($stopCallIndex -lt 0) {
    throw "Start-PackagedDebug.ps1 must call Stop-DevelopmentProcesses.ps1 before building."
}

if ($buildIndex -lt 0) {
    throw "Start-PackagedDebug.ps1 must still build with dotnet."
}

if ($stopCallIndex -gt $buildIndex) {
    throw "Start-PackagedDebug.ps1 must stop existing development processes before dotnet build."
}

powershell -NoProfile -ExecutionPolicy Bypass -File $stopScriptPath -WhatIf -Quiet
if ($LASTEXITCODE -ne 0) {
    throw "Stop-DevelopmentProcesses.ps1 -WhatIf failed with exit code $LASTEXITCODE."
}

"DEVELOPMENT_LAUNCH_SOURCE_OK"
