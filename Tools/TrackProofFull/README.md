# TrackProofFull

Tool-only proof of the authored 4.4.0 tracks using the production Core, .NET 10, no NuGet packages. From the Unity project root run `dotnet run --project Tools/TrackProofFull/TrackProofFull.csproj -- --verify`.

Commands: `--stage1` narrow landing/chain and rock rule; `--verify` routes, actual finishes, authored finish entry and local safe timing; `--windows` full-route timing windows; `--recovery` sampled actual errors plus150ms reaction; `--routes` browser JSON export. `--full-draft` prints input states for diagnosis. No command changes runtime geometry.

Results, scope, exact hashes and the unresolved late-pending finish constraint are in [Verification/TRACKS-GEOMETRY.md](../../Verification/TRACKS-GEOMETRY.md). The shared scheduler in TrackProofControl/Harness.cs performs actual-speed distance actions, including effect changes inside a large step, through snapshots of production Advance.
