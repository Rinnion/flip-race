using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
#if !GENERATE_ONLY
using FlipRace.Core;
#endif
static class ExportCatalog
{
    const string First="Verification/bots-first-levels.json", Complex="Verification/bots-complex-levels.json", Report="Verification/bot-diversity-plans.json", Target="Assets/Scripts/Core/BotPlanCatalog.cs";
    sealed class Entry { public string Id,Signature;public double[] Distances; }
    sealed class Group { public string Track,Profile,Level,CatalogHash; public string[] Goals;public Entry[] Plans;public int[][] Signatures;public JsonElement Coverage; }
    static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
    static string Quote(string s)=>JsonSerializer.Serialize(s);
    static JsonElement Field(JsonElement value,string name)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new Exception("Expected object for "+name);
        var matches=value.EnumerateObject().Where(p=>string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length!=1)throw new Exception("Missing/ambiguous field "+name);
        return matches[0].Value;
    }
    static string Str(JsonElement value,string name)
    { string s=Field(value,name).GetString();if(string.IsNullOrWhiteSpace(s))throw new Exception("Blank field "+name);return s; }
    static string[] Strings(JsonElement value)
    { var a=value.EnumerateArray().Select(x=>x.GetString()).ToArray();if(a.Any(string.IsNullOrWhiteSpace)||a.Distinct(StringComparer.Ordinal).Count()!=a.Length)throw new Exception("Invalid/duplicate string IDs");return a; }
    static bool SameSet(string[] a,string[] b)=>a.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(b.OrderBy(x=>x,StringComparer.Ordinal));
    static string SetKey(string[] a)=>string.Join("\n",a.OrderBy(x=>x,StringComparer.Ordinal));
    static List<Group> Read()
    {
        var groups=new List<Group>();using var doc=JsonDocument.Parse(File.ReadAllBytes(Report));var root=doc.RootElement;
        if(Str(root,"status")!="PASS")throw new Exception("Diversity source must be final PASS; partial/checkpoint reports cannot be exported");
        if(Field(root,"schemaVersion").GetInt32()!=1||!Field(root,"sourceStable").GetBoolean()||Field(root,"blockers").GetArrayLength()!=0)throw new Exception("Unsupported/unstable/blocked source");
        string[] tracks={"full-long","full-short","full-flow","rhythm-cascade","mixed-tricks"};
        string[] profiles={"Chosen","FastFlip"},levels={"Novice","Amateur","Confident","Strong","Master"};
        var keys=new HashSet<string>(StringComparer.Ordinal);var allPlanIds=new HashSet<string>(StringComparer.Ordinal);
        foreach(var lib in Field(root,"libraries").EnumerateArray())
        {
            var group=new Group{Track=Str(lib,"trackId"),Profile=Str(lib,"profile"),Level=Str(lib,"difficulty"),CatalogHash=Str(lib,"catalogHash"),Goals=Strings(Field(lib,"catalogIds"))};
            string key=group.Track+"/"+group.Profile+"/"+group.Level;
            if(!tracks.Contains(group.Track)||!profiles.Contains(group.Profile)||!levels.Contains(group.Level)||!keys.Add(key))throw new Exception("Unexpected/duplicate library "+key);
            if(group.Goals.Length==0||Hash(Encoding.UTF8.GetBytes(string.Join("\n",group.Goals)))!=group.CatalogHash)throw new Exception("Catalog hash mismatch "+key);
            var signatures=Field(lib,"signatures").EnumerateArray().ToArray();var plans=Field(lib,"plans").EnumerateArray().ToArray();
            if(signatures.Length==0||plans.Length==0||(group.Level!="Master"&&signatures.Length<12))throw new Exception("Empty signature/plan pool "+key);
            var signatureIds=new HashSet<string>(StringComparer.Ordinal);var eventSets=new HashSet<string>(StringComparer.Ordinal);var eventById=new Dictionary<string,(string[] Success,string[] Miss,string[] Hit)>(StringComparer.Ordinal);
            foreach(var signature in signatures)
            {
                string id=Str(signature,"id");string[] success=Strings(Field(signature,"successfulGoalIds")),miss=Strings(Field(signature,"missedGoalIds")),hit=Strings(Field(signature,"hitObstacleIds"));
                if(!signatureIds.Add(id)||success.Intersect(miss,StringComparer.Ordinal).Any()||!SameSet(success.Concat(miss).ToArray(),group.Goals))throw new Exception("Invalid goal partition/signature "+key);
                if(!eventSets.Add(JsonSerializer.Serialize(new[]{SetKey(success),SetKey(miss),SetKey(hit)})))throw new Exception("Duplicate actual outcome signature "+key);
                eventById.Add(id,(success,miss,hit));
            }
            var entries=new List<Entry>();var indexById=new Dictionary<string,int>(StringComparer.Ordinal);var tapeKeys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var plan in plans)
            {
                string id=Str(plan,"id"),signature=Str(plan,"signatureId");var distances=Field(plan,"distances").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
                if(!allPlanIds.Add(id)||!signatureIds.Contains(signature)||distances.Length==0)throw new Exception("Duplicate/invalid plan "+id);
                for(int i=0;i<distances.Length;i++)if(!double.IsFinite(distances[i])||distances[i]<0||(i>0&&distances[i]<distances[i-1]))throw new Exception("Invalid distance tape "+id);
                if(!tapeKeys.Add(string.Join(",",distances.Select(x=>BitConverter.DoubleToInt64Bits(x).ToString("X16",CultureInfo.InvariantCulture)))))throw new Exception("Duplicate distance tape "+id);
                var lagger=Field(plan,"lagger");var constant=Field(plan,"constant");var expected=eventById[signature];
                if(Str(lagger,"status")!="Finished"||!SameSet(Strings(Field(lagger,"successfulGoalIds")),expected.Success)||!SameSet(Strings(Field(lagger,"missedGoalIds")),expected.Miss)||!SameSet(Strings(Field(lagger,"hitObstacleIds")),expected.Hit))throw new Exception("Plan differs from its actual Lagger signature "+id);
                if(Field(lagger,"total").GetInt32()!=group.Goals.Length||Field(lagger,"completed").GetInt32()!=expected.Success.Length)throw new Exception("Goal count mismatch "+id);
                double score=Field(lagger,"score").GetDouble();int level=Array.IndexOf(levels,group.Level);double[] low={40,60,70,85,100},high={60,70,80,95,100};
                if(Math.Abs(score-100d*expected.Success.Length/group.Goals.Length)>1e-8||score<low[level]-1e-8||score>high[level]+1e-8)throw new Exception("Actual score outside level range "+id);
                string terminal=Str(constant,"status");if(terminal!="Finished"&&terminal!="Eliminated")throw new Exception("Invalid Constant outcome "+id);
                if(group.Level=="Master"&&(terminal!="Finished"||Field(constant,"score").GetDouble()!=100))throw new Exception("Master must finish100 in both cameras "+id);
                if(Field(plan,"timing").ValueKind!=JsonValueKind.Object)throw new Exception("Missing timing provenance "+id);
                indexById.Add(id,entries.Count);entries.Add(new Entry{Id=id,Signature=signature,Distances=distances});
            }
            var used=new HashSet<int>();var signatureGroups=new List<int[]>();
            foreach(var signature in signatures)
            {
                string id=Str(signature,"id");string[] ids=Strings(Field(signature,"planIds"));if(ids.Length<3)throw new Exception("Every signature requires at least3 timing plans "+id);
                var indices=new List<int>();foreach(string planId in ids)
                {if(!indexById.TryGetValue(planId,out int index)||entries[index].Signature!=id||!used.Add(index))throw new Exception("Invalid/reused subgroup member "+planId);indices.Add(index);}
                signatureGroups.Add(indices.ToArray());
            }
            if(used.Count!=entries.Count)throw new Exception("Plan missing from signature groups "+key);
            group.Coverage=Field(lib,"timingCoverage").Clone();
            group.Plans=entries.ToArray();group.Signatures=signatureGroups.ToArray();groups.Add(group);
        }
        if(groups.Count!=50||Field(root,"libraryCount").GetInt32()!=50||Field(root,"planCount").GetInt32()!=groups.Sum(g=>g.Plans.Length))throw new Exception("Final source requires all50 libraries and exact dynamic plan count");
        foreach(string track in tracks)foreach(string profile in profiles)foreach(string level in levels)if(!keys.Contains(track+"/"+profile+"/"+level))throw new Exception("Missing complete Cartesian coverage");
        foreach(var pair in groups.GroupBy(g=>g.Track+"/"+g.Profile))if(pair.Any(g=>g.CatalogHash!=pair.First().CatalogHash||!g.Goals.SequenceEqual(pair.First().Goals)))throw new Exception("Catalog differs between levels "+pair.Key);
        ValidateCoverage(groups);return groups;
    }
    static void ValidateCoverage(List<Group> groups)
    {
        foreach(var group in groups)
        {
            string key=group.Track+"/"+group.Profile+"/"+group.Level;var proof=group.Coverage;string status=Str(proof,"status");
            if(group.Level!="Master"&&group.Level!="Confident")
            {
                string expected=group.Track+"/"+group.Profile+"/Confident";
                if(status!="TIMING_VARIANTS_VERIFIED"||Str(proof,"pairProofRef")!=expected||!groups.Any(g=>g.Track==group.Track&&g.Profile==group.Profile&&g.Level=="Confident"&&Str(g.Coverage,"status")=="PROVED"))throw new Exception("Invalid pair timing proof reference "+key);
                continue;
            }
            if(status!="PROVED"||!Field(proof,"allNineWholeRoutesValid").GetBoolean())throw new Exception("Missing separated whole-route timing proof "+key);
            int first=Field(proof,"firstEdge").GetInt32(),second=Field(proof,"secondEdge").GetInt32();
            if(first<0||second<0||first==second||Str(proof,"firstObstacle")==Str(proof,"secondObstacle"))throw new Exception("Timing maneuvers must be distinct "+key);
            foreach(string prefix in new[]{"first","second"})
            {
                double[] interval=Field(proof,prefix+"MeasuredInterval").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
                double[] positions=Field(proof,prefix+"PositionsRelativeObstacleX").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
                if(interval.Length!=2||positions.Length!=3||interval.Concat(positions).Any(x=>!double.IsFinite(x))||interval[1]<=interval[0]||positions[0]>=positions[1]||positions[1]>=positions[2]||positions[2]-positions[0]<(interval[1]-interval[0])*.5-1e-8)throw new Exception("Invalid local interval coverage "+key);
            }
            double[] fractions=Field(proof,"coverageOfSampledInterval").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
            if(fractions.Length!=2||fractions.Any(x=>!double.IsFinite(x)||x<.5||x>1+1e-8))throw new Exception("Invalid measured coverage fractions "+key);
            var points=Field(proof,"points").EnumerateArray().ToArray();var combinations=new HashSet<string>(StringComparer.Ordinal);string[] names={"early","middle","late"};
            foreach(var point in points)
            {
                string a=Str(point,"first"),b=Str(point,"second");if(!names.Contains(a)||!names.Contains(b)||!combinations.Add(a+"/"+b))throw new Exception("Invalid Cartesian proof point "+key);
                foreach(string field in new[]{"firstActualDistance","secondActualDistance","firstActualTime","secondActualTime"})if(!double.IsFinite(Field(point,field).GetDouble())||Field(point,field).GetDouble()<0)throw new Exception("Invalid timing observation "+key);
            }
            string[] ids=Strings(Field(proof,"planIds"));if(ids.Length!=9||points.Length!=9)throw new Exception("Proof requires nine distinct whole plans "+key);
            string signature=null;
            foreach(string id in ids)
            {
                var plan=group.Plans.SingleOrDefault(p=>p.Id==id);if(plan==null||first>=plan.Distances.Length||second>=plan.Distances.Length)throw new Exception("Unknown/out-of-bounds proof plan "+key);
                if(signature==null)signature=plan.Signature;else if(signature!=plan.Signature)throw new Exception("Cartesian timing proof must preserve signature "+key);
            }
        }
    }
    static string Fingerprint(IEnumerable<(string Id,double[] Distances)> plans)
    {
        var text=new StringBuilder();foreach(var p in plans){text.Append(p.Id).Append('\n');foreach(double d in p.Distances)text.Append(BitConverter.DoubleToInt64Bits(d).ToString("X16",CultureInfo.InvariantCulture)).Append(',');text.Append('\n');}return Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }
    static void Generate(List<Group> groups)
    {
        var text=new StringBuilder(@"// Generated by Tools/BotDiversity/ExportCatalog.cs from the complete accepted diversity report.
// Source hashes identify calibration provenance; no runtime route search or simulation mutation.
using System;
namespace FlipRace.Core
{
    public enum BotDifficulty { Novice, Amateur, Confident, Strong, Master }
    public sealed class BotPlan
    {
        private readonly double[] distances;
        public string Id { get; }
        public string TrackId { get; }
        public string ProfileId { get; }
        public string SignatureId { get; }
        public BotDifficulty Difficulty { get; }
        public int Seed { get; }
        public int PlanIndex { get; }
        public int InputCount { get { return distances.Length; } }
        public double[] Distances { get { return (double[])distances.Clone(); } }
        public double GetDistance(int index) { return distances[index]; }
        internal BotPlan(string id,string track,string profile,BotDifficulty difficulty,int seed,int index,string signature,double[] source)
        { Id=id;TrackId=track;ProfileId=profile;Difficulty=difficulty;Seed=seed;PlanIndex=index;SignatureId=signature;distances=(double[])source.Clone(); }
    }
    public static class BotPlanCatalog
    {
        private sealed class Tape
        {
            internal readonly string Id,Signature;
            internal readonly double[] Distances;
            internal Tape(string id,string signature,double[] distances) { Id=id;Signature=signature;Distances=distances; }
        }
        private sealed class Library
        {
            internal readonly string Track,Profile,CatalogHash;
            internal readonly BotDifficulty Difficulty;
            internal readonly string[] Goals;
            internal readonly Tape[] Tapes;
            internal readonly int[][] Signatures;
            internal Library(string track,string profile,BotDifficulty difficulty,string hash,string[] goals,Tape[] tapes,int[][] signatures)
            { Track=track;Profile=profile;Difficulty=difficulty;CatalogHash=hash;Goals=goals;Tapes=tapes;Signatures=signatures; }
        }
        public const int TotalPlans=TOTAL_PLANS;
");
        text.Replace("TOTAL_PLANS",groups.Sum(g=>g.Plans.Length).ToString(CultureInfo.InvariantCulture));
        text.Append("        public const string SourceReportSha256=").Append(Quote(Hash(File.ReadAllBytes(Report)))).AppendLine(";");
        text.Append("        public const string FirstReportSha256=").Append(Quote(Hash(File.ReadAllBytes(First)))).AppendLine(";");
        text.Append("        public const string ComplexReportSha256=").Append(Quote(Hash(File.ReadAllBytes(Complex)))).AppendLine(";");
        text.Append(@"        private static bool Exact(FlightSettings a,FlightSettings b)
        { return a!=null&&a.FirstAction==b.FirstAction&&a.Law==b.Law&&a.AirRepeats==b.AirRepeats&&a.GravityAcceleration==b.GravityAcceleration&&a.FlipImpulse==b.FlipImpulse&&a.JumpImpulse==b.JumpImpulse&&a.LinearSpeed==b.LinearSpeed&&a.LinearJumpHeight==b.LinearJumpHeight&&a.LegacyLinear==b.LegacyLinear; }
        private static string Profile(FlightSettings flight)
        {
            if(flight==null)throw new ArgumentNullException(nameof(flight));
            if(Exact(flight,FlightPresets.Chosen))return ""Chosen"";
            if(Exact(flight,FlightPresets.FastFlip))return ""FastFlip"";
            throw new ArgumentException(""Bots require an exact Chosen or FastFlip preset."",nameof(flight));
        }
        public static bool IsSupported(string trackId,FlightSettings flight)
        {
            if(trackId==null||flight==null)return false;
            string profile=Exact(flight,FlightPresets.Chosen)?""Chosen"":Exact(flight,FlightPresets.FastFlip)?""FastFlip"":null;
            if(profile==null)return false;
            foreach(var library in Libraries)if(library.Track==trackId&&library.Profile==profile)return true;
            return false;
        }
        private static Library Find(string trackId,FlightSettings flight,BotDifficulty difficulty)
        {
            if(trackId==null)throw new ArgumentNullException(nameof(trackId));
            if(!Enum.IsDefined(typeof(BotDifficulty),difficulty))throw new ArgumentOutOfRangeException(nameof(difficulty));
            string profile=Profile(flight);
            foreach(var library in Libraries)if(library.Track==trackId&&library.Profile==profile&&library.Difficulty==difficulty)return library;
            throw new ArgumentException(""Unsupported bot track."",nameof(trackId));
        }
        public static int Count(string trackId,FlightSettings flight,BotDifficulty difficulty)
        { return Find(trackId,flight,difficulty).Tapes.Length; }
        public static int GoalCount(string trackId,FlightSettings flight)
        { return Find(trackId,flight,BotDifficulty.Novice).Goals.Length; }
        public static string[] GoalIds(string trackId,FlightSettings flight)
        { return (string[])Find(trackId,flight,BotDifficulty.Novice).Goals.Clone(); }
        public static string CatalogHash(string trackId,FlightSettings flight)
        { return Find(trackId,flight,BotDifficulty.Novice).CatalogHash; }
        public static BotPlan Select(string trackId,FlightSettings flight,BotDifficulty difficulty,int seed)
        {
            var library=Find(trackId,flight,difficulty);int index=seed%library.Tapes.Length;if(index<0)index+=library.Tapes.Length;
            var tape=library.Tapes[index];return new BotPlan(tape.Id,trackId,library.Profile,difficulty,seed,index,tape.Signature,tape.Distances);
        }
        public static int[][] SignatureGroups(string trackId,FlightSettings flight,BotDifficulty difficulty)
        {
            var source=Find(trackId,flight,difficulty).Signatures;var result=new int[source.Length][];
            for(int i=0;i<source.Length;i++)result[i]=(int[])source[i].Clone();return result;
        }
        public static BotPlan SelectAt(string trackId,FlightSettings flight,BotDifficulty difficulty,int index,int attemptSeed)
        {
            var library=Find(trackId,flight,difficulty);
            if(index<0||index>=library.Tapes.Length)throw new ArgumentOutOfRangeException(nameof(index));
            var tape=library.Tapes[index];return new BotPlan(tape.Id,trackId,library.Profile,difficulty,attemptSeed,index,tape.Signature,tape.Distances);
        }
        private static readonly Library[] Libraries=Create();
        private static Library[] Create()
        {
            return new Library[] {
");
        for(int i=0;i<groups.Count;i++)
        {
            var g=groups[i];text.Append("                new Library(").Append(Quote(g.Track)).Append(',').Append(Quote(g.Profile)).Append(",BotDifficulty.").Append(g.Level).Append(',').Append(Quote(g.CatalogHash)).Append(",new string[]{").Append(string.Join(",",g.Goals.Select(Quote))).Append("},Library").Append(i).Append("(),new int[][]{").Append(string.Join(",",g.Signatures.Select(a=>"new int[]{"+string.Join(",",a)+"}"))).AppendLine("}),");
        }
        text.AppendLine("            };\n        }");
        for(int i=0;i<groups.Count;i++)
        {
            text.Append("        private static Tape[] Library").Append(i).AppendLine("()\n        {\n            return new Tape[] {");
            foreach(var p in groups[i].Plans)text.Append("                new Tape(").Append(Quote(p.Id)).Append(',').Append(Quote(p.Signature)).Append(",new double[]{").Append(string.Join(",",p.Distances.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)+"d"))).AppendLine("}),");
            text.AppendLine("            };\n        }");
        }
        text.AppendLine("    }\n}");File.WriteAllText(Target,text.ToString(),new UTF8Encoding(false));
        if(!File.Exists(Target+".meta"))File.WriteAllText(Target+".meta","fileFormatVersion: 2\nguid: 8f706a905b834e8ab89d43dc48bc6281\n",new UTF8Encoding(false));
        Console.WriteLine("Generated "+Target+":50 libraries;"+groups.Sum(g=>g.Plans.Length)+" plans; source binary-double fingerprint="+Fingerprint(groups.SelectMany(g=>g.Plans).Select(p=>(p.Id,p.Distances))));
    }
#if !GENERATE_ONLY
    static int checks;
    static void Need(bool yes,string why){checks++;if(!yes)throw new Exception(why);}
    static void Reject(Action action){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Need(rejected,"invalid selection must reject");}
    static void Verify(List<Group> groups)
    {
        Need(BotPlanCatalog.SourceReportSha256==Hash(File.ReadAllBytes(Report)),"authoritative diversity provenance");
        var actual=new List<(string Id,double[] Distances)>();
        foreach(var group in groups)
        {
            FlightSettings flight=group.Profile=="Chosen"?FlightPresets.Chosen:FlightPresets.FastFlip;BotDifficulty difficulty=Enum.Parse<BotDifficulty>(group.Level);
            Need(BotPlanCatalog.IsSupported(group.Track,flight),"supported");Need(BotPlanCatalog.Count(group.Track,flight,difficulty)==group.Plans.Length,"plan count");Need(BotPlanCatalog.GoalCount(group.Track,flight)==group.Goals.Length,"goal count");Need(BotPlanCatalog.GoalIds(group.Track,flight).SequenceEqual(group.Goals),"goal IDs");Need(BotPlanCatalog.CatalogHash(group.Track,flight)==group.CatalogHash,"catalog hash");
            var goals=BotPlanCatalog.GoalIds(group.Track,flight);goals[0]="modified";Need(BotPlanCatalog.GoalIds(group.Track,flight)[0]==group.Goals[0],"defensive goal copy");
            var signatures=BotPlanCatalog.SignatureGroups(group.Track,flight,difficulty);Need(signatures.Length==group.Signatures.Length,"signature count");
            for(int signature=0;signature<signatures.Length;signature++)Need(signatures[signature].SequenceEqual(group.Signatures[signature]),"exact signature subgroup indices");
            signatures[0][0]=-1;signatures[signatures.Length-1]=new[]{-2};var againGroups=BotPlanCatalog.SignatureGroups(group.Track,flight,difficulty);
            Need(againGroups[0].SequenceEqual(group.Signatures[0])&&againGroups.Last().SequenceEqual(group.Signatures.Last()),"deep inner/outer subgroup copy");
            for(int index=0;index<group.Plans.Length;index++)
            {
                var expected=group.Plans[index];var plan=BotPlanCatalog.SelectAt(group.Track,flight,difficulty,index,int.MinValue);var again=BotPlanCatalog.SelectAt(group.Track,flight,difficulty,index,int.MaxValue);
                Need(plan.Id==expected.Id&&plan.Id==again.Id&&plan.TrackId==group.Track&&plan.ProfileId==group.Profile&&plan.Seed==int.MinValue&&again.Seed==int.MaxValue&&plan.Difficulty==difficulty&&plan.PlanIndex==index&&again.PlanIndex==index&&plan.SignatureId==expected.Signature&&again.SignatureId==expected.Signature,"SelectAt retains direct index and arbitrary attempt seed");
                Need(plan.InputCount==expected.Distances.Length,"input count");double[] copy=plan.Distances;
                for(int i=0;i<plan.InputCount;i++){Need(BitConverter.DoubleToInt64Bits(plan.GetDistance(i))==BitConverter.DoubleToInt64Bits(expected.Distances[i]),"IEEE754 source distance "+plan.Id+" edge"+i);Need(BitConverter.DoubleToInt64Bits(copy[i])==BitConverter.DoubleToInt64Bits(expected.Distances[i]),"IEEE754 copy distance");}
                actual.Add((plan.Id,plan.Distances));copy[0]=-1;Need(BitConverter.DoubleToInt64Bits(plan.GetDistance(0))==BitConverter.DoubleToInt64Bits(expected.Distances[0])&&BotPlanCatalog.SelectAt(group.Track,flight,difficulty,index,0).GetDistance(0)==expected.Distances[0],"defensive plan copies");
                Need(BotPlanCatalog.Select(group.Track,flight,difficulty,index).Id==expected.Id,"compatibility Select local index");Need(BotPlanCatalog.Select(group.Track,flight,difficulty,index+group.Plans.Length).Id==plan.Id,"modulo compatibility");
            }
            foreach(int seed in new[]{-1,int.MinValue,int.MaxValue,0})
            {
                int index=seed%group.Plans.Length;if(index<0)index+=group.Plans.Length;var plan=BotPlanCatalog.Select(group.Track,flight,difficulty,seed);
                Need(plan.Id==group.Plans[index].Id&&plan.Seed==seed&&plan.PlanIndex==index,"signed seed exact modulo");
            }
            Reject(()=>BotPlanCatalog.SelectAt(group.Track,flight,difficulty,-1,0));Reject(()=>BotPlanCatalog.SelectAt(group.Track,flight,difficulty,group.Plans.Length,0));
            var cloned=new FlightSettings(flight.FirstAction,flight.Law,flight.AirRepeats,flight.GravityAcceleration,flight.FlipImpulse,flight.JumpImpulse,flight.LinearSpeed,flight.LinearJumpHeight);Need(BotPlanCatalog.IsSupported(group.Track,cloned),"exact preset clone");
            foreach(var custom in new[]{new FlightSettings(flight.FirstAction,flight.Law,flight.AirRepeats,flight.GravityAcceleration,flight.FlipImpulse,flight.JumpImpulse,flight.LinearSpeed+1,flight.LinearJumpHeight),new FlightSettings(flight.FirstAction,flight.Law,flight.AirRepeats,flight.GravityAcceleration+.01,flight.FlipImpulse,flight.JumpImpulse,flight.LinearSpeed,flight.LinearJumpHeight)}){Need(!BotPlanCatalog.IsSupported(group.Track,custom),"custom unsupported");Reject(()=>BotPlanCatalog.SignatureGroups(group.Track,custom,difficulty));Reject(()=>BotPlanCatalog.SelectAt(group.Track,custom,difficulty,0,0));}
        }
        Need(actual.Count==groups.Sum(g=>g.Plans.Length)&&BotPlanCatalog.TotalPlans==actual.Count,"dynamic total");Need(Fingerprint(actual)==Fingerprint(groups.SelectMany(g=>g.Plans).Select(p=>(p.Id,p.Distances))),"all source/export bit hashes identical");Reject(()=>BotPlanCatalog.Select("unsupported",FlightPresets.Chosen,BotDifficulty.Novice,0));Reject(()=>BotPlanCatalog.Select(groups[0].Track,null,BotDifficulty.Novice,0));Reject(()=>BotPlanCatalog.Select(groups[0].Track,FlightPresets.Chosen,(BotDifficulty)99,0));
        Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,libraries=groups.Count,plans=actual.Count,signatureGroups=groups.Sum(g=>g.Signatures.Length),sourceReportSha256=Hash(File.ReadAllBytes(Report)),sourceFingerprint=Fingerprint(actual),scope="All source IEEE754 distances, IDs, signatures, local groups, hashes, deep copies, exact profiles, signed seeds and direct SelectAt indices."},new JsonSerializerOptions{WriteIndented=true}));
    }
#endif
    static int Main(string[] args)
    {
        try {var groups=Read();if(args.Contains("--validate-source")){Console.WriteLine("Source PASS:50 libraries;"+groups.Sum(g=>g.Plans.Length)+" plans;validated signatures and coverage references");return 0;}if(args.Contains("--generate"))Generate(groups);
#if !GENERATE_ONLY
            else Verify(groups);
#else
            else throw new Exception("GenerateOnly build requires --generate");
#endif
            return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
