param(
    [string]$CertificatePath = "",

    [string]$Password = "password",

    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$StoreLocation = "LocalMachine"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    $CertificatePath = Join-Path $Root "artifacts\wslc-desktop-devcert.pfx"
}

if (-not (Test-Path $CertificatePath)) {
    throw "Development certificate was not found: $CertificatePath"
}

$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
$pfx = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath, $Password)
$existing = Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $pfx.Thumbprint | Select-Object -First 1
$cert = if ($null -ne $existing) {
    $existing
}
else {
    Import-PfxCertificate -FilePath $CertificatePath -CertStoreLocation Cert:\CurrentUser\My -Password $securePassword
}

$stores = @(
    [System.Security.Cryptography.X509Certificates.X509Store]::new("Root", $StoreLocation),
    [System.Security.Cryptography.X509Certificates.X509Store]::new("TrustedPublisher", $StoreLocation)
)

try {
    foreach ($store in $stores) {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        if (-not ($store.Certificates | Where-Object Thumbprint -eq $cert.Thumbprint)) {
            $store.Add($cert)
        }
    }
}
finally {
    foreach ($store in $stores) {
        $store.Close()
    }
}

"DEV_CERT_IMPORTED_$($StoreLocation.ToUpperInvariant()) $($cert.Thumbprint)"
