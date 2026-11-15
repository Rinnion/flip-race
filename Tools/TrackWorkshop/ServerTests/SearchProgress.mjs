import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
const [base, output] = process.argv.slice(2);
const config = { profile: 'Chosen', warningDuration: .8, surfaceRunSettings:
  { enabled: true, ratePercent: 5, curve: 'Linear', capPercent: 30, airDecayPercent: 200, resetOnCollision: true } };
const json = JSON.stringify({ schemaVersion: 1, physicsVersion: 1, profilePolicy: 'any', warningMode: 'selected',
  track: { id: 'progress-http', name: 'Прогресс', length: 650, obstacles: [], riskZones: [] }, botReferences: [] });
const request = { json, ...config };
async function post(path, body, signal, origin = base) {
  return fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: origin }, body: JSON.stringify(body), signal });
}
async function* events(response) {
  const reader = response.body.getReader(), decoder = new TextDecoder(); let pending = '';
  try {
    while (true) {
      const { value, done } = await reader.read();
      pending += decoder.decode(value, { stream: !done });
      let newline;
      while ((newline = pending.indexOf('\n')) >= 0) {
        const line = pending.slice(0, newline).trim(); pending = pending.slice(newline + 1);
        if (line) yield JSON.parse(line);
      }
      if (done) break;
    }
    assert.equal(pending.trim(), '', 'each server event ends in a newline');
  } finally { reader.releaseLock(); }
}
const response = await post('/api/route-search/stream', request);
assert.equal(response.status, 200);
assert.match(response.headers.get('content-type'), /application\/x-ndjson/);
const received = [];
for await (const item of events(response)) received.push(item);
assert.equal(received[0].type, 'progress'); assert.equal(received[0].percent, 0);
assert.equal(received.at(-1).type, 'result');
assert.equal(received.filter(e => e.type === 'result').length, 1);
const progress = received.filter(e => e.type === 'progress');
assert.ok(progress.some(e => e.phase === 'search'));
assert.ok(progress.some(e => e.phase === 'verify'));
assert.ok(progress.some(e => e.phase === 'save' && e.percent === 99));
for (let i = 0; i < progress.length; i++) {
  assert.ok(Number.isInteger(progress[i].percent) && progress[i].percent >= 0 && progress[i].percent < 100);
  if (i) assert.ok(progress[i].percent >= progress[i - 1].percent);
}
const result = received.at(-1).result;
assert.equal(result.analysis.version, 2);
assert.equal(JSON.parse(result.sourceJson).workshopAnalysis.groups.length, 1);
const plainResponse = await post('/api/route-search', request);
assert.equal(plainResponse.status, 200);
const plain = await plainResponse.json();
assert.deepEqual(result.analysis.runs[0].routes, plain.analysis.runs[0].routes);
const invalid = await post('/api/route-search/stream', { ...request, warningDuration: .7 });
assert.equal(invalid.status, 400); assert.ok((await invalid.json()).error);
const foreign = await post('/api/route-search/stream', request, undefined, 'https://foreign.example');
assert.equal(foreign.status, 400); await foreign.text();
const catalog = await (await fetch(base + '/api/catalog/mixed-tricks')).json();
const controller = new AbortController();
const cancellable = await post('/api/route-search/stream', { ...request, json: catalog.json }, controller.signal);
let beforeCancel;
for await (const item of events(cancellable)) { beforeCancel = item; controller.abort(); break; }
assert.equal(beforeCancel.type, 'progress'); assert.ok(beforeCancel.percent < 100);
// A new short search still completes after disconnecting the previous stream.
const restarted = await post('/api/route-search/stream', request);
let restartFirst, restartLast;
for await (const item of events(restarted)) { restartFirst ??= item; restartLast = item; }
assert.equal(restartFirst.percent, 0); assert.equal(restartLast.type, 'result');
const report = { passed: true, base, progress, routes: result.analysis.runs[0].routes.length,
  checks: ['monotonic actual-work events before result', 'search/verify/save phases', 'no server 100 before result',
    'same routes as legacy HTTP', 'grouped JSON preserved', 'invalid settings and foreign origin rejected', 'disconnect cancellation and restart from zero'] };
if (output) await writeFile(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({ passed: true, events: progress.length, routes: report.routes, checks: report.checks }, null, 2));
