param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ProviderPath = Join-Path $Root "src\WslcDesktop.Runtime\Providers\WslcCli\WslcCliProvider.cs"
$DaemonPath = Join-Path $Root "src\wslcd\Program.cs"
$DockerPipeProxyPath = Join-Path $Root "src\wslcd\DockerPipeProxy.cs"
$VerifierPath = Join-Path $Root "tools\DockerApiCompatVerify\Program.cs"

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

$provider = Get-Content -Raw $ProviderPath
$daemon = Get-Content -Raw $DaemonPath
$dockerPipeProxy = if (Test-Path $DockerPipeProxyPath) { Get-Content -Raw $DockerPipeProxyPath } else { "" }
$verifier = Get-Content -Raw $VerifierPath

Assert-Contains $provider "MaxExecSessionCount" "WslcCliProvider must bound retained exec sessions."
Assert-Contains $provider "CompletedExecRetention" "WslcCliProvider must define completed exec retention."
Assert-Contains $provider "PruneExecSessions" "WslcCliProvider must prune retained exec sessions."
Assert-Contains $provider "CreatedAt" "Exec sessions must track creation time."
Assert-Contains $provider "CompletedAt" "Exec sessions must track completion time."
Assert-Contains $provider "PruneExecSessions\(\);\s*_execSessions\[execId\]" "CreateExecAsync must prune before adding a new session."
Assert-Contains $provider "PruneExecSessions\(\);\s*return _execSessions" "InspectExecAsync must prune before inspecting sessions."

Assert-Contains $daemon "QueryBoolDefault\(context,\s*""stream"",\s*true\)" "Docker stats must default stream=true like the Docker Engine API."
Assert-Contains $daemon "QueryBool\(context,\s*""one-shot""\)" "Docker stats must parse the one-shot query."
Assert-Contains $daemon "StatsStreaming" "Docker stats must branch on provider StatsStreaming capability."
Assert-Contains $daemon "DockerError\(501" "Docker stats must return an explicit Docker-style unsupported error for unavailable streaming."
Assert-Contains $daemon "application/json" "Docker stats streaming path must declare JSON output."
Assert-Contains $daemon "IHttpUpgradeFeature" "Docker exec start must support Docker CLI HTTP connection upgrade."
Assert-Contains $daemon "UpgradeAsync" "Docker exec start must use Kestrel HTTP upgrade for Docker CLI exec."
Assert-Contains $daemon "StatusCodes\.Status101SwitchingProtocols" "Docker exec start must fall back to manual 101 switching-protocol response for named-pipe Docker CLI exec."
Assert-Contains $daemon "Connection" "Docker exec start must detect Docker CLI upgrade headers."
Assert-Contains $daemon "Upgrade" "Docker exec start must detect Docker CLI upgrade headers."
Assert-Contains $daemon "DockerPipeProxy" "Docker public named pipe must be fronted by a proxy so exec hijack can leave Kestrel's HTTP response pipeline."
Assert-Contains $daemon "InternalDockerPipeName" "wslcd must isolate the internal Kestrel Docker pipe from the public Docker CLI pipe."
Assert-Contains $dockerPipeProxy "NamedPipeServerStream" "DockerPipeProxy must own the public named pipe transport."
Assert-Contains $dockerPipeProxy "HandleExecStartUpgradeAsync" "DockerPipeProxy must directly handle Docker CLI exec-start upgrade requests."
Assert-Contains $dockerPipeProxy "HTTP/1.1 101 Switching Protocols" "DockerPipeProxy must write the Docker exec hijack 101 response itself."
Assert-Contains $dockerPipeProxy "ProxyHttpAsync" "DockerPipeProxy must forward non-hijack HTTP requests to the internal Kestrel pipe."

Assert-Contains $verifier "VerifyDockerCliStreamingAsync" "DockerApiCompatVerify must include a Docker CLI streaming smoke gate."
Assert-Contains $verifier 'containerName = "wslcd-cli-streaming-' "Docker CLI streaming gate must use a unique Phase 16 fixture container."
Assert-Contains $verifier '"-H", DockerHost' "Docker CLI streaming gate must target the WSLC Desktop Docker named pipe."
Assert-Contains $verifier '"logs", containerName' "Docker CLI streaming gate must verify docker logs through the named pipe."
Assert-Contains $verifier '"stats", "--no-stream"' "Docker CLI streaming gate must verify docker stats --no-stream through the named pipe."
Assert-Contains $verifier '"exec", containerName' "Docker CLI streaming gate must verify docker exec through the named pipe."
Assert-Contains $verifier "phase16-cli-log" "Docker CLI streaming gate must assert log output from the fixture container."
Assert-Contains $verifier "phase16-cli-exec" "Docker CLI streaming gate must assert exec output from the fixture container."

Write-Host "PHASE16_SOURCE_OK"
