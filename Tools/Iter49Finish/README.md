# Finish verification after FIX491

From the Unity repository root:

    dotnet run --project Tools/Fix491/Fix491.csproj
    dotnet run --project Tools/Fix491/Fix491.csproj -- --proof
    dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --finish

The first command is the exact automatic-collection regression (frozen original Mixed inputs, actual no-action races and supported arrivals). The second checks all5tracks/bothprofiles/sixdt, full55unit gain and same TIMED input counterfactuals, deliberate timing shifts and30/60fps phase quantization. EngineFinishTests independently checks stroke contacts, tangencies, misses, large dt, timer/FIFO/recovery/pause/rematch.

The legacy Iter49Finish command forwards to the new proof. Its rectangular28x8 fixtures and active-avoidance author routes are historical at3214a10. Accepted iter49 verification artifacts remain unchanged; their PASS does not establish FIX491. The old baseline command refuses regeneration explicitly.
