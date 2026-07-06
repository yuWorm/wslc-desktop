$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

function Read-ProjectFile {
    param([string]$RelativePath)

    $path = Join-Path $root $RelativePath
    if (-not (Test-Path $path)) {
        throw "Missing $RelativePath"
    }

    return Get-Content -Raw $path
}

function Require-Contains {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

function Require-NotContains {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -match $Pattern) {
        throw $Message
    }
}

$models = Read-ProjectFile "Models\WslcDomainModels.cs"
$app = Read-ProjectFile "App.xaml.cs"
$mainWindowCode = Read-ProjectFile "MainWindow.xaml.cs"
$mainWindowXaml = Read-ProjectFile "MainWindow.xaml"
$appServices = Read-ProjectFile "Services\AppServices.cs"
$bootstrap = Read-ProjectFile "Services\EnvironmentBootstrapService.cs"
$evaluator = Read-ProjectFile "Services\BootstrapPrerequisiteEvaluator.cs"
$installer = Read-ProjectFile "Services\CliToolInstallationService.cs"
$archiveInstaller = Read-ProjectFile "Services\CliToolArchiveInstaller.cs"
$releaseIndex = Read-ProjectFile "Services\DockerStaticReleaseIndex.cs"
$pathEditor = Read-ProjectFile "Services\PathEnvironmentEditor.cs"
$settingsVm = Read-ProjectFile "ViewModels\SettingsViewModel.cs"
$settingsXaml = Read-ProjectFile "Pages\SettingsPage.xaml"
$settingsCodeBehind = Read-ProjectFile "Pages\SettingsPage.xaml.cs"
$en = Read-ProjectFile "Strings\en-US\Resources.resw"
$zh = Read-ProjectFile "Strings\zh-CN\Resources.resw"
$localizer = Read-ProjectFile "Services\AppStringLocalizer.cs"
$bootstrapVerify = Read-ProjectFile "tools\BootstrapVerify\Program.cs"

$mainWindowConstructor = [regex]::Match(
    $mainWindowCode,
    'public\s+MainWindow\(\)[\s\S]*?AppLaunchLogger\.Info\("MainWindow constructor completed\."\);\s*\}').Value
if ([string]::IsNullOrWhiteSpace($mainWindowConstructor)) {
    throw "Could not locate the MainWindow constructor for startup gate verification."
}

$mainWindowLoaded = [regex]::Match(
    $mainWindowCode,
    'private\s+(?:async\s+)?void\s+MainWindow_Loaded\([^\)]*\)[\s\S]*?\n    \}').Value
if ([string]::IsNullOrWhiteSpace($mainWindowLoaded)) {
    throw "Could not locate MainWindow_Loaded for startup gate verification."
}

$startupBootstrap = [regex]::Match(
    $app,
    'private\s+static\s+async\s+Task\s+RunStartupBootstrapAsync\(\)[\s\S]*?(?=\n    private\s+static\s+async\s+Task<bool>\s+EnsureWslcPrerequisiteAsync)').Value
if ([string]::IsNullOrWhiteSpace($startupBootstrap)) {
    throw "Could not locate RunStartupBootstrapAsync for startup gate verification."
}

foreach ($needle in @("WslcPrerequisiteState", "WslcPrerequisiteStatus", "DockerCliStatus", "DockerStaticRelease", "CliToolInstallResult")) {
    Require-Contains $models ([regex]::Escape($needle)) "Models must expose $needle."
}

foreach ($needle in @("RunStartupBootstrapAsync", "EnsureWslcPrerequisiteAsync", "ShowWslcRequiredDialogAsync", "MaybeShowDockerCliDialogAsync", "StartDaemonOnLaunchAsync", "CopyTextToClipboard")) {
    Require-Contains $app ([regex]::Escape($needle)) "App startup must contain $needle."
}
Require-Contains $startupBootstrap 'EnsureWslcPrerequisiteAsync[\s\S]*StartDaemonOnLaunchAsync' "App must gate daemon startup behind the wslc prerequisite check."
Require-Contains $startupBootstrap 'EnsureWslcPrerequisiteAsync[\s\S]*EnterApplicationShell' "App must only enter the navigable shell after the wslc prerequisite check succeeds."
Require-NotContains $startupBootstrap 'EnterApplicationShell[\s\S]*EnsureWslcPrerequisiteAsync' "App must not enter the navigable shell before checking the wslc prerequisite."
Require-NotContains $app 'CloseButtonText\s*=' "WSLC required dialog must not expose a close button."
Require-NotContains $app 'Application\.Current\.Exit\(\)' "WSLC required dialog must stay mandatory instead of exiting from App bootstrap."
foreach ($needle in @("WslcPrerequisiteInitialized", "MarkWslcPrerequisiteInitializedAsync", "CheckWslcWithStartupTimeoutAsync")) {
    Require-Contains $app ([regex]::Escape($needle)) "App startup must contain first-run prerequisite gate item $needle."
}

