// Integration check against an explicitly started Workshop server.
import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { replaySummary } from '../Web/replay-summary.mjs';

const [base, output] = process.argv.slice(2);
assert.ok(base, 'Pass an isolated Workshop URL');
const surfaceRunSettings = { enabled:false, ratePercent:5, curve:'Linear', capPercent:30, airDecayPercent:200, resetOnCollision:true };
const json = JSON.stringify({schemaVersion:1,physicsVersion:1,profilePolicy:'any',warningMode:'selected',
  track:{id:'deep-http',name:'Глубокий манёвр: проверка',length:1500,obstacles:[],riskZones:[]},botReferences:[]});
const seed = {json,profile:'Chosen',warningDuration:.8,axis:'time',surfaceRunSettings};
async function post(path, body) {
  const response = await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json',Origin:base},body:JSON.stringify(body)});
  const text = await response.text();
  assert.equal(response.status,200,text.slice(0,500));
  const value = path.endsWith('/stream') ? text.trim().split('\n').map(JSON.parse) : JSON.parse(text);
  if(Array.isArray(value)) assert.equal(value.at(-1).type,'result',text.slice(-500));
  return value;
}
// Independent count oracle: enumerate the schedule grid, without using production helpers.
function admissibleCounts(inputs) {
  const rows=inputs.map(()=>Array(25).fill(0)), choices=Array(inputs.length).fill(0);
  let valid=0;
  function visit(index,previous) {
    if(index===inputs.length) { valid++; choices.forEach((choice,row)=>rows[row][choice]++); return; }
    for(let choice=0;choice<25;choice++) {
      const time=inputs[index]+(choice*50-600)/1000;
      if(time < -1e-10 || index && time-previous < .1-1e-10) continue;
      choices[index]=choice; visit(index+1,time);
    }
  }
  visit(0,-Infinity); return {valid,rows};
}

const cases=[];
for(const count of [1,2,3,4]) {
  const inputs=Array.from({length:count},(_,i)=>.4+i*.15);
  const request={...seed,inputs};
  const replay=await post('/api/replay',request);
  assert.equal(replay.inputs.filter(input=>input.status==='accepted').length,count);
  let saved, current=request, chunks=0, previous=-1n;
  const started=performance.now();
  do {
    const events=await post('/api/tolerance-batch/stream',current);
    saved=events.at(-1); chunks++;
    assert.equal(saved.batch.targets.length,1,'All source flight commands form one maneuver');
    const target=saved.batch.targets[0], report=target.maneuver;
    assert.equal(target.kind,'maneuver');
    assert.deepEqual(target.inputIndices,inputs.map((_,i)=>i));
    assert.equal(report.cells.length,count*25);
    assert.ok(BigInt(report.cursor)>previous,'Every resumable chunk advances');
    previous=BigInt(report.cursor);
    assert.ok(chunks<200,'Unexpected excessive chunking');
    current={...request,json:saved.canonicalJson};
  } while(!saved.batch.complete);
  const report=saved.batch.targets[0].maneuver, oracle=admissibleCounts(inputs);
  assert.equal(report.total,String(25**count));
  assert.equal(report.cursor,report.total);
  assert.equal(report.valid,String(oracle.valid));
  assert.equal(report.computed,report.valid);
  const safe=BigInt(report.safe), valid=BigInt(report.valid);
  assert.ok(safe>0n && safe<=valid);
  assert.ok(Math.abs(report.percent-100*Number(safe)/Number(valid))<1e-6);
  for(let row=0;row<count;row++) {
    const cells=report.cells.filter(cell=>cell.inputIndex===row);
    assert.equal(cells.length,25);
    assert.equal(cells.reduce((sum,cell)=>sum+BigInt(cell.safe),0n),safe);
    assert.equal(cells.reduce((sum,cell)=>sum+BigInt(cell.valid),0n),valid);
    for(let column=0;column<25;column++) {
      const cell=cells.find(cell=>cell.offsetMs===column*50-600);
      assert.equal(cell.valid,String(oracle.rows[row][column]));
      if(cell.valid==='0') assert.equal(cell.percent,null);
    }
  }
  const summary=replaySummary(replay,saved.batch);
  assert.equal(summary.scores.count,1,'Whole maneuver contributes only once');
  assert.ok(Math.abs(summary.scores.minimum-report.percent/100)<1e-6);
  const restored=await post('/api/analyze',{json:saved.canonicalJson});
  assert.equal(restored.toleranceBatch.status,'ready');
  assert.deepEqual(replaySummary(replay,restored.toleranceBatch.batch),summary);
  const cell=report.cells.find(cell=>cell.inputIndex===count-1 && cell.witnessOffsetsMs);
  assert.ok(cell,'A safe witness exists for selected row');
  const preview=await post('/api/maneuver-tolerance/preview',{...request,inputIndex:cell.inputIndex,offsetMs:cell.offsetMs,offsetsMs:cell.witnessOffsetsMs});
  assert.equal(preview.status,'safe');
  assert.deepEqual(preview.offsetsMs,cell.witnessOffsetsMs);
  assert.ok(preview.samples.length>1);
  assert.equal(preview.pressPoints.length,count);
  assert.ok(preview.pressPoints.every(point=>point.status==='accepted'));
  cases.push({count,total:report.total,valid:report.valid,safe:report.safe,percent:report.percent,chunks,elapsedMs:Math.round(performance.now()-started),previewPoints:preview.pressPoints.length});
  if(output && count===4) await writeFile(output.replace(/\.json$/,'.track.json'),saved.canonicalJson);
}
const evidence={passed:true,version:'0.5.25',base,cases,roundTrip:true,independentScheduleCounts:true};
if(output) await writeFile(output,JSON.stringify(evidence,null,2)+'\n');
console.log(JSON.stringify(evidence,null,2));
