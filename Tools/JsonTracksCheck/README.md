# JSON track migration acceptance

The frozen fixture `accepted-14e22ba.json` is the independent pre-migration oracle. Do not regenerate or update it to make a new loader pass.

- Accepted commit: `14e22ba824a2d9195fba428bce2c42e4f93af401`.
- Fixture SHA256: `ABA65005AE1A4A4CD2CC6ED00013A413235AD440E47C5A02D69BC762CBB6F704`.
- Source archive SHA256: `BF615F518D0EE9DBFFCBE0D51C610C220656AA2E53E79FA37426021041F86EE5`.
- Inventory: eight full tracks and 15 supported bot track/profile combinations, compiled from 27 accepted Core files.

Generation was performed once, before runtime JSON integration, using `git archive --format=tar 14e22ba Assets/Scripts/Core` extracted under this tool's `obj/accepted14e22ba`. The temporary `AcceptedSnapshot.csproj` compiled only those extracted Core files plus `BaselineCapture.cs` and `AcceptedBaselineWriter.cs`; no current working-tree Core source was part of that build. The fixture records all compiled-source SHA256 values as well as the full accepted revision. The writer refuses to overwrite an existing fixture.

The fixture includes exact double-valued geometry, offsets, nullable hidden metadata, cues/rewards, route arrays, legacy profile/warning/camera policy, and all bot map/reference inputs, selected goal IDs, opportunity boundaries, B/C/base/Master time and five-level probabilities. JSON serialization uses round-trip double formatting. The future acceptance runner must compare actual `TrackJson`/`TrackCatalog` results to this oracle, not invoke the old generators as its main source.

`BaselineCapture.cs` is the explicit normalization projection shared with acceptance; package profile/warning policy must also be checked directly, because pre-migration policy was derived from legacy code rather than stored on Track. `AcceptedBaselineWriter.cs` is a generation tool only and is excluded from the normal acceptance runner.

## Acceptance command

From the Unity repository:

```powershell
dotnet run --configuration Release --project Tools/JsonTracksCheck/JsonTracksCheck.csproj -- --full
```

The runner reads the actual authored `Assets/StreamingAssets/Tracks/index.json` and its listed files through the production `TrackJson`/`TrackCatalog` APIs. It does not use the C# geometry factories as the migrated source. `--data` omits only the 15 runtime Master races; the 15 ordinary reference replays still run. `--compile` checks only the shared Core API compilation and is not acceptance evidence.

The full fixed schedule contains exact comparisons against all eight frozen geometries and 15 bot maps/reference arrays/probability sets, then 15 reference replays and 15 seed-0 Master races. Physical goal IDs, full selected effects, clean finish/time, original horizon/candidate caps and camera speed are checked. No empirical calibration or global reference optimization runs.

The negative table checks malformed JSON/versions, duplicate keys, required/optional nulls, unknown kinds/policy, nonfinite/out-of-bounds numbers, duplicate IDs, invalid links, too-short authored warning, stale references and invalid input schedules/B. Builtin missing files, malformed file contents, invalid manifest paths, duplicate/reserved IDs and failed local imports preserve the prior catalogue. A malformed first install is tested before any successful one: `RaceTracks.All` must stay empty and a known full ID must throw rather than return a legacy factory map.

Additional integration fixtures cover actual-content mutation under a known ID, a local file with structurally valid declared references that still has no BOT trust, imported ChosenOnly/Authored metadata under a new ID, preserved legacy practice restrictions, explicitly changed known-ID builtin without references, and a ninth human-only map added through the persistent manifest. The latter starts with production SOLO rules, remains BOT-disabled, and catalogue reload preserves local session entries.

The command writes only `Verification/v543-json-check.json`, recording Core source hashes before/after, negative errors and the 30 race results. It verifies the frozen fixture SHA256 before reading it. Managed checks do not establish browser fetch/file-chooser behavior, native phone interaction, visible error layout or final build contents; those remain separate UI/root acceptance.
