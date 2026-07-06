param(
    [string]$Platform = "x64",
    [string]$Version = "0.1.0",
    [switch]$SkipSetup
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$ReleaseRid = if ($Platform.Equals("ARM64", [System.StringComparison]::OrdinalIgnoreCase)) { "win-arm64" } elseif ($Platform.Equals("x86", [System.StringComparison]::OrdinalIgnoreCase)) { "win-x86" } else { "win-x64" }
$ReleaseLayout = Join-Path $Root "bin\$Platform\Release\net10.0-windows10.0.26100.0\$ReleaseRid"
$DistRoot = Join-Path $Root "artifacts\dist"
$ReleaseRoot = Join-Path $Root "artifacts\release"
$DistDir = Join-Path $DistRoot "wslc-desktop-$Version-$ReleaseRid"
$PortableZip = Join-Path $ReleaseRoot "wslc-desktop-$Version-$ReleaseRid-portable.zip"
$SetupExe = Join-Path $ReleaseRoot "wslc-desktop-$Version-$ReleaseRid-setup.exe"

function Assert-UnderRoot {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPath = [System.IO.Path]::GetFullPath($Root)
    if (-not $fullPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside repository root: $fullPath"
    }
}

function Remove-DirectoryIfExists {
    param([string]$Path)

    Assert-UnderRoot $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Remove-FileIfExists {
    param([string]$Path)

    Assert-UnderRoot $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Write-Checksum {
    param([string]$Path)

    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $Path
    $checksumPath = "$Path.sha256"
    "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $Path)" |
        Set-Content -NoNewline -Encoding UTF8 -LiteralPath $checksumPath
    Write-Host "SHA256: $checksumPath"
}

function Remove-ReleasePayloadFiles {
    param(
        [string]$Directory,
        [string[]]$FileNames
    )

    foreach ($fileName in $FileNames) {
        Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter $fileName -ErrorAction SilentlyContinue |
            ForEach-Object {
                Remove-FileIfExists $_.FullName
                Write-Host "PRUNED_PAYLOAD=$($_.FullName)"
            }
    }
}

function Find-Iscc {
    if (-not [string]::IsNullOrWhiteSpace($env:ISCC_PATH) -and (Test-Path -LiteralPath $env:ISCC_PATH)) {
        return $env:ISCC_PATH
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    foreach ($candidate in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    throw "Inno Setup compiler ISCC.exe was not found. Install Inno Setup or pass -SkipSetup to build only portable.zip."
}

if (-not (Test-Path -LiteralPath $ReleaseLayout)) {
    throw "Release layout was not found: $ReleaseLayout"
}

foreach ($required in @(
    (Join-Path $ReleaseLayout "wslc-desktop.exe"),
    (Join-Path $ReleaseLayout "Microsoft.WindowsAppRuntime.dll"),
    (Join-Path $ReleaseLayout "Microsoft.UI.Xaml.dll"),
    (Join-Path $ReleaseLayout "Assets\AppIcon.ico"),
    (Join-Path $ReleaseLayout "wslcd\wslcd-desktop.exe"))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Release layout is missing required file: $required"
    }
}

New-Item -ItemType Directory -Force -Path $DistRoot, $ReleaseRoot | Out-Null
Remove-DirectoryIfExists $DistDir
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$excludedRootItems = @(
    "AppX",
    "artifacts",
    "docs.bak",
    "sources",
    ".agents",
    ".codex",
    ".git",
    ".github")

Get-ChildItem -LiteralPath $ReleaseLayout -Force |
    Where-Object { $excludedRootItems -notcontains $_.Name } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $DistDir -Recurse -Force
    }

Remove-ReleasePayloadFiles -Directory $DistDir -FileNames @(
    "onnxruntime.dll",
    "DirectML.dll",
    "Microsoft.ML.OnnxRuntime.dll",
    "Microsoft.Windows.AI.MachineLearning.dll",
    "Microsoft.Windows.AI.MachineLearning.Projection.dll")

New-Item -ItemType Directory -Force -Path (Join-Path $DistDir "bin") | Out-Null

foreach ($doc in @("README.md", "README.zh-CN.md")) {
    $docPath = Join-Path $Root $doc
    if (Test-Path -LiteralPath $docPath) {
        Copy-Item -LiteralPath $docPath -Destination (Join-Path $DistDir $doc) -Force
    }
}

Remove-FileIfExists $PortableZip
Remove-FileIfExists "$PortableZip.sha256"
Compress-Archive -Path (Join-Path $DistDir "*") -DestinationPath $PortableZip -Force
Write-Host "PORTABLE_ZIP=$PortableZip"
Write-Checksum $PortableZip

if ($SkipSetup) {
    Write-Host "SETUP_EXE_SKIPPED"
    Write-Host "RELEASE_ARTIFACTS_OK"
    return
}

$iscc = Find-Iscc
$archAllowed = if ($ReleaseRid.Equals("win-arm64", [System.StringComparison]::OrdinalIgnoreCase)) { "arm64" } else { "x64compatible" }
$setupBaseName = "wslc-desktop-$Version-$ReleaseRid-setup"
$issPath = Join-Path $Root "installer\wslc-desktop.iss"

Remove-FileIfExists $SetupExe
Remove-FileIfExists "$SetupExe.sha256"

$isccArgs = @(
    "/DAppVersion=$Version",
    "/DSourceDir=$DistDir",
    "/DOutputDir=$ReleaseRoot",
    "/DOutputBaseFilename=$setupBaseName",
    "/DInstallerArchitecturesAllowed=$archAllowed",
    "/DInstallerArchitecturesInstallIn64BitMode=$archAllowed",
    $issPath
)

& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe exited with code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $SetupExe)) {
    throw "Inno Setup did not produce $SetupExe."
}

Write-Host "SETUP_EXE=$SetupExe"
Write-Checksum $SetupExe
Write-Host "RELEASE_ARTIFACTS_OK"
