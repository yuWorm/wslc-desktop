param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

$ProjectPath = Join-Path $Root "wslc-desktop.csproj"
$AppPath = Join-Path $Root "App.xaml.cs"
$ProgramPath = Join-Path $Root "Program.cs"
$MainWindowPath = Join-Path $Root "MainWindow.xaml.cs"
$ManifestPath = Join-Path $Root "Package.appxmanifest"
$LaunchLoggerPath = Join-Path $Root "Services\AppLaunchLogger.cs"
$ArtifactScriptPath = Join-Path $Root "scripts\Build-ReleaseArtifacts.ps1"
$InstallerPath = Join-Path $Root "installer\wslc-desktop.iss"
$DaemonPath = Join-Path $Root "src\wslcd\Program.cs"
$SettingsVmPath = Join-Path $Root "ViewModels\SettingsViewModel.cs"
$SettingsXamlPath = Join-Path $Root "Pages\SettingsPage.xaml"
$ModelsPath = Join-Path $Root "Models\WslcDomainModels.cs"
$InterfacesPath = Join-Path $Root "Services\IWslcServices.cs"
$AppServicesPath = Join-Path $Root "Services\AppServices.cs"
$SettingsServicePath = Join-Path $Root "Services\FileAppSettingsService.cs"
$StartupTaskPath = Join-Path $Root "Services\StartupTaskService.cs"
$DaemonDiagnosticsPath = Join-Path $Root "Services\DaemonDiagnosticsService.cs"
$DaemonProcessPath = Join-Path $Root "Services\DaemonProcessManager.cs"
$DiagnosticsPath = Join-Path $Root "Services\AppDiagnosticsService.cs"
$WslcdSmokePath = Join-Path $Root "tools\WslcdSmokeVerify\Program.cs"
$DockerVerifyPath = Join-Path $Root "tools\DockerApiCompatVerify\Program.cs"
$FullVerifyPath = Join-Path $Root "scripts\Verify-FullRelease.ps1"
$ReleaseNotesPath = Join-Path $Root "docs\RELEASE_NOTES.md"
$TrackerPath = Join-Path $Root "docs\WSLCD_PHASE_TRACKER.md"

function Assert-Path {
    param([string]$Path, [string]$Message)
    if (-not (Test-Path $Path)) {
        throw $Message
    }
}

