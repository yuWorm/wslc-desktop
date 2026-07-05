$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

function Get-RepoFile {
    param([string]$Path)

    $fullPath = Join-Path $root $Path
    if (!(Test-Path $fullPath)) {
        throw "Missing file: $Path"
    }

    Get-Content -Raw $fullPath
}

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

$project = Get-RepoFile "wslc-desktop.csproj"
$manifest = Get-RepoFile "Package.appxmanifest"
$mainWindow = Get-RepoFile "MainWindow.xaml"
$mainWindowCode = Get-RepoFile "MainWindow.xaml.cs"
$containersPage = Get-RepoFile "Pages\ContainersPage.xaml"
$settings = Get-RepoFile "Services\FileAppSettingsService.cs"
$signScript = Get-RepoFile "scripts\Sign-DevelopmentOutput.ps1"

Assert-NotContains $project 'WindowsTerminal\.WinUI3\.Control' "Main app must not reference the experimental Windows Terminal control package."
Assert-NotContains $project 'WindowsTerminal\.WinUI3\.Settings\.Model' "Main app must not reference the experimental Windows Terminal settings package."
Assert-NotContains $project 'CopyNativeTerminalAssets' "Main app must not copy experimental Windows Terminal native assets."
Assert-Contains $project 'AppX\\AppX\\Microsoft\.Terminal\*\.dll' "Build cleanup must remove stale Terminal native DLLs from nested loose AppX output."
Assert-NotContains $manifest 'Microsoft\.Terminal\.' "Package manifest must not register experimental Terminal WinRT classes."
Assert-NotContains $mainWindow 'NavTerminalLab|Terminal Lab|terminal-lab' "Main navigation must not expose the experimental native terminal lab."
Assert-NotContains $mainWindowCode 'NativeTerminalPage' "Main navigation must not route to the experimental native terminal page."
Assert-NotContains $signScript 'Microsoft\.Terminal\.Control\.dll|Microsoft\.Terminal\.Settings\.Model\.dll|Microsoft\.Terminal\.TerminalConnection\.dll|cpprest142_2_10\.dll' "Development signing must not retain skip exceptions for removed Terminal native assets."

if (Test-Path (Join-Path $root "Pages\NativeTerminalPage.xaml")) {
    throw "Pages\NativeTerminalPage.xaml must be removed from the main app."
}

if (Test-Path (Join-Path $root "Pages\NativeTerminalPage.xaml.cs")) {
    throw "Pages\NativeTerminalPage.xaml.cs must be removed from the main app."
}

$externalIndex = $containersPage.IndexOf('x:Uid="ExternalTerminalButton"', [StringComparison]::Ordinal)
$embeddedIndex = $containersPage.IndexOf('x:Uid="ConnectTerminalButton"', [StringComparison]::Ordinal)

if ($externalIndex -lt 0) {
    throw "Container terminal pane must expose the external terminal action."
}

if ($embeddedIndex -lt 0) {
    throw "Container terminal pane may retain the embedded preview action as a secondary fallback."
}

if ($externalIndex -gt $embeddedIndex) {
    throw "External terminal action must appear before the embedded preview action."
}

Assert-Contains $containersPage 'Label="Open Terminal"' "External terminal button must be labeled as the primary terminal action."
Assert-Contains $containersPage 'Label="Embedded Preview"' "Embedded ConPTY transcript must be labeled as a preview fallback."
Assert-Contains $settings '"/bin/sh",\s*true,\s*AppLanguage\.System' "Default settings must prefer external terminal behavior."

Write-Output "EXTERNAL_TERMINAL_STRATEGY_OK"
