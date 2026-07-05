param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Output = Join-Path $Root "bin\Debug\net10.0-windows10.0.26100.0\win-x64"
$Manifest = Join-Path $Output "AppxManifest.xml"
$ScreenshotDir = Join-Path $Root "artifacts\phase18-ui"
$ContainersScreenshot = Join-Path $ScreenshotDir "phase18-containers.png"
$ImagesScreenshot = Join-Path $ScreenshotDir "phase18-images.png"

if (-not (Test-Path $Manifest)) {
    throw "Debug WinUI output was not found. Run dotnet build --no-restore first."
}

New-Item -ItemType Directory -Force -Path $ScreenshotDir | Out-Null

$launchJson = winapp run $Output --manifest $Manifest --detach --json | ConvertFrom-Json
$appPid = [int]$launchJson.ProcessId

try {
    Start-Sleep -Seconds 2
    $process = Get-Process -Id $appPid -ErrorAction Stop
    if (-not $process.Responding) {
        throw "wslc-desktop launched but is not responding."
    }

    winapp ui wait-for "NavContainers" -a $appPid -t 5000 | Out-Null
    winapp ui invoke "NavContainers" -a $appPid | Out-Null
    winapp ui wait-for "TxtContainerSearch" -a $appPid -t 10000 | Out-Null
    winapp ui wait-for "ContainerList" -a $appPid -t 10000 | Out-Null
    winapp ui invoke "BtnRefreshContainers" -a $appPid | Out-Null
    Start-Sleep -Seconds 12
    winapp ui screenshot -a $appPid -o $ContainersScreenshot | Out-Null

    winapp ui invoke "NavImages" -a $appPid | Out-Null
    winapp ui wait-for "TxtImagesSearch" -a $appPid -t 10000 | Out-Null
    winapp ui wait-for "LstImages" -a $appPid -t 10000 | Out-Null
    winapp ui invoke "BtnImagesRefresh" -a $appPid | Out-Null
    Start-Sleep -Seconds 12

    foreach ($query in @("镜像加载失败", "Image load failed", "容器加载失败", "Container load failed")) {
        $searchOutput = winapp ui search $query -a $appPid --json 2>$null
        if ($searchOutput -and $searchOutput -match [regex]::Escape($query)) {
            throw "GUI surfaced an error state matching '$query'."
        }
    }

    winapp ui screenshot -a $appPid -o $ImagesScreenshot | Out-Null

    Write-Host "PHASE18_GUI_SMOKE_OK"
}
finally {
    Stop-Process -Id $appPid -Force -ErrorAction SilentlyContinue
}
