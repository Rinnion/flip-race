import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { replaySummary } from '../Web/replay-summary.mjs';

const [base, output] = process.argv.slice(2);
const surfaceRunSettings = { enabled:false, ratePercent:5, curve:'Linear', capPercent:30, airDecayPercent:200, resetOnCollision:true };
async function post(path, request) {
  const response = await fetch(base + path, { method:'POST', headers:{ 'Content-Type':'application/json', Origin:base }, body:JSON.stringify(request) });
  const text = await response.text(); assert.equal(response.status, 200, text.slice(0, 300));
  return path.endsWith('/stream') ? text.trim().split('\n').map(JSON.parse).at(-1) : JSON.parse(text);
}
const json = await readFile(new URL('../../../Assets/StreamingAssets/Tracks/rethink.track.json', import.meta.url), 'utf8');
const inputs = JSON.parse(json).botReferences.find(reference => reference.profileId === 'Chosen').inputs;
const request = { json, profile:'Chosen', warningDuration:.8, axis:'distance', inputs, surfaceRunSettings };
const replay = await post('/api/replay', request);
const saved = await post('/api/tolerance-batch/stream', request);
assert.equal(saved.type, 'result');
const report = replaySummary(replay, saved.batch);
const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < .001, `${actual} != ${expected}`);
assert.equal(report.scores.count, 16);
near(report.scores.minimum * 100, 54.716981);
near(report.scores.maximum * 100, 100);
near(report.scores.median * 100, 83.320755);
near(report.gaps.minimum, -431.818);
near(report.gaps.maximum, 2673.551);
near(report.gaps.median, 867.153);
assert.equal(report.overlaps.length, 3);
assert.equal(report.hardPlaces.length, 0);
const restored = await post('/api/analyze', {json:saved.canonicalJson});
assert.equal(restored.toleranceBatch.status, 'ready');
assert.deepEqual(replaySummary(replay, restored.toleranceBatch.batch), report);

const empty = JSON.stringify({schemaVersion:1,physicsVersion:1,profilePolicy:'any',warningMode:'selected',
  track:{id:'summary-overlap',name:'Проверка пар',length:1500,obstacles:[],riskZones:[]},botReferences:[]});
const chainRequest = {...request,json:empty,axis:'time',inputs:[.05,.1,.15]};
const chainReplay = await post('/api/replay',chainRequest);
const chainSaved = await post('/api/tolerance-batch/stream',chainRequest);
const chain = replaySummary(chainReplay,chainSaved.batch);
assert.equal(chain.scores.count,2);
assert.equal(chain.hardPlaces.length,1);
assert.equal(chainSaved.batch.targets[0].pair.cells.filter(cell=>cell.status==='safe').length,3);
assert.equal(chainSaved.batch.targets[1].pair.cells.filter(cell=>cell.status==='safe').length,91);
const evidence={passed:true,version:'0.5.24',base,rethink:{scores:report.scores,gaps:report.gaps,overlaps:report.overlaps.length},
  chain:{scores:chain.scores,hard:chain.hardPlaces.length},roundTrip:true};
if(output) await writeFile(output,JSON.stringify(evidence,null,2)+'\n');
console.log(JSON.stringify(evidence,null,2));
