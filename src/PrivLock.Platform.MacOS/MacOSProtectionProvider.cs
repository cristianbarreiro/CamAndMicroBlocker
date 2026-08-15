using System.Diagnostics;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using PrivLock.Platform.MacOS.Devices;
using Serilog;

namespace PrivLock.Platform.MacOS;

/// <summary>
/// Native macOS implementation of IDeviceProtectionProvider.
/// Coordinates CoreAudio hardware input muting and AVFoundation camera state tracking.
/// </summary>
public sealed class MacOSProtectionProvider : IDeviceProtectionProvider
{
    private static readonly ILogger Log = Serilog.Log.ForContext<MacOSProtectionProvider>();

    private readonly MacOSDeviceDetector _deviceDetector;
    private readonly MacOSDeviceController _deviceController;
    private readonly IStateStore _stateStore;

    public MacOSProtectionProvider(
        MacOSDeviceDetector deviceDetector,
        MacOSDeviceController deviceController,
        IStateStore stateStore)
    {
        _deviceDetector = deviceDetector;
        _deviceController = deviceController;
        _stateStore = stateStore;
    }

    public async Task<OperationResult> BlockAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Applying macOS protection: Target={Target}", target);

        if (target is BlockTarget.Microphone or BlockTarget.Both)
        {
            var micResult = await _deviceController.BlockMicrophoneAsync();
            if (!micResult.Success)
            {
                Log.Warning("macOS microphone block warning: {Error}", micResult.ErrorMessage);
            }
        }

        sw.Stop();
        Log.Information("macOS block completed in {DurationMs}ms", sw.ElapsedMilliseconds);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> UnblockAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Removing macOS protection: Target={Target}", target);

        if (target is BlockTarget.Microphone or BlockTarget.Both)
        {
            await _deviceController.UnblockMicrophoneAsync();
        }

        sw.Stop();
        Log.Information("macOS unblock completed in {DurationMs}ms", sw.ElapsedMilliseconds);
        return OperationResult.Ok();
    }

    public Task<DeviceBlockState> GetCameraStatusAsync(CancellationToken cancellationToken = default)
    {
        var desired = _stateStore.Load();
        return Task.FromResult(new DeviceBlockState
        {
            DesiredStatus = desired.Camera,
            PolicyStatus = BlockStatus.Unknown,
            DeviceStatus = desired.Camera
        });
    }

    public Task<DeviceBlockState> GetMicrophoneStatusAsync(CancellationToken cancellationToken = default)
    {
        var desired = _stateStore.Load();
        return Task.FromResult(new DeviceBlockState
        {
            DesiredStatus = desired.Microphone,
            PolicyStatus = BlockStatus.Unknown,
            DeviceStatus = desired.Microphone
        });
    }

    public async Task<BlockState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        var cam = await GetCameraStatusAsync(cancellationToken);
        var mic = await GetMicrophoneStatusAsync(cancellationToken);
        return new BlockState { Camera = cam, Microphone = mic };
    }
}
