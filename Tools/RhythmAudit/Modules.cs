using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    public static string ProjectRoot=Directory.GetCurrentDirectory();
    sealed class ModuleSpec
    {
        public string Kind;public double Length,ExportedWorkEnd;public int EndSide;
        public Track Track;public readonly List<ModuleRoute> Routes=new List<ModuleRoute>();
    }
    sealed class ModuleRoute
    {public FlightSettings Flight;public bool Full;public double[] Inputs;}
    sealed class PulseRelease
    {public string Id;public double X;public bool Negative;}
    static List<PulseRelease> Releases(Track track)
    {
        var releases=new List<PulseRelease>();
        foreach(var z in track.RiskZones)
        {
            if(z.IsFinish)continue;
            double latest=z.IsLanding?z.X+z.LandingWidth/2:z.X+z.Radius+Radius;
            if(!z.IsLanding&&!z.IsInstant&&!z.IsNegative&&!z.FreeReward)
            {
                double safe=z.ClearX;bool linked=false;
                foreach(var o in track.Obstacles)if(o.Id==z.ObstacleId)
                {safe=Math.Max(safe,ObstacleGeometry.Right(o)+Radius+1e-7);linked=true;}
                Need(linked,"linked reward references a real obstacle");latest=Math.Max(latest,safe);
            }
            releases.Add(new PulseRelease{Id=z.Id,X=latest,Negative=z.IsNegative});
        }
        releases.Sort((a,b)=>a.X.CompareTo(b.X));return releases;
    }
    static double TailSubset(List<PulseRelease> releases,int mask,bool negativeWorkExact=false)
    {
        double x=0;
        for(int i=0;i<releases.Count;i++)if((mask&(1<<i))!=0)x=Math.Max(x,releases[i].X)+(negativeWorkExact&&releases[i].Negative?110:330)*.6;
        return x;
    }
    static bool NoPendingRings(PlayerState p)
    {
        foreach(string id in p.CollectedRiskIds)
            if(!p.SuccessfulRiskIds.Contains(id)&&!p.FailedRiskIds.Contains(id)&&!p.NegativeRiskIds.Contains(id))return false;
        return true;
    }
    static List<ModuleSpec> ReadModules(string fileName)
    {
        using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectRoot,"Verification",fileName))))
        {
            var result=new List<ModuleSpec>();var options=new JsonSerializerOptions{IncludeFields=true};
            foreach(var e in doc.RootElement.EnumerateArray())
            {
                var m=new ModuleSpec{Kind=e.GetProperty("kind").GetString(),Length=e.GetProperty("length").GetDouble(),EndSide=e.GetProperty("endSide").GetInt32(),ExportedWorkEnd=e.GetProperty("latestAllWorkEndX").GetDouble()};
                context="module export "+m.Kind;
                Near(e.GetProperty("endX").GetDouble(),m.Length,"explicit end boundary");
                Need(e.GetProperty("entrySide").GetInt32()==1&&e.GetProperty("entrySpeed").GetDouble()==220&&e.GetProperty("entryEffects").GetString()=="empty","declared supported clean entry");
                var obstacles=JsonSerializer.Deserialize<Obstacle[]>(e.GetProperty("obstacles").GetRawText(),options);
                var zones=JsonSerializer.Deserialize<RiskZone[]>(e.GetProperty("zones").GetRawText(),options);
                m.Track=new Track(m.Length+100,obstacles){Id=m.Kind,RiskZones=zones};
                foreach(var route in e.GetProperty("routes").EnumerateArray())
                    m.Routes.Add(new ModuleRoute{Flight=route.GetProperty("profile").GetString()=="fast-flip"?FlightPresets.FastFlip:FlightPresets.Chosen,
                        Full=route.GetProperty("full").GetBoolean(),Inputs=JsonSerializer.Deserialize<double[]>(route.GetProperty("inputs").GetRawText())});
                Need(m.Routes.Count==4,"both profiles and both route purposes exported");result.Add(m);
            }
            Need(result.Count==8,"eight module families");return result;
        }
    }
    static void AppendModule(ModuleSpec m,double at,bool mirror,string prefix,List<Obstacle> obstacles,List<RiskZone> zones)
    {
        foreach(var o in m.Track.Obstacles)
        {
            var c=new Obstacle();foreach(var field in typeof(Obstacle).GetFields(BindingFlags.Instance|BindingFlags.Public))field.SetValue(c,field.GetValue(o));
            c.Id=prefix+o.Id;c.X+=at;
            if(mirror)
            {
                if(c.Kind=="gate"){c.GapTop=Height-o.GapBottom;c.GapBottom=Height-o.GapTop;c.TopOffsetX=o.BottomOffsetX;c.BottomOffsetX=o.TopOffsetX;}
                else if(c.Kind=="floor")c.Kind="ceiling";else if(c.Kind=="ceiling")c.Kind="floor";
            }
            obstacles.Add(c);
        }
        foreach(var z in m.Track.RiskZones)
        {
            var c=new RiskZone();foreach(var field in typeof(RiskZone).GetFields(BindingFlags.Instance|BindingFlags.Public))field.SetValue(c,field.GetValue(z));
            c.Id=prefix+z.Id;if(!string.IsNullOrEmpty(z.ObstacleId))c.ObstacleId=prefix+z.ObstacleId;
            c.X+=at;if(c.ClearX!=0)c.ClearX+=at;if(c.SafeFlipX!=0)c.SafeFlipX+=at;if(c.RiskFlipX!=0)c.RiskFlipX+=at;
            if(mirror)c.Y=Height-z.Y;zones.Add(c);
        }
    }
    static void SupportedBoundary(GameState g,int side,int earned,string why)
    {
        foreach(var p in g.Players)
        {
            Need(p.Collisions==0&&p.Support==SupportKind.Wall&&p.SupportSide==side&&!p.Flipping&&p.AirFlipAvailable,why+" real wall/charge");
            Near(p.VerticalSpeed,0,why+" VY0");Near(p.CurrentSpeed,220,why+" speed220");
            Need(p.ActiveEffect==null&&p.PendingEffects.Count==0&&NoPendingRings(p),why+" no active/queued/linked work");
            Need(p.BoostsEarned==earned&&p.CompletedBoosts==earned&&p.SlowsEarned==0,why+" complete pulse ledger");
        }
    }
    static int OrdinaryCount(Track t)
    {int n=0;foreach(var z in t.RiskZones)if(!z.IsNegative&&!z.IsFinish)n++;return n;}
    static ModuleRoute GetModuleRoute(ModuleSpec m,FlightSettings f,bool full)
    {foreach(var r in m.Routes)if(r.Flight==f&&r.Full==full)return r;throw new Exception("Missing module route");}
    static void ModuleReplayIndependent(ModuleSpec first,ModuleSpec second,FlightSettings f,bool fullFirst,bool fullSecond,bool mirror,double step)
    {
        const double pad=500;var solids=new List<Obstacle>();var zones=new List<RiskZone>();
        int side=mirror?0:1,afterFirst=mirror?1-first.EndSide:first.EndSide;
        AppendModule(first,pad,mirror,"a/",solids,zones);
        if(second!=null)AppendModule(second,pad+first.Length,afterFirst==0,"b/",solids,zones);
        var g=Start(new Track(pad+first.Length+(second==null?0:second.Length)+100,solids.ToArray()){RiskZones=zones.ToArray()},f);
        if(mirror)DepartToOpposite(g,step);
        FlowTo(g,pad,step);SupportedBoundary(g,side,0,"entry");double entry=g.Elapsed;
        int earned=0;double offset=pad;
        foreach(var m in second==null?new[]{first}:new[]{first,second})
        {
            bool full=m==first?fullFirst:fullSecond; // Pair may contain same family twice: distinguish by offset below.
            if(offset>pad)full=fullSecond;
            var r=GetModuleRoute(m,f,full);
            foreach(double x in r.Inputs)
            {
                FlowTo(g,offset+x,step);
                if(f==FlightPresets.FastFlip)foreach(var p in g.Players)Need(p.Support!=SupportKind.None,"Fast module action uses real support");
                Need(TapBoth(g),"both real module actions accepted");
            }
            earned+=full?OrdinaryCount(m.Track):0;
            int endSide=side==1?m.EndSide:1-m.EndSide;
            FlowTo(g,offset+m.Length,step);SupportedBoundary(g,endSide,earned,"module exit");
            Near(g.Elapsed-entry,(offset+m.Length-pad)/220-.3*earned,"actual cumulative module duration",2e-7);
            offset+=m.Length;side=endSide;
        }
    }
    static void Modules(string fileName="rhythm-modules.json")
    {
        var modules=ReadModules(fileName);int pairs=0;
        Console.WriteLine("INPUT "+fileName);
        foreach(var m in modules)
        {
            context="independent module "+m.Kind;var releases=Releases(m.Track);double max=0;
            for(int mask=0;mask<(1<<releases.Count);mask++)max=Math.Max(max,TailSubset(releases,mask));
            Need(max<m.Length,"all-subset release envelope finishes before boundary");Near(max,m.ExportedWorkEnd,"independent exported-tail agreement");
            foreach(var o in m.Track.Obstacles){Need(ObstacleGeometry.Left(o)-Radius>0,"solid begins after entry");Need(ObstacleGeometry.Right(o)+Radius<m.Length,"both solid bands cleared before exit");}
            foreach(var z in m.Track.RiskZones)Need(z.X-(z.IsLanding?z.LandingWidth/2:z.Radius+Radius)>0,"no next-module reward touches prior boundary");
            foreach(var f in Profiles)foreach(bool full in new[]{false,true})foreach(bool mirror in new[]{false,true})foreach(double step in Steps)
            {context=m.Kind+" "+Name(f)+" full="+full+" mirror="+mirror+" dt="+step;ModuleReplayIndependent(m,null,f,full,false,mirror,step);}
            Console.WriteLine("MODULE "+m.Kind+" boundary="+m.Length+" worstOrdinaryEnd="+max.ToString("R")+" reserve="+(m.Length-max).ToString("R"));
        }
        foreach(var first in modules)foreach(var second in modules)foreach(var f in Profiles)
        foreach(bool fullFirst in new[]{false,true})foreach(bool fullSecond in new[]{false,true})foreach(bool mirror in new[]{false,true})foreach(double step in new[]{.317,100})
        {
            context="composed pair "+first.Kind+"→"+second.Kind+" "+Name(f)+" rewards="+fullFirst+"/"+fullSecond+" mirror="+mirror+" dt="+step;
            ModuleReplayIndependent(first,second,f,fullFirst,fullSecond,mirror,step);pairs++;
        }
        Console.WriteLine("PASS independent module interface/composition: "+checks+" assertions / "+games+" games; "+pairs+" actual two-module concatenations; eight families, both profiles/mirrors/safe/full mixtures, real support/charge, empty effects/pending, unique instance IDs. Routes are exported author witnesses; this is not independent route discovery or full-track density/recovery proof.");
    }
}
