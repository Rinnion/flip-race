import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
const [base, output] = process.argv.slice(2);
const surface = { enabled: true, ratePercent: 5, curve: 'Linear', capPercent: 30,
  airDecayPercent: 200, resetOnCollision: true };
const json = JSON.stringify({ schemaVersion: 1, physicsVersion: 1, profilePolicy: 'any', warningMode: 'selected',
  track: { id: 'pair-http', name: 'Совместный допуск', length: 1500, obstacles: [], riskZones: [] }, botReferences: [] });
async function post(path, body, expected = 200) {
  const response = await fetch(base + path, { method: 'POST',
    headers: { 'Content-Type': 'application/json', Origin: base }, body: JSON.stringify(body) });
  const text = await response.text(); assert.equal(response.status, expected, text.slice(0, 300));
  return text ? JSON.parse(text) : null;
}
const key = cell => `${cell.firstOffsetMs},${cell.secondOffsetMs}`;
const cases = [];
for (const profile of ['Chosen', 'FastFlip']) for (const enabled of [false, true]) {
  const request = { json, profile, warningDuration: .8, axis: 'time', inputs: [1, 1.1], firstInputIndex: 0,
    spanMs: 300, surfaceRunSettings: { ...surface, enabled } };
  const original = await post('/api/replay', request);
  const report = await post('/api/pair-tolerance', request);
  assert.equal(report.status, 'ready', report.message);
  assert.equal(report.complete, true); assert.equal(report.cells.length, 169);
  assert.equal(new Set(report.cells.map(key)).size, 169);
  assert.ok(report.cells.every(c => c.computed));
  const zero = report.cells.find(c => key(c) === '0,0');
  assert.equal(zero.status, 'safe'); assert.equal(zero.pairIntervalMs, 100);
  assert.equal(report.cells.find(c => key(c) === '100,0').status, 'invalid');
  const offsets = report.cells.filter((_, index) => index % 13 === 0).map(({ firstOffsetMs, secondOffsetMs }) => ({ firstOffsetMs, secondOffsetMs }));
  const subset = await post('/api/pair-tolerance', { ...request, offsets });
  assert.deepEqual(subset.cells, offsets.map(o => report.cells.find(c => key(c) === key(o))));
  const view = [zero, ...report.cells.filter(c => c.status === 'safe' && key(c) !== '0,0').filter((_, i) => i % 25 === 0)];
  for (const cell of view) {
    const preview = await post('/api/pair-tolerance/preview', { ...request,
      firstOffsetMs: cell.firstOffsetMs, secondOffsetMs: cell.secondOffsetMs });
    assert.deepEqual(preview.cell, cell);
    assert.ok(preview.samples.length > 1);
    const end = preview.samples.at(-1);
    assert.ok(!end.flipping && ['Wall', 'Stone'].includes(end.support));
    assert.ok(end.x >= report.baseline.endX - 1e-5);
    assert.ok(end.time >= cell.secondInputTime);
  }
  const distances = original.inputs.map(i => i.x);
  const byX = await post('/api/pair-tolerance', { ...request, axis: 'distance', inputs: distances, offsets: [{ firstOffsetMs: 0, secondOffsetMs: 0 }] });
  assert.equal(byX.status, 'ready', byX.message); assert.equal(byX.cells[0].status, 'safe');
  assert.deepEqual(await post('/api/replay', request), original, 'reference replay is unchanged');
  cases.push({ profile, enabled, cells: report.cells.length, previewed: view.length,
    safe: report.cells.filter(c => c.status === 'safe').length, invalid: report.cells.filter(c => c.status === 'invalid').length });
}
const request = { json, profile: 'Chosen', warningDuration: .8, axis: 'time', inputs: [1, 1.1], firstInputIndex: 0, surfaceRunSettings: surface };
const expansions = [];
const cache = new Map();
for (const spanMs of [300, 600, 1000]) {
  const offsets = [];
  for (let b = -spanMs; b <= spanMs; b += 50) for (let a = -spanMs; a <= spanMs; a += 50)
    if (!cache.has(`${a},${b}`)) offsets.push({ firstOffsetMs: a, secondOffsetMs: b });
  const result = await post('/api/pair-tolerance', { ...request, spanMs, offsets });
  assert.equal(result.complete, true, 'short fixture should fit request budget');
  for (const cell of result.cells) cache.set(key(cell), cell);
  assert.equal(cache.size, (spanMs * 2 / 50 + 1) ** 2);
  expansions.push({ spanMs, newlyComputed: result.cells.length, total: cache.size });
}
const full = await post('/api/pair-tolerance', { ...request, spanMs: 1000 });
assert.equal(full.complete, true); assert.equal(full.cells.length, 1681);
assert.ok(full.cells.every(cell => JSON.stringify(cell) === JSON.stringify(cache.get(key(cell)))), 'expanded and fresh full matrix agree');
await post('/api/pair-tolerance', { ...request, spanMs: 500 }, 400);
const missingSecond = await post('/api/pair-tolerance', { ...request, firstInputIndex: 1 });
assert.equal(missingSecond.status, 'unsupported'); assert.ok(missingSecond.message);
await post('/api/pair-tolerance', { ...request, offsets: [{ firstOffsetMs: 25, secondOffsetMs: 0 }] }, 400);
await post('/api/pair-tolerance', { ...request, offsets: [{ firstOffsetMs: 0, secondOffsetMs: 0 }, { firstOffsetMs: 0, secondOffsetMs: 0 }] }, 400);
await post('/api/pair-tolerance/preview', { ...request, firstOffsetMs: 1050, secondOffsetMs: 0 }, 400);
const { firstInputIndex, ...noIndex } = request;
await post('/api/pair-tolerance', noIndex, 400);
await post('/api/pair-tolerance/preview', { ...noIndex, firstOffsetMs: 0, secondOffsetMs: 0 }, 400);
const separate = await post('/api/pair-tolerance', { ...request, inputs: [.1, 3] });
assert.equal(separate.status, 'unsupported'); assert.ok(separate.message);
const rethinkJson = await readFile(new URL('../../../Assets/StreamingAssets/Tracks/rethink.track.json', import.meta.url), 'utf8');
const rethinkRequest = { ...request, json: rethinkJson, inputs: [1.65, 4.15, 4.25], firstInputIndex: 1,
  spanMs: 1000, surfaceRunSettings: { ...surface, enabled: false } };
const rethink = await post('/api/pair-tolerance', rethinkRequest);
assert.equal(rethink.complete, true); assert.equal(rethink.cells.length, 1681);
const shiftedPair = rethink.cells.find(c => key(c) === '-1000,-500');
assert.equal(shiftedPair.status, 'safe'); assert.equal(shiftedPair.minimumIntervalMs, 600);
const rethinkPreview = await post('/api/pair-tolerance/preview', { ...rethinkRequest, firstOffsetMs: -1000, secondOffsetMs: -500 });
assert.deepEqual(rethinkPreview.cell, shiftedPair);
const report = { passed: true, version: '0.5.18', base, cases, expansions,
  rethink: { inputs: rethinkRequest.inputs, safe: rethink.cells.filter(c => c.status === 'safe').length,
    safe150: rethink.cells.filter(c => c.status === 'safe' && c.minimumIntervalMs >= 150).length, shiftedPair },
  checks: ['169/625/1681 discrete cells', 'subset reuse equals full matrix', 'same reference after previews',
    'both profiles acceleration on/off', 'distance normalization', 'support after both commands and reference end X',
    'order and request validation', 'separate source flights explained'] };
if (output) await writeFile(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
