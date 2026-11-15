using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;

namespace TrackWorkshop;

internal static class RouteSearchStream
{
    internal const string Path = "/api/route-search/stream";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task HandleAsync(WorkshopService service, RouteSearchRequest request, HttpContext context, ILogger logger)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var cancellation = lifetime.Token;
        // The producer reports each integer 0–99 at most once. Reserve room
        // for each value so a slow reader cannot stall the search budget.
        var progress = Channel.CreateBounded<RouteSearchProgress>(new BoundedChannelOptions(100)
        {
            SingleReader = true,
            SingleWriter = true
        });
        var worker = Task.Run(() =>
        {
            try { return service.SearchRoutes(request, cancellation, update => progress.Writer.TryWrite(update)); }
            finally { progress.Writer.TryComplete(); }
        });

        var response = context.Response;
        async Task WriteLine<T>(T message)
        {
            cancellation.ThrowIfCancellationRequested();
            string line = JsonSerializer.Serialize(message, Json) + "\n";
            if (!response.HasStarted)
            {
                response.ContentType = "application/x-ndjson; charset=utf-8";
                response.Headers.CacheControl = "no-store";
                response.Headers["X-Accel-Buffering"] = "no";
                context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            }
            await response.WriteAsync(line, cancellation);
            await response.Body.FlushAsync(cancellation);
        }

        try
        {
            await foreach (var update in progress.Reader.ReadAllAsync(cancellation))
                await WriteLine(new { type = "progress", update.Percent, update.Phase });

            RouteSearchResponse result;
            try { result = await worker; }
            catch (ArgumentException error) when (response.HasStarted && !cancellation.IsCancellationRequested)
            {
                await WriteLine(new { type = "error", error = error.Message });
                return;
            }
            catch (Exception error) when (response.HasStarted && !cancellation.IsCancellationRequested)
            {
                logger.LogError(error, "Workshop route search failed after progress was sent.");
                await WriteLine(new { type = "error", error = "Не удалось завершить поиск вариантов." });
                return;
            }
            await WriteLine(new { type = "result", result });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (IOException) { context.Abort(); }
        finally
        {
            await lifetime.CancelAsync();
            // On disconnect the reader can finish before the worker observes
            // cancellation. Keep the request's concurrency slot until it exits.
            await ((Task)worker).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
