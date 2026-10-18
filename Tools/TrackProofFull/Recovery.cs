using System;
using FlipRace.Core;

internal static partial class Program
{
    static GameState ErrorCollision(Track track, double[] route, RouteResult baseline, int changed, double shift)
    {
        var game = Begin(track);
        firstCollision = null; captureCollision = true;
        try
        {
            for (int i = 0; i < route.Length && firstCollision == null; i++)
            {
                if (i == changed)
                {
                    double time = baseline.Inputs[i].Time + shift;
                    if (time < game.Elapsed) return null;
                    AdvanceTime(game, time - game.Elapsed);
                }
                else if (route[i] >= game.Players[0].Distance) MoveTo(game, route[i], .01);
                else continue;
                RaceSimulation.Press(game, 0); RaceSimulation.Press(game, 1);
            }
            while (firstCollision == null && game.Phase == "racing") Step(game, .02);
            return firstCollision;
        }
        finally { captureCollision = false; firstCollision = null; }
    }
    static bool ClearsTarget(GameState game, double target, int hits)
    {
        if (game.Players[0].Collisions != hits) return false;
        if (game.Players[0].Distance >= target) return true;
        var trial = Clone(game);
        try { MoveTo(trial, target, .03); }
        catch { return false; }
        return trial.Players[0].Collisions == hits && trial.Players[0].Distance >= target - 1e-7;
    }
    static bool EscapeNext(GameState collision, out string witness)
    {
        witness = ""; var ready = Clone(collision); var p = ready.Players[0]; int hits = p.Collisions;
        int index = Array.FindIndex(ready.Track.Obstacles, o => o.Id == p.LastHit.Id);
        Check(index >= 0, "Recovery hit has real obstacle ID");
        if (index == ready.Track.Obstacles.Length - 1) { witness = "last obstacle, no later obstacle"; return true; }
        var next = ready.Track.Obstacles[index + 1]; double target = next.X + next.Width + 15;
        AdvanceTime(ready, p.Recovering); AdvanceTime(ready, .15);
        if (p.Collisions != hits) return false;
        if (ClearsTarget(ready, target, hits)) { witness = "stay after recovery + .15 reaction"; return true; }
        for (double delay = 0; delay <= 2.0; delay += .05)
        {
            var one = Clone(ready); AdvanceTime(one, delay, .03);
            if (one.Players[0].Collisions != hits || !RaceSimulation.Press(one, 0)) continue;
            RaceSimulation.Press(one, 1);
            double oneX = one.Players[0].Distance;
            if (ClearsTarget(one, target, hits))
            { witness = "tap after reaction + " + delay.ToString("F2") + " at x=" + oneX.ToString("F3"); return true; }
            for (double second = .15; second <= 1.8; second += .05)
            {
                var two = Clone(one); AdvanceTime(two, second, .03);
                if (two.Players[0].Collisions != hits || !RaceSimulation.Press(two, 0)) continue;
                RaceSimulation.Press(two, 1);
                if (!ClearsTarget(two, target, hits)) continue;
                witness = "tap after reaction + " + delay.ToString("F2") + " at x=" + oneX.ToString("F3")
                    + "; second +" + second.ToString("F2") + " at x=" + two.Players[0].Distance.ToString("F3");
                return true;
            }
        }
        return false;
    }
    static void RecoveryProof()
    {
        int total=0;
        foreach(var track in TracksFull.All) foreach(bool ideal in new[]{false,true}) {
            var route=ideal?track.RiskRoute:track.SafeRoute;var baseline=Run(track,route);int count=0;
            for(int input=0;input<route.Length;input++) foreach(double shift in new[]{-.32,-.16,.16,.32}) {
                var collision=ErrorCollision(track,route,baseline,input,shift);if(collision==null)continue;
                count++;string witness;bool escaped=EscapeNext(collision,out witness);
                Check(escaped,"Recovery150ms failed "+track.Id+" input="+input+" shift="+shift+" hit="+collision.Players[0].LastHit.Id);
            }
            total+=count;Console.WriteLine("RECOVERY "+track.Id+" "+(ideal?"full":"safe")+" states="+count+" PASS");
        }
        Console.WriteLine("RECOVERY TOTAL "+total+" states; sampled real errors, no injected state.");
    }
}
