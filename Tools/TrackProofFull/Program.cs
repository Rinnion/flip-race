using System;
using System.Globalization;
using FlipRace.Core;
internal static partial class Program
{
    static GameState Begin(Track track)
    { var game=RaceSimulation.CreateTrial(track, FlightPresets.Chosen); RaceSimulation.Press(game,0); RaceSimulation.Press(game,1); RaceSimulation.Advance(game,3); return game; }
    static RouteResult Run(Track track, double[] inputs, double step=0.01)
    { var result=new RouteResult { Game=Begin(track) }; foreach(double x in inputs) { MoveTo(result.Game,x,step); Tap(result.Game,result); } Finish(result.Game,step); CaptureReward(result); return result; }
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        var track=TracksFull.Practice();
        if(args.Length>0 && args[0]=="--probe")
        {
            var game=Begin(track); int i=0;
            for(double x=20;x<=1600;x+=20) {
                while(i<track.RiskRoute.Length&&track.RiskRoute[i]<=x) { MoveTo(game,track.RiskRoute[i++],.01); Tap(game); }
                MoveTo(game,x,.01); var p=game.Players[0];
                if(x>=700) Console.WriteLine($"{x} y={RaceSimulation.GetPlayerCenterY(p):F3} vy={p.VerticalSpeed:F3} speed={p.CurrentSpeed} support={p.Support} hit={p.Collisions} earned={p.BoostsEarned}");
            }
        }
        else if(args.Length>0&&args[0]=="--full-draft") foreach(var t in TracksFull.All) foreach(var inputs in new[]{t.SafeRoute,t.RiskRoute,TracksFull.NegativeRoute(t.Id)}) {
            var r=Run(t,inputs);var p=r.Game.Players[0];Console.WriteLine($"{t.Id} finish={p.FinishTime} rewards={p.BoostsEarned}+{p.FinishBoostsEarned} negative={p.SlowsEarned} hits={string.Join(',',p.HitObstacleIds)} collected={string.Join(',',p.SuccessfulRiskIds)}");
            foreach(var input in r.Inputs)Console.WriteLine($"input {input.X:R} t={input.Time:F4} y={input.CenterY:F2} support={input.Support}");
        }
        else if(args.Length>0&&args[0]=="--routes") Export();
        else if(args.Length>0&&args[0]=="--windows") Windows();
        else if(args.Length>0&&args[0]=="--recovery") RecoveryProof();
        else if(args.Length>0&&args[0]=="--verify") {FullProof();Console.WriteLine("FULL TRACKS PASS "+checks+" checks.");}
        else { StageOne(); Console.WriteLine("TRACKS-01 PASS " + checks + " checks."); }
    }
}
