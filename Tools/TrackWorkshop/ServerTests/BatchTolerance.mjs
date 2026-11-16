// Real-process verification; does not start or stop the server.
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
const [base, output] = process.argv.slice(2);
const surface = {enabled:false,ratePercent:5,curve:'Linear',capPercent:30,airDecayPercent:200,resetOnCollision:true};
const empty = {schemaVersion:1,physicsVersion:1,profilePolicy:'any',warningMode:'selected',
  track:{id:'batch-http',name:'Матрицы',length:1500,obstacles:[],riskZones:[]},botReferences:[],
  workshopAnalysis:{version:2,groups:[],futureExtension:{keep:'yes'}}};
const source = {json:JSON.stringify(empty),profile:'Chosen',warningDuration:.8,axis:'time',inputs:[.5,2],surfaceRunSettings:surface};
async function post(path, body, status=200) {
  const response = await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json',Origin:base},body:JSON.stringify(body)});
  const text=await response.text(); assert.equal(response.status,status,text.slice(0,350));
  return text ? JSON.parse(text) : null;
}
async function batch(request) {
  const response=await fetch(base+'/api/tolerance-batch/stream',{method:'POST',headers:{'Content-Type':'application/json',Origin:base},body:JSON.stringify(request)});
  const text=await response.text(); assert.equal(response.status,200,text.slice(0,350));
  const events=text.trim().split('\n').map(line=>JSON.parse(line));
  assert.ok(!events.some(event=>event.type==='error'),text.slice(-1000));
  assert.equal(events[0].type,'plan');
  const result=events.at(-1); assert.equal(result.type,'result');
  assert.equal(result.batch.spanMs,600); assert.equal(result.batch.stepMs,50);
  assert.ok(result.batch.complete);
  return {events,result};
}
const cases=[];
for(const profile of ['Chosen','FastFlip']) {
  const request={...source,profile};
  const before=await post('/api/replay',request);
  const {events,result}=await batch(request);
  assert.equal(result.batch.targets.length,2);
  for(const target of result.batch.targets) { assert.equal(target.kind,'single'); assert.equal(target.single.probes.length,25); }
  const saved=JSON.parse(result.canonicalJson);
  assert.deepEqual(saved.track,empty.track);
  assert.deepEqual(saved.workshopAnalysis.futureExtension,empty.workshopAnalysis.futureExtension);
  assert.deepEqual(saved.workshopAnalysis.groups,[]);
  const loaded=await post('/api/analyze',{json:result.canonicalJson});
  assert.equal(loaded.toleranceBatch.status,'ready');
  assert.deepEqual(loaded.toleranceBatch.batch,result.batch);
  assert.deepEqual(await post('/api/replay',{...request,json:result.canonicalJson}),before);
  const resumed=await batch({...request,json:result.canonicalJson});
  assert.equal(resumed.events.filter(event=>event.type==='progress').length,0);
  assert.deepEqual(resumed.result.batch,result.batch);
  const first=events.find(event=>event.type==='progress'); assert.ok(first?.checkpoint);
  const partial=await post('/api/tolerance-batch/checkpoint',{...request,checkpoints:[first.checkpoint]});
  assert.ok(!partial.batch.complete);
  const continuation=await batch({...request,json:partial.canonicalJson});
  assert.equal(continuation.events.filter(event=>event.type==='progress').length,1);
  assert.deepEqual(continuation.result.batch,result.batch);
  saved.track.length+=10;
  assert.equal((await post('/api/analyze',{json:JSON.stringify(saved)})).toleranceBatch.status,'stale');
  cases.push({profile,singleCells:25,roundTrip:true,resume:true,geometryInvalidation:true,replayUnchanged:true});
}
const pair=await batch({...source,inputs:[.5,.6]});
assert.equal(pair.result.batch.targets.length,1);
assert.equal(pair.result.batch.targets[0].pair.cells.length,625);
assert.deepEqual(pair.result.batch.commandTargetIndices,[0,0]);
const chain=await batch({...source,inputs:[.4,.6,.75]});
assert.equal(chain.result.batch.targets.length,2);
assert.deepEqual(chain.result.batch.commandTargetIndices,[0,0,1]);
assert.deepEqual(chain.result.batch.targets.map(target=>target.firstInputIndex),[0,1]);
const rethinkJson=await readFile(new URL('../../../Assets/StreamingAssets/Tracks/rethink.track.json',import.meta.url),'utf8');
const rethink=await batch({...source,json:rethinkJson,inputs:[1.65,4.15,4.25]});
assert.deepEqual(rethink.result.batch.targets.map(target=>target.kind),['single','pair']);
const cells=rethink.result.batch.targets[1].pair.cells;
assert.equal(cells.length,625);
const green=cells.filter(cell=>cell.status==='safe').length;
const report={passed:true,version:'0.5.21',base,cases,pairCells:625,chainPairs:2,rethink:{green,total:625,percent:green/625*100}};
if(output) await writeFile(output,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
