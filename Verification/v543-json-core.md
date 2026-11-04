# 5.4.3 JSON catalogue integration and independent acceptance

4 November 2026. Sol/high. Source request and delivery ownership: [STATUS](V543-JSON-TRACKS-STATUS.md). Accepted source baseline: `14e22ba824a2d9195fba428bce2c42e4f93af401`. No geometry, flight/control parameter, hidden-timer algorithm, bot search budget, probability formula or seed behavior was changed by this specialist.

## Production changes

- `Assets/Scripts/Core/TrackCatalog.cs/.meta`: atomic builtin installation and session import; strict entries/lookup; builtin/local provenance; metadata-driven human-profile, authored-warning and prepared-bot predicates. Local imports cannot replace existing/reserved identities or acquire BOT trust from file-supplied references. Reload preserves existing local session entries.
- The `RaceTracks` block in `Assets/Scripts/Core/TracksRhythm.cs`: after a catalogue installation attempt, full runtime lookups use the installed data strictly. The old factories remain lazily available only in the offline/no-install path. The two historical alias IDs still normalize explicitly; known code-authored practices remain separate explicit paths.
- `Assets/Scripts/Core/StochasticTrackMap.cs`: installed-catalog references replace legacy ID/reference dispatch for runtime full tracks. The actual track fingerprint must match the installed builtin package and reference hash, with the exact profile. Empty/untrusted reference data never falls back to the old hard-coded route. All obstacle opportunities and the selected-reward/B semantics remain unchanged.

The codec, package/fingerprint model, authored JSON assets and exporter are owned by the track specialist. UI/bootstrap/file chooser are owned by the UI specialist. Root owns build/browser/archive acceptance and current product documentation. No commits or Unity builds were made here.

## Independent immutable oracle

[Baseline report](v543-json-baseline.md) and [fixture](../Tools/JsonTracksCheck/accepted-14e22ba.json): eight full tracks / 15 track-profile combinations captured by compiling **only** the accepted commit's 27 archived Core files. Working-tree loader/catalog code did not participate. Fixture SHA256 is `ABA65005AE1A4A4CD2CC6ED00013A413235AD440E47C5A02D69BC762CBB6F704`; the writer refuses overwrite and the acceptance runner verifies this hash. The oracle was not updated after the new loader was written.

## Executed result

```powershell
dotnet run --configuration Release --project Tools/JsonTracksCheck/JsonTracksCheck.csproj -- --full
```

**PASS: 19103 assertions, 15 pairs, 30 actual races, 41 negative fixtures.** [Machine evidence](v543-json-check.json) records exact source hashes, errors and race results. Core hashes remained unchanged during the run.

All eight geometries, stored reward/cue fields, offsets, nullable hidden values, route doubles and metadata match the accepted oracle. All 15 loaded bot maps retain exact reference inputs, selected goal IDs, opportunity boundaries, B/C/base/Master values and five-level probabilities. The runner explicitly checks package policy rather than inferring the new metadata from a known ID. Codec read/write roundtrips preserve each content hash; runtime `RaceTracks.Get/GetAt` returns the installed Track instance.

Each loaded pair completes one ordinary reference replay and one real seed-0 Master run using production Press/Advance. Both old profiles on seven maps and Chosen on Rethink finish clean with selected rewards and full effects; horizons and candidate caps remain unchanged. Observed Master times are 33.05 s for each old pair, 33.35/32.75 s for Intercepts Chosen/Fast, 33.95 s for both Manifestation profiles and 34.85 s for Rethink/Chosen. No route solver or calibration series was introduced.

## Failure and trust boundaries verified

- The first malformed builtin install happens before any successful install: catalogue remains uninstalled, `RaceTracks.All` is empty, and a known full-track lookup throws. It cannot silently return a legacy C# map.
- After a valid install, missing files, malformed file contents, invalid manifest paths, duplicate builtin IDs and reserved practice IDs fail atomically, retaining the previous catalogue. Local bad/duplicate/reserved imports likewise publish nothing.
- Required nulls, explicit null arrays, unknown obstacle/reward kinds, unsupported schema/physics/policy, duplicate JSON keys/object IDs, nonfinite/out-of-bounds values, bad landing links, invalid hidden timing, stale reference hashes, missing selected IDs, unordered inputs and wrong B fail before installation.
- A cloned builtin Track with modified actual geometry cannot reuse its former reference merely through the same ID. A valid local package carrying recomputed, structurally correct reference data remains Local and BOT-disabled.
- An explicitly modified known-ID builtin with references removed loads its new human geometry and stays BOT-disabled; it does not inherit either old geometry or old reference through fallback.
- An imported new-ID ChosenOnly/Authored package uses metadata correctly and preserves the user's global warning choice. Known legacy practice/control entries keep their existing human-profile support; an unknown ID does not gain it.
- A ninth reference-free human map added to the persistent manifest is installed and starts through ordinary SOLO readiness/Advance. It is a full catalogue entry with builtin provenance but remains BOT-disabled. Reload preserves prior local session imports.

## Remaining acceptance and limits

Before Core receives a builtin installation attempt, offline factories intentionally remain available for historical tooling. During asynchronous startup fetch/404, the UI must therefore keep Game/startup gameplay unavailable and display loading/error state; it must not access offline full tracks as a substitute. Root and the UI specialist were explicitly notified of this bootstrap boundary. The malformed-JSON path after the Core install call is independently covered here.

Managed acceptance establishes data/physics/catalogue behavior. It does not establish actual URL fetching, native file-picker cancellation/phone behavior, visible loading/error messages, JSON inclusion in WebGL/ZIP, or browser performance. Root owns those checks, along with final delivery acceptance. Runtime parsing validates structure and identity; it does not solve or prove playability of arbitrary user-authored maps.
