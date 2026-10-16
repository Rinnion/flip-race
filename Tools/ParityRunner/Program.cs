using System;
using FlipRace.Editor;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--finish")
            {
                Console.WriteLine(EngineFinishTests.RunAll());
                return 0;
            }
            if (args.Length == 1 && args[0] == "--control")
            {
                Console.WriteLine(EngineControlTests.RunAll());
                return 0;
            }
            if (args.Length == 1 && args[0] == "--tune-instant-preemption")
            {
                Console.WriteLine(EngineTuneTests.RunInstantPreemptionRegression());
                return 0;
            }
            if (args.Length == 1 && args[0] == "--v4-post-hit-ring")
            {
                Console.WriteLine(EngineV4Tests.RunPostHitRingRegression());
                return 0;
            }
            Console.WriteLine(EngineParityTests.RunAll());
            Console.WriteLine(EngineV2Tests.RunAll());
            Console.WriteLine(EngineV3Tests.RunAll());
            Console.WriteLine(EngineV4Tests.RunAll());
            Console.WriteLine(EngineTuneTests.RunAll());
            Console.WriteLine(EngineControlTests.RunAll());
            Console.WriteLine(EngineFinishTests.RunAll());
            if (args.Length > 0) Console.WriteLine(EngineParityTests.CompareFixtures(args[0]));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
