using System.Diagnostics;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using PrivLock.Platform.Windows.Devices;
using PrivLock.Platform.Windows.Elevation;
using PrivLock.Platform.Windows.Policies;
using PrivLock.Platform.Windows.Privileged;
using Serilog;

namespace PrivLock.Platform.Windows;

/// <summary>
/// Native Windows implementation of IDeviceProtectionProvider.
/// Supports both in-process execution (if already elevated) and seamless on-demand elevation
/// within the single PrivLock application.
/// </summary>
public sealed class WindowsProtectionProvider : IDeviceProtectionProvider
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsProtectionProvider>();

    private readonly WindowsDeviceDetector _deviceDetector;
    private readonly WindowsDeviceController _deviceController;
    private readonly WindowsPolicyManager _policyManager;
    private readonly IElevationProvider _elevationProvider;
    private readonly IStateStore _stateStore;

    public WindowsProtectionProvider(
        WindowsDeviceDetector deviceDetector,
        WindowsDeviceController deviceController,
        WindowsPolicyManager policyManager,
        IElevationProvider elevationProvider,
        IStateStore stateStore)
    {
        _deviceDetector = deviceDetector;
        _deviceController = deviceController;
        _policyManager = policyManager;
        _elevationProvider = elevationProvider;
        _stateStore = stateStore;
    }

    public async Task<OperationResult> BlockAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Applying Windows protection: Target={Target}, IsElevated={IsElevated}",
            target, _elevationProvider.IsElevated);

        var targetStr = target.ToString().ToLowerInvariant();

        // 1. If running as standard user, use on-demand elevation via self-contained executor
        if (!_elevationProvider.IsElevated)
        {
            Log.Information("Process is running as standard user. Requesting on-demand elevation...");

            // Set policy via on-demand elevation
            var policyResult = await WindowsPrivilegedExecutor.InvokeOnDemandElevationAsync("set-policy", targetStr);
            if (!policyResult.Success)
            {
                sw.Stop();
                Log.Error("On-demand policy elevation failed: {Error}", policyResult.ErrorMessage);
                return policyResult;
            }

            // Disable physical devices via on-demand elevation
            var devices = await GetDevicesForTargetAsync(target, cancellationToken);
            if (devices.Count > 0)
            {
                var ids = string.Join("|", devices.Select(d => d.Id));
                var devResult = await WindowsPrivilegedExecutor.InvokeOnDemandElevationAsync("disable-devices", ids);
                if (!devResult.Success)
                {
                    Log.Warning("On-demand device disable returned warning: {Error}", devResult.ErrorMessage);
                }
            }

            sw.Stop();
            Log.Information("On-demand Windows block completed in {DurationMs}ms", sw.ElapsedMilliseconds);
            return OperationResult.Ok();
        }

        // 2. If already elevated, execute directly in-process for maximum performance (0ms IPC delay)
        var inProcessPolicyResult = _policyManager.SetPolicy(target, BlockStatus.Blocked);
        if (!inProcessPolicyResult.Success)
        {
            sw.Stop();
            Log.Error("Failed to set Windows registry policy in-process: {Error}", inProcessPolicyResult.ErrorMessage);
            return inProcessPolicyResult;
        }

        var inProcessDevices = await GetDevicesForTargetAsync(target, cancellationToken);
        if (inProcessDevices.Count > 0)
        {
            var deviceResult = await _deviceController.DisableDevicesAsync(inProcessDevices);
            if (!deviceResult.Success)
            {
                Log.Warning("Some physical devices could not be disabled via CfgMgr32: {Error}", deviceResult.ErrorMessage);
            }
        }

        sw.Stop();
        Log.Information("In-process Windows block completed in {DurationMs}ms", sw.ElapsedMilliseconds);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> UnblockAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Removing Windows protection: Target={Target}, IsElevated={IsElevated}",
            target, _elevationProvider.IsElevated);

        var targetStr = target.ToString().ToLowerInvariant();

        // 1. If running as standard user, use on-demand elevation
        if (!_elevationProvider.IsElevated)
        {
            Log.Information("Process is running as standard user. Requesting on-demand elevation...");

            // Remove policy via on-demand elevation
            var policyResult = await WindowsPrivilegedExecutor.InvokeOnDemandElevationAsync("remove-policy", targetStr);
            if (!policyResult.Success)
            {
                sw.Stop();
                Log.Error("On-demand policy removal elevation failed: {Error}", policyResult.ErrorMessage);
                return policyResult;
            }

            // Enable physical devices via on-demand elevation
            var devices = await GetDevicesForTargetAsync(target, cancellationToken);
            if (devices.Count > 0)
            {
                var ids = string.Join("|", devices.Select(d => d.Id));
                var devResult = await WindowsPrivilegedExecutor.InvokeOnDemandElevationAsync("enable-devices", ids);
                if (!devResult.Success)
                {
                    Log.Warning("On-demand device enable returned warning: {Error}", devResult.ErrorMessage);
                }
            }

            sw.Stop();
            Log.Information("On-demand Windows unblock completed in {DurationMs}ms", sw.ElapsedMilliseconds);
            return OperationResult.Ok();
        }

        // 2. If already elevated, execute directly in-process
        var inProcessPolicyResult = _policyManager.SetPolicy(target, BlockStatus.Allowed);
        if (!inProcessPolicyResult.Success)
        {
            sw.Stop();
            Log.Error("Failed to remove Windows registry policy in-process: {Error}", inProcessPolicyResult.ErrorMessage);
            return inProcessPolicyResult;
        }

        var inProcessDevices = await GetDevicesForTargetAsync(target, cancellationToken);
        if (inProcessDevices.Count > 0)
        {
            var deviceResult = await _deviceController.EnableDevicesAsync(inProcessDevices);
            if (!deviceResult.Success)
            {
                Log.Warning("Some physical devices could not be enabled via CfgMgr32: {Error}", deviceResult.ErrorMessage);
            }
        }

        sw.Stop();
        Log.Information("In-process Windows unblock completed in {DurationMs}ms", sw.ElapsedMilliseconds);
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
