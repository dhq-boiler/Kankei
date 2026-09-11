using System.Text.Json;
using System.IO;

namespace Kankei.Desktop;

public sealed class LayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kankei", "layouts");

    public async Task<Layout> SaveAsync(string name, IReadOnlyList<SavedWindow> windows, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var id = ToId(name);
        var layout = new Layout(id, name.Trim(), DateTimeOffset.UtcNow, windows);
        await using var stream = File.Create(Path.Combine(_directory, id + ".json"));
        await JsonSerializer.SerializeAsync(stream, layout, JsonOptions, cancellationToken);
        return layout;
    }

    public async Task<IReadOnlyList<Layout>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return [];
        var layouts = new List<Layout>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            await using var stream = File.OpenRead(path);
            var layout = await JsonSerializer.DeserializeAsync<Layout>(stream, JsonOptions, cancellationToken);
            if (layout is not null) layouts.Add(layout);
        }
        return layouts.OrderByDescending(x => x.SavedAt).ToList();
    }

    public async Task<Layout?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_directory, ToId(id) + ".json");
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Layout>(stream, JsonOptions, cancellationToken);
    }

    private static string ToId(string value)
    {
        var slug = string.Concat(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        slug = slug.Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? throw new ArgumentException("Layout name must include a letter or digit.") : slug;
    }
}
