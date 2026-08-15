using PrivLock.Domain.Models;
using PrivLock.Domain.Results;

namespace PrivLock.Platform.Abstractions;

/// <summary>
/// Controls blocking, unblocking, and status verification for cameras and microphones.
/// Implemented natively for each supported operating system.
/// </summary>
public interface IDeviceProtectionProvider
{
    /// <summary>
    /// Blocks the specified target (camera, microphone, or both).
    /// </summary>
    Task<OperationResult> BlockAsync(BlockTarget target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unblocks the specified target (camera, microphone, or both).
    /// </summary>
    Task<OperationResult> UnblockAsync(BlockTarget target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current camera protection state (desired, policy, and hardware/stream status).
    /// </summary>
    Task<DeviceBlockState> GetCameraStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current microphone protection state (desired, policy, and hardware/stream status).
    /// </summary>
    Task<DeviceBlockState> GetMicrophoneStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles and gets the overall blocking state.
    /// </summary>
    Task<BlockState> GetCurrentStateAsync(CancellationToken cancellationToken = default);
}
