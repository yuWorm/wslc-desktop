param()

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ProviderPath = Join-Path $Root "src\WslcDesktop.Runtime\Providers\DockerApi\DockerApiProvider.cs"
$OptionsPath = Join-Path $Root "src\WslcDesktop.Runtime\Providers\DockerApi\DockerApiProviderOptions.cs"
$ClientFactoryPath = Join-Path $Root "src\WslcDesktop.Runtime\Providers\DockerApi\DockerApiHttpClientFactory.cs"
$ContractsPath = Join-Path $Root "src\WslcDesktop.Contracts\WslcdContracts.cs"
$DaemonPath = Join-Path $Root "src\wslcd\Program.cs"
$SettingsXamlPath = Join-Path $Root "Pages\SettingsPage.xaml"
$SettingsViewModelPath = Join-Path $Root "ViewModels\SettingsViewModel.cs"
$ModelsPath = Join-Path $Root "Models\WslcDomainModels.cs"
$SettingsServicePath = Join-Path $Root "Services\FileAppSettingsService.cs"
$VerifierPath = Join-Path $Root "tools\WslcdSmokeVerify\Program.cs"
$MatrixPath = Join-Path $Root "docs\RUNTIME_PROVIDER_MATRIX.md"

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

Assert-Path $ProviderPath "Phase 19 must add DockerApiProvider."
Assert-Path $OptionsPath "Phase 19 must add DockerApiProviderOptions."
Assert-Path $ClientFactoryPath "Phase 19 must add DockerApiHttpClientFactory."
Assert-Path $MatrixPath "Phase 19 must document docs\RUNTIME_PROVIDER_MATRIX.md."

$provider = Get-Content -Raw $ProviderPath
$options = Get-Content -Raw $OptionsPath
$factory = Get-Content -Raw $ClientFactoryPath
$contracts = Get-Content -Raw $ContractsPath
$daemon = Get-Content -Raw $DaemonPath
$settingsXaml = Get-Content -Raw $SettingsXamlPath
$settingsViewModel = Get-Content -Raw $SettingsViewModelPath
$models = Get-Content -Raw $ModelsPath
$settingsService = Get-Content -Raw $SettingsServicePath
$verifier = Get-Content -Raw $VerifierPath
$matrix = Get-Content -Raw $MatrixPath

Assert-Contains $provider "class\s+DockerApiProvider\s*:\s*IContainerRuntimeProvider" "DockerApiProvider must implement IContainerRuntimeProvider."
Assert-Contains $provider "ListContainersAsync" "DockerApiProvider must implement container listing."
Assert-Contains $provider "ListImagesAsync" "DockerApiProvider must implement image listing."
Assert-Contains $provider "CreateContainerAsync" "DockerApiProvider must implement container create."
Assert-Contains $provider "StartContainerAsync" "DockerApiProvider must implement container start."
Assert-Contains $provider "StopContainerAsync" "DockerApiProvider must implement container stop."
Assert-Contains $provider "RemoveContainerAsync" "DockerApiProvider must implement container remove."
Assert-Contains $provider "com\.wslc-desktop\.provider" "DockerApiProvider must label created resources with migration-safe provider metadata."

Assert-Contains $options "DockerApiProviderOptions" "DockerApiProviderOptions must exist."
Assert-Contains $options "AllowTcp" "TCP Docker hosts must require an explicit AllowTcp option."
Assert-Contains $options "npipe:////./pipe" "Named-pipe Docker host support must be documented in options."
Assert-Contains $factory "NamedPipeClientStream" "DockerApiProvider must support Windows named-pipe Docker hosts."
Assert-Contains $factory "SocketsHttpHandler" "DockerApiProvider must use SocketsHttpHandler for named-pipe transport."
Assert-Contains $factory "ConnectCallback" "DockerApiProvider must connect named pipes through ConnectCallback."

Assert-Contains $contracts "RuntimeProviderSelection" "Contracts must include persisted runtime provider selection."
Assert-Contains $contracts "ProviderPreviewResponse" "Contracts must include provider preview response."
Assert-Contains $daemon "CreateRuntimeProvider" "wslcd must create the active runtime provider from options."
Assert-Contains $daemon "DockerApiProvider" "wslcd must be able to select DockerApiProvider."
Assert-Contains $daemon "/v1/providers/preview" "wslcd must expose provider preview for Settings."

Assert-Contains $models "RuntimeProviderSelection" "App settings snapshot must carry provider selection."
Assert-Contains $models "DockerApiHost" "App settings snapshot must carry Docker API host."
Assert-Contains $models "AllowTcpDockerApi" "App settings snapshot must carry explicit TCP consent."
Assert-Contains $settingsService "RuntimeProvider" "FileAppSettingsService must persist provider selection."
Assert-Contains $settingsService "DockerApiHost" "FileAppSettingsService must persist Docker API host."
Assert-Contains $settingsService "AllowTcpDockerApi" "FileAppSettingsService must persist TCP consent."

Assert-Contains $settingsViewModel "ProviderPreview" "SettingsViewModel must expose provider capability preview."
Assert-Contains $settingsViewModel "TestProviderCommand" "SettingsViewModel must expose a read-only provider connection test."
Assert-Contains $settingsXaml "CmbRuntimeProvider" "Settings page must expose provider selection."
Assert-Contains $settingsXaml "TxtDockerApiHost" "Settings page must expose Docker API host input."
Assert-Contains $settingsXaml "TglAllowTcpDockerApi" "Settings page must expose TCP security confirmation."
Assert-Contains $settingsXaml "BtnTestProvider" "Settings page must expose provider test action."

Assert-Contains $verifier "provider" "WslcdSmokeVerify must include provider mode."
Assert-Contains $verifier "docker-api" "WslcdSmokeVerify must include docker-api provider validation."
Assert-Contains $verifier "WSLCD_SMOKE_DAEMON_PATH" "WslcdSmokeVerify provider mode must support a prebuilt daemon path so WDAC-blocked Debug DLLs do not block provider verification."
Assert-Contains $verifier "VerifyRuntimeAsync\(cancellationToken\)" "WslcdSmokeVerify provider mode must verify that the default WSLC provider still reports CLI-parity resources after the Docker API provider preview."
Assert-Contains $verifier "WSLCD_RUNTIME_PROVIDER" "WslcdSmokeVerify provider mode must start a daemon with the Docker API provider activated."
Assert-Contains $verifier "VerifyDockerApiProviderDaemonAsync" "WslcdSmokeVerify provider mode must validate active DockerApiProvider read paths, not only Settings preview."
Assert-Contains $matrix "DockerApiProvider" "Runtime provider matrix must document DockerApiProvider."
Assert-Contains $matrix "TCP" "Runtime provider matrix must document TCP security behavior."
Assert-Contains $matrix "Named pipe" "Runtime provider matrix must document named pipe behavior."

Write-Host "PHASE19_SOURCE_OK"
