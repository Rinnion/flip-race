using System;
using System.IO;
using System.Linq;
using FlipRace.Core;

static class Program
{
    static void Need(bool yes,string text) { if(!yes)throw new Exception(text); }
    static PreparedReferenceProof Run(TrackPackage p,FlightSettings f)
    {
        var proof=new PreparedReferenceProof(p,f);int slices=0;
        while(!proof.Complete&&slices++<200000)proof.Step(32);
        Need(proof.Complete,"proof did not stop");return proof;
    }
    static TrackPackage Changed(TrackPackage p,string id,bool forged=false)
    {
        TrackJson.TryRead(TrackJson.Write(p),out var clone,out _);
        clone.Track.Id=id;clone.Track.Name="Prepared import check";clone.Track.Obstacles[0].X+=.1;clone.Track.Length+=220;
        string hash=TrackFingerprint.Compute(clone.Track,clone.ProfilePolicy,clone.WarningMode);
        return new TrackPackage(clone.Track,clone.ProfilePolicy,clone.WarningMode,p.References.Select(r=>
            new TrackBotReference(r.ProfileId,hash,forged?new[]{1.0}:r.Inputs,r.RewardIds,r.BonusGain)).ToArray());
    }
    static int Main()
    {
        try
        {
            const string folder="Assets/StreamingAssets/Tracks/";
            string manifest=File.ReadAllText(folder+"index.json");TrackJson.TryReadManifest(manifest,out var index,out _);
            var files=index.Files.ToDictionary(f=>f,f=>File.ReadAllText(folder+f));
            Need(TrackCatalog.TryInstallBuiltIns(manifest,files,out var error),error);
            var built=TrackCatalog.Entries.First(e=>e.Package.Track.Id==TracksMixed.Id).Package;
            Need(TrackCatalog.Entries.Sum(e=>e.Package.References.Length)==15,"builtin combinations changed");
            var fresh=Changed(built,"prepared-own-new-geometry");
            Need(TrackCatalog.TryImportLocal(TrackJson.Write(fresh),out var entry,out error),error);
            foreach(var flight in new[]{FlightPresets.Chosen,FlightPresets.FastFlip})
            {
                Need(!TrackCatalog.SupportsBot(fresh.Track.Id,flight),"unproven import enabled");
                var proof=entry.GetOrCreateProof(flight);int slices=0;
                while(!proof.Complete&&slices++<200000)proof.Step(32);
                Need(proof.Success,"changed track "+proof.ProfileId+": "+proof.Failure);
                Need(proof.RequiredRuns==2&&proof.CompletedRuns==2,"no-hidden duplicate configs");
                Need(TrackCatalog.SupportsBot(fresh.Track.Id,flight),"proven import still disabled");
                var game=RaceSimulation.CreateStochasticBotRace(entry.Package.Track,flight,BotDifficulty.Master,1);new StochasticBotController(game);
                Need(ReferenceEquals(proof,entry.GetOrCreateProof(flight)),"proof not cached");
                Console.WriteLine("PASS imported actual Master + reference "+proof.ProfileId);
            }
            entry.Package.Track.Obstacles[0].Width+=1;
            Need(!TrackCatalog.SupportsBot(fresh.Track.Id,FlightPresets.Chosen),"geometry mutation retained admission");
            bool denied=false;try { StochasticTrackMap.Create(entry.Package.Track,FlightPresets.Chosen); }catch(NotSupportedException){denied=true;}
            Need(denied,"mutated map factory admitted");
            var forged=Changed(built,"forged-valid-hash",true);var bad=Run(forged,FlightPresets.Chosen);
            Need(!bad.Success&&!string.IsNullOrEmpty(bad.Failure),"forged hash route admitted");
            Need(TrackCatalog.TryImportLocal(TrackJson.Write(forged),out var badEntry,out error),error);
            var badProof=badEntry.GetOrCreateProof(FlightPresets.Chosen);while(!badProof.Complete)badProof.Step(32);
            Need(!badProof.Success&&!TrackCatalog.SupportsBot(forged.Track.Id,FlightPresets.Chosen),"failed route BOT enabled");
            var human=RaceSimulation.CreateSolo(badEntry.Package.Track,FlightPresets.Chosen,PartyCamera.Lagger,-288.8,1520);
            RaceSimulation.Press(human,0);RaceSimulation.Advance(human,4);Need(human.Players[0].Distance>0,"failed proof prevents human map");
            var stale=Changed(built,"stale-after-package");stale.Track.Obstacles[0].X+=1;Need(!Run(stale,FlightPresets.Chosen).Success,"stale package admitted");
            var hidden=Changed(TrackCatalog.Entries.First(e=>e.Package.Track.Id==TracksHidden.InterceptsId).Package,"hidden-prepared-check");
            var hp=new PreparedReferenceProof(hidden,FlightPresets.Chosen);Need(hp.RequiredRuns==10,"selected hidden configs not all five");
            while(!hp.Complete)hp.Step(32);Need(hp.Success,"selected-hidden: "+hp.Failure);
            var restricted=Changed(TrackCatalog.Entries.First(e=>e.Package.Track.Id==TracksRethink.Id).Package,"restricted-prepared-check");
            Need(!Run(restricted,FlightPresets.FastFlip).Success,"restricted profile admitted");
            var authored=Run(restricted,FlightPresets.Chosen);Need(authored.Success&&authored.RequiredRuns==2,"authored: "+authored.Failure);
            Need(TrackCatalog.Entries.Where(e=>e.Origin==TrackOrigin.BuiltIn).Sum(e=>e.Package.References.Length)==15,"builtins mutated");
            Console.WriteLine("PASS forged/stale/mutation/restricted/human-only; selected-hidden 10 runs, authored 2; 15 builtin references preserved");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error);return 1; }
    }
}
