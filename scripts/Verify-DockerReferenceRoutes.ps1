$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$programPath = Join-Path $root "src\wslcd\Program.cs"
$verifierPath = Join-Path $root "tools\DockerApiCompatVerify\Program.cs"

if (!(Test-Path $programPath)) {
    throw "Missing src\wslcd\Program.cs"
}

if (!(Test-Path $verifierPath)) {
    throw "Missing tools\DockerApiCompatVerify\Program.cs"
}

$program = Get-Content -Raw $programPath
$verifier = Get-Content -Raw $verifierPath

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

Require-Contains $program 'MapGet\("/images/\{\*\*name\}"' "Docker image GET routes must use a catch-all name parameter."
Require-Contains $program 'MapGet\("/v\{apiVersion\}/images/\{\*\*name\}"' "Versioned Docker image GET routes must use a catch-all name parameter."
Require-Contains $program 'MapDelete\("/images/\{\*\*name\}"' "Docker image DELETE routes must use a catch-all name parameter."
Require-Contains $program 'MapDelete\("/v\{apiVersion\}/images/\{\*\*name\}"' "Versioned Docker image DELETE routes must use a catch-all name parameter."
Require-Contains $program 'MapDelete\("/v1/images/\{\*\*idOrName\}"' "Native image delete route must use a catch-all id/name parameter."
Require-Contains $program 'DockerImageGet' "Docker image catch-all routes must dispatch through DockerImageGet."
Require-Contains $program 'Uri\.UnescapeDataString\(name\)' "Docker image references must be URL-decoded before provider lookup."

Require-NotContains $program 'Map(Get|Delete)\("/images/\{name\}' "Docker image routes must not use single-segment {name} parameters."
Require-NotContains $program 'Map(Get|Delete)\("/v\{apiVersion\}/images/\{name\}' "Versioned Docker image routes must not use single-segment {name} parameters."

Require-Contains $verifier 'wslcd/scope-fixture' "Docker API verifier must cover scoped image references."
Require-Contains $verifier 'localhost:5000/wslcd/registry-fixture' "Docker API verifier must cover registry-host image references."

Write-Output "DOCKER_REFERENCE_ROUTES_OK"
