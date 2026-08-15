using Moq;
using PrivLock.Application.Services;
using PrivLock.Domain.Capabilities;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using PrivLock.Platform.Abstractions;
using Xunit;

namespace PrivLock.Application.Tests;

public class OnDemandElevationTests
{
    private readonly Mock<IDeviceProtectionProvider> _protectionProviderMock;
    private readonly Mock<IDeviceDetector> _detectorMock;
    private readonly Mock<IPlatformCapabilityProvider> _capabilityProviderMock;
    private readonly Mock<IStateStore> _stateStoreMock;

    public OnDemandElevationTests()
    {
        _protectionProviderMock = new Mock<IDeviceProtectionProvider>();
        _detectorMock = new Mock<IDeviceDetector>();
        _capabilityProviderMock = new Mock<IPlatformCapabilityProvider>();
        _stateStoreMock = new Mock<IStateStore>();

        _capabilityProviderMock.Setup(c => c.Capabilities).Returns(new PlatformCapabilities
        {
            RequiresElevationForBlock = true,
            CameraProtectionLevel = CapabilityLevel.DualLayer,
            MicrophoneProtectionLevel = CapabilityLevel.DualLayer
        });

        _capabilityProviderMock.Setup(c => c.PlatformInfo).Returns(new PlatformInfo
        {
            OperatingSystemName = "Windows",
            OsVersion = "10.0.22631",
            Architecture = "X64",
            Is64Bit = true,
            IsElevated = false
        });

        _stateStoreMock.Setup(s => s.Load()).Returns(new DesiredState());
    }

    [Fact]
    public async Task BlockAsync_WhenStandardUser_DelegatesToProviderWhichHandlesOnDemandElevation()
    {
        // Arrange
        _protectionProviderMock
            .Setup(p => p.BlockAsync(BlockTarget.Both, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Ok());

        _protectionProviderMock
            .Setup(p => p.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockState
            {
                Camera = new DeviceBlockState { DesiredStatus = BlockStatus.Blocked, PolicyStatus = BlockStatus.Blocked },
                Microphone = new DeviceBlockState { DesiredStatus = BlockStatus.Blocked, PolicyStatus = BlockStatus.Blocked }
            });

        var service = new ProtectionService(
            _protectionProviderMock.Object,
            _detectorMock.Object,
            _capabilityProviderMock.Object,
            _stateStoreMock.Object);

        // Act
        var result = await service.BlockAsync(BlockTarget.Both);

        // Assert
        Assert.True(result.Success);
        _protectionProviderMock.Verify(p => p.BlockAsync(BlockTarget.Both, It.IsAny<CancellationToken>()), Times.Once);
        _stateStoreMock.Verify(s => s.Save(It.Is<DesiredState>(d => d.Camera == BlockStatus.Blocked && d.Microphone == BlockStatus.Blocked)), Times.Once);
    }

    [Fact]
    public async Task BlockAsync_WhenUserCancelsElevation_ReturnsFailureWithoutModifyingSavedState()
    {
        // Arrange
        _protectionProviderMock
            .Setup(p => p.BlockAsync(BlockTarget.Camera, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Fail("Operation cancelled: Administrator permissions were denied."));

        _protectionProviderMock
            .Setup(p => p.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockState
            {
                Camera = new DeviceBlockState { DesiredStatus = BlockStatus.Allowed, PolicyStatus = BlockStatus.Allowed },
                Microphone = new DeviceBlockState { DesiredStatus = BlockStatus.Allowed, PolicyStatus = BlockStatus.Allowed }
            });

        var service = new ProtectionService(
            _protectionProviderMock.Object,
            _detectorMock.Object,
            _capabilityProviderMock.Object,
            _stateStoreMock.Object);

        // Act
        var result = await service.BlockAsync(BlockTarget.Camera);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        _stateStoreMock.Verify(s => s.Save(It.IsAny<DesiredState>()), Times.Never);
    }
}
