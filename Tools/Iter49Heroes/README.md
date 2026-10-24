# ITER49 hero artwork check

From the Unity project root:

    dotnet run --project Tools/Iter49Heroes/Iter49Heroes.csproj -c Release

Uses production MascotArtwork, CharacterRoster and Core with the existing CPU-only Unity shim copied into this directory. Baseline.g.cs is the exact pre-stage02 MascotArtwork source with only the class renamed; its original source SHA256 is in Verification/iter49-02-art-before.sha256. No existing probe or historical report is changed.

Compares all 192 poses for each of four original heroes and both slots (1,536 exact ordered RGBA matches), plus eight original previews. Exercises all 192 poses for each new hero and both slots (768), checks nonempty interior bounds, repeated-pose/preview identity, animation in all ten existing cache groups, meaningful pumpkin split/hat and Jack body/hat identity, distinct eyes/blinks/hit/success/win, public motion and reaction selection and personal scarf/stitch differences. All twelve previews are checked.

Outputs hashes and counts to Verification/iter49-02-art.json, individual PNG pose examples and four contact sheets. Sheet columns: idle, run, air, recover, success, win, blink. Top row is the normal source; bottom row rotates the entire square 180 degrees for gravity visual QA. Native world/lane transforms are owned by existing callers and need browser verification; this harness does not emulate scene clipping, native texture upload or phone performance.

The maximum cold selection time is desktop .NET with shim rasterization and pixel retention; it is diagnostic only. It does not establish browser latency or a phone budget.
