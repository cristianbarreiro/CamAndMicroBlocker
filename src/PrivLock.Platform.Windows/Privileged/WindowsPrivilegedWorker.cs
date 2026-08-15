using System.IO.Pipes;
using System.Text.Json;
using PrivLock.Domain.Results;
using Serilog;

namespace PrivLock.Platform.Windows.Privileged;

/// <summary>
/// Long-lived elevated worker process executed within the same PrivLock binary.
/// Handles commands over a secure NamedPipe session so UAC is prompted only once per application run.
/// </summary>
public static class WindowsPrivilegedWorker
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(WindowsPrivilegedWorker));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static int Run(string pipeName)
    {
        Log.Information("Starting elevated session worker on pipe: {PipeName}", pipeName);

        try
        {
            using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipeClient.Connect(10000); // 10s timeout to connect to main UI process

            Log.Information("Elevated session worker connected to UI process");

            using var reader = new StreamReader(pipeClient);
            using var writer = new StreamWriter(pipeClient) { AutoFlush = true };

            while (pipeClient.IsConnected)
            {
                var line = reader.ReadLine();
                if (line == null) break; // Pipe disconnected (main app closed)

                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('\t', 2);
                var command = parts[0];
                var argument = parts.Length > 1 ? parts[1] : string.Empty;

                if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Worker received exit command");
                    break;
                }

                var result = WindowsPrivilegedExecutor.ExecutePrivilegedCommand(command, argument);
                var json = JsonSerializer.Serialize(result, JsonOptions);
                writer.WriteLine(json);
            }

            Log.Information("Elevated session worker exiting cleanly");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Elevated session worker encountered an error");
            return 1;
        }
    }
}
