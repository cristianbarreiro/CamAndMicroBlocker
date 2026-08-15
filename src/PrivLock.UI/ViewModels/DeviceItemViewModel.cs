using CommunityToolkit.Mvvm.ComponentModel;
using PrivLock.Domain.Models;

namespace PrivLock.UI.ViewModels;

/// <summary>
/// ViewModel for displaying a hardware device item in the device list.
/// </summary>
public sealed partial class DeviceItemViewModel : ObservableObject
{
    public DeviceInfo Device { get; }

    public string DisplayName => $"[{Device.DeviceType}] {Device.FriendlyName}";
    public string Id => Device.Id;
    public string StatusText { get; }
    public string StatusColor { get; }
    public bool IsEnabled => Device.IsEnabled;

    public DeviceItemViewModel(DeviceInfo device, string enabledText, string disabledText)
    {
        Device = device;
        StatusText = device.IsEnabled ? enabledText : disabledText;
        StatusColor = device.IsEnabled ? "#4CAF50" : "#E57373"; // Green or Red
    }
}
