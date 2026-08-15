using Microsoft.Win32;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
using Serilog;

namespace PrivLock.Platform.Windows.Policies;

/// <summary>
/// Manages user-level privacy permissions in the Windows Capability Consent Store (HKCU).
/// Enforces standard camera and microphone blocking on Windows without requiring administrative elevation.
/// </summary>
public sealed class WindowsUserPrivacyManager
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsUserPrivacyManager>();

    private const string ConsentStoreCameraPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
    private const string ConsentStoreCameraNonPackagedPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged";
    private const string ConsentStoreMicPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    private const string ConsentStoreMicNonPackagedPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\NonPackaged";

    public OperationResult SetCameraUserPrivacy(BlockStatus status)
    {
        Log.Information("Setting Windows user privacy for Camera: Status={Status}", status);

        try
        {
            var isBlocked = status == BlockStatus.Blocked;
            var consentValue = isBlocked ? "Deny" : "Allow";

            // 1. Consent Store (UWP / Windows Camera App / Packaged Apps)
            SetRegistryStringValue(Registry.CurrentUser, ConsentStoreCameraPath, "Value", consentValue);

            // 2. Consent Store NonPackaged (Desktop Apps: Chrome, Firefox, Zoom, Teams, OBS, etc.)
            SetRegistryStringValue(Registry.CurrentUser, ConsentStoreCameraNonPackagedPath, "Value", consentValue);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update Windows Camera user privacy in ConsentStore");
            return OperationResult.Fail($"Camera user privacy error: {ex.Message}");
        }
    }

    public OperationResult SetMicrophoneUserPrivacy(BlockStatus status)
    {
        Log.Information("Setting Windows user privacy for Microphone: Status={Status}", status);

        try
        {
            var isBlocked = status == BlockStatus.Blocked;
            var consentValue = isBlocked ? "Deny" : "Allow";

            // 1. Consent Store (UWP / Voice Recorder / Packaged Apps)
            SetRegistryStringValue(Registry.CurrentUser, ConsentStoreMicPath, "Value", consentValue);

            // 2. Consent Store NonPackaged (Desktop Apps: Chrome, Zoom, Teams, Discord, etc.)
            SetRegistryStringValue(Registry.CurrentUser, ConsentStoreMicNonPackagedPath, "Value", consentValue);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update Windows Microphone user privacy in ConsentStore");
            return OperationResult.Fail($"Microphone user privacy error: {ex.Message}");
        }
    }

    public BlockStatus GetCameraUserPrivacyStatus()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ConsentStoreCameraPath);
            var val = key?.GetValue("Value")?.ToString();
            return string.Equals(val, "Deny", StringComparison.OrdinalIgnoreCase)
                ? BlockStatus.Blocked
                : BlockStatus.Allowed;
        }
        catch
        {
            return BlockStatus.Allowed;
        }
    }

    public BlockStatus GetMicrophoneUserPrivacyStatus()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ConsentStoreMicPath);
            var val = key?.GetValue("Value")?.ToString();
            return string.Equals(val, "Deny", StringComparison.OrdinalIgnoreCase)
                ? BlockStatus.Blocked
                : BlockStatus.Allowed;
        }
        catch
        {
            return BlockStatus.Allowed;
        }
    }

    private static void SetRegistryStringValue(RegistryKey root, string subKeyPath, string valueName, string value)
    {
        try
        {
            using var key = root.CreateSubKey(subKeyPath, writable: true);
            key?.SetValue(valueName, value, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not set registry string value for {Path}\\{Name}", subKeyPath, valueName);
        }
    }
}
