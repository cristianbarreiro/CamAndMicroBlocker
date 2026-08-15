using PrivLock.Infrastructure.Common.Localization;
using PrivLock.Platform.Abstractions;
using Serilog;

namespace PrivLock.Application.Services;

/// <summary>
/// Application service managing the active language and providing translated strings.
/// Independent of any specific UI framework.
/// </summary>
public sealed class LocalizationService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<LocalizationService>();

    private readonly IStateStore _stateStore;
    private string _currentLanguage = "es";

    public event Action<string>? LanguageChanged;

    public string CurrentLanguage => _currentLanguage;

    public LocalizationService(IStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public void Initialize()
    {
        var state = _stateStore.Load();
        var initialLang = string.IsNullOrWhiteSpace(state.Language) ? "es" : state.Language.ToLowerInvariant();
        SetLanguage(initialLang, saveState: false);
    }

    public void SetLanguage(string langCode, bool saveState = true)
    {
        langCode = langCode.ToLowerInvariant() switch
        {
            "en" => "en",
            _ => "es"
        };

        Log.Information("Switching application language to: {LangCode}", langCode);
        _currentLanguage = langCode;

        if (saveState)
        {
            var state = _stateStore.Load();
            state.Language = langCode;
            _stateStore.Save(state);
        }

        LanguageChanged?.Invoke(langCode);
    }

    public string GetString(string key, string fallback = "")
    {
        return LocalizationCatalog.Get(key, _currentLanguage, fallback);
    }

    public IReadOnlyDictionary<string, string> GetAllStrings()
    {
        return LocalizationCatalog.GetAll(_currentLanguage);
    }
}
