using Kankei.Desktop;
using System.Text.RegularExpressions;

namespace Kankei.Tests;

public sealed class LocalizationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Kankei-language-" + Guid.NewGuid().ToString("N"));
    private string Settings => Path.Combine(_directory, "language.json");

    [Fact]
    public void LanguageRoundTripPreservesChoiceAndNotifiesBindings()
    {
        var localization = new Localization(Settings);
        Assert.Equal("バージョン情報", localization["バージョン情報"]);
        var changes = 0;
        localization.PropertyChanged += (_, e) => { Assert.Equal("Item[]", e.PropertyName); changes++; };
        localization.SetLanguage("en");
        Assert.Equal("About", localization["バージョン情報"]);
        Assert.Equal("my profile", localization["my profile"]);
        var restored = new Localization(Settings);
        restored.Load();
        Assert.Equal("en", restored.Language);
        localization.SetLanguage("en");
        Assert.Equal(1, changes);
        localization.SetLanguage("ja");
        restored.Load();
        Assert.Equal("ja", restored.Language);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"Language\":\"fr\"}")]
    public void InvalidSettingsFallBackToJapanese(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Settings, content);
        var localization = new Localization(Settings);
        localization.Load();
        Assert.Equal("ja", localization.Language);
    }

    [Fact]
    public void InvalidLanguageAndFailedSaveDoNotChangeCurrentLanguage()
    {
        var localization = new Localization(Settings);
        Assert.Throws<ArgumentException>(() => localization.SetLanguage("fr"));
        Directory.CreateDirectory(Settings);
        var error = Record.Exception(() => localization.SetLanguage("en"));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("ja", localization.Language);
    }

    [Fact]
    public void CatalogPreservesAllFormattingArguments()
    {
        Assert.NotEmpty(Localization.EnglishCatalog);
        foreach (var (source, translation) in Localization.EnglishCatalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(translation));
            Assert.Equal(Regex.Matches(source, @"\{\d+\}").Select(x => x.Value).Order(),
                Regex.Matches(translation, @"\{\d+\}").Select(x => x.Value).Order());
        }
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
