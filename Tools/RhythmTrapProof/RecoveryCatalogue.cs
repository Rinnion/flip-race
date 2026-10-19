using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static Track ShiftFuture(Track source,double from,double dx)
    {
        var track=new Track(source.Length+dx,source.Obstacles.Select(o=>{var c=new Obstacle();foreach(var f in typeof(Obstacle).GetFields())f.SetValue(c,f.GetValue(o));if(ObstacleGeometry.Left(o)>=from)c.X+=dx;return c;}).ToArray()) {Id=source.Id,RiskZones=source.RiskZones.Select(z=>{var c=new RiskZone();foreach(var f in typeof(RiskZone).GetFields())f.SetValue(c,f.GetValue(z));if(c.X>=from)c.X+=dx;if(c.ClearX>=from)c.ClearX+=dx;return c;}).ToArray()};
        return track;
    }
    static void RecoveryCatalogue()
    {
        var requests=JsonSerializer.Deserialize<RecoveryRequest[]>(File.ReadAllText("Verification/rhythm-r5-recovery-no-found.json"),CourseJson);
        var baselines=new Dictionary<string,CourseResult>();var rows=new List<object>();int cases=0,failed=0,bareCount=0,wallCount=0,beforeCount=0;
        foreach(var r in requests) {
            var source=R5Track(r.track);var hitObstacle=source.Obstacles.Single(o=>o.Id==r.hit);
            var next=source.Obstacles.Where(o=>ObstacleGeometry.Left(o)>ObstacleGeometry.Left(hitObstacle)).OrderBy(ObstacleGeometry.Left).First();
            bool topFirst=next.TopOffsetX<next.BottomOffsetX;double y=topFirst?r.Y:240-r.Y,v=topFirst?r.VY:-r.VY;
            bool bare=r.support=="None"&&Math.Abs(y-93.037545)<.001&&Math.Abs(v+133.32386)<.001;
            bool before=r.support=="None"&&Math.Abs(y-83.45055)<.001&&Math.Abs(v+174.57955)<.001;
            bool wall=r.support=="Wall";if(!bare&&!before&&!wall)continue;
            cases++;if(bare)bareCount++;else if(before)beforeCount++;else wallCount++;
            double dx=bare?30:0,from=ObstacleGeometry.Left(next),target=ObstacleGeometry.Right(next)+dx+15;var track=ShiftFuture(source,from,dx);
            var route=R5Route(r.track,FlightPresets.Chosen,r.route);
            string key=r.track+"/"+r.route;if(!baselines.ContainsKey(key))baselines[key]=RunCourse(source,FlightPresets.Chosen,route,1d/144);
            var hit=RecoveryPrefix(track,route,baselines[key].Times,r.input,r.shift);Check(hit!=null&&hit.Players[0].LastHit.Id==r.hit,"actual catalogue hit ID");
            var reaction=Clone(hit);AdvanceTime(reaction,reaction.Players[0].Recovering+.15,.001);
            Near(reaction.Players[0].Distance,r.reactionX,"actual catalogue reaction X",5e-5);Near(RaceSimulation.GetPlayerCenterY(reaction.Players[0]),r.Y,"actual catalogue reaction Y",5e-5);
            GameState end;List<InputSample> samples;var witnesses=new List<object>();bool good=true;
            if(bare)foreach(double first in new[]{0d,.005,.01,.015,.02}){
                var offsets=new List<double>();if(!AdaptiveWallRecovery(reaction,first,0,.05,target,offsets)){good=false;break;}
                foreach(double step in Steps)Check(RecoveryTry(reaction,offsets.ToArray(),target,out end,out samples,step),"adaptive recovery six dt");
                witnesses.Add(new{firstInputSeconds=first,offsetsSeconds=offsets});
            }
            else {
                var catalogue=before?new[]{new[]{.275,.325,.775,.825},new[]{.275,.325,.775,.9}}:
                    new[]{new[]{.1,.25,.7,.875},new[]{0d,.15,.6,.75},new[]{.02,.24,.66},new[]{.15,.3,.75,.925}};
                double[] selected=null;
                foreach(var candidate in catalogue){var late=(double[])candidate.Clone();late[0]+=.02;
                    if(RecoveryTry(reaction,candidate,target,out end,out samples)&&RecoveryTry(reaction,late,target,out end,out samples)){selected=candidate;break;}}
                if(selected==null)good=false;
                else foreach(double lateBy in new[]{0d,.005,.01,.015,.02}){
                    var candidate=(double[])selected.Clone();candidate[0]+=lateBy;
                    foreach(double step in Steps)Check(RecoveryTry(reaction,candidate,target,out end,out samples,step),"catalogue first20ms and six dt");
                    witnesses.Add(new{firstInputSeconds=candidate[0],offsetsSeconds=candidate});
                }
            }
            if(!good){failed++;Console.WriteLine($"NO_FOUND_CATALOGUE {r.track}/{r.route} input{r.input} shift{r.shift} class={(bare?"bare-air":before?"before-exit":"wall")}");}
            rows.Add(new{r.track,r.profile,r.route,changedInput=r.input,shiftSeconds=r.shift,geometrySuffixFrom=from,geometrySuffixDelta=dx,targetX=target,found=good,kind=bare?"bare-air":before?"before-exit":"wall",witnesses});
            if(cases%20==0)Console.WriteLine($"CATALOGUE {cases} / no-found{failed}");
        }
        File.WriteAllText("Verification/rhythm-recovery-catalogue-witnesses.json",JsonSerializer.Serialize(rows,CourseJson));
        Console.WriteLine($"CATALOGUE COMPLETE {cases} actual modified prefixes ({wallCount}wall/{bareCount}bare-air/{beforeCount}before-exit), no-found{failed}, {checks} checks. Tool-only candidate; runtime unchanged.");
    }
}
