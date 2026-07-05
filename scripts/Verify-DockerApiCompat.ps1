param(
    [ValidateSet("streaming")]
    [string]$Mode = "streaming"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$NativePipe = "wslc-desktop"
$DockerPipe = "wslc-desktop-docker"
$DaemonPath = Join-Path $Root "artifacts\wslcd-singlefile\wslcd-desktop.exe"
$DaemonDllPath = Join-Path $Root "src\wslcd\bin\Debug\net10.0\wslcd-desktop.dll"
$LegacyDaemonPath = Join-Path $Root "artifacts\wslcd-singlefile\wslcd.exe"
$LegacyDaemonDllPath = Join-Path $Root "src\wslcd\bin\Debug\net10.0\wslcd.dll"
$TracePath = Join-Path $Root "artifacts\docker-api-compat-script\trace.log"
$DaemonStdoutPath = Join-Path $Root "artifacts\docker-api-compat-script\wslcd.stdout.log"
$DaemonStderrPath = Join-Path $Root "artifacts\docker-api-compat-script\wslcd.stderr.log"

function Trace-Step {
    param([string]$Message)
    $line = "{0:o} {1}" -f [DateTimeOffset]::UtcNow, $Message
    Add-Content -Path $TracePath -Value $line
    Write-Host $Message
}

function Start-WslcdDaemon {
    if (Test-Path $DaemonDllPath) {
        Trace-Step "START_DAEMON_DOTNET"
        $process = Start-Process -FilePath "dotnet" -ArgumentList @($DaemonDllPath) -WorkingDirectory $Root -PassThru -WindowStyle Hidden -RedirectStandardOutput $DaemonStdoutPath -RedirectStandardError $DaemonStderrPath
        Trace-Step "START_DAEMON_DOTNET_OK pid=$($process.Id)"
        return $process
    }

    if (Test-Path $LegacyDaemonDllPath) {
        Trace-Step "START_LEGACY_DAEMON_DOTNET"
        $process = Start-Process -FilePath "dotnet" -ArgumentList @($LegacyDaemonDllPath) -WorkingDirectory $Root -PassThru -WindowStyle Hidden -RedirectStandardOutput $DaemonStdoutPath -RedirectStandardError $DaemonStderrPath
        Trace-Step "START_LEGACY_DAEMON_DOTNET_OK pid=$($process.Id)"
        return $process
    }

    if (Test-Path $DaemonPath) {
        try {
            Trace-Step "START_DAEMON_SINGLEFILE"
            $process = Start-Process -FilePath $DaemonPath -WorkingDirectory $Root -PassThru -WindowStyle Hidden -RedirectStandardOutput $DaemonStdoutPath -RedirectStandardError $DaemonStderrPath
            Trace-Step "START_DAEMON_SINGLEFILE_OK pid=$($process.Id)"
            return $process
        }
        catch {
            Trace-Step "START_DAEMON_SINGLEFILE_BLOCKED $($_.Exception.Message)"
        }
    }

    if (Test-Path $LegacyDaemonPath) {
        try {
            Trace-Step "START_LEGACY_DAEMON_SINGLEFILE"
            $process = Start-Process -FilePath $LegacyDaemonPath -WorkingDirectory $Root -PassThru -WindowStyle Hidden -RedirectStandardOutput $DaemonStdoutPath -RedirectStandardError $DaemonStderrPath
            Trace-Step "START_LEGACY_DAEMON_SINGLEFILE_OK pid=$($process.Id)"
            return $process
        }
        catch {
            Trace-Step "START_LEGACY_DAEMON_SINGLEFILE_BLOCKED $($_.Exception.Message)"
        }
    }

    throw "Missing daemon executable and daemon DLL: $DaemonPath ; $DaemonDllPath ; $LegacyDaemonPath ; $LegacyDaemonDllPath"
}

function ConvertTo-HttpBody {
    param([byte[]]$ResponseBytes)

    $text = [Text.Encoding]::ASCII.GetString($ResponseBytes)
    $separator = $text.IndexOf("`r`n`r`n", [StringComparison]::Ordinal)
    if ($separator -lt 0) {
        throw "Invalid HTTP response without header separator."
    }

    $headerText = $text.Substring(0, $separator)
    $bodyOffset = [Text.Encoding]::ASCII.GetByteCount($text.Substring(0, $separator + 4))
    $statusLine = $headerText.Split("`r`n")[0]
    $status = [int]($statusLine.Split(" ")[1])
    $isChunked = $headerText -match "(?im)^Transfer-Encoding:\s*chunked\s*$"

    if (-not $isChunked) {
        $body = [byte[]]::new($ResponseBytes.Length - $bodyOffset)
        [Array]::Copy($ResponseBytes, $bodyOffset, $body, 0, $body.Length)
        return [pscustomobject]@{ Status = $status; BodyBytes = $body; BodyText = [Text.Encoding]::UTF8.GetString($body) }
    }

    $chunkBytes = [byte[]]::new($ResponseBytes.Length - $bodyOffset)
    [Array]::Copy($ResponseBytes, $bodyOffset, $chunkBytes, 0, $chunkBytes.Length)
    $chunkText = [Text.Encoding]::ASCII.GetString($chunkBytes)
    $out = [IO.MemoryStream]::new()
    $position = 0
    while ($true) {
        $lineEnd = $chunkText.IndexOf("`r`n", $position, [StringComparison]::Ordinal)
        if ($lineEnd -lt 0) { break }
        $line = $chunkText.Substring($position, $lineEnd - $position)
        $sizeText = ($line -split ";")[0]
        $size = [Convert]::ToInt32($sizeText, 16)
        if ($size -eq 0) { break }
        $position = $lineEnd + 2
        if ($position + $size -gt $chunkBytes.Length) { throw "Incomplete chunked body." }
        $out.Write($chunkBytes, $position, $size)
        $position += $size + 2
    }

    $bodyBytes = $out.ToArray()
    return [pscustomobject]@{ Status = $status; BodyBytes = $bodyBytes; BodyText = [Text.Encoding]::UTF8.GetString($bodyBytes) }
}

function Test-HttpResponseComplete {
    param([byte[]]$ResponseBytes)

    if ($ResponseBytes.Length -eq 0) {
        return $false
    }

    $text = [Text.Encoding]::ASCII.GetString($ResponseBytes)
    $separator = $text.IndexOf("`r`n`r`n", [StringComparison]::Ordinal)
    if ($separator -lt 0) {
        return $false
    }

    $headerText = $text.Substring(0, $separator)
    $bodyOffset = [Text.Encoding]::ASCII.GetByteCount($text.Substring(0, $separator + 4))
    $contentLengthMatch = [regex]::Match($headerText, "(?im)^Content-Length:\s*(\d+)\s*$")
    if ($contentLengthMatch.Success) {
        $length = [int]$contentLengthMatch.Groups[1].Value
        return $ResponseBytes.Length -ge ($bodyOffset + $length)
    }

    $isChunked = $headerText -match "(?im)^Transfer-Encoding:\s*chunked\s*$"
    if (-not $isChunked) {
        return $false
    }

    $chunkBytes = [byte[]]::new($ResponseBytes.Length - $bodyOffset)
    [Array]::Copy($ResponseBytes, $bodyOffset, $chunkBytes, 0, $chunkBytes.Length)
    $chunkText = [Text.Encoding]::ASCII.GetString($chunkBytes)
    $position = 0
    while ($true) {
        $lineEnd = $chunkText.IndexOf("`r`n", $position, [StringComparison]::Ordinal)
        if ($lineEnd -lt 0) { return $false }
        $line = $chunkText.Substring($position, $lineEnd - $position)
        $sizeText = ($line -split ";")[0]
        $size = 0
        if (-not [int]::TryParse($sizeText, [Globalization.NumberStyles]::HexNumber, [Globalization.CultureInfo]::InvariantCulture, [ref]$size)) {
            return $false
        }

        $position = $lineEnd + 2
        if ($size -eq 0) {
            return $chunkText.Length -ge ($position + 2)
        }

        if ($position + $size + 2 -gt $chunkBytes.Length) {
            return $false
        }

        $position += $size + 2
    }
}

function Invoke-PipeHttp {
    param(
        [string]$PipeName,
        [string]$Method,
        [string]$Path,
        [object]$Body = $null
    )

    $pipe = [IO.Pipes.NamedPipeClientStream]::new(".", $PipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $pipe.Connect(5000)
    try {
        $bodyText = ""
        if ($null -ne $Body) {
            $bodyText = $Body | ConvertTo-Json -Depth 20 -Compress
        }

        $bodyBytes = [Text.Encoding]::UTF8.GetBytes($bodyText)
        $request = "$Method $Path HTTP/1.1`r`nHost: localhost`r`nConnection: close`r`n"
        if ($bodyBytes.Length -gt 0) {
            $request += "Content-Type: application/json`r`nContent-Length: $($bodyBytes.Length)`r`n"
        }
        $request += "`r`n"
        $headerBytes = [Text.Encoding]::ASCII.GetBytes($request)
        $pipe.Write($headerBytes, 0, $headerBytes.Length)
        if ($bodyBytes.Length -gt 0) {
            $pipe.Write($bodyBytes, 0, $bodyBytes.Length)
        }
        $pipe.Flush()

        $buffer = [byte[]]::new(4096)
        $memory = [IO.MemoryStream]::new()
        while ($true) {
            $readTask = $pipe.ReadAsync($buffer, 0, $buffer.Length)
            try {
                $waitTask = $readTask.AsTask()
            }
            catch {
                $waitTask = $readTask
            }

            if (-not $waitTask.Wait(15000)) {
                throw "Timed out reading HTTP response for $Method $Path."
            }

            $read = $waitTask.Result
            if ($read -le 0) { break }
            $memory.Write($buffer, 0, $read)
            if (Test-HttpResponseComplete $memory.ToArray()) {
                break
            }
        }

        ConvertTo-HttpBody $memory.ToArray()
    }
    finally {
        $pipe.Dispose()
    }
}

function Assert-Status {
    param($Response, [int]$Status, [string]$Message)
    if ($Response.Status -ne $Status) {
        throw "$Message returned HTTP $($Response.Status), expected $Status. Body: $($Response.BodyText)"
    }
}

function ConvertFrom-DockerRawStream {
    param([byte[]]$BodyBytes)

    $builder = [Text.StringBuilder]::new()
    $position = 0
    while ($position + 8 -le $BodyBytes.Length) {
        $length = (([int]$BodyBytes[$position + 4]) -shl 24) -bor
            (([int]$BodyBytes[$position + 5]) -shl 16) -bor
            (([int]$BodyBytes[$position + 6]) -shl 8) -bor
            ([int]$BodyBytes[$position + 7])
        $position += 8
        if ($length -lt 0 -or $position + $length -gt $BodyBytes.Length) {
            throw "Invalid Docker raw-stream frame length."
        }

        if ($length -gt 0) {
            [void]$builder.Append([Text.Encoding]::UTF8.GetString($BodyBytes, $position, $length))
        }

        $position += $length
    }

    return $builder.ToString()
}

$env:WSLCD_LOG_DIRECTORY = Join-Path $Root "artifacts\docker-api-compat-script\Diagnostics"
$env:WSLCD_ENABLE_TEST_SHUTDOWN = "1"
New-Item -ItemType Directory -Force -Path $env:WSLCD_LOG_DIRECTORY | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $TracePath) | Out-Null
Remove-Item -Force -ErrorAction SilentlyContinue $TracePath
Remove-Item -Force -ErrorAction SilentlyContinue $DaemonStdoutPath
Remove-Item -Force -ErrorAction SilentlyContinue $DaemonStderrPath
Trace-Step "VERIFY_START mode=$Mode"

$daemon = Start-WslcdDaemon
$containerId = $null
try {
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        $daemon.Refresh()
        if ($daemon.HasExited) {
            $stderr = if (Test-Path $DaemonStderrPath) { Get-Content -Raw $DaemonStderrPath } else { "" }
            $stdout = if (Test-Path $DaemonStdoutPath) { Get-Content -Raw $DaemonStdoutPath } else { "" }
            throw "wslcd exited before ready with code $($daemon.ExitCode). STDOUT: $stdout STDERR: $stderr"
        }

        try {
            Trace-Step "PING_ATTEMPT $i"
            $ping = Invoke-PipeHttp -PipeName $DockerPipe -Method GET -Path "/_ping"
            Trace-Step "PING_STATUS $($ping.Status)"
            if ($ping.Status -eq 200) { $ready = $true; break }
        } catch {
            Trace-Step "PING_ERROR $($_.Exception.Message)"
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $ready) { throw "wslcd did not become ready." }
    Trace-Step "WSLCD_READY"

    if ($Mode -eq "streaming") {
        $name = "wslcd_streaming_ps_$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
        Trace-Step "CREATE_CONTAINER"
        $create = Invoke-PipeHttp -PipeName $DockerPipe -Method POST -Path "/v1.54/containers/create?name=$name" -Body @{
            Image = "ubuntu"
            Cmd = @("sh", "-c", "echo phase16-log && sleep 30")
            HostConfig = @{ AutoRemove = $false }
        }
        Assert-Status $create 201 "Container create"
        $containerId = ((ConvertFrom-Json $create.BodyText).Id)

        Trace-Step "START_CONTAINER"
        $start = Invoke-PipeHttp -PipeName $DockerPipe -Method POST -Path "/v1.54/containers/$containerId/start"
        Assert-Status $start 204 "Container start"

        Trace-Step "READ_LOGS"
        $logs = Invoke-PipeHttp -PipeName $DockerPipe -Method GET -Path "/v1.54/containers/$containerId/logs?stdout=1&stderr=1&tail=20"
        Assert-Status $logs 200 "Container logs"
        $logText = ConvertFrom-DockerRawStream $logs.BodyBytes
        if ($logText -notmatch "phase16-log") {
            throw "Container logs did not include expected output. Body: $logText"
        }

        $futureSince = [DateTimeOffset]::UtcNow.AddDays(1).ToUnixTimeSeconds()
        Trace-Step "READ_LOGS_SINCE"
        $futureLogs = Invoke-PipeHttp -PipeName $DockerPipe -Method GET -Path "/v1.54/containers/$containerId/logs?stdout=1&since=$futureSince"
        Assert-Status $futureLogs 200 "Container logs since filter"
        $futureLogText = ConvertFrom-DockerRawStream $futureLogs.BodyBytes
        if ($futureLogText -match "phase16-log") {
            throw "Container logs ignored future since filter. Body: $futureLogText"
        }

        Trace-Step "READ_STATS"
        $stats = Invoke-PipeHttp -PipeName $DockerPipe -Method GET -Path "/v1.54/containers/$containerId/stats?stream=false"
        Assert-Status $stats 200 "Container stats"
        $statsJson = ConvertFrom-Json $stats.BodyText
        if ($null -eq $statsJson.cpu_stats -or $null -eq $statsJson.memory_stats) {
            throw "Container stats did not include cpu_stats and memory_stats."
        }

        Trace-Step "READ_STATS_STREAM_UNSUPPORTED"
        $statsStream = Invoke-PipeHttp -PipeName $DockerPipe -Method GET -Path "/v1.54/containers/$containerId/stats"
        Assert-Status $statsStream 501 "Container stats default stream"

        Trace-Step "CREATE_EXEC"
        $execCreate = Invoke-PipeHttp -PipeName $DockerPipe -Method POST -Path "/v1.54/containers/$containerId/exec" -Body @{
            AttachStdout = $true
            AttachStderr = $true
            Tty = $false
            Cmd = @("sh", "-lc", "echo wslcd-exec")
        }
        Assert-Status $execCreate 200 "Exec create"
        $execId = ((ConvertFrom-Json $execCreate.BodyText).Id)
        if ([string]::IsNullOrWhiteSpace($execId)) {
            throw "Exec create did not return an Id."
        }

        Trace-Step "START_EXEC"
        $execStart = Invoke-PipeHttp -PipeName $DockerPipe -Method POST -Path "/v1.54/exec/$execId/start" -Body @{
            Detach = $false
            Tty = $false
        }
        Assert-Status $execStart 200 "Exec start"
        $execText = ConvertFrom-DockerRawStream $execStart.BodyBytes
        if ($execText -notmatch "wslcd-exec") {
            throw "Exec start did not include expected output. Body: $execText"
        }

        Trace-Step "INSPECT_EXEC"
        $execInspect = Invoke-PipeHttp -PipeName $DockerPipe -Method GET -Path "/v1.54/exec/$execId/json"
        Assert-Status $execInspect 200 "Exec inspect"
        $execInspectJson = ConvertFrom-Json $execInspect.BodyText
        if ($execInspectJson.ExitCode -ne 0) {
            throw "Exec inspect returned ExitCode=$($execInspectJson.ExitCode), expected 0."
        }

        Trace-Step "RESIZE_EXEC"
        $execResize = Invoke-PipeHttp -PipeName $DockerPipe -Method POST -Path "/v1.54/exec/$execId/resize?h=24&w=80"
        Assert-Status $execResize 204 "Exec resize"

        Trace-Step "CREATE_TTY_EXEC_UNSUPPORTED"
        $ttyExecCreate = Invoke-PipeHttp -PipeName $DockerPipe -Method POST -Path "/v1.54/containers/$containerId/exec" -Body @{
            AttachStdout = $true
            AttachStderr = $true
            Tty = $true
            Cmd = @("sh")
        }
        Assert-Status $ttyExecCreate 501 "TTY exec create"

        Trace-Step "RESIZE_MISSING_EXEC"
        $missingResize = Invoke-PipeHttp -PipeName $DockerPipe -Method POST -Path "/v1.54/exec/missing-phase16-exec/resize?h=24&w=80"
        Assert-Status $missingResize 404 "Missing exec resize"

        Write-Host "DOCKER_API_STREAMING_OK"
    }
}
finally {
    if ($containerId) {
        try { Invoke-PipeHttp -PipeName $DockerPipe -Method DELETE -Path "/v1.54/containers/$containerId`?force=1&v=1" | Out-Null } catch {}
    }
    try { Invoke-PipeHttp -PipeName $NativePipe -Method POST -Path "/__shutdown" | Out-Null } catch {}
    if (-not $daemon.HasExited) {
        $daemon.WaitForExit(5000) | Out-Null
    }
    if (-not $daemon.HasExited) {
        Stop-Process -Id $daemon.Id -Force
    }
}
