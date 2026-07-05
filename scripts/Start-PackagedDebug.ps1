param(
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$NoRestore,

    [switch]$Detach
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Stop-DevelopmentProcesses.ps1") -Quiet
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$RuntimeIdentifier = switch ($Platform) {
    "x86" { "win-x86" }
    "ARM64" { "win-arm64" }
    default { "win-x64" }
}

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    throw "The winapp CLI is required for packaged debug runs. Install Microsoft.WinAppCLI or run /winui-setup."
}

function Find-PackageManifest {
    param(
        [string]$RootPath,
        [switch]$IncludeAppXCache
    )

    if (-not (Test-Path $RootPath)) {
        return $null
    }

    $manifests = Get-ChildItem -Path $RootPath -Recurse -Filter "AppxManifest.xml" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*$RuntimeIdentifier*" }

    if (-not $IncludeAppXCache) {
        $manifests = $manifests | Where-Object { $_.FullName -notmatch "\\AppX\\" }
    }

    return $manifests |
        Sort-Object @{ Expression = "LastWriteTime"; Descending = $true } |
        Select-Object -First 1
}

$buildArgs = @(
    "build",
    ".\wslc-desktop.csproj",
    "-p:Platform=$Platform",
    "-p:Configuration=$Configuration",
    "-p:RuntimeIdentifier=$RuntimeIdentifier"
)

if ($NoRestore) {
    $buildArgs += "--no-restore"
}

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$preferredOutputRoot = Join-Path $Root "bin\$Platform\$Configuration"
$fallbackOutputRoot = Join-Path $Root "bin\$Configuration"

$manifest = Find-PackageManifest -RootPath $preferredOutputRoot
if ($null -eq $manifest) {
    $manifest = Find-PackageManifest -RootPath $fallbackOutputRoot
}

if ($null -eq $manifest) {
    $manifest = Find-PackageManifest -RootPath $preferredOutputRoot -IncludeAppXCache
}

if ($null -eq $manifest) {
    $manifest = Find-PackageManifest -RootPath $fallbackOutputRoot -IncludeAppXCache
}

if ($null -eq $manifest) {
    throw "Could not find AppxManifest.xml for $Configuration/$Platform/$RuntimeIdentifier."
}

$packageRoot = Split-Path -Parent $manifest.FullName

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Sign-DevelopmentOutput.ps1") -Path $packageRoot
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# Equivalent command shape: winapp run <packageRoot> --manifest <AppxManifest.xml> --debug-output
$runArgs = @("run", $packageRoot, "--manifest", $manifest.FullName)

if ($Detach) {
    $runArgs += "--detach"
}
else {
    $runArgs += "--debug-output"
}

& winapp @runArgs
exit $LASTEXITCODE
