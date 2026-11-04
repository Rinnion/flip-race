# NEWBOT-02 — five compatible bot references

4 November 2026. Implemented and checked with **Sol/high**. Root request: add bots to the three new courses, including Rethink, while preserving the common algorithm. Source chat `01a13ea2-10f9-7821-b785-5c3a7e30b079`; [delivery STATUS](V542-NEW-TRACK-BOTS-STATUS.md). Product contract: [BOT](../../flip-race/docs/features/bot-play.md), [HIDDEN](../../flip-race/docs/features/hidden-blocks.md), and the [compatible-100% clarification](../../flip-race/docs/flip-race-dense-tracks-plan.md#принятое-уточнение-эталона-бота).

## Ownership and API

New production files only: `Assets/Scripts/Core/BotReferenceRoutes.cs/.meta`. The specialist did not change geometry, old flight/hidden mechanics, catalogue, Controller/Map, UI or existing reference paths. Root/Core own integration. No Unity builds or commits were made here.

API agreed with Core:

- `BotReferenceRoutes.Supports(trackId, flight)` and `Get(trackId, flight)` support exactly the five new track/profile pairs.
- Immutable `BotReferenceRoute.Inputs` and `RewardIds` return defensive copies. IDs are raw zone IDs and include finish.
- `BonusGain` represents fully served selected ordinary rewards plus the actual finish gain, computed from `RaceConfig` constants. All supplied references have one full finish target; partial-effect accounting is unnecessary.

There is one fixed route per supported pair. Data assembly does not depend on seed, q, chosen intentions, warning duration, human position or previous errors. The physical track retains every reward. Core filters intentions and B to the selected set while keeping all logical obstacle opportunities/C.

## Final selected sets and useful splice coordinates

| Pair | Selected ordinary rewards; finish is also included | B / clean time |
| --- | --- | --- |
| Intercepts / Chosen | `pair-a-0`, `pair-b-0`, `landing-0`, `pair-a-1`, `pair-b-1`, `landing-1`, `landing-2`, `air-2` with prefix `hidden-intercepts-` | 2.65 / 33.35 s |
| Intercepts / FastFlip | All ten ordinary zones | 3.25 / 32.75 s |
| Manifestation / Chosen | All three `landing-0..2` and three `air-0..2` with prefix `hidden-manifestation-` | 2.05 / 33.95 s |
| Manifestation / FastFlip | Same six zones | 2.05 / 33.95 s |
| Rethink / Chosen | `rethink-m2-air`, `rethink-m3-price-bonus`, `rethink-m6-landing` | 1.15 / 34.85 s |

The full exact tapes and actual reward timestamps/poses are in [final proof JSON](v542-new-bot-reference-proof.json). Main edits to existing safe witnesses:

- Intercepts Chosen: first paired approaches at X2130/4530 combine both air rings with marked landings; the final pad uses X6995.181118735319 and final post-air X7370. Finish X7674. The following original two-input cross-gate preparations remain.
- Intercepts Fast: first two episodes use X2180/2290/2510/2560 and X4580/4690/4910/4960. Earlier returns at 2290/4690 add actual landings to both pair-plus-post-air approaches. Final pad uses X6990/7118.119611152856; post-air X7310/7360. Finish X7730.
- Manifestation Chosen: pads use stoneX−164.8188812646808; post-air stoneX+210. After the second post-air, one correction at X3940 replaces the two safe inputs at 3980.844877314131/4024.844877314131. Finish X7674.
- Manifestation Fast: each pad uses stoneX−170 and stoneX−41.8803888471439; post-air uses stoneX+150/+200. Finish X7730.
- Rethink: combine optional air input X2140, the ground-bonus branch with X2920, the landing branch at X6695.181118735319, and finish X7674. All three ordinary effects complete without affecting later clean modules.

## Compatibility investigation and exclusions

Only Intercepts Chosen excludes rewards: post-air IDs `hidden-intercepts-air-0`/`-air-1`, at X2620/X5020. The selected approach already takes both pairs and all marked landings. Twenty local two-input experiments tested adding the first post-air and changing the next cross-gate response. Some collected nine ordinary rewards, but all these extra-collection attempts hit the immediately following gate; none produced a better clean route. The repeated later episode uses the same approach. These finite attempts are recorded in [compatibility JSON](v542-new-bot-reference-compatibility.json); no global impossibility or optimum is claimed.

Nine additional Fast first-launch/return experiments found a useful mixed collection rather than preserving an artificial air/pad exclusion. The clean return at X2290 adds landing-0; applying the same local offset to the next episode adds landing-1. The resulting all-ten route is fully replayed, rather than inferred from symmetry alone.

## Verification and remaining integration

```powershell
dotnet run --project Tools/NewBotReferenceCheck/NewBotReferenceCheck.csproj
```

**PASS: 165 assertions / 26 full replays.** The first 135 assertions cover 21 normal cases, exact selected IDs, clean finish, fully served effects, actual B, defensive copies and exact-profile restrictions. All four Hidden pairs pass all five warning delays; Rethink keeps authored .35 warnings. The final 30 assertions replay all five references with every hidden obstacle initially Solid.

That initial-Solid group is an explicit conservative structural fixture, not a claim about obtaining a human lead from fresh spawn. Ownership is agreed with root/Core: real human-ahead resumed-state, common reveal/postponement, forecast independence and runtime stochastic q1/lower-level checks belong to Core/root. This specialist does not duplicate those controller tests. See [tool README](../Tools/NewBotReferenceCheck/README.md) for the exact boundary.

Production references are frozen. Root completes Core/runtime integration, old-track regression, selected-intention metrics, interface guards, actual build/browser/archive, current normative articles and final acceptance. No unresolved reference/geometry blocker remains. No geometry or stochastic planning algorithm was changed by this specialist, and no policy/tool refusal occurred during this delivery work.
