using Moq;
using PrivLock.Application.Services;
using PrivLock.Platform.Abstractions;
using Xunit;

namespace PrivLock.Application.Tests;

public class LocalizationServiceTests
{
    private readonly Mock<IStateStore> _storeMock;
    private readonly LocalizationService _service;

    public LocalizationServiceTests()
    {
        _storeMock = new Mock<IStateStore>();
        _storeMock.Setup(s => s.Load()).Returns(new DesiredState { Language = "es" });

        _service = new LocalizationService(_storeMock.Object);
        _service.Initialize();
    }

    [Fact]
    public void Initialize_LoadsSavedLanguage()
    {
        Assert.Equal("es", _service.CurrentLanguage);
        Assert.Equal("🔒 Bloqueado", _service.GetString("StatusBlocked"));
    }

    [Fact]
    public void SetLanguage_English_SwitchesLanguageAndSaves()
    {
        var languageChangedFired = false;
        _service.LanguageChanged += lang => languageChangedFired = (lang == "en");

        _service.SetLanguage("en");

        Assert.Equal("en", _service.CurrentLanguage);
        Assert.Equal("🔒 Blocked", _service.GetString("StatusBlocked"));
        Assert.True(languageChangedFired);
        _storeMock.Verify(s => s.Save(It.Is<DesiredState>(ds => ds.Language == "en")), Times.Once);
    }
}
