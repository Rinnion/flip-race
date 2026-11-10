# Explicit JSON catalogue migration

From the Unity repository, run explicitly:

```powershell
dotnet run --project Tools/TrackCatalogExport/TrackCatalogExport.csproj
```

This explicit export writes eight factory maps and sixteen accepted bot references to `Assets/StreamingAssets/Tracks/`, with manifest `index.json`. It refuses to overwrite authored targets and runs independently of the Unity build. After migration JSON is the runtime source.

To verify the original migration without writing JSON assets:

```powershell
dotnet run --project Tools/TrackCatalogExport/TrackCatalogExport.csproj -- --check
```

Pass an optional fresh output directory for a separate export. The tool uses shared Core `TrackJson` and compares every public stored Track field, including exact double bits, nullable overrides and original witness arrays. All sixteen input schedules, reward sets, B values and canonical write/read/write equality are checked. Results go to `Tools/TrackCatalogExport/Verification/migration-checks.json`. Independent catalogue integration checks cover reference and Master replays.

`Samples/my-mixed-track.json` is a standalone user file with a fresh ID, existing geometry and no bot references, ready to copy/edit for session import. Schema and trust rules are in `Verification/v543-json-contract.md`. Manifest `index.json` contains relative leaf filenames, not arbitrary remote URLs. This command is an explicit migration, not a build hook or solver.
