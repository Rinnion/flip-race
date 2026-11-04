# 5.4.3 independent accepted track baseline

4 November 2026. Sol/high. Scope: JSON-03 migration acceptance, [STATUS](V543-JSON-TRACKS-STATUS.md). No production source was changed to generate this fixture.

The baseline is frozen at accepted Unity commit `14e22ba824a2d9195fba428bce2c42e4f93af401`, before the new loader/catalog implementation. An isolated `git archive` of that commit's Core directory was compiled from `Tools/JsonTracksCheck/obj/accepted14e22ba`; the build included exactly 27 archived Core files and the standalone capture/writer. It did not compile working-tree Core files.

Result: **eight full tracks / 15 bot track-profile combinations**. The [fixture](../Tools/JsonTracksCheck/accepted-14e22ba.json) records geometry, rewards/cues, nullable authored hidden values, route doubles, pre-migration profile/warning/camera policy, selected bot goals, reference inputs, opportunity boundaries, B/C/base/Master values and all five difficulty probabilities. Compiled-source hashes are recorded inside it.

| Artifact | SHA256 |
| --- | --- |
| `Tools/JsonTracksCheck/accepted-14e22ba.json` | `ABA65005AE1A4A4CD2CC6ED00013A413235AD440E47C5A02D69BC762CBB6F704` |
| Temporary accepted Core tar | `BF615F518D0EE9DBFFCBE0D51C610C220656AA2E53E79FA37426021041F86EE5` |

The fixture is immutable migration evidence, not new track data. The writer refuses to overwrite it. New-loader failures must be corrected in migration/data, not by updating this oracle. [Tool documentation](../Tools/JsonTracksCheck/README.md) records the generation boundary and capture sources.
