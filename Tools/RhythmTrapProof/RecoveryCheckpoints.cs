using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static Track CheckpointTrack(string file,string id)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText("Verification/"+file));
        var row=doc.RootElement.EnumerateArray().First(x=>x.GetProperty("id").GetString()==id);
        return new Track(row.GetProperty("length").GetDouble(),JsonSerializer.Deserialize<Obstacle[]>(row.GetProperty("obstacles").GetRawText(),CourseJson))
        {Id=id,RiskZones=JsonSerializer.Deserialize<RiskZone[]>(row.GetProperty("zones").GetRawText(),CourseJson),NegativeEnabled=true};
    }
    static double[] CheckpointRoute(string file,string id,FlightSettings flight,string variant)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText("Verification/"+file));string profile=flight==FlightPresets.FastFlip?"fast-flip":"chosen";
        var row=doc.RootElement.EnumerateArray().First(x=>x.GetProperty("id").GetString()==id&&x.GetProperty("profile").GetString()==profile);
        return JsonSerializer.Deserialize<double[]>(row.GetProperty(variant=="trap"?"negative":variant).GetRawText());
    }
    static Track R5Track(string id)=>CheckpointTrack("rhythm-final-routes.json",id);
    static double[] R5Route(string id,FlightSettings flight,string variant)=>CheckpointRoute("rhythm-final-routes.json",id,flight,variant);
}
