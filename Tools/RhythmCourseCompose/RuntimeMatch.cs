using System;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;

internal static partial class Program
{
    static void Same<T>(T expected,T actual,string label)
    { Check(JsonSerializer.Serialize(expected,Json)==JsonSerializer.Serialize(actual,Json),label); }

    static void RuntimeMatches(Course[] courses)
    {
        foreach(var c in courses) {
            var runtime=TracksRhythm.Draft(c.Id);
            Near(runtime.Length,c.Track.Length,"runtime length");
            Same(c.Track.Id,runtime.Id,"runtime id");
            Same(c.Track.Name,runtime.Name,"runtime name");
            Same(c.Track.Focus,runtime.Focus,"runtime rhythm description");
            Same(c.Track.Obstacles,runtime.Obstacles,"runtime exact physical obstacles");
            Same(c.Track.RiskZones,runtime.RiskZones.Where(z=>!z.IsNegative).ToArray(),"runtime exact positive zones");
            Same(c.Track.Cues,runtime.Cues,"runtime cues");
            Same(c.Track.SafeRoute,runtime.SafeRoute,"runtime default safe metadata");
            Same(c.Track.RiskRoute,runtime.RiskRoute,"runtime default full metadata");
            foreach(var f in Profiles) {
                Same(c.Routes[RouteKey(f,false)].Select(i=>i.X).ToArray(),TracksRhythm.SafeRoute(c.Id,f),"runtime safe witness");
                Same(c.Routes[RouteKey(f,true)].Select(i=>i.X).ToArray(),TracksRhythm.FullRoute(c.Id,f),"runtime full witness");
            }
            var groups=TracksRhythm.Groups(c.Id);
            Check(groups.Length==c.Modules.Max(m=>m.Group)+1,"runtime group count");
            for(int i=0;i<groups.Length;i++) {
                var members=c.Modules.Where(m=>m.Group==i).ToArray();
                Near(groups[i].StartX,members.First().X,"runtime group start");
                Near(groups[i].EndX,members.Last().EndX,"runtime group end");
            }
        }
        Console.WriteLine("RUNTIME MATCH PASS "+checks+" assertions; negative zones audited separately");
    }
}
