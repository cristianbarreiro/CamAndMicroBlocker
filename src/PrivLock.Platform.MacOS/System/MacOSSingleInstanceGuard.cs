using PrivLock.Platform.Abstractions;
using Serilog;

namespace PrivLock.Platform.MacOS.System;

/// <summary>
/// Ensures single-instance execution on macOS using a lock file in Application Support.
/// </summary>
public sealed class MacOSSingleInstanceGuard : ISingleInstanceGuard
{
    private static readonly ILogger Log = Serilog.Log.ForContext<MacOSSingleInstanceGuard>();
    private readonly string _lockFilePath;
    private FileStream? _fileStream;
    private bool _hasAcquired;

    public MacOSSingleInstanceGuard()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "PrivLock");

        Directory.CreateDirectory(appData);
        _lockFilePath = Path.Combine(appData, "privlock.lock");
    }

    public bool TryAcquireSingleInstance()
    {
        try
        {
            _fileStream = new FileStream(_lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _hasAcquired = true;
            return true;
        }
        catch (IOException)
        {
            Log.Information("Another instance of PrivLock is already running on macOS.");
            _hasAcquired = false;
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create single-instance lock file on macOS");
            return true;
        }
    }

    public void Release()
    {
        if (_fileStream != null && _hasAcquired)
        {
            try
            {
                _fileStream.Dispose();
                _fileStream = null;
                if (File.Exists(_lockFilePath))
                {
                    File.Delete(_lockFilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error releasing macOS single-instance lock file");
            }
            finally
            {
                _hasAcquired = false;
            }
        }
    }

    public void Dispose()
    {
        Release();
    }
}
