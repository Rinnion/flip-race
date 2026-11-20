using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FlipRace.Core;

namespace TrackWorkshop;

internal sealed record ToleranceBatchIdentity(
    [property: JsonRequired] int RulesVersion, [property: JsonRequired] int PhysicsVersion,
    string TrackFingerprint, RouteFlightSettings FlightSettings, string WarningMode, string Profile,
    [property: JsonRequired] double WarningDuration, WorkshopSurfaceRunSettings SurfaceRunSettings,
    string Axis, double[] Inputs, [property: JsonRequired] int SpanMs, [property: JsonRequired] int StepMs)
{
    const int CurrentRules = ManeuverToleranceAnalysis.RulesVersion;
    internal bool Supported => RulesVersion == CurrentRules && PhysicsVersion == TrackJson.PhysicsVersion &&
        SpanMs == ToleranceBatch.SpanMs && StepMs == ToleranceBatch.StepMs;

    internal static ToleranceBatchIdentity Capture(ReplayRequest request, TrackPackage track, FlightSettings flight) =>
        new(CurrentRules, TrackJson.PhysicsVersion, track.ContentHash, RouteFlightSettings.Capture(flight),
            Mode(track), request.Profile, request.WarningDuration, request.SurfaceRunSettings, request.Axis,
            request.Inputs, ToleranceBatch.SpanMs, ToleranceBatch.StepMs);

    static string Mode(TrackPackage track) => track.WarningMode == TrackWarningMode.Selected ? "selected" : "authored";

    internal bool Matches(TrackPackage track)
    {
        if (TrackFingerprint is not { Length: 64 } || !TrackFingerprint.All(Uri.IsHexDigit) ||
            WarningMode is not ("selected" or "authored") || Profile is not ("Chosen" or "FastFlip") ||
            FlightSettings == null || SurfaceRunSettings == null)
            throw new JsonException("Некорректные настройки матриц.");
        WorkshopService.ValidateWarning(WarningDuration);
        WorkshopService.ValidateAxis(Axis);
        WorkshopService.ValidateInputs(Inputs);
        SurfaceRunSettings.ToCore();
        var flight = Profile == "Chosen" ? FlightPresets.Chosen : FlightPresets.FastFlip;
        return TrackFingerprint == track.ContentHash && WarningMode == Mode(track) && track.SupportsProfile(flight) &&
            FlightSettings == RouteFlightSettings.Capture(flight);
    }

    internal string Key => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this, ToleranceBatchStorage.Json)));

    internal ToleranceBatchReport Report(ToleranceBatchTarget[] targets, int[] mapping, string reason) =>
        new(Profile, WarningDuration, SurfaceRunSettings, Axis, Inputs, SpanMs, StepMs, (ToleranceBatchTarget[])targets.Clone(),
            mapping, reason == "completed", reason);
}

internal sealed class ToleranceBatchStorage
{
    internal const string Key = "toleranceBatch";
    const int Version = 1;
    // Bound the expansion of compressed map metadata on import independently of
    // the game's existing 2 MiB file size limit.
    const int TargetByteLimit = 1024 * 1024, DecodedByteLimit = 64 * 1024 * 1024;
    const int PayloadChunkSize = 4096;
    internal const string SizeMessage = "В документе нет места для всех матриц. Сохранены результаты, которые поместились.";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    sealed record StoredBatch([property: JsonRequired] int Version, ToleranceBatchIdentity Identity,
        ToleranceBatchTarget[] Targets, int[] CommandTargetIndices, ToleranceCheckpoint[] Checkpoints, string StopReason);
    readonly record struct Entry(ToleranceCheckpoint Checkpoint, int StoredLength);
    readonly DeveloperDocument document;
    readonly JsonObject root;
    readonly JsonNode prior;
    readonly JsonObject batch;
    readonly int baseBytes;
    readonly SortedDictionary<int, Entry> entries = new();
    int storedBytes, decodedBytes;
    internal ToleranceBatchIdentity Identity { get; }
    internal string KeyForIdentity { get; }
    internal ToleranceCheckpoint[] Checkpoints => entries.Values.Select(entry => entry.Checkpoint).ToArray();

