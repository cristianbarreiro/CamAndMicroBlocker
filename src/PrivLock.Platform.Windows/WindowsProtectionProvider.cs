using System.Diagnostics;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using PrivLock.Platform.Windows.Devices;
using PrivLock.Platform.Windows.Policies;
using Serilog;

namespace PrivLock.Platform.Windows;

/// <summary>
/// Native Windows implementation of IDeviceProtectionProvider.
/// Provides dual-layer protection (HKLM Registry Privacy Policies + CfgMgr32 PnP Hardware disablement).
/// </summary>
public sealed class WindowsProtectionProvider : IDeviceProtectionProvider
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsProtectionProvider>();

    private readonly WindowsDeviceDetector _deviceDetector;
    private readonly WindowsDeviceController _deviceController;
    private readonly WindowsPolicyManager _policyManager;
    private readonly IStateStore _stateStore;

    public WindowsProtectionProvider(
        WindowsDeviceDetector deviceDetector,
        WindowsDeviceController deviceController,
        WindowsPolicyManager policyManager,
        IStateStore stateStore)
    {
        _deviceDetector = deviceDetector;
        _deviceController = deviceController;
        _policyManager = policyManager;
        _stateStore = stateStore;
    }

    public async Task<OperationResult> BlockAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Applying Windows dual-layer block: Target={Target}", target);

        // 1. Apply HKLM Registry Policy
        var policyResult = _policyManager.SetPolicy(target, BlockStatus.Blocked);
        if (!policyResult.Success)
        {
            sw.Stop();
            Log.Error("Failed to set Windows registry policy: {Error}", policyResult.ErrorMessage);
            return policyResult;
        }

        // 2. Locate and disable physical hardware devices via CfgMgr32
        var devices = await GetDevicesForTargetAsync(target, cancellationToken);
        if (devices.Count > 0)
        {
            var deviceResult = await _deviceController.DisableDevicesAsync(devices);
            if (!deviceResult.Success)
            {
                Log.Warning("Some physical devices could not be disabled via CfgMgr32: {Error}. System policy remains active.",
                    deviceResult.ErrorMessage);
            }
        }
        else
        {
            Log.Warning("No physical {Target} devices detected, but Windows policy was enforced.", target);
        }

        sw.Stop();
        Log.Information("Windows block completed in {DurationMs}ms", sw.ElapsedMilliseconds);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> UnblockAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Removing Windows dual-layer block: Target={Target}", target);

        // 1. Remove HKLM Registry Policy
        var policyResult = _policyManager.SetPolicy(target, BlockStatus.Allowed);
        if (!policyResult.Success)
        {
            sw.Stop();
            Log.Error("Failed to remove Windows registry policy: {Error}", policyResult.ErrorMessage);
            return policyResult;
        }

        // 2. Locate and enable physical hardware devices via CfgMgr32
        var devices = await GetDevicesForTargetAsync(target, cancellationToken);
        if (devices.Count > 0)
        {
            var deviceResult = await _deviceController.EnableDevicesAsync(devices);
            if (!deviceResult.Success)
            {
                Log.Warning("Some physical devices could not be enabled via CfgMgr32: {Error}", deviceResult.ErrorMessage);
            }
        }

        sw.Stop();
        Log.Information("Windows unblock completed in {DurationMs}ms", sw.ElapsedMilliseconds);
        return OperationResult.Ok();
    }

    public async Task<DeviceBlockState> GetCameraStatusAsync(CancellationToken cancellationToken = default)
    {
        var desired = _stateStore.Load();
        var policy = _policyManager.GetCameraPolicyStatus();
        var cameras = await _deviceDetector.DetectCamerasAsync(cancellationToken);
        var deviceStatus = DetermineDeviceStatus(cameras);

        return new DeviceBlockState
        {
            DesiredStatus = desired.Camera,
            PolicyStatus = policy,
            DeviceStatus = deviceStatus
        };
    }

    public async Task<DeviceBlockState> GetMicrophoneStatusAsync(CancellationToken cancellationToken = default)
    {
        var desired = _stateStore.Load();
        var policy = _policyManager.GetMicrophonePolicyStatus();
        var microphones = await _deviceDetector.DetectMicrophonesAsync(cancellationToken);
        var deviceStatus = DetermineDeviceStatus(microphones);

        return new DeviceBlockState
        {
            DesiredStatus = desired.Microphone,
            PolicyStatus = policy,
            DeviceStatus = deviceStatus
        };
    }

    public async Task<BlockState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        var cameraState = await GetCameraStatusAsync(cancellationToken);
        var micState = await GetMicrophoneStatusAsync(cancellationToken);

        return new BlockState
        {
            Camera = cameraState,
            Microphone = micState
        };
    }

    private async Task<List<DeviceInfo>> GetDevicesForTargetAsync(BlockTarget target, CancellationToken ct)
    {
        var devices = new List<DeviceInfo>();
        if (target is BlockTarget.Camera or BlockTarget.Both)
            devices.AddRange(await _deviceDetector.DetectCamerasAsync(ct));
        if (target is BlockTarget.Microphone or BlockTarget.Both)
            devices.AddRange(await _deviceDetector.DetectMicrophonesAsync(ct));
        return devices;
    }

    private static BlockStatus DetermineDeviceStatus(IReadOnlyList<DeviceInfo> devices)
    {
        if (devices.Count == 0) return BlockStatus.Unknown;
        var allEnabled = devices.All(d => d.IsEnabled);
        var allDisabled = devices.All(d => !d.IsEnabled);

        if (allDisabled) return BlockStatus.Blocked;
        if (allEnabled) return BlockStatus.Allowed;
        return BlockStatus.Unknown;
    }
}
