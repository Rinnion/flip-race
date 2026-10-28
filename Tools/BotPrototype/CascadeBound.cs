using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.BotPrototype;
static class CascadeBound
{
    static void Check(bool b,string why){if(!b)throw new Exception(why);}
    static int Main()
    {
        var track=RaceTracks.All.Single(t=>t.Id==TracksRhythm.CascadeId);
        int obstacles=track.Obstacles.Length,positive=track.RiskZones.Count(z=>!z.IsFinish&&!z.IsCoin&&!z.IsNegative),finish=track.RiskZones.Count(z=>z.IsFinish);
        Check(track.Obstacles.Select(o=>o.Id).Distinct().Count()==obstacles,"unique logical obstacles");
        Check(Probe.Catalog(track).Distinct().Count()==obstacles+positive+finish,"unique fixed goals");
        int total=obstacles+positive+finish,cap=(int)Math.Floor(total*.6+1e-9);
        double best=double.PositiveInfinity;object bestCase=null;
        double finishGain=(RaceConfig.FinishBoostSpeed/RaceConfig.Speed-1)*RaceConfig.FinishBoostDuration;
        for(int h=0;h<=obstacles;h++)for(int b=0;b<=positive;b++)for(int f=0;f<=finish;f++)
        {
            int goals=obstacles-h+b+f;if(goals>cap)continue;
            double minimumTime=track.Length/RaceConfig.Speed+h*RaceConfig.RecoveryDuration-b*RaceConfig.BoostTimeGain-f*finishGain;
            if(minimumTime<best){best=minimumTime;bestCase=new{h,b,f,goals};}
        }
        double cameraDeadline=(track.Length+288.8+RaceConfig.PlayerRadius)/(track.Length/36);
        Check(best>cameraDeadline,"incompatible constraints require decision");
        using var input=JsonDocument.Parse(File.ReadAllText("Verification/bots-complex-prototype.json"));var cases=new List<object>();
        foreach(string profile in new[]{"Jump","Flip"})
        {
            var blocker=input.RootElement.GetProperty("Blockers").EnumerateArray().First(x=>x.GetProperty("Profile").GetString()==profile);
            double[] distances=blocker.GetProperty("Distances").EnumerateArray().Select(x=>x.GetDouble()).ToArray();var flight=profile=="Jump"?FlightPresets.Chosen:FlightPresets.FastFlip;
            foreach(double dt in new[]{1d/30,1d/60,1d/240,.137})
            {
                var lagger=Probe.Run(track,flight,distances,PartyCamera.Lagger,dt);var constant=Probe.Run(track,flight,distances,PartyCamera.Constant,dt);
                Check(lagger.Finished&&lagger.Completed<=cap&&lagger.Collisions==3&&Math.Abs(lagger.Time-best)<1e-7,"legal qualifying full finish without elimination");
                Check(lagger.Accepted==distances.Length&&lagger.Rejected==0,"no rejected recovery taps");
                Check(constant.Status==PlayerStatus.Eliminated,"same legal plan eliminated by constant");
                Check(Math.Abs(constant.Game.Players[0].Distance+RaceConfig.PlayerRadius-constant.Game.PartyCameraLeft)<1e-6,"exact full-body left boundary");
                cases.Add(new{profile,dt,distances,goals=lagger.Completed,total,time=lagger.Time,hits=lagger.Collisions,accepted=lagger.Accepted,constantTime=constant.Time,constantX=constant.Game.Players[0].Distance,constantStatus=constant.Status.ToString(),constantActions=constant.Actions,laggerActions=lagger.Actions});
            }
        }
        var hashes=Directory.GetFiles("Assets/Scripts/Core","*.cs").Append("Tools/BotPrototype/SharedProbe.cs").Append("Tools/BotPrototype/CascadeBound.cs").ToDictionary(x=>x,x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
        File.WriteAllText("Verification/bots-cascade-bound-audit.json",JsonSerializer.Serialize(new{status="PASS independent reproduction of incompatible BOT-03 constraints; not a successful release",obstacles,positive,finish,total,noviceCap=cap,earliestQualifyingFinish=best,bestCase,latestPossibleConstantFinish=cameraDeadline,deficitSeconds=best-cameraDeadline,deficitWorldUnits=(best-cameraDeadline)*220,cases,hashes},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine($"CONFIRMED: {total} goals, Novice <= {cap}; earliest {best:R}s > camera deadline {cameraDeadline:R}s; deficit {(best-cameraDeadline):R}s. Eight legal paired reproductions pass.");return 0;
    }
}