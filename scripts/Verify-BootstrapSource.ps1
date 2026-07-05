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

$models = Read-ProjectFile "Models\WslcDomainModels.cs"
$app = Read-ProjectFile "App.xaml.cs"
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

foreach ($needle in @("WslcPrerequisiteState", "WslcPrerequisiteStatus", "DockerCliStatus", "DockerStaticRelease", "CliToolInstallResult")) {
    Require-Contains $models ([regex]::Escape($needle)) "Models must expose $needle."
}

foreach ($needle in @("RunStartupBootstrapAsync", "EnsureWslcPrerequisiteAsync", "ShowWslcRequiredDialogAsync", "MaybeShowDockerCliDialogAsync", "StartDaemonOnLaunchAsync", "CopyTextToClipboard")) {
    Require-Contains $app ([regex]::Escape($needle)) "App startup must contain $needle."
}
Require-Contains $app 'EnsureWslcPrerequisiteAsync[\s\S]*StartDaemonOnLaunchAsync' "App must gate daemon startup behind the wslc prerequisite check."

foreach ($needle in @("Bootstrap", "CliTools", "EnvironmentBootstrapService", "CliToolInstallationService", "ProcessCommandProbe", "CliToolPathResolver")) {
    Require-Contains $appServices ([regex]::Escape($needle)) "AppServices must wire $needle."
}
Require-Contains $appServices "DockerContext" "AppServices must wire the Docker context service."

foreach ($needle in @("CheckWslcAsync", "CheckDockerCliAsync", "AddToolBinToProcessPath", "wsl.exe", "wslc.exe", "docker.exe")) {
    Require-Contains $bootstrap ([regex]::Escape($needle)) "Environment bootstrap service must contain $needle."
}

foreach ($needle in @("wsl --install", "wsl --update", "WslUpdateRequired", "MissingWsl")) {
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

foreach ($needle in @("WslcRequiredDialogTitle", "DockerCliOptionalDialogTitle", "CliToolsWslcMissing", "CliToolsPathAdded", "CliToolsSystemPathAdded", "CliToolsDialogTitle", "DockerContextDefaultCreated", "DockerContextDefaultConfirmTitle")) {
    Require-Contains $localizer ([regex]::Escape($needle)) "AppStringLocalizer must contain $needle."
}

foreach ($needle in @("Missing WSL must recommend wsl --install", "Old WSL must recommend wsl --update", "Docker CLI installer must not copy dockerd.exe", "Compose installer must install Docker CLI plugin")) {
    Require-Contains $bootstrapVerify ([regex]::Escape($needle)) "BootstrapVerify must assert $needle."
}

"BOOTSTRAP_SOURCE_OK"
