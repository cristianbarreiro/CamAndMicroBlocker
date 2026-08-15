using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivLock.Application.Services;
using PrivLock.Domain.Models;
using Serilog;

namespace PrivLock.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly ILogger Log = Serilog.Log.ForContext<MainViewModel>();

    private readonly ProtectionService _protectionService;
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localizationService;

    private bool _isUpdating;

    [ObservableProperty]
    private string _appTitle = "PrivLock";

    [ObservableProperty]
    private string _appSubtitle = "Camera & Microphone Blocker";

    // --- Camera States ---
    [ObservableProperty]
    private bool _isCameraStandardActive;

    [ObservableProperty]
    private string _cameraStandardText = "";

    [ObservableProperty]
    private string _cameraStandardColor = "#81C784";

    [ObservableProperty]
    private string _cameraSecureText = "";

    [ObservableProperty]
    private string _cameraSecureBadgeColor = "#555555";

    [ObservableProperty]
    private string _cameraSecureButtonText = "";

    [ObservableProperty]
    private bool _isCameraSecureButtonEnabled;

    [ObservableProperty]
    private string _cameraSecureHint = "";

    [ObservableProperty]
    private bool _isCameraSecureHintVisible;

    // --- Microphone States ---
    [ObservableProperty]
    private bool _isMicStandardActive;

    [ObservableProperty]
    private string _micStandardText = "";

    [ObservableProperty]
    private string _micStandardColor = "#81C784";

    [ObservableProperty]
    private string _micSecureText = "";

    [ObservableProperty]
    private string _micSecureBadgeColor = "#555555";

    [ObservableProperty]
    private string _micSecureButtonText = "";

    [ObservableProperty]
    private bool _isMicSecureButtonEnabled;

    [ObservableProperty]
    private string _micSecureHint = "";

    [ObservableProperty]
    private bool _isMicSecureHintVisible;

    // --- Common & Settings ---
    [ObservableProperty]
    private bool _isSpanishSelected = true;

    [ObservableProperty]
    private bool _isEnglishSelected;

    [ObservableProperty]
    private bool _isAutostartEnabled;

    [ObservableProperty]
    private string _platformName = "";

    [ObservableProperty]
    private string _capabilitiesSummary = "";

    [ObservableProperty]
    private string _securityBadgeText = "Standard";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    // Localized Labels
    public string CameraTitle => _localizationService.GetString("CameraTitle");
    public string CameraSubtitle => _localizationService.GetString("CameraSubtitle");
    public string MicrophoneTitle => _localizationService.GetString("MicrophoneTitle");
    public string MicrophoneSubtitle => _localizationService.GetString("MicrophoneSubtitle");
    public string StandardProtectionTitle => _localizationService.GetString("StandardProtectionTitle");
    public string StandardProtectionDesc => _localizationService.GetString("StandardProtectionDesc");
    public string SecureProtectionTitle => _localizationService.GetString("SecureProtectionTitle");
    public string SecureProtectionDesc => _localizationService.GetString("SecureProtectionDesc");
    public string DetectedDevicesLabel => _localizationService.GetString("DetectedDevices");
    public string StartWithSystemLabel => _localizationService.GetString("StartWithSystem");
    public string LanguageLabel => _localizationService.GetString("Language");
    public string CapabilitiesTitle => _localizationService.GetString("CapabilitiesTitle");

    public MainViewModel(
        ProtectionService protectionService,
        SettingsService settingsService,
        LocalizationService localizationService)
    {
        _protectionService = protectionService;
        _settingsService = settingsService;
        _localizationService = localizationService;

        _protectionService.StateChanged += OnProtectionStateChanged;
        _localizationService.LanguageChanged += OnLanguageChanged;

        _isSpanishSelected = _localizationService.CurrentLanguage == "es";
        _isEnglishSelected = !_isSpanishSelected;
        _isAutostartEnabled = _settingsService.IsAutostartEnabled();

        var info = _protectionService.PlatformInfo;
        PlatformName = $"{info.OperatingSystemName} ({info.Architecture}) - {(info.IsElevated ? "Admin/Root" : "Standard User")}";
        CapabilitiesSummary = $"Cam: {_protectionService.Capabilities.CameraProtectionLevel} | Mic: {_protectionService.Capabilities.MicrophoneProtectionLevel}";

        _ = RefreshStateAsync();
    }

    public async Task RefreshStateAsync()
    {
        try
        {
            var state = await _protectionService.GetCurrentStateAsync();
            var devices = await _protectionService.GetDetectedDevicesAsync();
            UpdateUi(state, devices);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh UI state");
        }
    }

    private void OnProtectionStateChanged(FullProtectionState state)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            var devices = await _protectionService.GetDetectedDevicesAsync();
            UpdateUi(state, devices);
        });
    }

    private void OnLanguageChanged(string lang)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(CameraTitle));
            OnPropertyChanged(nameof(CameraSubtitle));
            OnPropertyChanged(nameof(MicrophoneTitle));
            OnPropertyChanged(nameof(MicrophoneSubtitle));
            OnPropertyChanged(nameof(StandardProtectionTitle));
            OnPropertyChanged(nameof(StandardProtectionDesc));
            OnPropertyChanged(nameof(SecureProtectionTitle));
            OnPropertyChanged(nameof(SecureProtectionDesc));
            OnPropertyChanged(nameof(DetectedDevicesLabel));
            OnPropertyChanged(nameof(StartWithSystemLabel));
            OnPropertyChanged(nameof(LanguageLabel));
            OnPropertyChanged(nameof(CapabilitiesTitle));
            _ = RefreshStateAsync();
        });
    }

    private void UpdateUi(FullProtectionState state, IReadOnlyList<DeviceInfo> devices)
    {
        _isUpdating = true;
        try
        {
            // === 1. Camera UI ===
            var camStdActive = state.Camera.StandardState == StandardProtectionState.Active;
            IsCameraStandardActive = camStdActive;
            CameraStandardText = camStdActive
                ? _localizationService.GetString("StatusStandardActive", "● Standard Active")
                : _localizationService.GetString("StatusStandardInactive", "○ Standard Inactive");
            CameraStandardColor = camStdActive ? "#E57373" : "#81C784"; // Red when blocked/protected, green when open

            switch (state.Camera.SecureState)
            {
                case SecureProtectionState.Active:
                    CameraSecureText = _localizationService.GetString("StatusSecureActive", "🛡️ Secure Active (Hardened)");
                    CameraSecureBadgeColor = "#D32F2F"; // Strong red
                    CameraSecureButtonText = _localizationService.GetString("DisableSecure", "Disable Secure");
                    IsCameraSecureButtonEnabled = true;
                    IsCameraSecureHintVisible = false;
                    break;

                case SecureProtectionState.Available:
                    CameraSecureText = _localizationService.GetString("StatusSecureAvailable", "○ Available to enable");
                    CameraSecureBadgeColor = "#F57C00"; // Orange
                    CameraSecureButtonText = _localizationService.GetString("EnableSecure", "🔒 Enable Secure Protection");
                    IsCameraSecureButtonEnabled = true;
                    IsCameraSecureHintVisible = false;
                    break;

                default: // Unavailable or Failed
                    CameraSecureText = _localizationService.GetString("StatusSecureUnavailable", "🔒 Unavailable");
                    CameraSecureBadgeColor = "#555555";
                    CameraSecureButtonText = _localizationService.GetString("EnableSecure", "🔒 Enable Secure Protection");
                    IsCameraSecureButtonEnabled = false;
                    CameraSecureHint = _localizationService.GetString("SecureRequirementHint", "Enable Standard Protection first");
                    IsCameraSecureHintVisible = !camStdActive;
                    break;
            }

            // === 2. Microphone UI ===
            var micStdActive = state.Microphone.StandardState == StandardProtectionState.Active;
            IsMicStandardActive = micStdActive;
            MicStandardText = micStdActive
                ? _localizationService.GetString("StatusStandardActive", "● Standard Active")
                : _localizationService.GetString("StatusStandardInactive", "○ Standard Inactive");
            MicStandardColor = micStdActive ? "#E57373" : "#81C784";

            switch (state.Microphone.SecureState)
            {
                case SecureProtectionState.Active:
                    MicSecureText = _localizationService.GetString("StatusSecureActive", "🛡️ Secure Active (Hardened)");
                    MicSecureBadgeColor = "#D32F2F";
                    MicSecureButtonText = _localizationService.GetString("DisableSecure", "Disable Secure");
                    IsMicSecureButtonEnabled = true;
                    IsMicSecureHintVisible = false;
                    break;

                case SecureProtectionState.Available:
                    MicSecureText = _localizationService.GetString("StatusSecureAvailable", "○ Available to enable");
                    MicSecureBadgeColor = "#F57C00";
                    MicSecureButtonText = _localizationService.GetString("EnableSecure", "🔒 Enable Secure Protection");
                    IsMicSecureButtonEnabled = true;
                    IsMicSecureHintVisible = false;
                    break;

                default:
                    MicSecureText = _localizationService.GetString("StatusSecureUnavailable", "🔒 Unavailable");
                    MicSecureBadgeColor = "#555555";
                    MicSecureButtonText = _localizationService.GetString("EnableSecure", "🔒 Enable Secure Protection");
                    IsMicSecureButtonEnabled = false;
                    MicSecureHint = _localizationService.GetString("SecureRequirementHint", "Enable Standard Protection first");
                    IsMicSecureHintVisible = !micStdActive;
                    break;
            }

            // Overall Badge
            if (state.BothSecure)
                SecurityBadgeText = "Hardened (Secure)";
            else if (state.BothProtected)
                SecurityBadgeText = "Protected (Standard)";
            else
                SecurityBadgeText = "Unprotected";

            // Devices list
            var enabledStr = _localizationService.GetString("DeviceEnabled", "ENABLED");
            var disabledStr = _localizationService.GetString("DeviceDisabled", "BLOCKED");

            Devices.Clear();
            foreach (var d in devices)
            {
                Devices.Add(new DeviceItemViewModel(d, enabledStr, disabledStr));
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    // --- Property Changed Event Handlers for ToggleSwitches ---

    partial void OnIsCameraStandardActiveChanged(bool value)
    {
        if (_isUpdating) return;
        _ = ExecuteCameraStandardToggleAsync(value);
    }

    private async Task ExecuteCameraStandardToggleAsync(bool enable)
    {
        ClearError();
        Log.Information("User toggled Camera Standard Protection to {Enable}", enable);

        var result = enable
            ? await _protectionService.EnableStandardProtectionAsync(BlockTarget.Camera)
            : await _protectionService.DisableStandardProtectionAsync(BlockTarget.Camera);

        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Failed to update Camera Standard Protection");
            await RefreshStateAsync();
        }
    }

    partial void OnIsMicStandardActiveChanged(bool value)
    {
        if (_isUpdating) return;
        _ = ExecuteMicStandardToggleAsync(value);
    }

    private async Task ExecuteMicStandardToggleAsync(bool enable)
    {
        ClearError();
        Log.Information("User toggled Microphone Standard Protection to {Enable}", enable);

        var result = enable
            ? await _protectionService.EnableStandardProtectionAsync(BlockTarget.Microphone)
            : await _protectionService.DisableStandardProtectionAsync(BlockTarget.Microphone);

        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Failed to update Microphone Standard Protection");
            await RefreshStateAsync();
        }
    }

    // --- Secure Protection Buttons ---

    [RelayCommand]
    private async Task ToggleCameraSecureAsync()
    {
        if (_isUpdating) return;
        ClearError();

        var state = await _protectionService.GetCurrentStateAsync();
        var result = state.Camera.SecureState == SecureProtectionState.Active
            ? await _protectionService.DisableSecureProtectionAsync(BlockTarget.Camera)
            : await _protectionService.EnableSecureProtectionAsync(BlockTarget.Camera);

        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Camera Secure Protection error");
            await RefreshStateAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleMicSecureAsync()
    {
        if (_isUpdating) return;
        ClearError();

        var state = await _protectionService.GetCurrentStateAsync();
        var result = state.Microphone.SecureState == SecureProtectionState.Active
            ? await _protectionService.DisableSecureProtectionAsync(BlockTarget.Microphone)
            : await _protectionService.EnableSecureProtectionAsync(BlockTarget.Microphone);

        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Microphone Secure Protection error");
            await RefreshStateAsync();
        }
    }

    [RelayCommand]
    private void SelectLanguage(string lang)
    {
        if (_isUpdating) return;
        _localizationService.SetLanguage(lang);
        IsSpanishSelected = lang == "es";
        IsEnglishSelected = lang == "en";
    }

    [RelayCommand]
    private void ToggleAutostart(bool enable)
    {
        if (_isUpdating) return;
        var result = _settingsService.SetAutostart(enable);
        IsAutostartEnabled = _settingsService.IsAutostartEnabled();
        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Failed to update autostart setting");
        }
    }

    private void ShowError(string msg)
    {
        ErrorMessage = msg;
        HasError = true;
    }

    [RelayCommand]
    private void ClearError()
    {
        ErrorMessage = "";
        HasError = false;
    }
}
