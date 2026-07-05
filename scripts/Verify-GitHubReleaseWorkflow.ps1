$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $root ".github\workflows\release.yml"

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
Assert-Contains $workflow 'microsoft/setup-WinAppCli@v0\.1' "Release workflow must install winapp CLI."
Assert-Contains $workflow 'Verify-FullRelease\.ps1' "Release workflow must reuse the local full release verifier."
Assert-Contains $workflow '-SkipPackage' "Release workflow must separate verification from final packaging."
Assert-Contains $workflow '-AllowKnownLocalBlockers' "Release workflow must tolerate unavailable local WSLC/Docker runtime gates in CI."
Assert-Contains $workflow 'Package\.appxmanifest' "Release workflow must stamp Package.appxmanifest from the tag."
Assert-Contains $workflow 'WINDOWS_SIGNING_CERT_BASE64' "Release workflow must support a repository signing certificate secret."
Assert-Contains $workflow 'WINDOWS_SIGNING_CERT_PASSWORD' "Release workflow must support a repository signing certificate password secret."
Assert-Contains $workflow 'WINDOWS_PUBLISHER' "Release workflow must support an optional publisher override."
Assert-Contains $workflow 'winapp package' "Release workflow must package MSIX artifacts."
Assert-Contains $workflow 'Get-FileHash' "Release workflow must generate checksums."
Assert-Contains $workflow 'actions/upload-artifact@v4' "Release workflow must upload per-platform artifacts."
Assert-Contains $workflow 'actions/download-artifact@v4' "Release workflow must collect matrix artifacts before release."
Assert-Contains $workflow 'softprops/action-gh-release@v2' "Release workflow must publish artifacts to GitHub Releases."
Assert-Contains $workflow 'docs/RELEASE_NOTES\.md' "Release workflow must use release notes from docs."

Write-Host "GITHUB_RELEASE_WORKFLOW_OK"
