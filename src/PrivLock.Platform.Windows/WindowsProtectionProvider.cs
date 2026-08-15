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
/// Supports clean two-tier protection:
/// 1. Standard Protection: HKCU Capability Consent Store (Packaged & NonPackaged) + Windows Core Audio WASAPI Mute (0 elevation).
/// 2. Secure Protection: HKLM Group Policy & CfgMgr32 PnP hardware device node disable (on-demand UAC elevation, single prompt).
/// </summary>
public sealed class WindowsProtectionProvider : IDeviceProtectionProvider
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsProtectionProvider>();

    private readonly WindowsDeviceDetector _deviceDetector;
    private readonly WindowsDeviceController _deviceController;
    private readonly WindowsCoreAudioController _coreAudioController;
    private readonly WindowsPolicyManager _policyManager;
    private readonly WindowsUserPrivacyManager _userPrivacyManager;
    private readonly IElevationProvider _elevationProvider;
    private readonly IStateStore _stateStore;

    public WindowsProtectionProvider(
        WindowsDeviceDetector deviceDetector,
        WindowsDeviceController deviceController,
        WindowsCoreAudioController coreAudioController,
        WindowsPolicyManager policyManager,
        WindowsUserPrivacyManager userPrivacyManager,
        IElevationProvider elevationProvider,
        IStateStore stateStore)
    {
        _deviceDetector = deviceDetector;
        _deviceController = deviceController;
        _coreAudioController = coreAudioController;
        _policyManager = policyManager;
        _userPrivacyManager = userPrivacyManager;
        _elevationProvider = elevationProvider;
        _stateStore = stateStore;
    }

    public Task<OperationResult> EnableStandardProtectionAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        Log.Information("Enabling Windows Standard Protection: Target={Target}", target);

        if (target is BlockTarget.Camera or BlockTarget.Both)
        {
            var camResult = _userPrivacyManager.SetCameraUserPrivacy(BlockStatus.Blocked);
            if (!camResult.Success)
            {
                Log.Warning("Camera user privacy block returned error: {Error}", camResult.ErrorMessage);
                return Task.FromResult(camResult);
            }
        }

        if (target is BlockTarget.Microphone or BlockTarget.Both)
        {
            var micResult = _userPrivacyManager.SetMicrophoneUserPrivacy(BlockStatus.Blocked);
            _coreAudioController.SetMicrophonesMute(true);

            if (!micResult.Success)
            {
                Log.Warning("Microphone user privacy block returned error: {Error}", micResult.ErrorMessage);
                return Task.FromResult(micResult);
            }
        }

        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> DisableStandardProtectionAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        Log.Information("Disabling Windows Standard Protection: Target={Target}", target);

        if (target is BlockTarget.Camera or BlockTarget.Both)
        {
            _userPrivacyManager.SetCameraUserPrivacy(BlockStatus.Allowed);
        }

        if (target is BlockTarget.Microphone or BlockTarget.Both)
        {
            _userPrivacyManager.SetMicrophoneUserPrivacy(BlockStatus.Allowed);
            _coreAudioController.SetMicrophonesMute(false);
        }

        return Task.FromResult(OperationResult.Ok());
    }

    public async Task<OperationResult> EnableSecureProtectionAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Enabling Windows Secure Protection: Target={Target}, IsElevated={IsElevated}",
            target, _elevationProvider.IsElevated);

        var targetStr = target.ToString().ToLowerInvariant();

        // 1. If running as standard user, elevate on-demand (reusing single-prompt persistent session)
        if (!_elevationProvider.IsElevated)
        {
            var policyResult = await WindowsPrivilegedExecutor.InvokeOnDemandElevationAsync("set-policy", targetStr);
            if (!policyResult.Success)
            {
                sw.Stop();
                Log.Error("On-demand policy elevation failed: {Error}", policyResult.ErrorMessage);
                return policyResult;
            }

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
            Log.Information("Windows Secure Protection enabled on-demand in {DurationMs}ms", sw.ElapsedMilliseconds);
            return OperationResult.Ok();
        }

        // 2. If already elevated, execute directly in-process
        var inProcessPolicyResult = _policyManager.SetPolicy(target, BlockStatus.Blocked);
        if (!inProcessPolicyResult.Success)
        {
            sw.Stop();
            return inProcessPolicyResult;
        }

        var inProcessDevices = await GetDevicesForTargetAsync(target, cancellationToken);
        if (inProcessDevices.Count > 0)
        {
            await _deviceController.DisableDevicesAsync(inProcessDevices);
        }

        sw.Stop();
        Log.Information("Windows Secure Protection enabled in-process in {DurationMs}ms", sw.ElapsedMilliseconds);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DisableSecureProtectionAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Disabling Windows Secure Protection: Target={Target}, IsElevated={IsElevated}",
            target, _elevationProvider.IsElevated);

        var targetStr = target.ToString().ToLowerInvariant();

        // 1. If running as standard user, elevate on-demand
        if (!_elevationProvider.IsElevated)
        {
            var policyResult = await WindowsPrivilegedExecutor.InvokeOnDemandElevationAsync("remove-policy", targetStr);
            if (!policyResult.Success)
            {
                sw.Stop();
                Log.Error("On-demand policy removal elevation failed: {Error}", policyResult.ErrorMessage);
                return policyResult;
            }

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
            Log.Information("Windows Secure Protection disabled on-demand in {DurationMs}ms", sw.ElapsedMilliseconds);
            return OperationResult.Ok();
        }

        // 2. If already elevated, execute directly in-process
        var inProcessPolicyResult = _policyManager.SetPolicy(target, BlockStatus.Allowed);
        if (!inProcessPolicyResult.Success)
        {
            sw.Stop();
            return inProcessPolicyResult;
        }

        var inProcessDevices = await GetDevicesForTargetAsync(target, cancellationToken);
        if (inProcessDevices.Count > 0)
        {
            await _deviceController.EnableDevicesAsync(inProcessDevices);
        }

        sw.Stop();
        Log.Information("Windows Secure Protection disabled in-process in {DurationMs}ms", sw.ElapsedMilliseconds);
        return OperationResult.Ok();
    }

    public async Task<FullProtectionState> GetProtectionStateAsync(CancellationToken cancellationToken = default)
    {
        var desired = _stateStore.Load();

        // Check real hardware and policy state
        var camPolicy = _policyManager.GetCameraPolicyStatus();
        var micPolicy = _policyManager.GetMicrophonePolicyStatus();

        var cameras = await _deviceDetector.DetectCamerasAsync(cancellationToken);
        var mics = await _deviceDetector.DetectMicrophonesAsync(cancellationToken);

        var camDeviceBlocked = cameras.Count > 0 && cameras.All(d => !d.IsEnabled);
        var micDeviceBlocked = mics.Count > 0 && mics.All(d => !d.IsEnabled);

        // Derive verified secure state
        var camSecureActive = camPolicy == BlockStatus.Blocked || camDeviceBlocked;
        var micSecureActive = micPolicy == BlockStatus.Blocked || micDeviceBlocked;

        var camStandardState = desired.CameraStandard;
        var camSecureState = camSecureActive
            ? SecureProtectionState.Active
            : (camStandardState == StandardProtectionState.Active
                ? SecureProtectionState.Available
                : SecureProtectionState.Unavailable);

        var micStandardState = desired.MicrophoneStandard;
        var micSecureState = micSecureActive
            ? SecureProtectionState.Active
            : (micStandardState == StandardProtectionState.Active
                ? SecureProtectionState.Available
                : SecureProtectionState.Unavailable);

        return new FullProtectionState
        {
            Camera = new TargetProtectionStatus
            {
                Target = BlockTarget.Camera,
                StandardState = camStandardState,
                SecureState = camSecureState,
                IsVerified = true
            },
            Microphone = new TargetProtectionStatus
            {
                Target = BlockTarget.Microphone,
                StandardState = micStandardState,
                SecureState = micSecureState,
                IsVerified = true
            }
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
}
