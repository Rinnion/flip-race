// Integration check against an explicitly started release, with real Core results.
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';

const base = process.argv[2];
const output = process.argv[3];
const main = { enabled: true, ratePercent: 5, curve: 'Linear', capPercent: 30, airDecayPercent: 200, resetOnCollision: true };
const off = { ...main, enabled: false };
async function post(path, request, expected = 200) {
  const response = await fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: base }, body: JSON.stringify(request) });
  const text = await response.text();
  assert.equal(response.status, expected, `${path}: ${text.slice(0, 200)}`);
  const result = text ? JSON.parse(text) : null;
  return result;
}
const empty = JSON.stringify({ schemaVersion: 1, physicsVersion: 1, profilePolicy: 'any', warningMode: 'selected',
  track: { id: 'settings-http-empty', name: 'Настройки HTTP', length: 1500, obstacles: [], riskZones: [] }, botReferences: [] });
const request = { json: empty, profile: 'Chosen', warningDuration: .8, axis: 'time', inputs: [] };
const legacy = await post('/api/replay', request);
const explicitOff = await post('/api/replay', { ...request, surfaceRunSettings: off });
assert.deepEqual(legacy, explicitOff);
const accelerated = await post('/api/replay', { ...request, surfaceRunSettings: main });
assert.ok(accelerated.summary.finishTime < legacy.summary.finishTime);
assert.deepEqual(accelerated.surfaceRunSettings, main);
const custom = { ...main, curve: 'Exponential', capPercent: 40, airDecayPercent: 0, resetOnCollision: false };
const customResult = await post('/api/replay', { ...request, surfaceRunSettings: custom });
assert.deepEqual(customResult.surfaceRunSettings, custom);
assert.notEqual(customResult.summary.finishTime, accelerated.summary.finishTime);
for (const invalid of [{ ...main, ratePercent: 6 }, { ...main, curve: 'guess' }, { ...main, capPercent: 25 },
  { ...main, airDecayPercent: 75 }, { enabled: true }]) {
  await post('/api/replay', { ...request, surfaceRunSettings: invalid }, 400);
}
const catalog = await (await fetch(base + '/api/catalog/mixed-tricks')).json();
const searched = await post('/api/route-search', { json: catalog.json, profile: 'Chosen', warningDuration: .8, surfaceRunSettings: main });
const run = searched.analysis.runs.find(item => item.profile === 'Chosen');
assert.ok(run.routes.length > 0);
assert.equal(run.surfaceRun, true);
assert.deepEqual(run.surfaceRunSettings, main);
assert.deepEqual(JSON.parse(searched.sourceJson).workshopAnalysis.groups[0].settings.surfaceRunSettings, main);
for (const index of [...new Set([0, Math.floor(run.routes.length / 2), run.routes.length - 1])]) {
  const route = run.routes[index];
  const replay = await post('/api/replay', { json: searched.sourceJson, profile: run.profile, warningDuration: run.warningDuration,
    axis: 'time', inputs: route.inputTicks.map(tick => tick / 10), surfaceRunSettings: run.surfaceRunSettings });
  assert.ok(Math.abs(replay.summary.finishTime - route.finishTime) < 1e-6);
  assert.equal(replay.summary.collisions, route.collisions);
  assert.equal(replay.summary.acceptedInputs, route.inputTicks.length);
  assert.equal(replay.summary.ignoredInputs + replay.summary.unreachedInputs, 0);
  assert.deepEqual(replay.surfaceRunSettings, main);
}
const botJson = await readFile(new URL('../PreparationTests/raw-custom-bonus.track.json', import.meta.url), 'utf8');
const prepared = await post('/api/prepare-bot', { json: botJson, profiles: ['Chosen'], warningDuration: .8, surfaceRunSettings: main });
assert.equal(prepared.status, 'prepared');
assert.ok(prepared.preparedJson);
const trial = prepared.profiles[0].masterTrial;
assert.ok(trial);
assert.deepEqual(trial.surfaceRunSettings, main);
assert.equal(trial.participants, 1);
if (trial.replayVerified) {
  const replay = await post('/api/replay', { json: prepared.preparedJson, profile: trial.profile, warningDuration: trial.warningDuration,
    axis: 'time', inputs: trial.inputTimes, surfaceRunSettings: trial.surfaceRunSettings });
  assert.ok(Math.abs(replay.summary.finishTime - trial.finishTime) < 1e-6);
  assert.equal(replay.summary.collisions, trial.collisions);
}
const report = { passed: true, base, empty: { legacyOff: legacy.summary.finishTime, mainOn: accelerated.summary.finishTime,
  customOn: customResult.summary.finishTime }, search: { count: run.routes.length, stopReason: run.stopReason,
  minTime: run.routes[0].finishTime, maxTime: run.routes.at(-1).finishTime,
  distinctTimesAtMilliseconds: new Set(run.routes.map(route => route.finishTime.toFixed(3))).size,
  elapsedMilliseconds: run.elapsedMilliseconds, surfaceRunSettings: run.surfaceRunSettings },
  preparation: { status: prepared.status, compatibilityFinishTime: prepared.profiles[0].finishTime, masterTrial: trial } };
if (output) await writeFile(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
