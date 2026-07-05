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

$defaults = Read-ProjectFile "src\WslcDesktop.Contracts\WslcdDefaults.cs"
$daemonProject = Read-ProjectFile "src\wslcd\wslcd.csproj"
$appProject = Read-ProjectFile "wslc-desktop.csproj"
$manager = Read-ProjectFile "Services\DaemonProcessManager.cs"
$interfaces = Read-ProjectFile "Services\IWslcServices.cs"
$settingsVm = Read-ProjectFile "ViewModels\SettingsViewModel.cs"
$settingsXaml = Read-ProjectFile "Pages\SettingsPage.xaml"
$settingsCodeBehind = Read-ProjectFile "Pages\SettingsPage.xaml.cs"
$app = Read-ProjectFile "App.xaml.cs"
$mainWindow = Read-ProjectFile "MainWindow.xaml"
$mainWindowCode = Read-ProjectFile "MainWindow.xaml.cs"
$diagnostics = Read-ProjectFile "Services\DaemonDiagnosticsService.cs"
$killScript = Read-ProjectFile "scripts\Kill-Wslcd.ps1"
$killVerifier = Read-ProjectFile "scripts\Verify-KillWslcdScript.ps1"
$en = Read-ProjectFile "Strings\en-US\Resources.resw"
$zh = Read-ProjectFile "Strings\zh-CN\Resources.resw"
$localizer = Read-ProjectFile "Services\AppStringLocalizer.cs"

Require-Contains $defaults 'DaemonName\s*=\s*"wslcd-desktop"' "WslcdDefaults.DaemonName must be wslcd-desktop."
Require-Contains $daemonProject '<AssemblyName>wslcd-desktop</AssemblyName>' "Daemon project must emit wslcd-desktop assembly name."
Require-Contains $appProject 'wslcd-desktop\.exe' "App project must package wslcd-desktop.exe."
Require-Contains $appProject 'wslcd-desktop\.dll' "App project must package wslcd-desktop.dll."
Require-Contains $appProject 'H\.NotifyIcon\.WinUI' "App project must reference H.NotifyIcon.WinUI for tray support."

foreach ($needle in @("wslcd-desktop.exe", "wslcd-desktop.dll", "wslcd.exe", "wslcd.dll", "StopAllAsync", "EnumerateDaemonProcesses", "DaemonStopResult")) {
    Require-Contains $manager ([regex]::Escape($needle)) "DaemonProcessManager must contain $needle."
}

foreach ($needle in @("StartAsync", "RestartAsync", "StopAllAsync", "DaemonStopResult")) {
    Require-Contains $interfaces ([regex]::Escape($needle)) "IDaemonControlService contract must contain $needle."
}

foreach ($needle in @("StartDaemonCommand", "RestartDaemonCommand", "StopDaemonCommand", "DaemonStarted", "DaemonStopped", "CanStartDaemon", "CanRestartDaemon", "CanStopDaemon", "LastSaveRequiresDaemonRestart", "SettingsRestartImpact")) {
    Require-Contains $settingsVm ([regex]::Escape($needle)) "SettingsViewModel must contain $needle."
}

foreach ($needle in @("SettingsDaemonHeading", "SettingsDaemonDescription", "SettingsDaemonStatusInfo", "BtnSettingsSave", "SaveSettings_Click", "BtnStartDaemon", "BtnRestartDaemon", "BtnStopDaemon", "CanStartDaemon", "CanRestartDaemon", "CanStopDaemon")) {
    Require-Contains $settingsXaml ([regex]::Escape($needle)) "Settings page must contain $needle."
}

Require-Contains $settingsXaml 'SystemFillColorCriticalBrush' "Stop daemon button must use a destructive theme brush."

foreach ($needle in @("ContentDialog", "SaveSettings_Click", "StartDaemon_Click", "RestartDaemon_Click", "StopDaemon_Click", "LastSaveRequiresDaemonRestart", "XamlRoot", "ContentDialogResult.Primary")) {
    Require-Contains $settingsCodeBehind ([regex]::Escape($needle)) "Settings code-behind must contain $needle."
}

