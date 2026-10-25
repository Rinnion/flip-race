using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;

internal static partial class Program
{
    static readonly FlightSettings[] Profiles={FlightPresets.Chosen,FlightPresets.FastFlip};
    static readonly double[] Steps={1d/30,1d/60,1d/144,.317,2.5,100};
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
    static readonly List<object> Evidence=new List<object>();
    static readonly string C1=TracksMixed.Id+"-coin-first",C2=TracksMixed.Id+"-coin-second",N1=TracksMixed.Id+"-negative-first-choice",N2=TracksMixed.Id+"-negative-second-choice";
    static readonly string[] Additions={C1,C2,N1,N2};
    static int games;
    static string Snapshot(object value)=>JsonSerializer.Serialize(value,Json);
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static string ValueHash(object value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(value))));
    static GameState Begin(Track track,FlightSettings flight)
    {games++;var game=RaceSimulation.CreateTrial(track,flight);Tap(game);RaceSimulation.Advance(game,3);Check(game.Phase=="racing"&&game.Elapsed==0,"actual readiness/countdown");return game;}
    static RouteResult Replay(Track track,FlightSettings flight,double[] inputs,double step)
    {var r=new RouteResult{Game=Begin(track,flight)};foreach(double x in inputs){MoveTo(r.Game,x,step);Tap(r.Game,r);}Finish(r.Game,step);CaptureReward(r);return r;}
    static RouteResult Timeline(Track track,FlightSettings flight,List<InputSample> inputs,double step)
    {var r=new RouteResult{Game=Begin(track,flight)};foreach(var i in inputs){AdvanceTime(r.Game,i.Time-r.Game.Elapsed,step);Tap(r.Game,r);}Finish(r.Game,step);return r;}
    static Track WithoutCoins(Track track)
    {return new Track(track.Length,track.Obstacles,track.Cues){Id=track.Id,Name=track.Name,NegativeEnabled=track.NegativeEnabled,RiskZones=track.RiskZones.Where(z=>!z.IsCoin).ToArray()};}
    static Track Empty(params RiskZone[] zones)=>new Track(1000,Array.Empty<Obstacle>()){RiskZones=zones};
    static RiskZone Coin(string id,double x,double y=226)=>new RiskZone{Id=id,Kind="coin",X=x,Y=y,Radius=8};
    static int Bits(int n)=>(n&1)+((n>>1)&1);
    static void Geometry()
    {
        var track=TracksMixed.Get();using var before=JsonDocument.Parse(File.ReadAllText("Verification/iter49-01-before.json"));
        var original=before.RootElement.GetProperty("Tracks").EnumerateArray().Single(t=>t.GetProperty("Id").GetString()==track.Id);
        Near(track.Length,original.GetProperty("Length").GetDouble(),"Mixed length unchanged");
        Check(ValueHash(track.Obstacles)==original.GetProperty("ObstaclesHash").GetString(),"every original stone and both gate halves unchanged");
        var nodes=JsonNode.Parse(Snapshot(track.RiskZones.Where(z=>!z.IsFinish&&!Additions.Contains(z.Id)).ToArray())).AsArray();
        foreach(var z in nodes)z.AsObject().Remove("IsCoin");
        Check(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nodes.ToJsonString(Json))))==original.GetProperty("NonFinishHash").GetString(),"all original nine rewards and two traps unchanged; only additive IsCoin schema omitted");
        Check(track.RiskZones.Count(z=>z.IsCoin)==2&&track.RiskZones.Count(z=>z.IsNegative)==4,"exactly two coins plus four total negatives");
        Check(track.RiskZones.Count(z=>!z.IsCoin&&!z.IsNegative&&!z.IsFinish)==9&&track.PositiveRingCount==10,"coins excluded from nine ordinary plus finish reward count");
        foreach(var c in new[]{(C1,3250d,150d,true),(C2,5500d,100d,true),(N1,3300d,180d,false),(N2,5720d,180d,false)}){
            var z=track.RiskZones.Single(z=>z.Id==c.Item1);Near(z.X,c.Item2,"chosen X");Near(z.Y,c.Item3,"chosen Y");Near(z.Radius,8,"visible circle radius");Check(z.IsCoin==c.Item4&&z.IsNegative!=c.Item4,"exact additive type");
            Check((z.X>=3008&&z.X<=3550)||(z.X>=5400&&z.X<=5900),"only approved free intervals");
        }
        Check(track.RiskZones.Count(z=>z.IsFinish)==1,"one independent finish target");
        Near(FinishTargetGeometry.HalfWidth+FinishTargetGeometry.StrokeWidth/2,12,"compact stroke outer half-width");
        Near(FinishTargetGeometry.HalfHeight+FinishTargetGeometry.StrokeWidth/2,10,"compact stroke outer half-height");
        Check(FinishTargetGeometry.SegmentCount==4,"two chevrons, four visible strokes");
        foreach(var other in RaceTracks.All.Where(t=>t.Id!=TracksMixed.Id))Check(!other.RiskZones.Any(z=>z.IsCoin||Additions.Contains(z.Id)),"other four tracks receive no new content");
    }
    static void FullStats(RouteResult r,int coins,int slows,string why)
    {
        foreach(var p in r.Game.Players){
            Check(p.Collisions==0,why+" clean collision count: "+string.Join(",",p.HitObstacleIds));
            Check(p.BoostsEarned==9&&p.SlowsEarned==slows&&p.CompletedBoosts==9&&p.CompletedSlows==slows,why+" unchanged full ordinary pulse ledger");
            Check(p.CoinsEarned==coins,why+" exact personal coin count");
            Check(p.FinishBoostsEarned==0&&!p.FinishBoost&&p.LastFinishBoost==null,why+" ordinary content route does not aim at finish");Near(p.FinishBoostElapsed,0,why+" no served finish pulse");Near(p.FinishBoostDistanceGained,0,why+" no finish travel credit");
            Near(p.FinishTime.Value,33.30+.3*slows,why+" actual physical arrival");
        }
        Check(Equals(r.Game.Winner,"tie"),why+" symmetric real tie");
    }
    static void SameMotion(RouteResult actual,RouteResult stripped,string why)
    {
        Check(actual.Inputs.Count==stripped.Inputs.Count,why+" same input count");
        for(int i=0;i<actual.Inputs.Count;i++){
            var a=actual.Inputs[i];var b=stripped.Inputs[i];Near(a.Time,b.Time,why+" identical input time");Near(a.X,b.X,why+" same physical X",1e-6);Near(a.CenterY,b.CenterY,why+" same physical Y",1e-6);Near(a.VerticalSpeed,b.VerticalSpeed,why+" same momentum",1e-6);
            Check(a.Support==b.Support&&a.Airborne==b.Airborne&&a.AirFlipAvailable==b.AirFlipAvailable,why+" same support/repeat state");
        }
        for(int i=0;i<2;i++){
            var a=actual.Game.Players[i];var b=stripped.Game.Players[i];Near(a.FinishTime.Value,b.FinishTime.Value,why+" score never alters finish");
            Check(a.BoostsEarned==b.BoostsEarned&&a.SlowsEarned==b.SlowsEarned&&a.Collisions==b.Collisions&&a.FinishBoostsEarned==b.FinishBoostsEarned,why+" score never changes rewards/hits");
            Check(b.CoinsEarned==0&&b.LastCoin==null,why+" true no-coin counterfactual");
        }
        Check(Equals(actual.Game.Winner,stripped.Game.Winner),why+" score never changes winner");
    }
    static void Routes()
    {
        foreach(var f in Profiles)foreach(double step in Steps)foreach(int mask in new[]{0,1,2,3}){
            var take=Replay(TracksMixed.Get(),f,TracksMixed.CoinRoute(f,mask),step);FullStats(take,Bits(mask),0,"coin mask"+mask);
            foreach(var p in take.Game.Players)foreach(var c in new[]{(C1,1),(C2,2)}){
                bool expected=(mask&c.Item2)!=0;Check(p.SuccessfulRiskIds.Contains(c.Item1)==expected&&p.CollectedRiskIds.Contains(c.Item1)==expected,"personal availability matches actual coin path");Check(!p.FailedRiskIds.Contains(c.Item1),"coin never becomes a provisional failed ring");
            }
            var absent=Timeline(WithoutCoins(TracksMixed.Get()),f,take.Inputs,step);SameMotion(take,absent,"same exact timed input with no coins");
            Evidence.Add(new{Kind="actual-coin-subset",Profile=f.FirstAction.ToString(),Step=step,Mask=mask,Coins=take.Game.Players[0].CoinsEarned,Finish=take.Game.Players[0].FinishTime,LastCoin=take.Game.Players[0].LastCoin,Inputs=take.Inputs});
            var trap=Replay(TracksMixed.Get(),f,TracksMixed.ContentTrapRoute(f,mask),step);FullStats(trap,0,Bits(mask),"new trap mask"+mask);
            foreach(var p in trap.Game.Players)foreach(var n in new[]{(N1,1),(N2,2)})Check(p.NegativeRiskIds.Contains(n.Item1)==((mask&n.Item2)!=0),"each new trap actually taken or avoided");
            Evidence.Add(new{Kind="actual-new-trap-subset",Profile=f.FirstAction.ToString(),Step=step,Mask=mask,Finish=trap.Game.Players[0].FinishTime,NegativeIds=trap.Game.Players[0].NegativeRiskIds,Inputs=trap.Inputs});
            var old=Replay(TracksMixed.Get(),f,TracksMixed.NegativeRoute(f,mask),step);FullStats(old,0,Bits(mask),"existing trap mask"+mask);
            Check(old.Game.Players.All(p=>!p.NegativeRiskIds.Contains(N1)&&!p.NegativeRiskIds.Contains(N2)),"existing negative routes avoid both new traps");
        }
    }
    static void PersonalAndReset()
    {
        foreach(var f in Profiles)foreach(double step in Steps){
            var t=Empty(Coin("one",200),Coin("two",400));var game=Begin(t,f);
            MoveTo(game,100,step);Check(RaceSimulation.Press(game,1),"one real player takes an avoiding flight");Finish(game,step);
            var a=game.Players[0];var b=game.Players[1];
            Check(a.CoinsEarned==2&&b.CoinsEarned==(f.FirstAction==FirstAction.Jump?1:0),"personal contacts remain independent and capped2");
            var capped=Begin(Empty(Coin("one",200),Coin("two",400),Coin("three-cap",600)),f);Finish(capped,step);
            Check(capped.Players.All(p=>p.CoinsEarned==2&&p.SuccessfulRiskIds.Contains("three-cap")&&p.LastCoin.Id=="two"),"actual third contact deduplicates without exceeding cap or replacing last earned event");
            Check(a.BoostsEarned==0&&b.BoostsEarned==0&&a.SlowsEarned==0&&b.SlowsEarned==0,"coins never become ordinary rewards");
            Near(a.FinishTime.Value,1000d/220,"coin take no speed gain");Near(b.FinishTime.Value,1000d/220,"coin skip no speed loss");Check(Equals(game.Winner,"tie"),"higher coin count cannot break a physical tie");
            Check(t.PositiveRingCount==0,"isolated coin track has no positive rings");
            double end=game.Elapsed;RaceSimulation.Advance(game,100);Check(game.Elapsed==end&&a.CoinsEarned==2,"result freezes score");
            Check(RaceSimulation.Press(game,0),"first rematch ready");Check(a.CoinsEarned==2,"result persists until next race");Check(RaceSimulation.Press(game,1),"second rematch ready");games++;
            Check(game.Players.All(p=>p.CoinsEarned==0&&p.LastCoin==null&&p.SuccessfulRiskIds.Count==0&&p.CollectedRiskIds.Count==0),"fresh rematch resets score/event/personal availability");
            RaceSimulation.Advance(game,3);Finish(game,step);Check(game.Players.All(p=>p.CoinsEarned==2),"both may personally collect again in new race");
            var asyncGame=Begin(Empty(Coin("shared-personal",200,150)),f);
            double first=f.FirstAction==FirstAction.Jump?110:130,second=f.FirstAction==FirstAction.Jump?135:145;
            MoveTo(asyncGame,first,step);Check(RaceSimulation.Press(asyncGame,0),"first player's optional pickup input");
            MoveTo(asyncGame,second,step);Check(RaceSimulation.Press(asyncGame,1),"second player's later optional pickup input");
            bool firstOnly=false;while(asyncGame.Players[1].CoinsEarned==0&&asyncGame.Elapsed<2){RaceSimulation.Advance(asyncGame,.001);if(!firstOnly&&asyncGame.Players[0].CoinsEarned==1&&asyncGame.Players[1].CoinsEarned==0){Check(asyncGame.Players[0].SuccessfulRiskIds.Contains("shared-personal")&&!asyncGame.Players[1].SuccessfulRiskIds.Contains("shared-personal"),"pure personal availability survives first collection for split or shared views");firstOnly=true;}}
            Check(firstOnly&&asyncGame.Players.All(p=>p.CoinsEarned==1),"one collector leaves the same coin available to opponent later");
            Check(asyncGame.Players[0].LastCoin.At<asyncGame.Players[1].LastCoin.At,"separate real personal contact timestamps");Finish(asyncGame,step);
        }
    }
    static void ContactsAndQueues()
    {
        foreach(double step in Steps){
            foreach(double gap in new[]{0d,21.9,22,22.001}){
                var game=Begin(Empty(Coin("circle",200,226-gap)),FlightPresets.Chosen);Finish(game,step);var p=game.Players[0];
                Check(p.CoinsEarned==(gap<=22?1:0),"actual swept circular inclusive tangent / outside miss");
                if(gap<=22)Near(p.LastCoin.At,(200-Math.Sqrt(484-gap*gap))/220,"continuous circular contact timestamp",1e-7);
                Near(p.FinishTime.Value,1000d/220,"coin contact never changes speed");
            }
            var zones=new[]{new RiskZone{Id="ordinary",Kind="positive",FreeReward=true,X=50,Y=226,Radius=8},new RiskZone{Id="queued",Kind="negative",X=90,Y=226,Radius=8},Coin("coin",100)};
            var game2=Begin(Empty(zones),FlightPresets.Chosen);MoveTo(game2,77,step);var q=game2.Players[0];var active=q.ActiveEffect;var queued=q.PendingEffects.Peek();double remaining=active.Remaining;
            MoveTo(game2,79,step);Check(q.CoinsEarned==1&&q.CurrentSpeed==330,"coin does not preempt active speed");
            Check(ReferenceEquals(active,q.ActiveEffect)&&ReferenceEquals(queued,q.PendingEffects.Peek()),"coin keeps exact active and queued objects/order");
            Near(active.Remaining,remaining-2d/330,"ordinary clock continues normally through coin event");Near(queued.Remaining,.6,"coin never advances waiting effect");
            Finish(game2,step);Check(q.CompletedBoosts==1&&q.CompletedSlows==1,"coin preserves all queued ordinary pulses");Near(q.FinishTime.Value,1000d/220,"coin and balanced pair preserve ledger");
            var stone=new Obstacle{Id="stone",Kind="floor",X=230,Width=20,Height=48};var collision=Begin(new Track(1000,new[]{stone}){RiskZones=new[]{Coin("at-impact",238),Coin("after",500)}},FlightPresets.Chosen);
            MoveTo(collision,216,step);var hit=collision.Players[0];Check(hit.Collisions==1&&hit.CoinsEarned==1&&hit.Recovering>0,"simultaneous real impact and personal coin contact");
            AdvanceTime(collision,.49,step);Check(hit.CoinsEarned==1,"lingering frozen contact never re-awards");Finish(collision,step);Check(hit.CoinsEarned==2,"next personal coin still available after recovery");Near(hit.FinishTime.Value,1000d/220+.5,"coin grants neither immunity nor speed");
            var pulse=new RiskZone{Id="finish",Kind="finish",X=172+10.5+Math.Sqrt(168),Y=226};var during=Begin(Empty(pulse,Coin("during-pulse",230)),FlightPresets.Chosen);
            MoveTo(during,250,step);var d=during.Players[0];Check(d.CoinsEarned==1&&d.FinishBoost&&d.CurrentSpeed==440,"coin leaves active finish speed unchanged");Near(d.FinishBoostElapsed,78d/440,"coin preserves finish moving clock");
            double frozen=d.Distance;RaceSimulation.Advance(during,0);Check(d.Distance==frozen&&d.CoinsEarned==1,"zero-advance pause preserves coin state");Finish(during,step);Near(d.FinishTime.Value,1000d/220-.25,"coin cannot modify finish time ledger");
        }
    }
    static void Main()
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;captureCollision=false;
        try{
            var paths=Directory.GetFiles("Assets/Scripts/Core","*.cs").Concat(Directory.GetFiles("Tools/Iter49Content","*.cs")).Append("Tools/TrackProofControl/Harness.cs").ToArray();var hashes=paths.ToDictionary(p=>p,Hash);
            Geometry();Routes();PersonalAndReset();ContactsAndQueues();Check(hashes.All(p=>Hash(p.Key)==p.Value),"recorded sources unchanged during proof");
            File.WriteAllText("Verification/fix491-content.json",JsonSerializer.Serialize(new{Status="PASS",Checks=checks,Games=games,Steps,SourceHashes=hashes,Stage01BaselineHash=Hash("Verification/iter49-01-before.json"),
                Scope="Actual production IsCoin/CoinsEarned/LastCoin, public Press/Advance; all coin subsets and new/old negative subsets on both profiles/six steps; identical timed-input no-coin counterfactuals; personal late and simultaneous collections, cap/reset/sweep/FIFO/recovery/finish interactions. No state injection or passive prep probes in final proof.",Evidence},Json));
            Console.WriteLine("PASS FIX491 CONTENT: "+checks+" checks / "+games+" actual games; 2 coins + 2 additional negatives; both profiles/six steps/all coin and trap subsets; 9 ordinary rewards/ordinary routes skip finish; same timed inputs without coins; personal once/cap/reset/queue/impact/tangent.");
        }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
}