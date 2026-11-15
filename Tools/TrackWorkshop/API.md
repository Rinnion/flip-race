# Track Workshop API

HTTP protocol v1, Workshop 0.5.16. JSON properties use camelCase. Validation errors return HTTP 400 `{ "error": "readable detail" }`. JSON documents are bounded to 2 MiB and HTTP request bodies to 16 MiB. POST requires JSON content type and same-origin browser requests.

The default endpoint remains local `http://127.0.0.1:4186`. Deployment can set `WORKSHOP_BIND_ADDRESS`, `WORKSHOP_PORT` and an exact `WORKSHOP_PUBLIC_ORIGIN` (mandatory for non-loopback binding). Host and browser POST Origin must match the configured public origin; no wildcard CORS or forwarded-header trust is used. JSON CLI requests without Origin remain supported. TLS and authentication belong to the supplied Caddy deployment, whose app port is private.

`GET /healthz` returns `{status:"ok",version:"0.5.16"}` under the same host policy. By default two heavy POSTs (analyze, replay, route-search or prepare-bot combined) may run concurrently. `WORKSHOP_MAX_CONCURRENT_REQUESTS` accepts 1..32. Excess requests receive HTTP 429 with JSON error and `Retry-After: 1` before body binding. The server keeps uploaded files and simulations in memory only. Each operation uses its own state and preparation leaves the process-wide catalogue intact.

`GET /api/catalog` returns `{entries:[{id,name,file,profiles:["Chosen","FastFlip"]}]}`. `GET /api/catalog/{id}` returns `{json:string}` using only explicitly loaded manifest entries.

## Shared run settings

Workshop 0.5.13 accepts the optional object below in `/api/replay`, `/api/route-search`, `/api/prepare-bot` and the WebSocket `start` message, alongside the existing profile and warning duration:

```json
{"surfaceRunSettings":{"enabled":true,"ratePercent":5,"curve":"Linear","capPercent":30,"airDecayPercent":200,"resetOnCollision":true}}
```

All six fields are required when the object is present. Rates are integers 1–5 percentage points per second; curve is `Linear` or `Exponential`; cap is 20, 30 or 40%; air decay is 0, 50, 100, 200 or 400% of the chosen rate; both switches are booleans. These map directly to Core SurfaceRunSettings. Invalid values are rejected; malformed request binding may return an empty HTTP 400 body.

Omitted/null settings preserve legacy API behavior: acceleration OFF with the Core default parameter values. A new Web session explicitly sends acceleration ON with the default values shown above. Replay responses and live states echo the effective settings. Live restart and positive-start prefix reconstruction use the same captured configuration. Standard Workshop SOLO uses the lagger camera without elimination; multiplayer presentation and custom flight physics are outside this API.

`POST /api/analyze` accepts `{json:string}` and returns:

```js
{
  id, name, length, corridorHeight:240, playerRadius:14,
  profilePolicy:"any"|"chosen-only", warningMode:"selected"|"authored",
  contentHash, gameValid:boolean, staleReferences:boolean, referenceError:string|null,
  warnings:string[], canonicalJson:string|null, cleanedJson:string,
  references:[{profileId,trackHash,inputs:number[],rewardIds:string[],bonusGain}],
  obstacles:[{id,kind,x,width,height,gapTop,gapBottom,topOffsetX,bottomOffsetX,
    hidden,hiddenRevealDistance:number|null,hiddenWarningDuration:number|null,
    bands:[{index,x,y,width,height}]}],
  rewards:[{id,kind,x,y,radius,obstacleId,placement,freeReward,clearX,
    shape:"circle"|"landing"|"finish",
    left:number|null,right:number|null,planeY:number|null,
    segments:[{x1,y1,x2,y2}],strokeWidth:number|null}],
  cues:[{x,text}], safeRoute:number[], riskRoute:number[]
}
```

Strict production TrackJson validation is authoritative. If only `botReferences` prevent validation, preview validates a copy with references removed, explicitly sets `staleReferences`, reports the original error, and returns no usable references. `canonicalJson` is then null. `cleanedJson` always contains the validated document with all bot references removed, for an explicit save without references. Editor text is never silently changed. Analyze/replay do not certify new references; use the separate preparation endpoint below.

`POST /api/replay` accepts `{json:string,profile:"Chosen"|"FastFlip",warningDuration:0.4|0.6|0.8|1|1.2,axis:"distance"|"time",inputs:number[]}`. Same preview validation applies. Inputs must be finite, nonnegative, strictly increasing, at most 4096. Distance is center X in world units; time is seconds since racing begins, excluding countdown. Out-of-race inputs remain unreached and never restart the race. Maximum simulated racing time is 120 seconds.

