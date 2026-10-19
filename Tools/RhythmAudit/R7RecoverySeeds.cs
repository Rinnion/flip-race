using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlipRace.Core;

public static partial class RhythmIndependentAudit
{
    public static string RecoverySeedFile="";
    sealed class RecoverySeed
    {public string Policy;public double[] Taps;}
    static string RecoverySeedKey(string track,string profile,string route,int input,double shift)
    {return track+"/"+profile+"/"+route+"/"+input+"/"+shift.ToString("R",CultureInfo.InvariantCulture);}
    static Dictionary<string,RecoverySeed> LoadRecoverySeeds()
    {
        var result=new Dictionary<string,RecoverySeed>();if(string.IsNullOrEmpty(RecoverySeedFile))return result;
        if(Path.GetFileName(RecoverySeedFile)!=RecoverySeedFile)throw new ArgumentException("Seed must be a Verification filename");
        foreach(string line in File.ReadLines(Path.Combine(ProjectRoot,"Verification",RecoverySeedFile)))
        {
            using(var doc=JsonDocument.Parse(line))
            {
                var r=doc.RootElement;if(!r.GetProperty("escaped").GetBoolean())continue;
                string key=RecoverySeedKey(r.GetProperty("track").GetString(),r.GetProperty("profile").GetString(),r.GetProperty("route").GetString(),r.GetProperty("changedInput").GetInt32(),r.GetProperty("shiftSeconds").GetDouble());
                var t=r.GetProperty("offsetsSeconds");result[key]=new RecoverySeed{Policy=r.GetProperty("policy").GetString(),Taps=t.ValueKind==JsonValueKind.Null?null:JsonSerializer.Deserialize<double[]>(t.GetRawText())};
            }
        }
        Console.WriteLine("RECOVERY PROGRESS seeds="+result.Count+" file="+RecoverySeedFile+"; proposals only, every current prefix and target replayed");return result;
    }
    static bool ReplayLegacyPolicy(GameState reaction,string policy,double target,out double[] taps)
    {
        taps=null;const string prefix="legacy legal catalogue: ";if(!policy.StartsWith(prefix))return false;
        string text=policy.Substring(prefix.Length);var times=new List<double>();
        var g=Clone(reaction);var p=g.Players[0];int hits=p.Collisions;double start=g.Elapsed;
        if(text.StartsWith("stay")){taps=Array.Empty<double>();return Reach(g,target,hits);}
        const string number="([0-9.eE+\\-]+)";
        var direct=Regex.Match(text,"^tap \\+"+number+"(?:, repeat \\+"+number+")? reaches");
        if(direct.Success)
        {
            double first=double.Parse(direct.Groups[1].Value,CultureInfo.InvariantCulture);times.Add(first);
            if(direct.Groups[2].Success)times.Add(first+double.Parse(direct.Groups[2].Value,CultureInfo.InvariantCulture));
            taps=times.ToArray();return ReplayRecovery(reaction,taps,target,.025,out var ignored);
        }
        if(text.StartsWith("settling tap"))
        {
            var settle=Regex.Match(text,"^settling tap \\+"+number+"(?:, correction \\+"+number+")?");if(!settle.Success)return false;
            double first=double.Parse(settle.Groups[1].Value,CultureInfo.InvariantCulture);Until(g,start+first,.025);
            if(p.Collisions!=hits||!TapBoth(g))return false;times.Add(g.Elapsed-start);
            if(settle.Groups[2].Success)
            {
                double repeat=double.Parse(settle.Groups[2].Value,CultureInfo.InvariantCulture);Advance(g,repeat,.025);
                if(p.Collisions!=hits||!TapBoth(g))return false;times.Add(g.Elapsed-start);
            }
        }
        else if(!text.StartsWith("natural wall"))return false;
        if(!UntilRealWall(g,hits,target))return false;
        var next=OptionalNextObstacle(reaction);
        if(next==null||next.Kind!="gate"||p.ActiveEffect!=null)return false;
        var plan=Oracle(new Track(g.Track.Length,new[]{next}),g.Flight);
        if(p.SupportSide!=plan[0].OriginSide||p.Distance>plan[0].X+1e-8)return false;
        Advance(g,(plan[0].X-p.Distance)/220,.025);
        if(p.Collisions!=hits||!TapBoth(g))return false;times.Add(g.Elapsed-start);
        if(plan.Count>1)
        {Advance(g,MainRepeatDelay,.025);if(p.Collisions!=hits||!TapBoth(g))return false;times.Add(g.Elapsed-start);}
        taps=times.ToArray();return Reach(g,target,hits);
    }
    static bool TryRecoverySeed(GameState reaction,RecoverySeed seed,double target,out double[] taps,out bool late20)
    {
        taps=null;late20=false;if(seed==null)return false;
        if(seed.Taps!=null)
        {taps=seed.Taps;if(!ReplayRecovery(reaction,taps,target,.025,out var ignored))return false;}
        else if(!ReplayLegacyPolicy(reaction,seed.Policy,target,out taps))return false;
        if(taps.Length==0){late20=true;return true;}
        var delayed=(double[])taps.Clone();delayed[0]+=.02;late20=ReplayRecovery(reaction,delayed,target,.025,out var ignoredLate);
        return true;
    }
}
