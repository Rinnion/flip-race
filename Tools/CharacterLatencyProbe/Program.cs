using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlipRace.Core;
using FlipRace.Presentation;

public sealed record Sample(int Run, int Slot, int Hero, string Kind, double StateMs, double FrameReadyMs, double ReturnMs, int FramesCreated, long AllocatedBytes, bool CorrectState);
public static class Program
{
    public static int Main(string[] args)
    {
        var reportPath = args.Length > 0 ? args[0] : "report.json";
        const double budgetMs = 100; // Diagnostic long-task threshold, not a claimed product SLA.
        var samples = new List<Sample>();
        // Mirror startup's selected full animations and eight menu previews.
        MascotArtwork.GetFrame(0,0,0,false,false,false);
        MascotArtwork.GetFrame(1,1,0,false,false,false);
        for (int slot=0;slot<2;slot++) for (int hero=0;hero<4;hero++) MascotArtwork.Create(hero,slot);
        var ui = new LatencyProbeUi();
        for (int run=0;run<3;run++)
            for (int slot=0;slot<2;slot++)
                for (int hero=0;hero<4;hero++)
                {
                    samples.Add(ui.Measure(run,slot,hero,run==0?"first-in-sequence":"revisit-after-other"));
                    samples.Add(ui.Measure(run,slot,hero,"repeat-current"));
                }
        var changes=samples.Where(s=>s.Kind!="repeat-current" && s.FramesCreated>0).ToArray();
        var repeats=samples.Where(s=>s.Kind=="repeat-current").ToArray();
        bool red=changes.Any(s=>s.ReturnMs>budgetMs) || samples.Any(s=>!s.CorrectState);
        var report = new {
            CapturedUtc=DateTimeOffset.UtcNow,
            Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Scope="CPU seam only: actual extracted SelectCharacter plus unchanged production Core and MascotArtwork; Unity primitive/math/texture shims; no browser event dispatch, GPU, upload or paint",
            DiagnosticBudgetMs=budgetMs,
            Verdict=red?"RED_CPU_SELECTION":"GREEN_CPU_SELECTION_ONLY",
            ChangedSelectionCount=changes.Length,
            ChangedSelectionMedianMs=Median(changes.Select(s=>s.ReturnMs)),
            ChangedSelectionMinMs=changes.Min(s=>s.ReturnMs), ChangedSelectionMaxMs=changes.Max(s=>s.ReturnMs),
            RepeatedCurrentMedianMs=Median(repeats.Select(s=>s.ReturnMs)),
            StateUpdateMaxMs=samples.Max(s=>s.StateMs),
            Samples=samples
        };
        File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{report.Verdict}: {changes.Length} changed selections; min/median/max {report.ChangedSelectionMinMs:F2}/{report.ChangedSelectionMedianMs:F2}/{report.ChangedSelectionMaxMs:F2} ms; current-repeat median {report.RepeatedCurrentMedianMs:F4} ms; max state {report.StateUpdateMaxMs:F4} ms; threshold {budgetMs} ms.");
        Console.WriteLine($"Report: {Path.GetFullPath(reportPath)}");
        return red?1:0;
    }
    static double Median(IEnumerable<double> values) { var a=values.OrderBy(v=>v).ToArray(); return a.Length%2==0?(a[a.Length/2-1]+a[a.Length/2])/2:a[a.Length/2]; }
}
public sealed partial class LatencyProbeUi
{
    public GameState Game = RaceSimulation.CreateGame();
    float animationTime=0;
    double lastFrame;
    object tapClip=null;
    long started;
    double stateMs,frameMs;
    static double Elapsed(long start) { return (Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency; }
    void MarkState() { stateMs=Elapsed(started); }
    void MarkFrameReady() { frameMs=Elapsed(started); }
    void Beep(object clip) { }
    public Sample Measure(int run,int slot,int hero,string kind)
    {
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        int frames=UnityEngine.Texture2D.Created;
        started=Stopwatch.GetTimestamp();
        SelectCharacter(slot,hero);
        double elapsed=Elapsed(started);
        return new Sample(run,slot,hero,kind,stateMs,frameMs,elapsed,UnityEngine.Texture2D.Created-frames,
            GC.GetAllocatedBytesForCurrentThread()-allocated,Game.Players[slot].CharacterIndex==hero&&!Game.Players[slot].Ready);
    }
}
