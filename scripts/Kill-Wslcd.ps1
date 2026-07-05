[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

function Add-TargetProcess {
    param(
        [hashtable]$Targets,
        [int]$ProcessId,
        [string]$Name,
        [string]$Reason,
        [string]$CommandLine
    )

    if ($ProcessId -le 0 -or $Targets.ContainsKey($ProcessId)) {
        return
    }

    $Targets[$ProcessId] = [pscustomobject]@{
        ProcessId = $ProcessId
        Name = $Name
        Reason = $Reason
        CommandLine = $CommandLine
    }
}

$targets = @{}
$processNames = @("wslcd-desktop", "wslcd")
$executableNames = @("wslcd-desktop.exe", "wslcd.exe")
$dllPattern = "(^|[\\/\s])(wslcd-desktop|wslcd)\.dll(\s|$)"

foreach ($processName in $processNames) {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | ForEach-Object {
        Add-TargetProcess `
            -Targets $targets `
            -ProcessId $_.Id `
            -Name $_.ProcessName `
            -Reason "process name is $processName" `
            -CommandLine ""
    }
}

$originalWhatIfPreference = $WhatIfPreference

try {
    $WhatIfPreference = $false

    Get-CimInstance Win32_Process -ErrorAction Stop |
        Where-Object {
            ($executableNames -contains $_.Name) -or
            ($_.CommandLine -match $dllPattern)
        } |
        ForEach-Object {
            $reason = if ($executableNames -contains $_.Name) {
                "process name is $($_.Name)"
            }
            else {
                "command line contains wslcd-desktop.dll or legacy wslcd.dll"
            }

            Add-TargetProcess `
                -Targets $targets `
                -ProcessId ([int]$_.ProcessId) `
                -Name $_.Name `
                -Reason $reason `
                -CommandLine ([string]$_.CommandLine)
        }
}
catch {
    if (-not $Quiet) {
        Write-Warning "Could not inspect process command lines. Run from an elevated PowerShell if you also need to catch dotnet-hosted wslcd-desktop.dll or legacy wslcd.dll processes. $($_.Exception.Message)"
    }
}
finally {
    $WhatIfPreference = $originalWhatIfPreference
}

if ($targets.Count -eq 0) {
    if (-not $Quiet) {
        Write-Host "No wslcd processes found."
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
