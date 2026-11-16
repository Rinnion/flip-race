import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
const [base, output] = process.argv.slice(2);
const json = JSON.stringify({schemaVersion:1,physicsVersion:1,profilePolicy:'any',warningMode:'selected',
  track:{id:'target-http',name:'Выбор точки',length:1500,obstacles:[],riskZones:[]},botReferences:[]});
const surface = {enabled:false,ratePercent:5,curve:'Linear',capPercent:30,airDecayPercent:200,resetOnCollision:true};
const source = {json,profile:'Chosen',warningDuration:.8,axis:'time',inputs:[.5,.6],surfaceRunSettings:surface};
async function post(path, body, status=200) {
  const response = await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json',Origin:base},body:JSON.stringify(body)});
  const text=await response.text(); assert.equal(response.status,status,text.slice(0,250));
  return text ? JSON.parse(text) : null;
}
const cases=[];
for (const profile of ['Chosen','FastFlip']) for (const enabled of [false,true]) {
  const request={...source,profile,surfaceRunSettings:{...surface,enabled}};
  const before=await post('/api/replay',request);
  for (const inputIndex of [0,1]) {
    const target=await post('/api/analysis-target',{...request,inputIndex});
    assert.equal(target.kind,'pair',target.message); assert.equal(target.firstInputIndex,0); assert.equal(target.inputIndex,inputIndex);
  }
  for (const inputIndex of [0,1]) {
    const target=await post('/api/analysis-target',{...request,inputs:[.5,2],inputIndex});
    assert.equal(target.kind,'single',target.message); assert.equal(target.firstInputIndex,null);
  }
  const distances=before.inputs.map(i=>i.x);
  const distanceTarget=await post('/api/analysis-target',{...request,axis:'distance',inputs:distances,inputIndex:1});
  assert.equal(distanceTarget.kind,'pair',distanceTarget.message); assert.equal(distanceTarget.firstInputIndex,0);
  assert.deepEqual(await post('/api/replay',request),before);
  cases.push({profile,enabled,bothEndpoints:'pair',separateFlights:'single',distance:'pair',unchanged:true});
}
const chain=[];
for (const inputIndex of [0,1,2]) {
  const target=await post('/api/analysis-target',{...source,inputs:[.4,.6,.75],inputIndex});
  assert.equal(target.kind,'pair',target.message); assert.equal(target.firstInputIndex,inputIndex===2?1:0); chain.push(target);
}
const rejected=await post('/api/analysis-target',{...source,profile:'FastFlip',inputs:[.1,.15,.2],inputIndex:2});
assert.equal(rejected.kind,'unsupported'); assert.ok(rejected.message);
const unreached=await post('/api/analysis-target',{...source,inputs:[100],inputIndex:0});
assert.equal(unreached.kind,'unsupported'); assert.ok(unreached.message);
await post('/api/analysis-target',source,400);
await post('/api/analysis-target',{...source,inputIndex:-1},400);
await post('/api/analysis-target',{...source,inputIndex:2},400);
const rethinkJson=await readFile(new URL('../../../Assets/StreamingAssets/Tracks/rethink.track.json',import.meta.url),'utf8');
const rethink=[];
for (const inputIndex of [0,1,2]) rethink.push(await post('/api/analysis-target',{...source,json:rethinkJson,inputs:[1.65,4.15,4.25],inputIndex}));
assert.equal(rethink[0].kind,'single');
assert.ok(rethink.slice(1).every(t=>t.kind==='pair'&&t.firstInputIndex===1));
const report={passed:true,version:'0.5.19',base,cases,chain,rejected,unreached,rethink};
if(output) await writeFile(output,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
