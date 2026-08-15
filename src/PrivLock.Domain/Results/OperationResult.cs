namespace PrivLock.Domain.Results;

/// <summary>
/// Detail of an operation performed on a specific hardware device or endpoint.
/// </summary>
public sealed class DeviceOperationDetail
{
    public required string DeviceId { get; init; }
    public required string FriendlyName { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of a protection or system operation.
/// </summary>
public sealed class OperationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<DeviceOperationDetail> Details { get; init; } = [];

    public static OperationResult Ok(IReadOnlyList<DeviceOperationDetail>? details = null) =>
        new() { Success = true, Details = details ?? [] };

    public static OperationResult Fail(string error, IReadOnlyList<DeviceOperationDetail>? details = null) =>
        new() { Success = false, ErrorMessage = error, Details = details ?? [] };
}