    internal ToleranceBatchStorage(DeveloperDocument document, ToleranceBatchIdentity identity,
        ToleranceBatchTarget[] targets, int[] mapping)
    {
        this.document = document;
        Identity = identity;
        KeyForIdentity = identity.Key;
        root = document.ToleranceBatchRoot();
        prior = root[Key];
        if (prior is JsonObject priorObject && priorObject["version"] is JsonValue version &&
            version.TryGetValue<int>(out int number) && number != Version)
            throw new ArgumentException("Версия сохранённых матриц неизвестна; исходный JSON сохранён.");
        batch = JsonSerializer.SerializeToNode(new StoredBatch(Version, identity, targets, mapping, [], "storage-limit"), Json).AsObject();
        root[Key] = batch;
        baseBytes = Encoding.UTF8.GetByteCount(document.WithAnalysis(root));
        if (baseBytes > TrackJson.MaximumBytes)
            throw new ArgumentException("В документе нет места для анализа при лимите 2 МиБ. Исходный файл сохранён.");
    }

    internal ToleranceCheckpoint[] SavedCheckpoints()
    {
        if (prior == null) return [];
        try
        {
            var stored = prior.Deserialize<StoredBatch>(Json);
            if (stored?.Version != Version || stored.Identity?.Supported != true ||
                stored.Identity.Key != KeyForIdentity) return [];
            if (!ValidPlan(stored)) throw new JsonException();
            return stored.Checkpoints;
        }
        catch (Exception ex) when (IsInvalid(ex)) { throw new ArgumentException("Некорректные сохранённые матрицы.", ex); }
    }

