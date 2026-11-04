using System.Text;

namespace Trip;

public sealed record TripSubmission(string RequestText);
public sealed record TripMessage(string Id, string RequestText);
public sealed record QueueLease(TripMessage Job, string MessageId, string PopReceipt, long DequeueCount);
public sealed record TripDebugEvent(DateTimeOffset At, string Message);
public sealed record TripJob(string Id, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, string? ResultBlob = null, string? Error = null,
    string? Progress = null, IReadOnlyList<TripDebugEvent>? DebugEvents = null);

public interface ITripStore
{
    Task InitializeAsync(CancellationToken token);
    Task CreateAsync(TripJob job, CancellationToken token);
    Task EnqueueAsync(TripMessage message, CancellationToken token);
    Task<TripJob?> GetAsync(string id, CancellationToken token);
    Task UpdateAsync(string id, string status, string? resultBlob, string? error,
        string? progress, CancellationToken token, IReadOnlyList<TripDebugEvent>? debugEvents = null);
    Task<QueueLease?> ReceiveAsync(CancellationToken token);
    Task<QueueLease> RenewAsync(QueueLease lease, CancellationToken token);
    Task DeleteAsync(QueueLease lease, CancellationToken token);
    Task<string> SaveResultAsync(string id, string result, CancellationToken token);
    Task<string?> GetResultAsync(string blobName, CancellationToken token);
}

public interface ITripRunner
{
    Task<string> RunAsync(string id, string request, Func<string, Task> progress,
        CancellationToken token);
}

public sealed class TripService(ITripStore store)
{
    public const int MaximumBytes = 24 * 1024;
    public static string Validate(string? request)
    {
        if (string.IsNullOrWhiteSpace(request))
            throw new ArgumentException("Zadani nesmi byt prazdne.");
        if (request.Length > 20_000 || Encoding.UTF8.GetByteCount(request) > MaximumBytes)
            throw new ArgumentException("Zadani je prilis dlouhe (maximum 20 000 znaku / 24 KiB).");
        return request.Trim();
    }

    public async Task<TripJob> SubmitAsync(string? request, CancellationToken token)
    {
        var text = Validate(request);
        var now = DateTimeOffset.UtcNow;
        var job = new TripJob(Guid.NewGuid().ToString(), "queued", now, now);
        await store.CreateAsync(job, token);
        try { await store.EnqueueAsync(new(job.Id, text), token); }
        catch
        {
            await store.UpdateAsync(job.Id, "failed", null,
                "Pozadavek se nepodarilo zaradit do fronty.", null, CancellationToken.None);
            throw;
        }
        return job;
    }
}
