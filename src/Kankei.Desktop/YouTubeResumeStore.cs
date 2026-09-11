using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kankei.Desktop;

public sealed class YouTubeResumeStore(string? directory = null)
{
    private readonly string _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kankei", "youtube-resume");

    public static string Key(Layout layout, int index) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{layout.Id}\n{layout.SavedAt:O}\n{index}")));

    public async Task SaveAsync(Layout layout, int index, YouTubePlaybackState state, CancellationToken cancellationToken = default)
    {
        _ = YouTubePlayback.RestoreUrl(state);
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Key(layout, index) + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<YouTubePlaybackState?> GetAsync(Layout layout, int index, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_directory, Key(layout, index) + ".json");
        if (!File.Exists(path)) return null;
        var state = JsonSerializer.Deserialize<YouTubePlaybackState>(await File.ReadAllTextAsync(path, cancellationToken));
        if (state is not null) _ = YouTubePlayback.RestoreUrl(state);
        return state;
    }

    public static YouTubePlaybackState? Select(YouTubePlaybackState? saved, YouTubePlaybackState? checkpoint,
        YouTubePlaybackState? current) => saved is null ? null : current ?? checkpoint ?? saved;
}
