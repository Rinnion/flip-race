using System;
using System.IO;
using System.Linq;
using FlipRace.Core;

static partial class Program
{
    static double ScrollTravel(double speed, SurfaceRunSettings settings, double time)
    {
        double rate = settings.Rate / 2, cap = settings.Cap;
        if (settings.Curve == SurfaceRunCurve.Exponential)
            return speed * time + RaceConfig.Speed * cap * (time - (1 - Math.Exp(-rate * time / cap)) * cap / rate);
        double ramp = Math.Min(time, cap / rate);
        return speed * time + RaceConfig.Speed * (.5 * rate * ramp * ramp + cap * (time - ramp));
    }

    static double ScrollSpeed(double speed, SurfaceRunSettings settings, double time)
    {
        double halfRate = settings.Rate / 2;
        double reserve = settings.Curve == SurfaceRunCurve.Linear ? Math.Min(settings.Cap, halfRate * time)
            : settings.Cap * (1 - Math.Exp(-halfRate * time / settings.Cap));
        return speed + RaceConfig.Speed * reserve;
    }

    static double GroundSpeed(SurfaceRunSettings settings, double reserve, double time)
    { return RaceConfig.Speed * (1 + GroundReserve(settings, reserve, time)); }

    static double FirstClosingRoot(Func<double, double> gap, double lo, double hi)
    {
        Check(gap(lo) >= 0 && gap(hi) <= 0, "Fixture brackets its first root");
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) / 2;
            if (gap(mid) > 0) lo = mid;
            else hi = mid;
        }
        return hi;
    }

    static GameState ScrollRace(double length, double left, SurfaceRunSettings settings, bool enabled = true,
        PartyCamera camera = PartyCamera.Constant, int count = 1)
    {
        var track = Empty(length);
        var game = count == 1 ? RaceSimulation.CreateSolo(track, FlightPresets.Chosen, camera, left, 1520)
            : RaceSimulation.CreateParty(track, FlightPresets.Chosen, count, camera, left, 1520);
        RaceSimulation.ConfigureSurfaceRun(game, enabled, settings);
        return game;
    }

    static void AcceleratingScrollClock()
    {
        foreach (SurfaceRunCurve curve in Enum.GetValues<SurfaceRunCurve>())
        {
            var settings = new SurfaceRunSettings(4, curve, 30, 0, false);
            var game = ScrollRace(6120, -788.8, settings, count: 2);
            double start = game.PartyCameraLeft;
            Check(RaceRunner.Press(game, 0) && RaceRunner.Press(game, 1), "Party readiness");
            RaceRunner.Advance(game, 1.7);
            RaceRunner.Advance(game, .3, paused: true);
            Near(game.PartyCameraLeft, start, "Countdown and pause leave camera at start");
            RaceRunner.Advance(game, 2.3);
            Near(game.Elapsed, 1, "Countdown remainder advances camera on active time");
            Near(game.PartyCameraLeft, start + ScrollTravel(170, settings, 1), "Imported length changes only the base speed");
            var snapshot = RaceSnapshot.Clone(game);
            var forecast = RaceSnapshot.ForBotForecast(game, 1);
            Check(forecast.PartyCamera == PartyCamera.Lagger, "Personal forecast follows its existing camera rules");
            Near(snapshot.PartyCameraLeft, game.PartyCameraLeft, "Snapshot has the real camera clock");
            RaceRunner.Press(snapshot, 0);
            snapshot.Players[1].Recovering = .5;
            RaceRunner.Advance(snapshot, 18);
            Near(snapshot.PartyCameraLeft, start + ScrollTravel(170, settings, 19), "Camera ignores flight and recovery");
            Near(game.Elapsed, 1, "Snapshot cannot change the live clock");
            RaceRunner.Advance(game, 60);
            Check(game.Phase == "finished" && game.Players[0].Status == PlayerStatus.Finished, "Runner outside right edge can finish");
            double terminalCamera = game.PartyCameraLeft;
            RaceRunner.Advance(game, 3);
            Near(game.PartyCameraLeft, terminalCamera, "Finished camera freezes at the last terminal time");
            Check(RaceRunner.Press(game, 0) && RaceRunner.Press(game, 1), "Retry readiness");
            Near(game.PartyCameraLeft, start, "Retry resets camera");
            RaceRunner.Advance(game, 4);
            Near(game.PartyCameraLeft, start + ScrollTravel(170, settings, 1), "Retry restarts the same camera ramp");
            var off = ScrollRace(6120, start, settings, enabled: false);
            var lag = ScrollRace(6120, start, settings, camera: PartyCamera.Lagger);
            RaceRunner.Press(off, 0); RaceRunner.Advance(off, 7);
            RaceRunner.Press(lag, 0); RaceRunner.Advance(lag, 7);
            Near(off.PartyCameraLeft, start + 170 * 4, "OFF has constant speed");
            Near(lag.PartyCameraLeft, start + lag.Players[0].Distance, "Lagger still tracks its runner");
            var trial = RaceSimulation.CreateTrial(Empty(6120), FlightPresets.Chosen);
            RaceSimulation.ConfigureSurfaceRun(trial, true, settings);
            RaceRunner.Press(trial, 0); RaceRunner.Press(trial, 1); RaceRunner.Advance(trial, 5);
            Near(trial.PartyCameraLeft, 0, "Ordinary nonshared race has no scroll ramp");
        }
    }

    static void AcceleratingScrollContacts()
    {
        var linear = new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 20);
        var exponential = new SurfaceRunSettings(5, SurfaceRunCurve.Exponential, 40);
        foreach (double frame in Frames)
        {
            // Gap and speed recover after the first crossing. A large
            // frame checking only its endpoints would miss it.
            var settings = new SurfaceRunSettings(5, SurfaceRunCurve.Linear, 40);
            var transient = ScrollRace(243 * 36, 14 - 47, settings);
            RaceRunner.Press(transient, 0); RaceRunner.Advance(transient, 3);
            double firstRoot = FirstClosingRoot(t => 47 + GroundDistance(settings, 0, t) - ScrollTravel(243, settings, t), 0, 4.18);
            Check(47 + GroundDistance(settings, 0, 8) - ScrollTravel(243, settings, 8) > 0, "Transient gap reopens before frame endpoint");
            Run(transient, 8, frame);
            Check(transient.Players[0].Status == PlayerStatus.Eliminated, "Linear interior crossing eliminates");
            Near(transient.Elapsed, firstRoot, "Linear interior first root");

            double tangentTime = 23 / 5.5;
            double tangentGap = 23 * tangentTime / 2;
            var tangent = ScrollRace(243 * 36, 14 - tangentGap, settings);
            RaceRunner.Press(tangent, 0); RaceRunner.Advance(tangent, 3);
            Run(tangent, 8, frame);
            Check(tangent.Players[0].Status == PlayerStatus.Eliminated, "Linear minimum contact eliminates");
            Near(tangent.Elapsed, tangentTime, "Linear tangent timestamp", 2e-5);

            // Player stands at cap. At the camera's cap the speeds differ,
            // and the root falls in the second segment of the same frame.
            const double afterCap = 9;
            double gap = ScrollTravel(270, linear, afterCap) - GroundDistance(linear, linear.Cap, afterCap);
            var capped = ScrollRace(9720, 14 - gap, linear);
            RaceRunner.Press(capped, 0); RaceRunner.Advance(capped, 3);
            capped.Players[0].SurfaceRunReserve = linear.Cap;
            Run(capped, 14, frame);
            Check(capped.Players[0].Status == PlayerStatus.Eliminated, "Post-cap crossing eliminates");
            Near(capped.Elapsed, afterCap, "Post-cap exact root");

            // Runner has just hit a rock. The camera keeps accelerating
            // across its cap while this runner stays stationary.
            var recovering = ScrollRace(7920, -800, linear);
            RaceRunner.Press(recovering, 0); RaceRunner.Advance(recovering, 10.9);
            recovering.Players[0].Distance = recovering.PartyCameraLeft - RaceConfig.PlayerRadius
                + ScrollTravel(220, linear, 8.15) - ScrollTravel(220, linear, 7.9);
            recovering.Players[0].Recovering = .5;
            Run(recovering, 1, frame);
            Check(recovering.Players[0].Status == PlayerStatus.Eliminated, "Recovery cannot defer camera elimination");
            Near(recovering.Elapsed, 8.15, "Camera crosses a stopped runner after cap");

            const double clock = 1.7, finish = 2, length = 9720;
            var tie = ScrollRace(length, length + RaceConfig.PlayerRadius - ScrollTravel(length / 36, exponential, finish), exponential);
            RaceRunner.Press(tie, 0);
            RaceRunner.Advance(tie, 3);
            tie.Elapsed = clock;
            tie.Players[0].Distance = length - GroundDistance(exponential, 0, finish - clock);
            Check(tie.PartyCameraLeft < tie.Players[0].Distance + RaceConfig.PlayerRadius, "Runner starts inside the left boundary");
            Run(tie, 1, frame);
            Check(tie.Players[0].Status == PlayerStatus.Finished, "Finish wins simultaneous boundary contact");
            Near(tie.Elapsed, finish, "Boundary tie timestamp");
        }
    }

    static void AcceleratingScrollBots()
    {
        const string path = "Assets/StreamingAssets/Tracks";
        var payloads = Directory.GetFiles(path, "*.track.json").ToDictionary(Path.GetFileName, File.ReadAllText);
        Check(TrackCatalog.TryInstallBuiltIns(File.ReadAllText(Path.Combine(path, "index.json")), payloads, out string error), error);
        Check(TrackCatalog.TryGet(TracksFull.LongId, out var entry), "Built-in track");
        var settings = SurfaceRunSettings.Default;
        var roles = new BotDifficulty?[] { BotDifficulty.Novice, BotDifficulty.Amateur, BotDifficulty.Strong, BotDifficulty.Master };
        var config = new PrestartBotConfig(entry.Package, FlightPresets.Chosen, roles, PartyCamera.Constant, true, settings, .8);
        var oldRules = new PrestartBotConfig(entry.Package, FlightPresets.Chosen, roles, PartyCamera.Constant, false, settings, .8);
        var lagger = new PrestartBotConfig(entry.Package, FlightPresets.Chosen, roles, PartyCamera.Lagger, true, settings, .8);
        Check(config.CacheKey.StartsWith("prestart-v1-accelerating-scroll/", StringComparison.Ordinal)
            && oldRules.CacheKey.StartsWith("prestart-v1/", StringComparison.Ordinal) && lagger.CacheKey.StartsWith("prestart-v1/", StringComparison.Ordinal), "Only accelerated Constant invalidates cache namespace");
        var cold = PrepareScrollBots(config, 12101, null);
        string cache = cold.ExportCache();
        var warm = PrepareScrollBots(config, 12102, cache);
        Check(warm.UsedCache && warm.ExpandedStates == 0 && warm.ReplaySteps > 0, "Warm plans revalidate in new camera");
        var oldCache = ReplaceCacheKey(cache, config.CacheKey, config.CacheKey.Replace("prestart-v1-accelerating-scroll/", "prestart-v1/"));
        var recovered = PrepareScrollBots(config, 12103, oldCache);
        Check(!recovered.UsedCache && recovered.ExpandedStates > 0, "Old Constant+ON cache rejected");
        var race = RaceSimulation.CreatePrestartRace(config);
        Check(RaceRunner.InstallPrestartPlans(race, warm) && RaceRunner.StartAllBotParty(race), "Four-bot preparation passes shared camera");
        RaceRunner.Advance(race, RaceConfig.CountdownDuration);
        for (int frame = 0; frame < 7200 && race.Phase == "racing"; frame++) RaceRunner.Advance(race, 1d / 60);
        Check(race.Phase == "finished" && race.Players.Any(p => p.Status == PlayerStatus.Finished), "Verified group has a real finisher");
        Check(race.Players.All(p => RaceRunner.PrestartAcceptedCommands(race, p.Id) > 0), "Four independent controllers executed input");

        var legacy = RaceSimulation.CreateStochasticBotRace(entry.Package.Track, FlightPresets.Chosen, BotDifficulty.Master, 0, PartyCamera.Constant);
        RaceSimulation.ConfigureSurfaceRun(legacy, true, settings);
        var controller = new StochasticBotController(legacy);
        Check(RaceRunner.Press(legacy, 0), "Legacy bot race ready");
        RaceRunner.Advance(legacy, RaceConfig.CountdownDuration, controller);
        for (int frame = 0; frame < 7200 && legacy.Phase == "racing"; frame++) RaceRunner.Advance(legacy, 1d / 60, controller);
        var master = legacy.Players[1];
        Check(legacy.Phase == "finished" && master.TerminalTime.HasValue && controller.Cursor > 0, "Legacy Master reached a terminal outcome");
        if (master.Status == PlayerStatus.Eliminated)
            Near(master.Distance + RaceConfig.PlayerRadius, legacy.PartyCameraStartLeft + ScrollTravel(220, settings, master.TerminalTime.Value),
                "Legacy Master crossed the accelerating left boundary", 1e-6);
        else Check(master.Status == PlayerStatus.Finished, "Legacy Master outcome is a finish or left-boundary elimination");
        Console.WriteLine($"Accelerating scroll bots: legacy Master {master.Status} at {master.TerminalTime:F3}s; prestart {race.Players.Count(p => p.Status == PlayerStatus.Finished)}/4 finish");
    }

    static PrestartBotPreparation PrepareScrollBots(PrestartBotConfig config, uint seed, string cache)
    {
        var job = new PrestartBotPreparation(config, seed, cache);
        for (int slice = 0; slice < 4096 && !job.Complete; slice++) job.Step(192);
        Check(job.Complete && job.Success, "Prestart scroll preparation: " + job.Failure);
        return job;
    }

    static string ReplaceCacheKey(string encoded, string expected, string replacement)
    {
        using var input = new MemoryStream(Convert.FromBase64String(encoded));
        using var reader = new BinaryReader(input);
        Check(reader.ReadString() == expected, "Cache fixture key");
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write(replacement);
        writer.Flush();
        input.CopyTo(output);
        return Convert.ToBase64String(output.ToArray());
    }
}
