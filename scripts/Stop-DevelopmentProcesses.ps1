[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
$RootPrefix = $Root.TrimEnd("\") + "\"
$TargetProcessNames = @("wslc-desktop", "wslcd-desktop")
$TargetExecutableNames = @("wslc-desktop.exe", "wslcd-desktop.exe")
$TargetNamePattern = "(^|[\\/\s])(wslc-desktop|wslcd-desktop)(\.exe|\.dll)?(\s|$|`")"

function Test-IsUnderRepository {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    try {
        $fullPath = [System.IO.Path]::GetFullPath($Path.Trim('"'))
        return $fullPath.StartsWith($RootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function Test-IsRepositoryCommandLine {
    param([string]$CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) {
        return $false
    }

    return (
        $CommandLine.IndexOf($Root, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $CommandLine -match $TargetNamePattern
    )
}

function Add-TargetProcess {
    param(
        [hashtable]$Targets,
        [int]$ProcessId,
        [string]$Name,
        [string]$Reason,
        [string]$Path,
        [string]$CommandLine
    )

    if ($ProcessId -le 0 -or $Targets.ContainsKey($ProcessId)) {
        return
    }

    $Targets[$ProcessId] = [pscustomobject]@{
        ProcessId = $ProcessId
        Name = $Name
        Reason = $Reason
        Path = $Path
        CommandLine = $CommandLine
    }
}

$targets = @{}

foreach ($processName in $TargetProcessNames) {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | ForEach-Object {
        if (Test-IsUnderRepository $_.Path) {
            Add-TargetProcess `
                -Targets $targets `
                -ProcessId $_.Id `
                -Name $_.ProcessName `
                -Reason "executable path is under this repository" `
                -Path $_.Path `
                -CommandLine ""
        }
    }
}

$originalWhatIfPreference = $WhatIfPreference

try {
    $WhatIfPreference = $false

    Get-CimInstance Win32_Process -ErrorAction Stop |
        Where-Object {
            ($TargetExecutableNames -contains $_.Name -and (Test-IsUnderRepository $_.ExecutablePath)) -or
            (Test-IsRepositoryCommandLine $_.CommandLine)
        } |
        ForEach-Object {
            $reason = if (Test-IsUnderRepository $_.ExecutablePath) {
                "executable path is under this repository"
            }
            else {
                "command line references this repository loose layout"
            }

            Add-TargetProcess `
                -Targets $targets `
                -ProcessId ([int]$_.ProcessId) `
                -Name $_.Name `
                -Reason $reason `
                -Path ([string]$_.ExecutablePath) `
                -CommandLine ([string]$_.CommandLine)
        }
}
catch {
    if (-not $Quiet) {
        Write-Warning "Could not inspect process command lines. $($_.Exception.Message)"
    }
}
finally {
    $WhatIfPreference = $originalWhatIfPreference
}

if ($targets.Count -eq 0) {
    if (-not $Quiet) {
        Write-Host "No repository-owned development processes found."
    }

    exit 0
}

$hadFailures = $false

foreach ($target in ($targets.Values | Sort-Object ProcessId)) {
    $description = "PID $($target.ProcessId) ($($target.Name)): $($target.Reason)"

    if ($PSCmdlet.ShouldProcess($description, "Stop-Process -Force")) {
        try {
            Stop-Process -Id $target.ProcessId -Force -ErrorAction Stop

            if (-not $Quiet) {
                Write-Host "Stopped $description"
            }
        }
        catch {
            $hadFailures = $true
            Write-Warning "Failed to stop $description. $($_.Exception.Message)"
        }
    }
}

if ($hadFailures) {
    exit 1
}
