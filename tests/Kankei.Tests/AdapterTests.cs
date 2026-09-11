using Kankei.Desktop;
using System.Text.Json;

namespace Kankei.Tests;

public class AdapterTests
{
    private static McpApplicationRestoreAdapter Create(string export = "export", int timeout = 10) => new(new(
        "test", @"C:\Apps\Editor.exe", "dotnet",
        ["exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Kankei.Tests.runtimeconfig.json"), typeof(TestServerMarker).Assembly.Location],
        export, "launch", "import", TimeoutSeconds: timeout));

    private static SavedWindow Window(string? id = null, string? state = null) =>
        new(@"C:\Apps\Editor.exe", "Editor", 0, 0, 640, 480, WindowDisplayState.Normal, id, state);

    [Fact] // EQ-1: separate process / SDK / JSON / import contract
    public async Task StateRoundTripsThroughStdioServer()
    {
        var adapter = Create();
        var registry = new ApplicationAdapterRegistry([adapter]);
        var captured = await registry.CaptureAsync([Window(), Window()], CancellationToken.None);
        Assert.All(captured, window => Assert.Equal("test", window.AdapterId));
        Assert.Equal(captured[0].AdapterStateJson, captured[1].AdapterStateJson);
        var saved = captured[0];
        Assert.Same(adapter, registry.ForSavedWindow(saved));
        using var state = JsonDocument.Parse(saved.AdapterStateJson!);
        Assert.Equal(1, state.RootElement.GetProperty("version").GetInt32());
        await adapter.LaunchOrFocusAsync(CancellationToken.None);
        await adapter.ImportStateAsync(saved.AdapterStateJson!, CancellationToken.None);
    }

    [Fact] // EQ-2
    public async Task NoAdapterPreservesLegacyWindow()
    {
        var registry = new ApplicationAdapterRegistry([]);
        var window = Window();
        Assert.Equal(window, Assert.Single(await registry.CaptureAsync([window], CancellationToken.None)));
        Assert.Null(registry.ForSavedWindow(window));
    }

    [Theory] // ER-1: inconsistent persisted adapter identity
    [InlineData("missing", "{}")]
    [InlineData("test", null)]
    [InlineData(null, "{}")]
    public void MissingIdentityOrStateFails(string? id, string? state) =>
        Assert.Throws<InvalidDataException>(() => new ApplicationAdapterRegistry([Create()]).ForSavedWindow(Window(id, state)));

    [Theory] // BV-1 / ER-2: invalid versions and shape are rejected before starting a server
    [InlineData("{\"version\":0,\"state\":{}}")]
    [InlineData("{\"version\":2,\"state\":{}}")]
    [InlineData("{\"version\":1,\"state\":null}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":\"1\",\"state\":{}}")]
    [InlineData("[]")]
    public async Task UnsupportedEnvelopeFails(string json) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Create().ImportStateAsync(json, CancellationToken.None));

    [Fact] // ER-2
    public async Task MalformedImportFails() =>
        await Assert.ThrowsAnyAsync<JsonException>(() => Create().ImportStateAsync("broken", CancellationToken.None));

    [Fact] // ER-2
    public async Task MalformedExportFails() =>
        await Assert.ThrowsAnyAsync<JsonException>(() => Create("invalid").ExportStateAsync(CancellationToken.None));

    [Fact] // ER-2
    public async Task NullExportFails() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Create("null").ExportStateAsync(CancellationToken.None));

    [Fact] // ER-3
    public async Task ToolErrorFails() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create("fail").ExportStateAsync(CancellationToken.None));

    [Fact] // ER-4
    public async Task TimeoutFails() =>
        await Assert.ThrowsAsync<TimeoutException>(() => Create("slow", 1).ExportStateAsync(CancellationToken.None));

    [Fact] // ER-4
    public async Task CallerCancellationIsPreserved()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create("slow").ExportStateAsync(cancellation.Token));
    }

    [Fact]
    public void DuplicateIdsAreRejected() => Assert.Throws<InvalidDataException>(() => new ApplicationAdapterRegistry([Create(), Create()]));

    [Fact] // EQ-1: structured output without text
    public async Task StructuredStateRoundTrips()
    {
        var adapter = Create("structured");
        var json = await adapter.ExportStateAsync(CancellationToken.None);
        await adapter.ImportStateAsync(json!, CancellationToken.None);
    }

    [Theory] // Boundary values around the configured timeout range
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void ConfigurationTimeoutBounds(int seconds, bool valid)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new[] { new McpAdapterOptions(
                "test", @"C:\Apps\Editor.exe", "unused", [], "export", "launch", "import", TimeoutSeconds: seconds) }));
            if (valid) Assert.NotNull(ApplicationAdapterRegistry.Load(path));
            else Assert.Throws<InvalidDataException>(() => ApplicationAdapterRegistry.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DifferentExecutableIsRejected()
    {
        var registry = new ApplicationAdapterRegistry([Create()]);
        Assert.Throws<InvalidDataException>(() => registry.ForSavedWindow(Window("test", "{}") with { ExecutablePath = @"C:\Other.exe" }));
    }
}
