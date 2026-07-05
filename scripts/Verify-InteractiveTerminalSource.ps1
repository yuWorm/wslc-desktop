$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

function Require-FileContains {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Message
    )

    $fullPath = Join-Path $root $Path
    if (!(Test-Path $fullPath)) {
        throw "Missing file: $Path"
    }

    $content = Get-Content -Raw $fullPath
    if ($content -notmatch $Pattern) {
        throw $Message
    }
}

function Require-FileNotContains {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Message
    )

    $fullPath = Join-Path $root $Path
    if (!(Test-Path $fullPath)) {
        throw "Missing file: $Path"
    }

    $content = Get-Content -Raw $fullPath
    if ($content -match $Pattern) {
        throw $Message
    }
}

Require-FileContains "Services\IWslcServices.cs" "interface\s+IWslcTerminalService" "IWslcTerminalService contract is missing."
Require-FileContains "Services\IWslcServices.cs" "interface\s+ITerminalSession" "ITerminalSession contract is missing."
Require-FileContains "Models\TerminalSessionModels.cs" "TerminalConnectRequest" "TerminalConnectRequest model is missing."
Require-FileContains "Models\TerminalSessionModels.cs" "TerminalSessionState" "TerminalSessionState model is missing."
Require-FileContains "Services\WslcCliTerminalService.cs" "wslc" "WSLC CLI terminal service is missing the wslc launcher."
Require-FileContains "Services\WslcCliTerminalService.cs" '"exec"\s*,\s*"-it"' "WSLC CLI terminal service must launch interactive TTY exec."
Require-FileContains "Services\WslcCliTerminalService.cs" "/bin/bash" "WSLC CLI terminal service must try bash first."
Require-FileContains "Services\WslcCliTerminalService.cs" "/bin/sh" "WSLC CLI terminal service must fall back to sh."
Require-FileContains "Services\WslcCliTerminalService.cs" "isAsync:\s*false" "ConPTY pipe FileStream wrappers must use synchronous handle mode for CreatePipe handles."
Require-FileNotContains "Services\WslcCliTerminalService.cs" "isAsync:\s*true" "ConPTY CreatePipe handles are not overlapped handles and must not be wrapped as async FileStreams."
Require-FileContains "Services\WslcCliTerminalService.cs" "ArgumentList\.Add" "External terminal launch must use ProcessStartInfo.ArgumentList instead of hand-quoted command strings."
Require-FileContains "Services\WslcCliTerminalService.cs" '"new-tab"' "Windows Terminal launch must explicitly open a new tab command."
Require-FileContains "Services\AppServices.cs" "Terminals" "AppServices must expose the terminal service."

Require-FileContains "Pages\ContainersPage.xaml" "ConnectTerminal" "Terminal pane must expose a connect action."
Require-FileContains "Pages\ContainersPage.xaml" "DisconnectTerminal" "Terminal pane must expose a disconnect action."
Require-FileContains "Pages\ContainersPage.xaml" "TerminalInputLine" "Terminal pane must expose an interactive input line."
Require-FileContains "Pages\ContainersPage.xaml" "TerminalTranscript" "Terminal pane must expose a terminal transcript viewport."
Require-FileContains "Pages\ContainersPage.xaml.cs" "TerminalInputLine_KeyDown" "Terminal input line must send input on Enter."

Require-FileNotContains "Pages\ContainersPage.xaml" "TerminalCommandInput" "Terminal command-runner input must be removed."
Require-FileNotContains "Pages\ContainersPage.xaml" "TerminalStandardInput" "Terminal standard input box must be removed."
Require-FileNotContains "Pages\ContainersPage.xaml" "RunTerminalCommand" "Terminal run command must be removed from UI."
Require-FileNotContains "ViewModels\ContainersViewModel.cs" "RunTerminalCommand" "RunTerminalCommand must be removed from the containers view model."
Require-FileNotContains "ViewModels\ContainersViewModel.cs" "\bTerminalCommand\b" "TerminalCommand must be removed from the containers view model."
Require-FileNotContains "ViewModels\ContainersViewModel.cs" "\bTerminalInput\b" "TerminalInput must be replaced by TerminalInputLine."

Write-Output "INTERACTIVE_TERMINAL_SOURCE_OK"
