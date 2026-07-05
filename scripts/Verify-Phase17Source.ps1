param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$PlanPath = Join-Path $Root "docs\superpowers\plans\2026-07-05-wslcd-docker-api-compatibility.md"
$ComposeDocPath = Join-Path $Root "docs\COMPOSE_COMPATIBILITY.md"
$VerifierPath = Join-Path $Root "tools\DockerApiCompatVerify\Program.cs"
$FixturePath = Join-Path $Root "tools\DockerApiCompatVerify\fixtures\compose-basic\compose.yaml"
$ContractsPath = Join-Path $Root "src\WslcDesktop.Contracts\RuntimeContracts.cs"
$RuntimeProviderPath = Join-Path $Root "src\WslcDesktop.Runtime\IContainerRuntimeProvider.cs"
$WslcProviderPath = Join-Path $Root "src\WslcDesktop.Runtime\Providers\WslcCli\WslcCliProvider.cs"
$DaemonPath = Join-Path $Root "src\wslcd\Program.cs"

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

Assert-Path $ComposeDocPath "Phase 17 must create docs\COMPOSE_COMPATIBILITY.md."
Assert-Path $FixturePath "Phase 17 must create the compose-basic fixture."
Assert-Path $ContractsPath "Phase 17 must keep runtime contracts available."
Assert-Path $RuntimeProviderPath "Phase 17 must keep runtime provider contract available."
Assert-Path $WslcProviderPath "Phase 17 must keep WSLC provider available."
Assert-Path $DaemonPath "Phase 17 must keep wslcd routes available."

$plan = Get-Content -Raw $PlanPath
$doc = Get-Content -Raw $ComposeDocPath
$verifier = Get-Content -Raw $VerifierPath
$fixture = Get-Content -Raw $FixturePath
$contracts = Get-Content -Raw $ContractsPath
$runtimeProvider = Get-Content -Raw $RuntimeProviderPath
$wslcProvider = Get-Content -Raw $WslcProviderPath
$daemon = Get-Content -Raw $DaemonPath

Assert-Contains $plan "docker context create wslc-desktop --docker ""host=npipe:////./pipe/wslc-desktop-docker""" "Plan must document context creation."
Assert-Contains $doc "## Supported Service Keys" "Compose compatibility doc must list supported service keys."
Assert-Contains $doc "## Partial Service Keys" "Compose compatibility doc must list partial service keys."
Assert-Contains $doc "## Rejected Service Keys" "Compose compatibility doc must list rejected service keys."
foreach ($key in @("image", "command", "entrypoint", "environment", "ports", "volumes", "working_dir", "labels", "depends_on")) {
    Assert-Contains $doc "\b$key\b" "Compose compatibility doc must cover '$key'."
}
Assert-Contains $doc "network_mode:\s*default" "Compose compatibility doc must explain network_mode: default for Phase 17."
Assert-Contains $doc "npipe:////./pipe/wslc-desktop-docker" "Compose compatibility doc must include the WSLC Desktop Docker pipe."

Assert-Contains $verifier 'mode\.Equals\("context"' "DockerApiCompatVerify must accept context mode."
Assert-Contains $verifier 'mode\.Equals\("compose"' "DockerApiCompatVerify must accept compose mode."
Assert-Contains $verifier "VerifyContextAsync" "DockerApiCompatVerify must implement context validation."
Assert-Contains $verifier "VerifyComposeAsync" "DockerApiCompatVerify must implement compose validation."
Assert-Contains $verifier 'mode\.Equals\("docker-cli"' "DockerApiCompatVerify must expose a combined Docker CLI gate."
Assert-Contains $verifier "DOCKER_API_DOCKER_CLI_OK" "DockerApiCompatVerify docker-cli mode must print DOCKER_API_DOCKER_CLI_OK."
Assert-Contains $verifier "RunDockerComposeAsync" "DockerApiCompatVerify must support Docker Compose invocation."
Assert-Contains $verifier "docker-compose" "DockerApiCompatVerify must fall back to standalone docker-compose."
Assert-Contains $verifier "compose-basic" "DockerApiCompatVerify must use the compose-basic fixture."
Assert-Contains $verifier "docker" "DockerApiCompatVerify context/compose validation must call Docker CLI."

Assert-Contains $contracts "Labels" "Runtime contracts must carry labels for Compose-created containers and volumes."
Assert-Contains $runtimeProvider "CreateVolumeAsync\(string name, IReadOnlyDictionary<string, string>\? labels" "Runtime provider contract must allow volume labels."
Assert-Contains $wslcProvider "inspect" "WSLC provider must inspect containers to recover labels for Compose filters."
Assert-Contains $wslcProvider '"-l"' "WSLC provider must pass labels through to wslc create/volume create."
Assert-Contains $daemon "ReadFilterValues" "wslcd must parse Docker filter object syntax used by Compose."
Assert-Contains $daemon "MatchesLabelFilter" "wslcd must implement Docker label filters used by Compose."
Assert-Contains $daemon "DockerCreatedAt" "wslcd must return Compose-parseable Docker resource timestamps."

Assert-Contains $fixture "services:" "Compose fixture must define services."
Assert-Contains $fixture "depends_on:" "Compose fixture must exercise depends_on."
Assert-Contains $fixture "network_mode:\s*default" "Compose fixture must avoid user-defined network creation in Phase 17."
Assert-Contains $fixture "ports:" "Compose fixture must exercise ports."
Assert-Contains $fixture "volumes:" "Compose fixture must exercise volumes."
Assert-Contains $fixture "labels:" "Compose fixture must exercise labels."

Write-Host "PHASE17_SOURCE_OK"