Returns `{profile,axis,warningDuration,staleReferences,referenceError,samples,inputs,events,hiddenStates,summary,landingIntervals}`:

- `samples`: `[{time,x,y,speed,flipping,recovering,support,supportSide,collisions,boosts,slows,coins,finishBoost,status}]`. World center Y points down. Samples <=1/120 s apart plus exact input edges; maximum 20000. Interpolation is display only.
- `inputs`: `[{index,value,status:"accepted"|"ignored"|"unreached",time:number|null,x:number|null,y:number|null,reason:string|null}]`.
- `events`: `[{type:"collision"|"collected"|"reward"|"failedReward"|"negative"|"coin"|"finishBoost"|"hidden",id,time,x,y,phase:string|null}]`. Reward/collision positions are sampled observations within 1/120 s, not exact contact coordinates. Hidden transition times use engine clocks when available.
- `hiddenStates`: `[{id,phase:"Hidden"|"Warning"|"Solid",revealedAt,solidAt,activationPostponed}]` (nullable times).
- `landingIntervals`: `[{landingTime,landingX,landingY,inputIndex,inputTime,inputX,inputY}]`. A completed, uninterrupted flight must reach real support and remain on that support through the accepted press. Times are elapsed game seconds. Landing time uses the Core flight-end clock. Landing coordinates are observed within 1/120 s after contact. The browser filters this optional replay response field by its display threshold.
- `summary`: `{status:"Finished"|"TimedOut",finishTime:number|null,elapsed,cleanFinish:boolean,collisions,boosts,instantBoosts,slows,coins,finishBoosts,acceptedInputs,ignoredInputs,unreachedInputs,collectedIds:string[],successfulIds:string[],failedIds:string[],hitIds:string[]}`.

Replay creates one production SOLO player at the floor with lagger camera (no elimination), runs normal readiness/countdown, then calls Press/Advance. Distance positioning uses independent production snapshots and bisection, never a current-speed assumption across bonus boundaries.

## Test a later ground press

Workshop 0.5.16 adds `POST /api/later-press` with `{json,profile,warningDuration,surfaceRunSettings?,axis,inputs,inputIndex,minimumMs:200}`. `inputIndex` is zero-based; `minimumMs` is an integer 0–2000, default 200. The selected original landing interval must be shorter than that threshold. The original replay must finish with every command accepted/reached. Distance tapes are normalized to actual command times and checked for equivalent outcomes, input positions/times and landings before testing.

Candidates are `landingTime + (minimumMs + k*50)/1000`, k=0..20, strictly later than the original command and at least 1e-6 seconds before the next unchanged command. All other absolute command times stay fixed. Every completed probe runs to its actual finish/time limit in Core. It must retain the original uninterrupted landing and preserve achieved positive goals, with no extra collisions, slow effects, new hit IDs or failed goal IDs. Avoiding an original trap is allowed. A later finish can still succeed.

The response is `{status,message,attempted,candidateCount,complete,stopReason,stepMs:50,spanMs:1000,baseline,alternatives}`. `status` is `ready` or `unsupported`; the latter explains unsuitable baseline data. `baseline` contains the normalized `inputs`, `finishTime`, `landingTime`, `inputTime`, `inputIndex`, and `collisions`. Each successful alternative contains `{delayMs,inputTime,finishTime,finishDelta,collisions,inputs,summary}`. Replay its full timed `inputs` with the captured source/settings through `/api/replay` to display its trajectory; do not duplicate all trajectory frames in this response.

There are at most 21 eligible candidates and a 10-second protective budget, including baseline verification. `attempted` counts fully completed probes. `complete:false` and `stopReason:"time-budget"` mean untested work remains; interrupted probes are not counted as failures. Client disconnect cancels work. The same origin, JSON-body and heavy-request concurrency policies apply. Nothing is written to developer JSON by this endpoint. No alternatives means none were found in these probes, not that later action is impossible; successful times may have gaps.

## Search and store route variants

Since Workshop 0.5.15, `POST /api/route-search/stream` accepts the same body as `/api/route-search` and streams `application/x-ndjson`. The old endpoint remains available. Lines are `{type:"progress",percent:integer,phase:"search"|"verify"|"save"}`, followed by `{type:"result",result:{analysis,sourceJson}}`. Percent is monotonic 0–99; the client shows 100 only after applying a valid final result. Search progress estimates consumption of bounded work budgets; verification uses completed candidates. Phase boundaries may jump when work finishes early. This is not the fraction of all possible paths.

