# JSON tracks v1 — shared contract

4 November 2026. Implementation contract for 5.4.3. Pure Core codec shared by .NET tools and Unity; no package dependencies or runtime solver. Physics version and schema version are independently fixed at 1. Builtin startup reads `StreamingAssets/Tracks/index.json`; normal builds copy these authored JSON files and never regenerate them from C#.

## Public interfaces

- `TrackJson.MaximumBytes = 2097152`; UTF-8 bytes, also suitable for the browser chooser's pre-read File.size guard.
- `TrackJson.TryRead(string json, out TrackPackage package, out string error)`; Russian path-specific errors, no partial result.
- `TrackJson.Write(TrackPackage package, bool pretty = true)`.
- `TrackJson.TryReadManifest(string json, out TrackManifest manifest, out string error)` and `WriteManifest(TrackManifest manifest, bool pretty = true)`.
- `TrackManifest.Files`: defensive `string[]`; relative leaf `.json` filenames, no URL/path components.
- `TrackPackage`: `Track`, computed `ContentHash`, `ProfilePolicy`, `WarningMode`, defensive `References`; constructor `(Track, TrackProfilePolicy = Any, TrackWarningMode = Selected, TrackBotReference[] = null)`.
- `TrackProfilePolicy { Any, ChosenOnly }`; Any preserves manual/custom human profiles on the existing seven unrestricted maps. ChosenOnly uses all exact Chosen values.
- `TrackWarningMode { Selected, Authored }`; UI uses metadata, not the ID `rethink`.
- `TrackPackage.SupportsProfile(FlightSettings)`, `TryGetReference(FlightSettings, out TrackBotReference)` and static `MatchesProfile(string, FlightSettings)`.
- `TrackBotReference`: immutable `ProfileId`, `TrackHash`, `BonusGain`, defensive `Inputs` and `RewardIds`; constructor `(string profileId, string trackHash, double[] inputs, string[] rewardIds, double bonusGain)`.
- `TrackFingerprint.Compute(Track, TrackProfilePolicy = Any, TrackWarningMode = Selected)`: deterministic lowercase SHA256 of all stored Track fields, policies and physics version. References excluded; JSON formatting/property order irrelevant.

## JSON shape

Single user file: `{ "schemaVersion":1, "physicsVersion":1, "profilePolicy":"any", "warningMode":"selected", "track":{...}, "botReferences":[...] }`. Policies may be omitted: Any/Selected. References optional. Each reference stores `profileId` (Chosen/FastFlip), `trackHash`, increasing distance-based `inputs`, selected raw `rewardIds` including one finish, and full served `bonusGain`.

Track fields use existing readable camelCase stored-field names. Required fields: track id/name/length/obstacles; each obstacle id/kind/x/width plus height for stones or gapTop/gapBottom for gates; each zone id/kind/x/y plus type-specific data. Missing optional collections become empty. Nullable hidden overrides and obstacleId retain null; explicit null/nonnumeric values for required fields fail. Unknown kinds/schema/physics versions fail. Duplicate JSON keys, object IDs, invalid links, out-of-bounds/nonfinite geometry, stale reference hashes and invalid schedules/reward sets/B fail before installation. No simulation runs at bootstrap.

Manifest: `{ "schemaVersion":1, "physicsVersion":1, "files":["full-long.track.json", ...] }`. Leaf filenames must match ASCII letters/digits/dot/underscore/hyphen and lowercase `.json`, without `..`. Eight current full maps only; practices remain C#. Depth 32; UTF-8 size 2 MiB; obstacles 1024; zones 4096; cues 1024; inputs 4096; maximum track length 1,000,000. Existing maps fit unchanged.

## Ownership and trust

Tracks specialist: codec/package/fingerprint, explicit offline exporter, eight JSON assets and index, round-trip proof. Core specialist: atomic catalogue installation, strict lookup, session import, actual-content identity and StochasticTrackMap integration. UI specialist: async manifest/file fetch and chooser, explicit failure/retry, dynamic metadata-driven menu. Root: acceptance/build/docs.

Builtin installation is atomic; errors never trigger a playable legacy C# fallback. Local import is session-only and never grants BOT trust from file-supplied references or an ID; duplicate/reserved IDs are rejected. Runtime BOT references require installed builtin provenance, recomputed matching content hash and exact profile. Existing C# factories remain offline export/test baselines. Exporter is an explicit one-time migration command, not a build hook.

## Implemented handoff

Codec, package/fingerprint and their `.meta` files are complete. `Assets/StreamingAssets/Tracks/index.json` lists eight current packages containing all fifteen accepted references; parent and per-JSON Unity metadata are present. The one-file user sample is `Tools/TrackCatalogExport/Samples/my-mixed-track.json`, ID `my-mixed-track`, name «Моя трасса», no references. All assets were generated once by the explicit migration command; normal builds must only copy them.

Executed: `dotnet run --project Tools/TrackCatalogExport/TrackCatalogExport.csproj` — PASS 8 maps / 15 references / 7617 assertions. Then `-- --check` — PASS 7660 assertions, checking the authored files without writing/regenerating them. Every public stored Track field was compared recursively, including exact double bits, nulls and original arrays; all reference schedules/IDs/B and canonical JSON write/read/write matched. Machine evidence: `Tools/TrackCatalogExport/Verification/migration-checks.json`. Independent accepted-HEAD baseline comparison, invalid/atomicity tests, fifteen reference/Master replays, Unity and browser checks belong to Core/UI/root.

No geometry, constants, legacy factory, runtime catalogue/map/controller or unrelated historical artifact was changed by this specialist. Production codec/assets are frozen after this handoff; no commit or Unity build was performed. One technical parent-directory creation failure was resolved by an accepted scoped directory-creation call; no policy refusal occurred.
