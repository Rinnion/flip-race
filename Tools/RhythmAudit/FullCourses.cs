using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    // Adapter data only; these helpers never activate TracksRhythm.All or edit
    // production tracks. A draft API/export will provide the frozen witnesses.
    public sealed class CourseGroup
    {public string Name;public double FromX,ToX;}
    public sealed class CourseWitness
    {
        public Track Track;public FlightSettings Flight;public string Purpose;
        public double[] Inputs;public int ExpectedNegative=1;
        public CourseGroup[] Groups=Array.Empty<CourseGroup>();
    }
    public static string CourseFile="rhythm-course-r2.json";
    static int courseShorteningFindings;
    static List<CourseWitness> ReadCourses()
    {
        using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectRoot,"Verification",CourseFile))))
        {
            var result=new List<CourseWitness>();var options=new JsonSerializerOptions{IncludeFields=true};
            foreach(var entry in doc.RootElement.EnumerateArray())
            {
                if(!entry.TryGetProperty("track",out var source))
                {
                    string id=entry.GetProperty("id").GetString();var finalTrack=new Track(entry.GetProperty("length").GetDouble(),
                        JsonSerializer.Deserialize<Obstacle[]>(entry.GetProperty("obstacles").GetRawText(),options))
                        {Id=id,RiskZones=JsonSerializer.Deserialize<RiskZone[]>(entry.GetProperty("zones").GetRawText(),options)};
                    var finalGroups=new List<CourseGroup>();
                    foreach(var group in TracksRhythm.Groups(id))finalGroups.Add(new CourseGroup{Name="group-"+group.Number,FromX=group.StartX,ToX=group.EndX});
                    foreach(string purpose in new[]{"safe","full","trap"})
                        result.Add(new CourseWitness{Track=finalTrack,Flight=entry.GetProperty("profile").GetString()=="fast-flip"?FlightPresets.FastFlip:FlightPresets.Chosen,
                            Purpose=purpose,Inputs=JsonSerializer.Deserialize<double[]>(entry.GetProperty(purpose=="trap"?"negative":purpose).GetRawText()),Groups=finalGroups.ToArray()});
                    continue;
                }
                var track=new Track(source.GetProperty("Length").GetDouble(),
                    JsonSerializer.Deserialize<Obstacle[]>(source.GetProperty("Obstacles").GetRawText(),options))
                    {Id=entry.GetProperty("id").GetString(),RiskZones=JsonSerializer.Deserialize<RiskZone[]>(source.GetProperty("RiskZones").GetRawText(),options)};
                var groups=new SortedDictionary<int,CourseGroup>();
                foreach(var module in entry.GetProperty("modules").EnumerateArray())
                {
                    int id=module.GetProperty("group").GetInt32();double a=module.GetProperty("x").GetDouble(),b=module.GetProperty("endX").GetDouble();
                    if(!groups.ContainsKey(id))groups[id]=new CourseGroup{Name="group-"+id,FromX=a,ToX=b};
                    else {groups[id].FromX=Math.Min(groups[id].FromX,a);groups[id].ToX=Math.Max(groups[id].ToX,b);}
                }
                foreach(var route in entry.GetProperty("routes").EnumerateArray())
                {
                    var inputs=new List<double>();foreach(var input in route.GetProperty("route").EnumerateArray())inputs.Add(input.GetProperty("X").GetDouble());
                    result.Add(new CourseWitness{Track=track,Flight=route.GetProperty("profile").GetString()=="fast-flip"?FlightPresets.FastFlip:FlightPresets.Chosen,
                        Purpose=route.GetProperty("variant").GetString(),Inputs=inputs.ToArray(),Groups=new List<CourseGroup>(groups.Values).ToArray()});
                }
            }
            return result;
        }
    }
    sealed class CourseAction
    {
        public int Index;public double X,Time,Y,VY;public SupportKind Support;
        public int SupportSide;public bool Air,Charge;public bool Accepted;
    }
    sealed class CourseObservation
    {
        public GameState Game;public readonly List<CourseAction> Actions=new List<CourseAction>();
        public readonly Dictionary<double,double> BoundaryTimes=new Dictionary<double,double>();
    }
    public static void CheckFullBudget(Track track,int expectedNegative=2)
    {
        context="full budget "+track.Id;Near(track.Length,7920,"base length");
        var ids=new HashSet<string>();
        foreach(var o in track.Obstacles)
        {
            Need(!string.IsNullOrEmpty(o.Id)&&ids.Add(o.Id),"unique authored obstacle ID");
            Need(ObstacleGeometry.Right(o)+Radius<7876,"whole collider outside final44");
            if(o.Kind=="gate")Need(o.GapTop>0&&o.GapBottom<Height,"both gate halves present");
        }
        ids.Clear();int positive=0,negative=0,finish=0,landing=0,linked=0,after=0;
        foreach(var z in track.RiskZones)
        {
            Need(!string.IsNullOrEmpty(z.Id)&&ids.Add(z.Id),"unique authored risk ID");
            if(z.IsFinish){finish++;Near(z.X,7876,"finish plane X");Near(z.FinishHeight,16,"unchanged target height");continue;}
            if(z.IsNegative)negative++;else positive++;
            if(z.IsLanding)landing++;
            if(!z.IsLanding&&!z.IsInstant&&!z.IsNegative&&!z.FreeReward){linked++;if(z.AfterObstacle)after++;}
            Need(z.X+(z.IsLanding?z.LandingWidth/2:z.Radius+Radius)<7876,"ordinary contact window outside final44");
            if(!string.IsNullOrEmpty(z.ObstacleId))Need(Array.Exists(track.Obstacles,o=>o.Id==z.ObstacleId),"module link remapped to existing obstacle");
        }
        Need(positive==9&&negative==expectedNegative&&finish==1,"9 ordinary /"+expectedNegative+" negative /1 finish budget");
        Need(landing==3&&linked==2&&after==1,"three landings plus before/after linked pair");
        var releases=Releases(track);Need(releases.Count==9+expectedNegative,"potential ordinary pulse count");
        double worst=0;int worstMask=0;
        int subsets=1<<releases.Count;
        for(int mask=0;mask<subsets;mask++)
        {
            // While active, a negative .6s pulse consumes exactly 66 units of
            // travel, a positive consumes198. Preemption conserves remaining
            // work; collisions freeze both service and X. Sorted latest-release
            // envelopes bound arbitrary subsets/order without flushing effects.
            double end=TailSubset(releases,mask,true);
            Need(end<7876-1e-7,"subset "+mask+" natural tail before finish");
            if(end>worst){worst=end;worstMask=mask;}
        }
        Console.WriteLine("BUDGET "+track.Id+" negative="+negative+" subsets="+subsets+" conservativeEnd="+worst.ToString("R")+" margin="+(7876-worst).ToString("R")+" worstMask="+worstMask);
    }
    static void CourseMove(CourseObservation result,double x,double step,SortedSet<double> boundaries)
    {
        foreach(double boundary in boundaries)
            if(!result.BoundaryTimes.ContainsKey(boundary)&&boundary<=x&&boundary>=result.Game.Players[0].Distance-1e-7)
            {FlowTo(result.Game,boundary,step);result.BoundaryTimes[boundary]=result.Game.Elapsed;}
        FlowTo(result.Game,x,step);
    }
    static CourseObservation ObserveCourse(CourseWitness witness,double step,int omit=-1,int omitSecond=-1,
        int changed=-1,double shift=0,double[] originalTimes=null)
    {
        var r=new CourseObservation{Game=Start(witness.Track,witness.Flight)};
        var boundaries=new SortedSet<double>{0,7876-1e-5,witness.Track.Length};
        foreach(var group in witness.Groups){boundaries.Add(group.FromX);boundaries.Add(group.ToX);}
        r.BoundaryTimes[0]=0;
        for(int i=0;i<witness.Inputs.Length&&r.Game.Phase=="racing";i++)
        {
            if(i==omit||i==omitSecond)continue;
            if(i==changed)
            {
                Need(originalTimes!=null&&originalTimes[i]+shift>=r.Game.Elapsed-1e-8,"actual-time shifted input preserves order");
                Until(r.Game,originalTimes[i]+shift,step);
            }
            else CourseMove(r,witness.Inputs[i],step,boundaries);
            var p=r.Game.Players[0];
            var action=new CourseAction{Index=i,X=p.Distance,Time=r.Game.Elapsed,Y=RaceSimulation.GetPlayerCenterY(p),VY=p.VerticalSpeed,
                Support=p.Support,SupportSide=p.SupportSide,Air=p.Flipping,Charge=p.AirFlipAvailable};
            bool a=RaceSimulation.Press(r.Game,0),b=RaceSimulation.Press(r.Game,1);Need(a==b,"same-input players accept independently and equally");action.Accepted=a;r.Actions.Add(action);
        }
        CourseMove(r,7876-1e-5,step,boundaries);
        // Keep diagnostics for failed/shortened witnesses instead of declaring
        // failure here; the completed outcome decides whether removal matters.
        CourseMove(r,witness.Track.Length,step,boundaries);Finish(r.Game,step);return r;
    }
    static bool CourseGoal(CourseObservation r,CourseWitness witness)
    {
        int ordinary=witness.Purpose=="safe"?0:9,slows=witness.Purpose=="trap"?witness.ExpectedNegative:0,finish=witness.Purpose=="safe"?0:1;
        double expected=36-.3*ordinary+.3*slows-.1*finish;
        foreach(var p in r.Game.Players)
            if(p.Collisions!=0||!p.FinishTime.HasValue||Math.Abs(p.FinishTime.Value-expected)>2e-6||Math.Abs(p.Distance-7920)>2e-6
                ||p.BoostsEarned!=ordinary||p.CompletedBoosts!=ordinary||p.SlowsEarned!=slows||p.CompletedSlows!=slows
                ||p.FinishBoostsEarned!=finish||p.FinishBoost||p.ActiveEffect!=null||p.PendingEffects.Count!=0||!NoPendingRings(p))return false;
        return Math.Abs(r.Game.Elapsed-expected)<2e-6;
    }
    static void DescribeCourse(CourseObservation r,CourseWitness witness)
    {
        var accepted=r.Actions.FindAll(a=>a.Accepted);double min=double.PositiveInfinity,max=0;
        for(int i=1;i<accepted.Count;i++){double d=accepted[i].Time-accepted[i-1].Time;min=Math.Min(min,d);max=Math.Max(max,d);}
        Console.WriteLine("CLOCK "+witness.Track.Id+" "+Name(witness.Flight)+" "+witness.Purpose+" accepted="+accepted.Count+" ignored="+(r.Actions.Count-accepted.Count)
            +" first="+(accepted.Count==0?0:accepted[0].Time).ToString("R")+" minInterval="+min.ToString("R")+" maxInterval="+max.ToString("R")
            +" tail="+(r.Game.Elapsed-(accepted.Count==0?0:accepted[accepted.Count-1].Time)).ToString("R"));
        foreach(var group in witness.Groups)
        {
            var actions=accepted.FindAll(a=>a.X>=group.FromX&&a.X<group.ToX);double actualStart=r.BoundaryTimes[group.FromX],actualEnd=r.BoundaryTimes[group.ToX];
            Console.WriteLine("GROUP "+group.Name+" X="+group.FromX+".."+group.ToX+" actualClock="+actualStart.ToString("R")+".."+actualEnd.ToString("R")+" accepted="+actions.Count);
        }
        foreach(var a in accepted)Console.WriteLine("ACTION "+a.Index+" X="+a.X.ToString("R")+" t="+a.Time.ToString("R")+" support="+a.Support+" side="+a.SupportSide+" air="+a.Air+" charge="+a.Charge);
    }
    public static void CheckFullWitness(CourseWitness witness,bool shorten=true)
    {
        Need(witness.Purpose=="safe"||witness.Purpose=="full"||witness.Purpose=="trap","declared witness purpose");
        CourseObservation baseline=null;
        foreach(double step in Steps)
        {
            context=witness.Track.Id+" "+Name(witness.Flight)+" "+witness.Purpose+" dt="+step;
            baseline=ObserveCourse(witness,step);Need(CourseGoal(baseline,witness),"actual goal and fully served ledger");
            Need(baseline.Actions.TrueForAll(a=>a.Accepted),"nominal actions are actual accepted gameplay input");
        }
        DescribeCourse(baseline,witness);
        double[] clocks=baseline.Actions.ConvertAll(a=>a.Time).ToArray();
        if(witness.Purpose!="trap")for(int i=0;i<witness.Inputs.Length;i++)foreach(int sign in new[]{-1,1})
        {
            context=witness.Track.Id+" "+Name(witness.Flight)+" "+witness.Purpose+" input="+i+" window";
            var r=ObserveCourse(witness,1d/144,changed:i,shift:sign*(witness.Purpose=="safe"?.06:.025),originalTimes:clocks);
            Need(CourseGoal(r,witness),"individual actual-time window");
        }
        if(shorten&&witness.Purpose!="trap")
        {
            int cleanShorter=0;
            for(int i=0;i<witness.Inputs.Length;i++)
            {
                context=witness.Track.Id+" "+Name(witness.Flight)+" "+witness.Purpose+" remove="+i;
                var removed=ObserveCourse(witness,.317,omit:i);
                if(CourseGoal(removed,witness)){cleanShorter++;Console.WriteLine("CLEAN_SHORTER "+context+" single="+i+" X="+witness.Inputs[i]+" accepted="+removed.Actions.FindAll(a=>a.Accepted).Count+" finish="+removed.Game.Players[0].FinishTime+" hits="+removed.Game.Players[0].Collisions);}
                if(i+1<witness.Inputs.Length)
                {
                    var pair=ObserveCourse(witness,.317,omit:i,omitSecond:i+1);
                    if(CourseGoal(pair,witness)){cleanShorter++;Console.WriteLine("CLEAN_SHORTER "+context+" pair="+i+","+(i+1)+" X="+witness.Inputs[i]+","+witness.Inputs[i+1]+" accepted="+pair.Actions.FindAll(a=>a.Accepted).Count+" finish="+pair.Game.Players[0].FinishTime+" hits="+pair.Game.Players[0].Collisions);}
                }
            }
            courseShorteningFindings+=cleanShorter;
        }
    }
    static void CoursesDraft(bool final=false)
    {
        courseShorteningFindings=0;
        Console.WriteLine((final?"FINAL GEOMETRY CPU SNAPSHOT ":"POSITIVE-ONLY COURSE SNAPSHOT ")+CourseFile+"; browser and human comfort are separate gates.");
        var seen=new HashSet<string>();
        foreach(var witness in ReadCourses())
        {
            if(seen.Add(witness.Track.Id)){CheckFullBudget(witness.Track,final?2:0);if(final)ValidateRuntimeCourse(witness.Track);}
            foreach(var group in witness.Groups)Need(group.FromX>=0&&group.ToX>group.FromX&&group.ToX<7876,"valid group extent");
            CheckFullWitness(witness);
        }
        Console.WriteLine((courseShorteningFindings==0?"PASS":"REVIEW REQUIRED")+" independent "+(final?"final":"positive-only")+" courses: "+checks+" assertions / "+games+" games; actual goals and windows passed; clean shortening findings="+courseShorteningFindings+". Not a global minimality proof or release verdict.");
    }
    static void ValidateRuntimeCourse(Track track)
    {
        var draft=typeof(TracksRhythm).GetMethod("Draft");Need(draft!=null,"runtime Draft API exists");
        var actual=(Track)draft.Invoke(null,new object[]{track.Id});var options=new JsonSerializerOptions{IncludeFields=true};
        Near(actual.Length,track.Length,"export/runtime length");
        Need(JsonSerializer.Serialize(actual.Obstacles,options)==JsonSerializer.Serialize(track.Obstacles,options),"export matches all runtime physical obstacle fields");
        Need(JsonSerializer.Serialize(actual.RiskZones,options)==JsonSerializer.Serialize(track.RiskZones,options),"export matches all runtime reward fields");
    }
    static void CoursesBypass()
    {
        int samples=0,clean=0;var seen=new HashSet<string>();
        foreach(var witness in ReadCourses())
        {
            if(!seen.Add(witness.Track.Id+Name(witness.Flight)))continue;
            foreach(int mode in new[]{0,1,2,3})
            {
                var r=Controller(witness.Track,witness.Flight,0,0,mode);samples++;
                if(NoHit(r)){clean++;Console.WriteLine("CLEAN_BYPASS "+witness.Track.Id+" "+Name(witness.Flight)+" controller="+mode+" accepted="+r.Accepted/2);}
            }
            foreach(double period in new[]{.25,.35,.45,.6,.8,1.0,1.2,1.5})foreach(double phase in new[]{0,.11,.23,.41})
            {
                var r=Controller(witness.Track,witness.Flight,period,phase,0);samples++;
                if(NoHit(r)){clean++;Console.WriteLine("CLEAN_BYPASS "+witness.Track.Id+" "+Name(witness.Flight)+" period="+period+" phase="+phase+" accepted="+r.Accepted/2);}
            }
        }
        Need(clean==0,"bounded controls found "+clean+" clean bypasses");
        Console.WriteLine("PASS independent course bypass: "+checks+" assertions / "+samples+" real games, 36 controllers per track/profile; not a global minimality proof.");
    }
}
