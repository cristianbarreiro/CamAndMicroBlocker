using System.Diagnostics;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using Serilog;

namespace PrivLock.Platform.Windows.Devices;

/// <summary>
/// Direct in-process PnP hardware device controller using CfgMgr32.dll on Windows.
/// </summary>
public sealed class WindowsDeviceController
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsDeviceController>();

    public Task<OperationResult> DisableDevicesAsync(IEnumerable<DeviceInfo> devices)
    {
        var deviceList = devices.ToList();
        if (deviceList.Count == 0)
        {
            Log.Warning("No devices provided to disable");
            return Task.FromResult(OperationResult.Ok());
        }

        var sw = Stopwatch.StartNew();
        Log.Information("Disabling {Count} physical device(s) on Windows via CfgMgr32", deviceList.Count);

        var details = new List<DeviceOperationDetail>();
        var hasFailure = false;

        foreach (var device in deviceList)
        {
            var opResult = DisableDevice(device.Id);
            details.Add(new DeviceOperationDetail
            {
                DeviceId = device.Id,
                FriendlyName = device.FriendlyName,
                Success = opResult.Success,
                ErrorMessage = opResult.ErrorMessage
            });

            if (!opResult.Success)
            {
                hasFailure = true;
            }
        }

        sw.Stop();

        if (hasFailure)
        {
            var combinedError = string.Join("; ", details.Where(d => !d.Success).Select(d => $"[{d.FriendlyName}]: {d.ErrorMessage}"));
            Log.Error("Failed to disable some devices in {DurationMs}ms: {Error}", sw.ElapsedMilliseconds, combinedError);
            return Task.FromResult(OperationResult.Fail(combinedError, details));
        }

        Log.Information("Successfully disabled {Count} device(s) in {DurationMs}ms", deviceList.Count, sw.ElapsedMilliseconds);
        return Task.FromResult(OperationResult.Ok(details));
    }

    public Task<OperationResult> EnableDevicesAsync(IEnumerable<DeviceInfo> devices)
    {
        var deviceList = devices.ToList();
        if (deviceList.Count == 0)
        {
            Log.Warning("No devices provided to enable");
            return Task.FromResult(OperationResult.Ok());
        }

        var sw = Stopwatch.StartNew();
        Log.Information("Enabling {Count} physical device(s) on Windows via CfgMgr32", deviceList.Count);

        var details = new List<DeviceOperationDetail>();
        var hasFailure = false;

        foreach (var device in deviceList)
        {
            var opResult = EnableDevice(device.Id);
            details.Add(new DeviceOperationDetail
            {
                DeviceId = device.Id,
                FriendlyName = device.FriendlyName,
                Success = opResult.Success,
                ErrorMessage = opResult.ErrorMessage
            });

            if (!opResult.Success)
            {
                hasFailure = true;
            }
        }

        sw.Stop();

        if (hasFailure)
        {
            var combinedError = string.Join("; ", details.Where(d => !d.Success).Select(d => $"[{d.FriendlyName}]: {d.ErrorMessage}"));
            Log.Error("Failed to enable some devices in {DurationMs}ms: {Error}", sw.ElapsedMilliseconds, combinedError);
            return Task.FromResult(OperationResult.Fail(combinedError, details));
        }

        Log.Information("Successfully enabled {Count} device(s) in {DurationMs}ms", deviceList.Count, sw.ElapsedMilliseconds);
        return Task.FromResult(OperationResult.Ok(details));
    }

    private static OperationResult DisableDevice(string instanceId)
    {
        var locateResult = CfgMgrInterop.CM_Locate_DevNodeW(
            out uint devInst,
            instanceId,
            CfgMgrInterop.CM_LOCATE_DEVNODE_NORMAL);

        if (locateResult != CfgMgrInterop.CR_SUCCESS)
        {
            Log.Error("CM_Locate_DevNodeW failed for device {InstanceId}: error 0x{ErrorCode:X8}", instanceId, locateResult);
            return OperationResult.Fail($"Failed to locate device: error 0x{locateResult:X8}");
        }

        var disableResult = CfgMgrInterop.CM_Disable_DevNode(devInst, CfgMgrInterop.CM_DISABLE_UI_NOT_OK);
        if (disableResult != CfgMgrInterop.CR_SUCCESS)
        {
            Log.Error("CM_Disable_DevNode failed for device {InstanceId}: error 0x{ErrorCode:X8}", instanceId, disableResult);
            return OperationResult.Fail($"Failed to disable device: error 0x{disableResult:X8}");
        }

        Log.Debug("Device {InstanceId} disabled successfully via CfgMgr32", instanceId);
        return OperationResult.Ok();
    }

    private static OperationResult EnableDevice(string instanceId)
    {
        var locateResult = CfgMgrInterop.CM_Locate_DevNodeW(
            out uint devInst,
            instanceId,
            CfgMgrInterop.CM_LOCATE_DEVNODE_NORMAL);

        if (locateResult != CfgMgrInterop.CR_SUCCESS)
        {
            Log.Error("CM_Locate_DevNodeW failed for device {InstanceId}: error 0x{ErrorCode:X8}", instanceId, locateResult);
            return OperationResult.Fail($"Failed to locate device: error 0x{locateResult:X8}");
        }

        var enableResult = CfgMgrInterop.CM_Enable_DevNode(devInst, 0);
        if (enableResult != CfgMgrInterop.CR_SUCCESS)
        {
            Log.Error("CM_Enable_DevNode failed for device {InstanceId}: error 0x{ErrorCode:X8}", instanceId, enableResult);
            return OperationResult.Fail($"Failed to enable device: error 0x{enableResult:X8}");
        }

        Log.Debug("Device {InstanceId} enabled successfully via CfgMgr32", instanceId);
        return OperationResult.Ok();
    }
}
