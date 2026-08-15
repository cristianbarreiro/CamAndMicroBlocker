using System.Diagnostics;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Windows.Devices;
using PrivLock.Platform.Windows.Policies;
using Serilog;

namespace PrivLock.Platform.Windows.Privileged;

/// <summary>
/// Handles execution of privileged commands in the elevated worker process
/// and delegates on-demand execution via the persistent WindowsPrivilegedSession.
/// </summary>
public static class WindowsPrivilegedExecutor
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(WindowsPrivilegedExecutor));

    /// <summary>
    /// Executes a privileged command in-process with strict whitelist validation.
    /// Called when PrivLock is invoked with the '--privileged-exec' or '--privileged-worker' flag.
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
                "ping" => OperationResult.Ok(),
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
    /// Requests execution via the persistent elevated session (UAC prompted once per run).
    /// </summary>
    public static Task<OperationResult> InvokeOnDemandElevationAsync(string command, string argument)
    {
        return WindowsPrivilegedSession.Instance.ExecuteCommandAsync(command, argument);
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
}
