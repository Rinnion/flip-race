// Real HTTP verification against an explicitly started isolated Workshop.
import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';

const base = process.argv[2] || 'http://127.0.0.1:4213';
const output = process.argv[3];
const checks = [];
async function post(path, body, status = 200) {
  const response = await fetch(base + path, { method: 'POST', headers: {
    'Content-Type': 'application/json', Origin: new URL(base).origin,
  }, body: JSON.stringify(body) });
  const value = await response.json();
  assert.equal(response.status, status, `${path}: ${JSON.stringify(value).slice(0, 200)}`);
  return value;
}
const original = {
  schemaVersion: 1, physicsVersion: 1, profilePolicy: 'any', warningMode: 'selected',
  track: { id: 'route-search-http', name: 'Поиск HTTP', length: 1200, obstacles: [], riskZones: [] },
  botReferences: [], authorExtension: { untouched: ['future', 42] },
};
let source = JSON.stringify(original, null, 2);
for (const profile of ['Chosen', 'FastFlip']) {
  const start = performance.now();
  const result = await post('/api/route-search', { json: source, profile, warningDuration: .8 });
  const parsed = JSON.parse(result.sourceJson);
  assert.deepEqual(parsed.track, original.track);
  assert.deepEqual(parsed.authorExtension, original.authorExtension);
  assert.equal(parsed.workshopAnalysis.version, 2);
  assert.ok(Array.isArray(parsed.workshopAnalysis.groups));
  const run = result.analysis.runs.find(value => value.profile === profile);
  assert.ok(run.routes.length > 0 && run.routes.length <= 1000);
  assert.equal(run.stepMs, 50);
  assert.equal(run.rulesVersion, 2);
  assert.equal(run.searchParameters.minimumInputIntervalMs, 100);
  assert.equal(run.surfaceRun, false);
  assert.equal(run.axis, 'time');
  assert.equal(run.startX, 0);
  assert.equal(run.storedCount, run.routes.length);
  const tapes = new Set();
  let prior = -1;
  for (const route of run.routes) {
    assert.ok(route.finishTime >= prior);
    prior = route.finishTime;
    const tape = JSON.stringify(route.inputTicks);
    assert.ok(!tapes.has(tape)); tapes.add(tape);
    route.inputTicks.forEach((tick, i) => assert.ok(Number.isInteger(tick) && tick >= 0 && (!i || tick - route.inputTicks[i - 1] >= 2)));
  }
  for (const index of [...new Set([0, Math.floor(run.routes.length / 2), run.routes.length - 1])]) {
    const route = run.routes[index];
    const replay = await post('/api/replay', { json: result.sourceJson, profile, warningDuration: .8,
      axis: 'time', inputs: route.inputTicks.map(tick => tick * run.stepMs / 1000) });
    assert.equal(replay.summary.status, 'Finished');
    assert.ok(Math.abs(replay.summary.finishTime - route.finishTime) < 1e-7);
    assert.equal(replay.summary.collisions, route.collisions);
    assert.equal(replay.summary.acceptedInputs, route.inputTicks.length);
    assert.equal(replay.summary.ignoredInputs + replay.summary.unreachedInputs, 0);
    assert.ok(Array.isArray(replay.landingIntervals));
  }
  source = result.sourceJson;
  const analyzed = await post('/api/analyze', { json: source });
  assert.equal(analyzed.routeAnalysis.status, 'ready');
  assert.deepEqual(JSON.parse(analyzed.cleanedJson).workshopAnalysis, parsed.workshopAnalysis);
  assert.deepEqual(analyzed.routeAnalysis.analysis, result.analysis);
  checks.push({ profile, routes: run.routes.length, stopReason: run.stopReason,
    expandedStates: run.expandedStates, elapsedMilliseconds: run.elapsedMilliseconds,
    httpMilliseconds: Math.round(performance.now() - start), bytes: Buffer.byteLength(source) });
}
const saved = JSON.parse(source);
assert.equal(saved.workshopAnalysis.groups.length, 2);
saved.track.length += 100;
const stale = await post('/api/analyze', { json: JSON.stringify(saved) });
assert.equal(stale.routeAnalysis.status, 'stale');
await post('/api/route-search', { json: source, profile: 'Manual', warningDuration: .8 }, 400);
await post('/api/route-search', { json: source, profile: 'Chosen', warningDuration: .7 }, 400);
const report = { base, checks, result: 'passed', verified: ['distinct sorted tapes', 'sampled exact replays',
  'both profiles retained', 'author extension preserved', 'JSON round trip', 'stale geometry', 'invalid settings'] };
if (output) await writeFile(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
