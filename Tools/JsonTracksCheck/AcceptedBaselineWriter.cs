using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Encodings.Web;
using FlipRace.Core;

static class AcceptedBaselineWriter
{
    static void Main(string[] args)
    {
        const string revision="14e22ba824a2d9195fba428bce2c42e4f93af401";
        if(args.Length!=2)throw new Exception("Arguments: snapshot output path, accepted Core directory");
        if(File.Exists(args[0]))throw new Exception("Accepted fixture already exists; regeneration is forbidden.");
        var tracks=RaceTracks.All;var maps=tracks.SelectMany(t=>new[]{FlightPresets.Chosen,FlightPresets.FastFlip}
            .Where(f=>t.Id!=TracksRethink.Id||f==FlightPresets.Chosen).Select(f=>BaselineCapture.BotMap(t,f))).ToArray();
        if(tracks.Length!=8||maps.Length!=15)throw new Exception("Accepted 8-track/15-pair inventory changed");
        var sources=Directory.GetFiles(args[1],"*.cs").OrderBy(p=>p).ToDictionary(Path.GetFileName,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var value=new{revision,sourceMethod="git archive accepted Core only; no current runtime sources compiled",sources,
            tracks=tracks.Select(BaselineCapture.Geometry).ToArray(),maps};
        File.WriteAllText(args[0],JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        Console.WriteLine("Frozen accepted baseline: tracks="+tracks.Length+", pairs="+maps.Length+", Core sources="+sources.Count);
        Console.WriteLine("SHA256 "+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))));
    }
}