Pre-stream validation uses normal HTTP errors. A failure after streaming begins is `{type:"error",error:string}`. Disconnecting cancels work. The stream uses the same origin, JSON-body and concurrency policy as other heavy requests. Clients must handle partial UTF-8 chunks/lines, missing final results, cancellation and obsolete requests, without displaying false completion. Intermediate progress is not persisted in developer JSON.

`POST /api/route-search` accepts `{json:string,profile:"Chosen"|"FastFlip",warningDuration:0.4|0.6|0.8|1|1.2,surfaceRunSettings?}`. The request is cancellable on client disconnect. It branches on one legal production press or waiting at 100 ms decision points, from race time zero, for the selected supported profile. It uses the same selected acceleration and SOLO settings as replay. Any finished route may qualify, including a route with collisions. This is separate from the stricter legacy bot-reference preparation.

The response is `{analysis,sourceJson}`. `sourceJson` preserves author content and updates only the reserved root `workshopAnalysis` section. Since Workshop 0.5.14 the saved format is `{version:2,groups:[{settings:{profile,warningDuration,surfaceRunSettings},runs:[...]}]}`. Every group currently contains exactly one search run. Its payload stores the fingerprint, rules version, flight-settings snapshot, warning mode, start X, time axis/origin, decision and simulation steps, race-time bound, search counters, stop reason and routes. The four former settings fields (`profile`, `warningDuration`, `surfaceRun`, `surfaceRunSettings`) are not repeated in that payload. Group identity compares the profile, warning duration and all six surface-run fields, including retained values when acceleration is OFF.

One new search replaces only its exact settings group. Other groups of the same or another profile are retained. Supported version-1 flat data migrate to grouped version 2 on the next search. Read-only analysis does not rewrite the author's JSON. For client compatibility HTTP `analysis` stays normalized as `{version:2,runs:[...]}` with each run's settings rehydrated; it is not the on-disk representation. The UI lists only runs matching its current complete settings, sorted fastest first, with pages of 100 rows.

Each route has `{finishTime,collisions,boosts,slows,coins,finishBoosts,inputTicks}`. `inputTicks` is a strictly increasing array of nonnegative integer decision ticks. With `stepMs:100`, tick 13 is a press at 1.3 seconds, excluding countdown. Routes are replay-verified, distinct and sorted by actual finish time, then number of presses. At most 1000 are retained. The finite search may return fewer; its counters and reason describe the limit reached, and neither global optimality nor human success probability is claimed.

`searchParameters` records algorithm `beam-v1`, beam width 768, up to 12 prefixes per coarse state bucket, 920000 transitions, 11000 ms search time and 20000 ms verification time. Coarse buckets prune search only; they never alter collision physics. `foundCount` counts preliminary finished candidates; at most the fastest 1128 candidates are independently replayed, and `verifiedCount` counts successful checks. `storedCount` is the actual saved list size after the 1000-route and file-size limits. `beamPruned` records discarded alternatives. `elapsedMilliseconds` includes verification. Race simulation is capped at 120 seconds.

`POST /api/analyze` additionally returns `routeAnalysis:{status,analysis?}`. Supported current data are available for selection; missing, stale, malformed or future-version data are reported rather than silently treated as current. Geometry/fingerprint and rules/configuration changes require a new search. Switching current UI controls does not invalidate the saved parameters of another run.

To display a stored route, call `/api/replay` with its profile, warning, `surfaceRunSettings`, `axis:"time"`, and `inputs: inputTicks.map(t => t * stepMs / 1000)`. This restores the authoritative trajectory, events and landing intervals. Old version-1 `surfaceRun:false` runs without a snapshot normalize to OFF defaults. ON without a complete snapshot, or a mismatch between the flag and snapshot, is invalid. Serialized metrics are analysis data, not trusted game-bot admission.

The complete developer document remains bounded to 2 MiB; if the results cannot fit, storage retains the fastest fitting prefix and reports `storageLimited`. Workshop excludes the reserved metadata section before the production track parser, so bulk analysis does not consume the game's JSON-node budget. Dedicated prepared game exports omit developer analysis; the original author JSON retains it. Unknown extensions are not executed or interpreted as physics.

## Live solo attempt

`GET /api/live` upgrades to a WebSocket at the same Workshop address. The handshake requires the configured Host and a nonempty exact browser Origin. Plain HTTP and mismatches return HTTP 400. Two live connections are allowed independently of the heavy POST limit. A third receives HTTP 429 with `Retry-After: 1`. The same site authentication applies when using the supplied Caddy deployment.

