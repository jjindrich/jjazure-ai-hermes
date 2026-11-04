using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Trip;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Trip.Tests;

public sealed class TripTests
{
    [Fact]
    public void DeploymentConfigurationRequiresStaticKeyAndCreatesPrivateGatewayKey()
    {
        var settings = new Dictionary<string, string>
        {
            ["TRIP_API_KEY"] = new('k', 40),
            ["TRIP_COPILOT_TOKEN"] = new('c', 40)
        };
        var first = Runtime.ReadConfiguration(name => settings.GetValueOrDefault(name), true);
        var second = Runtime.ReadConfiguration(name => settings.GetValueOrDefault(name), true);
        Assert.Equal(settings["TRIP_API_KEY"], first.IntakeKey);
        Assert.Equal(64, first.HermesKey.Length);
        Assert.NotEqual(first.HermesKey, second.HermesKey);
        settings.Remove("TRIP_COPILOT_TOKEN");
        Assert.Throws<InvalidOperationException>(() =>
            Runtime.ReadConfiguration(name => settings.GetValueOrDefault(name), true));
        settings["TRIP_API_KEY"] = "short";
        Assert.Throws<InvalidOperationException>(() =>
            Runtime.ReadConfiguration(name => settings.GetValueOrDefault(name), false));
    }

