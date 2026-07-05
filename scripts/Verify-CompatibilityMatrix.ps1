param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$MatrixPath = Join-Path $Root "docs\DOCKER_API_COMPATIBILITY_MATRIX.md"

if (-not (Test-Path $MatrixPath)) {
    throw "Compatibility matrix is missing: $MatrixPath"
}

$failures = New-Object System.Collections.Generic.List[string]
$lines = Get-Content $MatrixPath

foreach ($line in $lines) {
    if (-not $line.StartsWith("|")) {
        continue
    }

    if ($line -match "^\|\s*---") {
        continue
    }

    $cells = $line.Trim("|").Split("|") | ForEach-Object { $_.Trim() }
    if ($cells.Count -lt 7 -or $cells[0] -eq "Method") {
        continue
    }

    $method = $cells[0]
    $endpoint = $cells[1]
    $status = $cells[3]
    $verifier = $cells[6]

    if ($status -eq "Supported" -and (
        [string]::IsNullOrWhiteSpace($verifier) -or
        $verifier -match "Manual|TBD|Not Started|until a throwaway")) {
        $failures.Add("$method $endpoint is Supported without concrete verifier coverage.")
    }
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "COMPATIBILITY_MATRIX_OK"
