using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using PrivLock.Domain.Results;
using Serilog;

namespace PrivLock.Platform.Windows.Privileged;

/// <summary>
/// Client-side manager for the persistent elevated session.
/// Prompts UAC exactly once when first needed, then maintains a secure, long-lived IPC pipe
/// for all subsequent privileged operations during the application run.
/// </summary>
public sealed class WindowsPrivilegedSession : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsPrivilegedSession>();

    private static readonly Lazy<WindowsPrivilegedSession> InstanceLazy = new(() => new WindowsPrivilegedSession());
    public static WindowsPrivilegedSession Instance => InstanceLazy.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly object _lock = new();
    private NamedPipeServerStream? _pipeServer;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Process? _workerProcess;
    private bool _isElevatedSessionActive;

    public bool IsSessionActive
    {
        get
        {
            lock (_lock)
            {
                return _isElevatedSessionActive &&
                       _pipeServer != null &&
                       _pipeServer.IsConnected &&
                       _workerProcess != null &&
                       !_workerProcess.HasExited;
            }
        }
    }

    /// <summary>
    /// Executes a privileged command via the persistent elevated worker session.
    /// If no elevated session is active, prompts UAC once to start the worker.
    /// All subsequent executions reuse the active session with 0 UAC prompts.
    /// </summary>
    public async Task<OperationResult> ExecuteCommandAsync(string command, string argument)
    {
        try
        {
            await EnsureSessionActiveAsync();

            lock (_lock)
            {
                if (_writer == null || _reader == null || _pipeServer == null || !_pipeServer.IsConnected)
                {
                    return OperationResult.Fail("Elevated session is not connected.");
                }

                _writer.WriteLine($"{command}\t{argument}");
                var responseJson = _reader.ReadLine();

                if (string.IsNullOrEmpty(responseJson))
                {
                    return OperationResult.Fail("Empty response from elevated worker.");
                }

                var result = JsonSerializer.Deserialize<OperationResult>(responseJson, JsonOptions);
                return result ?? OperationResult.Ok();
            }
        }
        catch (global::System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED (1223) = User clicked "No" on UAC prompt
            Log.Warning("User cancelled UAC elevation prompt for elevated session");
            return OperationResult.Fail("Operation cancelled: Administrator permissions were denied.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to execute command '{Command}' via elevated session", command);
            CloseSession();
            return OperationResult.Fail($"Elevated session error: {ex.Message}");
        }
    }

    private async Task EnsureSessionActiveAsync()
    {
        if (IsSessionActive) return;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            throw new InvalidOperationException("Cannot locate current executable path for privileged session.");
        }

        CloseSession();

        var pipeName = $"PrivLock_Pipe_{Guid.NewGuid():N}";
        Log.Information("Starting new elevated session worker with pipe: {PipeName}", pipeName);

        var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"--privileged-worker \"{pipeName}\"",
            Verb = "runas", // UAC prompt asked ONCE here
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            pipeServer.Dispose();
            throw new InvalidOperationException("Failed to launch elevated session worker process.");
        }

        // Wait for the worker to connect
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await pipeServer.WaitForConnectionAsync(cts.Token);

        lock (_lock)
        {
            _pipeServer = pipeServer;
            _workerProcess = process;
            _writer = new StreamWriter(_pipeServer) { AutoFlush = true };
            _reader = new StreamReader(_pipeServer);
            _isElevatedSessionActive = true;
        }

        Log.Information("Elevated session successfully established. Subsequent operations will require 0 UAC prompts.");
    }

    public void CloseSession()
    {
        lock (_lock)
        {
            _isElevatedSessionActive = false;

            try
            {
                if (_writer != null && _pipeServer != null && _pipeServer.IsConnected)
                {
                    _writer.WriteLine("exit");
                }
            }
            catch { /* Best effort */ }

            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _pipeServer?.Dispose(); } catch { }

            _writer = null;
            _reader = null;
            _pipeServer = null;

            if (_workerProcess != null)
            {
                try
                {
                    if (!_workerProcess.HasExited)
                    {
                        _workerProcess.WaitForExit(1000);
                        if (!_workerProcess.HasExited)
                        {
                            _workerProcess.Kill();
                        }
                    }
                    _workerProcess.Dispose();
                }
                catch { /* Best effort */ }
                _workerProcess = null;
            }
        }
    }

    public void Dispose()
    {
        CloseSession();
    }
}
