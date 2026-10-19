using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FlipRace.Presentation;
using UnityEngine;

record PixelSample(int Player,int Hero,string Group,int Index,string Hash);
record PerfSample(string Kind,int Round,int Player,int Hero,int Pose,double Milliseconds,int Textures,long AllocatedBytes);
static class Program
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented=true };
    static readonly string[] Groups={"Run","RunBlink","RunSuccess","Idle","IdleBlink","Air","AirBlink","Recover","Success","Win"};
    static int Main(string[] args)
    {
        try { if(args[0]=="compare")Compare(args[1]);else Perf(args[0]=="perf-old",args[1]);return 0; }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void Compare(string path)
    {
        Texture2D.CapturePixels=true;
        var beforeType=typeof(BaselineMascot);var afterType=typeof(CurrentMascot);
        var oldEnsure=beforeType.GetMethod("Ensure",BindingFlags.NonPublic|BindingFlags.Static);
        var newEnsure=afterType.GetMethod("Ensure",BindingFlags.NonPublic|BindingFlags.Static);
        var pose=afterType.GetMethod("Pose",BindingFlags.NonPublic|BindingFlags.Static);
        var samples=new List<PixelSample>();int poses=0,previews=0,selectorChecks=0,cacheChecks=0;
        for(int player=0;player<2;player++)for(int hero=0;hero<4;hero++)
        {
            object oldSet=oldEnsure.Invoke(null,new object[]{hero,player}),newSet=newEnsure.Invoke(null,new object[]{hero,player});
            int variantPoses=0;
            foreach(string group in Groups)
            {
                var oldArray=(Texture2D[])oldSet.GetType().GetField(group).GetValue(oldSet);
                var newArray=(Texture2D[])newSet.GetType().GetField(group).GetValue(newSet);
                if(oldArray.Length!=newArray.Length)throw new Exception("Group length changed");
                for(int i=0;i<oldArray.Length;i++)
                {
                    var actual=(Texture2D)pose.Invoke(null,new object[]{newSet,newArray,i});
                    string expected=oldArray[i].GetHash(),hash=actual.GetHash();
                    if(expected!=hash)throw new Exception($"Pixel mismatch {player}/{hero}/{group}/{i}: {expected} != {hash}");
                    int created=Texture2D.Created;
                    if(!ReferenceEquals(actual,pose.Invoke(null,new object[]{newSet,newArray,i}))||Texture2D.Created!=created)
                        throw new Exception("Pose cache recreated a texture");
                    cacheChecks++;poses++;variantPoses++;samples.Add(new PixelSample(player,hero,group,i,hash));
                }
            }
            if(variantPoses!=192)throw new Exception("Expected all 192 poses");
            string preview=BaselineMascot.Create(hero,player).GetHash();
            if(preview!=CurrentMascot.Create(hero,player).GetHash())throw new Exception("Preview mismatch");
            previews++;samples.Add(new PixelSample(player,hero,"Preview",0,preview));
            for(int i=0;i<128;i++)
            {
                float visual=i*.137f,motion=i*.071f,flight=(i%23)/22f;
                bool running=(i&1)!=0,airborne=i%3==0,recovering=i%11==0;
                var reaction=(MascotReaction)(i%4);float age=i*.053f;
                var old=BaselineMascot.GetMotionFrame(hero,player,visual,motion,flight,running,airborne,recovering,reaction,age);
                var current=CurrentMascot.GetMotionFrame(hero,player,visual,motion,flight,running,airborne,recovering,reaction,age);
                if(old.GetHash()!=current.GetHash())throw new Exception("Public motion selector mismatch");
                var oldReaction=BaselineMascot.GetReactionFrame(hero,player,age,reaction);
                var newReaction=CurrentMascot.GetReactionFrame(hero,player,age,reaction);
                if(oldReaction.GetHash()!=newReaction.GetHash())throw new Exception("Public reaction selector mismatch");
                selectorChecks+=2;
            }
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{Verdict="PASS_PIXEL_EQUIVALENCE",Variants=8,Poses=poses,Previews=previews,SelectorChecks=selectorChecks,CacheChecks=cacheChecks,Samples=samples},Json));
        Console.WriteLine($"PASS: {poses} poses + {previews} previews pixel-identical; {selectorChecks} public selector and {cacheChecks} cache checks.");
    }
    static void Perf(bool old,string path)
    {
        Texture2D.CapturePixels=false;
        var samples=new List<PerfSample>();
        Texture2D Frame(int hero,int player,float time,bool running=false)=>old?BaselineMascot.GetFrame(hero,player,time,running,false,false):CurrentMascot.GetFrame(hero,player,time,running,false,false);
        Texture2D Preview(int hero,int player)=>old?BaselineMascot.Create(hero,player):CurrentMascot.Create(hero,player);
        void Measure(string kind,int round,int player,int hero,int pose,Action action)
        {
            long bytes=GC.GetAllocatedBytesForCurrentThread();int textures=Texture2D.Created;var watch=Stopwatch.StartNew();
            action();watch.Stop();samples.Add(new PerfSample(kind,round,player,hero,pose,watch.Elapsed.TotalMilliseconds,Texture2D.Created-textures,GC.GetAllocatedBytesForCurrentThread()-bytes));
        }
        Measure("startup-selected",0,0,0,0,()=>Frame(0,0,0));
        Measure("startup-selected",0,1,1,0,()=>Frame(1,1,0));
        for(int player=0;player<2;player++)for(int hero=0;hero<4;hero++)
        {int h=hero,p=player;Measure("startup-preview",0,p,h,0,()=>Preview(h,p));}
        for(int round=0;round<3;round++)for(int player=0;player<2;player++)for(int hero=0;hero<4;hero++)
        {
            int h=hero,p=player;
            Measure("selection",round,p,h,0,()=>Frame(h,p,0));
            Measure("repeat-selection",round,p,h,0,()=>Frame(h,p,0));
            for(int pose=0;pose<8;pose++)
            {
                float t=(pose+.25f)*.0157f;
                Measure("first-running-pose",round,p,h,pose,()=>Frame(h,p,t,true));
                Measure("repeat-running-pose",round,p,h,pose,()=>Frame(h,p,t,true));
            }
        }
        object Stats(string kind)
        {
            var subset=samples.Where(s=>s.Kind==kind).ToArray();var times=subset.Select(s=>s.Milliseconds).OrderBy(v=>v).ToArray();
            return new{Count=subset.Length,Min=times[0],Median=times.Length%2==0?(times[times.Length/2-1]+times[times.Length/2])/2:times[times.Length/2],Max=times[times.Length-1],Textures=subset.Sum(s=>s.Textures),AllocatedBytes=subset.Sum(s=>s.AllocatedBytes)};
        }
        var startup=samples.Where(s=>s.Kind.StartsWith("startup-")).ToArray();
        var report=new{Implementation=old?"39097d0 eager":"current lazy",Scope="Desktop .NET CPU rasterizer with Unity shims; excludes GPU/native upload, browser input and paint, and physical phone",Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,StartupMilliseconds=startup.Sum(s=>s.Milliseconds),StartupTextures=startup.Sum(s=>s.Textures),StartupAllocatedBytes=startup.Sum(s=>s.AllocatedBytes),Selection=Stats("selection"),RepeatSelection=Stats("repeat-selection"),FirstRunningPose=Stats("first-running-pose"),RepeatRunningPose=Stats("repeat-running-pose"),Samples=samples};
        File.WriteAllText(path,JsonSerializer.Serialize(report,Json));
        Console.WriteLine($"{report.Implementation}: startup {report.StartupMilliseconds:F3} ms / {report.StartupTextures} textures; details {path}");
    }
}
