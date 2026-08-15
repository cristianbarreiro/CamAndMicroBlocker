using Microsoft.Win32;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using Serilog;

namespace PrivLock.Platform.Windows.System;

/// <summary>
/// Manages autostart on Windows using HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public sealed class WindowsAutostartProvider : IAutostartProvider
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsAutostartProvider>();

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "PrivLock";

    public bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(AppName);
            return value != null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check Windows startup registry key");
            return false;
        }
    }

    public OperationResult EnableAutostart()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                var error = "Cannot determine current process path for autostart registration";
                Log.Error(error);
                return OperationResult.Fail(error);
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                var error = "Failed to open Run registry key for writing";
                Log.Error(error);
                return OperationResult.Fail(error);
            }

            key.SetValue(AppName, $"\"{exePath}\" --minimized");
            Log.Information("Windows startup enabled: {Path}", exePath);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enable Windows startup");
            return OperationResult.Fail(ex.Message);
        }
    }

    public OperationResult DisableAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
            Log.Information("Windows startup disabled");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to disable Windows startup");
            return OperationResult.Fail(ex.Message);
        }
    }
}
