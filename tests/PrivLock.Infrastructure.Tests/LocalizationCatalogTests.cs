using PrivLock.Infrastructure.Common.Localization;
using Xunit;

namespace PrivLock.Infrastructure.Tests;

public class LocalizationCatalogTests
{
    [Theory]
    [InlineData("es", "🔒 Bloqueado")]
    [InlineData("en", "🔒 Blocked")]
    public void Get_StatusBlocked_ReturnsCorrectTranslation(string lang, string expected)
    {
        var result = LocalizationCatalog.Get("StatusBlocked", lang);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Get_UnknownKey_ReturnsFallbackOrKey()
    {
        var result = LocalizationCatalog.Get("NonExistentKey", "es", "FallbackVal");
        Assert.Equal("FallbackVal", result);
    }

    [Fact]
    public void GetAll_ReturnsCompleteDictionary()
    {
        var dictEs = LocalizationCatalog.GetAll("es");
        var dictEn = LocalizationCatalog.GetAll("en");

        Assert.NotEmpty(dictEs);
        Assert.NotEmpty(dictEn);
        Assert.Equal(dictEs.Count, dictEn.Count);
    }
}
