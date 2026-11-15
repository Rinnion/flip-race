using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FlipRace.Core;

namespace TrackWorkshop;

internal sealed record RouteAnalysisSettings(
    [property: JsonRequired] string Profile,
    [property: JsonRequired] double WarningDuration,
    [property: JsonRequired] WorkshopSurfaceRunSettings SurfaceRunSettings)
{
    internal static RouteAnalysisSettings FromRun(RouteSearchRun run) =>
        new(run.Profile, run.WarningDuration, run.SurfaceRunSettings ?? WorkshopSurfaceRunSettings.Legacy);
}

internal sealed class DeveloperDocument
{
    internal const string AnalysisKey = "workshopAnalysis";
    internal const int Version = 2;
    const int LegacyVersion = 1;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    static readonly string[] RunSettingFields = ["profile", "warningDuration", "surfaceRun", "surfaceRunSettings"];

    readonly byte[] source;
    readonly RootProperty? analysis;
    readonly RootProperty? references;
    readonly int insertAt;
    readonly bool hasProperties;
    readonly record struct RootProperty(int Start, int End);
    readonly record struct StoredRun(RouteAnalysisSettings Settings, JsonElement Payload);

    public bool HasAnalysis => analysis.HasValue;
    string Original => prefix + Encoding.UTF8.GetString(source);
    public string GameJson => analysis is { } propertySpan ? Replace(propertySpan, "null") : Original;
    public string WithoutReferences => references is { } propertySpan ? Replace(propertySpan, "[]") : Original;

    DeveloperDocument(byte[] bytes)
    {
        source = bytes;
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 33 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new ArgumentException("Ожидается объект JSON трассы.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            hasProperties = true;
            bool isAnalysis = reader.ValueTextEquals(AnalysisKey);
            bool isReferences = reader.ValueTextEquals("botReferences");
            reader.Read();
            int start = (int)reader.TokenStartIndex;
            reader.Skip();
            var span = new RootProperty(start, (int)reader.BytesConsumed);
            if (isAnalysis)
            {
                if (analysis.HasValue) throw new ArgumentException($"Повтор JSON-поля: {AnalysisKey}.");
                analysis = span;
            }
            if (isReferences) references = span;
        }
        insertAt = (int)reader.TokenStartIndex;
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            throw new ArgumentException("Ожидается один законченный объект JSON трассы.");
    }

    internal static DeveloperDocument Read(string text)
    {
        // Preserve the game loader's support for an optional UTF-8 BOM.
        try
        {
            if (text == null || Utf8.GetByteCount(text) > TrackJson.MaximumBytes)
                throw new ArgumentException("JSON отсутствует или превышает 2 МиБ.");
            int bom = text.StartsWith('\ufeff') ? 1 : 0;
            var document = new DeveloperDocument(Utf8.GetBytes(text[bom..]));
            document.prefix = bom == 0 ? "" : "\ufeff";
            return document;
        }
        catch (Exception ex) when (ex is JsonException or EncoderFallbackException)
        { throw new ArgumentException("Некорректный JSON трассы.", ex); }
    }

    string prefix = "";

    string Replace(RootProperty span, string value) => prefix + Encoding.UTF8.GetString(source.AsSpan(0, span.Start))
        + value + Encoding.UTF8.GetString(source.AsSpan(span.End));

    string SetAnalysis(string json) => analysis is { } field ? Replace(field, json) :
        Replace(new(insertAt, insertAt), (hasProperties ? "," : "") + "\"workshopAnalysis\":" + json);

    JsonElement ReadAnalysis()
    {
        var span = analysis.Value;
        using var document = JsonDocument.Parse(source.AsMemory(span.Start, span.End - span.Start));
        RejectDuplicates(document.RootElement);
        return document.RootElement.Clone();
    }

    static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException("Повтор ключа в анализе трассы.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }

    internal RouteAnalysisStatus AnalysisFor(TrackPackage track)
    {
        if (!HasAnalysis) return new("missing");
        try
        {
            var root = ReadAnalysis();
            if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out int number)) return new("invalid");
            if (number is not (Version or LegacyVersion)) return new("unsupported");
            var fresh = new List<RouteSearchRun>();
            bool stale = false, unsupported = false, invalid = false;
            foreach (var (settings, payload) in ReadRuns(root, number))
            {
                var run = payload.Deserialize<RouteSearchRun>(Json) with
                {
                    Profile = settings.Profile,
                    WarningDuration = settings.WarningDuration,
                    SurfaceRun = settings.SurfaceRunSettings.Enabled,
                    SurfaceRunSettings = settings.SurfaceRunSettings
                };
                if (run.RulesVersion is not (RouteSearch.LegacyRulesVersion or RouteSearch.RulesVersion))
                { unsupported = true; continue; }
                if (!ValidRun(payload, run)) { invalid = true; continue; }
                if (run.RulesVersion == RouteSearch.LegacyRulesVersion)
                    run = run with { SearchParameters = run.SearchParameters with { MinimumInputIntervalMs = RouteSearch.LegacyStepMs } };
                var flight = run.Profile == "Chosen" ? FlightPresets.Chosen : FlightPresets.FastFlip;
                string warningMode = track.WarningMode == TrackWarningMode.Selected ? "selected" : "authored";
                if (run.FlightSettings != RouteFlightSettings.Capture(flight) || run.WarningMode != warningMode ||
                    run.TrackFingerprint != track.ContentHash || !track.SupportsProfile(flight))
                { stale = true; continue; }
                fresh.Add(run);
            }
            if (fresh.Count > 0) return new("ready", new(Version, fresh.ToArray()));
            return new(invalid ? "invalid" : unsupported ? "unsupported" : stale ? "stale" : "missing");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { return new("invalid"); }
    }

