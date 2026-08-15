using System.Diagnostics;
using PrivLock.Domain.Capabilities;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using Serilog;
using Serilog.Context;

namespace PrivLock.Application.Services;

/// <summary>
/// Core application service orchestrating two-tier protection workflows (Standard and Secure),
/// enforcing strict business rules, measuring execution durations, and notifying subscribers.
/// </summary>
public sealed class ProtectionService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ProtectionService>();

    private readonly IDeviceProtectionProvider _protectionProvider;
    private readonly IDeviceDetector _deviceDetector;
    private readonly IPlatformCapabilityProvider _capabilityProvider;
    private readonly IStateStore _stateStore;

    public event Action<FullProtectionState>? StateChanged;

    public PlatformCapabilities Capabilities => _capabilityProvider.Capabilities;
    public PlatformInfo PlatformInfo => _capabilityProvider.PlatformInfo;

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
    /// Enables standard protection (without elevated permissions).
    /// Transitions Secure Protection from Unavailable to Available.
    /// </summary>
    public async Task<OperationResult> EnableStandardProtectionAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var opId = $"Op-StdEn-{Guid.NewGuid():N}"[..16];
        using var _ = LogContext.PushProperty("OperationId", opId);
        var sw = Stopwatch.StartNew();

        Log.Information("Enabling standard protection for target: {Target}", target);

        try
        {
            var result = await _protectionProvider.EnableStandardProtectionAsync(target, cancellationToken);
            if (!result.Success)
            {
                sw.Stop();
                Log.Error("Failed to enable standard protection: {Error}", result.ErrorMessage);
                return result;
            }

            // Update persisted desired state
            var desired = _stateStore.Load();
            if (target is BlockTarget.Camera or BlockTarget.Both)
            {
                desired.CameraStandard = StandardProtectionState.Active;
                if (desired.CameraSecure == SecureProtectionState.Unavailable)
                    desired.CameraSecure = SecureProtectionState.Available;
            }
            if (target is BlockTarget.Microphone or BlockTarget.Both)
            {
                desired.MicrophoneStandard = StandardProtectionState.Active;
                if (desired.MicrophoneSecure == SecureProtectionState.Unavailable)
                    desired.MicrophoneSecure = SecureProtectionState.Available;
            }
            _stateStore.Save(desired);

            // Fetch and verify actual state
            var newState = await _protectionProvider.GetProtectionStateAsync(cancellationToken);
            StateChanged?.Invoke(newState);

            sw.Stop();
            Log.Information("Standard protection enabled successfully in {DurationMs}ms", sw.ElapsedMilliseconds);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Unexpected error enabling standard protection");
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Disables standard protection.
    /// Transitions Secure Protection back to Unavailable.
    /// </summary>
    public async Task<OperationResult> DisableStandardProtectionAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var opId = $"Op-StdDis-{Guid.NewGuid():N}"[..16];
        using var _ = LogContext.PushProperty("OperationId", opId);
        var sw = Stopwatch.StartNew();

        Log.Information("Disabling standard protection for target: {Target}", target);

        try
        {
            var current = await _protectionProvider.GetProtectionStateAsync(cancellationToken);

            // If secure protection was active, disable it first to ensure clean state
            if (target is BlockTarget.Camera or BlockTarget.Both && current.Camera.SecureState == SecureProtectionState.Active)
            {
                Log.Information("Disabling active camera secure protection as part of standard teardown");
                await _protectionProvider.DisableSecureProtectionAsync(BlockTarget.Camera, cancellationToken);
            }
            if (target is BlockTarget.Microphone or BlockTarget.Both && current.Microphone.SecureState == SecureProtectionState.Active)
            {
                Log.Information("Disabling active microphone secure protection as part of standard teardown");
                await _protectionProvider.DisableSecureProtectionAsync(BlockTarget.Microphone, cancellationToken);
            }

            var result = await _protectionProvider.DisableStandardProtectionAsync(target, cancellationToken);
            if (!result.Success)
            {
                sw.Stop();
                Log.Error("Failed to disable standard protection: {Error}", result.ErrorMessage);
                return result;
            }

            // Update persisted desired state
            var desired = _stateStore.Load();
            if (target is BlockTarget.Camera or BlockTarget.Both)
            {
                desired.CameraStandard = StandardProtectionState.Inactive;
                desired.CameraSecure = SecureProtectionState.Unavailable;
            }
            if (target is BlockTarget.Microphone or BlockTarget.Both)
            {
                desired.MicrophoneStandard = StandardProtectionState.Inactive;
                desired.MicrophoneSecure = SecureProtectionState.Unavailable;
            }
            _stateStore.Save(desired);

            var newState = await _protectionProvider.GetProtectionStateAsync(cancellationToken);
            StateChanged?.Invoke(newState);

            sw.Stop();
            Log.Information("Standard protection disabled successfully in {DurationMs}ms", sw.ElapsedMilliseconds);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Unexpected error disabling standard protection");
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Enables secure / administrator protection (requests on-demand elevation).
    /// Enforces precondition: Standard Protection MUST be active first.
    /// </summary>
    public async Task<OperationResult> EnableSecureProtectionAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var opId = $"Op-SecEn-{Guid.NewGuid():N}"[..16];
        using var _ = LogContext.PushProperty("OperationId", opId);
        var sw = Stopwatch.StartNew();

        Log.Information("Requesting secure protection for target: {Target}", target);

        try
        {
            var current = await _protectionProvider.GetProtectionStateAsync(cancellationToken);

            // Precondition validation: Standard protection must be Active
            if (target is BlockTarget.Camera or BlockTarget.Both && current.Camera.StandardState != StandardProtectionState.Active)
            {
                sw.Stop();
                var msg = "You must enable Standard Protection before enabling Secure Protection for Camera.";
                Log.Warning(msg);
                return OperationResult.Fail(msg);
            }

            if (target is BlockTarget.Microphone or BlockTarget.Both && current.Microphone.StandardState != StandardProtectionState.Active)
            {
                sw.Stop();
                var msg = "You must enable Standard Protection before enabling Secure Protection for Microphone.";
                Log.Warning(msg);
                return OperationResult.Fail(msg);
            }

            // Perform privileged secure protection (on-demand elevation)
            var result = await _protectionProvider.EnableSecureProtectionAsync(target, cancellationToken);
            if (!result.Success)
            {
                sw.Stop();
                Log.Error("Failed to enable secure protection: {Error}", result.ErrorMessage);
                return result;
            }

            // Update persisted desired state
            var desired = _stateStore.Load();
            if (target is BlockTarget.Camera or BlockTarget.Both)
                desired.CameraSecure = SecureProtectionState.Active;
            if (target is BlockTarget.Microphone or BlockTarget.Both)
                desired.MicrophoneSecure = SecureProtectionState.Active;
            _stateStore.Save(desired);

            var newState = await _protectionProvider.GetProtectionStateAsync(cancellationToken);
            StateChanged?.Invoke(newState);

            sw.Stop();
            Log.Information("Secure protection enabled and verified in {DurationMs}ms", sw.ElapsedMilliseconds);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Unexpected error enabling secure protection");
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Disables secure / administrator protection.
    /// Standard protection remains active.
    /// </summary>
    public async Task<OperationResult> DisableSecureProtectionAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var opId = $"Op-SecDis-{Guid.NewGuid():N}"[..16];
        using var _ = LogContext.PushProperty("OperationId", opId);
        var sw = Stopwatch.StartNew();

        Log.Information("Disabling secure protection for target: {Target}", target);

        try
        {
            var result = await _protectionProvider.DisableSecureProtectionAsync(target, cancellationToken);
            if (!result.Success)
            {
                sw.Stop();
                Log.Error("Failed to disable secure protection: {Error}", result.ErrorMessage);
                return result;
            }

            var desired = _stateStore.Load();
            if (target is BlockTarget.Camera or BlockTarget.Both)
                desired.CameraSecure = SecureProtectionState.Available;
            if (target is BlockTarget.Microphone or BlockTarget.Both)
                desired.MicrophoneSecure = SecureProtectionState.Available;
            _stateStore.Save(desired);

            var newState = await _protectionProvider.GetProtectionStateAsync(cancellationToken);
            StateChanged?.Invoke(newState);

            sw.Stop();
            Log.Information("Secure protection disabled successfully in {DurationMs}ms", sw.ElapsedMilliseconds);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Unexpected error disabling secure protection");
            return OperationResult.Fail(ex.Message);
        }
    }

    public Task<FullProtectionState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        return _protectionProvider.GetProtectionStateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DeviceInfo>> GetDetectedDevicesAsync(CancellationToken cancellationToken = default)
    {
        return _deviceDetector.DetectAllAsync(cancellationToken);
    }
}
