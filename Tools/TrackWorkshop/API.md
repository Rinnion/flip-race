# Track Workshop API

Local protocol v1. JSON properties use camelCase. API errors return HTTP 400 `{ "error": "readable detail" }`. JSON documents are bounded to 2 MiB. No paths or remote URLs are accepted. POST requires JSON content type and same-origin browser requests.

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

Strict production TrackJson validation is authoritative. If only `botReferences` prevent validation, preview validates a copy with references removed, explicitly sets `staleReferences`, reports the original error, and returns no usable references. `canonicalJson` is then null. `cleanedJson` always contains the validated document with all bot references removed, for an explicit save without references. Editor text is never silently changed. No solver or reference certification is provided.

`POST /api/replay` accepts `{json:string,profile:"Chosen"|"FastFlip",warningDuration:0.4|0.6|0.8|1|1.2,axis:"distance"|"time",inputs:number[]}`. Same preview validation applies. Inputs must be finite, nonnegative, strictly increasing, at most 4096. Distance is center X in world units; time is seconds since racing begins, excluding countdown. Out-of-race inputs remain unreached and never restart the race. Maximum simulated racing time is 120 seconds.

Returns `{profile,axis,warningDuration,staleReferences,referenceError,samples,inputs,events,hiddenStates,summary}`:

- `samples`: `[{time,x,y,speed,flipping,recovering,support,supportSide,collisions,boosts,slows,coins,finishBoost,status}]`. World center Y points down. Samples <=1/120 s apart plus exact input edges; maximum 20000. Interpolation is display only.
- `inputs`: `[{index,value,status:"accepted"|"ignored"|"unreached",time:number|null,x:number|null,y:number|null,reason:string|null}]`.
- `events`: `[{type:"collision"|"collected"|"reward"|"failedReward"|"negative"|"coin"|"finishBoost"|"hidden",id,time,x,y,phase:string|null}]`. Reward/collision positions are sampled observations within 1/120 s, not exact contact coordinates. Hidden transition times use engine clocks when available.
- `hiddenStates`: `[{id,phase:"Hidden"|"Warning"|"Solid",revealedAt,solidAt,activationPostponed}]` (nullable times).
- `summary`: `{status:"Finished"|"TimedOut",finishTime:number|null,elapsed,cleanFinish:boolean,collisions,boosts,instantBoosts,slows,coins,finishBoosts,acceptedInputs,ignoredInputs,unreachedInputs,collectedIds:string[],successfulIds:string[],failedIds:string[],hitIds:string[]}`.

Replay creates one production SOLO player at the floor with lagger camera (no elimination), runs normal readiness/countdown, then calls Press/Advance. Distance positioning uses independent production snapshots and bisection, never a current-speed assumption across bonus boundaries.
