using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlipRace.Core;
static class Program
{
 static int checks;
 static readonly List<object> matrix=new List<object>();
 static void Check(bool b,string m){checks++;if(!b)throw new Exception(m);}
 static void Reject(Action action,string label){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Expected argument rejection: "+label);}
 static int[][] Make(params int[] counts){int next=0;return counts.Select(c=>{int[] group=Enumerable.Range(next,c).ToArray();next+=c;return group;}).ToArray();}
 static bool Equal(BotPlanChoice a,BotPlanChoice b)=>a.SignatureIndex==b.SignatureIndex&&a.PlanIndex==b.PlanIndex&&a.AttemptSeed==b.AttemptSeed&&a.SequenceIndex==b.SequenceIndex;
 static string Digest(IEnumerable<BotPlanChoice> choices){var text=string.Join(";",choices.Select(c=>$"{c.SignatureIndex},{c.PlanIndex},{c.AttemptSeed},{c.SequenceIndex}"));return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));}
 static void Bags(uint seed,int[][] groups,string label)
 {
  var selector=new BotPlanShuffle(seed);var repeat=new BotPlanShuffle(seed);var signatures=new HashSet<int>();var timing=groups.Select(g=>new HashSet<int>()).ToArray();
  var lastTiming=groups.Select(g=>-1).ToArray();var groupCounts=new int[groups.Length];int last=-1;int draws=100*groups.Length*groups.Max(g=>g.Length);var tape=new List<BotPlanChoice>();var attemptSeeds=new HashSet<int>();
  for(int i=0;i<draws;i++)
  {
   var c=selector.Next("track/profile/level",groups);Check(Equal(c,repeat.Next("track/profile/level",groups)),"fixed seed exact repeat");tape.Add(c);
   Check(c.SequenceIndex==i&&c.SignatureIndex>=0&&c.SignatureIndex<groups.Length&&groups[c.SignatureIndex].Contains(c.PlanIndex),"indices in bounds and sequence");Check(attemptSeeds.Add(c.AttemptSeed),"attempt seeds distinct in measured sequence");
   if(signatures.Count==groups.Length)signatures.Clear();Check(signatures.Add(c.SignatureIndex),"no signature repeat before exhaustion");if(groups.Length>1)Check(last!=c.SignatureIndex,"no boundary signature repeat");last=c.SignatureIndex;
   int s=c.SignatureIndex;if(timing[s].Count==groups[s].Length)timing[s].Clear();Check(timing[s].Add(c.PlanIndex),"no timing repeat before exhaustion");if(groups[s].Length>1)Check(lastTiming[s]!=c.PlanIndex,"no timing boundary repeat");lastTiming[s]=c.PlanIndex;groupCounts[s]++;
  }
  Check(groupCounts.All(n=>n==draws/groups.Length),"exact equal signature counts despite unequal variant weights");
  string digest=Digest(tape);if(seed==0&&label=="unequal-groups")Check(digest=="AFAB75496113830AF050DF57B15F2080143B52D779875E8512DB55E6F76A0095","stable uint/UTF16 shuffle golden vector");
  matrix.Add(new{label,seed,variantCounts=groups.Select(g=>g.Length),draws,signatureCounts=groupCounts,minimumTimingCycles=100,sha256=digest});
 }
 static void Isolation()
 {
  var g=Make(1,2,7);var baseline=new BotPlanShuffle(123);var mixed=new BotPlanShuffle(123);var other=new BotPlanShuffle(123);var tape=new List<BotPlanChoice>();
  for(int i=0;i<3000;i++)
  {
   var a=baseline.Next("Трасса/Chosen/Новичок",g);var b=mixed.Next("Трасса/Chosen/Новичок",g);Check(Equal(a,b),"interleaving cannot alter another key");tape.Add(b);
   for(int j=0;j<i%5;j++)Check(Equal(mixed.Next("other",g),other.Next("other",g)),"other key independent reproducibility");
  }
  var exact=new BotPlanShuffle(123);Check(Equal(exact.Next("A",g),new BotPlanShuffle(123).Next("A",g)),"stable case key");Check(!Equal(exact.Next("a",g),new BotPlanShuffle(123).Next("A",g)),"ordinal case distinct streams");
  matrix.Add(new{label="interleaved-ordinal-unicode",draws=3000,sha256=Digest(tape)});
 }
 static void Validation()
 {
  var u=new BotPlanShuffle(0);int[][] g=Make(2,3),original=g.Select(a=>(int[])a.Clone()).ToArray();u.Next("stable",g);g[0][0]=99;
  Reject(()=>u.Next("stable",g),"mutated inner");var baseline=new BotPlanShuffle(0);baseline.Next("stable",original);Check(Equal(u.Next("stable",original),baseline.Next("stable",original)),"rejection leaves cached clone and stream unchanged");
  g[0]=new[]{10,11};Reject(()=>u.Next("stable",g),"mutated outer");Reject(()=>u.Next("stable",Make(2,3,1)),"changed length");Reject(()=>u.Next("stable",Make(3,2)),"changed shape");Reject(()=>u.Next("stable",new[]{original[1],original[0]}),"reordered groups");Reject(()=>u.Next("stable",new[]{original[0].Reverse().ToArray(),original[1]}),"reordered timing");Reject(()=>u.Next("stable",null),"existing null");
  foreach(string key in new string[]{null,""," ","\t\n"})Reject(()=>u.Next(key,original),"blank key");
  foreach(int[][] bad in new int[][][]{null,Array.Empty<int[]>(),new int[][]{null},new[]{Array.Empty<int>()},new[]{new[]{-1}},new[]{new[]{0,0}},new[]{new[]{0},new[]{0}},new[]{new[]{0,1},Array.Empty<int>()}})Reject(()=>u.Next("invalid",bad),"invalid shape/ID");
  var max=new[]{new[]{int.MaxValue,0}};var high=u.Next("max-id",max);Check(max[0].Contains(high.PlanIndex),"max global ID accepted");Check(Equal(u.Next("invalid",original),new BotPlanShuffle(0).Next("invalid",original)),"failed initial validation does not create key or consume state");
  var changed=new BotPlanShuffle(999);changed.Next("x",original);g=original.Select(a=>(int[])a.Clone()).ToArray();g[0][0]=50;changed.Next("y",g);Check(Equal(changed.Next("x",original),new[]{new BotPlanShuffle(999)}.Select(s=>{s.Next("x",original);return s.Next("x",original);}).Single()),"caller reuse across keys cannot mutate cached groups");
  matrix.Add(new{label="invalid-and-mutation",status="PASS"});
 }
 static void SeedVariation()
 {
  var groups=Make(1,2,17);var counts=new int[3];var tapes=new HashSet<string>();
  for(uint seed=0;seed<1024;seed++){var u=new BotPlanShuffle(seed);counts[u.Next("variation",groups).SignatureIndex]++;tapes.Add(Digest(Enumerable.Range(0,20).Select(i=>u.Next("variation",groups))));}
  Check(counts.All(n=>n>=260&&n<=425),"first signature is not weighted by timing count");Check(tapes.Count>1000,"different initial seeds vary sequence");matrix.Add(new{label="1024-initial-seeds",firstSignatureCounts=counts,distinctTapes=tapes.Count});
 }
 static int Main(string[] args){try{
  foreach(uint seed in new[]{0u,1u,uint.MaxValue,0x80000000u,unchecked((uint)-2)})
  {Bags(seed,Make(1,2,7),"unequal-groups");Bags(seed,Make(7),"master-one-signature");Bags(seed,Make(1),"singleton");Bags(seed,Make(1,1),"two-singleton-boundary");}
  Bags(42,Make(2,3,5,7,11,13,17,19),"eight-groups");Isolation();Validation();SeedVariation();
  string production=Path.GetFullPath("Assets/Scripts/Core/BotPlanShuffle.cs");if(!File.Exists(production))production=Path.GetFullPath("../../Assets/Scripts/Core/BotPlanShuffle.cs");
  var report=new{status="PASS",checks,matrix,source=new{path="Assets/Scripts/Core/BotPlanShuffle.cs",sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(production)))},scope="Isolated deterministic selection only; not catalogue feasibility, physics, UI or native WebGL evidence."};
  string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});if(args.Length>0)File.WriteAllText(args[0],json);Console.WriteLine(json);return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}