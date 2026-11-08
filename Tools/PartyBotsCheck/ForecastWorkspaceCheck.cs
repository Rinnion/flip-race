using System.Reflection;
using FlipRace.Core;

static class ForecastWorkspaceCheck
{
    static readonly FieldInfo[] playerFields = typeof(PlayerState).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static void Run(Track track, Action<bool, string> check)
    {
        var origin = RaceSimulation.CreateParty(track, FlightPresets.Chosen, 2, PartyCamera.Lagger, -288.8, 1520);
        var flight = new FlightMeasurement(1, 2, false, 3, 1, "stone");
        var hit = new HitInfo { Id = "ring", Message = "reward", At = 2.25 };
        var effect = new SpeedEffect { RingId = "ring", Remaining = .4, Elapsed = .2, PulseCount = 2, CompletedPulses = 1 };
        var player = origin.Players[0];
        player.LastFlight = flight;
        player.LastSuccess = player.LastInstant = origin.Players[1].LastCoin = hit;
        player.ActiveEffect = effect;
        player.PendingEffects.Enqueue(effect);
        player.PendingEffects.Enqueue(new SpeedEffect { RingId = "slow", IsNegative = true });
        player.CollectedRiskIds = player.SuccessfulRiskIds;
        player.SuccessfulRiskIds.Add("ring");
        player.HitObstacleIds.Add("obstacle");
        origin.HiddenObstacles.Add("stone", new HiddenObstacleState { Phase = HiddenObstaclePhase.Warning,
            RevealedAt = 2, ActivationPostponed = true, TriggerPlayerId = 1 });
        SetScalars(player, 1);
        SetClock(origin, 2, "racing");

        var expected = RaceSnapshot.Clone(origin);
        var workspace = new RaceSnapshot.ForecastWorkspace(origin);
        var trial = workspace.BeginCandidate();
        Compare(trial, expected, check);
        workspace.SaveCheckpoint();
        Dirty(trial, 4);
        Compare(origin, expected, check);
        var restored = workspace.Rollback();
        Compare(restored, expected, check);
        check(!ReferenceEquals(restored.Players[0].ActiveEffect, trial.Players[0].ActiveEffect), "Rollback shared trial effect");
        Dirty(restored, 6);
        Compare(workspace.BeginCandidate(), expected, check);
        Compare(origin, expected, check);

        var withoutHidden = RaceSimulation.CreateParty(new Track(100, Array.Empty<Obstacle>()), FlightPresets.Chosen,
            2, PartyCamera.Lagger, -288.8, 1520);
        withoutHidden.HiddenObstacles = null;
        var plainWorkspace = new RaceSnapshot.ForecastWorkspace(withoutHidden);
        check(plainWorkspace.BeginCandidate().HiddenObstacles == null, "No-hidden forecast changed a null hidden timeline");
        plainWorkspace.SaveCheckpoint();
        check(plainWorkspace.Rollback().HiddenObstacles == null, "No-hidden rollback changed a null hidden timeline");
    }

    static void Dirty(GameState race, int value)
    {
        SetClock(race, value, "finished");
        var player = race.Players[0];
        SetScalars(player, value);
        player.HitObstacleIds.Clear();
        player.FailedRiskIds.Add("discarded");
        player.SuccessfulRiskIds.Clear();
        player.ActiveEffect.Elapsed = 9;
        player.PendingEffects.Dequeue();
        player.LastSuccess.At = 9;
        race.HiddenObstacles["stone"].Phase = HiddenObstaclePhase.Solid;
        race.HiddenObstacles.Add("discarded", new HiddenObstacleState());
    }

    static void SetClock(GameState race, int value, string phase)
    {
        race.Elapsed = value;
        race.Phase = phase;
        race.CountdownRemaining = value / 2d;
        race.Winner = value;
        race.WinnerAt = value + .25;
        race.PartyLastCameraLeft = value * 100;
    }

    static void SetScalars(PlayerState player, int value)
    {
        foreach (var field in playerFields)
        {
            Type type = field.FieldType;
            if (type == typeof(double) || type == typeof(double?)) field.SetValue(player, value + .125);
            else if (type == typeof(int)) field.SetValue(player, value);
            else if (type == typeof(bool)) field.SetValue(player, value % 2 != 0);
            else if (type == typeof(string)) field.SetValue(player, "value-" + value);
            else if (type.IsEnum)
            {
                var options = Enum.GetValues(type);
                field.SetValue(player, options.GetValue(value % options.Length));
            }
        }
    }

    static void Compare(GameState actual, GameState expected, Action<bool, string> check)
    {
        check(actual.Elapsed == expected.Elapsed && actual.Phase == expected.Phase
            && actual.CountdownRemaining == expected.CountdownRemaining && Equals(actual.Winner, expected.Winner)
            && actual.WinnerAt == expected.WinnerAt && actual.PartyLastCameraLeft == expected.PartyLastCameraLeft, "Forecast race clock/state");
        check(actual.HiddenObstacles.Count == 1 && actual.HiddenObstacles.ContainsKey("stone"), "Forecast retained a discarded hidden block");
        CompareRecord(actual.HiddenObstacles["stone"], expected.HiddenObstacles["stone"], check);
        for (int i = 0; i < actual.Players.Length; i++)
        {
            foreach (var field in playerFields)
            {
                object actualValue = field.GetValue(actual.Players[i]), expectedValue = field.GetValue(expected.Players[i]);
                if (actualValue is HashSet<string> set)
                    check(!ReferenceEquals(set, expectedValue) && set.SetEquals((HashSet<string>)expectedValue), "Forecast set " + field.Name);
                else if (actualValue is Queue<SpeedEffect> queue)
                {
                    var other = (Queue<SpeedEffect>)expectedValue;
                    check(!ReferenceEquals(queue, other) && queue.Count == other.Count, "Forecast queue");
                    foreach (var pair in queue.Zip(other)) CompareRecord(pair.First, pair.Second, check);
                }
                else if (actualValue is SpeedEffect || actualValue is HitInfo) CompareRecord(actualValue, expectedValue, check);
                else check(Equals(actualValue, expectedValue), "Forecast scalar or immutable field " + field.Name);
            }
        }
        var player = actual.Players[0];
        check(ReferenceEquals(player.LastSuccess, player.LastInstant) && ReferenceEquals(player.LastSuccess, actual.Players[1].LastCoin)
            && ReferenceEquals(player.ActiveEffect, player.PendingEffects.Peek()) && ReferenceEquals(player.CollectedRiskIds, player.SuccessfulRiskIds), "Forecast lost mutable aliases");
    }

    static void CompareRecord(object actual, object expected, Action<bool, string> check)
    {
        check(actual != null && expected != null && !ReferenceEquals(actual, expected), "Forecast shared a mutable record");
        foreach (var field in actual.GetType().GetFields())
            check(Equals(field.GetValue(actual), field.GetValue(expected)), "Forecast mutable record " + field.Name);
    }
}
