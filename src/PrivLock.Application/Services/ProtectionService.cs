using System.Diagnostics;
using PrivLock.Domain.Capabilities;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using Serilog;
using Serilog.Context;

namespace PrivLock.Application.Services;

/// <summary>
/// Core application service orchestrating camera and microphone protection across all platforms.
/// Completely decoupled from operating system APIs and UI frameworks.
/// </summary>
public sealed class ProtectionService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ProtectionService>();

    private readonly IDeviceProtectionProvider _protectionProvider;
    private readonly IDeviceDetector _deviceDetector;
    private readonly IPlatformCapabilityProvider _capabilityProvider;
    private readonly IStateStore _stateStore;

    public ProtectionService(
        IDeviceProtectionProvider protectionProvider,
        IDeviceDetector deviceDetector,
        IPlatformCapabilityProvider capabilityProvider,
        IStateStore stateStore)
    {
        _protectionProvider = protectionProvider;
        _deviceDetector = deviceDetector;
        _capabilityProvider = capabilityProvider;
        _stateStore = stateStore;
    }

    /// <summary>
    /// Fired when the protection state changes.
    /// </summary>
    public event Action<BlockState>? StateChanged;

    /// <summary>
    /// Declarative security capabilities of the current platform.
    /// </summary>
    public PlatformCapabilities Capabilities => _capabilityProvider.Capabilities;

    /// <summary>
    /// Diagnostic information about the running environment.
    /// </summary>
    public PlatformInfo PlatformInfo => _capabilityProvider.PlatformInfo;

    /// <summary>
    /// Blocks the specified target (camera, microphone, or both).
    /// </summary>
    public async Task<OperationResult> BlockAsync(BlockTarget target, CancellationToken ct = default)
    {
        var opId = $"Op-{Guid.NewGuid():N}"[..11];
        using (LogContext.PushProperty("OperationId", opId))
        {
            var sw = Stopwatch.StartNew();
            Log.Information("Block requested: Target={Target}, OperationId={OperationId}", target, opId);

            // 1. Delegate to the platform-specific native protection provider
            var result = await _protectionProvider.BlockAsync(target, ct);
            if (!result.Success)
            {
                sw.Stop();
                Log.Error("Block failed: Target={Target}, Error={Error}, DurationMs={DurationMs}",
                    target, result.ErrorMessage, sw.ElapsedMilliseconds);
                return result;
            }

            // 2. Persist desired state
            var desired = _stateStore.Load();
            if (target is BlockTarget.Camera or BlockTarget.Both)
                desired.Camera = BlockStatus.Blocked;
            if (target is BlockTarget.Microphone or BlockTarget.Both)
                desired.Microphone = BlockStatus.Blocked;
            _stateStore.Save(desired);

            // 3. Reconcile and notify subscribers
            var state = await _protectionProvider.GetCurrentStateAsync(ct);
            StateChanged?.Invoke(state);

            sw.Stop();
            Log.Information("Block completed successfully: Target={Target}, DurationMs={DurationMs}",
                target, sw.ElapsedMilliseconds);

            return result;
        }
    }

    /// <summary>
    /// Unblocks the specified target (camera, microphone, or both).
    /// </summary>
    public async Task<OperationResult> UnblockAsync(BlockTarget target, CancellationToken ct = default)
    {
        var opId = $"Op-{Guid.NewGuid():N}"[..11];
        using (LogContext.PushProperty("OperationId", opId))
        {
            var sw = Stopwatch.StartNew();
            Log.Information("Unblock requested: Target={Target}, OperationId={OperationId}", target, opId);

            // 1. Delegate to the platform-specific native protection provider
            var result = await _protectionProvider.UnblockAsync(target, ct);
            if (!result.Success)
            {
                sw.Stop();
                Log.Error("Unblock failed: Target={Target}, Error={Error}, DurationMs={DurationMs}",
                    target, result.ErrorMessage, sw.ElapsedMilliseconds);
                return result;
            }

            // 2. Persist desired state
            var desired = _stateStore.Load();
            if (target is BlockTarget.Camera or BlockTarget.Both)
                desired.Camera = BlockStatus.Allowed;
            if (target is BlockTarget.Microphone or BlockTarget.Both)
                desired.Microphone = BlockStatus.Allowed;
            _stateStore.Save(desired);

            // 3. Reconcile and notify subscribers
            var state = await _protectionProvider.GetCurrentStateAsync(ct);
            StateChanged?.Invoke(state);

            sw.Stop();
            Log.Information("Unblock completed successfully: Target={Target}, DurationMs={DurationMs}",
                target, sw.ElapsedMilliseconds);

            return result;
        }
    }

    /// <summary>
    /// Toggles the blocking state. If currently blocked -> unblock; if allowed -> block.
    /// </summary>
    public async Task<OperationResult> ToggleAsync(BlockTarget target = BlockTarget.Both, CancellationToken ct = default)
    {
        var currentState = await GetCurrentStateAsync(ct);
        bool shouldBlock = target switch
        {
            BlockTarget.Camera => currentState.Camera.EffectiveStatus != BlockStatus.Blocked,
            BlockTarget.Microphone => currentState.Microphone.EffectiveStatus != BlockStatus.Blocked,
            BlockTarget.Both => !currentState.AllBlocked,
            _ => true
        };

        return shouldBlock
            ? await BlockAsync(target, ct)
            : await UnblockAsync(target, ct);
    }

    /// <summary>
    /// Gets the current verified system blocking state.
    /// </summary>
    public async Task<BlockState> GetCurrentStateAsync(CancellationToken ct = default)
    {
        var state = await _protectionProvider.GetCurrentStateAsync(ct);
        if (!state.IsFullyConsistent)
        {
            Log.Warning("State inconsistency detected: Camera(Desired={CD}, Effective={CE}), Mic(Desired={MD}, Effective={ME})",
                state.Camera.DesiredStatus, state.Camera.EffectiveStatus,
                state.Microphone.DesiredStatus, state.Microphone.EffectiveStatus);
        }
        return state;
    }

    /// <summary>
    /// Enumerates detected physical/system devices for the specified target.
    /// </summary>
    public async Task<IReadOnlyList<DeviceInfo>> GetDetectedDevicesAsync(BlockTarget target = BlockTarget.Both, CancellationToken ct = default)
    {
        return target switch
        {
            BlockTarget.Camera => await _deviceDetector.DetectCamerasAsync(ct),
            BlockTarget.Microphone => await _deviceDetector.DetectMicrophonesAsync(ct),
            BlockTarget.Both => await _deviceDetector.DetectAllAsync(ct),
            _ => []
        };
    }
}