    static List<StoredRun> ReadRuns(JsonElement root, int version)
    {
        var stored = new List<StoredRun>();
        if (version == LegacyVersion)
        {
            var runs = root.GetProperty("runs");
            if (runs.GetArrayLength() > 2) throw new JsonException("Некорректный список сохранённых анализов.");
            var profiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var run in runs.EnumerateArray())
            {
                var profile = run.GetProperty("profile").GetString();
                var surfaceRun = run.TryGetProperty("surfaceRunSettings", out var surface)
                    ? surface.Deserialize<WorkshopSurfaceRunSettings>(Json) : null;
                surfaceRun ??= WorkshopSurfaceRunSettings.Legacy;
                if (!profiles.Add(profile) || run.GetProperty("surfaceRun").GetBoolean() != surfaceRun.Enabled)
                    throw new JsonException("Некорректные настройки сохранённого анализа.");
                var settings = new RouteAnalysisSettings(profile, run.GetProperty("warningDuration").GetDouble(), surfaceRun);
                ValidateSettings(settings);
                stored.Add(new(settings, run));
            }
            return stored;
        }

        var groupSettings = new HashSet<RouteAnalysisSettings>();
        foreach (var group in root.GetProperty("groups").EnumerateArray())
        {
            var settings = group.GetProperty("settings").Deserialize<RouteAnalysisSettings>(Json);
            ValidateSettings(settings);
            if (!groupSettings.Add(settings)) throw new JsonException("Повтор настроек группы сохранённого анализа.");
            var runs = group.GetProperty("runs");
            if (runs.GetArrayLength() != 1 || runs[0].ValueKind != JsonValueKind.Object ||
                RunSettingFields.Any(field => runs[0].TryGetProperty(field, out _)))
                throw new JsonException("Некорректный список анализов в группе настроек.");
            stored.Add(new(settings, runs[0]));
        }
        return stored;
    }

    static void ValidateSettings(RouteAnalysisSettings settings)
    {
        if (settings?.Profile is not ("Chosen" or "FastFlip") || settings.SurfaceRunSettings == null)
            throw new JsonException("Некорректные настройки сохранённого анализа.");
        WorkshopService.ValidateWarning(settings.WarningDuration);
        settings.SurfaceRunSettings.ToCore();
    }

    static bool ValidRun(JsonElement sourceRun, RouteSearchRun run)
    {
        // Defaults of value types must never turn missing settings into valid settings.
        string[] fields = ["startX", "beamPruned", "storageLimited", "expandedStates", "elapsedMilliseconds", "foundCount", "verifiedCount", "storedCount"];
        var routes = run.Routes;
        bool legacy = run.RulesVersion == RouteSearch.LegacyRulesVersion;
        int stepMs = legacy ? RouteSearch.LegacyStepMs : RouteSearch.StepMs;
        int intervalMs = legacy ? RouteSearch.LegacyStepMs : RouteSearch.MinimumInputIntervalMs;
        if (fields.Any(field => !sourceRun.TryGetProperty(field, out _)) ||
            run.TrackFingerprint is not { Length: 64 } || !run.TrackFingerprint.All(Uri.IsHexDigit) ||
            run.WarningMode is not ("selected" or "authored") || run.StartX != 0 ||
            run.Axis != "time" || run.TimeOrigin != "race-start" ||
            run.StepMs != stepMs || run.SimulationStepSeconds != WorkshopService.SampleStep ||
            run.MaxRaceSeconds != WorkshopService.MaximumTime ||
            run.StopReason is not ("frontier-exhausted" or "state-limit" or "time-limit" or "race-time-limit" or "verification-time-limit") ||
            run.SearchParameters is not { Algorithm: "beam-v1", MaximumStates: > 0, BeamWidth: > 0,
                PrefixesPerState: > 0, MaximumMilliseconds: > 0, VerificationMilliseconds: > 0 } ||
            run.ExpandedStates < 0 || run.ExpandedStates > run.SearchParameters.MaximumStates || run.ElapsedMilliseconds < 0 || routes == null ||
            run.FoundCount < run.VerifiedCount || run.VerifiedCount < run.StoredCount ||
            run.StoredCount != routes.Length || routes.Length > RouteSearch.MaximumRoutes)
            return false;
        bool hasInterval = sourceRun.GetProperty("searchParameters").TryGetProperty("minimumInputIntervalMs", out _);
        if (hasInterval ? run.SearchParameters.MinimumInputIntervalMs != intervalMs : !legacy)
            return false;

        int maximumTicks = (int)WorkshopService.MaximumTime * 1000 / stepMs;
        int minimumInputIntervalTicks = intervalMs / stepMs;
        double previous = 0;
        var tapes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in sourceRun.GetProperty("routes").EnumerateArray().Zip(routes))
        {
            var route = item.Second;
            if (route == null || new[] { "collisions", "boosts", "slows", "coins", "finishBoosts" }.Any(field => !item.First.TryGetProperty(field, out _)) ||
                !double.IsFinite(route.FinishTime) || route.FinishTime <= 0 || route.FinishTime > WorkshopService.MaximumTime + 1e-8 ||
                route.FinishTime < previous || route.Collisions < 0 || route.Boosts < 0 || route.Slows < 0 ||
                route.Coins is < 0 or > 2 || route.FinishBoosts is < 0 or > 1 || route.InputTicks == null ||
                route.InputTicks.Length > maximumTicks)
                return false;
            previous = route.FinishTime;
            int last = -minimumInputIntervalTicks;
            foreach (var tick in route.InputTicks)
            {
                if (tick < last + minimumInputIntervalTicks || tick >= maximumTicks || tick * stepMs / 1000d >= route.FinishTime)
                    return false;
                last = tick;
            }
            if (!tapes.Add(string.Join(",", route.InputTicks))) return false;
        }
        return true;
    }

    internal (JsonObject Root, JsonObject Target) AnalysisToUpdate(RouteAnalysisSettings settings)
    {
        var groups = new JsonArray();
        var root = new JsonObject { ["version"] = Version, ["groups"] = groups };
        var stored = new List<StoredRun>();
        if (HasAnalysis)
        {
            try
            {
                var prior = ReadAnalysis();
                int version = prior.GetProperty("version").GetInt32();
                if (version is not (Version or LegacyVersion))
                    throw new ArgumentException("Версия сохранённого анализа неизвестна; исходный JSON сохранён.");
                stored = ReadRuns(prior, version);
                root = JsonNode.Parse(prior.GetRawText()).AsObject();
                groups = version == LegacyVersion ? MigrateLegacy(root, stored) : root["groups"].AsArray();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { throw new ArgumentException("Исправьте поле workshopAnalysis в JSON, чтобы записать анализ.", ex); }
        }
        int targetIndex = stored.FindIndex(item => item.Settings == settings);
        if (targetIndex >= 0) return (root, groups[targetIndex]["runs"][0].AsObject());

        var target = new JsonObject();
        groups.Add(NewGroup(settings, target));
        return (root, target);
    }

    static JsonObject NewGroup(RouteAnalysisSettings settings, JsonObject run) => new()
    {
        ["settings"] = JsonSerializer.SerializeToNode(settings, Json),
        ["runs"] = new JsonArray(run)
    };

    static JsonArray MigrateLegacy(JsonObject root, List<StoredRun> stored)
    {
        var groups = new JsonArray();
        foreach (var (settings, payload) in stored)
        {
            var run = JsonNode.Parse(payload.GetRawText()).AsObject();
            var surface = run["surfaceRunSettings"];
            foreach (var field in RunSettingFields) run.Remove(field);
            var group = NewGroup(settings, run);
            if (surface != null) group["settings"]["surfaceRunSettings"] = surface;
            groups.Add(group);
        }
        root.Remove("runs");
        root["version"] = Version;
        root["groups"] = groups;
        return groups;
    }

    internal RouteSearchResponse Save(RouteSearchRun run, TrackPackage track, (JsonObject Root, JsonObject Target) update)
    {
        var (root, target) = update;

        string WithCount(int count)
        {
            var saved = run with { StoredCount = count, StorageLimited = count < run.Routes.Length, Routes = run.Routes[..count] };
            var payload = JsonSerializer.SerializeToNode(saved, Json).AsObject();
            foreach (var field in RunSettingFields) payload.Remove(field);
            foreach (var property in payload)
                target[property.Key] = property.Value?.DeepClone();
            return SetAnalysis(root.ToJsonString(Json));
        }

        string text = WithCount(run.Routes.Length);
        if (Encoding.UTF8.GetByteCount(text) > TrackJson.MaximumBytes)
        {
            text = WithCount(0);
            if (Encoding.UTF8.GetByteCount(text) > TrackJson.MaximumBytes)
                throw new ArgumentException("В документе нет места для анализа при лимите 2 МиБ. Исходный файл сохранён.");
            int low = 0, high = run.Routes.Length;
            while (low + 1 < high)
            {
                int middle = (low + high) / 2;
                string candidate = WithCount(middle);
                if (Encoding.UTF8.GetByteCount(candidate) <= TrackJson.MaximumBytes) { low = middle; text = candidate; }
                else high = middle;
            }
        }
        var status = Read(text).AnalysisFor(track);
        var settings = RouteAnalysisSettings.FromRun(run);
        if (status.Analysis == null || !status.Analysis.Runs.Any(saved => RouteAnalysisSettings.FromRun(saved) == settings))
            throw new InvalidOperationException("The search could not round-trip its own route analysis.");
        return new(status.Analysis, text);
    }
}
