using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FlipRace.Core;

static class Program
{
    static int assertions;
    static void Check(bool ok,string message){assertions++;if(!ok)throw new Exception(message);}
    static void Exact(object expected,object actual,string path)
    {
        if(expected==null||actual==null){Check(expected==null&&actual==null,path+" null differs");return;}
        Check(expected.GetType()==actual.GetType(),path+" type differs");
        if(expected is double d){Check(BitConverter.DoubleToInt64Bits(d)==BitConverter.DoubleToInt64Bits((double)actual),path+" double bits differ");return;}
        if(expected is string||expected.GetType().IsValueType){Check(expected.Equals(actual),path+" scalar differs");return;}
        if(expected is IEnumerable sequence){var a=sequence.Cast<object>().ToArray();var b=((IEnumerable)actual).Cast<object>().ToArray();Check(a.Length==b.Length,path+" array length differs");for(int i=0;i<a.Length;i++)Exact(a[i],b[i],path+"["+i+"]");return;}
        foreach(var field in expected.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))Exact(field.GetValue(expected),field.GetValue(actual),path+"."+field.Name);
    }
    static double[] LegacyInputs(Track track,FlightSettings flight)
    {
        bool chosen=flight.FirstAction==FirstAction.Jump,mixed=track.Id==TracksMixed.Id,cascade=track.Id==TracksRhythm.CascadeId;
        // Explicit migration of accepted old-five Map data, independent of the
        // runtime catalogue and its new reference trust resolver.
        double[] baseRoute=mixed?TracksMixed.FullRoute(flight):cascade?TracksRhythm.FullRoute(track.Id,flight):TracksFull.FullRoute(track.Id,flight);
        var route=new List<double>(baseRoute);double aim=mixed?(chosen?7639:7650):cascade?(chosen?7758:7724):(chosen?7736:7706);
        if(mixed)route[route.Count-1]=aim;else{if(!cascade)route.RemoveAll(x=>x>=7500);route.Add(aim);}route.Sort();return route.ToArray();
    }
    static int Main(string[] args)
    {
        try{
            bool checkOnly=args.Contains("--check");string output=Path.GetFullPath(args.FirstOrDefault(a=>!a.StartsWith("--"))??"Assets/StreamingAssets/Tracks");
            var tracks=TracksFull.All.Concat(TracksRhythm.All).Concat(new[]{TracksMixed.Get()}).Concat(TracksHidden.All).Concat(new[]{TracksRethink.Get()}).ToArray();
            Check(tracks.Length==8,"Exactly eight current factory maps");var files=new List<string>();var payloads=new Dictionary<string,string>();var reports=new List<object>();int references=0;
            foreach(var track in tracks){
                var policy=track.Id==TracksRethink.Id?TrackProfilePolicy.ChosenOnly:TrackProfilePolicy.Any;
                var warning=track.Id==TracksRethink.Id?TrackWarningMode.Authored:TrackWarningMode.Selected;
                var bare=new TrackPackage(track,policy,warning);var prepared=new List<TrackBotReference>();
                foreach(var profile in new[]{("Chosen",FlightPresets.Chosen),("FastFlip",FlightPresets.FastFlip)}){
                    if(!bare.SupportsProfile(profile.Item2))continue;double[] inputs;string[] rewardIds;double gain;
                    if(BotReferenceRoutes.Supports(track.Id,profile.Item2)){var source=BotReferenceRoutes.Get(track.Id,profile.Item2);inputs=source.Inputs;rewardIds=source.RewardIds;gain=source.BonusGain;}
                    else{inputs=LegacyInputs(track,profile.Item2);rewardIds=track.RiskZones.Where(z=>!z.IsNegative&&!z.IsCoin).Select(z=>z.Id).ToArray();gain=(rewardIds.Length-1)*RaceConfig.BoostTimeGain+RaceConfig.FinishBoostDuration*(RaceConfig.FinishBoostSpeed/RaceConfig.Speed-1);}
                    prepared.Add(new TrackBotReference(profile.Item1,bare.ContentHash,inputs,rewardIds,gain));references++;
                }
                var package=new TrackPackage(track,policy,warning,prepared.ToArray());string json=TrackJson.Write(package);
                Check(TrackJson.TryRead(json,out var restored,out var error),track.Id+": "+error);Exact(track,restored.Track,track.Id);
                Check(package.ContentHash==restored.ContentHash,track.Id+" fingerprint roundtrip");Check(TrackJson.Write(restored)==json,track.Id+" canonical JSON roundtrip");
                for(int i=0;i<prepared.Count;i++){var b=restored.References[i];var a=prepared[i];Exact(a.Inputs,b.Inputs,track.Id+"/"+a.ProfileId+" inputs");Exact(a.RewardIds,b.RewardIds,track.Id+"/"+a.ProfileId+" IDs");Check(a.BonusGain==b.BonusGain,track.Id+" B");}
                string file=track.Id+".track.json";files.Add(file);payloads.Add(file,json);reports.Add(new{id=track.Id,hash=package.ContentHash,obstacles=track.Obstacles.Length,zones=track.RiskZones.Length,profiles=prepared.Select(p=>p.ProfileId).ToArray(),bytes=Encoding.UTF8.GetByteCount(json)});
            }
            Check(references==15,"Exactly fifteen accepted bot pairs");string manifest=TrackJson.WriteManifest(new TrackManifest(files.ToArray()));
            Check(TrackJson.TryReadManifest(manifest,out var restoredManifest,out var manifestError),manifestError);Exact(files.ToArray(),restoredManifest.Files,"manifest.files");payloads.Add("index.json",manifest);
            if(checkOnly){
                foreach(var pair in payloads){string path=Path.Combine(output,pair.Key);Check(File.Exists(path),"Missing JSON asset "+pair.Key);if(pair.Key=="index.json"){Check(TrackJson.TryReadManifest(File.ReadAllText(path),out var m,out var error),error);Exact(files.ToArray(),m.Files,"asset index");}
                    else{Check(TrackJson.TryRead(File.ReadAllText(path),out var p,out var error),error);Check(TrackJson.Write(p)==pair.Value,"Asset differs from accepted factory snapshot: "+pair.Key);}}
            }else{
                Directory.CreateDirectory(output);foreach(string file in payloads.Keys)if(File.Exists(Path.Combine(output,file)))throw new Exception("Refusing to overwrite authored JSON: "+file+". Use --check, or a new output directory.");
                foreach(var pair in payloads)File.WriteAllText(Path.Combine(output,pair.Key),pair.Value,new UTF8Encoding(false));
                string samples="Tools/TrackCatalogExport/Samples";Directory.CreateDirectory(samples);Check(TrackJson.TryRead(payloads[TracksMixed.Id+".track.json"],out var sample,out var sampleError),sampleError);
                sample.Track.Id="my-mixed-track";sample.Track.Name="Моя трасса";var user=new TrackPackage(sample.Track);string samplePath=Path.Combine(samples,"my-mixed-track.json");if(!File.Exists(samplePath))File.WriteAllText(samplePath,TrackJson.Write(user),new UTF8Encoding(false));
            }
            string proof="Tools/TrackCatalogExport/Verification";Directory.CreateDirectory(proof);
            File.WriteAllText(Path.Combine(proof,"migration-checks.json"),JsonSerializer.Serialize(new{schemaVersion=TrackJson.SchemaVersion,physicsVersion=TrackJson.PhysicsVersion,mode=checkOnly?"check":"explicit-migration",assertions,tracks=tracks.Length,references,exactStoredFieldRoundtrip=true,canonicalRoundtrip=true,entries=reports},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS JSON migration: {tracks.Length} maps / {references} bot references / {assertions} assertions; exact stored fields and canonical roundtrip. Mode={(checkOnly?"check":"explicit migration")}");return 0;
        }catch(Exception failure){Console.Error.WriteLine(failure.Message);return 1;}
    }
}
