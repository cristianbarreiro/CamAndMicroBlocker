using System.Runtime.InteropServices;
using PrivLock.Domain.Results;
using Serilog;

namespace PrivLock.Platform.Windows.Devices;

/// <summary>
/// Controls audio capture endpoints (microphones) via Windows Core Audio (WASAPI) COM APIs.
/// Allows muting and unmuting all audio recording devices without administrator elevation.
/// </summary>
public sealed class WindowsCoreAudioController
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WindowsCoreAudioController>();

    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const uint CLSCTX_ALL = 23;
    private const uint DEVICE_STATE_ACTIVE = 1;

    public OperationResult SetMicrophonesMute(bool mute)
    {
        Log.Information("Setting Windows Core Audio capture endpoints mute={Mute}", mute);

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var hr = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE_ACTIVE, out var collection);

            if (hr != 0 || collection == null)
            {
                Log.Warning("Failed to enumerate audio capture endpoints. HR={Hr}", hr);
                return OperationResult.Fail($"Failed to enumerate audio capture devices. HR=0x{hr:X8}");
            }

            collection.GetCount(out var count);
            Log.Debug("Found {Count} active audio capture endpoints", count);

            var eventContext = Guid.Empty;
            var successCount = 0;

            for (uint i = 0; i < count; i++)
            {
                try
                {
                    if (collection.Item(i, out var device) == 0 && device != null)
                    {
                        var iid = IID_IAudioEndpointVolume;
                        var actHr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volumeObj);
                        if (actHr == 0 && volumeObj is IAudioEndpointVolume volume)
                        {
                            volume.SetMute(mute, ref eventContext);
                            successCount++;
                            Marshal.ReleaseComObject(volume);
                        }
                        Marshal.ReleaseComObject(device);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error toggling mute for audio endpoint index {Index}", i);
                }
            }

            Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(enumerator);

            Log.Information("Successfully toggled mute={Mute} on {SuccessCount}/{TotalCount} capture endpoints",
                mute, successCount, count);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Core Audio endpoint volume operation failed");
            return OperationResult.Fail($"Core Audio mute error: {ex.Message}");
        }
    }

    public bool IsDefaultMicrophoneMuted()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out var device);

            if (hr != 0 || device == null)
            {
                return false;
            }

            var iid = IID_IAudioEndpointVolume;
            var actHr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volumeObj);
            var isMuted = false;

            if (actHr == 0 && volumeObj is IAudioEndpointVolume volume)
            {
                volume.GetMute(out isMuted);
                Marshal.ReleaseComObject(volume);
            }

            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);

            return isMuted;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to query default microphone mute state");
            return false;
        }
    }

    #region COM Interop Definitions

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr pClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint pcDevices);

        [PreserveSig]
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig]
        int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

        [PreserveSig]
        int GetState(out uint pdwState);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig] int VolumeStepUp(ref Guid pguidEventContext);
        [PreserveSig] int VolumeStepDown(ref Guid pguidEventContext);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }

    private enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    #endregion
}
