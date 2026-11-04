namespace Trip;

public sealed class TripWorker(ITripStore store, ITripRunner runner, ILogger<TripWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessNextAsync(stoppingToken))
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Trip worker poll failed.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessNextAsync(CancellationToken token)
    {
        var lease = await store.ReceiveAsync(token);
        if (lease is null) return false;
        var job = await store.GetAsync(lease.Job.Id, token);
        if (job is null || job.Status is "completed" or "failed")
        {
            await store.DeleteAsync(lease, token);
            return true;
        }
        if (lease.DequeueCount > 3)
        {
            await store.UpdateAsync(job.Id, "failed", null,
                "Zpracovani bylo opakovane preruseno. Pozadavek prosim zadejte znovu.", null, token);
            await store.DeleteAsync(lease, token);
            return true;
        }
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        await store.UpdateAsync(job.Id, "running", null, null, "Hermes pripravuje plan.", token);
        var heartbeat = RenewAsync();
        var terminalSaved = false;
        try
        {
            var result = await runner.RunAsync(job.Id, lease.Job.RequestText,
                p => store.UpdateAsync(job.Id, "running", null, null, p, runCancellation.Token),
                runCancellation.Token);
            var blob = await store.SaveResultAsync(job.Id, result, runCancellation.Token);
            await store.UpdateAsync(job.Id, "completed", blob, null, "Plan je hotovy.", runCancellation.Token);
            terminalSaved = true;
            logger.LogInformation("Trip {JobId} completed; {Characters} characters.", job.Id, result.Length);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.LogWarning("Trip {JobId} interrupted by shutdown; queue message retained.", job.Id);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Trip {JobId} failed.", job.Id);
            var error = ex is OperationCanceledException or TimeoutException
                ? "Zpracovani prekrocilo casovy limit nebo spojeni s Hermes neodpovida."
                : "Hermes nedokoncil plan. Zkontrolujte pripojeni modelu; spravce ma podrobnosti v logu.";
            await store.UpdateAsync(job.Id, "failed", null, error, "Zpracovani selhalo.", token);
            terminalSaved = true;
        }
        finally
        {
            await heartbeatCancellation.CancelAsync();
            await heartbeat;
            if (terminalSaved) await store.DeleteAsync(lease, token);
        }
        return true;

        async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), heartbeatCancellation.Token);
                    lease = await store.RenewAsync(lease, heartbeatCancellation.Token);
                }
            }
            catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Queue lease renewal failed for trip {JobId}.", job.Id);
                await runCancellation.CancelAsync();
            }
        }
    }
}
