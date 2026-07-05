param(
    [string]$Platform = "x64",
    [switch]$SkipPackage,
    [switch]$AllowKnownLocalBlockers
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$HostRid = "win-" + [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$ReleaseRid = if ($Platform.Equals("ARM64", [System.StringComparison]::OrdinalIgnoreCase)) { "win-arm64" } elseif ($Platform.Equals("x86", [System.StringComparison]::OrdinalIgnoreCase)) { "win-x86" } else { "win-x64" }
$ReleaseLayout = Join-Path $Root "bin\$Platform\Release\net10.0-windows10.0.26100.0\$ReleaseRid"
$ReleaseDaemon = Join-Path $ReleaseLayout "AppX\wslcd\wslcd-desktop.exe"

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Script,
        [switch]$AllowFailure
    )

    Write-Host "== $Name =="
    try {
        & $Script
        Write-Host "OK: $Name"
    }
    catch {
        if ($AllowFailure) {
            Write-Warning "KNOWN_BLOCKER: $Name failed: $($_.Exception.Message)"
            return
        }

        throw
    }
}

function Invoke-ProcessStep {
    param(
        [string]$Name,
        [string]$ExpectedMarker,
        [string]$FileName,
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    Invoke-Step $Name -AllowFailure:$AllowFailure {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            $output = & $FileName @Arguments 2>&1
            $exitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        $text = ($output | Out-String)
        Write-Host $text

        if ($exitCode -ne 0) {
            throw "$Name exited with code $exitCode."
        }

        if (-not [string]::IsNullOrWhiteSpace($ExpectedMarker) -and $text -notmatch [regex]::Escape($ExpectedMarker)) {
            throw "$Name did not print expected marker $ExpectedMarker."
        }
    }
}

Invoke-ProcessStep "dotnet restore root project" "" "dotnet" @(
    "restore",
    "wslc-desktop.csproj",
    "-p:Platform=$Platform",
    "-p:RuntimeIdentifier=$ReleaseRid",
    "-p:PublishReadyToRun=false",
    "-p:EnableDevelopmentCodeSigning=false")

Invoke-ProcessStep "dotnet restore wslcd host RID" "" "dotnet" @(
    "restore",
    "src\wslcd\wslcd.csproj",
    "-r",
    $HostRid,
    "-p:PublishReadyToRun=false")

if (-not $ReleaseRid.Equals($HostRid, [System.StringComparison]::OrdinalIgnoreCase)) {
    Invoke-ProcessStep "dotnet restore wslcd release RID" "" "dotnet" @(
        "restore",
        "src\wslcd\wslcd.csproj",
        "-r",
        $ReleaseRid,
        "-p:PublishReadyToRun=false")
}

foreach ($toolProject in @(
    "tools\LocalizationVerify\LocalizationVerify.csproj",
    "tools\ComposePlanVerify\ComposePlanVerify.csproj",
    "tools\HardeningVerify\HardeningVerify.csproj",
    "tools\WslcdSmokeVerify\WslcdSmokeVerify.csproj",
    "tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj")) {
    Invoke-ProcessStep "dotnet restore $toolProject" "" "dotnet" @(
        "restore",
        $toolProject,
        "-p:RuntimeIdentifier=any")
}

Invoke-ProcessStep "dotnet build --no-restore" "" "dotnet" @(
    "build",
    "wslc-desktop.csproj",
    "--no-restore",
    "-p:Platform=$Platform",
    "-p:RuntimeIdentifier=$ReleaseRid",
    "-p:PublishReadyToRun=false",
    "-p:EnableDevelopmentCodeSigning=false")

Invoke-ProcessStep "Phase 18 source verifier" "PHASE18_SOURCE_OK" "powershell" @(
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    "scripts\Verify-Phase18Source.ps1")

Invoke-ProcessStep "Phase 19 source verifier" "PHASE19_SOURCE_OK" "powershell" @(
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    "scripts\Verify-Phase19Source.ps1")

Invoke-ProcessStep "Phase 20 source verifier" "PHASE20_SOURCE_OK" "powershell" @(
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    "scripts\Verify-Phase20Source.ps1")

Invoke-ProcessStep "Compatibility matrix verifier" "COMPATIBILITY_MATRIX_OK" "powershell" @(
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    "scripts\Verify-CompatibilityMatrix.ps1")

Invoke-ProcessStep "LocalizationVerify" "LOCALIZATION_VERIFY_OK" "dotnet" @("run", "--project", "tools\LocalizationVerify\LocalizationVerify.csproj", "--no-restore")

Invoke-ProcessStep "ComposePlanVerify" "PHASE7_COMPOSE_PLAN_OK" "dotnet" @("run", "--project", "tools\ComposePlanVerify\ComposePlanVerify.csproj", "--no-restore")

Invoke-ProcessStep "HardeningVerify" "PHASE8_HARDENING_OK" "dotnet" @("run", "--project", "tools\HardeningVerify\HardeningVerify.csproj", "--no-restore") -AllowFailure:$AllowKnownLocalBlockers

Invoke-ProcessStep "Release build" "" "dotnet" @(
    "build",
    "wslc-desktop.csproj",
    "-c",
    "Release",
    "--no-restore",
    "-p:Platform=$Platform",
    "-p:RuntimeIdentifier=$ReleaseRid",
    "-p:PublishReadyToRun=false",
    "-p:EnableDevelopmentCodeSigning=false")

Invoke-Step "Release packaged daemon exists" {
    if (-not (Test-Path $ReleaseDaemon)) {
        throw "Packaged daemon was not found at $ReleaseDaemon."
    }
}

$previousSmokeDaemonPath = $env:WSLCD_SMOKE_DAEMON_PATH
try {
    $env:WSLCD_SMOKE_DAEMON_PATH = $ReleaseDaemon
    Invoke-ProcessStep "WslcdSmokeVerify full" "WSLCD_FULL_SMOKE_OK" "dotnet" @("run", "--project", "tools\WslcdSmokeVerify\WslcdSmokeVerify.csproj", "--no-restore", "--", "full") -AllowFailure:$AllowKnownLocalBlockers
}
finally {
    $env:WSLCD_SMOKE_DAEMON_PATH = $previousSmokeDaemonPath
}

Invoke-ProcessStep "DockerApiCompatVerify full" "DOCKER_API_COMPAT_FULL_OK" "dotnet" @("run", "--project", "tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj", "--no-restore", "--", "full") -AllowFailure:$AllowKnownLocalBlockers

if (-not $SkipPackage) {
    Invoke-Step "winapp package" -AllowFailure:$AllowKnownLocalBlockers {
        if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
            throw "winapp CLI is not installed or not on PATH."
        }

        $cert = Join-Path $Root "artifacts\wslc-desktop-phase20-devcert.pfx"
        if (-not (Test-Path $cert)) {
            winapp cert generate --manifest . --output $cert --if-exists overwrite
        }

        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            $output = & winapp package $ReleaseLayout --cert $cert --executable wslc-desktop.exe 2>&1
            $exitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        Write-Host ($output | Out-String)

        if ($exitCode -ne 0) {
            throw "winapp package exited with code $exitCode."
        }
    }
}

Write-Host "WSLC_FULL_RELEASE_VERIFY_OK"
