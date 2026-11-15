using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using FlipRace.Core;

static class SocketSmoke
{
    public static async Task RunAsync(Uri http, string json, Action<bool, string> need)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = deadline.Token;
        var uri = new UriBuilder(http) { Scheme = http.Scheme == "https" ? "wss" : "ws", Path = "/api/live" }.Uri;
        string origin = http.GetLeftPart(UriPartial.Authority);

        ClientWebSocket Client(string requestedOrigin = null, string host = null)
        {
            var socket = new ClientWebSocket();
            socket.Options.CollectHttpResponseDetails = true;
            if (requestedOrigin != null) socket.Options.SetRequestHeader("Origin", requestedOrigin);
            if (host != null) socket.Options.SetRequestHeader("Host", host);
            return socket;
        }

        async Task Reject(string requestedOrigin, HttpStatusCode code, string name, string host = null)
        {
            using var socket = Client(requestedOrigin, host);
            try { await socket.ConnectAsync(uri, token); }
            catch (WebSocketException)
            {
                need(socket.HttpStatusCode == code, $"{name} status {socket.HttpStatusCode}");
                return;
            }
            need(false, name);
        }

        async Task<JsonElement> Start(ClientWebSocket socket, string source, double? startX = null)
        {
            await socket.ConnectAsync(uri, token);
            object start = startX.HasValue
                ? new { type = "start", json = source, profile = "Chosen", warningDuration = .8, startX, axis = "time", inputs = new[] { .013, .015 } }
                : new { type = "start", json = source, profile = "Chosen", warningDuration = .8 };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(start);
            await socket.SendAsync(bytes.AsMemory(0, bytes.Length / 2), WebSocketMessageType.Text, false, token);
            await socket.SendAsync(bytes.AsMemory(bytes.Length / 2), WebSocketMessageType.Text, true, token);
            return await Read(socket, token);
        }

