using System.Text.Json;
using PrivLock.Platform.Abstractions;
using Serilog;

namespace PrivLock.Infrastructure.Common.Storage;

/// <summary>
/// Persists the user's desired blocking state and preferences as a JSON file in the OS local application data directory.
/// Thread-safe via file locking. Works transparently on Windows, Linux, and macOS.
/// </summary>
public sealed class FileStateStore : IStateStore
{
    private static readonly ILogger Log = Serilog.Log.ForContext<FileStateStore>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly object _lock = new();

    public FileStateStore(string? customDirectory = null)
    {
        var appDataDir = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrivLock");

        Directory.CreateDirectory(appDataDir);
        _filePath = Path.Combine(appDataDir, "state.json");
    }

    public DesiredState Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    Log.Information("No state file found at {Path}, using defaults", _filePath);
                    return new DesiredState();
                }

                var json = File.ReadAllText(_filePath);
                var state = JsonSerializer.Deserialize<DesiredState>(json, JsonOptions);

                if (state == null)
                {
                    Log.Warning("State file deserialized to null, using defaults");
                    return new DesiredState();
                }

                Log.Debug("Loaded state: Camera={Camera}, Microphone={Microphone}, StartWithSystem={Startup}, Lang={Lang}",
                    state.Camera, state.Microphone, state.StartWithSystem, state.Language);
                return state;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load state from {Path}, using defaults", _filePath);
                return new DesiredState();
            }
        }
    }

    public void Save(DesiredState state)
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(state, JsonOptions);
                File.WriteAllText(_filePath, json);
                Log.Debug("Saved state to {Path}", _filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save state to {Path}", _filePath);
            }
        }
    }
}
