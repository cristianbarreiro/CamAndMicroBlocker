using System.Diagnostics;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using PrivLock.Platform.Linux.Devices;
using Serilog;

namespace PrivLock.Platform.Linux;

/// <summary>
/// Native Linux implementation of IDeviceProtectionProvider.
/// Coordinates V4L2 device node control and PipeWire/PulseAudio source management.
/// </summary>
public sealed class LinuxProtectionProvider : IDeviceProtectionProvider
{
    private static readonly ILogger Log = Serilog.Log.ForContext<LinuxProtectionProvider>();

    private readonly LinuxDeviceDetector _deviceDetector;
    private readonly LinuxDeviceController _deviceController;
    private readonly IStateStore _stateStore;

    public LinuxProtectionProvider(
        LinuxDeviceDetector deviceDetector,
        LinuxDeviceController deviceController,
        IStateStore stateStore)
    {
        _deviceDetector = deviceDetector;
        _deviceController = deviceController;
        _stateStore = stateStore;
    }

    public async Task<OperationResult> BlockAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Applying Linux protection: Target={Target}", target);

        if (target is BlockTarget.Camera or BlockTarget.Both)
        {
            var cameras = await _deviceDetector.DetectCamerasAsync(cancellationToken);
            var camResult = await _deviceController.BlockCameraAsync(cameras);
            if (!camResult.Success)
            {
                Log.Warning("Linux camera block had partial errors: {Error}", camResult.ErrorMessage);
            }
        }

        if (target is BlockTarget.Microphone or BlockTarget.Both)
        {
            var micResult = await _deviceController.BlockMicrophoneAsync();
            if (!micResult.Success)
            {
                Log.Warning("Linux microphone block had partial errors: {Error}", micResult.ErrorMessage);
            }
        }

        sw.Stop();
        Log.Information("Linux block completed in {DurationMs}ms", sw.ElapsedMilliseconds);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> UnblockAsync(BlockTarget target, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Information("Removing Linux protection: Target={Target}", target);

        if (target is BlockTarget.Camera or BlockTarget.Both)
        {
            var cameras = await _deviceDetector.DetectCamerasAsync(cancellationToken);
            await _deviceController.UnblockCameraAsync(cameras);
        }

        if (target is BlockTarget.Microphone or BlockTarget.Both)
        {
            await _deviceController.UnblockMicrophoneAsync();
        }

        sw.Stop();
        Log.Information("Linux unblock completed in {DurationMs}ms", sw.ElapsedMilliseconds);
        return OperationResult.Ok();
    }

    public Task<DeviceBlockState> GetCameraStatusAsync(CancellationToken cancellationToken = default)
    {
        var desired = _stateStore.Load();
        return Task.FromResult(new DeviceBlockState
        {
            DesiredStatus = desired.Camera,
            PolicyStatus = BlockStatus.Unknown, // Linux has no group policy registry
            DeviceStatus = desired.Camera // Reflected from active state
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
