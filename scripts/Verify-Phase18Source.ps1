param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ClientProjectPath = Join-Path $Root "src\WslcDesktop.DaemonClient\WslcDesktop.DaemonClient.csproj"
$ClientPath = Join-Path $Root "src\WslcDesktop.DaemonClient\WslcdNativeClient.cs"
$ProcessManagerPath = Join-Path $Root "Services\DaemonProcessManager.cs"
$AdapterPath = Join-Path $Root "Services\DaemonBackedWslcServices.cs"
$AppServicesPath = Join-Path $Root "Services\AppServices.cs"
$AppProjectPath = Join-Path $Root "wslc-desktop.csproj"
$DaemonPath = Join-Path $Root "src\wslcd\Program.cs"
$ImagesViewModelPath = Join-Path $Root "ViewModels\ImagesViewModel.cs"
$ContainersViewModelPath = Join-Path $Root "ViewModels\ContainersViewModel.cs"
$DockerVerifierPath = Join-Path $Root "tools\DockerApiCompatVerify\Program.cs"

function Assert-Path {
    param([string]$Path, [string]$Message)
    if (-not (Test-Path $Path)) {
        throw $Message
    }
}

function Assert-NotPath {
    param([string]$Path, [string]$Message)
    if (Test-Path $Path) {
        throw $Message
    }
}

