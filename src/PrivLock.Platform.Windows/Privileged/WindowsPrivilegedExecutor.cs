using System.Diagnostics;
using System.Text.Json;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Windows.Devices;
using PrivLock.Platform.Windows.Policies;
using Serilog;

namespace PrivLock.Platform.Windows.Privileged;

/// <summary>
/// Handles both the execution of privileged commands in the transient elevated instance
/// and the on-demand UAC invocation from the standard unprivileged instance.
/// 
/// Everything is self-contained within the single PrivLock executable.
/// </summary>
public static class WindowsPrivilegedExecutor
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(WindowsPrivilegedExecutor));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Executes a privileged command in-process with strict whitelist validation.
    /// Called when PrivLock is invoked with the '--privileged-exec' internal CLI flag.
    /// </summary>
    public static OperationResult ExecutePrivilegedCommand(string command, string argument)
    {
        Log.Information("Executing internal privileged command: Command={Command}", command);

        try
        {
            var policyManager = new WindowsPolicyManager();
            var deviceController = new WindowsDeviceController();

            return command.ToLowerInvariant() switch
            {
                "set-policy" => ValidateAndSetPolicy(policyManager, argument, BlockStatus.Blocked),
                "remove-policy" => ValidateAndSetPolicy(policyManager, argument, BlockStatus.Allowed),
                "disable-devices" => ValidateAndToggleDevices(deviceController, argument, disable: true),
                "enable-devices" => ValidateAndToggleDevices(deviceController, argument, disable: false),
                _ => OperationResult.Fail($"Unknown privileged command: '{command}'")
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to execute internal privileged command: {Command}", command);
            return OperationResult.Fail($"Privileged execution error: {ex.Message}");
        }
    }

    /// <summary>
    /// Requests on-demand elevation by launching a hidden, short-lived instance of PrivLock itself with Verb="runas".
    /// Called by the standard unprivileged PrivLock instance when an operation requires administrative rights.
    /// </summary>
    public static async Task<OperationResult> InvokeOnDemandElevationAsync(string command, string argument)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            var error = "Cannot determine current executable path for on-demand elevation.";
            Log.Error(error);
            return OperationResult.Fail(error);
        }

        var resultFilePath = Path.Combine(Path.GetTempPath(), $"PrivLock_res_{Guid.NewGuid():N}.json");
        var sw = Stopwatch.StartNew();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--privileged-exec \"{command}\" \"{argument}\" --result-file \"{resultFilePath}\"",
                Verb = "runas", // Triggers Windows UAC on-demand prompt
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                sw.Stop();
                Log.Error("Failed to start on-demand elevated PrivLock process");
                return OperationResult.Fail("Failed to start on-demand elevated PrivLock process.");
            }

            await process.WaitForExitAsync();
            sw.Stop();

            Log.Debug("On-demand elevated process finished: ExitCode={Code}, DurationMs={DurationMs}",
                process.ExitCode, sw.ElapsedMilliseconds);

            if (File.Exists(resultFilePath))
            {
                var json = await File.ReadAllTextAsync(resultFilePath);
                CleanupFile(resultFilePath);

                var result = JsonSerializer.Deserialize<OperationResult>(json, JsonOptions);
                if (result != null)
                {
                    return result;
                }
            }

            if (process.ExitCode == 0)
            {
                return OperationResult.Ok();
            }

            return OperationResult.Fail($"Privileged operation exited with code {process.ExitCode}");
        }
        catch (global::System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED (1223) = User clicked "No" on UAC prompt
            Log.Warning("User cancelled UAC elevation prompt for command {Command}", command);
            CleanupFile(resultFilePath);
            return OperationResult.Fail("Operation cancelled: Administrator permissions were denied.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during on-demand elevation for command {Command}", command);
            CleanupFile(resultFilePath);
            return OperationResult.Fail($"Elevation request error: {ex.Message}");
        }
    }

    private static OperationResult ValidateAndSetPolicy(WindowsPolicyManager policyManager, string targetArg, BlockStatus status)
    {
        var target = targetArg.ToLowerInvariant() switch
        {
            "camera" => BlockTarget.Camera,
            "microphone" => BlockTarget.Microphone,
            "both" => BlockTarget.Both,
            _ => (BlockTarget?)null
        };

        if (target == null)
        {
            return OperationResult.Fail($"Invalid policy target: '{targetArg}'");
        }

        return policyManager.SetPolicy(target.Value, status);
    }

    private static OperationResult ValidateAndToggleDevices(WindowsDeviceController controller, string deviceIdsArg, bool disable)
    {
        var ids = deviceIdsArg.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0)
        {
            return OperationResult.Ok();
        }

        // Validate that IDs are non-empty and well-formed
        var devices = ids.Select(id => new DeviceInfo
        {
            Id = id,
            FriendlyName = id,
            DeviceType = DeviceType.Camera
        }).ToList();

        return disable
            ? controller.DisableDevicesAsync(devices).GetAwaiter().GetResult()
            : controller.EnableDevicesAsync(devices).GetAwaiter().GetResult();
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* Best effort */ }
    }
}
