import test from 'node:test';
import assert from 'node:assert/strict';
import { batchCoverage, batchRequest, matchesBatchReplay, readBatchResponse, toleranceColor, toleranceScore } from '../tolerance-batch.mjs';
import { DEFAULT_SURFACE_RUN_SETTINGS } from '../run-settings.mjs';
import { maneuver, batchFor, replay } from './deep-fixtures.mjs';

const result = replay([.4, .8, 1.2]);
const settings = { profile: result.profile, warningDuration: result.warningDuration, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS };
const batch = batchFor(result, [maneuver([0, 1, 2])]);

test('all N points map to exactly the same whole maneuver; source tape/settings/range gate freshness', () => {
  assert.equal(matchesBatchReplay(batch, result), true);
  for (const changed of [null, { ...result, axis: 'distance' }, { ...result, warningDuration: .8 }, replay([.4, .8])])
    assert.equal(matchesBatchReplay(batch, changed), false);
  for (const changed of [{ ...batch, spanMs: 300 }, { ...batch, commandTargetIndices: [0, 0, 1] },
    { ...batch, targets: [{ kind: 'pair', inputIndex: 0, firstInputIndex: 0 }] },
    { ...batch, targets: [{ ...batch.targets[0], maneuver: { rulesVersion: 1 } }] }])
    assert.equal(matchesBatchReplay(changed, result), false);
  assert.deepEqual(batchRequest({ result, editorSource: 'map' }), { json: 'map', ...settings, axis: 'time', inputs: batch.inputs });
});

test('G/V excludes inadmissible tuples and preserves exact planned/computed counts', () => {
  const target = maneuver([0, 1, 2], { total: '15625', cursor: '15625', valid: '12000', computed: '12000', safe: '600' });
  const coverage = batchCoverage(target);
  assert.equal(coverage.state, 'ready'); assert.equal(coverage.fraction, .05);
  assert.equal(coverage.total, 15625n); assert.equal(coverage.computed, 12000n);
  target.maneuver.complete = false; target.maneuver.cursor = '13000';
  assert.equal(batchCoverage(target).state, 'pending');
  assert.equal(batchCoverage(target).fraction, .05);
  Object.assign(target.maneuver, { total: '88817841970012523233890533447265625', cursor: '256', valid: '200', computed: '200', safe: '1' });
  assert.equal(batchCoverage(target).total, 88817841970012523233890533447265625n);
});

test('zero admissible tuples is unknown, zero safe among valid tuples is zero percent', () => {
  const empty = maneuver([0], { valid: '0', computed: '0', safe: '0' });
  assert.equal(batchCoverage(empty).fraction, null);
  assert.equal(batchCoverage(empty).state, 'ready');
  const failure = maneuver([0], { valid: '25', computed: '25', safe: '0' });
  assert.equal(batchCoverage(failure).fraction, 0);
  assert.equal(batchCoverage({ kind: 'unsupported' }).state, 'unsupported');
});

test('direct scale has no historical calibration or saturation', () => {
  for (const fraction of [0, .07, .55, .6, 1]) assert.equal(toleranceScore(fraction), fraction);
  assert.equal(toleranceScore(null), null);
  assert.equal(toleranceColor(0), 'rgb(176 74 67)');
  assert.equal(toleranceColor(.5), 'rgb(167 118 32)');
  assert.equal(toleranceColor(1), 'rgb(47 121 81)');
  assert.notEqual(toleranceColor(.6), toleranceColor(1));
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
