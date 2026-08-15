using PrivLock.Domain.Models;

namespace PrivLock.Platform.Abstractions;

/// <summary>
/// Persists the user's desired blocking state and settings across application restarts.
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// Loads the persisted desired state, returning defaults if not found.
    /// </summary>
    DesiredState Load();

    /// <summary>
    /// Saves the current desired state to persistent storage.
    /// </summary>
    void Save(DesiredState state);
}

/// <summary>
/// User configuration and desired blocking state persisted across sessions.
/// </summary>
public sealed class DesiredState
{
    public BlockStatus Camera { get; set; } = BlockStatus.Allowed;
    public BlockStatus Microphone { get; set; } = BlockStatus.Allowed;
    public bool StartWithSystem { get; set; } = false;
    public string Language { get; set; } = "es";
    public bool StartMinimized { get; set; } = false;
}
