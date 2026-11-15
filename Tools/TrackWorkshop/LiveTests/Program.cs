using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;
using TrackWorkshop;

int assertions = 0;
void Need(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}
void Near(double a, double b, string message) => Need(Math.Abs(a - b) < 1e-7, $"{message}: {a} vs {b}");
void Reject<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { Need(true, message); return; }
    Need(false, message);
}

var service = new WorkshopService(Path.Combine(AppContext.BaseDirectory, "Catalog"));
string hidden = service.CatalogJson("hidden-intercepts");
Need(TrackJson.TryRead(hidden, out var package, out _), "strict catalogue fixture");
var results = new List<object>();
foreach (var reference in package.References)
{
    var seeded = service.Replay(new(hidden, reference.ProfileId, .8, "distance", reference.Inputs));
    var scheduled = seeded.Inputs.Select(i => (int)Math.Round(i.Time.Value / WorkshopService.SampleStep)).ToHashSet();
    Need(scheduled.Count == reference.Inputs.Length, "reference inputs fit distinct live ticks");
    var a = Run(hidden, reference.ProfileId, scheduled, new[] { 30, 7, 1, 19 });
    var b = Run(hidden, reference.ProfileId, scheduled, new[] { 4, 5, 2 });
    Need(a.State.Sample == b.State.Sample && a.Inputs.SequenceEqual(b.Inputs) && a.Events.SequenceEqual(b.Events), "batch independent state, presses and events");
    Need(JsonSerializer.Serialize(a.State.Summary) == JsonSerializer.Serialize(b.State.Summary), "batch independent summary");
    Need(a.Hidden.OrderBy(p => p.Key).SequenceEqual(b.Hidden.OrderBy(p => p.Key)), "batch independent merged hidden state");
    Need(a.State.Phase == "finished" && a.State.Summary.CleanFinish && a.State.Summary.FinishBoosts == 1, "clean finish dash on catalogue");
    Need(a.State.Summary.SuccessfulIds.Length > 1, "ordinary reward plus finish observed");
    Need(a.Events.Any(e => e.Type == "hidden" && e.Phase == "Warning") && a.Events.Any(e => e.Type == "hidden" && e.Phase == "Solid"), "hidden transitions emitted");

    var replay = service.Replay(new(hidden, reference.ProfileId, .8, "time", a.Inputs.Select(i => i.Value).ToArray()));
    Near(a.State.Summary.FinishTime.Value, replay.Summary.FinishTime.Value, "live/offline finish time");
    var rounded = a.State.Summary with { FinishTime = replay.Summary.FinishTime, Elapsed = replay.Summary.Elapsed };
    Need(JsonSerializer.Serialize(rounded) == JsonSerializer.Serialize(replay.Summary), "live/offline summary outcomes");
    Need(a.Inputs.Count == replay.Inputs.Count && a.Events.Count == replay.Events.Count, "live/offline observation counts");
    foreach (var (liveInput, replayInput) in a.Inputs.Zip(replay.Inputs))
    {
        Need(liveInput.Status == replayInput.Status, "live/offline press result");
        Near(liveInput.X.Value, replayInput.X.Value, "live/offline input x");
        Near(liveInput.Y.Value, replayInput.Y.Value, "live/offline input y");
    }
    foreach (var (liveEvent, replayEvent) in a.Events.Zip(replay.Events))
    {
        Need((liveEvent.Type, liveEvent.Id, liveEvent.Phase) == (replayEvent.Type, replayEvent.Id, replayEvent.Phase), "live/offline event kind, ID and phase");
        Near(liveEvent.Time, replayEvent.Time, "live/offline event time");
        Near(liveEvent.X, replayEvent.X, "live/offline event x");
        Near(liveEvent.Y, replayEvent.Y, "live/offline event y");
    }
    foreach (var state in replay.HiddenStates)
    {
        var current = a.Hidden[state.Id];
        Need(current.Phase == state.Phase && current.ActivationPostponed == state.ActivationPostponed, "live/offline final hidden state");
        Near(current.RevealedAt.Value, state.RevealedAt.Value, "live/offline reveal time");
        Near(current.SolidAt.Value, state.SolidAt.Value, "live/offline solid time");
    }
    var markers = new[]
    {
        CheckMarker(hidden, reference.ProfileId, "distance", reference.Inputs, seeded),
        CheckMarker(hidden, reference.ProfileId, "time", a.Inputs.Select(i => i.Value).ToArray(), replay)
    };
    results.Add(new { reference.ProfileId, a.State.Summary, hiddenEvents = a.Events.Count(e => e.Type == "hidden"), markers });
}
Need(results.Count == 2, "both supported profiles");

