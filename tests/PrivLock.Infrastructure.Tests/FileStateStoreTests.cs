using PrivLock.Domain.Models;
using PrivLock.Infrastructure.Common.Storage;
using PrivLock.Platform.Abstractions;
using Xunit;

namespace PrivLock.Infrastructure.Tests;

public class FileStateStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testFilePath;

    public FileStateStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"PrivLock_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _testFilePath = Path.Combine(_testDir, "state.json");
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var store = new FileStateStore(_testDir);
        var state = store.Load();

        Assert.Equal(BlockStatus.Allowed, state.Camera);
        Assert.Equal(BlockStatus.Allowed, state.Microphone);
        Assert.False(state.StartWithSystem);
        Assert.Equal("es", state.Language);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesState()
    {
        var store = new FileStateStore(_testDir);
        var original = new DesiredState
        {
            Camera = BlockStatus.Blocked,
            Microphone = BlockStatus.Allowed,
            StartWithSystem = true,
            Language = "en"
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(BlockStatus.Blocked, loaded.Camera);
        Assert.Equal(BlockStatus.Allowed, loaded.Microphone);
        Assert.True(loaded.StartWithSystem);
        Assert.Equal("en", loaded.Language);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(_testFilePath, "{ corrupt json [[[]");

        var store = new FileStateStore(_testDir);
        var state = store.Load();

        Assert.Equal(BlockStatus.Allowed, state.Camera);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* Best effort */ }
    }
}
