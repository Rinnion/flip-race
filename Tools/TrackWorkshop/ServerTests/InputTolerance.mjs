import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';

const [base, output] = process.argv.slice(2);
const surface = { enabled: true, ratePercent: 5, curve: 'Linear', capPercent: 30,
  airDecayPercent: 200, resetOnCollision: true };
const json = JSON.stringify({ schemaVersion: 1, physicsVersion: 1, profilePolicy: 'any', warningMode: 'selected',
  track: { id: 'tolerance-http', name: 'Допуск нажатий', length: 1500, obstacles: [], riskZones: [] }, botReferences: [] });
async function post(path, body, expected = 200) {
  const response = await fetch(base + path, { method: 'POST',
    headers: { 'Content-Type': 'application/json', Origin: base }, body: JSON.stringify(body) });
  const text = await response.text();
  assert.equal(response.status, expected, text.slice(0, 350));
  return text ? JSON.parse(text) : null;
}
const cases = [];
for (const profile of ['Chosen', 'FastFlip']) for (const enabled of [false, true]) {
  const request = { json, profile, warningDuration: .8, axis: 'time', inputs: [.5], inputIndex: 0,
    surfaceRunSettings: { ...surface, enabled } };
  const frozen = JSON.stringify(request);
  const baseline = await post('/api/replay', request);
  const report = await post('/api/input-tolerance', request);
  assert.equal(report.status, 'ready'); assert.equal(report.complete, true);
  assert.equal(report.stepMs, 50); assert.equal(report.spanMs, 300);
  assert.deepEqual(report.probes.map(p => p.offsetMs), Array.from({ length: 13 }, (_, i) => -300 + i * 50));
  assert.ok(report.probes.every(p => p.status === 'safe'), JSON.stringify(report));
  assert.equal(report.omission.status, 'safe'); assert.equal(report.attempted, 14);
  for (const probe of report.probes) {
    const preview = await post('/api/input-tolerance/preview', { ...request, offsetMs: probe.offsetMs, omit: false });
    assert.equal(preview.probe.status, probe.status);
    assert.ok(preview.samples.length > 1);
    assert.ok(preview.samples.every(s => s.collisions === 0));
    assert.ok(preview.probe.endX >= report.baseline.endX - 1e-5);
    assert.ok(Math.abs(preview.probe.inputTime - (.5 + probe.offsetMs / 1000)) < 1e-7);
  }
  const originalAgain = await post('/api/replay', request);
  assert.deepEqual(originalAgain, baseline, 'analysis and preview leave original replay unchanged');
  assert.equal(JSON.stringify(request), frozen);
  const distance = { ...request, axis: 'distance', inputs: [baseline.inputs[0].x] };
  const distanceReport = await post('/api/input-tolerance', distance);
  assert.ok(['ready', 'unsupported'].includes(distanceReport.status));
  if (distanceReport.status === 'unsupported') assert.ok(distanceReport.message.length > 0);
  const chain = await post('/api/input-tolerance', { ...request, inputs: [.5, .65], inputIndex: 1 });
  assert.equal(chain.status, 'ready');
  assert.equal(chain.probes.find(p => p.offsetMs === -150).status, 'invalid', 'cannot coincide with preceding press');
  assert.equal(chain.probes.find(p => p.offsetMs === 0).status, 'safe');
  const omission = await post('/api/input-tolerance', { ...request, inputs: [.5, .65], inputIndex: 0 });
  assert.ok(Math.abs(omission.omission.endTime - .65) < 1e-7, 'omission ends before next command');
  cases.push({ profile, enabled, probes: report.probes.length, previewed: 13,
    distanceStatus: distanceReport.status, chainZero: chain.probes.find(p => p.offsetMs === 0).status });
}
const request = { json, profile: 'Chosen', warningDuration: .8, axis: 'time', inputs: [.1, .25], inputIndex: 0 };
const boundary = await post('/api/input-tolerance', request);
assert.equal(boundary.probes.find(p => p.offsetMs === -150).status, 'invalid');
assert.equal(boundary.probes.find(p => p.offsetMs === 150).status, 'invalid');
await post('/api/input-tolerance', { ...request, inputIndex: 99 }, 400);
await post('/api/input-tolerance/preview', { ...request, offsetMs: 25 }, 400);
await post('/api/input-tolerance/preview', { ...request, offsetMs: 350 }, 400);
await post('/api/input-tolerance', { ...request, json: '{' }, 400);
const result = { passed: true, version: '0.5.17', base, cases,
  checks: ['13 discrete shifts', '52 previews', 'both controls and acceleration on/off', 'immutable source replay',
    'whole flight and original endX', 'order and race-start boundaries', 'omission ends before next input',
    'distance normalization or explicit explanation', 'request validation'] };
if (output) await writeFile(output, JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify(result, null, 2));
