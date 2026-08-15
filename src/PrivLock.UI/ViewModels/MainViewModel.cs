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

    [ObservableProperty]
    private bool _isCameraBlocked;

    [ObservableProperty]
    private string _cameraStatusText = "";

    [ObservableProperty]
    private string _cameraStatusColor = "#4CAF50";

    [ObservableProperty]
    private bool _isMicrophoneBlocked;

    [ObservableProperty]
    private string _microphoneStatusText = "";

    [ObservableProperty]
    private string _microphoneStatusColor = "#4CAF50";

    [ObservableProperty]
    private bool _isMasterBlocked;

    [ObservableProperty]
    private string _masterStatusText = "";

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
    private string _securityBadgeText = "Active";

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
    public string MasterToggleLabel => _localizationService.GetString("MasterToggle");
    public string MasterSubtitle => _localizationService.GetString("MasterSubtitle");
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

    private void OnProtectionStateChanged(BlockState state)
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
            OnPropertyChanged(nameof(MasterToggleLabel));
            OnPropertyChanged(nameof(MasterSubtitle));
            OnPropertyChanged(nameof(DetectedDevicesLabel));
            OnPropertyChanged(nameof(StartWithSystemLabel));
            OnPropertyChanged(nameof(LanguageLabel));
            OnPropertyChanged(nameof(CapabilitiesTitle));
            _ = RefreshStateAsync();
        });
    }

    private void UpdateUi(BlockState state, IReadOnlyList<DeviceInfo> devices)
    {
        _isUpdating = true;
        try
        {
            var blockedStr = _localizationService.GetString("StatusBlocked", "🔒 Blocked");
            var allowedStr = _localizationService.GetString("StatusAllowed", "✅ Allowed");

            // Camera
            IsCameraBlocked = state.Camera.EffectiveStatus == BlockStatus.Blocked;
            CameraStatusText = IsCameraBlocked ? blockedStr : allowedStr;
            CameraStatusColor = IsCameraBlocked ? "#E57373" : "#81C784";

            // Microphone
            IsMicrophoneBlocked = state.Microphone.EffectiveStatus == BlockStatus.Blocked;
            MicrophoneStatusText = IsMicrophoneBlocked ? blockedStr : allowedStr;
            MicrophoneStatusColor = IsMicrophoneBlocked ? "#E57373" : "#81C784";

            // Master
            IsMasterBlocked = state.AllBlocked;
            MasterStatusText = IsMasterBlocked
                ? _localizationService.GetString("ProtectionActive", "Protection active (Both Blocked)")
                : state.AllAllowed
                    ? _localizationService.GetString("ProtectionInactive", "Protection inactive (Both Allowed)")
                    : _localizationService.GetString("MixedState", "Mixed state");

            SecurityBadgeText = IsMasterBlocked ? "Protected" : (state.AllAllowed ? "Allowed" : "Partial");

            // Devices list
            var enabledStr = _localizationService.GetString("DeviceEnabled", "ENABLED");
            var disabledStr = _localizationService.GetString("DeviceDisabled", "DISABLED");

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

    [RelayCommand]
    private async Task ToggleMasterAsync()
    {
        if (_isUpdating) return;
        ClearError();

        var target = BlockTarget.Both;
        var result = IsMasterBlocked
            ? await _protectionService.UnblockAsync(target)
            : await _protectionService.BlockAsync(target);

        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Operation failed");
            await RefreshStateAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleCameraAsync()
    {
        if (_isUpdating) return;
        ClearError();

        var result = IsCameraBlocked
            ? await _protectionService.UnblockAsync(BlockTarget.Camera)
            : await _protectionService.BlockAsync(BlockTarget.Camera);

        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Camera toggle failed");
            await RefreshStateAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleMicrophoneAsync()
    {
        if (_isUpdating) return;
        ClearError();

        var result = IsMicrophoneBlocked
            ? await _protectionService.UnblockAsync(BlockTarget.Microphone)
            : await _protectionService.BlockAsync(BlockTarget.Microphone);

        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Microphone toggle failed");
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
