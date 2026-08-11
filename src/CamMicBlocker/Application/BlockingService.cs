using CamMicBlocker.Domain.Interfaces;
using CamMicBlocker.Domain.Models;
using Serilog;

namespace CamMicBlocker.Application;

/// <summary>
/// Orchestrates the block/unblock workflow:
/// 1. Detect target devices
/// 2. Set/remove privacy policy (elevated)
/// 3. Disable/enable devices (elevated)
/// 4. Update desired state
/// 5. Reconcile actual state
/// 
/// This service coordinates the domain interfaces; it does NOT perform
/// privileged operations directly.
/// </summary>
public sealed class BlockingService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<BlockingService>();

    private readonly IDeviceDetector _deviceDetector;
    private readonly IDeviceController _deviceController;
    private readonly IPolicyManager _policyManager;
    private readonly IStateStore _stateStore;

    public BlockingService(
        IDeviceDetector deviceDetector,
        IDeviceController deviceController,
        IPolicyManager policyManager,
        IStateStore stateStore)
    {
        _deviceDetector = deviceDetector;
        _deviceController = deviceController;
        _policyManager = policyManager;
        _stateStore = stateStore;
    }

    /// <summary>
    /// Fired when the blocking state changes (after a block/unblock operation or reconciliation).
    /// </summary>
    public event Action<BlockState>? StateChanged;

    /// <summary>
    /// Blocks the specified target (camera, microphone, or both).
    /// Triggers a UAC prompt for the elevated operations.
    /// </summary>
    public async Task<OperationResult> BlockAsync(BlockTarget target)
    {
        Log.Information("Block requested: {Target}", target);

        // 1. Set privacy policy
        var policyResult = await _policyManager.SetPolicyAsync(target, BlockStatus.Blocked);
        if (!policyResult.Success)
        {
            Log.Error("Failed to set policy: {Error}", policyResult.ErrorMessage);
            return policyResult;
        }

        // 2. Detect and disable physical devices
        var devices = GetDevicesForTarget(target);
        if (devices.Count > 0)
        {
            var deviceResult = await _deviceController.DisableDevicesAsync(devices);
            if (!deviceResult.Success)
            {
                Log.Warning("Some devices could not be disabled: {Error}. Policy was still applied.", deviceResult.ErrorMessage);
                // Don't return failure — the policy is still applied which provides protection
            }
        }
        else
        {
            Log.Warning("No {Target} devices detected to disable, but policy was applied", target);
        }

        // 3. Update desired state
        var state = _stateStore.Load();
        if (target is BlockTarget.Camera or BlockTarget.Both)
            state.Camera = BlockStatus.Blocked;
        if (target is BlockTarget.Microphone or BlockTarget.Both)
            state.Microphone = BlockStatus.Blocked;
        _stateStore.Save(state);

        // 4. Reconcile and notify
        var blockState = GetCurrentState();
        StateChanged?.Invoke(blockState);

        Log.Information("Block completed for {Target}", target);
        return OperationResult.Ok();
    }

    /// <summary>
    /// Unblocks the specified target (camera, microphone, or both).
    /// Triggers a UAC prompt for the elevated operations.
    /// </summary>
    public async Task<OperationResult> UnblockAsync(BlockTarget target)
    {
        Log.Information("Unblock requested: {Target}", target);

        // 1. Remove privacy policy
        var policyResult = await _policyManager.SetPolicyAsync(target, BlockStatus.Allowed);
        if (!policyResult.Success)
        {
            Log.Error("Failed to remove policy: {Error}", policyResult.ErrorMessage);
            return policyResult;
        }

        // 2. Detect and enable physical devices
        var devices = GetDevicesForTarget(target);
        if (devices.Count > 0)
        {
            var deviceResult = await _deviceController.EnableDevicesAsync(devices);
            if (!deviceResult.Success)
            {
                Log.Warning("Some devices could not be enabled: {Error}", deviceResult.ErrorMessage);
            }
        }

        // 3. Update desired state
        var state = _stateStore.Load();
        if (target is BlockTarget.Camera or BlockTarget.Both)
            state.Camera = BlockStatus.Allowed;
        if (target is BlockTarget.Microphone or BlockTarget.Both)
            state.Microphone = BlockStatus.Allowed;
        _stateStore.Save(state);

        // 4. Reconcile and notify
        var blockState = GetCurrentState();
        StateChanged?.Invoke(blockState);

        Log.Information("Unblock completed for {Target}", target);
        return OperationResult.Ok();
    }

    /// <summary>
    /// Toggles the blocking state. If currently blocked → unblock; if allowed → block.
    /// When state is mixed/unknown, defaults to blocking for safety.
    /// </summary>
    public async Task<OperationResult> ToggleAsync(BlockTarget target = BlockTarget.Both)
    {
        var currentState = GetCurrentState();
        bool shouldBlock = target switch
        {
            BlockTarget.Camera => currentState.Camera.EffectiveStatus != BlockStatus.Blocked,
            BlockTarget.Microphone => currentState.Microphone.EffectiveStatus != BlockStatus.Blocked,
            BlockTarget.Both => !currentState.AllBlocked,
            _ => true
        };

        return shouldBlock
            ? await BlockAsync(target)
            : await UnblockAsync(target);
    }

    /// <summary>
    /// Reads the current actual state from the system (registry + devices)
    /// and compares with the desired state.
    /// </summary>
    public BlockState GetCurrentState()
    {
        var desired = _stateStore.Load();

        // Read policy state (doesn't require admin)
        var cameraPolicyStatus = _policyManager.GetCameraPolicyStatus();
        var micPolicyStatus = _policyManager.GetMicrophonePolicyStatus();

        // Read device state
        var cameras = _deviceDetector.DetectCameras();
        var microphones = _deviceDetector.DetectMicrophones();

        var cameraDeviceStatus = DetermineDeviceStatus(cameras);
        var micDeviceStatus = DetermineDeviceStatus(microphones);

        var state = new BlockState
        {
            Camera = new DeviceBlockState
            {
                DesiredStatus = desired.Camera,
                PolicyStatus = cameraPolicyStatus,
                DeviceStatus = cameraDeviceStatus
            },
            Microphone = new DeviceBlockState
            {
                DesiredStatus = desired.Microphone,
                PolicyStatus = micPolicyStatus,
                DeviceStatus = micDeviceStatus
            }
        };

        if (!state.IsFullyConsistent)
        {
            Log.Warning("State inconsistency detected: Camera(desired={CD}, effective={CE}), Mic(desired={MD}, effective={ME})",
                state.Camera.DesiredStatus, state.Camera.EffectiveStatus,
                state.Microphone.DesiredStatus, state.Microphone.EffectiveStatus);
        }

        return state;
    }

    /// <summary>
    /// Gets the list of detected devices relevant to the specified target.
    /// </summary>
    public IReadOnlyList<DeviceInfo> GetDetectedDevices(BlockTarget target = BlockTarget.Both)
    {
        return GetDevicesForTarget(target);
    }

    private List<DeviceInfo> GetDevicesForTarget(BlockTarget target)
    {
        var devices = new List<DeviceInfo>();

        if (target is BlockTarget.Camera or BlockTarget.Both)
            devices.AddRange(_deviceDetector.DetectCameras());

        if (target is BlockTarget.Microphone or BlockTarget.Both)
            devices.AddRange(_deviceDetector.DetectMicrophones());

        return devices;
    }

    /// <summary>
    /// Determines the aggregate device status from a list of devices.
    /// If ALL devices are disabled → Blocked.
    /// If ALL devices are enabled → Allowed.
    /// If no devices found → Unknown.
    /// Mixed states → Unknown (reported as inconsistency).
    /// </summary>
    private static BlockStatus DetermineDeviceStatus(IReadOnlyList<DeviceInfo> devices)
    {
        if (devices.Count == 0)
            return BlockStatus.Unknown;

        var allEnabled = devices.All(d => d.IsEnabled);
        var allDisabled = devices.All(d => !d.IsEnabled);

        if (allDisabled) return BlockStatus.Blocked;
        if (allEnabled) return BlockStatus.Allowed;
        return BlockStatus.Unknown; // Mixed state
    }
}
