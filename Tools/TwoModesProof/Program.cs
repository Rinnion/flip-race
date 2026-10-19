using System;
using System.Globalization;
using FlipRace.Core;

internal static partial class Program
{
    static readonly double[] Steps = { 1d/30, 1d/60, 1d/144, .317, 2.5, 100 };
    static readonly FlightSettings[] Profiles = { FlightPresets.Chosen, FlightPresets.FastFlip };
    static string ProfileId(FlightSettings flight) { return flight == FlightPresets.FastFlip ? "fast-flip" : "chosen"; }
    static GameState Begin(Track track, FlightSettings flight)
    {
        var game = RaceSimulation.CreateTrial(track, flight);
        Check(RaceSimulation.Press(game,0) && RaceSimulation.Press(game,1), "both ready");
        RaceSimulation.Advance(game,3); return game;
    }
    static RouteResult Run(Track track, FlightSettings flight, double[] route, double step=.01)
    {
        var result = new RouteResult { Game=Begin(track,flight) };
        foreach(double x in route) { MoveTo(result.Game,x,step); Tap(result.Game,result,flight==FlightPresets.FastFlip); }
        Finish(result.Game,step); return result;
    }
    static void Describe(Track track, FlightSettings flight, double[] route)
    {
        var r=Run(track,flight,route,.01); var p=r.Game.Players[0];
        Console.WriteLine($"{track.Id}/{ProfileId(flight)} finish={p.FinishTime:R} +{p.BoostsEarned} finish+{p.FinishBoostsEarned} -{p.SlowsEarned} hits={string.Join(',',p.HitObstacleIds)} rings={string.Join(',',p.SuccessfulRiskIds)}");
        foreach(var input in r.Inputs) Console.WriteLine($"  x={input.X:R} t={input.Time:R} y={input.CenterY:R} support={input.Support}");
    }
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        captureCollision=false;
        if(args.Length>0 && args[0]=="--draft")
            foreach(var flight in Profiles) foreach(var track in new[]{TracksFull.Practice()}) {
                Describe(track,flight,TracksFull.SafeRoute(track.Id,flight));
                Describe(track,flight,TracksFull.FullRoute(track.Id,flight));
            }
        else if(args.Length>0&&args[0]=="--draft-full")
            foreach(var track in TracksFull.All)
                foreach(var route in new[]{TracksFull.SafeRoute(track.Id,FlightPresets.FastFlip),TracksFull.FullRoute(track.Id,FlightPresets.FastFlip),TracksFull.NegativeRoute(track.Id,FlightPresets.FastFlip)})
                    Describe(track,FlightPresets.FastFlip,route);
        else if(args.Length>0&&args[0]=="--search-short")SearchShortLow();
        else if(args.Length>0&&args[0]=="--verify")FullProof();
        else if(args.Length>0&&args[0]=="--core")Console.WriteLine(FlipRace.Editor.EngineModesTests.RunAll());
        else if(args.Length>0&&args[0]=="--hover")HoverRegression();
        else if(args.Length>0&&args[0]=="--windows")Windows();
        else if(args.Length>0&&args[0]=="--routes")ExportAll(args.Length>1?args[1]:"Verification/modes-routes.json");
        else if(args.Length>0&&args[0]=="--routes-stage")ExportStage();
        else StageProof();
    }
}