var acceleration = new WorkshopSurfaceRunSettings(true, 3, "Exponential", 40, 50, false);
var acceleratedInputs = new[] { 300d, 710d };
var accelerated = service.Replay(new(hidden, "FastFlip", .8, "distance", acceleratedInputs, acceleration));
results.Add(CheckMarker(hidden, "FastFlip", "distance", acceleratedInputs, accelerated, acceleration));
var acceleratedTimes = accelerated.Inputs.Select(input => input.Time.Value).ToArray();
var acceleratedByTime = service.Replay(new(hidden, "FastFlip", .8, "time", acceleratedTimes, acceleration));
results.Add(CheckMarker(hidden, "FastFlip", "time", acceleratedTimes, acceleratedByTime, acceleration));

string plain = TrackJson.Write(new TrackPackage(new Track(1_000_000, Array.Empty<Obstacle>()) { Id = "live-long", Name = "Live long" }));
LiveRace NewPlain() => new(new("start", plain, "FastFlip", .8));
var active = NewPlain();
var independent = NewPlain();
Need(active.State() is { Phase: "countdown", CountdownRemaining: 3, Seq: 0, CameraLeft: -288.8, Summary: null }, "ordinary Core countdown and lagger camera");
LiveRace Marker(double x, string axis = "time", double[] inputs = null) =>
    new(new("start", plain, "FastFlip", .8, x, axis, inputs));
Need(Marker(0, "unused", new[] { double.NaN }).State() is { Phase: "countdown", RequestedStartX: 0, PrefixInputs.Length: 0 },
    "zero start ignores unused author settings and preserves normal countdown");

var neutralMarker = Marker(3).State();
var futureTimeMarker = Marker(3, inputs: new[] { .015 }).State();
var futureDistanceMarker = Marker(3, "distance", new[] { 3d }).State();
Need(neutralMarker.Sample == futureTimeMarker.Sample && neutralMarker.Sample == futureDistanceMarker.Sample &&
    futureTimeMarker.PrefixInputs.Length == 0 && futureDistanceMarker.PrefixInputs.Length == 0,
    "a future off-grid time edge and a distance press at the marker cannot split the normal step");
var timeBeforeMarker = Marker(3, inputs: new[] { .013 }).State();
Need(timeBeforeMarker.PrefixInputs is [{ Status: "accepted", Time: .013 }] && timeBeforeMarker.Sample.Flipping,
    "an off-grid time press before the marker is executed and splits the grid");
Need(Marker(3, "distance", new[] { 2d }).State().PrefixInputs is [{ Status: "accepted", Value: 2 }],
    "a distance press before the marker executes at its swept boundary");
foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity, 1_000_000 })
    Reject<ArgumentException>(() => Marker(invalid), "invalid selected coordinate");
Reject<ArgumentException>(() => Marker(3, "other"), "invalid author axis");
Reject<ArgumentException>(() => Marker(3, inputs: new double[4097]), "author input cap");
Reject<ArgumentException>(() => Marker(3, inputs: new[] { 0d, 1e-8 }), "ambiguous duplicate applied prefix time rejected");
Reject<ArgumentException>(() => Marker(3, "distance", new[] { 0d, 1e-8 }), "ambiguous distance prefix time rejected");
Reject<ArgumentException>(() => Marker(900_000), "cannot reach requested point within global time limit");
Reject<OperationCanceledException>(() => new LiveRace(new("start", plain, "FastFlip", .8, 3), new CancellationToken(true)),
    "disconnect cancels prefix");

string collisionTrack = TrackJson.Write(new TrackPackage(new Track(200, new[] {
    new Obstacle { Id = "floor-hit", Kind = "floor", X = 75, Width = 25, Height = 55 }
}) { Id = "live-hit", Name = "Live hit" }));
var collided = service.Replay(new(collisionTrack, "Chosen", .8, "time", Array.Empty<double>()));
double hitX = collided.Events.Single(e => e.Type == "collision").X;
var recovering = new LiveRace(new("start", collisionTrack, "Chosen", .8, hitX));
Need(recovering.State() is { Sample.Collisions: 1, Sample.Recovering: > 0 } && recovering.State().Sample.X == hitX,
    "marker can carry the original collision and in-progress recovery");
Need(recovering.Step(new("step", 1, 1, new[] { 0 }), default).Inputs is [{ Status: "ignored" }],
    "first live press obeys inherited recovery");
string tiny = TrackJson.Write(new TrackPackage(new Track(2, Array.Empty<Obstacle>()) { Id = "live-last", Name = "Live last" }));
Reject<ArgumentException>(() => new LiveRace(new("start", tiny, "Chosen", .8, 1.99)),
    "first reaching sample cannot be terminal");

