using Kankei.Desktop;

namespace Kankei.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Kankei-updates-" + Guid.NewGuid().ToString("N"));
    private UpdateService Service(FakeBackend backend) => new(backend, Path.Combine(_directory, "settings.json"));

    [Fact]
    public async Task DevelopmentBuildNeverQueriesOrAppliesUpdates()
    {
        var backend = new FakeBackend { IsInstalled = false };
        var service = Service(backend);
        await service.CheckAsync();
        Assert.False(await service.ApplyNowAsync(() => Task.CompletedTask));
        Assert.Equal(AppUpdateState.NotInstalled, service.State);
        Assert.Equal(0, backend.Checks);
    }

    [Fact]
    public async Task LatestVersionDoesNotDownloadOrNotify()
    {
        var backend = new FakeBackend { Latest = null };
        var service = Service(backend);
        var notices = 0;
        service.UpdateFound += _ => notices++;
        await service.CheckAsync();
        Assert.Equal(AppUpdateState.Idle, service.State);
        Assert.Equal(0, backend.Downloads);
        Assert.Equal(0, notices);
    }

    [Fact]
    public async Task AutomaticUpdateDownloadsButDoesNotRestartUntilRequested()
    {
        var backend = new FakeBackend();
        var service = Service(backend);
        var notices = 0;
        service.UpdateFound += _ => notices++;
        await service.CheckAsync();
        await service.CheckAsync();
        Assert.Equal(AppUpdateState.Ready, service.State);
        Assert.Equal(1, backend.Downloads);
        Assert.Equal(1, notices);
        Assert.Empty(backend.Applied);
        service.ApplyOnExit();
        Assert.Equal([false], backend.Applied);
    }

    [Fact]
    public async Task DisablingAutomaticUpdatePersistsAndStillNotifiesOnce()
    {
        var backend = new FakeBackend();
        Service(backend).SetAutomaticUpdates(false);
        var service = Service(backend);
        var notices = 0;
        service.UpdateFound += _ => notices++;
        await service.CheckAsync();
        await service.CheckAsync();
        service.ApplyOnExit();
        Assert.False(service.AutomaticUpdates);
        Assert.Equal(AppUpdateState.Available, service.State);
        Assert.Equal(0, backend.Downloads);
        Assert.Equal(1, notices);
        Assert.Empty(backend.Applied);
    }

    [Fact]
    public async Task ImmediateUpdateSavesBeforeSchedulingRestartEvenWhenAutomaticIsOff()
    {
        var backend = new FakeBackend();
        var service = Service(backend);
        service.SetAutomaticUpdates(false);
        var saved = false;
        backend.BeforeApply = () => Assert.True(saved);
        Assert.True(await service.ApplyNowAsync(() => { saved = true; return Task.CompletedTask; }));
        Assert.Equal([true], backend.Applied);
    }

    [Fact]
    public async Task FailedStateSaveNeverSchedulesUpdateAndCanBeRetried()
    {
        var backend = new FakeBackend();
        var service = Service(backend);
        Assert.False(await service.ApplyNowAsync(() => throw new IOException("save failed")));
        Assert.Empty(backend.Applied);
        Assert.Equal(AppUpdateState.Error, service.State);
        backend.Latest = null;
        Assert.True(await service.ApplyNowAsync(() => Task.CompletedTask));
        Assert.Single(backend.Applied);
        Assert.Equal(1, backend.Downloads);
    }

    [Fact]
    public async Task FailedDownloadDoesNotApplyAndNextCheckCanRecover()
    {
        var backend = new FakeBackend { FailDownload = true };
        var service = Service(backend);
        await service.CheckAsync();
        service.ApplyOnExit();
        Assert.Equal(AppUpdateState.Error, service.State);
        Assert.Empty(backend.Applied);
        backend.FailDownload = false;
        await service.CheckAsync();
        Assert.Equal(AppUpdateState.Ready, service.State);
    }

    [Fact]
    public async Task ConcurrentChecksDoNotRaceDownloads()
    {
        var backend = new FakeBackend { CheckBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = Service(backend);
        var first = service.CheckAsync();
        await service.CheckAsync();
        backend.CheckBarrier.SetResult();
        await first;
        Assert.Equal(1, backend.Checks);
        Assert.Equal(1, backend.Downloads);
    }

    [Fact]
    public void ExistingDownloadedUpdateRespectsDisabledAutomaticPreference()
    {
        var backend = new FakeBackend { PendingVersion = "0.2.0" };
        var service = Service(backend);
        service.SetAutomaticUpdates(false);
        service.ApplyOnExit();
        Assert.Equal(AppUpdateState.Ready, service.State);
        Assert.Empty(backend.Applied);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class FakeBackend : IUpdateBackend
    {
        public bool IsInstalled { get; set; } = true;
        public string? Latest { get; set; } = "0.2.0";
        public string? PendingVersion { get; set; }
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public bool FailDownload { get; set; }
        public List<bool> Applied { get; } = [];
        public Action? BeforeApply { get; set; }
        public TaskCompletionSource? CheckBarrier { get; set; }
        public async Task<string?> CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            if (CheckBarrier is not null) await CheckBarrier.Task.WaitAsync(cancellationToken);
            return Latest;
        }
        public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken)
        {
            Downloads++;
            if (FailDownload) throw new IOException("download failed");
            progress(100);
            PendingVersion = Latest;
            return Task.CompletedTask;
        }
        public void ScheduleApply(bool restart) { BeforeApply?.Invoke(); Applied.Add(restart); }
    }
}
