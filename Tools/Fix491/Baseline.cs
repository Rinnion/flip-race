using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlipRace.Core;
internal static partial class Program
{
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
    const string BaselinePath="Verification/fix491-before.json";
    static string ValueHash(object x)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(x,Json))));
    static void SaveBaseline()
    {
        if(File.Exists(BaselinePath))throw new Exception("Frozen baseline already exists; never regenerate silently");
        File.WriteAllText(BaselinePath,JsonSerializer.Serialize(new{Revision="3214a10b0b4e95d9f547ed899279d53dc771913a",Note="Captured before ANY production track edits; solver/render-only WIP is excluded. Frozen ordinary inputs are not replaced by later production author helpers.",
            Tracks=RaceTracks.All.Select(t=>new{t.Id,t.Length,Obstacles=ValueHash(t.Obstacles),NonFinish=ValueHash(t.RiskZones.Where(z=>!z.IsFinish).ToArray())}),
            Routes=RaceTracks.All.SelectMany(t=>new[]{FlightPresets.Chosen,FlightPresets.FastFlip}.Select(f=>new{Track=t.Id,Profile=f.FirstAction.ToString(),Full=Ordinary(t,f),
                Safe=t.Id==TracksMixed.Id?TracksMixed.SafeRoute(f):TracksRhythm.IsFull(t.Id)?TracksRhythm.SafeRoute(t.Id,f):TracksFull.SafeRoute(t.Id,f)}))},Json));
        Console.WriteLine("Frozen baseline saved before track edits");
    }
    static double[] FrozenMixed(FlightSettings flight,bool full)=>FrozenRoute(TracksMixed.Id,flight,full);
    static double[] FrozenRoute(string track,FlightSettings flight,bool full=true)
    {
        using var d=JsonDocument.Parse(File.ReadAllText(BaselinePath));
        return d.RootElement.GetProperty("Routes").EnumerateArray().Single(r=>r.GetProperty("Track").GetString()==track&&r.GetProperty("Profile").GetString()==flight.FirstAction.ToString()).GetProperty(full?"Full":"Safe").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
    }
}
