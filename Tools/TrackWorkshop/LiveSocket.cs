using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlipRace.Core;

namespace TrackWorkshop;

internal sealed class LiveSocket(ServerOptions options) : IDisposable
{
    // A JSON string may use six wire bytes for a one-byte source character.
    const int MaximumStartBytes = 6 * TrackJson.MaximumBytes + 128 * 1024 + 1024;
    const int MaximumStepBytes = 1024;
    static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan PrefixTimeout = TimeSpan.FromSeconds(8);
    static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(120);
    static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);
    static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(10);
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    readonly SemaphoreSlim capacity = new(2, 2);

    public async Task HandleAsync(HttpContext context)
    {
        string origin = context.Request.Headers.Origin.ToString();
        if (origin.Length == 0 || !options.AcceptsOrigin(context.Request.Host, origin) || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Требуется WebSocket с того же адреса мастерской." }, context.RequestAborted);
            return;
        }
        if (!capacity.Wait(0))
        {
            context.Response.StatusCode = 429;
            context.Response.Headers.RetryAfter = "1";
            await context.Response.WriteAsJsonAsync(new { error = "Все места для забегов заняты. Повторите попытку позже." }, context.RequestAborted);
            return;
        }

        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await RunAsync(socket, context.RequestAborted);
        }
        finally { capacity.Release(); }
    }

    static async Task RunAsync(WebSocket socket, CancellationToken disconnected)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(disconnected);
        lifetime.CancelAfter(MaximumLifetime);
        Task pendingRead = null;
        async Task<T> Read<T>(int limit, TimeSpan timeout) where T : class
        {
            // Canceling ReceiveAsync itself aborts the socket. Keep the read
            // pending on expiry so the peer can receive an error and close.
            var receiving = ReadAsync<T>(socket, limit, disconnected);
            try { return await receiving.WaitAsync(timeout, lifetime.Token); }
            catch (TimeoutException) { pendingRead = receiving; throw; }
            catch (OperationCanceledException) { pendingRead = receiving; throw; }
        }

        try
        {
            var start = await Read<LiveStart>(MaximumStartBytes, StartTimeout);
            if (start == null) { await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, disconnected); return; }
            var race = StartRace(start, lifetime.Token);
            await SendAsync(socket, race.State(), lifetime.Token);
            var budget = new LiveRate();
            while (!race.IsTerminal)
            {
                var step = await Read<LiveStep>(MaximumStepBytes, IdleTimeout);
                if (step == null) break;
                if (step.Steps is < 1 or > LiveRace.MaxSteps || !budget.Consume(step.Steps))
                    throw new ArgumentException("Слишком много команд или шагов забега.");
                await SendAsync(socket, race.Step(step, lifetime.Token), lifetime.Token);
            }
            await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, disconnected);
        }
        catch (JsonException)
        {
            await FailAsync(socket, "Неверный формат команды забега.", WebSocketCloseStatus.InvalidPayloadData, disconnected);
        }
        catch (LiveMessageException error)
        {
            await FailAsync(socket, error.Message, error.Status, disconnected);
        }
        catch (ArgumentException error)
        {
            await FailAsync(socket, error.Message, WebSocketCloseStatus.PolicyViolation, disconnected);
        }
        catch (OperationCanceledException) when (disconnected.IsCancellationRequested) { }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        {
            await FailAsync(socket, "Истекло время соединения забега. Запустите новый забег.", WebSocketCloseStatus.PolicyViolation, disconnected, pendingRead);
        }
        catch (WebSocketException) { }
        finally
        {
            if (pendingRead != null)
            {
                if (!pendingRead.IsCompleted) socket.Abort();
                await pendingRead.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    static LiveRace StartRace(LiveStart start, CancellationToken cancellationToken)
    {
        if (start.StartX <= 0) return new LiveRace(start, cancellationToken);
        using var prefix = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        prefix.CancelAfter(PrefixTimeout);
        try { return new LiveRace(start, prefix.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ArgumentException("Не удалось подготовить участок до точки старта за 8 с.");
        }
    }

    static async Task<T> ReadAsync<T>(WebSocket socket, int limit, CancellationToken cancellationToken) where T : class
    {
        var buffer = new ArrayBufferWriter<byte>();
        while (true)
        {
            int read = Math.Min(4096, limit - buffer.WrittenCount + 1);
            var result = await socket.ReceiveAsync(buffer.GetMemory(read)[..read], cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new LiveMessageException("Команда должна быть текстом JSON.", WebSocketCloseStatus.InvalidMessageType);
            if (result.Count > limit - buffer.WrittenCount)
                throw new LiveMessageException("Сообщение забега слишком велико.", WebSocketCloseStatus.MessageTooBig);
            buffer.Advance(result.Count);
            if (result.EndOfMessage)
                return JsonSerializer.Deserialize<T>(buffer.WrittenSpan, Json) ?? throw new JsonException("Empty command.");
        }
    }

    static async Task SendAsync<T>(WebSocket socket, T message, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(SendTimeout);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, deadline.Token);
    }

    static async Task FailAsync(WebSocket socket, string error, WebSocketCloseStatus status, CancellationToken cancellationToken, Task pendingRead = null)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
                await SendAsync(socket, new { type = "error", error }, cancellationToken);
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        await CloseAsync(socket, status, cancellationToken, pendingRead);
    }

    static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, CancellationToken cancellationToken, Task pendingRead = null)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            if (pendingRead == null) await socket.CloseAsync(status, string.Empty, deadline.Token);
            else
            {
                await socket.CloseOutputAsync(status, string.Empty, deadline.Token);
                await pendingRead.WaitAsync(deadline.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
    }

    sealed class LiveMessageException(string message, WebSocketCloseStatus status) : Exception(message)
    {
        public WebSocketCloseStatus Status { get; } = status;
    }

    public void Dispose() => capacity.Dispose();
}

internal sealed class LiveRate
{
    const double MaxCommands = 12, MaxTicks = 120;
    readonly TimeProvider clock;
    long previous;
    double commands = MaxCommands, ticks = MaxTicks;

    public LiveRate(TimeProvider clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        previous = this.clock.GetTimestamp();
    }

    public bool Consume(int count)
    {
        long now = clock.GetTimestamp();
        double elapsed = clock.GetElapsedTime(previous, now).TotalSeconds;
        previous = now;
        commands = Math.Min(MaxCommands, commands + elapsed * 45);
        ticks = Math.Min(MaxTicks, ticks + elapsed * 180);
        if (commands < 1 || ticks < count) return false;
        commands -= 1;
        ticks -= count;
        return true;
    }
}
