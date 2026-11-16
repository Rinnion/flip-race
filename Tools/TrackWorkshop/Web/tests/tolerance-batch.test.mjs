import test from 'node:test';
import assert from 'node:assert/strict';
import { batchCoverage, batchRequest, matchesBatchReplay, readBatchResponse, toleranceColor } from '../tolerance-batch.mjs';
import { inputToleranceRequest, singleMatrixCells } from '../input-tolerance.mjs';
import { DEFAULT_SURFACE_RUN_SETTINGS } from '../run-settings.mjs';

const settings = { profile: 'Chosen', warningDuration: 0.6, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS };
const result = { ...settings, axis: 'time', inputs: [0.4, 0.8, 1.2].map(value => ({ value })) };
const pair = index => ({ kind: 'pair', inputIndex: index, firstInputIndex: index });
const batch = { ...settings, spanMs: 600, stepMs: 50, axis: 'time', inputs: [0.4, 0.8, 1.2],
  targets: [pair(0), pair(1)], commandTargetIndices: [0, 0, 1] };

test('restore requires the current range, full tape and run settings, and supports adjacent overlapping pairs', () => {
  assert.equal(matchesBatchReplay(batch, result), true);
  assert.equal(matchesBatchReplay(batch, null), false);
  assert.equal(matchesBatchReplay({ ...batch, spanMs: 300 }, result), false);
  assert.equal(matchesBatchReplay({ ...batch, commandTargetIndices: [1, 0, 1] }, result), false);
  assert.equal(matchesBatchReplay(batch, { ...result, axis: 'distance' }), false);
  assert.equal(matchesBatchReplay(batch, { ...result, warningDuration: 0.8 }), false);
  assert.equal(matchesBatchReplay(batch, { ...result, inputs: [{ value: 0.4 }, { value: 0.8 }] }), false);
  assert.equal(matchesBatchReplay(batch, { ...result, surfaceRunSettings: { ...DEFAULT_SURFACE_RUN_SETTINGS, enabled: false } }), false);
  assert.deepEqual(batchRequest({ result, editorSource: 'map' }), { json: 'map', ...settings, axis: 'time', inputs: batch.inputs });
});

test('pair fraction includes invalid, rejected and physically unfinished cells; uncomputed cells keep it unknown', () => {
  const cell = (status, computed = true) => ({ status, computed });
  const cells = Array.from({ length: 625 }, (_, i) => cell(
    i < 62 ? 'safe' : ['collision', 'invalid', 'rejected', 'incomplete'][i % 4]));
  const target = { ...pair(0), pair: { status: 'ready', spanMs: 600, complete: true, cells } };
  assert.deepEqual(batchCoverage(target), { state: 'ready', computed: 625, total: 625, safe: 62, fraction: 62 / 625 });
  cells[0] = cell('incomplete', false);
  assert.deepEqual(batchCoverage(target), { state: 'pending', computed: 624, total: 625 });
  assert.deepEqual(batchCoverage({ kind: 'unsupported' }), { state: 'unsupported' });
});

test('single counts its 25 cells; omission remains separate and manual preview keeps its original range', () => {
  const probes = Array.from({ length: 25 }, (_, i) => ({ status: i === 12 ? 'safe' : 'incomplete', computed: true, endTime: null }));
  const single = { spanMs: 600, status: 'ready', complete: false, probes, omission: { computed: false, status: 'incomplete' } };
  assert.deepEqual(batchCoverage({ kind: 'single', single }), { state: 'ready', computed: 25, total: 25, safe: 1, fraction: 1 / 25 });
  const cells = singleMatrixCells(single);
  assert.deepEqual([cells[0].firstOffsetMs, cells.at(-1).firstOffsetMs], [-600, 600]);
  assert.equal(singleMatrixCells(null).length, 13);
  const snapshot = { provenance: { source: '{}' }, result, inputIndex: 0 };
  const selected = { offsetMs: -600, omit: false };
  assert.equal('spanMs' in inputToleranceRequest(snapshot), false);
  assert.equal(inputToleranceRequest({ ...snapshot, spanMs: 600 }, selected).offsetMs, -600);
  assert.equal(inputToleranceRequest({ ...snapshot, spanMs: 600 }, selected).spanMs, 600);
  assert.notEqual(toleranceColor(0), toleranceColor(1));
});

function response(events, chunkSize = 15) {
  const bytes = new TextEncoder().encode(events.map(event => JSON.stringify(event)).join('\n'));
  return new Response(new ReadableStream({
    start(controller) {
      for (let i = 0; i < bytes.length; i += chunkSize) controller.enqueue(bytes.slice(i, i + chunkSize));
      controller.close();
    },
  }));
}

test('batch stream handles fragmented UTF-8, plan/progress checkpoints and terminal metadata', async () => {
  const plan = { type: 'plan', batch, checkpoints: [] };
  const progress = { type: 'progress', completed: 1, total: 2, checkpoint: { inputIndex: 0, payload: 'готово' } };
  const terminal = { type: 'result', canonicalJson: '{}', batch };
  const received = [];
  assert.deepEqual(await readBatchResponse(response([plan, progress, terminal]), {
    onPlan: event => received.push(event), onProgress: event => received.push(event),
  }), terminal);
  assert.deepEqual(received, [plan, progress]);
});

test('cancellation cannot deliver a queued result after a completed checkpoint', async () => {
  const abort = new AbortController();
  const progress = { type: 'progress', completed: 1, total: 2, checkpoint: { inputIndex: 0, payload: 'готово' } };
  const terminal = { type: 'result', canonicalJson: '{}', batch };
  const received = [];
  await assert.rejects(readBatchResponse(response([progress, terminal], 4096), {
    signal: abort.signal, onProgress(event) { received.push(event); abort.abort(); },
  }), { name: 'AbortError' });
  assert.deepEqual(received, [progress]);
});
