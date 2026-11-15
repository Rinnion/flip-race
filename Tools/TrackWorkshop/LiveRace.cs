using FlipRace.Core;

namespace TrackWorkshop;

internal sealed record LiveStart(string Type, string Json, string Profile, double WarningDuration,
    double StartX = 0, string Axis = "distance", double[] Inputs = null, WorkshopSurfaceRunSettings SurfaceRunSettings = null);
internal sealed record LiveStep(string Type, int Seq, int Steps, int[] Inputs);
internal sealed record LiveState(int Seq, string Phase, double CountdownRemaining, double CameraLeft,
    Sample Sample, InputResult[] Inputs, ReplayEvent[] Events, HiddenState[] HiddenStates,
    Summary Summary, bool StaleReferences, string ReferenceError, double RequestedStartX, InputResult[] PrefixInputs,
    WorkshopSurfaceRunSettings SurfaceRunSettings)
{
    public string Type => "state";
}

/// <summary>One production solo attempt, with no time advancing between commands.</summary>
internal sealed class LiveRace
{
    public const int MaxSteps = 30;
    readonly GameState game;
    readonly TrialObservation observation;
    readonly bool staleReferences;
    readonly string referenceError;
    readonly double requestedStartX;
    readonly WorkshopSurfaceRunSettings surfaceRunSettings;
    readonly InputResult[] prefixInputs = Array.Empty<InputResult>();
    readonly Dictionary<string, HiddenState> sentHidden = new(StringComparer.Ordinal);
    Sample sample;
    int sequence, accepted, ignored, unreached;

    public LiveRace(LiveStart start, CancellationToken cancellationToken = default)
    {
        if (start?.Type != "start") throw new ArgumentException("Сначала отправьте команду start.");
        var preview = WorkshopService.Read(start.Json);
        staleReferences = preview.Stale;
        referenceError = preview.ReferenceError;
        var flight = WorkshopService.GetFlight(preview.Package, start.Profile);
        WorkshopService.ValidateWarning(start.WarningDuration);
        var track = preview.Package.Track;
        if (!double.IsFinite(start.StartX) || start.StartX < 0 || start.StartX >= track.Length)
            throw new ArgumentException("Точка старта X должна быть внутри трассы до финиша.");
        requestedStartX = start.StartX;
        surfaceRunSettings = start.SurfaceRunSettings ?? WorkshopSurfaceRunSettings.Legacy;
        game = WorkshopService.StartSolo(track, flight, start.WarningDuration, surfaceRunSettings);
        observation = new TrialObservation(game);
        if (requestedStartX == 0) sample = observation.Observe();
        else prefixInputs = PlayPrefix(start.Axis, start.Inputs ?? Array.Empty<double>(), cancellationToken);
    }

    public bool IsTerminal => game.Phase == "finished" || game.Elapsed >= WorkshopService.MaximumTime - 1e-10;

    InputResult[] PlayPrefix(string axis, double[] authorInputs, CancellationToken cancellationToken)
    {
        WorkshopService.ValidateAxis(axis);
        WorkshopService.ValidateInputs(authorInputs);
        RaceSimulation.Advance(game, RaceConfig.CountdownDuration);
        sample = observation.Observe();
        var prefix = new List<InputResult>();
        int next = 0;
        while (sample.X < requestedStartX && WorkshopService.IsRacing(game))
        {
            var input = WorkshopService.ReplayStep(game, axis, authorInputs, ref next, requestedStartX, cancellationToken);
            if (input != null)
            {
                if (prefix.Count > 0 && input.Time <= prefix[^1].Time)
                    throw new ArgumentException("До точки старта есть нажатия в один момент. Разнесите их, чтобы сохранить точное прохождение по времени.");
                prefix.Add(input);
                if (input.Status == "accepted") accepted++;
                else ignored++;
            }
            sample = observation.Observe();
        }
        if (!WorkshopService.IsRacing(game) || sample.X < requestedStartX)
            throw new ArgumentException("Точка старта недоступна до финиша или предела 120 с.");
        return prefix.ToArray();
    }

    public LiveState Step(LiveStep command, CancellationToken cancellationToken)
    {
        if (IsTerminal) throw new ArgumentException("Забег завершён. Запустите новый забег.");
        if (command?.Type != "step" || command.Seq != sequence + 1 || command.Steps is < 1 or > MaxSteps)
            throw new ArgumentException("Нужна следующая команда step с 1–30 шагами.");
        int[] presses = command.Inputs ?? Array.Empty<int>();
        int previous = -1;
        foreach (int tick in presses)
        {
            if (tick <= previous || tick >= command.Steps)
                throw new ArgumentException("Номера шагов нажатий должны возрастать и попадать в диапазон команды.");
            previous = tick;
        }

        sequence = command.Seq;
        var inputs = new InputResult[presses.Length];
        int nextPress = 0, tickIndex = 0;
        while (tickIndex < command.Steps && !IsTerminal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nextPress < presses.Length && presses[nextPress] == tickIndex)
            {
                var input = WorkshopService.ApplyPress(game, accepted + ignored + unreached, game.Elapsed);
                inputs[nextPress++] = input;
                if (input.Status == "accepted") accepted++;
                else ignored++;
            }
            // Clamp the final countdown tick to the Core's remaining time so
            // the first racing state starts on the same time-zero edge as replay.
            double remaining = game.Phase == "countdown" ? game.CountdownRemaining : WorkshopService.MaximumTime - game.Elapsed;
            RaceSimulation.Advance(game, Math.Min(WorkshopService.SampleStep, remaining));
            if (game.Phase != "countdown") sample = observation.Observe();
            tickIndex++;
        }
        while (nextPress < presses.Length)
        {
            double time = game.Elapsed + (presses[nextPress] - tickIndex) * WorkshopService.SampleStep;
            inputs[nextPress++] = WorkshopService.UnreachedPress(accepted + ignored + unreached, time);
            unreached++;
        }
        return State(inputs);
    }

    public LiveState State(InputResult[] inputs = null)
    {
        var changes = new List<HiddenState>();
        foreach (var state in observation.HiddenStates())
        {
            if (sentHidden.TryGetValue(state.Id, out var previous) && state == previous) continue;
            sentHidden[state.Id] = state;
            changes.Add(state);
        }
        var events = observation.Events.ToArray();
        observation.Events.Clear();
        var summary = IsTerminal ? WorkshopService.Summarize(game, accepted, ignored, unreached) : null;
        string phase = summary?.Status == "TimedOut" ? "timedOut" : game.Phase;
        return new(sequence, phase, game.CountdownRemaining, game.PartyCameraLeft, sample,
            inputs ?? Array.Empty<InputResult>(), events, changes.ToArray(), summary, staleReferences, referenceError,
            requestedStartX, sequence == 0 ? prefixInputs : Array.Empty<InputResult>(), surfaceRunSettings);
    }
}
