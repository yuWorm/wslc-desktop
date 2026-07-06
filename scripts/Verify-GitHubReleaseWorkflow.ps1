$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $root ".github\workflows\release.yml"
$gitignorePath = Join-Path $root ".gitignore"
$projectPath = Join-Path $root "wslc-desktop.csproj"
$appPath = Join-Path $root "App.xaml.cs"
$programPath = Join-Path $root "Program.cs"
$mainWindowPath = Join-Path $root "MainWindow.xaml.cs"
$launchLoggerPath = Join-Path $root "Services\AppLaunchLogger.cs"
$fullReleasePath = Join-Path $root "scripts\Verify-FullRelease.ps1"
$artifactScriptPath = Join-Path $root "scripts\Build-ReleaseArtifacts.ps1"
$installerScriptPath = Join-Path $root "installer\wslc-desktop.iss"
$releaseNotesPath = Join-Path $root "docs\RELEASE_NOTES.md"
$dockerMatrixPath = Join-Path $root "docs\DOCKER_API_COMPATIBILITY_MATRIX.md"
$runtimeMatrixPath = Join-Path $root "docs\RUNTIME_PROVIDER_MATRIX.md"

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

function Assert-NotContains {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -match $Pattern) {
        throw $Message
    }
}

if (-not (Test-Path $workflowPath)) {
    throw "Missing .github\workflows\release.yml"
}

$workflow = Get-Content -Raw $workflowPath
$gitignore = Get-Content -Raw $gitignorePath
$project = Get-Content -Raw $projectPath
$app = Get-Content -Raw $appPath
$program = Get-Content -Raw $programPath
$mainWindow = Get-Content -Raw $mainWindowPath
$launchLogger = Get-Content -Raw $launchLoggerPath
$fullRelease = Get-Content -Raw $fullReleasePath
$artifactScript = Get-Content -Raw $artifactScriptPath
$installerScript = Get-Content -Raw $installerScriptPath

foreach ($path in @($projectPath, $appPath, $programPath, $mainWindowPath, $launchLoggerPath, $artifactScriptPath, $installerScriptPath, $releaseNotesPath, $dockerMatrixPath, $runtimeMatrixPath)) {
    if (-not (Test-Path $path)) {
        throw "Missing release documentation required by release automation: $path"
    }
}

Assert-NotContains $gitignore '(?m)^/docs\s*$' ".gitignore must not ignore docs because release automation publishes docs/RELEASE_NOTES.md and source gates require docs matrices."

