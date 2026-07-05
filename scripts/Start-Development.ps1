param(
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",

    [switch]$Detach
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

& (Join-Path $PSScriptRoot "Start-PackagedDebug.ps1") -Platform $Platform -Configuration Debug -Detach:$Detach
exit $LASTEXITCODE
