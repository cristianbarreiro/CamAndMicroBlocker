using PrivLock.Platform.Abstractions;
using Serilog;

namespace PrivLock.Platform.Windows.System;

/// <summary>
/// Ensures single-instance execution on Windows using a system-wide named Mutex.
/// </summary>
public sealed class WindowsSingleInstanceGuard : ISingleInstanceGuard
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsSingleInstanceGuard>();
    private const string MutexName = @"Global\PrivLock_SingleInstance";

    private Mutex? _mutex;
    private bool _hasAcquired;

    public bool TryAcquireSingleInstance()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            _hasAcquired = createdNew;

            if (!_hasAcquired)
            {
                Log.Information("Another instance of PrivLock is already running on Windows.");
            }

            return _hasAcquired;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create or acquire single-instance mutex on Windows");
            return true; // Degrade gracefully
        }
    }

    public void Release()
    {
        if (_mutex != null && _hasAcquired)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error releasing single-instance mutex on Windows");
            }
            finally
            {
                _mutex.Dispose();
                _mutex = null;
                _hasAcquired = false;
            }
        }
    }

    public void Dispose()
    {
        Release();
    }
}