    internal ToleranceCheckpoint Pack(ToleranceBatchTarget target)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(target, Json);
        using var payload = new MemoryStream();
        using (var gzip = new GZipStream(payload, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(bytes);
        string encoded = Convert.ToBase64String(payload.GetBuffer().AsSpan(0, (int)payload.Length));
        string[] chunks = Enumerable.Range(0, (encoded.Length + PayloadChunkSize - 1) / PayloadChunkSize)
            .Select(i => encoded.Substring(i * PayloadChunkSize, Math.Min(PayloadChunkSize, encoded.Length - i * PayloadChunkSize))).ToArray();
        return new(KeyForIdentity, target.InputIndex, bytes.Length, chunks);
    }

    internal bool TryAdd(ToleranceCheckpoint checkpoint)
    {
        int size = JsonSerializer.SerializeToUtf8Bytes(checkpoint, Json).Length;
        bool found = entries.TryGetValue(checkpoint.InputIndex, out var old);
        int count = entries.Count + (found ? 0 : 1);
        int nextStored = storedBytes - old.StoredLength + size;
        int nextDecoded = decodedBytes - (found ? old.Checkpoint.Length : 0) + checkpoint.Length;
        if (checkpoint.Length > TargetByteLimit || nextDecoded > DecodedByteLimit ||
            baseBytes + nextStored + count - 1 > TrackJson.MaximumBytes) return false;
        entries[checkpoint.InputIndex] = new(checkpoint, size);
        storedBytes = nextStored;
        decodedBytes = nextDecoded;
        return true;
    }

    internal string Save(string reason)
    {
        batch["checkpoints"] = JsonSerializer.SerializeToNode(Checkpoints, Json);
        batch["stopReason"] = reason;
        string json = document.WithAnalysis(root);
        if (Encoding.UTF8.GetByteCount(json) > TrackJson.MaximumBytes)
            throw new InvalidOperationException("The tolerance batch exceeded its calculated storage limit.");
        return json;
    }

    internal ToleranceBatchTarget Decode(ToleranceCheckpoint checkpoint, ToleranceBatchTarget planned)
    {
        try { return Decode(checkpoint, planned, Identity); }
        catch (Exception ex) when (IsInvalid(ex)) { throw new ArgumentException("Некорректная сохранённая матрица.", ex); }
    }

    static ToleranceBatchTarget Decode(ToleranceCheckpoint checkpoint, ToleranceBatchTarget planned, ToleranceBatchIdentity identity)
    {
        if (checkpoint.Payload is not { Length: > 0 and <= TrackJson.MaximumBytes / PayloadChunkSize } ||
            checkpoint.Payload.Any(chunk => chunk is not { Length: > 0 and <= PayloadChunkSize }) ||
            checkpoint.Length is <= 0 or > TargetByteLimit)
            throw new JsonException();
        using var payload = new MemoryStream(Convert.FromBase64String(string.Concat(checkpoint.Payload)), writable: false);
        using var gzip = new GZipStream(payload, CompressionMode.Decompress);
        var bytes = new byte[checkpoint.Length];
        gzip.ReadExactly(bytes);
        if (gzip.ReadByte() != -1) throw new JsonException();
        var target = JsonSerializer.Deserialize<ToleranceBatchTarget>(bytes, Json);
        if (target == null || target.Kind != planned.Kind || target.InputIndex != planned.InputIndex ||
            target.FirstInputIndex != planned.FirstInputIndex || target.Message != planned.Message ||
            target.Single != null || target.Pair != null || target.InputIndices == null ||
            !target.InputIndices.SequenceEqual(planned.InputIndices) || !ValidManeuver(target, identity))
            throw new JsonException();
        return target;
    }

    static bool ValidManeuver(ToleranceBatchTarget target, ToleranceBatchIdentity identity)
    {
        var result = target.Maneuver;
        int n = target.InputIndices.Length;
        if (result == null || result.RulesVersion != ManeuverToleranceAnalysis.RulesVersion || result.Message == null ||
            result.SpanMs != identity.SpanMs || result.StepMs != identity.StepMs || result.Cells?.Length != n * 25 ||
            result.Total != ManeuverToleranceAnalysis.Text(ManeuverToleranceAnalysis.Count(n)) ||
            !ManeuverToleranceAnalysis.Number(result.Cursor, out var cursor) || cursor > ManeuverToleranceAnalysis.Count(n) ||
            !ManeuverToleranceAnalysis.Number(result.Valid, out var valid) || valid > cursor || result.Computed != result.Valid ||
            !ManeuverToleranceAnalysis.Number(result.Safe, out var safe) || safe > valid ||
            result.Percent != ManeuverToleranceAnalysis.Percentage(safe, valid) ||
            result.StopReason is not ("completed" or "time-budget" or "chunk-limit" or "cancelled")) return false;
        bool unsupported = result.Status == "unsupported";
        if (result.Status is not ("ready" or "pending" or "unsupported") ||
            result.Complete != (unsupported || cursor == ManeuverToleranceAnalysis.Count(n)) ||
            result.Complete != (result.StopReason == "completed")) return false;
        var baseline = result.Baseline;
        if (baseline == null)
        {
            if (result.Status == "ready" || cursor != 0 || valid != 0 || safe != 0) return false;
        }
        else
        {
            if (result.Status != "ready" || baseline.Inputs?.Length != identity.Inputs.Length ||
                !double.IsFinite(baseline.StartTime) || !double.IsFinite(baseline.EndTime) || !double.IsFinite(baseline.EndX) ||
                baseline.StartTime < 0 || baseline.EndTime < baseline.StartTime || baseline.EndTime < baseline.Inputs[target.InputIndices[^1]]) return false;
            WorkshopService.ValidateInputs(baseline.Inputs);
            if (identity.Axis == "time" && !baseline.Inputs.SequenceEqual(identity.Inputs)) return false;
        }
        if (safe == 0 ? result.SafeWindow != null : result.SafeWindow == null ||
            !double.IsFinite(result.SafeWindow.EarliestMs) || !double.IsFinite(result.SafeWindow.LatestMs) ||
            result.SafeWindow.EarliestMs < -1e-7 || result.SafeWindow.LatestMs < result.SafeWindow.EarliestMs) return false;
        for (int row = 0; row < n; row++)
        {
            System.Numerics.BigInteger rowValid = 0, rowSafe = 0;
            var block = ManeuverToleranceAnalysis.Count(n - row - 1);
            for (int digit = 0; digit < 25; digit++)
            {
                var cell = result.Cells[row * 25 + digit];
                var cycles = cursor / (block * 25);
                var remainder = cursor % (block * 25) - digit * block;
                var visited = cycles * block + System.Numerics.BigInteger.Min(block, System.Numerics.BigInteger.Max(0, remainder));
                if (cell == null || cell.InputIndex != target.InputIndices[row] || cell.OffsetMs != digit * 50 - 600 ||
                    cell.Total != ManeuverToleranceAnalysis.Text(ManeuverToleranceAnalysis.Count(n - 1)) ||
                    !ManeuverToleranceAnalysis.Number(cell.Valid, out var cv) || cv > visited || cell.Computed != cell.Valid ||
                    !ManeuverToleranceAnalysis.Number(cell.Safe, out var cs) || cs > cv ||
                    cell.Percent != ManeuverToleranceAnalysis.Percentage(cs, cv)) return false;
                rowValid += cv; rowSafe += cs;
                var witness = cell.WitnessOffsetsMs;
                if (cs == 0) { if (witness != null) return false; }
                else if (baseline == null || !ManeuverToleranceAnalysis.Grid(witness, n) || witness[row] != cell.OffsetMs ||
                    ManeuverToleranceAnalysis.Rank(witness) >= cursor ||
                    ManeuverToleranceAnalysis.InvalidAt(ManeuverToleranceAnalysis.Shifted(baseline.Inputs, target.InputIndices, witness), target.InputIndices) >= 0)
                    return false;
            }
            if (rowValid != valid || rowSafe != safe) return false;
        }
        return true;
    }

    internal static ToleranceBatchStatus Read(JsonElement raw, TrackPackage track)
    {
        if (raw.GetProperty("version").GetInt32() != Version) return new("unsupported");
        var stored = raw.Deserialize<StoredBatch>(Json);
        var identity = stored?.Identity ?? throw new JsonException();
        if (!identity.Supported) return new("stale");
        if (!identity.Matches(track)) return new("stale");
        if (!ValidPlan(stored)) return new("invalid");
        var targets = stored.Targets;
        var lookup = targets.Select((target, index) => (target.InputIndex, index)).ToDictionary(item => item.InputIndex, item => item.index);
        var seen = new HashSet<int>();
        string key = identity.Key;
        long decodedBytes = 0;
        foreach (var checkpoint in stored.Checkpoints)
        {
            if (checkpoint == null || checkpoint.Identity != key || !seen.Add(checkpoint.InputIndex) ||
                !lookup.TryGetValue(checkpoint.InputIndex, out int index) || targets[index].Kind == "unsupported")
                return new("invalid");
            decodedBytes += checkpoint.Length;
            if (decodedBytes > DecodedByteLimit) return new("invalid");
            targets[index] = Decode(checkpoint, targets[index], identity);
        }
        bool complete = targets.All(target => target.Finished);
        if (complete != (stored.StopReason == "completed") ||
            stored.StopReason is not ("completed" or "cancelled" or "time-budget" or "storage-limit"))
            return new("invalid");
        return new("ready", identity.Report(targets, stored.CommandTargetIndices, stored.StopReason));
    }

    static bool ValidPlan(StoredBatch stored)
    {
        int count = stored.Identity.Inputs.Length;
        var targets = stored.Targets;
        var mapping = stored.CommandTargetIndices;
        if (targets == null || targets.Length == 0 || targets.Length > count || stored.Checkpoints == null ||
            stored.Checkpoints.Length > targets.Length || mapping?.Length != count) return false;
        int next = 0;
        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (target == null || target.InputIndex != next || target.Message == null ||
                target.Single != null || target.Pair != null || target.Maneuver != null || target.InputIndices is not { Length: > 0 } ||
                target.InputIndices.Length > count - next) return false;
            if (target.Kind == "maneuver")
            {
                if (target.FirstInputIndex != next || target.Message.Length != 0) return false;
            }
            else if (target.Kind != "unsupported" || target.FirstInputIndex != null || target.InputIndices.Length != 1 || target.Message.Length == 0) return false;
            foreach (int index in target.InputIndices)
                if (index != next || mapping[next++] != i) return false;
        }
        return next == count;
    }

    internal static bool IsInvalid(Exception ex) => ex is JsonException or InvalidOperationException or
        KeyNotFoundException or FormatException or ArgumentException or IOException or InvalidDataException;
}