Assert-Contains $workflow '(?m)^\s*on:\s*$' "Release workflow must declare triggers."
Assert-Contains $workflow '(?m)^\s*push:\s*$' "Release workflow must be push-triggered."
Assert-Contains $workflow '(?m)^\s*tags:\s*$' "Release workflow must restrict pushes to tags."
Assert-Contains $workflow 'v\*\.\*\.\*' "Release workflow must only match version-style v*.*.* tags."
Assert-NotContains $workflow '(?m)^\s*branches:\s*$' "Release workflow must not run for branch pushes."
Assert-NotContains $workflow '(?m)^\s*pull_request:\s*$' "Release workflow must not run for pull requests."
Assert-NotContains $workflow '(?m)^\s*workflow_dispatch:\s*$' "Release workflow must not support manual non-tag releases."
Assert-Contains $workflow 'contents:\s*write' "Release workflow must have permission to create GitHub releases."
Assert-Contains $workflow 'matrix:' "Release workflow must build a platform matrix."
Assert-Contains $workflow 'x64' "Release workflow must package x64."
Assert-Contains $workflow 'ARM64' "Release workflow must package ARM64."
Assert-Contains $workflow 'win-x64' "Release workflow must produce a win-x64 package."
Assert-Contains $workflow 'win-arm64' "Release workflow must produce a win-arm64 package."
Assert-Contains $workflow 'actions/checkout@v4' "Release workflow must checkout repository sources."
Assert-Contains $workflow 'actions/setup-dotnet@v4' "Release workflow must install the .NET SDK."
Assert-Contains $workflow 'choco install innosetup' "Release workflow must install Inno Setup for Setup.exe packaging."
Assert-Contains $workflow 'Verify-FullRelease\.ps1' "Release workflow must reuse the local full release verifier."
Assert-Contains $workflow '-SkipPackage' "Release workflow must separate build verification from final artifact creation."
Assert-Contains $workflow '-AllowKnownLocalBlockers' "Release workflow must tolerate unavailable local WSLC/Docker runtime gates in CI."
Assert-Contains $workflow 'Build-ReleaseArtifacts\.ps1' "Release workflow must build Setup.exe and portable.zip artifacts."
Assert-Contains $workflow '\*\.exe' "Release workflow must upload Setup.exe artifacts."
Assert-Contains $workflow '\*\.zip' "Release workflow must upload portable.zip artifacts."
Assert-NotContains $workflow 'microsoft/setup-WinAppCli@v0\.1' "Release workflow must not install WinApp CLI when publishing only Setup.exe and portable.zip."
Assert-NotContains $workflow 'WINDOWS_SIGNING_CERT_BASE64' "Release workflow must not require MSIX signing certificate secrets."
Assert-NotContains $workflow 'WINDOWS_SIGNING_CERT_PASSWORD' "Release workflow must not require MSIX signing certificate secrets."
Assert-NotContains $workflow 'winapp package' "Release workflow must not package MSIX artifacts."
Assert-NotContains $workflow '\.msix' "Release workflow must not publish MSIX artifacts."
Assert-Contains $artifactScript 'Get-FileHash' "Release artifact script must generate checksums."
Assert-Contains $artifactScript 'Compress-Archive' "Release artifact script must create portable.zip."
Assert-Contains $artifactScript 'ISCC\.exe' "Release artifact script must create Setup.exe through Inno Setup."
Assert-Contains $artifactScript 'Microsoft\.WindowsAppRuntime\.dll' "Release artifact script must require a self-contained Windows App SDK layout."
Assert-Contains $artifactScript 'excludedRootItems' "Release artifact script must exclude repository-only directories from installers."
Assert-Contains $artifactScript '"sources"' "Release artifact script must exclude reference source snapshots from installers."
Assert-Contains $artifactScript '"artifacts"' "Release artifact script must exclude nested build artifacts from installers."
Assert-Contains $artifactScript 'Remove-ReleasePayloadFiles' "Release artifact script must prune unused runtime payload files after staging."
Assert-Contains $artifactScript 'onnxruntime\.dll' "Release artifact script must remove unused ONNX runtime payloads."
Assert-Contains $artifactScript 'DirectML\.dll' "Release artifact script must remove unused DirectML payloads."
Assert-Contains $artifactScript 'Microsoft\.Windows\.AI\.MachineLearning\.dll' "Release artifact script must remove unused Windows AI MachineLearning payloads."
Assert-Contains $artifactScript 'Microsoft\.Windows\.AI\.MachineLearning\.Projection\.dll' "Release artifact script must remove unused Windows AI projection payloads."
Assert-Contains $artifactScript 'Microsoft\.ML\.OnnxRuntime\.dll' "Release artifact script must remove unused managed ONNX payloads."
Assert-Contains $installerScript 'AppName=\{#AppName\}' "Inno Setup script must define the WSLC Desktop app identity."
Assert-Contains $installerScript 'Source: "\{#SourceDir\}\\\*"' "Inno Setup script must package the prepared release layout."
Assert-Contains $installerScript 'DefaultDirName=\{localappdata\}\\Programs\\WSLC Desktop' "Inno Setup script must install per-user without elevation."
Assert-Contains $installerScript 'UninstallDisplayIcon=\{app\}\\Assets\\AppIcon\.ico' "Inno Setup script must use the app icon for Add/Remove Programs."
Assert-Contains $installerScript 'IconFilename: "\{app\}\\Assets\\AppIcon\.ico"' "Inno Setup shortcuts must use the app icon."
Assert-Contains $workflow 'actions/upload-artifact@v4' "Release workflow must upload per-platform artifacts."
Assert-Contains $workflow 'actions/download-artifact@v4' "Release workflow must collect matrix artifacts before release."
Assert-Contains $workflow 'softprops/action-gh-release@v2' "Release workflow must publish artifacts to GitHub Releases."
Assert-Contains $workflow 'prerelease:\s*\$\{\{\s*contains\(github\.ref_name,\s*''-''\)\s*\}\}' "Release workflow must mark pre-release tags such as beta builds as GitHub prereleases."
Assert-Contains $workflow 'docs/RELEASE_NOTES\.md' "Release workflow must use release notes from docs."

