using System;
using FlipRace.Editor;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            Console.WriteLine(EngineParityTests.RunAll());
            Console.WriteLine(EngineV2Tests.RunAll());
            Console.WriteLine(EngineV3Tests.RunAll());
            Console.WriteLine(EngineV4Tests.RunAll());
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