foreach ($needle in @("RuntimePrerequisitesReady", "EnterApplicationShell", "StartShellStatusPollingAsync")) {
    Require-Contains $mainWindowCode ([regex]::Escape($needle)) "MainWindow must contain the startup gate member $needle."
}
foreach ($needle in @("ShowStartupOverlay", "HideStartupOverlay")) {
    Require-Contains $mainWindowCode ([regex]::Escape($needle)) "MainWindow must expose $needle for startup prerequisite feedback."
}
Require-NotContains $mainWindowConstructor 'NavFrame\.Navigate' "MainWindow constructor must not navigate before WSLC is verified."
Require-NotContains $mainWindowLoaded 'RefreshShellStatusAsync' "MainWindow.Loaded must not refresh daemon status before WSLC is verified."
Require-Contains $mainWindowCode 'if\s*\(!RuntimePrerequisitesReady\)' "Navigation must ignore user selection while startup prerequisites are not ready."
Require-Contains $mainWindowXaml 'x:Name="NavView"[\s\S]*IsEnabled="False"' "NavigationView must remain disabled until startup prerequisites pass."
Require-Contains $mainWindowXaml 'x:Name="StartupOverlay"' "MainWindow must show a startup overlay while WSLC prerequisites are checked."
Require-Contains $mainWindowXaml 'ProgressRing[\s\S]*IsActive="True"' "Startup overlay must use an active WinUI ProgressRing."
Require-Contains $mainWindowXaml 'x:Uid="StartupOverlayTitle"' "Startup overlay title must be localized through x:Uid."
Require-Contains $mainWindowXaml 'x:Uid="StartupOverlayDescription"' "Startup overlay description must be localized through x:Uid."
Require-Contains $app 'ShowStartupOverlay[\s\S]*CheckWslcWithStartupTimeoutAsync' "App startup must show the startup overlay before checking WSLC."
Require-Contains $app 'GetWslcPrerequisiteMessage' "App startup must localize the WSLC required dialog message by prerequisite state."
Require-NotContains $app 'Content\s*=\s*CreateDialogContent\(status\.Message' "WSLC required dialog must not use raw evaluator English messages directly."

foreach ($needle in @("Bootstrap", "CliTools", "EnvironmentBootstrapService", "CliToolInstallationService", "ProcessCommandProbe", "CliToolPathResolver")) {
    Require-Contains $appServices ([regex]::Escape($needle)) "AppServices must wire $needle."
}
Require-Contains $appServices "DockerContext" "AppServices must wire the Docker context service."

foreach ($needle in @("CheckWslcAsync", "CheckDockerCliAsync", "AddToolBinToProcessPath", "wsl.exe", "wslc.exe", "docker.exe")) {
    Require-Contains $bootstrap ([regex]::Escape($needle)) "Environment bootstrap service must contain $needle."
}
foreach ($needle in @("TimeSpan.FromSeconds(5)", "CancelAfter", "OperationCanceledException", "CreateWslcCheckTimedOut")) {
    Require-Contains $bootstrap ([regex]::Escape($needle)) "WSLC prerequisite probing must enforce timeout item $needle."
}

foreach ($needle in @("wsl --install", "wsl --update", "WslUpdateRequired", "MissingWsl", "CheckTimedOut", "CreateWslcCheckTimedOut")) {
    Require-Contains $evaluator ([regex]::Escape($needle)) "WSLC prerequisite evaluator must contain $needle."
}

foreach ($needle in @("https://download.docker.com/win/static/stable/x86_64/", "https://api.github.com/repos/docker/compose/releases/latest", "InstallLatestDockerCliAsync", "InstallLatestComposeAsync", "AddBinToUserPath", "AddBinToMachinePathAsync", "runas")) {
    Require-Contains $installer ([regex]::Escape($needle)) "CLI tool installer must contain $needle."
}