Send one text JSON message `{type:"start",json,profile,warningDuration,startX?,axis?,inputs?}` within 15 seconds. The source, profile and warning choices use the same validation and stale-reference handling as replay. With `startX` omitted or zero, each connection creates a fresh production SOLO attempt and starts the normal three-second countdown. This mode ignores the optional author axis and inputs.

A positive `startX` must be finite and below the course length. The server runs the same production solo physics and replay scheduler from the beginning with the author presses before the marker. `axis` defaults to `distance` and `inputs` defaults to an empty list. The first ordinary racing sample at or past the marker becomes the live state. A press at the handoff belongs to live input. The camera, flight, recovery, collected rewards, hidden timers and elapsed race time carry through. The initial state has `seq:0`, reports the actual X in `sample.x`, and is held until the client sends steps. The total 120 seconds includes the prefix. Reconstruction has an eight-second budget. The server reports an error if the point is unreachable before the terminal sample, the prefix exceeds its budget, or two executed prefix presses share a timestamp that the editor cannot represent.

Then send ordered messages `{type:"step",seq,steps,inputs?}` with contiguous integer sequence numbers starting at 1. Each message requests 1–30 fixed ticks of 1/120 second. Optional `inputs` contains strictly increasing integer tick offsets from 0 through `steps-1`. The Core evaluates each press before advancing that tick. Countdown presses are reported as ignored. Race time and the countdown advance only through these commands, so pausing means sending no steps. To stop or restart, close the connection and open a new one.

The server replies to each command with the following state.

```js
{
  type: "state", seq,
  phase: "countdown"|"racing"|"finished"|"timedOut",
  countdownRemaining, cameraLeft, requestedStartX,
  sample, inputs, events, hiddenStates, summary,
  staleReferences, referenceError, prefixInputs
}
```

`sample`, `inputs`, `events`, `hiddenStates` and terminal `summary` use the replay shapes above. Input indices count presses from 0, including the author prefix for a positive start. New live input values use simulated racing seconds and remain zero in the countdown. Events cover the ticks processed since the previous response. For a positive start, the initial frame includes earlier events and `prefixInputs` holds the accepted and ignored author presses with their actual racing times. Later frames have empty `prefixInputs`. The initial `hiddenStates` contains every hidden obstacle, and subsequent responses include only changed states, keyed by ID. `cameraLeft` is the Core lagger camera's world left edge for the 1520 × 240 field. At finish or after 120 racing seconds the server returns a terminal summary and closes normally. Inputs remaining within the terminal batch are unreached.

The start envelope allows JSON escaping of a 2 MiB source. Each step message is capped at 1 KiB. The rate budget sustains up to 45 messages and 180 ticks per second with bursts of up to 12 messages and 120 ticks. A connection expires after 120 seconds without a step or ten minutes of wall time. After upgrade a protocol error yields `{type:"error",error}` and closes the socket. The attempt is held only for that connection.

## Prepare a reference for the existing game bot

With explicit shared settings, preparation has two separate results. Compatible `botReferences` and `preparedJson` retain the game's unchanged OFF admission and complete-effect proof. Afterwards each ready profile gains `masterTrial`, a real production Master run in one-participant SOLO under the requested settings and warning. It reports status, exact accepted `inputTimes`, settings, finish time, outcome counters, `clean`, `goalsMet`, missed/successful/failed reward IDs, hit IDs, seed, camera and participant count. ON may finish before an effect's remaining duration expires; collecting the required finish target does not assert a full-duration boost.

`masterTrial.replayVerified` is true only when independent timed Workshop replay reproduces the actual outcome. The Web UI displays this selected-config trajectory only when verified. Failed or timed-out selected trials do not revoke a separately successful OFF export; both results are labeled explicitly. Omitting shared settings keeps the legacy preparation behavior without the extra trial. No import format or admission rules in the game are changed.

Since Workshop 0.5.5 the browser can use a time-based author list as a preparation hint: it first calls `/api/replay` with `axis:"time"` and the captured source/profile/warning, then passes accepted, finite, strictly increasing X positions to `/api/prepare-bot`. The author's seconds list stays unchanged. This conversion does not change the preparation endpoint's distance-only input contract or its independent reference/Master validation.

`POST /api/prepare-bot` accepts:

```js
{
  json: string,
  profiles?: ["Chosen"|"FastFlip"],
  inputs?: number[],
  outputTrackId?: string,
  warningDuration?: 0.4|0.6|0.8|1|1.2
}
```

