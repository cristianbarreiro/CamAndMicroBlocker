namespace PrivLock.Infrastructure.Common.Localization;

/// <summary>
/// Pure C# in-memory localization provider containing built-in English and Spanish translation catalogs.
/// </summary>
public static class LocalizationCatalog
{
    private static readonly Dictionary<string, string> StringsEn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppTitle"] = "PrivLock",
        ["AppSubtitle"] = "Camera & Microphone Blocker",
        ["StatusBlocked"] = "🔒 Blocked",
        ["StatusAllowed"] = "✅ Allowed",
        ["StatusUnknown"] = "⚠️ Unknown",
        ["ProtectionActive"] = "Protection active (Both Blocked)",
        ["ProtectionInactive"] = "Protection inactive (Both Allowed)",
        ["MixedState"] = "Mixed state",
        ["CameraTitle"] = "Camera",
        ["CameraSubtitle"] = "Webcams and video capture devices",
        ["MicrophoneTitle"] = "Microphone",
        ["MicrophoneSubtitle"] = "Audio recording and input endpoints",
        ["MasterToggle"] = "Block All (Camera & Microphone)",
        ["MasterSubtitle"] = "Simultaneous protection toggle",
        ["DetectedDevices"] = "Detected Hardware Devices",
        ["NoDevicesDetected"] = "No devices detected",
        ["DeviceEnabled"] = "ENABLED",
        ["DeviceDisabled"] = "DISABLED",
        ["DevicePresent"] = "PRESENT",
        ["StartWithSystem"] = "Start with system",
        ["Language"] = "Language",
        ["CapabilitiesTitle"] = "Platform Security Capabilities",
        ["CapabilityHardware"] = "Hardware PnP Control",
        ["CapabilityPolicy"] = "System Privacy Policy",
        ["CapabilityAudioMute"] = "Audio Server Mute & Lock",
        ["CapabilityElevation"] = "Elevated Privileges Required",
        ["TrayShowApp"] = "Show Application",
        ["TrayHideApp"] = "Hide Application",
        ["TrayLockBoth"] = "Lock (Both)",
        ["TrayUnlockBoth"] = "Unlock (Both)",
        ["TrayLockUnlock"] = "Lock / Unlock",
        ["TrayExit"] = "Exit Application",
        ["NotifyBothBlocked"] = "Camera & Microphone: BLOCKED",
        ["NotifyBothAllowed"] = "Camera & Microphone: ALLOWED",
        ["NotifyCameraBlocked"] = "Camera: BLOCKED",
        ["NotifyMicBlocked"] = "Microphone: BLOCKED",
        ["ErrorElevationDenied"] = "Administrator permission was denied.",
        ["ErrorOperationFailed"] = "Operation failed"
    };

    private static readonly Dictionary<string, string> StringsEs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppTitle"] = "PrivLock",
        ["AppSubtitle"] = "Bloqueador de Cámara y Micrófono",
        ["StatusBlocked"] = "🔒 Bloqueado",
        ["StatusAllowed"] = "✅ Permitido",
        ["StatusUnknown"] = "⚠️ Desconocido",
        ["ProtectionActive"] = "Protección activa (Ambos bloqueados)",
        ["ProtectionInactive"] = "Protección inactiva (Ambos permitidos)",
        ["MixedState"] = "Estado mixto",
        ["CameraTitle"] = "Cámara",
        ["CameraSubtitle"] = "Cámaras web y dispositivos de captura de video",
        ["MicrophoneTitle"] = "Micrófono",
        ["MicrophoneSubtitle"] = "Entradas de audio y micrófonos del sistema",
        ["MasterToggle"] = "Bloquear todo (Cámara y Micrófono)",
        ["MasterSubtitle"] = "Control simultáneo de protección",
        ["DetectedDevices"] = "Dispositivos de Hardware Detectados",
        ["NoDevicesDetected"] = "No se detectaron dispositivos",
        ["DeviceEnabled"] = "HABILITADO",
        ["DeviceDisabled"] = "DESHABILITADO",
        ["DevicePresent"] = "PRESENTE",
        ["StartWithSystem"] = "Iniciar con el sistema",
        ["Language"] = "Idioma",
        ["CapabilitiesTitle"] = "Capacidades de Seguridad de la Plataforma",
        ["CapabilityHardware"] = "Control Hardware PnP",
        ["CapabilityPolicy"] = "Directivas de Privacidad del SO",
        ["CapabilityAudioMute"] = "Silenciamiento/Bloqueo Servidor Audio",
        ["CapabilityElevation"] = "Requiere Privilegios Elevados",
        ["TrayShowApp"] = "Mostrar Aplicación",
        ["TrayHideApp"] = "Ocultar Aplicación",
        ["TrayLockBoth"] = "Bloquear (Ambos)",
        ["TrayUnlockBoth"] = "Desbloquear (Ambos)",
        ["TrayLockUnlock"] = "Bloquear / Desbloquear",
        ["TrayExit"] = "Salir de la Aplicación",
        ["NotifyBothBlocked"] = "Cámara y Micrófono: BLOQUEADOS",
        ["NotifyBothAllowed"] = "Cámara y Micrófono: PERMITIDOS",
        ["NotifyCameraBlocked"] = "Cámara: BLOQUEADA",
        ["NotifyMicBlocked"] = "Micrófono: BLOQUEADO",
        ["ErrorElevationDenied"] = "Se denegaron los permisos de administrador.",
        ["ErrorOperationFailed"] = "La operación ha fallado"
    };

    public static string Get(string key, string language = "es", string fallback = "")
    {
        var dict = language.Equals("en", StringComparison.OrdinalIgnoreCase) ? StringsEn : StringsEs;
        if (dict.TryGetValue(key, out var val))
            return val;

        // Fallback to English dictionary before empty string
        if (StringsEn.TryGetValue(key, out var enVal))
            return enVal;

        return string.IsNullOrEmpty(fallback) ? key : fallback;
    }

    public static IReadOnlyDictionary<string, string> GetAll(string language = "es") =>
        language.Equals("en", StringComparison.OrdinalIgnoreCase) ? StringsEn : StringsEs;
}