foreach ($needle in @("InstallDockerCliFromZipAsync", "InstallComposeFromExeAsync", "docker.exe", "docker-compose.exe", "cli-plugins")) {
    Require-Contains $archiveInstaller ([regex]::Escape($needle)) "Archive installer must contain $needle."
}

foreach ($needle in @("FindLatestDockerZip", "GeneratedRegex", "docker-")) {
    Require-Contains $releaseIndex ([regex]::Escape($needle)) "Docker static release parser must contain $needle."
}

Require-Contains $pathEditor "AddPathSegment" "PATH editor must expose AddPathSegment."

foreach ($needle in @("RefreshCliToolsCommand", "InstallLatestDockerCliCommand", "InstallLatestComposeCommand", "AddCliToolsPathCommand", "AddCliToolsSystemPathCommand", "CreateDefaultDockerContextCommand", "InstallDockerCliFromZipAsync", "InstallComposeFromExeAsync", "CreateDefaultDockerContextAsync", "CliToolsStatus", "CliToolsBinDirectory")) {
    Require-Contains $settingsVm ([regex]::Escape($needle)) "SettingsViewModel must expose $needle."
}

foreach ($needle in @("SettingsCliToolsHeading", "BtnOpenCliToolsDialog", "BtnCreateDockerContextDefault")) {
    Require-Contains $settingsXaml ([regex]::Escape($needle)) "Settings XAML must contain $needle."
}
foreach ($needle in @("BtnInstallDockerCliLatest", "BtnInstallDockerCliZip", "BtnInstallComposeLatest", "BtnInstallComposeExe", "BtnAddCliToolsPath", "BtnAddCliToolsSystemPath")) {
    if ($settingsXaml -match [regex]::Escape($needle)) {
        throw "Settings XAML should keep CLI install actions in the reusable dialog instead of exposing $needle directly."
    }
}

foreach ($needle in @("OpenCliToolsDialog_Click", "CreateDockerContextDefault_Click", "ConfirmDockerContextDefaultAsync", "CliToolsInstallDialog")) {
    Require-Contains $settingsCodeBehind ([regex]::Escape($needle)) "Settings code-behind must contain $needle."
}

foreach ($needle in @("SettingsCliToolsHeading.Text", "SettingsCliToolsStatusInfo.Title", "SettingsCliToolsManageText.Text", "SettingsCliToolsDockerContextText.Text")) {
    Require-Contains $en ([regex]::Escape($needle)) "English resources must contain $needle."
    Require-Contains $zh ([regex]::Escape($needle)) "Chinese resources must contain $needle."
}

foreach ($needle in @("WslcRequiredDialogTitle", "WslcRequiredMissingWslMessage", "WslcRequiredUpdateRequiredMessage", "WslcRequiredUpdateRequiredWithVersion", "WslcRequiredTimedOutMessage", "WslcRequiredCommandHeader", "DockerCliOptionalDialogTitle", "CliToolsWslcMissing", "CliToolsPathAdded", "CliToolsSystemPathAdded", "CliToolsDialogTitle", "DockerContextDefaultCreated", "DockerContextDefaultConfirmTitle")) {
    Require-Contains $localizer ([regex]::Escape($needle)) "AppStringLocalizer must contain $needle."
}
foreach ($needle in @("StartupOverlayTitle.Text", "StartupOverlayDescription.Text", "WslcRequiredDialogTitle", "WslcRequiredMissingWslMessage", "WslcRequiredUpdateRequiredMessage", "WslcRequiredUpdateRequiredWithVersion", "WslcRequiredTimedOutMessage", "WslcRequiredCommandHeader", "Recheck", "CopyCommand")) {
    Require-Contains $en ([regex]::Escape($needle)) "English resources must contain $needle."
    Require-Contains $zh ([regex]::Escape($needle)) "Chinese resources must contain $needle."
}

foreach ($needle in @("English localizer must include WSLC required missing-WSL dialog content", "Chinese localizer must include WSLC required missing-WSL dialog content", "Missing WSL must list update and install commands one per line", "Old WSL must list update and install commands one per line", "Timed-out WSLC checks must list update and install commands one per line", "Default settings must force the first-run WSLC gate", "Settings must persist the successful WSLC initialization marker", "Docker CLI installer must not copy dockerd.exe", "Compose installer must install Docker CLI plugin")) {
    Require-Contains $bootstrapVerify ([regex]::Escape($needle)) "BootstrapVerify must assert $needle."
}

"BOOTSTRAP_SOURCE_OK"
