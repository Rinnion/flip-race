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
    const int CurrentRules = 1;
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
        if (target == null || target with { Single = null, Pair = null } != planned ||
            (planned.Kind == "pair" ? target.Single != null || !ValidPair(target, identity)
                                    : target.Pair != null || !ValidSingle(target, identity)))
            throw new JsonException();
        return target;
    }

    static bool ValidReport(string status, string message, bool complete, string stopReason, bool hasBaseline) =>
        message != null && status == (hasBaseline ? "ready" : "unsupported") &&
        stopReason == (complete ? "completed" : "time-budget") &&
        (complete || message == InputToleranceAnalysis.BudgetMessage);

    static bool ValidTape(double[] tape, ToleranceBatchIdentity identity)
    {
        if (tape?.Length != identity.Inputs.Length) return false;
        WorkshopService.ValidateInputs(tape);
        return identity.Axis == "distance" || tape.SequenceEqual(identity.Inputs);
    }

    static bool ValidCell(string status, string message, bool computed, int collisions) =>
        message != null && status is "safe" or "invalid" or "collision" or "rejected" or "incomplete" &&
        (computed || status == "incomplete" && message == InputToleranceAnalysis.BudgetMessage) &&
        collisions == (status == "collision" ? 1 : 0);

    static bool ValidPair(ToleranceBatchTarget target, ToleranceBatchIdentity identity)
    {
        var report = target.Pair;
        if (report == null || report.FirstInputIndex != target.InputIndex || report.SpanMs != identity.SpanMs ||
            report.StepMs != identity.StepMs || !ValidReport(report.Status, report.Message, report.Complete,
                report.StopReason, report.Baseline != null) || report.Cells == null) return false;
        if (report.Baseline == null) return report.Cells.Length == 0;
        int width = ToleranceBatch.Width;
        if (report.Cells.Length != width * width || !ValidTape(report.Baseline.Inputs, identity)) return false;
        bool allComputed = true;
        for (int i = 0; i < report.Cells.Length; i++)
        {
            var cell = report.Cells[i];
            if (cell == null || cell.FirstOffsetMs != i % width * identity.StepMs - identity.SpanMs ||
                cell.SecondOffsetMs != i / width * identity.StepMs - identity.SpanMs ||
                !ValidCell(cell.Status, cell.Message, cell.Computed, cell.Collisions)) return false;
            allComputed &= cell.Computed;
        }
        return report.Complete == allComputed;
    }

    static bool ValidSingle(ToleranceBatchTarget target, ToleranceBatchIdentity identity)
    {
        var report = target.Single;
        if (report == null || report.InputIndex != target.InputIndex || report.SpanMs != identity.SpanMs ||
            report.StepMs != identity.StepMs || !ValidReport(report.Status, report.Message, report.Complete,
                report.StopReason, report.Baseline != null) || report.Probes == null) return false;
        if (report.Baseline == null && report.Complete)
            return report.Probes.Length == 0 && report.Omission == null && report.Attempted == 0;
        if (report.Probes.Length != ToleranceBatch.Width || (report.Baseline != null && !ValidTape(report.Baseline.Inputs, identity)))
            return false;
        bool ValidProbe(InputToleranceProbe probe, int? offset) => probe != null && probe.OffsetMs == offset &&
            probe.Omit == !offset.HasValue && ValidCell(probe.Status, probe.Message, probe.Computed, probe.Collisions);
        if (!ValidProbe(report.Omission, null)) return false;
        for (int i = 0; i < report.Probes.Length; i++)
            if (!ValidProbe(report.Probes[i], i * identity.StepMs - identity.SpanMs)) return false;
        return report.Complete == (report.Omission.Computed && report.Probes.All(probe => probe.Computed)) &&
            report.Attempted == report.Probes.Append(report.Omission).Count(probe => probe.Status is "safe" or "collision" or "rejected");
    }

    internal static ToleranceBatchStatus Read(JsonElement raw, TrackPackage track)
    {
        if (raw.GetProperty("version").GetInt32() != Version) return new("unsupported");
        var stored = raw.Deserialize<StoredBatch>(Json);
        var identity = stored?.Identity ?? throw new JsonException();
        if (!identity.Supported) return new("unsupported");
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
        var indices = stored.CommandTargetIndices;
        if (targets == null || targets.Length == 0 || targets.Length > count || stored.Checkpoints == null ||
            stored.Checkpoints.Length > targets.Length || indices?.Length != count) return false;
        int previous = -1;
        var pairFrom = new int[count];
        Array.Fill(pairFrom, -1);
        var targetAt = new int[count];
        Array.Fill(targetAt, -1);
        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (target == null || target.InputIndex <= previous || target.InputIndex >= count || target.Message == null ||
                target.Single != null || target.Pair != null) return false;
            previous = target.InputIndex;
            targetAt[previous] = i;
            if (target.Kind == "pair")
            {
                if (target.FirstInputIndex != previous || previous + 1 >= count || target.Message.Length != 0) return false;
                pairFrom[previous] = i;
            }
            else if (target.FirstInputIndex != null || target.Kind is not ("single" or "unsupported") ||
                (target.Kind == "single") != (target.Message.Length == 0)) return false;
        }
        for (int i = 0; i < indices.Length; i++)
        {
            int pair = i == 0 ? -1 : pairFrom[i - 1];
            if (pair >= 0 && targetAt[i] >= 0 && pairFrom[i] < 0) return false;
            int expected = pair >= 0 ? pair : targetAt[i];
            if (expected < 0 || indices[i] != expected) return false;
        }
        return true;
    }

    internal static bool IsInvalid(Exception ex) => ex is JsonException or InvalidOperationException or
        KeyNotFoundException or FormatException or ArgumentException or IOException or InvalidDataException;
}
