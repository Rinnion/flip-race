using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlipRace.Core;

// Compatibility entry point. Rectangle-specific ITER49 assertions and the
// active-avoidance witnesses are historical at3214a10, not evidence for FIX491.
// The current proof is shared with Tools/Fix491; timer/FIFO/contact unit cases
// live in Assets/Editor/EngineFinishTests.cs (--finish).
internal static class Program
{
    static int Main(string[] args)
    {
        if(args.Length>0&&args[0]=="--baseline")
        {Console.Error.WriteLine("Historical ITER49 baseline is frozen. Do not regenerate it for FIX491.");return 1;}
        if(args.Length>0&&args[0]=="--inventory")
        {
            foreach(var t in RaceTracks.All){var z=t.RiskZones.Single(v=>v.IsFinish);Console.WriteLine(t.Id+" strokes center="+z.X+","+z.Y+" outer24x20 stroke3 latest="+(z.X+26)+" runway="+(t.Length-z.X-26));}
            return 0;
        }
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        Console.WriteLine("ITER49 finish entry redirects to FIX491 frozen ordinary-bypass/deliberate-aim proof.");
        var start=new ProcessStartInfo("dotnet"){WorkingDirectory=root,UseShellExecute=false};
        foreach(string a in new[]{"run","--project","Tools/Fix491/Fix491.csproj","--","--proof"})start.ArgumentList.Add(a);
        using var process=Process.Start(start);process.WaitForExit();return process.ExitCode;
    }
}