Assert-Contains $fullRelease 'EnableDevelopmentCodeSigning=false' "Full release verifier must disable Debug loose-layout development signing in CI."
Assert-Contains $fullRelease 'RuntimeIdentifier=\$ReleaseRid' "Full release verifier must build the selected release RID."
Assert-Contains $fullRelease 'PublishReadyToRun=false' "Full release verifier must avoid ReadyToRun restore mismatches on GitHub-hosted runners."
Assert-Contains $fullRelease 'WindowsPackageType=None' "Full release verifier must build an unpackaged Release layout for Setup.exe and portable.zip."
Assert-Contains $fullRelease 'WindowsAppSDKSelfContained=true' "Full release verifier must build a self-contained Windows App SDK layout."
Assert-Contains $fullRelease 'Microsoft\.WindowsAppRuntime\.dll' "Full release verifier must assert the self-contained Windows App SDK runtime is present."
Assert-Contains $fullRelease 'Invoke-ProcessStep "dotnet build --no-restore"' "Full release verifier must check native dotnet build exit codes."
Assert-Contains $fullRelease 'Invoke-ProcessStep "Release build"' "Full release verifier must check native Release build exit codes."

Assert-Contains $project 'ApplicationIcon' "Project must embed the app icon for unpackaged installers."
Assert-Contains $project 'WindowsPackageType' "Project must declare the Release app model."
Assert-Contains $project 'WindowsAppSDKSelfContained' "Project must declare the Release Windows App SDK deployment mode."
Assert-Contains $project 'EnableMsixTooling' "Project must keep MSIX tooling conditional by build configuration."
Assert-Contains $project 'DISABLE_XAML_GENERATED_MAIN' "Project must use the app-owned Program.Main for early startup diagnostics."
Assert-Contains $project 'sources\\\*\*\\\*' "Project must exclude reference source snapshots from build outputs."
Assert-Contains $project 'artifacts\\\*\*\\\*' "Project must exclude nested build artifacts from build outputs."
Assert-Contains $app 'TrySetPrimaryLanguageOverride' "App startup must not crash when Windows language override is unavailable in unpackaged mode."
Assert-Contains $program 'Program\.Main started' "Program.Main must log before WinUI Application.Start."
Assert-Contains $program 'WinRT\.ComWrappersSupport\.InitializeComWrappers' "Program.Main must preserve generated WinUI COM wrapper initialization."
Assert-Contains $program 'Application\.Start' "Program.Main must preserve WinUI Application.Start."
Assert-Contains $mainWindow 'CenterWindowOnPrimaryDisplay' "MainWindow must be centered on startup so hidden/off-screen windows are less likely."
Assert-Contains $mainWindow 'SetForegroundWindow' "MainWindow must request foreground activation after startup and tray restore."
Assert-Contains $launchLogger 'wslc-desktop-launch' "App must write launch diagnostics for silent startup failures."
Assert-Contains $launchLogger 'last-crash\.log' "App must write a last-crash diagnostic file."

Write-Host "GITHUB_RELEASE_WORKFLOW_OK"
