param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,

    [string]$CertificatePath = "",

    [string]$Password = "password"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    $CertificatePath = Join-Path $Root "artifacts\wslc-desktop-devcert.pfx"
}

if (-not (Test-Path $CertificatePath)) {
    throw "Development certificate was not found: $CertificatePath"
}

$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath, $Password)
$signed = 0
$alreadyValid = 0
$skipped = 0
$total = 0

foreach ($item in $Path) {
    if (-not (Test-Path $item)) {
        throw "Signing path was not found: $item"
    }

    $files = Get-ChildItem -Path $item -File -Include *.exe,*.dll -Recurse |
        Where-Object { $_.FullName -notmatch "\\publish\\" }

    foreach ($file in $files) {
        $total++
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        if ($signature.Status -eq "Valid") {
            $alreadyValid++
            continue
        }

        $result = $null
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                $result = Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert
                if ($result.Status -eq "Valid") {
                    break
                }
            }
            catch {
                if ($attempt -eq 5) {
                    throw
                }
            }

            Start-Sleep -Milliseconds (200 * $attempt)
        }

        if ($result.Status -ne "Valid") {
            throw "Failed to sign $($file.FullName): $($result.Status) $($result.StatusMessage)"
        }

        $signed++
    }
}

"DEVELOPMENT_OUTPUT_SIGNED signed=$signed alreadyValid=$alreadyValid skipped=$skipped total=$total"
