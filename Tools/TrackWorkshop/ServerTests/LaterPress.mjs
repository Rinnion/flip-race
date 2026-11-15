import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
const [base, output] = process.argv.slice(2);
const surface = { enabled: true, ratePercent: 5, curve: 'Linear', capPercent: 30, airDecayPercent: 200, resetOnCollision: true };
const json = JSON.stringify({ schemaVersion: 1, physicsVersion: 1, profilePolicy: 'any', warningMode: 'selected',
  track: { id: 'later-http', name: 'Поздние нажатия', length: 1500, obstacles: [], riskZones: [] }, botReferences: [] });
async function post(path, body, status = 200, signal) {
  const response = await fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: base }, body: JSON.stringify(body), signal });
  const text = await response.text(); assert.equal(response.status, status, text.slice(0,250));
  return text ? JSON.parse(text) : null;
}
const cases = [];
for (const profile of ['Chosen','FastFlip']) for (const enabled of [false,true]) {
  const config = { json, profile, warningDuration:.8, axis:'time', surfaceRunSettings:{...surface,enabled} };
  const flight = await post('/api/replay', {...config,inputs:[0]});
  const contact = flight.samples.find(s => s.time > .01 && !s.flipping && s.support === 'Wall');
  assert.ok(contact, 'a real landing follows the first command');
  const inputs = [0,contact.time+.05];
  const baseline = await post('/api/replay', {...config,inputs});
  const interval = baseline.landingIntervals.find(i=>i.inputIndex===1);
  assert.ok(interval && interval.inputTime-interval.landingTime < .2);
  const request = {...config,inputs,inputIndex:1,minimumMs:200};
  const result = await post('/api/later-press',request);
  assert.equal(result.status,'ready'); assert.equal(result.complete,true);
  assert.equal(result.candidateCount,21); assert.equal(result.attempted,21);
  assert.ok(result.alternatives.length>0);
  for (const alternative of result.alternatives) {
    assert.ok(alternative.delayMs>=200 && alternative.delayMs<=1200);
    assert.equal(alternative.inputs[0],inputs[0]);
    const replay = await post('/api/replay',{...config,inputs:alternative.inputs});
    assert.equal(replay.summary.status,'Finished');
    assert.ok(Math.abs(replay.summary.finishTime-alternative.finishTime)<1e-7);
    assert.deepEqual(replay.summary,alternative.summary);
    const moved = replay.landingIntervals.find(i=>i.inputIndex===1);
    assert.ok(moved && Math.abs(moved.landingTime-interval.landingTime)<1e-7);
    assert.ok(moved.inputTime-moved.landingTime>=.2-1e-7);
  }
  const higher = await post('/api/later-press',{...request,minimumMs:400});
  assert.ok(higher.alternatives.length>0 && higher.alternatives.every(a=>a.delayMs>=400));
  if (profile==='Chosen' && !enabled) {
    const cramped = await post('/api/later-press',{...request,inputs:[0,interval.landingTime+.05,interval.landingTime+.1]});
    assert.equal(cramped.status,'ready'); assert.equal(cramped.complete,true);
    assert.equal(cramped.candidateCount,0); assert.equal(cramped.attempted,0); assert.equal(cramped.alternatives.length,0);
  }
  const distance = await post('/api/later-press',{...request,axis:'distance',inputs:baseline.inputs.map(i=>i.x)});
  assert.ok(['ready','unsupported'].includes(distance.status));
  if(distance.status==='ready') assert.ok(distance.alternatives.length>0);
  else assert.ok(distance.message && distance.alternatives.length===0);
  cases.push({profile,enabled,originalMs:(interval.inputTime-interval.landingTime)*1000,
    attempted:result.attempted,successful:result.alternatives.length,firstDelayMs:result.alternatives[0].delayMs,
    distanceStatus:distance.status});
}
const config={json,profile:'Chosen',warningDuration:.8,axis:'time',surfaceRunSettings:surface};
await post('/api/later-press',{...config,inputs:[0,1.1],inputIndex:1,minimumMs:2001},400);
await post('/api/later-press',{...config,inputs:[0,1.1],inputIndex:8,minimumMs:200},400);
const unavailable=await post('/api/later-press',{...config,inputs:[0,119],inputIndex:1,minimumMs:200});
assert.equal(unavailable.status,'unsupported'); assert.equal(unavailable.alternatives.length,0);
const noMarked=await post('/api/later-press',{...config,inputs:[0],inputIndex:0,minimumMs:200});
assert.equal(noMarked.status,'unsupported');
const report={passed:true,base,cases,checks:['both controls and acceleration on/off','200 and 400ms thresholds',
  '21 bounded probes','every suggested tape independently replayed','same original landing','prefix preserved',
  'distance equivalence or explicit unsupported','invalid threshold/index rejected','unfinished baseline rejected']};
if(output)await writeFile(output,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
