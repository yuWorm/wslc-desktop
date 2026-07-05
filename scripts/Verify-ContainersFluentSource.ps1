param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$PagePath = Join-Path $Root "Pages\ContainersPage.xaml"
$CodeBehindPath = Join-Path $Root "Pages\ContainersPage.xaml.cs"
$ViewModelPath = Join-Path $Root "ViewModels\ContainersViewModel.cs"
$TerminalServicePath = Join-Path $Root "Services\WslcCliTerminalService.cs"
$EnglishResourcesPath = Join-Path $Root "Strings\en-US\Resources.resw"
$ChineseResourcesPath = Join-Path $Root "Strings\zh-CN\Resources.resw"

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

$page = Get-Content -Raw $PagePath
$codeBehind = Get-Content -Raw $CodeBehindPath
$viewModel = Get-Content -Raw $ViewModelPath
$terminalService = Get-Content -Raw $TerminalServicePath
$englishResources = Get-Content -Raw $EnglishResourcesPath
$chineseResources = Get-Content -Raw $ChineseResourcesPath

Assert-NotContains $page "<Pivot\b" "Containers page must replace large Pivot headers with compact Fluent SelectorBar tabs."
Assert-Contains $page "<SelectorBar\b" "Containers page must use WinUI SelectorBar for compact detail tabs."
Assert-Contains $page "ThemeResource" "Containers page must use Fluent theme resources."
Assert-NotContains $page "#[0-9A-Fa-f]{6,8}" "Containers page must not hard-code competitor-style hex colors."
Assert-Contains $page "SelectedContainerPortLinks" "Containers page must bind structured port links."
Assert-Contains $page "SelectedContainerEnvironmentEntries" "Containers page must bind structured environment entries."
Assert-Contains $page "SelectedContainerLogsPreview" "Containers page must show a compact logs preview."
Assert-Contains $page "SelectedContainerInspectPreview" "Containers page must show a compact inspect preview."
Assert-Contains $page "ShowLogsDialog_Click" "Logs preview must expand into a ContentDialog."
Assert-Contains $page "ShowTerminalDialog_Click" "Terminal preview must expand into a ContentDialog."
Assert-Contains $page "ShowInspectDialog_Click" "Inspect preview must expand into a ContentDialog."
Assert-Contains $page "ExternalTerminalCommand" "Terminal pane must offer an external Windows Terminal action."
Assert-Contains $page '(?s)x:Name="OverviewPane".*x:Uid="ContainersOverviewOpenTerminalButton".*Command="\{Binding ExternalTerminalCommand\}"' "Overview pane must offer a quick Open Terminal action."
Assert-Contains $page "OpenPort_Click" "Port chips must support opening localhost endpoints."

Assert-Contains $codeBehind "ContentDialog" "Code-behind must create Fluent ContentDialog expanders."
Assert-Contains $codeBehind "ShowTextDialogAsync" "Code-behind must share dialog rendering for dense text panes."
Assert-Contains $codeBehind "Launcher\.LaunchUriAsync" "Code-behind must open published port URLs with Windows Launcher."
Assert-Contains $codeBehind "ContainersDetailSelector_SelectionChanged" "Code-behind must switch SelectorBar detail panes."

Assert-Contains $viewModel "ContainerPortLink" "ViewModel must expose structured port link records."
Assert-Contains $viewModel "ContainerEnvironmentEntry" "ViewModel must expose structured environment records."
Assert-Contains $viewModel "SelectedContainerPortLinks" "ViewModel must project selected container ports."
Assert-Contains $viewModel "SelectedContainerEnvironmentEntries" "ViewModel must project selected container environment."
Assert-Contains $viewModel "SelectedContainerLogsPreview" "ViewModel must expose compact log preview text."
Assert-Contains $viewModel "SelectedContainerInspectPreview" "ViewModel must expose compact inspect preview text."
Assert-Contains $viewModel "ExternalTerminalCommand" "ViewModel must expose external terminal command."
Assert-Contains $viewModel "CanStartSelectedContainer => SelectedContainer[?][.]State is ContainerRuntimeState[.]Created or ContainerRuntimeState[.]Stopped or ContainerRuntimeState[.]Exited" "Start must only be available for created, stopped, or exited containers."
Assert-Contains $viewModel "CanStopSelectedContainer => SelectedContainer[?][.]State == ContainerRuntimeState[.]Running" "Stop must only be available for running containers."
Assert-Contains $viewModel "CanRestartSelectedContainer => SelectedContainer[?][.]State == ContainerRuntimeState[.]Running" "Restart must only be available for running containers."
Assert-Contains $viewModel "CanOpenTerminal => SelectedContainer[?][.]State == ContainerRuntimeState[.]Running" "Terminal actions must only be available for running containers."
Assert-Contains $viewModel 'StartCommand = new AsyncRelayCommand[(]StartSelectedAsync, [(][)] => CanStartSelectedContainer[)]' "Start command must bind to startable container states."
Assert-Contains $viewModel 'StopCommand = new AsyncRelayCommand[(]StopSelectedAsync, [(][)] => CanStopSelectedContainer[)]' "Stop command must bind to running state."
Assert-Contains $viewModel 'RestartCommand = new AsyncRelayCommand[(]RestartSelectedAsync, [(][)] => CanRestartSelectedContainer[)]' "Restart command must bind to running state."
Assert-Contains $viewModel 'ExternalTerminalCommand = new AsyncRelayCommand[(]OpenExternalTerminalAsync, [(][)] => CanOpenTerminal[)]' "External terminal command must bind to running state."
Assert-Contains $viewModel 'StartSelectedAsync[(][)] => CanStartSelectedContainer' "Start method must guard against invalid states."
Assert-Contains $viewModel 'StopSelectedAsync[(][)] => CanStopSelectedContainer' "Stop method must guard against invalid states."
Assert-Contains $viewModel 'RestartSelectedAsync[(][)] => CanRestartSelectedContainer' "Restart method must guard against invalid states."
Assert-Contains $viewModel 'DeleteSelectedAsync[(][)] => CanDeleteSelectedContainer' "Delete method must guard against missing selection."
Assert-Contains $viewModel 'if [(]selected is null [|][|] [!]CanOpenTerminal[)]' "OpenExternalTerminalAsync must guard against non-running containers."
Assert-Contains $page 'Click="ShowLogsDialog_Click"[^>]+IsEnabled="\{Binding HasSelectedContainer\}"' "Logs expand action must be disabled without a selected container."
Assert-Contains $page 'Click="ShowTerminalDialog_Click"[^>]+IsEnabled="\{Binding HasSelectedContainer\}"' "Terminal transcript expand action must be disabled without a selected container."
Assert-Contains $page 'Click="ShowInspectDialog_Click"[^>]+IsEnabled="\{Binding HasSelectedContainer\}"' "Inspect expand action must be disabled without a selected container."
Assert-Contains $terminalService "ProcessStartInfo" "Terminal service must launch external Windows Terminal."
Assert-Contains $terminalService '"exec"\s*,\s*"-it"' "Terminal service must use interactive WSLC exec."
Assert-Contains $englishResources 'ContainersOverviewOpenTerminalButtonText\.Text' "English resources must localize the overview terminal shortcut."
Assert-Contains $chineseResources 'ContainersOverviewOpenTerminalButtonText\.Text' "Chinese resources must localize the overview terminal shortcut."

Write-Host "CONTAINERS_FLUENT_SOURCE_OK"
