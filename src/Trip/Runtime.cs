using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Trip;

public sealed class Runtime : IDisposable
{
    public required string IntakeKey { get; init; }
    public required string HermesKey { get; init; }
    public Process? Gateway { get; private set; }

    public static async Task<Runtime> StartAsync(CancellationToken token)
    {
        var sandbox = Environment.GetEnvironmentVariable("HERMES_HOME") is not null;
        var (intake, hermes, copilot) = ReadConfiguration(Environment.GetEnvironmentVariable, sandbox);
        var runtime = new Runtime { IntakeKey = intake, HermesKey = hermes };
        if (!sandbox) return runtime;
        ValidateHermesConfiguration(Path.Combine(AppContext.BaseDirectory, "hermes", "sandbox-config.yaml"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "hermes", "sandbox-config.yaml"),
            Path.Combine(Environment.GetEnvironmentVariable("HERMES_HOME")!, "config.yaml"), true);
        var start = new ProcessStartInfo("hermes") { UseShellExecute = false, WorkingDirectory = "/app" };
        foreach (var arg in new[] { "gateway", "run", "--no-supervise", "--external-supervisor" })
            start.ArgumentList.Add(arg);
        start.Environment["COPILOT_GITHUB_TOKEN"] = copilot;
        start.Environment["API_SERVER_ENABLED"] = "true";
        start.Environment["API_SERVER_HOST"] = "127.0.0.1";
        start.Environment["API_SERVER_PORT"] = "8642";
        start.Environment["API_SERVER_KEY"] = runtime.HermesKey;
        runtime.Gateway = Process.Start(start) ?? throw new InvalidOperationException("Could not start Hermes.");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.HermesKey);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (runtime.Gateway.HasExited) throw new InvalidOperationException("Hermes exited before readiness.");
                try
                {
                    using var response = await http.GetAsync("http://127.0.0.1:8642/v1/models", deadline.Token);
                    if (response.IsSuccessStatusCode) return runtime;
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
                await Task.Delay(1000, deadline.Token);
            }
        }
        catch { runtime.Dispose(); throw; }
    }

    public static (string IntakeKey, string HermesKey, string? CopilotToken) ReadConfiguration(
        Func<string, string?> read, bool sandbox)
    {
        var intake = read("TRIP_API_KEY");
        if (string.IsNullOrWhiteSpace(intake) || intake.Length < 32)
            throw new InvalidOperationException("Configure TRIP_API_KEY with at least 32 characters at deployment.");
        var hermes = read("HERMES_API_KEY") ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (hermes.Length < 32)
            throw new InvalidOperationException("HERMES_API_KEY must contain at least 32 characters.");
        var copilot = read("TRIP_COPILOT_TOKEN") ?? read("COPILOT_GITHUB_TOKEN");
        if (sandbox && (string.IsNullOrWhiteSpace(copilot) || copilot.Length < 32))
            throw new InvalidOperationException("Configure the runtime-only COPILOT_GITHUB_TOKEN at deployment.");
        return (intake, hermes, copilot);
    }

    public static void ValidateHermesConfiguration(string path)
    {
        var lines = File.ReadAllLines(path);
        if (!lines.Contains("  context_length: 64000") ||
            !lines.Contains("  max_spawn_depth: 1") ||
            !lines.Contains("  max_concurrent_children: 3"))
            throw new InvalidDataException("Hermes requires the 64000-token context and one batch of three leaf agents.");
    }

    public void Dispose()
    {
        if (Gateway is not null)
        {
            if (!Gateway.HasExited) Gateway.Kill(entireProcessTree: true);
            Gateway.Dispose();
        }
    }
}

public sealed class GatewayMonitor(Runtime runtime, IHostApplicationLifetime lifetime,
    ILogger<GatewayMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (runtime.Gateway is null) return;
        await runtime.Gateway.WaitForExitAsync(stoppingToken);
        if (!stoppingToken.IsCancellationRequested)
        {
            logger.LogCritical("Hermes Gateway exited; stopping unhealthy intake.");
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }
}
