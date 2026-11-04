using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Trip;

if (args is ["--check-config"])
{
    Runtime.ValidateHermesConfiguration(Path.Combine(AppContext.BaseDirectory, "hermes", "sandbox-config.yaml"));
    Console.WriteLine("Hermes context and delegation configuration verified by C#.");
    return;
}

using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(30));
using var runtime = await Runtime.StartAsync(startup.Token);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args, ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = TripService.MaximumBytes);
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton(runtime);
builder.Services.AddSingleton<ITripStore>(new AzureTripStore(
    Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? ""));
builder.Services.AddSingleton<TripService>();
builder.Services.AddSingleton<ITripRunner>(_ => new HermesRunner(new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:8642/v1/"), Timeout = Timeout.InfiniteTimeSpan
}, runtime.HermesKey, HermesRunner.CreatePrompt(AppContext.BaseDirectory)));
builder.Services.AddHostedService<GatewayMonitor>();
builder.Services.AddHostedService<TripWorker>();
var app = builder.Build();
await app.Services.GetRequiredService<ITripStore>().InitializeAsync(startup.Token);

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var supplied = context.Request.Headers.Authorization.ToString();
        var expected = $"Bearer {runtime.IntakeKey}";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Je vyzadovan pristupovy klic." });
            return;
        }
    }
    try { await next(context); }
    catch (BadHttpRequestException ex)
    {
        context.Response.StatusCode = ex.StatusCode;
        await context.Response.WriteAsJsonAsync(new { error = "Neplatny nebo prilis velky pozadavek." });
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Trip API request failed.");
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { error = "Sluzba je docasne nedostupna." });
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", runtime = "dotnet", version = 2 }));
app.MapPost("/api/trips", async (HttpContext context, TripService service) =>
{
    if (!context.Request.HasJsonContentType())
        return Results.Json(new { error = "Odeslete application/json." }, statusCode: 415);
    try
    {
        using var payload = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        if (payload.RootElement.ValueKind != JsonValueKind.Object ||
            payload.RootElement.EnumerateObject().Count() != 1 ||
            !payload.RootElement.TryGetProperty("requestText", out var text) ||
            text.ValueKind != JsonValueKind.String)
            return Results.BadRequest(new { error = "Odeslete pouze requestText jako text." });
        var job = await service.SubmitAsync(text.GetString(), context.RequestAborted);
        return Results.Json(new { job.Id, job.Status, job.CreatedAt, statusUrl = $"/api/trips/{job.Id}" }, statusCode: 202);
    }
    catch (JsonException) { return Results.BadRequest(new { error = "Neplatny JSON." }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/trips/{id}", async (string id, ITripStore store, CancellationToken token) =>
{
    if (!Guid.TryParseExact(id, "D", out _)) return Results.BadRequest(new { error = "Neplatne ID." });
    var job = await store.GetAsync(id, token);
    if (job is null) return Results.NotFound(new { error = "Pozadavek nebyl nalezen." });
    var result = job.Status == "completed" && job.ResultBlob is not null
        ? await store.GetResultAsync(job.ResultBlob, token) : null;
    return Results.Ok(new { job.Id, status = job.Status == "completed" && result is null ? "expired" : job.Status,
        job.CreatedAt, job.UpdatedAt, job.Error, job.Progress, result });
});
await app.RunAsync();

public partial class Program;
