using System.IO;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Kankei.Desktop;

public sealed record McpAdapterOptions(
    string Id, string ExecutablePath, string Command, string[] Arguments,
    string ExportTool, string LaunchTool, string ImportTool,
    string StateArgument = "state", int TimeoutSeconds = 30);

public sealed class McpApplicationRestoreAdapter(McpAdapterOptions options) : IApplicationRestoreAdapter
{
    public string Id => options.Id;

    public bool CanHandle(string executablePath) =>
        string.Equals(Path.GetFullPath(executablePath), Path.GetFullPath(options.ExecutablePath), StringComparison.OrdinalIgnoreCase);

    public async Task<string?> ExportStateAsync(CancellationToken cancellationToken)
    {
        var result = await InvokeAsync(options.ExportTool, null, cancellationToken);
        var state = result.StructuredContent is { } structured
            ? JsonSerializer.SerializeToElement(structured)
            : JsonSerializer.Deserialize<JsonElement>(string.Join("\n", result.Content.OfType<TextContentBlock>().Select(x => x.Text)));
        if (state.ValueKind == JsonValueKind.Null) throw new InvalidDataException("MCP の保存状態が null です。");
        return JsonSerializer.Serialize(new { version = 1, state });
    }

    public async Task LaunchOrFocusAsync(CancellationToken cancellationToken) =>
        await InvokeAsync(options.LaunchTool, null, cancellationToken);

    public async Task ImportStateAsync(string versionedJson, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(versionedJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
            !root.TryGetProperty("state", out var state) || state.ValueKind == JsonValueKind.Null)
            throw new InvalidDataException("未対応または不正なアダプター状態です。");
        await InvokeAsync(options.ImportTool, new Dictionary<string, object?> { [options.StateArgument] = state.Clone() }, cancellationToken);
    }

    public string DescribeFailure(Exception exception) => $"MCP アダプター '{Id}': {exception.Message}";

    private async Task<CallToolResult> InvokeAsync(string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var transport = new StdioClientTransport(new()
            {
                Name = Id, Command = options.Command, Arguments = options.Arguments,
                ShutdownTimeout = TimeSpan.FromSeconds(2)
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
            var result = await client.CallToolAsync(tool, arguments, cancellationToken: timeout.Token);
            if (result.IsError == true)
                throw new InvalidOperationException($"ツール '{tool}' が失敗しました: " + string.Join("\n", result.Content.OfType<TextContentBlock>().Select(x => x.Text)));
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"ツール '{tool}' が {options.TimeoutSeconds} 秒以内に完了しませんでした。");
        }
    }
}
