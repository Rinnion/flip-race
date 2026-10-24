using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;
internal static partial class Program
{
    static string ValueHash(object value)
    { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(value)))); }
    static readonly string[] Stage03Ids={"mixed-tricks-coin-first","mixed-tricks-coin-second","mixed-tricks-negative-first-choice","mixed-tricks-negative-second-choice"};
    static string OriginalZonesHash(Track track)
    {
        // The immutable stage01 capture predates the additive IsCoin computed
        // property. Remove only that schema addition, and only the four exact
        // intentionally added Mixed IDs, before comparing every original field.
        var nodes=JsonNode.Parse(Snapshot(track.RiskZones.Where(z=>!z.IsFinish&&!Stage03Ids.Contains(z.Id)).ToArray())).AsArray();
        foreach(var zone in nodes)zone.AsObject().Remove("IsCoin");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nodes.ToJsonString(Json))));
    }
    static void PadInventory()
    {
        foreach(var track in RaceTracks.All) {
            var pad=track.RiskZones.Single(z=>z.IsFinish);
            Console.WriteLine(track.Id+" pad="+pad.X+","+pad.Y+" "+pad.FinishWidth+"x"+pad.FinishHeight+" runway="+(track.Length-pad.X-pad.FinishWidth/2-RaceConfig.PlayerRadius)+" obstacles="+ValueHash(track.Obstacles));
        }
    }
    static double[] AuthorRoute(Track track,FlightSettings flight)
    { return track.Id==TracksMixed.Id?MixedRoute(flight,true):TracksRhythm.IsFull(track.Id)?TracksRhythm.FullRoute(track.Id,flight):TracksFull.FullRoute(track.Id,flight); }
    static Track WithoutFinish(Track track)
    { return new Track(track.Length,track.Obstacles,track.Cues){Id=track.Id,Name=track.Name,NegativeEnabled=track.NegativeEnabled,RiskZones=track.RiskZones.Where(z=>!z.IsFinish).ToArray()}; }
    static double[] SkipRoute(Track track,FlightSettings flight)
    {
        if(track.Id==TracksMixed.Id)return MixedRoute(flight,false);
        double x=track.Id==TracksRhythm.CascadeId?(flight.FirstAction==FirstAction.Jump?7670:7710)
            :flight.FirstAction==FirstAction.Flip&&track.Id==TracksFull.ShortId?7750:7730;
        return AuthorRoute(track,flight).Concat(new[]{x}).ToArray();
    }
    static void BranchInventory()
    {
        foreach(var track in RaceTracks.All)
        foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip}) {
            var safe=track.Id==TracksMixed.Id?TracksMixed.SafeRoute(flight):TracksRhythm.IsFull(track.Id)?TracksRhythm.SafeRoute(track.Id,flight):TracksFull.SafeRoute(track.Id,flight);
            var negative=track.Id==TracksMixed.Id?TracksMixed.NegativeRoute(flight):TracksRhythm.IsFull(track.Id)?TracksRhythm.NegativeRoute(track.Id,flight):TracksFull.NegativeRoute(track.Id,flight);
            foreach(var pair in new[]{("safe",safe),("negative",negative)}) {
                var p=Replay(track,flight,pair.Item2,.317).Game.Players[0];
                Console.WriteLine(track.Id+" "+flight.FirstAction+" "+pair.Item1+" hit="+p.Collisions+" ordinary="+p.BoostsEarned+" slow="+p.SlowsEarned+" pad="+p.FinishBoostsEarned+" elapsed="+p.FinishBoostElapsed+" finish="+p.FinishTime);
            }
        }
    }    static void SaveBaseline()
    {
        File.WriteAllText("Verification/iter49-01-before.json",JsonSerializer.Serialize(new {
            Note="Captured before ITER49-01 production edits; pre-existing ITER49-00 Mixed pad is intentional.",
            SourceHashes=Directory.GetFiles("Assets/Scripts/Core","*.cs").ToDictionary(p=>p,Hash),
            Tracks=RaceTracks.All.Select(t=>new{t.Id,t.Length,ObstaclesHash=ValueHash(t.Obstacles),NonFinishHash=ValueHash(t.RiskZones.Where(z=>!z.IsFinish).ToArray())})
        },Json));
    }
    static void AllTracksProof()
    {
        using var baseline=JsonDocument.Parse(File.ReadAllText("Verification/iter49-01-before.json"));
        var hashes=Directory.GetFiles("Assets/Scripts/Core","*.cs").Concat(Directory.GetFiles("Tools/Iter49Finish","*.cs")).Append("Tools/TrackProofControl/Harness.cs").ToDictionary(p=>p,Hash);
        Check(RaceTracks.All.Length==5,"Exactly five real courses");
        foreach(var track in RaceTracks.All) {
            var previous=baseline.RootElement.GetProperty("Tracks").EnumerateArray().Single(t=>t.GetProperty("Id").GetString()==track.Id);
            Near(track.Length,previous.GetProperty("Length").GetDouble(),"Length preserved");
            Check(ValueHash(track.Obstacles)==previous.GetProperty("ObstaclesHash").GetString(),"All stone and both gate-half fields preserved");
            Check(OriginalZonesHash(track)==previous.GetProperty("NonFinishHash").GetString(),"Every original reward/trap field preserved; only four named stage03 additions excluded");
            Check(track.RiskZones.Count(z=>Stage03Ids.Contains(z.Id))==(track.Id==TracksMixed.Id?4:0),"Exactly four intentional Mixed additions, none elsewhere");
            var pad=track.RiskZones.Single(z=>z.IsFinish);
            Near(pad.FinishWidth,28,"Uniform horizontal width");Near(pad.FinishHeight,8,"Uniform horizontal height");
            Near(pad.X,7780,"Actual replacement pad position");
            double latest=pad.X+pad.FinishWidth/2+RaceConfig.PlayerRadius;
            Check(track.Length-latest>=110,"Latest possible circular-body contact leaves entire110-unit pulse");
            foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
            foreach(double step in Steps) {
                var take=Replay(track,flight,AuthorRoute(track,flight),step);
                var skip=Replay(track,flight,SkipRoute(track,flight),step);
                var same=Replay(WithoutFinish(track),flight,AuthorRoute(track,flight),step);
                int ordinary=track.RiskZones.Count(z=>!z.IsFinish&&!z.IsNegative&&!z.IsCoin);
                foreach(var p in take.Game.Players) {
                    Check(p.Collisions==0&&p.SlowsEarned==0,"Clean take "+track.Id+" "+flight.FirstAction);
                    Check(p.BoostsEarned==ordinary,"All ordinary rewards still earned");
                    Check(p.FinishBoostsEarned==1&&!p.FinishBoost,"Exactly one fully served finish reward");
                    Near(p.FinishBoostElapsed,.25,"Full moving duration");Near(p.FinishBoostDistanceGained,55,"Full real extra motion");
                }
                foreach(var p in skip.Game.Players) {
                    Check(p.Collisions==0&&p.SlowsEarned==0&&p.BoostsEarned==ordinary,"Clean skip retains same ordinary rewards");
                    Check(p.FinishBoostsEarned==0,"Actual optional skip");
                }
                Near(skip.Game.Players[0].FinishTime.Value-take.Game.Players[0].FinishTime.Value,.25,"Actual clean take/skip gain");
                Near(same.Game.Players[0].FinishTime.Value-take.Game.Players[0].FinishTime.Value,.25,"Identical input counterfactual gain");
                var hit=take.Rewards.Single(h=>h.Id==pad.Id);
                var contact=Begin(track,flight);
                foreach(var input in take.Inputs.Where(i=>i.Time<hit.At-1e-8)){AdvanceTime(contact,input.Time-contact.Elapsed,step);Tap(contact);}
                AdvanceTime(contact,hit.At-contact.Elapsed,step);
                var cp=contact.Players[0];double cx=cp.Distance,cy=RaceSimulation.GetPlayerCenterY(cp);
                double dx=Math.Max(0,Math.Abs(cx-pad.X)-pad.FinishWidth/2),dy=Math.Max(0,Math.Abs(cy-pad.Y)-pad.FinishHeight/2);
                Near(dx*dx+dy*dy,196,"Body touches visible rectangle precisely",2e-5);
                Check(track.Length-cx>=110,"Actual activation has full runway");
                Evidence.Add(new{Kind="actual-five-course",Track=track.Id,Profile=flight.FirstAction.ToString(),Step=step,
                    Pad=pad,LatestContactX=latest,MinimumRunway=track.Length-latest,Contact=new{X=cx,Y=cy,Time=hit.At,Remaining=track.Length-cx},
                    OrdinaryRewards=ordinary,TakeFinish=take.Game.Players[0].FinishTime,SkipFinish=skip.Game.Players[0].FinishTime,
                    TakeInputs=take.Inputs,SkipInputs=skip.Inputs,ObstaclesHash=ValueHash(track.Obstacles)});
            }
        }
        Check(hashes.All(k=>Hash(k.Key)==k.Value),"All recorded sources unchanged during proof");
        File.WriteAllText("Verification/iter49-01-finish.json",JsonSerializer.Serialize(new{
            Status="PASS",Stage="ITER49-01 five active tracks",Checks=checks,Steps,SourceHashes=hashes,
            BaselineHash=Hash("Verification/iter49-01-before.json"),
            Scope="Public Press/Advance witnesses for both players, both profiles, five active courses, six step sizes; no state injection. Native rendering remains delivery verification.",Evidence},Json));
        Console.WriteLine("PASS ITER49-01: "+checks+" checks;60 actual take/skip/same-input comparisons;five pads28x8 atX7780;latest contactX7808;minimum runway112;all full.25s/55unit;unchanged stones/gate halves/original zones/length; four explicit stage03 additions excluded.");
    }
}