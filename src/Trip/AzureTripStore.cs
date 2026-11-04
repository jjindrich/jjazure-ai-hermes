using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using System.Text.Json;

namespace Trip;

public sealed class AzureTripStore : ITripStore
{
    private const string Partition = "tripRequests";
    private readonly TableClient table;
    private readonly QueueClient queue;
    private readonly BlobContainerClient blobs;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AzureTripStore(string account, TokenCredential? credential = null)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(account, "^[a-z0-9]{3,24}$"))
            throw new ArgumentException("AZURE_STORAGE_ACCOUNT_NAME is invalid.");
        credential ??= new DefaultAzureCredential();
        table = new(new Uri($"https://{account}.table.core.windows.net"), "triprequests", credential);
        // Keep plain JSON queue messages compatible with the existing Node deployment.
        queue = new(new Uri($"https://{account}.queue.core.windows.net/triprequests"), credential);
        blobs = new(new Uri($"https://{account}.blob.core.windows.net/trip-results"), credential);
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        await table.CreateIfNotExistsAsync(token);
        await queue.CreateIfNotExistsAsync(cancellationToken: token);
        await blobs.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: token);
    }

    public Task CreateAsync(TripJob job, CancellationToken token) =>
        table.AddEntityAsync(new TableEntity(Partition, job.Id)
        {
            ["status"] = job.Status,
            ["createdAt"] = job.CreatedAt.ToString("O"),
            ["updatedAt"] = job.UpdatedAt.ToString("O")
        }, token);

    public Task EnqueueAsync(TripMessage message, CancellationToken token) =>
        queue.SendMessageAsync(JsonSerializer.Serialize(message, Json),
            timeToLive: TimeSpan.FromDays(7), cancellationToken: token);

    public async Task<TripJob?> GetAsync(string id, CancellationToken token)
    {
        var result = await table.GetEntityIfExistsAsync<TableEntity>(Partition, id,
            cancellationToken: token);
        if (!result.HasValue) return null;
        var e = result.Value ?? throw new InvalidDataException("Storage returned a missing job entity.");
        return new(id, e.GetString("status")!, DateTimeOffset.Parse(e.GetString("createdAt")!),
            DateTimeOffset.Parse(e.GetString("updatedAt")!), e.GetString("resultBlob"),
            e.GetString("error"), e.GetString("progress"));
    }

    public Task UpdateAsync(string id, string status, string? resultBlob, string? error,
        string? progress, CancellationToken token)
    {
        var entity = new TableEntity(Partition, id)
        {
            ["status"] = status, ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        if (resultBlob is not null) entity["resultBlob"] = resultBlob;
        if (error is not null) entity["error"] = error;
        if (progress is not null) entity["progress"] = progress;
        return table.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, token);
    }

    public async Task<QueueLease?> ReceiveAsync(CancellationToken token)
    {
        var messages = await queue.ReceiveMessagesAsync(1, TimeSpan.FromMinutes(5), token);
        if (messages.Value.Length == 0) return null;
        var m = messages.Value[0];
        TripMessage? job;
        try { job = JsonSerializer.Deserialize<TripMessage>(m.MessageText, Json); }
        catch (JsonException ex) { throw new InvalidDataException($"Invalid queue message {m.MessageId}.", ex); }
        if (job is null || !Guid.TryParse(job.Id, out _) || string.IsNullOrWhiteSpace(job.RequestText))
            throw new InvalidDataException($"Invalid queue message {m.MessageId}.");
        return new(job, m.MessageId, m.PopReceipt, m.DequeueCount);
    }

    public async Task<QueueLease> RenewAsync(QueueLease lease, CancellationToken token)
    {
        var result = await queue.UpdateMessageAsync(lease.MessageId, lease.PopReceipt,
            visibilityTimeout: TimeSpan.FromMinutes(5), cancellationToken: token);
        return lease with { PopReceipt = result.Value.PopReceipt };
    }

    public Task DeleteAsync(QueueLease lease, CancellationToken token) =>
        queue.DeleteMessageAsync(lease.MessageId, lease.PopReceipt, token);

    public async Task<string> SaveResultAsync(string id, string result, CancellationToken token)
    {
        var name = $"{id}.md";
        await blobs.GetBlobClient(name).UploadAsync(BinaryData.FromString(result),
            new BlobUploadOptions { HttpHeaders = new() { ContentType = "text/markdown; charset=utf-8" } }, token);
        return name;
    }

    public async Task<string?> GetResultAsync(string name, CancellationToken token)
    {
        try
        {
            var result = await blobs.GetBlobClient(name).DownloadContentAsync(token);
            return result.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }
}