function Assert-Contains {
    param([string]$Text, [string]$Pattern, [string]$Message)
    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

function Assert-NotContains {
    param([string]$Text, [string]$Pattern, [string]$Message)
    if ($Text -match $Pattern) {
        throw $Message
    }
}

Assert-Path $ClientProjectPath "Phase 18 must add src\WslcDesktop.DaemonClient."
Assert-Path $ClientPath "Phase 18 must add WslcdNativeClient."
Assert-Path $ProcessManagerPath "Phase 18 must add daemon startup orchestration."
Assert-Path $AdapterPath "Phase 18 must add WinUI daemon-backed service adapters."

$clientProject = Get-Content -Raw $ClientProjectPath
$client = Get-Content -Raw $ClientPath
$processManager = Get-Content -Raw $ProcessManagerPath
$adapters = Get-Content -Raw $AdapterPath
$appServices = Get-Content -Raw $AppServicesPath
$appProject = Get-Content -Raw $AppProjectPath
$daemon = Get-Content -Raw $DaemonPath
$imagesViewModel = Get-Content -Raw $ImagesViewModelPath
$containersViewModel = Get-Content -Raw $ContainersViewModelPath
$dockerVerifier = Get-Content -Raw $DockerVerifierPath

Assert-Contains $clientProject "WslcDesktop.Contracts.csproj" "Daemon client project must reference shared contracts."
Assert-Contains $appProject "WslcDesktop.DaemonClient.csproj" "WinUI app must reference the daemon client project."
Assert-Contains $appProject "WslcDesktop.Contracts.csproj" "WinUI app must reference shared contracts for adapter mappings."

Assert-Contains $client "SocketsHttpHandler" "Daemon client must use SocketsHttpHandler."
Assert-Contains $client "ConnectCallback" "Daemon client must connect HTTP through a named-pipe callback."
Assert-Contains $client "NamedPipeClientStream" "Daemon client must use Windows named pipes."
Assert-Contains $client "WslcdDefaults.NativePipeName" "Daemon client must default to the product-owned native pipe."

foreach ($method in @(
    "GetHealthAsync",
    "GetVersionAsync",
    "GetRuntimeAsync",
    "ListContainersAsync",
    "InspectContainerAsync",
    "CreateContainerAsync",
    "StartContainerAsync",
    "StopContainerAsync",
    "RemoveContainerAsync",
    "ListImagesAsync",
    "PullImageAsync",
    "RemoveImageAsync",
    "ListVolumesAsync",
    "CreateVolumeAsync",
    "RemoveVolumeAsync",
    "ListNetworksAsync",
    "StreamLogsAsync",
    "CreateExecAsync",
    "StartExecAsync",
    "InspectExecAsync",
    "ListOperationsAsync",
    "GetDiagnosticsAsync"
)) {
    Assert-Contains $client "\b$method\b" "Daemon client must expose $method."
}

foreach ($route in @(
    '"/v1/volumes"',
    '"/v1/volumes/{name}"',
    '"/v1/containers/{id}/logs"',
    '"/v1/containers/{id}/exec"',
    '"/v1/exec/{id}/start"',
    '"/v1/exec/{id}"',
    '"/v1/diagnostics"'
)) {
    $pattern = [regex]::Escape($route)
    Assert-Contains $daemon $pattern "Native daemon API must map $route."
}

Assert-Contains $processManager "wslcd-desktop.dll" "DaemonProcessManager must support development wslcd-desktop.dll startup."
Assert-Contains $processManager "wslcd-desktop.exe" "DaemonProcessManager must support published wslcd-desktop.exe startup."
Assert-Contains $processManager "wslcd.dll" "DaemonProcessManager must retain legacy development wslcd.dll cleanup/fallback."
Assert-Contains $processManager "wslcd.exe" "DaemonProcessManager must retain legacy published wslcd.exe cleanup/fallback."
Assert-Contains $processManager "EnsureRunningAsync" "DaemonProcessManager must expose EnsureRunningAsync."
Assert-Contains $processManager "FindDaemonTargets" "DaemonProcessManager must enumerate daemon startup candidates instead of choosing only the first existing path."
Assert-Contains $processManager "foreach\s*\(\s*var\s+target\s+in\s+FindDaemonTargets\(\)\s*\)" "DaemonProcessManager must try all daemon startup candidates."
Assert-Contains $processManager "Win32Exception" "DaemonProcessManager must catch Windows App Control and process startup failures."
Assert-Contains $processManager "lastError" "DaemonProcessManager must preserve the final startup failure when all candidates fail."

foreach ($adapter in @(
    "DaemonWslcHostService",
    "DaemonWslcImageService",
    "DaemonWslcContainerService",
    "DaemonWslcProcessService",
    "DaemonWslcVolumeService",
    "DaemonWslcNetworkService",
    "DaemonOperationTracker"
)) {
    Assert-Contains $adapters "\b$adapter\b" "WinUI must include $adapter."
    Assert-Contains $appServices "\b$adapter\b" "AppServices must wire $adapter."
}

Assert-Contains $appServices "DaemonProcessManager" "AppServices must wire daemon startup orchestration."
Assert-Contains $appServices "WslcdNativeClient" "AppServices must use the typed daemon client."
Assert-NotContains $appServices "new\s+WslcCommandService" "AppServices must not wire direct CLI command execution."
Assert-NotContains $appServices "new\s+WslcHostService" "AppServices must not wire the direct CLI host service."
Assert-NotContains $appServices "new\s+WslcImageService" "AppServices must not wire the direct CLI image service."
Assert-NotContains $appServices "new\s+WslcContainerService" "AppServices must not wire the direct CLI container service."
Assert-NotContains $appServices "new\s+WslcProcessService" "AppServices must not wire the direct CLI process service."
Assert-NotContains $appServices "new\s+WslcVolumeService" "AppServices must not wire the direct CLI volume service."
Assert-NotContains $appServices "new\s+WslcNetworkService" "AppServices must not wire the direct CLI network service."

foreach ($removedService in @(
    "Services\WslcCommandService.cs",
    "Services\WslcHostService.cs",
    "Services\WslcImageService.cs",
    "Services\WslcContainerService.cs",
    "Services\WslcProcessService.cs",
    "Services\WslcVolumeService.cs",
    "Services\WslcNetworkService.cs",
    "Services\WslcCliCommandLine.cs"
)) {
    Assert-NotPath (Join-Path $Root $removedService) "Phase 18 must remove direct WinUI shell-out service file $removedService."
}

Assert-Contains $imagesViewModel "SemaphoreSlim" "ImagesViewModel must serialize LoadAsync calls so refresh cannot duplicate rows during an in-flight daemon load."
Assert-Contains $imagesViewModel "WaitAsync" "ImagesViewModel LoadAsync must wait on the load semaphore."
Assert-Contains $imagesViewModel "Release\(\)" "ImagesViewModel LoadAsync must release the load semaphore."
Assert-Contains $containersViewModel "SemaphoreSlim" "ContainersViewModel must serialize LoadAsync calls so refresh cannot duplicate rows during an in-flight daemon load."
Assert-Contains $containersViewModel "WaitAsync" "ContainersViewModel LoadAsync must wait on the load semaphore."
Assert-Contains $containersViewModel "Release\(\)" "ContainersViewModel LoadAsync must release the load semaphore."

Assert-Contains $dockerVerifier 'mode\.Equals\("gui-lifecycle"' "DockerApiCompatVerify must expose a Phase 18 GUI/native lifecycle reflection mode."
Assert-Contains $dockerVerifier "VerifyGuiLifecycleAsync" "DockerApiCompatVerify must verify native daemon lifecycle reflection through the Docker pipe."
Assert-Contains $dockerVerifier 'NativePipe' "Phase 18 lifecycle verifier must call the native daemon pipe used by the GUI."
Assert-Contains $dockerVerifier 'DockerPipe' "Phase 18 lifecycle verifier must observe lifecycle state through the Docker pipe."
Assert-Contains $dockerVerifier '"/v1/containers/' "Phase 18 lifecycle verifier must call native container lifecycle endpoints."
Assert-Contains $dockerVerifier '"/v1.54/containers/json' "Phase 18 lifecycle verifier must inspect Docker container state after native lifecycle actions."
Assert-Contains $dockerVerifier 'PHASE18_GUI_LIFECYCLE_OK' "Phase 18 lifecycle verifier must print PHASE18_GUI_LIFECYCLE_OK."

Write-Host "PHASE18_SOURCE_OK"
