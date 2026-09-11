using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace Kankei.Desktop;

/// <summary>Japanese message IDs with an English catalog, following the gettext convention.</summary>
public sealed class Localization : INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string, string> English = LoadCatalog();
    private readonly string _settingsPath;
    public static Localization Current { get; } = new();
    public string Language { get; private set; } = "ja";
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;
    public string this[string message] => Translate(message);

    public Localization(string? settingsPath = null) => _settingsPath = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kankei", "language.json");

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
                Apply(JsonSerializer.Deserialize<LanguageSettings>(File.ReadAllText(_settingsPath))?.Language == "en" ? "en" : "ja");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }

    public void SetLanguage(string language)
    {
        if (language is not ("ja" or "en")) throw new ArgumentException("Unsupported language.", nameof(language));
        if (language == Language) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new LanguageSettings(language)));
        File.Move(temporary, _settingsPath, true);
        Apply(language);
    }

    private void Apply(string language)
    {
        Language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke();
    }

    public string Translate(string message) => Language == "en" && English.TryGetValue(message, out var translation) ? translation : message;
    public static IReadOnlyDictionary<string, string> EnglishCatalog => English;
    private static Dictionary<string, string> LoadCatalog()
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("Kankei.Desktop.Localization.en.json")
            ?? throw new InvalidOperationException("English translation catalog is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    private sealed record LanguageSettings(string Language);
}

public static class L
{
    public static string T(string message) => Localization.Current.Translate(message);
    public static string F(string message, params object?[] args) => string.Format(
        CultureInfo.GetCultureInfo(Localization.Current.Language), T(message), args);
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new System.Windows.Data.Binding
    {
        Source = Localization.Current,
        Path = new System.Windows.PropertyPath($"[{text}]"),
        Mode = BindingMode.OneWay
    }.ProvideValue(serviceProvider);
}
