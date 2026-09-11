using System.IO;
using System.Text.Json;

namespace Kankei.Desktop;

public sealed class ApplicationAdapterRegistry
{
    private readonly IApplicationRestoreAdapter[] _adapters;

    public ApplicationAdapterRegistry(IEnumerable<IApplicationRestoreAdapter> adapters)
    {
        _adapters = adapters.ToArray();
        if (_adapters.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != _adapters.Length)
            throw new InvalidDataException("アダプター ID が重複しています。");
    }

    public static ApplicationAdapterRegistry Load(string path)
    {
        if (!File.Exists(path)) return new([]);
        var options = JsonSerializer.Deserialize<McpAdapterOptions[]>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("アダプター設定が空です。");
        foreach (var item in options)
        {
            if (item is null || new[] { item.Id, item.ExecutablePath, item.Command, item.ExportTool, item.LaunchTool, item.ImportTool, item.StateArgument }.Any(string.IsNullOrWhiteSpace)
                || !Path.IsPathFullyQualified(item.ExecutablePath) || item.Arguments is null || item.TimeoutSeconds is < 1 or > 300)
                throw new InvalidDataException("アダプター設定が不正です。実行ファイルは絶対パス、タイムアウトは 1～300 秒で指定してください。");
        }
        if (options.Select(x => Path.GetFullPath(x.ExecutablePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Length)
            throw new InvalidDataException("同じ実行ファイルに複数のアダプターが設定されています。");
        return new(options.Select(x => new McpApplicationRestoreAdapter(x)));
    }

    public IApplicationRestoreAdapter? ForSavedWindow(SavedWindow window)
    {
        if (window.AdapterId is null)
        {
            if (window.AdapterStateJson is not null) throw new InvalidDataException("保存状態にアダプター ID がありません。");
            return null;
        }
        var adapter = _adapters.SingleOrDefault(x => x.Id == window.AdapterId)
            ?? throw new InvalidDataException($"アダプター '{window.AdapterId}' が登録されていません。");
        if (!adapter.CanHandle(window.ExecutablePath) || window.AdapterStateJson is null)
            throw new InvalidDataException("保存されたアダプターとアプリ、または保存状態が一致しません。");
        return adapter;
    }

    public async Task<IReadOnlyList<SavedWindow>> CaptureAsync(IReadOnlyList<SavedWindow> windows, CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, string>();
        var result = new List<SavedWindow>();
        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = _adapters.SingleOrDefault(x => x.CanHandle(window.ExecutablePath));
            if (adapter is null) { result.Add(window); continue; }
            if (!states.TryGetValue(adapter.Id, out var state))
            {
                state = await adapter.ExportStateAsync(cancellationToken)
                    ?? throw new InvalidDataException($"アダプター '{adapter.Id}' が状態を返しませんでした。");
                states.Add(adapter.Id, state);
            }
            result.Add(window with { AdapterId = adapter.Id, AdapterStateJson = state });
        }
        return result;
    }
}