foreach ($needle in @("tb:TaskbarIcon", "TrayMainWindowItem", "TraySettingsItem", "TrayStartDaemonItem", "TrayRestartDaemonItem", "TrayStopDaemonItem", "TrayExitItem", "ShellStatus.CanStartDaemon", "ShellStatus.CanRestartDaemon", "ShellStatus.CanStopDaemon")) {
    Require-Contains $mainWindow ([regex]::Escape($needle)) "MainWindow must contain tray/status item $needle."
}
Require-Contains $mainWindow 'LeftClickCommand="\{x:Bind ShowMainWindowCommand\}"' "TaskbarIcon left click must be bound to the main-window restore command."
Require-Contains $mainWindow 'NoLeftClickDelay="True"' "TaskbarIcon left click must restore the window without waiting for double-click detection."
Require-NotContains $mainWindow 'IsHitTestVisible\s*=\s*"False"' "TaskbarIcon must remain hit-test visible so tray mouse messages can invoke commands."
foreach ($needle in @("Command=""{x:Bind ShowMainWindowCommand}""", "Command=""{x:Bind ShowSettingsCommand}""", "Command=""{x:Bind StartDaemonCommand}""", "Command=""{x:Bind RestartDaemonCommand}""", "Command=""{x:Bind StopDaemonCommand}""", "Command=""{x:Bind ExitApplicationCommand}""")) {
    Require-Contains $mainWindow ([regex]::Escape($needle)) "Tray menu must bind $needle because H.NotifyIcon PopupMenu invokes commands, not WinUI Click handlers."
}
foreach ($needle in @("Click=""TrayMainWindow_Click""", "Click=""TraySettings_Click""", "Click=""TrayExit_Click""")) {
    Require-NotContains $mainWindow ([regex]::Escape($needle)) "Tray menu must not rely on $needle in H.NotifyIcon PopupMenu mode."
}

foreach ($needle in @("MainWindow_Closed", "args.Handled = true", "AppWindow.Hide", "AppWindow.Show", "NavigateToSettings", "ShowMainWindowCommand", "ShowSettingsCommand", "StartDaemonCommand", "RestartDaemonCommand", "StopDaemonCommand", "ExitApplicationCommand", "ShellStatus_PropertyChanged", "RaiseTrayCommandCanExecuteChanged")) {
    Require-Contains $mainWindowCode ([regex]::Escape($needle)) "MainWindow code-behind must contain tray lifecycle item $needle."
}

Require-Contains $mainWindowCode 'ExitApplicationCommand\s*=\s*new AsyncRelayCommand\(ExitApplicationAsync\)' "Tray exit command must route through the shared exit flow."
Require-Contains $mainWindowCode 'ExitApplicationAsync[\s\S]*StopAllAsync' "Tray exit must stop wslcd-desktop before leaving the app."

foreach ($needle in @("RunStartupBootstrapAsync", "StartDaemonOnLaunchAsync", "AppServices.DaemonControl.StartAsync", "_ = RunStartupBootstrapAsync()", "RefreshShellStatusAsync")) {
    Require-Contains $app ([regex]::Escape($needle)) "App launch must auto-start daemon and refresh global status through $needle."
}

foreach ($needle in @("wslcd-desktop-*.log", "wslcd-*.log")) {
    Require-Contains $diagnostics ([regex]::Escape($needle)) "Daemon diagnostics must handle $needle."
}

foreach ($needle in @("CaptureAsync(bool startIfNeeded", "if (startIfNeeded)", "GetHealthAsync")) {
    Require-Contains $diagnostics ([regex]::Escape($needle)) "Daemon diagnostics must support passive shell status checks."
}

foreach ($needle in @("wslcd-desktop", "wslcd.dll", "wslcd-desktop.dll", "Stop-Process -Id")) {
    Require-Contains $killScript ([regex]::Escape($needle)) "Kill script must contain $needle."
}

foreach ($needle in @("wslcd-desktop", "wslcd.dll", "wslcd-desktop.dll", "Stop-Process\s+-Id")) {
    Require-Contains $killVerifier ([regex]::Escape($needle)) "Kill script verifier must contain $needle."
}

foreach ($needle in @(
    "SettingsDaemonHeading.Text",
    "SettingsDaemonDescription.Text",
    "SettingsStartDaemonText.Text",
    "SettingsRestartDaemonText.Text",
    "SettingsStopDaemonText.Text",
    "SettingsDaemonStatusInfo.Title",
    "TrayMainWindowItem.Text",
    "TraySettingsItem.Text",
    "TrayStartDaemonItem.Text",
    "TrayRestartDaemonItem.Text",
    "TrayStopDaemonItem.Text",
    "TrayExitItem.Text",
    "DaemonRestartDialogTitle",
    "DaemonStartDialogTitle",
    "DaemonStopDialogTitle"
)) {
    Require-Contains $en ([regex]::Escape($needle)) "English resources must contain $needle."
    Require-Contains $zh ([regex]::Escape($needle)) "Chinese resources must contain $needle."
}

foreach ($needle in @("DaemonStarted", "DaemonRestarted", "DaemonStopped", "DaemonStopWarning", "DaemonStartDialogTitle", "SettingsSavedDaemonRestartRecommended")) {
    Require-Contains $localizer ([regex]::Escape($needle)) "AppStringLocalizer must contain $needle."
}

Require-NotContains $settingsXaml 'Content="Restart Daemon"' "Settings XAML must not keep hard-coded Restart Daemon content."

"DAEMON_CONTROLS_SOURCE_OK"
