using System.Diagnostics;
using System.Security.Principal;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using Serilog;

namespace PrivLock.Platform.Windows.Elevation;

/// <summary>
/// Checks and requests administrative elevation on Windows using WindowsIdentity and UAC 'runas' verb.
/// </summary>
public sealed class WindowsElevationProvider : IElevationProvider
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsElevationProvider>();

    public bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to determine Windows elevation status");
                return false;
            }
        }
    }

    public Task<ElevationResult> RequestElevationAsync(CancellationToken cancellationToken = default)
    {
        if (IsElevated)
        {
            return Task.FromResult(ElevationResult.Success());
        }

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return Task.FromResult(ElevationResult.Fail("Cannot determine executable path."));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Verb = "runas",
                UseShellExecute = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                return Task.FromResult(ElevationResult.Success());
            }

            return Task.FromResult(ElevationResult.Fail("Failed to start elevated process."));
        }
        catch (global::System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED (1223) = User clicked "No" on UAC prompt
            Log.Warning("User cancelled Windows UAC elevation prompt");
            return Task.FromResult(ElevationResult.Cancelled());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while requesting Windows elevation");
            return Task.FromResult(ElevationResult.Fail(ex.Message));
        }
    }
}
