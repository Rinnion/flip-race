using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FlipRace.Core;
internal static partial class Program
{
    static readonly double[] Frames={1d/30,1d/60,1d/144,.317,2.5,100};
    static (double x,double y) Position(string id)=>id==TracksMixed.Id?(7712,80):id==TracksFull.FlowId?(7780,176):(7780,64);
    static double Aim(string id,FlightSettings f)=>id==TracksMixed.Id?(f.FirstAction==FirstAction.Jump?7639:7650):id==TracksRhythm.CascadeId?(f.FirstAction==FirstAction.Jump?7758:7724):(f.FirstAction==FirstAction.Jump?7736:7706);
    static double[] TakeRoute(Track t,FlightSettings f)
    {
        var route=FrozenRoute(t.Id,f);if(t.Id==TracksMixed.Id){route[route.Length-1]=Aim(t.Id,f);return route;}
        return route.Concat(new[]{Aim(t.Id,f)}).ToArray();
    }
    static RouteResult Timeline(Track track,FlightSettings f,IEnumerable<InputSample> inputs,double step,double lastShift=0)
    {
        var all=inputs.ToArray();var r=new RouteResult{Game=Begin(track,f)};
        for(int i=0;i<all.Length;i++){AdvanceTime(r.Game,all[i].Time+(i==all.Length-1?lastShift:0)-r.Game.Elapsed,step);Tap(r.Game,r);}
        Finish(r.Game,step,false);CaptureReward(r);return r;
    }
    static double IndependentStrokeDistance(double x,double y)
    {
        double best=double.PositiveInfinity;double[,] s={{-10.5,-8.5,-2.1,0},{-2.1,0,-10.5,8.5},{2.1,-8.5,10.5,0},{10.5,0,2.1,8.5}};
        for(int i=0;i<4;i++){double dx=s[i,2]-s[i,0],dy=s[i,3]-s[i,1],u=Math.Clamp(((x-s[i,0])*dx+(y-s[i,1])*dy)/(dx*dx+dy*dy),0,1);double a=x-s[i,0]-u*dx,b=y-s[i,1]-u*dy;best=Math.Min(best,a*a+b*b);}return best;
    }
    static void TakeStats(RouteResult r,int ordinary,string label)
    {
        Check(Clean(r,ordinary),label+" preserves all ordinary rewards and no hits/slows");
        foreach(var p in r.Game.Players){Check(p.FinishBoostsEarned==1&&!p.FinishBoost,label+" one personal complete finish");Near(p.FinishBoostElapsed,.25,label+" .25 moving seconds");Near(p.FinishBoostDistanceGained,55,label+" 55 actual extra units");Check(p.ActiveEffect==null&&p.PendingEffects.Count==0,label+" no truncated ordinary effects");}
    }
    static void Proof(bool candidate)
    {
        var evidence=new List<object>();
        var sources=Directory.GetFiles("Assets/Scripts/Core","*.cs").Concat(Directory.GetFiles("Tools/Fix491","*.cs")).ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        using var baseline=JsonDocument.Parse(File.ReadAllText(BaselinePath));
        foreach(var actual in RaceTracks.All)
        {
            var where=Position(actual.Id);var t=candidate?At(actual,where.x,where.y):actual;
            var old=baseline.RootElement.GetProperty("Tracks").EnumerateArray().Single(b=>b.GetProperty("Id").GetString()==t.Id);
            Near(t.Length,old.GetProperty("Length").GetDouble(),"track length preserved");Check(ValueHash(t.Obstacles)==old.GetProperty("Obstacles").GetString(),"every stone and both gate halves unchanged");Check(ValueHash(t.RiskZones.Where(z=>!z.IsFinish).ToArray())==old.GetProperty("NonFinish").GetString(),"all ordinary rewards/traps/coins unchanged");
            var goal=t.RiskZones.Single(z=>z.IsFinish);Near(goal.X,where.x,"approved targetX");Near(goal.Y,where.y,"approved targetY");
            Check(TargetClear(t,goal.X,goal.Y),"visible arrows do not overlap solids");
            Check(Math.Min(goal.Y-10,240-goal.Y-10)>=34,"stroke edges >28 from both walls +6spare");Check(t.Length-goal.X-26>=110,"latest circular body contact has full runway");
            foreach(var f in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})foreach(double dt in Frames)
            {
                var none=Begin(t,f);Finish(none,dt,false);Check(none.Players.All(p=>p.FinishBoostsEarned==0),"actual whole race no actions misses "+t.Id+f.FirstAction);
                foreach(int side in new[]{0,1}){
                    var isolated=new Track(t.Length,Array.Empty<Obstacle>()){RiskZones=new[]{goal}};var g=Begin(isolated,f);
                    foreach(var p in g.Players){p.Distance=goal.X-80;p.Y=side;p.Gravity=side==0?-1:1;p.Support=SupportKind.Wall;p.SupportSide=side;}
                    Finish(g,dt,false);Check(g.Players.All(p=>p.FinishBoostsEarned==0&&p.Collisions==0),"isolated supported run-in misses each wall");
                }
                int count=t.RiskZones.Count(z=>!z.IsFinish&&!z.IsNegative&&!z.IsCoin);
                var skip=Replay(t,f,FrozenRoute(t.Id,f),dt);Check(Clean(skip,count),"frozen ordinary route clean "+t.Id+f.FirstAction);Check(skip.Game.Players.All(p=>p.FinishBoostsEarned==0),"ordinary route does NOT aim/collect");
                var take=Replay(t,f,TakeRoute(t,f),dt);TakeStats(take,count,"deliberate "+t.Id+f.FirstAction);
                var absent=Timeline(At(t,goal.X,goal.Y,false),f,take.Inputs,dt);
                Check(Clean(absent,count)&&absent.Game.Players.All(p=>p.FinishBoostsEarned==0),"same TIMED inputs no-target control");
                foreach(int i in new[]{0,1}){Near(absent.Game.Players[i].FinishTime.Value-take.Game.Players[i].FinishTime.Value,.25,"same TIMED input true .25 gain");Near(skip.Game.Players[i].FinishTime.Value-take.Game.Players[i].FinishTime.Value,.25,"clean collect/bypass gain");}
                var contact=Begin(t,f);double at=take.Game.Players[0].LastFinishBoost.At;
                foreach(var input in take.Inputs.Where(i=>i.Time<at-1e-9)){AdvanceTime(contact,input.Time-contact.Elapsed,dt);Tap(contact);}
                AdvanceTime(contact,at-contact.Elapsed,dt);var cp=contact.Players[0];double cy=RaceSimulation.GetPlayerCenterY(cp);
                Near(IndependentStrokeDistance(cp.Distance-goal.X,cy-goal.Y),15.5*15.5,"actual body contacts painted stroke, independent literal geometry",2e-5);
                Check(t.Length-cp.Distance>=110,"actual contact leaves whole pulse runway");
                var timing=new List<object>();
                foreach(double shift in new[]{-.033,0,.033}){
                    var timed=Timeline(t,f,take.Inputs,dt,shift);TakeStats(timed,count,"decisive last input shift"+shift);
                    timing.Add(new{shift,finish=timed.Game.Players[0].FinishTime});
                    if(t.Id==TracksMixed.Id){var ordinary=Timeline(t,f,skip.Inputs,dt,shift);Check(Clean(ordinary,count)&&ordinary.Game.Players.All(p=>p.FinishBoostsEarned==0),"Mixed ordinary bypass also stable +/-33ms");}
                }
                foreach(double frame in new[]{1d/30,1d/60})foreach(double phase in new[]{0d,.25,.5,.75}){
                    double eventTime=take.Inputs.Last().Time,offset=phase*frame,quantized=Math.Ceiling((eventTime-offset)/frame)*frame+offset;
                    var framed=Timeline(t,f,take.Inputs,frame,quantized-eventTime);TakeStats(framed,count,"decisive edge quantized30/60fps phase"+phase);
                }
                evidence.Add(new{Track=t.Id,Profile=f.FirstAction.ToString(),Step=dt,Target=new{goal.X,goal.Y,OuterWidth=24,OuterHeight=20,StrokeWidth=3,MinimumRunway=t.Length-goal.X-26},
                    Take=take.Game.Players[0].FinishTime,Skip=skip.Game.Players[0].FinishTime,SameTimedNoTarget=absent.Game.Players[0].FinishTime,
                    Contact=new{X=cp.Distance,Y=cy,At=at},Timing=timing,
                    TakeInputs=dt==1d/60?take.Inputs:null,SkipInputs=dt==1d/60?skip.Inputs:null});
            }
        }
        Check(sources.All(s=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(s.Key)))==s.Value),"sources unchanged during proof");
        File.WriteAllText(candidate?"Verification/fix491-candidate-proof.json":"Verification/fix491-finish-proof.json",JsonSerializer.Serialize(new{Status="PASS",Candidate=candidate,Checks=checks,Frames,SourceHashes=sources,Evidence=evidence},Json));
        Console.WriteLine("PASS FIX491 "+(candidate?"candidate":"production")+": "+checks+" checks; 5tracks/2profiles/6steps; frozen ordinary skips; deliberate takes; no-input eachwall; exact-stroke contacts; timed counterfactual .25; decisive +/-33ms and30/60fps phases");
    }
}
