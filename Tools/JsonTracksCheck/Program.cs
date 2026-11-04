using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlipRace.Core;

static class Program
{
    const string Fixture="Tools/JsonTracksCheck/accepted-14e22ba.json";
    const string FixtureSha="ABA65005AE1A4A4CD2CC6ED00013A413235AD440E47C5A02D69BC762CBB6F704";
    static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    static int checks;static readonly List<object> Negatives=new();static readonly List<object> Races=new();
    static void Need(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    static void Near(double actual,double expected,string reason,double tolerance=2e-7){Need(Math.Abs(actual-expected)<tolerance,reason+": "+actual+" vs "+expected);}
    static Dictionary<string,string> Sources()=>Directory.GetFiles("Assets/Scripts/Core","*.cs").OrderBy(p=>p).ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    static void Exact(JsonElement expected,JsonElement actual,string path)
    {
        Need(expected.ValueKind==actual.ValueKind,path+" type");
        switch(expected.ValueKind){
            case JsonValueKind.Object:foreach(var p in expected.EnumerateObject()){Need(actual.TryGetProperty(p.Name,out _),path+" property "+p.Name);Exact(p.Value,actual.GetProperty(p.Name),path+"."+p.Name);}break;
            case JsonValueKind.Array:Need(expected.GetArrayLength()==actual.GetArrayLength(),path+" count");for(int i=0;i<expected.GetArrayLength();i++)Exact(expected[i],actual[i],path+"["+i+"]");break;
            case JsonValueKind.Number:Need(expected.GetDouble()==actual.GetDouble(),path+" exact double");break;
            case JsonValueKind.String:Need(expected.GetString()==actual.GetString(),path+" string");break;
            default:Need(expected.GetRawText()==actual.GetRawText(),path+" value");break;
        }
    }
    static TrackPackage Package(string id="json-custom",bool authored=false)
    {
        var stone=new Obstacle{Id="custom-stone",Kind="floor",X=600,Width=64,Height=48,Hidden=authored,
            HiddenRevealDistance=authored?(double?)220:null,HiddenWarningDuration=authored?(double?).35:null};
        var t=new Track(7920,new[]{stone}){Id=id,Name="Импорт 🌿",Focus="JSON acceptance"};
        return new TrackPackage(t,authored?TrackProfilePolicy.ChosenOnly:TrackProfilePolicy.Any,authored?TrackWarningMode.Authored:TrackWarningMode.Selected);
    }
    static string Mutate(string json,Action<JsonObject> mutate){var root=JsonNode.Parse(json).AsObject();mutate(root);return root.ToJsonString();}
    static string CatalogStamp()=>string.Join("|",TrackCatalog.Entries.Select(e=>e.Origin+":"+e.Package.Track.Id+":"+e.Package.ContentHash));
    static void Rejected(string name,string json)
    {
        string stamp=CatalogStamp();bool installed=TrackCatalog.IsInstalled;
        bool ok=TrackJson.TryRead(json,out var package,out var error);
        Need(!ok&&package==null&&!string.IsNullOrWhiteSpace(error),"invalid JSON accepted: "+name);
        Need(!TrackCatalog.TryImportLocal(json,out var entry,out var importError)&&entry==null&&!string.IsNullOrWhiteSpace(importError),"invalid import accepted: "+name);
        Need(stamp==CatalogStamp()&&installed==TrackCatalog.IsInstalled,"invalid import mutated catalogue: "+name);
        Negatives.Add(new{name,error});
    }
    static void NegativeTable(string plain,string builtin)
    {
        foreach(string text in new[]{"","{","null","[]",plain+" trailing"})Rejected("syntax/root",text);
        Rejected("duplicate JSON key",plain.Replace("\"schemaVersion\": 1","\"schemaVersion\": 1, \"schemaVersion\": 1"));
        foreach(string key in new[]{"schemaVersion","physicsVersion"}){
            Rejected(key+" unsupported",Mutate(plain,r=>r[key]=2));Rejected(key+" null",Mutate(plain,r=>r[key]=null));}
        Rejected("unknown policy",Mutate(plain,r=>r["profilePolicy"]="unlimited"));
        Rejected("unknown warning mode",Mutate(plain,r=>r["warningMode"]="personal"));
        Rejected("authored without hidden",Mutate(plain,r=>r["warningMode"]="authored"));
        foreach(string key in new[]{"id","name","length","obstacles"})Rejected("required null "+key,Mutate(plain,r=>r["track"][key]=null));
        Rejected("negative length",Mutate(plain,r=>r["track"]["length"]=-1));
        Rejected("nonfinite length",plain.Replace("\"length\": 7920","\"length\": 1e309"));
        Rejected("null obstacle",Mutate(plain,r=>r["track"]["obstacles"][0]=null));
        Rejected("unknown obstacle kind",Mutate(plain,r=>r["track"]["obstacles"][0]["kind"]="flooor"));
        Rejected("null obstacle kind",Mutate(plain,r=>r["track"]["obstacles"][0]["kind"]=null));
        Rejected("zero width",Mutate(plain,r=>r["track"]["obstacles"][0]["width"]=0));
        Rejected("out of bounds band",Mutate(plain,r=>r["track"]["obstacles"][0]["x"]=7910));
        Rejected("duplicate obstacle ID",Mutate(plain,r=>r["track"]["obstacles"].AsArray().Add(r["track"]["obstacles"][0].DeepClone())));
        Rejected("unknown reward kind",Mutate(plain,r=>r["track"]["riskZones"]=new JsonArray(new JsonObject{["id"]="bad",["kind"]="loot",["x"]=100,["y"]=100})));
        Rejected("missing linked stone",Mutate(plain,r=>r["track"]["riskZones"]=new JsonArray(new JsonObject{["id"]="pad",["kind"]="landing",["obstacleId"]="absent",["landingWidth"]=32,["x"]=632,["y"]=192})));
        Rejected("zero authored lead",Mutate(plain,r=>r["track"]["obstacles"][0]["hiddenRevealDistance"]=0));
        Rejected("too short warning",Mutate(plain,r=>r["track"]["obstacles"][0]["hiddenWarningDuration"]=1d/240));
        Rejected("null optional array",Mutate(plain,r=>r["track"]["riskZones"]=null));
        Rejected("stale reference after geometry edit",Mutate(builtin,r=>r["track"]["obstacles"][0]["x"]=r["track"]["obstacles"][0]["x"].GetValue<double>()+.001));
        Rejected("stale reference after policy edit",Mutate(builtin,r=>r["profilePolicy"]="chosen-only"));
        Rejected("missing selected goal",Mutate(builtin,r=>r["botReferences"][0]["rewardIds"][0]="not-present"));
        Rejected("unordered reference inputs",Mutate(builtin,r=>r["botReferences"][0]["inputs"][1]=r["botReferences"][0]["inputs"][0].DeepClone()));
        Rejected("incorrect reference gain",Mutate(builtin,r=>r["botReferences"][0]["bonusGain"]=999));
    }
    static void ReferenceReplay(Track track,FlightSettings flight,StochasticTrackMap map)
    {
        var g=RaceSimulation.CreateSolo(track,flight,PartyCamera.Lagger,-288.8,1520);HiddenObstacles.ConfigureWarning(g,.8);
        RaceSimulation.Press(g,0);RaceSimulation.Advance(g,3);var p=g.Players[0];int edges=0;
        foreach(double target in map.ReferenceInputs)
        {
            int guard=0;while(target-p.Distance>2e-7&&g.Phase=="racing")
            {if(++guard>10000)throw new Exception("reference input scheduler stalled");RaceSimulation.Advance(g,Math.Min(.05,Math.Max(1e-10,(target-p.Distance)/RaceConfig.FinishBoostSpeed)));}
            Need(RaceSimulation.Press(g,0),"JSON reference input rejected "+track.Id);edges++;
        }
        while(g.Phase=="racing"&&g.Elapsed<60)RaceSimulation.Advance(g,.05);
        CheckMaster(p,map,"reference "+track.Id);
        Need(edges==map.ReferenceInputs.Length,"reference truncated");
        Races.Add(new{kind="reference",track.Id,map.ProfileId,p.FinishTime,p.Collisions,p.BoostsEarned,p.CompletedBoosts,edges});
    }
    static void CheckMaster(PlayerState p,StochasticTrackMap map,string context)
    {
        Need(p.Status==PlayerStatus.Finished&&p.Collisions==0&&p.SlowsEarned==0,context+" clean finish");
        foreach(var o in map.Opportunities.Where(o=>o.Kind!=BotOpportunityKind.Obstacle))Need(p.SuccessfulRiskIds.Contains(o.Id.Substring(o.Id.IndexOf(':')+1)),context+" goal "+o.Id);
        Need(p.CompletedBoosts>=map.Opportunities.Count(o=>o.Kind==BotOpportunityKind.Positive)&&p.FinishBoostsEarned==1,context+" complete ordinary/finish count");
        Near(p.FinishBoostElapsed,.25,context+" full finish effect");Near(p.FinishTime.Value,map.MasterTime,context+" actual reference time");
    }
    static void RuntimeMaster(Track track,FlightSettings flight,StochasticTrackMap map)
    {
        var g=RaceSimulation.CreateStochasticBotRace(track,flight,BotDifficulty.Master,0,PartyCamera.Lagger);HiddenObstacles.ConfigureWarning(g,.8);
        var c=new StochasticBotController(g);RaceSimulation.Press(g,0);c.Advance(3);
        while(g.Phase=="racing"&&g.Elapsed<65)c.Advance(1d/30);
        var p=g.Players[1];CheckMaster(p,map,"runtime "+track.Id);
        Need(c.MaximumRequestedHorizon<=1000+2e-7&&c.MaximumTrialHorizon<=1055+2e-7&&c.MaximumTrialOvershoot<=55+2e-7,"JSON runtime horizon unchanged");
        Need(c.PlanningSamples.All(s=>s.Candidates<=13)&&c.Decisions.Select(d=>d.Id).Distinct().Count()==c.Decisions.Count,"JSON runtime budget/intention unchanged");
        Races.Add(new{kind="Master",track.Id,map.ProfileId,p.FinishTime,p.Collisions,p.BoostsEarned,p.CompletedBoosts,c.PlanningCalls,c.CandidateSimulations});
        Console.WriteLine("PASS JSON Master "+track.Id+"/"+map.ProfileId+" time="+p.FinishTime);
    }
    static void AtomicCatalog(string manifest,Dictionary<string,string> files,string plain)
    {
        void BadInstall(string name,string index,Dictionary<string,string> data)
        {string before=CatalogStamp();bool installed=TrackCatalog.IsInstalled;Need(!TrackCatalog.TryInstallBuiltIns(index,data,out var error)&&!string.IsNullOrWhiteSpace(error),name+" install accepted");Need(before==CatalogStamp()&&installed==TrackCatalog.IsInstalled,name+" changed catalogue");Negatives.Add(new{name,error});}
        BadInstall("missing builtin file",manifest,new Dictionary<string,string>());
        BadInstall("malformed builtin",manifest,files.ToDictionary(p=>p.Key,p=>"<html>not JSON</html>"));
        foreach(string filename in new[]{"../bad.json","https://example.invalid/test.json"})
            BadInstall("manifest invalid path","{\"schemaVersion\":1,\"physicsVersion\":1,\"files\":[\""+filename+"\"]}",files);
        string duplicate=TrackJson.WriteManifest(new TrackManifest(new[]{"a.json","b.json"}));string first=files.First().Value;
        BadInstall("duplicate builtin IDs",duplicate,new Dictionary<string,string>{{"a.json",first},{"b.json",first}});
        string one=TrackJson.WriteManifest(new TrackManifest(new[]{"one.json"}));
        BadInstall("reserved builtin ID",one,new Dictionary<string,string>{{"one.json",TrackJson.Write(Package(TracksControl.ChallengeId))}});
        foreach(string id in new[]{TracksFull.LongId,TracksRhythm.DenseId,TracksControl.ChallengeId})
        {string before=CatalogStamp();Need(!TrackCatalog.TryImportLocal(TrackJson.Write(Package(id)),out _,out var error)&&!string.IsNullOrWhiteSpace(error),"reserved import accepted "+id);Need(before==CatalogStamp(),"reserved import changed catalogue");}
        Need(TrackCatalog.TryImportLocal(plain,out var local,out var localError),"valid local failed: "+localError);
        Need(local.Origin==TrackOrigin.Local&&RaceTracks.Get(local.Package.Track.Id)==local.Package.Track,"local lookup not authoritative");
        Need(!TrackCatalog.SupportsBot(local.Package.Track.Id,FlightPresets.Chosen)&&!StochasticTrackMap.SupportsTrack(local.Package.Track.Id),"local gained builtin BOT trust");
        Need(TrackCatalog.SupportsProfile(local.Package.Track.Id,new FlightSettings(FirstAction.Jump,MotionLaw.Gravity,AirRepeatLimit.One)),"Any policy lost custom human support");
        string localStamp=CatalogStamp();Need(!TrackCatalog.TryImportLocal(plain,out _,out _),"duplicate local import accepted");Need(CatalogStamp()==localStamp,"duplicate local mutated catalogue");
    }
    static void MetadataAndTrust(string manifest,Dictionary<string,string> files)
    {
        string firstText=files.First().Value;
        Need(TrackJson.TryRead(firstText,out var clone,out _),"clone builtin for stale-content fixture");
        clone.Track.Obstacles[0].X+=.001;
        bool staleRejected=false;try{StochasticTrackMap.Create(clone.Track,FlightPresets.Chosen);}catch(NotSupportedException){staleRejected=true;}
        Need(staleRejected,"actual mutated geometry reused builtin reference by ID");

        Need(TrackJson.TryRead(firstText,out var localCopy,out _),"clone builtin for untrusted reference fixture");
        localCopy.Track.Id="json-untrusted-reference";
        string localHash=TrackFingerprint.Compute(localCopy.Track,localCopy.ProfilePolicy,localCopy.WarningMode);
        var references=localCopy.References.Select(r=>new TrackBotReference(r.ProfileId,localHash,r.Inputs,r.RewardIds,r.BonusGain)).ToArray();
        var untrusted=new TrackPackage(localCopy.Track,localCopy.ProfilePolicy,localCopy.WarningMode,references);
        Need(TrackCatalog.TryImportLocal(TrackJson.Write(untrusted),out var imported,out var error),"valid declared local refs failed: "+error);
        Need(imported.Origin==TrackOrigin.Local&&imported.Package.References.Length>0,"untrusted fixture lost declared refs");
        Need(!TrackCatalog.SupportsBot(imported.Package.Track.Id,FlightPresets.Chosen)&&!StochasticTrackMap.SupportsTrack(imported.Package.Track.Id),"declared local refs granted BOT trust");
        bool localRejected=false;try{StochasticTrackMap.Create(imported.Package.Track,FlightPresets.Chosen);}catch(NotSupportedException){localRejected=true;}
        Need(localRejected,"BOT factory trusted local reference data");

        Need(TrackCatalog.TryImportLocal(TrackJson.Write(Package("json-authored-chosen",true)),out var authored,out error),"authored local import: "+error);
        Need(TrackCatalog.UsesAuthoredWarnings(authored.Package.Track.Id)&&TrackCatalog.SupportsProfile(authored.Package.Track.Id,FlightPresets.Chosen)
            &&!TrackCatalog.SupportsProfile(authored.Package.Track.Id,FlightPresets.FastFlip),"import metadata still depends on rethink ID");
        var selected=RaceSimulation.CreateSolo(authored.Package.Track,FlightPresets.Chosen,PartyCamera.Lagger,-288.8,1520);
        HiddenObstacles.ConfigureWarning(selected,.6);Near(selected.HiddenWarningDuration,.6,"authored import preserves global delay");
        Near(HiddenObstacles.WarningDurationFor(selected,authored.Package.Track.Obstacles[0]),.35,"authored import effective timer");
        foreach(string id in new[]{TracksFull.PracticeId,TracksRhythm.PracticeId,TracksV4.OpenId,TracksV4.ChallengeId,TracksControl.ChallengeId,TracksControl.UnlimitedId,TracksTune.ChallengeId})
            Need(TrackCatalog.SupportsProfile(id,FlightPresets.FastFlip),"legacy practice profile lost "+id);
        Need(!TrackCatalog.SupportsProfile("unknown-track",FlightPresets.Chosen),"unknown track silently gained profile support");

        Need(TrackJson.TryRead(firstText,out var noReference,out _),"clone builtin for no-reference fixture");
        noReference.Track.Obstacles[0].X+=.001;
        var changed=new TrackPackage(noReference.Track,noReference.ProfilePolicy,noReference.WarningMode);
        var noRefFiles=new Dictionary<string,string>(files);noRefFiles[files.First().Key]=TrackJson.Write(changed);
        Need(TrackCatalog.TryInstallBuiltIns(manifest,noRefFiles,out error),"explicit human-only builtin change: "+error);
        Need(!StochasticTrackMap.SupportsTrack(changed.Track.Id)&&!TrackCatalog.SupportsBot(changed.Track.Id,FlightPresets.Chosen),"changed known-ID builtin fell back to old BOT route");
        Need(RaceTracks.Get(changed.Track.Id).Obstacles[0].X==changed.Track.Obstacles[0].X,"known-ID changed builtin fell back to old geometry");

        Need(TrackJson.TryReadManifest(manifest,out var index,out _),"manifest for extension");
        const string addedFile="json-added-human.track.json";
        var extraFiles=new Dictionary<string,string>(files){{addedFile,TrackJson.Write(Package("json-index-human"))}};
        string extraManifest=TrackJson.WriteManifest(new TrackManifest(index.Files.Concat(new[]{addedFile}).ToArray()));
        Need(TrackCatalog.TryInstallBuiltIns(extraManifest,extraFiles,out error),"permanent index extension failed: "+error);
        Need(TrackCatalog.Entries.Count(e=>e.Origin==TrackOrigin.BuiltIn)==9&&TrackCatalog.TryGet("json-index-human",out var added),"ninth human builtin absent");
        TrackCatalog.TryGet("json-index-human",out added);
        Need(RaceTracks.IsFull("json-index-human")&&RaceTracks.Get("json-index-human")==added.Package.Track,"new index track not authoritative");
        Need(!StochasticTrackMap.SupportsTrack("json-index-human")&&!TrackCatalog.SupportsBot("json-index-human",FlightPresets.Chosen),"new reference-free index track gained BOT");
        var human=RaceSimulation.CreateSolo(added.Package.Track,FlightPresets.Chosen,PartyCamera.Lagger,-288.8,1520);
        Need(RaceSimulation.Press(human,0),"new index human ready");RaceSimulation.Advance(human,4);
        Need(human.Phase=="racing"&&human.Players[0].Distance>0,"new index human course cannot start");
        Need(TrackCatalog.TryGet("json-custom",out _),"builtin reload discarded session import");
    }
    static int Main(string[] args)
    {
        string mode=args.FirstOrDefault()??"--full";if(mode=="--compile"){Console.WriteLine("PASS Core codec/catalog API compile");return 0;}
        var sources=Sources();string output="Verification/v543-json-check.json";
        try
        {
            Need(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Fixture)))==FixtureSha,"accepted fixture changed");
            using var accepted=JsonDocument.Parse(File.ReadAllText(Fixture));
            Need(!TrackCatalog.HasAttemptedInstall&&RaceTracks.All.Length==8,"offline baseline boundary");
            Need(!TrackCatalog.TryInstallBuiltIns("{",null,out var startupError),"malformed initial builtin accepted");
            Need(!TrackCatalog.IsInstalled&&RaceTracks.All.Length==0,"malformed initial builtin masked by offline catalogue");
            bool failedStrict=false;try{RaceTracks.Get(TracksFull.LongId);}catch(KeyNotFoundException){failedStrict=true;}Need(failedStrict,"malformed builtin triggered legacy fallback");
            const string folder="Assets/StreamingAssets/Tracks";string manifest=File.ReadAllText(folder+"/index.json");
            Need(TrackJson.TryReadManifest(manifest,out var index,out var indexError),"manifest parse "+indexError);
            var files=index.Files.ToDictionary(f=>f,f=>File.ReadAllText(folder+"/"+f));
            Need(TrackCatalog.TryInstallBuiltIns(manifest,files,out var error),"builtin install "+error);
            var entries=TrackCatalog.Entries;Need(entries.Length==8&&TrackCatalog.IsInstalled,"eight JSON builtin entries");
            var expected=accepted.RootElement.GetProperty("tracks");
            for(int i=0;i<entries.Length;i++)
            {
                var e=entries[i];Need(e.Origin==TrackOrigin.BuiltIn,"builtin origin");
                Exact(expected[i],JsonSerializer.SerializeToElement(BaselineCapture.Geometry(e.Package.Track),Json),"track["+i+"]");
                bool chosenOnly=expected[i].GetProperty("profiles").GetArrayLength()==1;
                Need(e.Package.ProfilePolicy==(chosenOnly?TrackProfilePolicy.ChosenOnly:TrackProfilePolicy.Any),"profile metadata drift");
                Need(e.Package.WarningMode==(chosenOnly?TrackWarningMode.Authored:TrackWarningMode.Selected),"warning metadata drift");
                Need(TrackJson.TryRead(TrackJson.Write(e.Package),out var roundtrip,out var reason)&&roundtrip.ContentHash==e.Package.ContentHash,"codec roundtrip "+reason);
                Need(RaceTracks.Get(e.Package.Track.Id)==e.Package.Track&&RaceTracks.GetAt(i)==e.Package.Track,"runtime lookup bypassed JSON");
                foreach(var camera in new[]{PartyCamera.Constant,PartyCamera.Lagger}){var race=RaceSimulation.CreateSolo(e.Package.Track,FlightPresets.Chosen,camera,-288.8,1520);Near(race.PartyCameraSpeed,e.Package.Track.Length/36,"camera speed preserved");}
            }
            int pairs=0;foreach(var mapJson in accepted.RootElement.GetProperty("maps").EnumerateArray())
            {
                string id=mapJson.GetProperty("trackId").GetString();var f=mapJson.GetProperty("ProfileId").GetString()=="Chosen"?FlightPresets.Chosen:FlightPresets.FastFlip;
                var track=RaceTracks.Get(id);Exact(mapJson,JsonSerializer.SerializeToElement(BaselineCapture.BotMap(track,f),Json),"bot "+id);
                var map=StochasticTrackMap.Create(track,f);ReferenceReplay(track,f,map);if(mode!="--data")RuntimeMaster(track,f,map);pairs++;
            }
            Need(pairs==15,"fifteen migrated references");
            string plain=TrackJson.Write(Package());NegativeTable(plain,files.First().Value);AtomicCatalog(manifest,files,plain);MetadataAndTrust(manifest,files);
            Need(Sources().SequenceEqual(sources),"production source changed during acceptance");
            File.WriteAllText(output,JsonSerializer.Serialize(new{status="PASS",checks,pairs,mode,sources,fixtureSha=FixtureSha,negatives=Negatives,races=Races},Json));
            Console.WriteLine("PASS checks="+checks+", pairs="+pairs+", races="+Races.Count+", negative="+Negatives.Count);return 0;
        }
        catch(Exception failure){File.WriteAllText(output,JsonSerializer.Serialize(new{status="FAIL",checks,mode,sources,error=failure.ToString(),negatives=Negatives,races=Races},Json));Console.Error.WriteLine(failure);return 1;}
    }
}
