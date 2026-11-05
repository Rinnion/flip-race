# Track Workshop API

HTTP protocol v1, Workshop 0.5. JSON properties use camelCase. Validation errors return HTTP 400 `{ "error": "readable detail" }`. JSON documents are bounded to 2 MiB; request body to 16 MiB. No paths or remote URLs are accepted. POST requires JSON content type and same-origin browser requests.

The default endpoint remains local `http://127.0.0.1:4186`. Deployment can set `WORKSHOP_BIND_ADDRESS`, `WORKSHOP_PORT` and an exact `WORKSHOP_PUBLIC_ORIGIN` (mandatory for non-loopback binding). Host and browser POST Origin must match the configured public origin; no wildcard CORS or forwarded-header trust is used. JSON CLI requests without Origin remain supported. TLS and authentication belong to the supplied Caddy deployment, whose app port is private.

`GET /healthz` returns `{status:"ok",version:"0.5.0"}` under the same host policy. By default two heavy POSTs (analyze, replay or prepare-bot combined) may run concurrently; `WORKSHOP_MAX_CONCURRENT_REQUESTS` accepts 1..32. Excess requests receive HTTP 429 with JSON error and `Retry-After: 1`, without an unbounded queue or body binding. Payloads and edited files are not persisted on the server; each simulation/preparation uses its own state and preparation never installs the process-wide game catalogue.

`GET /api/catalog` returns `{entries:[{id,name,file,profiles:["Chosen","FastFlip"]}]}`. `GET /api/catalog/{id}` returns `{json:string}` using only explicitly loaded manifest entries.

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

Returns `{profile,axis,warningDuration,staleReferences,referenceError,samples,inputs,events,hiddenStates,summary}`:

- `samples`: `[{time,x,y,speed,flipping,recovering,support,supportSide,collisions,boosts,slows,coins,finishBoost,status}]`. World center Y points down. Samples <=1/120 s apart plus exact input edges; maximum 20000. Interpolation is display only.
- `inputs`: `[{index,value,status:"accepted"|"ignored"|"unreached",time:number|null,x:number|null,y:number|null,reason:string|null}]`.
- `events`: `[{type:"collision"|"collected"|"reward"|"failedReward"|"negative"|"coin"|"finishBoost"|"hidden",id,time,x,y,phase:string|null}]`. Reward/collision positions are sampled observations within 1/120 s, not exact contact coordinates. Hidden transition times use engine clocks when available.
- `hiddenStates`: `[{id,phase:"Hidden"|"Warning"|"Solid",revealedAt,solidAt,activationPostponed}]` (nullable times).
- `summary`: `{status:"Finished"|"TimedOut",finishTime:number|null,elapsed,cleanFinish:boolean,collisions,boosts,instantBoosts,slows,coins,finishBoosts,acceptedInputs,ignoredInputs,unreachedInputs,collectedIds:string[],successfulIds:string[],failedIds:string[],hitIds:string[]}`.

Replay creates one production SOLO player at the floor with lagger camera (no elimination), runs normal readiness/countdown, then calls Press/Advance. Distance positioning uses independent production snapshots and bisection, never a current-speed assumption across bonus boundaries.

## Prepare a reference for the existing game bot

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

The Web UI stores one versioned localStorage record at `flip-race.workshop.draft.v1`. It preserves raw JSON (including invalid/empty text), raw inputs, filename, profile, axis and warning selection, with a 500 ms debounce plus pagehide flush. JSON is limited to 2 MiB UTF-8 and inputs to 128 KiB. Startup offers restore/clear and suspends replacement of a pending saved draft; restored text is revalidated and visual undo history is cleared. Quota, disabled or corrupt storage is reported without breaking editing; another tab's storage change pauses saving. This storage belongs to the browser origin/profile, is not an account/server backup, and does not include prepared results or visual history.
