namespace Kankei.Desktop;

/// <summary>
/// アプリ固有の内部状態を Kankei から隔離するための契約です。
/// 実装は MCP クライアントを内部に持ち、スナップショットはバージョン付き JSON として扱います。
/// </summary>
public interface IApplicationRestoreAdapter
{
    string Id { get; }
    bool CanHandle(string executablePath);
    Task<string?> ExportStateAsync(CancellationToken cancellationToken);
    Task LaunchOrFocusAsync(CancellationToken cancellationToken);
    Task ImportStateAsync(string versionedJson, CancellationToken cancellationToken);
    string DescribeFailure(Exception exception);
}
