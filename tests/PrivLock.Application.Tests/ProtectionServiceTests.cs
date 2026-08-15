using Moq;
using PrivLock.Application.Services;
using PrivLock.Domain.Capabilities;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using Xunit;

namespace PrivLock.Application.Tests;

public class ProtectionServiceTests
{
    private readonly Mock<IDeviceProtectionProvider> _protectionMock;
    private readonly Mock<IDeviceDetector> _detectorMock;
    private readonly Mock<IPlatformCapabilityProvider> _capabilityMock;
    private readonly Mock<IStateStore> _storeMock;
    private readonly ProtectionService _service;

    public ProtectionServiceTests()
    {
        _protectionMock = new Mock<IDeviceProtectionProvider>();
        _detectorMock = new Mock<IDeviceDetector>();
        _capabilityMock = new Mock<IPlatformCapabilityProvider>();
        _storeMock = new Mock<IStateStore>();

        _capabilityMock.Setup(c => c.Capabilities).Returns(new PlatformCapabilities
        {
            CameraProtectionLevel = CapabilityLevel.Hardware,
            MicrophoneProtectionLevel = CapabilityLevel.Hardware
        });
        _capabilityMock.Setup(c => c.PlatformInfo).Returns(new PlatformInfo
        {
            OperatingSystemName = "GenericOS",
            OsVersion = "1.0",
            Architecture = "X64",
            Is64Bit = true,
            IsElevated = true
        });

        _detectorMock.Setup(d => d.DetectCamerasAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeviceInfo>());
        _detectorMock.Setup(d => d.DetectMicrophonesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeviceInfo>());
        _detectorMock.Setup(d => d.DetectAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeviceInfo>());

        _protectionMock.Setup(p => p.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockState());

        _storeMock.Setup(s => s.Load()).Returns(new DesiredState());

        _service = new ProtectionService(
            _protectionMock.Object,
            _detectorMock.Object,
            _capabilityMock.Object,
            _storeMock.Object);
    }

    [Fact]
    public async Task BlockAsync_Both_DelegatesToProviderAndSavesState()
    {
        _protectionMock.Setup(p => p.BlockAsync(BlockTarget.Both, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Ok());

        var result = await _service.BlockAsync(BlockTarget.Both);

        Assert.True(result.Success);
        _protectionMock.Verify(p => p.BlockAsync(BlockTarget.Both, It.IsAny<CancellationToken>()), Times.Once);
        _storeMock.Verify(s => s.Save(It.Is<DesiredState>(
            ds => ds.Camera == BlockStatus.Blocked && ds.Microphone == BlockStatus.Blocked)), Times.Once);
    }

    [Fact]
    public async Task BlockAsync_ProviderFails_DoesNotSaveState()
    {
        _protectionMock.Setup(p => p.BlockAsync(BlockTarget.Both, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Fail("Hardware access error"));

        var result = await _service.BlockAsync(BlockTarget.Both);

        Assert.False(result.Success);
        Assert.Equal("Hardware access error", result.ErrorMessage);
        _storeMock.Verify(s => s.Save(It.IsAny<DesiredState>()), Times.Never);
    }

    [Fact]
    public async Task UnblockAsync_Camera_UpdatesOnlyCameraState()
    {
        _protectionMock.Setup(p => p.UnblockAsync(BlockTarget.Camera, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Ok());
        _storeMock.Setup(s => s.Load()).Returns(new DesiredState
        {
            Camera = BlockStatus.Blocked,
            Microphone = BlockStatus.Blocked
        });

        var result = await _service.UnblockAsync(BlockTarget.Camera);

        Assert.True(result.Success);
        _storeMock.Verify(s => s.Save(It.Is<DesiredState>(
            ds => ds.Camera == BlockStatus.Allowed && ds.Microphone == BlockStatus.Blocked)), Times.Once);
    }

    [Fact]
    public async Task ToggleAsync_WhenCurrentlyAllowed_Blocks()
    {
        _protectionMock.Setup(p => p.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockState
            {
                Camera = new DeviceBlockState { PolicyStatus = BlockStatus.Allowed, DeviceStatus = BlockStatus.Allowed },
                Microphone = new DeviceBlockState { PolicyStatus = BlockStatus.Allowed, DeviceStatus = BlockStatus.Allowed }
            });

        _protectionMock.Setup(p => p.BlockAsync(BlockTarget.Both, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Ok());

        var result = await _service.ToggleAsync(BlockTarget.Both);

        Assert.True(result.Success);
        _protectionMock.Verify(p => p.BlockAsync(BlockTarget.Both, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StateChanged_FiresAfterBlock()
    {
        var eventFired = false;
        _service.StateChanged += _ => eventFired = true;

        _protectionMock.Setup(p => p.BlockAsync(BlockTarget.Both, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Ok());

        await _service.BlockAsync(BlockTarget.Both);

        Assert.True(eventFired);
    }
}