- `profiles` contains one or two known distinct profile IDs. If omitted/null, it defaults to both profiles for `any`, or `Chosen` for `chosen-only`. Explicitly asking for the prohibited FastFlip profile yields its `unsupported` report; it does not bypass the map policy. The Web UI submits the selected current profile only.
- `inputs` is an optional distance-X seed, never a required route. Values must be finite, nonnegative, strictly increasing, at most 4096. Seconds are not supported here. Invalid request inputs yield HTTP 400. Candidate seed coordinates outside the course are unusable hints, not valid route proof. Existing/stale copied reference inputs may also be used as hints; malformed copied hints are ignored, never trusted.
- `outputTrackId` defaults to the source ID for custom maps or `custom-<source-id>` for reserved/built-in sources. Explicit reserved IDs yield HTTP 400. The resulting document is normalized and validated by TrackJson before its geometry/reference hash is calculated. IDs must contain 1..128 non-control characters and be nonblank. Uniqueness against another game's local session cannot be checked by this service.
- `warningDuration` defaults to `0.8` and is a search hint. It does not narrow final hidden-map verification. `selected` hidden maps require reference + actual Master runs at all five durations; authored maps use their individual timers, while maps without hidden obstacles need one configuration.

HTTP 200 response:

```js
{
  status: "prepared"|"not-found",
  preparedJson: string|null,
  outputTrackId: string,
  profiles: [{
    profileId: "Chosen"|"FastFlip",
    status: "ready"|"not-found"|"unsupported",
    message: string,
    inputs: number[], rewardIds: string[], finishTime: number|null,
    expandedStates: number, elapsedMilliseconds: number
  }],
  message: string
}
```

`prepared` means at least one requested profile passed. `preparedJson` is a complete separate schema-1/physics-1 document containing **only ready references** and the output ID; it is null if none passed. For a not-found profile, diagnostic candidate inputs/IDs/time may be returned when a route was found but final proof failed; they are not certified or exported as references. `elapsedMilliseconds` covers the profile's search + verification, including unsupported checks, rather than only the beam search. The response contains no replay samples: use `/api/replay` with `preparedJson`, the ready profile/inputs and `axis:"distance"` to display the generated path.

Preparation uses normal production Press/Advance and a bounded beam search: 128 retained states, up to 180000 expansions and 8000 ms per profile, 0.05 s search slices, 4096 presses, and 120 racing seconds per candidate. A short finite improvement tail after the first clean finish is allowed within the same budget. Final reference + actual Master verification shares a 20000 ms per-profile budget measured from the start of search, and additionally stops after 100000 proof steps. Bounds and cancellation are checked between atomic Core operations, so an individual simulation/controller operation may finish after a wall-clock deadline. Two profiles are processed sequentially. The request's `RequestAborted` is propagated; disconnect/AbortController cancellation aborts work rather than returning a ready result, and always releases request capacity.

Supported references require a clean finish, actual successful positive IDs including exactly one fully served finish target, completed ordinary reference effects, exact reference gain B, at least one logical obstacle C, and legal B/C for the unchanged stochastic model (no probability clamping). The reference is independently replayed with the exact selected ID set and full gain/time. The actual Master must finish cleanly, successfully collect selected IDs, complete at least the selected ordinary effect count and serve the full finish effect. Incidental extra positives are permitted; the engine's Master contract uses an aggregate completed-effect count, not a per-ID pulse ledger. Invalid geometry/schema/request options yield HTTP 400; a restricted profile, C=0 or missing finish yields `unsupported`. Finite search/proof failure yields `not-found`; it never proves that the map is impossible or the returned route globally optimal.

Download `preparedJson` separately from the author's original draft, then import it into **Flip Race Unity 5.4.4** through the game's local JSON importer. The game independently replays the reference and actual Master before admitting BOT for that profile; a forged valid hash alone does not grant BOT. Until verification succeeds, the imported course remains available to human modes. Workshop does not mutate game catalogue files or calibrate a new bot algorithm.

## Browser draft (not an HTTP API)

The same version-1 record also stores `surfaceRunSettings`. Missing settings in an old draft normalize to acceleration OFF. Unknown extension fields are preserved. Selecting an analysis route restores its saved configuration before replay.

The Web UI stores one versioned localStorage record at `flip-race.workshop.draft.v1`. It preserves raw JSON (including invalid/empty text), raw inputs, filename, profile, axis and warning selection, with a 500 ms debounce plus pagehide flush. JSON is limited to 2 MiB UTF-8 and inputs to 128 KiB. Startup offers restore/clear and suspends replacement of a pending saved draft; restored text is revalidated and visual undo history is cleared. Quota, disabled or corrupt storage is reported without breaking editing; another tab's storage change pauses saving. This storage belongs to the browser origin/profile, is not an account/server backup, and does not include prepared results or visual history.