    [Fact]
    public void PublishedHermesConfigurationMatchesProviderLimits()
    {
        Runtime.ValidateHermesConfiguration(Path.Combine(AppContext.BaseDirectory, "hermes", "sandbox-config.yaml"));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "  context_length: 60000\n  max_spawn_depth: 2");
            Assert.Throws<InvalidDataException>(() => Runtime.ValidateHermesConfiguration(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HttpPromptReusesDemoWithoutLegacyFileInstructionsOrExtraCoordinator()
    {
        var prompt = HermesRunner.CreatePrompt(AppContext.BaseDirectory);
        Assert.Contains("You ARE the Program coordinator", prompt);
        Assert.Contains("exactly THREE leaf tasks", prompt);
        Assert.Contains("Route planner", prompt);
        Assert.Contains("Meteorologist", prompt);
        Assert.Contains("Budget analyst", prompt);
        Assert.Contains("10% contingency", prompt);
        Assert.Contains("Jak Hermes koordinoval agenty", prompt);
        Assert.DoesNotContain("write_file", prompt);
        Assert.DoesNotContain("data/travel-request.txt", prompt);
        Assert.DoesNotContain("Saturday and Sunday timetable", prompt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyRequestRejected(string input) =>
        Assert.Throws<ArgumentException>(() => TripService.Validate(input));

    [Fact]
    public void RequestSizeLimitsAndTrimming()
    {
        Assert.Equal("Praha", TripService.Validate(" Praha "));
        Assert.Throws<ArgumentException>(() => TripService.Validate(new string('x', 20_001)));
        Assert.Throws<ArgumentException>(() => TripService.Validate(new string('ž', 13_000)));
    }

    [Fact]
    public async Task QueueSubmissionDoesNotExposeRequest()
    {
        var store = new MemoryStore();
        var job = await new TripService(store).SubmitAsync("Praha", default);
        Assert.Equal("queued", job.Status);
        Assert.Equal("Praha", store.Lease!.Job.RequestText);
        Assert.DoesNotContain("Praha", JsonSerializer.Serialize(job));
    }

    [Fact]
    public async Task WorkerPersistsCompletedResultAndDeletesQueueMessage()
    {
        var store = new MemoryStore();
        var job = await new TripService(store).SubmitAsync("Praha", default);
        var worker = new TripWorker(store, new Runner(false), NullLogger<TripWorker>.Instance);
        Assert.True(await worker.ProcessNextAsync(default));
        Assert.Equal("completed", store.Job!.Status);
        Assert.Equal("# Plan", store.Result);
        Assert.Null(store.Lease);
    }

    [Fact]
    public async Task WorkerSurfacesProviderFailureInsteadOfSavingSuccess()
    {
        var store = new MemoryStore();
        await new TripService(store).SubmitAsync("Praha", default);
        var worker = new TripWorker(store, new Runner(true), NullLogger<TripWorker>.Instance);
        await worker.ProcessNextAsync(default);
        Assert.Equal("failed", store.Job!.Status);
        Assert.NotEmpty(store.Job.Error!);
        Assert.Null(store.Result);
        Assert.Null(store.Lease);
    }

    [Fact]
    public async Task StreamingClientAcceptsKeepaliveAndRequiresSuccessfulTermination()
    {
        string? submitted = null;
        var handler = new Handler(async r =>
        {
            submitted = await r.Content!.ReadAsStringAsync();
            Assert.False(r.Headers.Contains("X-Hermes-Session-Id"));
            return Sse(
                ": keepalive\n\n" +
                "event: hermes.tool.progress\ndata: {\"tool\":\"delegate_task\"}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"# Plan\\nJak Hermes koordinoval agenty\"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
                "data: [DONE]\n\n");
        });
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:8642/v1/"), Timeout = Timeout.InfiniteTimeSpan };
        var runner = new HermesRunner(client, new string('s', 40), "travel-demo");
        var output = await runner.RunAsync(Guid.NewGuid().ToString(), "Praha", _ => Task.CompletedTask, default);
        Assert.Contains("Jak Hermes", output);
        Assert.True(JsonDocument.Parse(submitted!).RootElement.GetProperty("stream").GetBoolean());
    }

    [Theory]
    [InlineData("data: {\"error\":{\"message\":\"provider failure\"}}\n\n")]
    [InlineData("data: {\"choices\":[{\"finish_reason\":\"length\"}]}\n\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"wrong format\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n")]
    public async Task StreamingClientRejectsFailedTruncatedAndIncompleteResponses(string response)
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(Sse(response))))
            { BaseAddress = new("http://127.0.0.1/v1/") };
        var runner = new HermesRunner(client, new string('s', 40), "travel-demo");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            runner.RunAsync(Guid.NewGuid().ToString(), "Praha", _ => Task.CompletedTask, default));
    }

    [Fact]
    public async Task PublicApiPreservesAuthJsonContractAndResultRecovery()
    {
        Environment.SetEnvironmentVariable("TRIP_API_KEY", new string('k', 40));
        Environment.SetEnvironmentVariable("HERMES_API_KEY", new string('s', 40));
        Environment.SetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME", "testtripaccount");
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Contains("resume-button", await client.GetStringAsync("/"));
        Assert.Contains("pollStatus", await client.GetStringAsync("/app.js"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/trips", new { requestText = "Praha" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('k', 40));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/trips", new { requestText = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/trips", new { requestText = "Praha", extra = true })).StatusCode);
        var accepted = await client.PostAsJsonAsync("/api/trips", new { requestText = "Praha" });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var url = body.RootElement.GetProperty("statusUrl").GetString();
        app.Store.Job = app.Store.Job! with { Status = "completed", ResultBlob = "test.md" };
        app.Store.Result = "# Test itinerary";
        using var result = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.Equal("# Test itinerary", result.RootElement.GetProperty("result").GetString());
        app.Store.Result = null;
        using var expired = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.Equal("expired", expired.RootElement.GetProperty("status").GetString());
    }

    private static HttpResponseMessage Sse(string text) => new(HttpStatusCode.OK)
        { Content = new StringContent(text, Encoding.UTF8, "text/event-stream") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request);
    }
    private sealed class Runner(bool fail) : ITripRunner
    {
        public Task<string> RunAsync(string id, string request, Func<string, Task> progress, CancellationToken token) =>
            fail ? Task.FromException<string>(new InvalidDataException("provider error")) : Task.FromResult("# Plan");
    }
    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public MemoryStore Store { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITripStore>();
                services.AddSingleton<ITripStore>(Store);
                services.RemoveAll<IHostedService>();
            });
    }
}

public sealed class MemoryStore : ITripStore
{
    public TripJob? Job;
    public QueueLease? Lease;
    public string? Result;
    public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
    public Task CreateAsync(TripJob job, CancellationToken token) { Job = job; return Task.CompletedTask; }
    public Task EnqueueAsync(TripMessage message, CancellationToken token)
    { Lease = new(message, "message", "receipt", 1); return Task.CompletedTask; }
    public Task<TripJob?> GetAsync(string id, CancellationToken token) => Task.FromResult(Job?.Id == id ? Job : null);
    public Task UpdateAsync(string id, string status, string? blob, string? error, string? progress, CancellationToken token)
    { Job = Job! with { Status = status, ResultBlob = blob, Error = error, Progress = progress }; return Task.CompletedTask; }
    public Task<QueueLease?> ReceiveAsync(CancellationToken token) => Task.FromResult(Lease);
    public Task<QueueLease> RenewAsync(QueueLease lease, CancellationToken token) => Task.FromResult(lease);
    public Task DeleteAsync(QueueLease lease, CancellationToken token) { Lease = null; return Task.CompletedTask; }
    public Task<string> SaveResultAsync(string id, string result, CancellationToken token)
    { Result = result; return Task.FromResult(id + ".md"); }
    public Task<string?> GetResultAsync(string name, CancellationToken token) => Task.FromResult(Result);
}