function Assert-Contains {
    param([string]$Text, [string]$Pattern, [string]$Message)
    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

Assert-Path $StartupTaskPath "Phase 20 must add a StartupTaskService for launch-at-login controls."
Assert-Path $DaemonDiagnosticsPath "Phase 20 must add daemon diagnostics capture."
Assert-Path $LaunchLoggerPath "Phase 20 must keep app launch diagnostics for installer troubleshooting."
Assert-Path $ProgramPath "Phase 20 must keep app-owned Program.Main for early startup diagnostics."
Assert-Path $FullVerifyPath "Phase 20 must add a full release verification script."
Assert-Path $ReleaseNotesPath "Phase 20 must add release notes."

$project = Get-Content -Raw $ProjectPath
$app = Get-Content -Raw $AppPath
$program = Get-Content -Raw $ProgramPath
$mainWindow = Get-Content -Raw $MainWindowPath
$manifest = Get-Content -Raw $ManifestPath
$launchLogger = Get-Content -Raw $LaunchLoggerPath
$artifactScript = Get-Content -Raw $ArtifactScriptPath
$installer = Get-Content -Raw $InstallerPath
$daemon = Get-Content -Raw $DaemonPath
$settingsVm = Get-Content -Raw $SettingsVmPath
$settingsXaml = Get-Content -Raw $SettingsXamlPath
$models = Get-Content -Raw $ModelsPath
$interfaces = Get-Content -Raw $InterfacesPath
$appServices = Get-Content -Raw $AppServicesPath
$settingsService = Get-Content -Raw $SettingsServicePath
$startupTask = Get-Content -Raw $StartupTaskPath
$daemonDiagnostics = Get-Content -Raw $DaemonDiagnosticsPath
$daemonProcess = Get-Content -Raw $DaemonProcessPath
$diagnostics = Get-Content -Raw $DiagnosticsPath
$wslcdSmoke = Get-Content -Raw $WslcdSmokePath
$dockerVerify = Get-Content -Raw $DockerVerifyPath
$fullVerify = Get-Content -Raw $FullVerifyPath
$releaseNotes = Get-Content -Raw $ReleaseNotesPath
$tracker = Get-Content -Raw $TrackerPath

Assert-Contains $project "CopyWslcdToOutput" "Project must copy wslcd outputs beside the packaged WinUI app."
Assert-Contains $project "Targets=""Publish""" "Project must publish wslcd instead of copying framework-dependent build output."
Assert-Contains $project "SelfContained=true" "Project must package wslcd as a self-contained daemon."
Assert-Contains $project "wslcd-desktop\.exe" "Project must package the renamed wslcd-desktop executable."
Assert-Contains $project "wslcd-desktop\.dll" "Project must package the renamed wslcd-desktop assembly."
Assert-Contains $project "\$\(OutDir\)wslcd\\" "Project must isolate packaged wslcd output under a daemon subdirectory."
Assert-Contains $project "\$\(OutDir\)AppX\\wslcd\\" "Project must copy packaged wslcd into the WinApp AppX run layout."
Assert-Contains $project "ApplicationIcon" "Project must embed the app icon for Setup.exe shortcuts and portable builds."
Assert-Contains $project "WindowsPackageType" "Project must declare the Release app model for Setup.exe and portable builds."
Assert-Contains $project "WindowsAppSDKSelfContained" "Project must build Release artifacts with self-contained Windows App SDK runtime files."
Assert-Contains $project "DISABLE_XAML_GENERATED_MAIN" "Project must use app-owned Program.Main for early startup diagnostics."
Assert-Contains $project "sources\\\*\*\\\*" "Project must exclude reference source snapshots from build outputs."
Assert-Contains $project "artifacts\\\*\*\\\*" "Project must exclude nested build artifacts from build outputs."
Assert-Contains $app "TrySetPrimaryLanguageOverride" "App startup must tolerate missing package identity for language overrides."
Assert-Contains $program "Program\.Main started" "Program.Main must log before WinUI Application.Start."
Assert-Contains $program "WinRT\.ComWrappersSupport\.InitializeComWrappers" "Program.Main must preserve generated WinUI COM wrapper initialization."
Assert-Contains $program "Application\.Start" "Program.Main must preserve WinUI Application.Start."
Assert-Contains $mainWindow "CenterWindowOnPrimaryDisplay" "MainWindow must center the first window on a visible display."
Assert-Contains $mainWindow "SetForegroundWindow" "MainWindow must request foreground activation."
Assert-Contains $launchLogger "wslc-desktop-launch" "App must write launch diagnostics for silent startup failures."
Assert-Contains $launchLogger "last-crash\.log" "App must write last-crash diagnostics."
Assert-Contains $artifactScript "Microsoft\.WindowsAppRuntime\.dll" "Release artifact script must reject non-self-contained Windows App SDK layouts."
Assert-Contains $artifactScript "excludedRootItems" "Release artifact script must exclude repository-only directories."
Assert-Contains $artifactScript '"sources"' "Release artifact script must exclude reference source snapshots from installers."
Assert-Contains $installer "IconFilename" "Installer shortcuts must explicitly use the app icon."
Assert-Contains $installer "UninstallDisplayIcon=\{app\}\\Assets\\AppIcon\.ico" "Installer must show the app icon in Add/Remove Programs."
Assert-Contains $daemonProcess "Path\.Combine\(appDirectory,\s*""wslcd""\)" "DaemonProcessManager must prefer the packaged daemon subdirectory."
Assert-Contains $daemonProcess "wslcd-desktop\.exe" "DaemonProcessManager must prefer the renamed daemon executable."
Assert-Contains $daemonProcess "wslcd-desktop\.dll" "DaemonProcessManager must prefer the renamed daemon DLL."
Assert-Contains $daemonProcess "EnumerateAppBaseDirectories" "DaemonProcessManager must account for WinApp AppX base-directory layouts."
Assert-Contains $wslcdSmoke "WSLCD_PACKAGED_DAEMON_OK" "WslcdSmokeVerify must expose packaged daemon verification."

Assert-Contains $manifest "windows\.startupTask" "Package manifest must declare a startup task extension."
Assert-Contains $manifest "WslcDesktopStartupTask" "Package manifest must use the startup task id consumed by code."

Assert-Contains $daemon "CurrentUserOnly\s*=\s*true" "wslcd named pipes must remain current-user only."

Assert-Contains $models "LaunchAtLogin" "AppSettingsSnapshot must persist launch-at-login intent."
Assert-Contains $models "StartupTaskSnapshot" "Models must expose startup task status."
Assert-Contains $models "DaemonDiagnosticsSnapshot" "Models must expose daemon diagnostics snapshot."

Assert-Contains $interfaces "IStartupTaskService" "Services must expose startup task control."
Assert-Contains $interfaces "IDaemonDiagnosticsService" "Services must expose daemon diagnostics capture."
Assert-Contains $interfaces "IDaemonControlService" "Services must expose daemon restart/recovery control."

Assert-Contains $settingsService "LaunchAtLogin" "FileAppSettingsService must persist launch-at-login intent."
Assert-Contains $startupTask "Windows\.ApplicationModel" "StartupTaskService must use the Windows startup task API."
Assert-Contains $startupTask "WslcDesktopStartupTask" "StartupTaskService must use the manifest startup task id."
Assert-Contains $startupTask "RequestEnableAsync" "StartupTaskService must enable startup through the platform API."
Assert-Contains $startupTask "Disable" "StartupTaskService must disable startup through the platform API."

Assert-Contains $daemonProcess "RestartAsync" "DaemonProcessManager must expose crash recovery restart."
Assert-Contains $daemonProcess "StopOwnedProcess" "DaemonProcessManager must stop only the daemon it owns."
Assert-Contains $settingsVm "LaunchAtLogin" "SettingsViewModel must expose launch-at-login binding."
Assert-Contains $settingsVm "RestartDaemonCommand" "SettingsViewModel must expose daemon recovery command."
Assert-Contains $settingsVm "StartupTaskStatus" "SettingsViewModel must surface startup task status."
Assert-Contains $settingsXaml "TglLaunchAtLogin" "Settings page must expose launch-at-login toggle."
Assert-Contains $settingsXaml "BtnRestartDaemon" "Settings page must expose daemon recovery action."

Assert-Contains $daemonDiagnostics "pipeHealth" "Daemon diagnostics must include pipe health."
Assert-Contains $daemonDiagnostics "logFiles" "Daemon diagnostics must include daemon log file inventory."
Assert-Contains $daemonDiagnostics "compatibilityMatrix" "Daemon diagnostics must include compatibility matrix metadata."
Assert-Contains $diagnostics '"daemon"' "App diagnostics export must include daemon diagnostics."
Assert-Contains $diagnostics '"compatibilityMatrix"' "App diagnostics export must include compatibility matrix metadata."
Assert-Contains $appServices "StartupTaskService" "AppServices must wire StartupTaskService."
Assert-Contains $appServices "DaemonDiagnosticsService" "AppServices must wire DaemonDiagnosticsService."
Assert-Contains $appServices "DaemonControlService" "AppServices must wire daemon recovery control."

Assert-Contains $wslcdSmoke "full" "WslcdSmokeVerify must expose full mode."
Assert-Contains $wslcdSmoke "WSLCD_FULL_SMOKE_OK" "WslcdSmokeVerify full mode must print WSLCD_FULL_SMOKE_OK."
Assert-Contains $dockerVerify "full" "DockerApiCompatVerify must expose full mode."
Assert-Contains $dockerVerify "DOCKER_API_COMPAT_FULL_OK" "DockerApiCompatVerify full mode must print DOCKER_API_COMPAT_FULL_OK."
Assert-Contains $dockerVerify "TimeSpan\.FromMinutes\(4\)" "DockerApiCompatVerify full mode must use a longer cancellation budget than single-phase smoke modes."
Assert-Contains $dockerVerify 'mode\.Equals\("docker-cli"' "DockerApiCompatVerify must expose packaged Docker CLI verification."
Assert-Contains $dockerVerify "DOCKER_API_DOCKER_CLI_OK" "DockerApiCompatVerify docker-cli mode must print DOCKER_API_DOCKER_CLI_OK."
Assert-Contains $dockerVerify "releaseLayoutDaemon" "DockerApiCompatVerify docker-cli mode must require the Release layout daemon."

Assert-Contains $fullVerify "dotnet build --no-restore" "Full release verifier must run a full build."
Assert-Contains $fullVerify 'Invoke-ProcessStep "dotnet restore root project"' "Full release verifier must restore the root project before no-restore builds."
Assert-Contains $fullVerify 'Invoke-ProcessStep "dotnet restore wslcd host RID"' "Full release verifier must restore packaged daemon RID assets."
Assert-Contains $fullVerify "RuntimeIdentifier=any" "Full release verifier must restore Windows-targeted verifier tools with RuntimeIdentifier=any."
Assert-Contains $fullVerify "WslcdSmokeVerify" "Full release verifier must include daemon smoke."
Assert-Contains $fullVerify "WSLCD_SMOKE_DAEMON_PATH" "Full release verifier must run WslcdSmokeVerify against the release daemon path so WDAC-blocked Debug DLLs do not block release gates."
Assert-Contains $fullVerify "DockerApiCompatVerify" "Full release verifier must include Docker API smoke."
Assert-Contains $fullVerify "LocalizationVerify" "Full release verifier must include localization."
Assert-Contains $fullVerify "ComposePlanVerify" "Full release verifier must include compose plan verification."
Assert-Contains $fullVerify "HardeningVerify" "Full release verifier must include hardening verification."
Assert-Contains $fullVerify "Build-ReleaseArtifacts\.ps1" "Full release verifier must include Setup.exe and portable.zip artifact creation."

Assert-Contains $releaseNotes "Docker API coverage" "Release notes must document Docker API coverage."
Assert-Contains $releaseNotes "Unsupported" "Release notes must document unsupported endpoints."
Assert-Contains $releaseNotes "Security model" "Release notes must document the security model."
Assert-Contains $releaseNotes "Known limitations" "Release notes must document known limitations."
Assert-Contains $releaseNotes "Setup\.exe" "Release notes must document Setup.exe distribution."
Assert-Contains $releaseNotes "portable\.zip" "Release notes must document portable.zip distribution."
Assert-Contains $tracker "Phase 20" "Phase tracker must retain Phase 20 state."

Write-Host "PHASE20_SOURCE_OK"
