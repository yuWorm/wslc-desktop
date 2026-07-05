param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$DaemonPath = Join-Path $Root "src\wslcd\Program.cs"
$MatrixPath = Join-Path $Root "docs\DOCKER_API_COMPATIBILITY_MATRIX.md"
$VerifierPath = Join-Path $Root "tools\DockerApiCompatVerify\Program.cs"

function Assert-Contains {
    param([string]$Text, [string]$Pattern, [string]$Message)
    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

$daemon = Get-Content -Raw $DaemonPath
$matrix = Get-Content -Raw $MatrixPath
$verifier = Get-Content -Raw $VerifierPath

Assert-Contains $daemon 'images/search' "wslcd must expose Docker API /images/search as explicit unsupported."
Assert-Contains $daemon 'DockerImageSearchUnsupported' "wslcd must route image search to a named unsupported handler."
Assert-Contains $matrix '\| GET \| `/images/search` \|' "Compatibility matrix must document /images/search."
Assert-Contains $matrix '\| GET \| `/images/search` \|[^\r\n]*Unsupported' "Compatibility matrix must mark /images/search unsupported."
Assert-Contains $verifier '/v1\.54/images/search' "DockerApiCompatVerify resources mode must cover /images/search unsupported."
Assert-Contains $verifier 'Image search unsupported' "DockerApiCompatVerify must assert image search unsupported status."
Assert-Contains $verifier 'PostJsonTextAsync' "DockerApiCompatVerify resources mode must read /images/create as a Docker progress stream."
Assert-Contains $verifier 'newline-delimited JSON progress messages' "DockerApiCompatVerify must require Docker-style newline-delimited image pull progress."
Assert-Contains $verifier 'image tag hello-world:latest' "DockerApiCompatVerify resources mode must create a throwaway image tag for image delete coverage."
Assert-Contains $verifier '/v1\.54/images/.+\?force=1&noprune=1' "DockerApiCompatVerify resources mode must delete the throwaway image tag through the Docker API."
Assert-Contains $verifier 'RequireImageAbsent' "DockerApiCompatVerify resources mode must assert the throwaway image tag was removed."
Assert-Contains $verifier 'RequireImagePresent\("hello-world", "latest"' "DockerApiCompatVerify resources mode must assert the source image remains after deleting the throwaway tag."

Write-Host "PHASE15_SOURCE_OK"
