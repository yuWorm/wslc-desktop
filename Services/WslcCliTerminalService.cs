using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

internal interface ITerminalProcessLauncher
{
    bool TryStart(string fileName, IReadOnlyList<string> arguments);
}

public sealed class WslcCliTerminalService : IWslcTerminalService
{
    private static readonly string[] DefaultShellCandidates = ["/bin/bash", "/bin/sh"];
    private readonly IAppSettingsService? _settingsService;
    private readonly ITerminalProcessLauncher _processLauncher;

    public WslcCliTerminalService()
        : this(null, new DefaultTerminalProcessLauncher())
    {
    }

    public WslcCliTerminalService(IAppSettingsService? settingsService)
        : this(settingsService, new DefaultTerminalProcessLauncher())
    {
    }

    internal WslcCliTerminalService(IAppSettingsService? settingsService, ITerminalProcessLauncher processLauncher)
    {
        _settingsService = settingsService;
        _processLauncher = processLauncher;
    }

    public Task<ITerminalSession> ConnectAsync(TerminalConnectRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ContainerId))
        {
            throw new ArgumentException("Container id is required.", nameof(request));
        }

        IReadOnlyList<string> shells = NormalizeShellCandidates(request.ShellCandidates);
        string commandLine = BuildInteractiveCommandLine(request.ContainerId, shells);
        var session = ConPtyTerminalSession.Start(request.ContainerId, BuildShellLabel(shells), commandLine, columns: 100, rows: 28);
        return Task.FromResult<ITerminalSession>(session);
    }

    public async Task OpenExternalAsync(TerminalConnectRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ContainerId))
        {
            throw new ArgumentException("Container id is required.", nameof(request));
        }

        IReadOnlyList<string> shells = NormalizeShellCandidates(request.ShellCandidates);
        AppSettingsSnapshot settings = await LoadSettingsAsync(cancellationToken);
        ExternalTerminalLaunchPlan plan = BuildExternalTerminalLaunchPlan(request, shells, settings);

        if (_processLauncher.TryStart("wt.exe", plan.WindowsTerminalArguments))
        {
            return;
        }

        if (_processLauncher.TryStart("cmd.exe", plan.CmdArguments))
        {
            return;
        }

        throw new InvalidOperationException("Unable to start an external terminal.");
    }

    private static IReadOnlyList<string> NormalizeShellCandidates(IReadOnlyList<string>? shellCandidates)
    {
        var shells = shellCandidates?
            .Where(shell => !string.IsNullOrWhiteSpace(shell))
            .Select(shell => shell.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return shells is { Length: > 0 } ? shells : DefaultShellCandidates;
    }

    private static string BuildShellLabel(IReadOnlyList<string> shells)
    {
        return string.Join(" -> ", shells);
    }

    private static string BuildInteractiveCommandLine(string containerId, IReadOnlyList<string> shells)
    {
        if (shells.Count == 1)
        {
            return JoinWindowsArguments(["wslc.exe", "exec", "-it", containerId, shells[0]]);
        }

        string fallbackScript = BuildShellFallbackScript(shells);
        return JoinWindowsArguments(["wslc.exe", "exec", "-it", containerId, "/bin/sh", "-lc", fallbackScript]);
    }

    private static string BuildShellFallbackScript(IReadOnlyList<string> shells)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < shells.Count; index++)
        {
            string shell = shells[index];
            if (index == 0)
            {
                builder.Append("if ");
            }
            else
            {
                builder.Append("elif ");
            }

            builder.Append("command -v ");
            builder.Append(QuotePosix(shell));
            builder.Append(" >/dev/null 2>&1; then exec ");
            builder.Append(QuotePosix(shell));
            builder.Append("; ");
        }

        builder.Append("else echo 'No supported shell found' >&2; exit 127; fi");
        return builder.ToString();
    }

    private async Task<AppSettingsSnapshot> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        if (_settingsService is null)
        {
            return CreateDefaultTerminalSettings();
        }

        try
        {
            return await _settingsService.LoadAsync(cancellationToken);
        }
        catch
        {
            return CreateDefaultTerminalSettings();
        }
    }

    private static AppSettingsSnapshot CreateDefaultTerminalSettings()
    {
        return new AppSettingsSnapshot(
            DataRoot: string.Empty,
            CpuCount: 1,
            MemoryMB: 1024,
            DefaultShell: "/bin/sh",
            PreferExternalTerminal: true,
            Language: "system");
    }

    private static ExternalTerminalLaunchPlan BuildExternalTerminalLaunchPlan(
        TerminalConnectRequest request,
        IReadOnlyList<string> shells,
        AppSettingsSnapshot settings)
    {
        string title = string.IsNullOrWhiteSpace(request.ContainerName)
            ? request.ContainerId
            : request.ContainerName.Trim();
        IReadOnlyList<string> execArguments = BuildExternalExecArguments(
            request.ContainerId,
            ChooseExternalShell(shells, settings.DefaultShell),
            settings);

        string[] wtArguments = ["new-tab", "--title", title, .. execArguments];
        string[] cmdArguments = ["/k", .. execArguments];
        return new ExternalTerminalLaunchPlan(wtArguments, cmdArguments);
    }

    private static IReadOnlyList<string> BuildExternalExecArguments(string containerId, string shell, AppSettingsSnapshot settings)
    {
        if (RuntimeProviderSelection.Normalize(settings.RuntimeProvider) == RuntimeProviderSelection.DockerApi)
        {
            var dockerArguments = new List<string> { "docker.exe" };
            if (!string.IsNullOrWhiteSpace(settings.DockerApiHost))
            {
                dockerArguments.Add("-H");
                dockerArguments.Add(settings.DockerApiHost.Trim());
            }

            dockerArguments.AddRange(["exec", "-it", containerId, shell]);
            return dockerArguments;
        }

        return ["wslc.exe", "exec", "-it", containerId, shell];
    }

    private static string ChooseExternalShell(IReadOnlyList<string> shells, string configuredShell)
    {
        if (!string.IsNullOrWhiteSpace(configuredShell))
        {
            return configuredShell.Trim();
        }

        return shells.FirstOrDefault(shell => !string.IsNullOrWhiteSpace(shell)) ?? "/bin/sh";
    }

    private static string QuotePosix(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string JoinWindowsArguments(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(QuoteWindowsArgument));
    }

    private static string QuoteWindowsArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (!value.Any(char.IsWhiteSpace) && !value.Contains('"', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        int backslashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(c);
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    private sealed record ExternalTerminalLaunchPlan(
        IReadOnlyList<string> WindowsTerminalArguments,
        IReadOnlyList<string> CmdArguments);

    private sealed class DefaultTerminalProcessLauncher : ITerminalProcessLauncher
    {
        public bool TryStart(string fileName, IReadOnlyList<string> arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false
                };

                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                Process.Start(startInfo);
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    private sealed class ConPtyTerminalSession : ITerminalSession
    {
        private const int StillActive = 259;
        private const uint ExtendedStartupInfoPresent = 0x00080000;
        private const uint CreateNoWindow = 0x08000000;
        private const int ProcThreadAttributePseudoConsole = 0x00020016;

        private readonly CancellationTokenSource _sessionCancellation = new();
        private readonly SafeFileHandle _inputWriteSide;
        private readonly SafeFileHandle _outputReadSide;
        private readonly FileStream _input;
        private readonly FileStream _output;
        private readonly IntPtr _pseudoConsole;
        private readonly IntPtr _processHandle;
        private readonly object _stateLock = new();
        private TerminalSessionState _state = TerminalSessionState.Connecting;
        private bool _disposed;

        private ConPtyTerminalSession(
            string containerId,
            string shell,
            SafeFileHandle inputWriteSide,
            SafeFileHandle outputReadSide,
            IntPtr pseudoConsole,
            IntPtr processHandle)
        {
            ContainerId = containerId;
            Shell = shell;
            _inputWriteSide = inputWriteSide;
            _outputReadSide = outputReadSide;
            _input = new FileStream(_inputWriteSide, FileAccess.Write, 4096, isAsync: false);
            _output = new FileStream(_outputReadSide, FileAccess.Read, 4096, isAsync: false);
            _pseudoConsole = pseudoConsole;
            _processHandle = processHandle;
        }

        public event EventHandler<TerminalOutputEvent>? OutputReceived;

        public event EventHandler<TerminalSessionState>? StateChanged;

        public string ContainerId { get; }

        public string Shell { get; }

        public TerminalSessionState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
        }

        public static ConPtyTerminalSession Start(string containerId, string shell, string commandLine, short columns, short rows)
        {
            if (!CreatePipe(out SafeFileHandle inputReadSide, out SafeFileHandle inputWriteSide, IntPtr.Zero, 0))
            {
                throw CreateWin32Exception("Create input pipe failed.");
            }

            if (!CreatePipe(out SafeFileHandle outputReadSide, out SafeFileHandle outputWriteSide, IntPtr.Zero, 0))
            {
                inputReadSide.Dispose();
                inputWriteSide.Dispose();
                throw CreateWin32Exception("Create output pipe failed.");
            }

            int hresult = CreatePseudoConsole(
                new Coord(columns, rows),
                inputReadSide.DangerousGetHandle(),
                outputWriteSide.DangerousGetHandle(),
                0,
                out IntPtr pseudoConsole);
            if (hresult != 0)
            {
                inputReadSide.Dispose();
                inputWriteSide.Dispose();
                outputReadSide.Dispose();
                outputWriteSide.Dispose();
                Marshal.ThrowExceptionForHR(hresult);
            }

            IntPtr attributeList = IntPtr.Zero;
            try
            {
                attributeList = CreatePseudoConsoleAttributeList(pseudoConsole);
                var startupInfo = new StartupInfoEx();
                startupInfo.StartupInfo.cb = (uint)Marshal.SizeOf<StartupInfoEx>();
                startupInfo.lpAttributeList = attributeList;

                if (!CreateProcessW(
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        ExtendedStartupInfoPresent | CreateNoWindow,
                        IntPtr.Zero,
                        null,
                        ref startupInfo,
                        out ProcessInformation processInformation))
                {
                    throw CreateWin32Exception("Create terminal process failed.");
                }

                CloseHandle(processInformation.hThread);
                inputReadSide.Dispose();
                outputWriteSide.Dispose();

                var session = new ConPtyTerminalSession(containerId, shell, inputWriteSide, outputReadSide, pseudoConsole, processInformation.hProcess);
                session.SetState(TerminalSessionState.Connected);
                session.StartReaders();
                return session;
            }
            catch
            {
                inputReadSide.Dispose();
                inputWriteSide.Dispose();
                outputReadSide.Dispose();
                outputWriteSide.Dispose();
                if (pseudoConsole != IntPtr.Zero)
                {
                    ClosePseudoConsole(pseudoConsole);
                }

                throw;
            }
            finally
            {
                if (attributeList != IntPtr.Zero)
                {
                    DeleteProcThreadAttributeList(attributeList);
                    Marshal.FreeHGlobal(attributeList);
                }
            }
        }

        public async Task SendInputAsync(string input, CancellationToken cancellationToken = default)
        {
            if (State != TerminalSessionState.Connected || string.IsNullOrEmpty(input))
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(input);
            await Task.Run(() =>
            {
                _input.Write(bytes, 0, bytes.Length);
                _input.Flush();
            }, cancellationToken);
        }

        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
        {
            if (columns <= 0 || rows <= 0 || _pseudoConsole == IntPtr.Zero)
            {
                return Task.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
            int hresult = ResizePseudoConsole(_pseudoConsole, new Coord((short)columns, (short)rows));
            if (hresult != 0)
            {
                Marshal.ThrowExceptionForHR(hresult);
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _sessionCancellation.Cancel();
            TryTerminateProcess();
            SetState(TerminalSessionState.Disconnected);
            Emit("system", $"{Environment.NewLine}[terminal disconnected]{Environment.NewLine}");
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sessionCancellation.Cancel();
            TryTerminateProcess();
            await _input.DisposeAsync();
            await _output.DisposeAsync();
            ClosePseudoConsole(_pseudoConsole);
            CloseHandle(_processHandle);
            _sessionCancellation.Dispose();
        }

        private void StartReaders()
        {
            _ = Task.Run(ReadOutputAsync);
            _ = Task.Run(WaitForExitAsync);
        }

        private async Task ReadOutputAsync()
        {
            var buffer = new byte[4096];
            try
            {
                while (!_sessionCancellation.IsCancellationRequested)
                {
                    int read = await Task.Run(
                        () => _output.Read(buffer, 0, buffer.Length),
                        _sessionCancellation.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    Emit("stdout", Encoding.UTF8.GetString(buffer, 0, read));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException ex)
            {
                if (!_sessionCancellation.IsCancellationRequested)
                {
                    SetState(TerminalSessionState.Failed);
                    Emit("stderr", $"{Environment.NewLine}[terminal output failed: {ex.Message}]{Environment.NewLine}");
                }
            }
        }

        private async Task WaitForExitAsync()
        {
            await Task.Run(() => WaitForSingleObject(_processHandle, uint.MaxValue));
            if (_sessionCancellation.IsCancellationRequested)
            {
                return;
            }

            if (GetExitCodeProcess(_processHandle, out uint exitCode) && exitCode != StillActive)
            {
                SetState(exitCode == 0 ? TerminalSessionState.Exited : TerminalSessionState.Failed);
                Emit("system", $"{Environment.NewLine}[terminal exited: {exitCode}]{Environment.NewLine}");
            }
            else
            {
                SetState(TerminalSessionState.Exited);
                Emit("system", $"{Environment.NewLine}[terminal exited]{Environment.NewLine}");
            }
        }

        private void TryTerminateProcess()
        {
            if (_processHandle == IntPtr.Zero)
            {
                return;
            }

            if (GetExitCodeProcess(_processHandle, out uint exitCode) && exitCode == StillActive)
            {
                TerminateProcess(_processHandle, 0);
            }
        }

        private void Emit(string stream, string text)
        {
            OutputReceived?.Invoke(this, new TerminalOutputEvent(DateTimeOffset.Now, stream, text));
        }

        private void SetState(TerminalSessionState state)
        {
            lock (_stateLock)
            {
                if (_state == state)
                {
                    return;
                }

                _state = state;
            }

            StateChanged?.Invoke(this, state);
        }

        private static IntPtr CreatePseudoConsoleAttributeList(IntPtr pseudoConsole)
        {
            IntPtr attributeListSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
            IntPtr attributeList = Marshal.AllocHGlobal(attributeListSize);

            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                Marshal.FreeHGlobal(attributeList);
                throw CreateWin32Exception("Initialize process attribute list failed.");
            }

            if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)ProcThreadAttributePseudoConsole,
                    pseudoConsole,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
                throw CreateWin32Exception("Update pseudo console attribute failed.");
            }

            return attributeList;
        }

        private static Win32Exception CreateWin32Exception(string message)
        {
            return new Win32Exception(Marshal.GetLastWin32Error(), message);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CreatePseudoConsole(Coord size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int ResizePseudoConsole(IntPtr hPC, Coord size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList,
            uint dwFlags,
            IntPtr attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessW(
            string? lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref StartupInfoEx lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [StructLayout(LayoutKind.Sequential)]
        private struct Coord
        {
            public Coord(short x, short y)
            {
                X = x;
                Y = y;
            }

            public short X;

            public short Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfoEx
        {
            public StartupInfo StartupInfo;
            public IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public uint cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }
    }
}
