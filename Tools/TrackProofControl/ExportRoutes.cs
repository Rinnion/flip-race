using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static void ExportRoutes()
    {
        var entries = new List<object>();
        foreach (var mode in ControlModes)
        {
            var track = TracksControl.Challenge(mode);
            entries.Add(ExportOne(track, mode, "safe", track.SafeRoute));
            entries.Add(ExportOne(track, mode, "ideal", track.RiskRoute));
            entries.Add(ExportOne(track, mode, "trap", TracksControl.NegativeRoute(mode)));
        }
        var passages = TracksControl.Unlimited();
        entries.Add(ExportOne(passages, MotionMode.GravityUnlimited, "passages", passages.SafeRoute));
        string path = Path.Combine("Tools", "TrackProofControl", "routes.json");
        File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions
            { WriteIndented = true, IncludeFields = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
        Console.WriteLine("Saved exact distance/time routes to " + path);
    }
    static object ExportOne(Track track, MotionMode mode, string name, double[] actions)
    {
        var route = Route(track, mode, ControlProfile(mode), actions, .01, false);
        var p = route.Game.Players[0];
        double minimum = double.PositiveInfinity, maximum = 0, minAir = double.PositiveInfinity;
        for (int i = 1; i < route.Inputs.Count; i++)
        {
            double gap = route.Inputs[i].Time-route.Inputs[i-1].Time;
            minimum = Math.Min(minimum,gap); maximum = Math.Max(maximum,gap);
            if (route.Inputs[i].Airborne) minAir = Math.Min(minAir,gap);
        }
        return new { track = track.Id, mode = mode.ToString(), profile = ControlProfile(mode).Id, route = name,
            distanceInputs = actions, actualInputs = route.Inputs, actualRewards = route.Rewards,
            finish = p.FinishTime.Value, hits = p.Collisions, positives = p.BoostsEarned + p.FinishBoostsEarned,
            ordinaryBoosts = p.BoostsEarned, finishBoosts = p.FinishBoostsEarned,
            finishBoostElapsed = p.FinishBoostElapsed, finishBoostDistanceGained = p.FinishBoostDistanceGained,
            negatives = p.SlowsEarned,
            emptyEffects = p.ActiveEffect == null && p.PendingEffects.Count == 0,
            firstTap = route.Inputs[0].Time, minimumInteriorGap = minimum, maximumInteriorGap = maximum,
            lastTapToFinish = p.FinishTime.Value-route.Inputs[route.Inputs.Count-1].Time,
            airInputs = route.AirInputs, minimumAirRepeatGap = double.IsPositiveInfinity(minAir) ? (double?)null : minAir };
    }
}