foreach (var bad in new LiveStep[] { new("start", 1, 1, null), new("step", 2, 1, null), new("step", 1, 0, null), new("step", 1, 31, null),
    new("step", 1, 1, new[] { -1 }), new("step", 1, 1, new[] { 1 }), new("step", 1, 2, new[] { 0, 0 }), new("step", 1, 2, new[] { 1, 0 }) })
    Reject<ArgumentException>(() => active.Step(bad, default), "invalid step rejected before mutation");
var starting = active.Step(new("step", 1, 30, new[] { 0, 1 }), default);
Need(starting.CountdownRemaining < 3 && starting.Sample.Time == 0 && starting.Sample.X == 0, "countdown advances without race motion");
Need(starting.Inputs.Length == 2 && starting.Inputs.All(i => i.Status == "ignored" && i.Reason.Contains("отсчёт")), "countdown presses explicitly ignored");
Need(independent.State() is { Phase: "countdown", CountdownRemaining: 3, Seq: 0 }, "other socket and unsent time do not advance");
for (int seq = 2; seq <= 12; seq++) starting = active.Step(new("step", seq, 30, null), default);
Need(starting is { Phase: "racing", CountdownRemaining: 0 } && starting.Sample.Time == 0 && starting.Sample.X == 0, "racing starts at exactly zero");
var pressed = active.Step(new("step", 13, 3, new[] { 0, 1, 2 }), default);
Need(pressed.Inputs.Select(i => i.Status).SequenceEqual(new[] { "accepted", "accepted", "ignored" }), "three rapid presses reported individually");
Near(pressed.CameraLeft, pressed.Sample.X - 288.8, "camera follows authoritative sample");
Need(pressed.Inputs.Select(i => i.Index).SequenceEqual(new[] { 2, 3, 4 }), "monotonic input identities across countdown");

var timeout = Run(plain, "Chosen", new HashSet<int>(), new[] { 30 });
Need(timeout.State is { Phase: "timedOut", Summary.Status: "TimedOut", Summary.FinishTime: null }, "120-second limit is terminal");
Near(timeout.State.Sample.Time, 120, "countdown excluded from timeout");
Reject<OperationCanceledException>(() => independent.Step(new("step", 1, 1, null), new CancellationToken(true)), "disconnect stops tick loop");
string chosenOnly = TrackJson.Write(new TrackPackage(new Track(200, Array.Empty<Obstacle>())
    { Id = "live-chosen-only", Name = "Live chosen only" }, TrackProfilePolicy.ChosenOnly));
Reject<ArgumentException>(() => new LiveRace(new("start", chosenOnly, "FastFlip", .8)), "chosen-only map policy");
Reject<ArgumentException>(() => new LiveRace(new("start", hidden, "Chosen", .7)), "warning choices match replay");
Reject<ArgumentException>(() => new LiveRace(new("start", new string('x', TrackJson.MaximumBytes + 1), "Chosen", .8)), "source byte limit");
var stale = JsonNode.Parse(hidden);
stale["track"]["obstacles"][0]["x"] = 301;
Need(new LiveRace(new("start", stale.ToJsonString(), "Chosen", .8)).State() is { StaleReferences: true, ReferenceError: not null }, "stale references explicit");

var clock = new TestClock();
var rate = new LiveRate(clock);
for (int i = 0; i < 12; i++) Need(rate.Consume(1), "allowed initial message burst");
Need(!rate.Consume(1), "message rate limit");
clock.Elapsed = TimeSpan.FromSeconds(1);
for (int i = 0; i < 4; i++) Need(rate.Consume(30), "allowed initial tick burst");
Need(!rate.Consume(1), "tick rate limit");
clock.Elapsed += TimeSpan.FromMilliseconds(34);
Need(rate.Consume(4), "normal cadence supported after burst");

if (args.Length > 0) await SocketSmoke.RunAsync(new Uri(args[0]), hidden, Need);
Console.WriteLine(JsonSerializer.Serialize(new { passed = true, assertions, socket = args.Length > 0, results }, new JsonSerializerOptions { WriteIndented = true }));

