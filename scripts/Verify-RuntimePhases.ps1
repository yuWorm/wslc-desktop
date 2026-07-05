param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipBuild) {
    dotnet build --no-restore
}

dotnet run --project tools\CliParseVerify\CliParseVerify.csproj --no-restore
dotnet run --project tools\ComposePlanVerify\ComposePlanVerify.csproj
dotnet run --project tools\LocalizationVerify\LocalizationVerify.csproj

Write-Host "CLI_RUNTIME_VERIFICATION_OK"
