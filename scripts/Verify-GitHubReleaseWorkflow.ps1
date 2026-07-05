$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $root ".github\workflows\release.yml"
$gitignorePath = Join-Path $root ".gitignore"
$fullReleasePath = Join-Path $root "scripts\Verify-FullRelease.ps1"
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
$fullRelease = Get-Content -Raw $fullReleasePath

foreach ($path in @($releaseNotesPath, $dockerMatrixPath, $runtimeMatrixPath)) {
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
Assert-Contains $workflow 'microsoft/setup-WinAppCli@v0\.1' "Release workflow must install winapp CLI."
Assert-Contains $workflow 'Verify-FullRelease\.ps1' "Release workflow must reuse the local full release verifier."
Assert-Contains $workflow '-SkipPackage' "Release workflow must separate verification from final packaging."
Assert-Contains $workflow '-AllowKnownLocalBlockers' "Release workflow must tolerate unavailable local WSLC/Docker runtime gates in CI."
Assert-Contains $workflow 'Package\.appxmanifest' "Release workflow must stamp Package.appxmanifest from the tag."
Assert-Contains $workflow 'WINDOWS_SIGNING_CERT_BASE64' "Release workflow must support a repository signing certificate secret."
Assert-Contains $workflow 'WINDOWS_SIGNING_CERT_PASSWORD' "Release workflow must support a repository signing certificate password secret."
Assert-Contains $workflow 'WINDOWS_PUBLISHER' "Release workflow must support an optional publisher override."
Assert-Contains $workflow 'winapp package' "Release workflow must package MSIX artifacts."
Assert-Contains $workflow '--cert-password' "Release workflow must pass the certificate password with winapp package's supported --cert-password option."
Assert-Contains $workflow '--output' "Release workflow must write MSIX packages to an explicit output path."
Assert-NotContains $workflow 'winapp package[^\r\n]*--password' "Release workflow must not pass unsupported --password to winapp package."
Assert-Contains $workflow 'Get-FileHash' "Release workflow must generate checksums."
Assert-Contains $workflow 'actions/upload-artifact@v4' "Release workflow must upload per-platform artifacts."
Assert-Contains $workflow 'actions/download-artifact@v4' "Release workflow must collect matrix artifacts before release."
Assert-Contains $workflow 'softprops/action-gh-release@v2' "Release workflow must publish artifacts to GitHub Releases."
Assert-Contains $workflow 'prerelease:\s*\$\{\{\s*contains\(github\.ref_name,\s*''-''\)\s*\}\}' "Release workflow must mark pre-release tags such as beta builds as GitHub prereleases."
Assert-Contains $workflow 'docs/RELEASE_NOTES\.md' "Release workflow must use release notes from docs."

Assert-Contains $fullRelease 'EnableDevelopmentCodeSigning=false' "Full release verifier must disable Debug loose-layout development signing in CI."
Assert-Contains $fullRelease 'RuntimeIdentifier=\$ReleaseRid' "Full release verifier must build the selected release RID."
Assert-Contains $fullRelease 'PublishReadyToRun=false' "Full release verifier must avoid ReadyToRun restore mismatches on GitHub-hosted runners."
Assert-Contains $fullRelease 'Invoke-ProcessStep "dotnet build --no-restore"' "Full release verifier must check native dotnet build exit codes."
Assert-Contains $fullRelease 'Invoke-ProcessStep "Release build"' "Full release verifier must check native Release build exit codes."

Write-Host "GITHUB_RELEASE_WORKFLOW_OK"
