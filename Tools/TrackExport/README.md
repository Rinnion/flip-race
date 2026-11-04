# Export a current track without changing the game

From the Unity repository, with .NET 10 and no package installation:

```powershell
dotnet run --project Tools/TrackExport/TrackExport.csproj -c Release -- ../flip-race/docs/authoring/mixed-tricks <SOURCE_HEAD>
python Tools/TrackExport/Diagram.py ../flip-race/docs/authoring/mixed-tricks
```

Use a new output directory on a later run; all exports refuse existing filenames. Replace `<SOURCE_HEAD>` with the exact `git rev-parse HEAD` of the source being exported. This tool currently exports `TracksMixed.Get()` specifically, not arbitrary files.

Outputs: all actual public stored Track fields in camelCase (not derived getters); two actual `StochasticTrackMap` objects with initial difficulty settings and controller limits; world/profile/source-hash context and all authored route variants; a focused six-route replay report. JSON is a review/editing handoff format; the production game does not import these files.

Verification: serialize stored fields -> deserialize actual Track -> re-export and compare; freeze all Core file hashes before/after export; replay safe/full/bot-reference for each exact preset through production Press/Advance and RaceSnapshot. Distance input bisection is scheduling only; no alternate motion/collision code exists. Maximum outer replay step 1/120 s. This does not re-establish timing margins, bot statistics, all-FPS stability, or human enjoyment.

`Diagram.py` uses only Python's standard library to draw physical bands and zone locations from the exports as SVG, with separate X/Y scales. It creates technical reference graphics and does not edit game artwork.
