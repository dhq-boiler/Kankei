using System.IO;
using System.Reflection;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace Kankei.Desktop;

public static class AppInfo
{
    public const string Repository = "https://github.com/dhq-boiler/Kankei";
    public static string Version => typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "0.1.0";
}

public interface IUpdateBackend
{
    bool IsInstalled { get; }
    string? PendingVersion { get; }
    Task<string?> CheckAsync(CancellationToken cancellationToken);
    Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken);
    void ScheduleApply(bool restart);
}

public sealed class VelopackUpdateBackend : IUpdateBackend
{
    private readonly UpdateManager _manager = new(new GithubSource(AppInfo.Repository, null, false));
    private UpdateInfo? _update;
    public bool IsInstalled => _manager.IsInstalled;
    public string? PendingVersion => _manager.UpdatePendingRestart?.Version.ToString();
    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        var update = await _manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _update = update;
        return update?.TargetFullRelease.Version.ToString();
    }
    public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken) =>
        _manager.DownloadUpdatesAsync(_update ?? throw new InvalidOperationException("先に更新を確認してください。"), progress, cancellationToken);
    public void ScheduleApply(bool restart) => _manager.WaitExitThenApplyUpdates(_manager.UpdatePendingRestart
        ?? throw new InvalidOperationException("更新のダウンロードが完了していません。"), silent: true, restart: restart);
}

public enum AppUpdateState { NotInstalled, Idle, Checking, Available, Downloading, Ready, Applying, Error }

public sealed class UpdateService
{
    private readonly IUpdateBackend _backend;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly string _settingsPath;
    private string? _notifiedVersion;
    public AppUpdateState State { get; private set; }
    public string Status { get; private set; } = "更新を確認できます。";
    public string? AvailableVersion { get; private set; }
    public bool AutomaticUpdates { get; private set; } = true;
    public event Action? Changed;
    public event Action<string>? UpdateFound;

    public UpdateService(IUpdateBackend backend, string? settingsPath = null)
    {
        _backend = backend;
        _settingsPath = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kankei", "updates.json");
        try { if (File.Exists(_settingsPath)) AutomaticUpdates = JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(_settingsPath))?.AutomaticUpdates ?? true; }
        catch (Exception ex) when (ex is IOException or JsonException) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        AvailableVersion = backend.IsInstalled ? backend.PendingVersion : null;
        Set(!backend.IsInstalled ? AppUpdateState.NotInstalled : AvailableVersion is null ? AppUpdateState.Idle : AppUpdateState.Ready,
            !backend.IsInstalled ? "開発版です。インストーラーから導入すると自動更新を利用できます。" : AvailableVersion is null ? "更新を確認できます。" : $"バージョン {AvailableVersion} の更新準備ができています。");
    }

    public void SetAutomaticUpdates(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new UpdateSettings(enabled)));
        File.Move(temporary, _settingsPath, true);
        AutomaticUpdates = enabled;
        Changed?.Invoke();
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_backend.IsInstalled || !await _gate.WaitAsync(0, cancellationToken)) return;
        try
        {
            if (State is AppUpdateState.Ready or AppUpdateState.Applying) return;
            Set(AppUpdateState.Checking, "新しいバージョンを確認中…");
            AvailableVersion = await _backend.CheckAsync(cancellationToken);
            if (AvailableVersion is null) { Set(AppUpdateState.Idle, "最新バージョンを使用しています。"); return; }
            Set(AppUpdateState.Available, $"バージョン {AvailableVersion} が公開されています。");
            if (_notifiedVersion != AvailableVersion) { _notifiedVersion = AvailableVersion; UpdateFound?.Invoke(AvailableVersion); }
            if (AutomaticUpdates) await DownloadAsync(cancellationToken);
        }
        catch (OperationCanceledException) { Set(AppUpdateState.Idle, "更新の確認・ダウンロードを中止しました。"); }
        catch (Exception ex) { Set(AppUpdateState.Error, "更新を確認・取得できませんでした: " + ex.Message); }
        finally { _gate.Release(); }
    }

    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        Set(AppUpdateState.Downloading, "更新をダウンロード中…");
        await _backend.DownloadAsync(percent => Set(AppUpdateState.Downloading, $"更新をダウンロード中… {percent}%"), cancellationToken);
        if (_backend.PendingVersion is null) throw new InvalidOperationException("更新ファイルを検証できませんでした。");
        Set(AppUpdateState.Ready, $"バージョン {AvailableVersion} の更新準備ができています。終了時、または「今すぐ更新」で適用します。");
    }

    public async Task<bool> ApplyNowAsync(Func<Task> prepareForExit, CancellationToken cancellationToken = default)
    {
        if (!_backend.IsInstalled || !await _gate.WaitAsync(0, cancellationToken)) return false;
        try
        {
            if (_backend.PendingVersion is null)
            {
                AvailableVersion = await _backend.CheckAsync(cancellationToken);
                if (AvailableVersion is null) { Set(AppUpdateState.Idle, "最新バージョンを使用しています。"); return false; }
                await DownloadAsync(cancellationToken);
            }
            Set(AppUpdateState.Applying, "再生状態を保存して更新の準備中…");
            await prepareForExit();
            _backend.ScheduleApply(restart: true);
            return true;
        }
        catch (Exception ex) { Set(AppUpdateState.Error, "更新を適用できませんでした: " + ex.Message); return false; }
        finally { _gate.Release(); }
    }

    public void ApplyOnExit()
    {
        if (AutomaticUpdates && State == AppUpdateState.Ready) _backend.ScheduleApply(restart: false);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_backend.IsInstalled) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
            do { await CheckAsync(cancellationToken); } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void Set(AppUpdateState state, string message) { State = state; Status = message; Changed?.Invoke(); }
    private sealed record UpdateSettings(bool AutomaticUpdates);
}