object CheckMarker(string json, string profile, string axis, double[] authorInputs, ReplayResult authored,
    WorkshopSurfaceRunSettings settings = null)
{
    const double marker = 4403.125;
    var race = new LiveRace(new("start", json, profile, .8, marker, axis, authorInputs, settings));
    var initial = race.State();
    var restarted = new LiveRace(new("start", json, profile, .8, marker, axis, authorInputs, settings)).State();
    double maximumSpeed = RaceConfig.FinishBoostSpeed + (settings?.Enabled == true ? RaceConfig.Speed * settings.CapPercent / 100d : 0);
    Need(initial is { Seq: 0, Phase: "racing", CountdownRemaining: 0, RequestedStartX: marker, Inputs.Length: 0 } &&
        initial.Sample.X >= marker && initial.Sample.X < marker + maximumSpeed * WorkshopService.SampleStep,
        "marker reaches an ordinary sample and retains the global racing clock");
    Need(initial.SurfaceRunSettings == authored.SurfaceRunSettings && restarted.Sample == initial.Sample &&
        restarted.SurfaceRunSettings == initial.SurfaceRunSettings, "prefix and restart retain exact running configuration and state");
    var fromReplay = authored.Samples.First(s => s.X >= marker);
    Near(initial.Sample.X, fromReplay.X, "prefix X equals offline replay");
    Near(initial.Sample.Speed, fromReplay.Speed, "prefix effective speed equals offline replay");
    Need(initial.PrefixInputs.SequenceEqual(authored.Inputs.Where(i => i.X.HasValue && i.X.Value < marker)),
        "only author presses before the marker are applied and recorded");
    if (settings == null) Need(initial.Events.Any(e => e.Type == "reward") &&
        initial.HiddenStates.Any(h => h.Phase == "Solid") && initial.HiddenStates.Any(h => h.Phase == "Warning"),
        "initial state contains earlier rewards and current warning and solid states");

    var inputs = initial.PrefixInputs.ToList();
    var state = initial;
    for (int seq = 1; !race.IsTerminal; seq++)
    {
        state = race.Step(new("step", seq, 30, seq <= 3 ? new[] { 2, 21 } : null), default);
        Need(state.PrefixInputs.Length == 0 && state.RequestedStartX == marker, "prefix emitted once; marker retained");
        inputs.AddRange(state.Inputs);
    }
    Need(inputs.Count == initial.PrefixInputs.Length + 6 && inputs.Select(i => i.Index).SequenceEqual(Enumerable.Range(0, inputs.Count)),
        "live press indices continue the prefix");
    var timeReplay = service.Replay(new(json, profile, .8, "time", inputs.Select(i => i.Time.Value).ToArray(), settings));
    Near(state.Summary.FinishTime.Value, timeReplay.Summary.FinishTime.Value, "prefix and suffix full time replay finish");
    var rounded = state.Summary with { FinishTime = timeReplay.Summary.FinishTime, Elapsed = timeReplay.Summary.Elapsed };
    Need(JsonSerializer.Serialize(rounded) == JsonSerializer.Serialize(timeReplay.Summary), "prefix and suffix full time replay outcomes");
    Need(inputs.Select(i => i.Status).SequenceEqual(timeReplay.Inputs.Select(i => i.Status)), "prefix and suffix press outcomes replay");
    return new { axis, requestedX = marker, actualX = initial.Sample.X, prefixPresses = initial.PrefixInputs.Length, livePresses = 6,
        initial.SurfaceRunSettings, initial.Sample.Speed };
}

(LiveState State, List<InputResult> Inputs, List<ReplayEvent> Events, Dictionary<string, HiddenState> Hidden) Run(
    string json, string profile, HashSet<int> schedule, int[] batches)
{
    var race = new LiveRace(new("start", json, profile, .8));
    var state = race.State();
    var inputs = new List<InputResult>();
    var events = new List<ReplayEvent>(state.Events);
    var states = state.HiddenStates.ToDictionary(h => h.Id);
    int seq = 0;
    for (int i = 0; i < 12; i++) state = race.Step(new("step", ++seq, 30, null), default);
    Need(state is { Phase: "racing", CountdownRemaining: 0 } && state.Sample.Time == 0, "countdown alignment");
    int elapsedTicks = 0, batch = 0;
    while (!race.IsTerminal && elapsedTicks < 14_401)
    {
        int ticks = batches[batch++ % batches.Length];
        int[] offsets = Enumerable.Range(0, ticks).Where(t => schedule.Contains(elapsedTicks + t)).ToArray();
        state = race.Step(new("step", ++seq, ticks, offsets), default);
        elapsedTicks += ticks;
        inputs.AddRange(state.Inputs);
        events.AddRange(state.Events);
        foreach (var change in state.HiddenStates) states[change.Id] = change;
    }
    Need(race.IsTerminal && state.Summary != null, "session reached terminal");
    Reject<ArgumentException>(() => race.Step(new("step", seq + 1, 1, new[] { 0 }), default), "terminal press cannot reset Core");
    return (state, inputs, events, states);
}

sealed class TestClock : TimeProvider
{
    public TimeSpan Elapsed { get; set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Elapsed.Ticks;
}
