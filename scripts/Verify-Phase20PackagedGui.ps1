param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Output = Join-Path $Root "bin\x64\Release\net10.0-windows10.0.26100.0\win-x64"
$Manifest = Join-Path $Output "AppxManifest.xml"
$DaemonDir = Join-Path $Output "AppX\wslcd"
$DaemonExe = Join-Path $DaemonDir "wslcd-desktop.exe"
$ScreenshotDir = Join-Path $Root "artifacts\phase20-packaged-ui"
$ContainersScreenshot = Join-Path $ScreenshotDir "phase20-containers.png"
$ImagesScreenshot = Join-Path $ScreenshotDir "phase20-images.png"

if (-not (Test-Path $Manifest)) {
    throw "Release x64 WinUI output was not found. Run dotnet build wslc-desktop.csproj -c Release -p:Platform=x64 --no-restore first."
}

foreach ($required in @("wslcd-desktop.exe", "wslcd-desktop.dll", "WslcDesktop.Runtime.dll", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll")) {
    $path = Join-Path $DaemonDir $required
    if (-not (Test-Path $path)) {
        throw "Packaged layout is missing daemon dependency: $required"
    }
}

New-Item -ItemType Directory -Force -Path $ScreenshotDir | Out-Null

$VerifierProject = Join-Path $Root "tools\WslcdSmokeVerify\WslcdSmokeVerify.csproj"
$VerifierDll = Join-Path $Root "tools\WslcdSmokeVerify\bin\Debug\net10.0\WslcdSmokeVerify.dll"
dotnet build $VerifierProject --no-restore | Out-Host
$daemonSmokeOutput = dotnet $VerifierDll package $DaemonExe 2>&1
$daemonSmokeOutput | Out-Host
$daemonSmokeText = $daemonSmokeOutput -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0 -or ($daemonSmokeText -notmatch "WSLCD_PACKAGED_DAEMON_OK")) {
    throw "Packaged daemon smoke failed."
}

$existingDaemonIds = @(Get-Process -Name wslcd-desktop,wslcd -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$newDaemonIds = @()
$launchJson = winapp run $Output --manifest $Manifest --detach --json | ConvertFrom-Json
$appPid = [int]$launchJson.ProcessId

try {
    Start-Sleep -Seconds 3
    $process = Get-Process -Id $appPid -ErrorAction Stop
    if (-not $process.Responding) {
        throw "wslc-desktop launched but is not responding."
    }

    winapp ui wait-for "NavContainers" -a $appPid -t 10000 | Out-Null
    winapp ui invoke "NavContainers" -a $appPid | Out-Null
    winapp ui wait-for "TxtContainerSearch" -a $appPid -t 10000 | Out-Null
    winapp ui wait-for "ContainerList" -a $appPid -t 10000 | Out-Null
    winapp ui invoke "BtnRefreshContainers" -a $appPid | Out-Null
    Start-Sleep -Seconds 12
    winapp ui screenshot -a $appPid -o $ContainersScreenshot | Out-Null

    $newDaemonIds = @(Get-Process -Name wslcd-desktop,wslcd -ErrorAction SilentlyContinue |
        Where-Object { $existingDaemonIds -notcontains $_.Id } |
        Select-Object -ExpandProperty Id)
    if ($existingDaemonIds.Count -eq 0 -and $newDaemonIds.Count -eq 0) {
        throw "Packaged app did not start a wslcd-desktop.exe process."
    }

    winapp ui invoke "NavImages" -a $appPid | Out-Null
    winapp ui wait-for "TxtImagesSearch" -a $appPid -t 10000 | Out-Null
    winapp ui wait-for "LstImages" -a $appPid -t 10000 | Out-Null
    winapp ui invoke "BtnImagesRefresh" -a $appPid | Out-Null
    Start-Sleep -Seconds 12

    foreach ($query in @("镜像加载失败", "Image load failed", "容器加载失败", "Container load failed", "wslcd readiness check failed", "wslcd unavailable")) {
        $searchOutput = winapp ui search $query -a $appPid --json 2>$null
        if ($searchOutput -and $searchOutput -match [regex]::Escape($query)) {
            throw "Packaged GUI surfaced an error state matching '$query'."
        }
    }

    winapp ui screenshot -a $appPid -o $ImagesScreenshot | Out-Null

    Write-Host "PHASE20_PACKAGED_GUI_OK"
}
finally {
    Stop-Process -Id $appPid -Force -ErrorAction SilentlyContinue
    foreach ($daemonId in $newDaemonIds) {
        Stop-Process -Id $daemonId -Force -ErrorAction SilentlyContinue
    }
}
