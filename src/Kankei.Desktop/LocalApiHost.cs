using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kankei.Desktop;

public sealed class LocalApiHost(LayoutStore store, WindowDiscovery discovery, RestoreOrchestrator orchestrator, ApplicationAdapterRegistry adapters)
{
    public const string BaseUrl = "http://127.0.0.1:48120";
    public static string RestoreUrl(string layoutId) => $"{BaseUrl}/v1/layouts/{Uri.EscapeDataString(layoutId)}/restore";
    private WebApplication? _application;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(BaseUrl);
        _application = builder.Build();
        _application.MapGet("/v1/health", () => Results.Ok(new { status = "ok", service = "Kankei" }));
        _application.MapGet("/v1/layouts", async (CancellationToken ct) => Results.Ok(await store.ListAsync(ct)));
        _application.MapPost("/v1/layouts", async (CreateLayoutRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["名前を指定してください。"] });
            var windows = await adapters.CaptureAsync(discovery.Capture(), ct);
            if (request.IncludeYouTube) windows = await ChromeYouTubeState.CaptureAsync(windows, ct);
            var layout = await store.SaveAsync(request.Name, windows, ct);
            return Results.Created($"/v1/layouts/{layout.Id}", layout);
        });
        _application.MapPost("/v1/layouts/{layoutId}/restore", async (string layoutId, RestoreRequest? request, CancellationToken ct) =>
        {
            var job = await orchestrator.StartAsync(layoutId, request?.ShowOverlay ?? true, ct);
            return job is null ? Results.NotFound() : Results.Accepted($"/v1/restores/{job.Id}", new { restoreId = job.Id, status = "running" });
        });
        _application.MapGet("/v1/restores/{restoreId}", (string restoreId) =>
            orchestrator.GetJob(restoreId) is { } job ? Results.Ok(job) : Results.NotFound());
        _application.MapGet("/v1/restores/{restoreId}/events", (string restoreId) =>
            orchestrator.GetJob(restoreId) is { } job ? Results.Ok(job.Events) : Results.NotFound());
        await _application.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => _application?.StopAsync(cancellationToken) ?? Task.CompletedTask;
}