        async Task<JsonElement> Step(ClientWebSocket socket, int seq, int steps, int[] inputs = null)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { type = "step", seq, steps, inputs });
            await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, token);
            return await Read(socket, token);
        }

        using var httpClient = new HttpClient();
        using var plain = await httpClient.GetAsync(new Uri(http, "/api/live"), token);
        need(plain.StatusCode == HttpStatusCode.BadRequest, "plain GET does not start a live attempt");
        await Reject(null, HttpStatusCode.BadRequest, "missing Origin");
        await Reject("null", HttpStatusCode.BadRequest, "opaque Origin");
        await Reject("https://other.example", HttpStatusCode.BadRequest, "foreign Origin");
        await Reject(origin + ", https://other.example", HttpStatusCode.BadRequest, "multiple Origins");
        await Reject(origin, HttpStatusCode.BadRequest, "wrong Host", "other.example");

        using (var first = Client(origin))
        using (var second = Client(origin))
        {
            var a = await Start(first, json);
            var b = await Start(second, json);
            need(a.GetProperty("type").GetString() == "state" && a.GetProperty("seq").GetInt32() == 0, "fragmented start accepted");
            need(b.GetProperty("countdownRemaining").GetDouble() == 3 && b.GetProperty("hiddenStates").GetArrayLength() > 0, "fresh countdown and hidden map states");
            await Reject(origin, HttpStatusCode.TooManyRequests, "two-slot capacity");
            using var health = await httpClient.GetAsync(new Uri(http, "/healthz"), token);
            need(health.IsSuccessStatusCode, "live capacity does not block HTTP");

            a = await Step(first, 1, 30, new[] { 0, 2 });
            b = await Step(second, 1, 1);
            need(a.GetProperty("countdownRemaining").GetDouble() < b.GetProperty("countdownRemaining").GetDouble(), "independent simulation timelines");
            need(a.GetProperty("inputs").GetArrayLength() == 2 && b.GetProperty("inputs").GetArrayLength() == 0, "independent press results");
            need(a.GetProperty("hiddenStates").GetArrayLength() == 0, "unchanged hidden states not repeated");

            // Keep the other slot occupied while replacing the aborted peer.
            second.Abort();
            for (int retry = 0; ; retry++)
            {
                using var replacement = Client(origin);
                try
                {
                    var fresh = await Start(replacement, json);
                    need(fresh.GetProperty("countdownRemaining").GetDouble() == 3, "abort releases its own slot and restart starts fresh");
                    need((await Step(replacement, 2, 1)).GetProperty("type").GetString() == "error", "out of order command rejected on the wire");
                    need(await ReadClose(replacement, token) == WebSocketCloseStatus.PolicyViolation, "bad sequence closes socket");
                    break;
                }
                catch (WebSocketException) when (replacement.HttpStatusCode == HttpStatusCode.TooManyRequests && retry < 20)
                {
                    await Task.Delay(20, token);
                }
            }
            await first.SendAsync(new byte[1025].AsMemory(), WebSocketMessageType.Text, true, token);
            need((await Read(first, token)).GetProperty("type").GetString() == "error", "oversized frame returns readable error");
            need(await ReadClose(first, token) == WebSocketCloseStatus.MessageTooBig, "step byte cap enforced");
        }

        string tiny = TrackJson.Write(new TrackPackage(new Track(2, Array.Empty<Obstacle>()) { Id = "live-tiny", Name = "Live tiny" }));
        using var finisher = Client(origin);
        var state = await Start(finisher, tiny);
        need(state.GetProperty("phase").GetString() == "countdown" && state.GetProperty("seq").GetInt32() == 0, "fresh run starts at zero");
        for (int seq = 1; seq <= 12; seq++)
        {
            state = await Step(finisher, seq, 30);
            await Task.Delay(180, token);
        }
        need(state.GetProperty("phase").GetString() == "racing" && state.GetProperty("sample").GetProperty("time").GetDouble() == 0, "wire countdown reaches racing at zero");
        state = await Step(finisher, 13, 30, new[] { 0, 29 });
        need(state.GetProperty("summary").GetProperty("status").GetString() == "Finished", "wire terminal summary sent");
        need(state.GetProperty("inputs")[1].GetProperty("status").GetString() == "unreached", "queued post-finish press cannot restart");
        need(await ReadClose(finisher, token) == WebSocketCloseStatus.NormalClosure, "terminal socket closed normally");

        using (var continued = Client(origin))
        {
            var ready = await Start(continued, json, startX: 3);
            need(ready.GetProperty("phase").GetString() == "racing" && ready.GetProperty("countdownRemaining").GetDouble() == 0 &&
                ready.GetProperty("seq").GetInt32() == 0 && ready.GetProperty("requestedStartX").GetDouble() == 3 &&
                ready.GetProperty("sample").GetProperty("x").GetDouble() >= 3, "wire marker gives the reached Core sample without a second countdown");
            var prefix = ready.GetProperty("prefixInputs");
            need(prefix.GetArrayLength() == 1 && prefix[0].GetProperty("time").GetDouble() == .013 &&
                ready.GetProperty("inputs").GetArrayLength() == 0, "wire marker sends the prefix separately and omits later authorship");
            var resumed = await Step(continued, 1, 1, new[] { 0 });
            need(resumed.GetProperty("prefixInputs").GetArrayLength() == 0 && resumed.GetProperty("inputs")[0].GetProperty("index").GetInt32() == 1,
                "wire continuation emits new input with the next absolute index");
            await continued.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, token);
        }

        using var expired = Client(origin);
        await expired.ConnectAsync(uri, token);
        need((await Read(expired, token)).GetProperty("type").GetString() == "error", "start deadline sends an error while receive is pending");
        need(await ReadClose(expired, token) == WebSocketCloseStatus.PolicyViolation, "expired startup socket closes");
    }

    static async Task<JsonElement> Read(ClientWebSocket socket, CancellationToken token)
    {
        using var stream = new MemoryStream();
        var bytes = new byte[4096];
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(bytes.AsMemory(), token);
            if (result.MessageType != WebSocketMessageType.Text) throw new Exception($"Expected live text, received {result.MessageType} ({socket.CloseStatus}).");
            stream.Write(bytes, 0, result.Count);
        } while (!result.EndOfMessage);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    static async Task<WebSocketCloseStatus?> ReadClose(ClientWebSocket socket, CancellationToken token)
    {
        var result = await socket.ReceiveAsync(new byte[32].AsMemory(), token);
        if (result.MessageType != WebSocketMessageType.Close) throw new Exception("Expected WebSocket close.");
        var status = socket.CloseStatus;
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, token);
        return status;
    }
}
