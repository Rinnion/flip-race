import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
const [base, output, fixture] = process.argv.slice(2);
const main = { enabled: true, ratePercent: 5, curve: 'Linear', capPercent: 30, airDecayPercent: 200, resetOnCollision: true };
const settings = [
  { profile: 'Chosen', warningDuration: .8, surfaceRunSettings: main },
  { profile: 'Chosen', warningDuration: .8, surfaceRunSettings: { ...main, enabled: false } },
  { profile: 'Chosen', warningDuration: 1.2, surfaceRunSettings: main },
  { profile: 'FastFlip', warningDuration: .8, surfaceRunSettings: main },
];
const original = { schemaVersion: 1, physicsVersion: 1, profilePolicy: 'any', warningMode: 'selected',
  track: { id: 'settings-groups-http', name: 'Группы настроек', length: 650, obstacles: [], riskZones: [] }, botReferences: [], authorExtension: { untouched: 42 } };
let source = JSON.stringify(original), first;
async function post(path, body) {
  const response = await fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: base }, body: JSON.stringify(body) });
  const data = await response.json();
  assert.equal(response.status, 200, JSON.stringify(data).slice(0, 250)); return data;
}
function groups(text) { const a = JSON.parse(text).workshopAnalysis; assert.equal(a.version, 2); assert.equal(a.runs, undefined); return a.groups; }
for (const [index, config] of settings.entries()) {
  const result = await post('/api/route-search', { json: source, ...config });
  const saved = groups(result.sourceJson);
  assert.equal(saved.length, index + 1);
  assert.deepEqual(saved[index].settings, config);
  assert.equal(saved[index].runs.length, 1);
  for (const key of ['profile', 'warningDuration', 'surfaceRun', 'surfaceRunSettings']) assert.equal(saved[index].runs[0][key], undefined);
  assert.ok(saved[index].runs[0].routes.length > 0);
  assert.deepEqual(JSON.parse(result.sourceJson).track, original.track);
  assert.deepEqual(JSON.parse(result.sourceJson).authorExtension, original.authorExtension);
  if (!index) first = result;
  source = result.sourceJson;
}
const before = groups(source);
const replaced = await post('/api/route-search', { json: source, ...settings[1] });
assert.equal(groups(replaced.sourceJson).length, 4);
for (const index of [0, 2, 3]) assert.deepEqual(groups(replaced.sourceJson)[index], before[index]);
source = replaced.sourceJson;
const analyzed = await post('/api/analyze', { json: source });
assert.equal(analyzed.routeAnalysis.status, 'ready');
assert.equal(analyzed.routeAnalysis.analysis.runs.length, 4);
for (const run of analyzed.routeAnalysis.analysis.runs) {
  const route = run.routes[0];
  const replay = await post('/api/replay', { json: source, profile: run.profile, warningDuration: run.warningDuration,
    surfaceRunSettings: run.surfaceRunSettings, axis: 'time', inputs: route.inputTicks.map(t => t * run.stepMs / 1000) });
  assert.ok(Math.abs(replay.summary.finishTime - route.finishTime) < 1e-6);
}
const legacy = { ...original, workshopAnalysis: { version: 1, runs: first.analysis.runs, futureRoot: { kept: true } } };
const migrated = await post('/api/route-search', { json: JSON.stringify(legacy), ...settings[3] });
assert.equal(groups(migrated.sourceJson).length, 2);
assert.deepEqual(groups(migrated.sourceJson)[0].settings, settings[0]);
assert.deepEqual(JSON.parse(migrated.sourceJson).workshopAnalysis.futureRoot, { kept: true });
const report = { passed: true, base, groups: groups(source).map(g => ({ settings: g.settings, routes: g.runs[0].routes.length })),
  checks: ['same-profile conditions coexist', 'exact group replaced only', 'settings not duplicated per run', 'independent replay for each group', 'v1 migration', 'author and extension data preserved'] };
if (fixture) await writeFile(fixture, source + '\n');
if (output) await writeFile(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
