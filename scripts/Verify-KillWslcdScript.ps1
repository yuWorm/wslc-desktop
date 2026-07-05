$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $PSScriptRoot "Kill-Wslcd.ps1"

if (-not (Test-Path $scriptPath)) {
    throw "Missing scripts\Kill-Wslcd.ps1"
}

$script = Get-Content -Raw $scriptPath

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

Assert-Contains $script "SupportsShouldProcess" "Kill-Wslcd.ps1 must support -WhatIf."
Assert-Contains $script "wslcd-desktop" "Kill-Wslcd.ps1 must target wslcd-desktop processes."
Assert-Contains $script "wslcd" "Kill-Wslcd.ps1 must retain legacy wslcd cleanup."
Assert-Contains $script "Get-CimInstance\s+Win32_Process" "Kill-Wslcd.ps1 must inspect command lines for dotnet-hosted wslcd.dll."
Assert-Contains $script "wslcd-desktop\.dll" "Kill-Wslcd.ps1 must detect development daemons started as dotnet wslcd-desktop.dll."
Assert-Contains $script "wslcd\.dll" "Kill-Wslcd.ps1 must detect development daemons started as dotnet wslcd.dll."
Assert-Contains $script "Stop-Process\s+-Id" "Kill-Wslcd.ps1 must stop processes by PID."
Assert-Contains $script "ProcessId" "Kill-Wslcd.ps1 must deduplicate targets by process ID."

powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath -WhatIf -Quiet
if ($LASTEXITCODE -ne 0) {
    throw "Kill-Wslcd.ps1 -WhatIf failed with exit code $LASTEXITCODE."
}

"KILL_WSLCD_SCRIPT_OK"
