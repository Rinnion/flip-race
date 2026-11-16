using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace TrackWorkshop;

internal static class ToleranceBatchStream
{
    internal const string Path = "/api/tolerance-batch/stream";
    internal const string CheckpointPath = "/api/tolerance-batch/checkpoint";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task HandleAsync(ReplayRequest request, HttpContext context, ILogger logger)
    {
        var cancellation = context.RequestAborted;
        var response = context.Response;

        async Task WriteLine<T>(T value)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!response.HasStarted)
            {
                response.ContentType = "application/x-ndjson; charset=utf-8";
                response.Headers.CacheControl = "no-store";
                response.Headers["X-Accel-Buffering"] = "no";
                context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            }
            await response.WriteAsync(JsonSerializer.Serialize(value, Json) + "\n", cancellation);
            await response.Body.FlushAsync(cancellation);
        }

        try
        {
            var batch = await Task.Run(() =>
            {
                var prepared = ToleranceBatch.Prepare(request, cancellation);
                prepared.Restore(cancellation);
                return prepared;
            }, cancellation);
            await WriteLine(new { type = "plan", batch = batch.Report("pending"), checkpoints = batch.Checkpoints });
            string reason = "time-budget";
            for (int i = 0; i < batch.Total; i++)
            {
                var (target, checkpoint) = await Task.Run(() => batch.Evaluate(i, cancellation), cancellation);
                if (target == null) continue;
                if (checkpoint == null)
                {
                    reason = "storage-limit";
                    break;
                }
                await WriteLine(new { type = "progress", completed = batch.Completed, total = batch.Total, target, checkpoint });
            }
            var result = batch.Save(reason);
            await WriteLine(new { type = "result", result.CanonicalJson, result.Batch });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (IOException) { context.Abort(); }
        catch (ArgumentException error) when (response.HasStarted && !cancellation.IsCancellationRequested)
        { await WriteLine(new { type = "error", error = error.Message }); }
        catch (Exception error) when (response.HasStarted && !cancellation.IsCancellationRequested)
        {
            logger.LogError(error, "Workshop tolerance batch failed after progress was sent.");
            await WriteLine(new { type = "error", error = "Не удалось завершить расчёт матриц." });
        }
    }
}
