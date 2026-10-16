using System;
using System.Globalization;
using System.Reflection;
using FlipRace.Core;

internal static partial class Program
{
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string command = args.Length == 0 ? "--preparation" : args[0];
        if (command == "--preparation")
        {
            SnapshotContract(); OpenBaseline();
            Console.WriteLine("PREPARATION PASS " + checks + " checks; no CONTROL physics or geometry gate claimed.");
        }
        else if (command == "--landing-fixtures")
        {
            // Run only after engine announces CONTROL-01 green. These are tool
            // fixtures, not proposed runtime geometry or the engine's own gate.
            SnapshotContract(); LandingFixtures();
            Console.WriteLine("LANDING FIXTURE PASS " + checks + " checks.");
        }
        else if (command == "--controls-open")
        {
            SnapshotContract(); ControlsOpen();
            Console.WriteLine("OPEN ROUTE PASS " + checks + " checks; browser gate remains separate.");
        }
        else if (command == "--routes-draft") DraftRoutes();
        else if (command == "--windows") RouteWindows();
        else if (command == "--trap-search") TrapSearch();
        else if (command == "--comparison") ComparisonProof();
        else if (command == "--recovery") RecoveryProof();
        else if (command == "--unlimited") UnlimitedProof();
        else if (command == "--finish") FinishPlaneProof();
        else if (command == "--export") ExportRoutes();
        else if (command == "--verify")
        {
            ComparisonProof(); RouteWindows(); RecoveryProof(); UnlimitedProof(); FinishPlaneProof();
            Console.WriteLine("CONTROL TRACKS PASS " + checks + " checks.");
        }
        else throw new ArgumentException("Use --preparation, --landing-fixtures or --controls-open");
    }
    static void SnapshotContract()
    {
        var source = Start(TracksV4.Open(), MotionMode.GravityAirFlip);
        var p = source.Players[0];
        p.Support = SupportKind.Stone; p.SupportSide = 0; p.SupportObstacleId = "snapshot-stone";
        p.ActiveEffect = new SpeedEffect { RingId = "active", Remaining = .7, Elapsed = .5,
            PulseCount = 2, CompletedPulses = 0 };
        p.PendingEffects.Enqueue(new SpeedEffect { RingId = "suspended", IsNegative = true,
            Remaining = .3, Elapsed = .3, PulseCount = 1 });
        p.CollectedRiskIds.Add("collected");
        p.LastInstant = new HitInfo { Id = "active", Message = "test", At = 1 };
        p.FinishBoost=true; p.FinishBoostsEarned=1; p.FinishBoostElapsed=.03; p.FinishBoostDistanceGained=6.6;
        p.LastFinishBoost=new HitInfo { Id="finish", At=2 };
        var copy = Clone(source); var q = copy.Players[0];
        Check(!ReferenceEquals(p, q) && ReferenceEquals(source.Track, copy.Track)
            && ReferenceEquals(source.Profile, copy.Profile), "Snapshot isolates players and shares immutable configuration");
        Check(q.Support == SupportKind.Stone && q.SupportSide == 0 && q.SupportObstacleId == "snapshot-stone",
            "Snapshot preserves explicit support");
        Check(q.ActiveEffect.PulseCount == 2 && q.PendingEffects.Peek().Elapsed == .3,
            "Snapshot preserves extended and suspended pulse-ledger");
        Check(q.FinishBoost && q.FinishBoostsEarned==1 && q.FinishBoostElapsed==.03
            && q.FinishBoostDistanceGained==6.6 && q.LastFinishBoost.At==2,"Snapshot preserves separate finish state and ledger");
        q.ActiveEffect.CompletedPulses = 1; q.PendingEffects.Peek().Remaining = .1;
        q.CollectedRiskIds.Add("copy-only"); q.LastInstant.At = 5;
        q.LastFinishBoost.At=9;
        Check(p.ActiveEffect.CompletedPulses == 0 && p.PendingEffects.Peek().Remaining == .3
            && !p.CollectedRiskIds.Contains("copy-only") && p.LastInstant.At == 1 && p.LastFinishBoost.At==2,
            "Snapshot has no mutable effect/set/event aliases");
    }
    static void OpenBaseline()
    {
        foreach (double step in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5 })
        {
            var result = Route(TracksV4.Open(), MotionMode.GravityAirFlip, MotionProfiles.Baseline,
                new double[] { 220, 660, 1100, 1540, 1980 }, step, false);
            CheckClean(result, 0, 0, 10);
            Check(result.AirInputs == 0, "Open baseline inputs start on walls");
            var changed = TimedShift(TracksV4.Open(), MotionMode.GravityAirFlip, MotionProfiles.Baseline,
                new double[] { 220, 660, 1100, 1540, 1980 }, result, 2, .04, false);
            CheckClean(changed, 0, 0, 10);
        }
    }
    static MotionMode RequireMode(string name)
    {
        MotionMode mode;
        Check(Enum.TryParse(name, out mode) && Enum.IsDefined(typeof(MotionMode), mode),
            "Production mode must be available: " + name);
        return mode;
    }
    static MotionProfile RequireProfile(string name)
    {
        var field = typeof(MotionProfiles).GetField(name, BindingFlags.Static | BindingFlags.Public);
        Check(field != null && field.FieldType == typeof(MotionProfile), "Production profile must be available: " + name);
        return (MotionProfile)field.GetValue(null);
    }
    static void ControlsOpen()
    {
        string[] modes = { "GravityAirFlip", "LinearAirFlip", "JumpGravityFlip", "GravityUnlimited" };
        string[] profiles = { "Moderate", "Baseline", "Jump", "Moderate" };
        for (int i = 0; i < modes.Length; i++)
        {
            var mode = RequireMode(modes[i]); var profile = RequireProfile(profiles[i]);
            foreach (double step in new[] { 1d / 30, 1d / 60, 1d / 144, .317, 2.5 })
            {
                var result = Route(TracksV4.Open(), mode, profile,
                    new double[] { 220, 660, 1100, 1540, 1980 }, step, true);
                CheckClean(result, 0, 0, 10);
                Check(result.AirInputs == 0, "Spaced open inputs use real support: " + modes[i]);
            }
            Console.WriteLine("Open " + modes[i] + ": 5 ground departures, 10s, 0 hits/rewards.");
        }
    }
}
