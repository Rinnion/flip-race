import test from 'node:test';
import assert from 'node:assert/strict';
import { inputTempo } from '../input-tempo.mjs';

const times = values => values.map((value, index) => ({ index, value, time: value, status: 'accepted' }));

test('tempo classifies all successive commands at exact 150 and 250 ms boundaries', () => {
  const inputs = times([0, 0.1499, 0.2999, 0.5499, 0.8000, 0.9000, 1.0000, 1.2000, 1.3000]);
  inputs[1].status = 'ignored';
  inputs[8].status = 'unreached';
  inputs[8].time = null;
  const result = inputTempo({ axis: 'time', inputs });
  assert.deepEqual(result.commands.map(({ interval }) => interval?.kind ?? null),
    [null, 'critical', 'normal', 'normal', 'easy', 'critical', 'critical', 'normal', 'critical']);
  assert.deepEqual(result.counts, { critical: 4, normal: 3, easy: 1, unknown: 0 });
  assert.equal(result.criticalFraction, 0.5);
  assert.equal(result.longestCritical, 2);
  assert.deepEqual(result.gaps, [650.1, 200]);
});

test('distance tempo uses resolved times, with no interval or episode gap inferred across missing time', () => {
  const inputs = [0.2, 0.3, null, 0.8, 0.9, null].map((time, index) => ({ index, value: index * 12, time }));
  const result = inputTempo({ axis: 'distance', inputs });
  assert.deepEqual(result.counts, { critical: 2, normal: 0, easy: 0, unknown: 3 });
  assert.equal(result.measured, 2);
  assert.equal(result.criticalFraction, 1);
  assert.equal(result.longestCritical, 1);
  assert.deepEqual(result.gaps, []);
  assert.equal(inputTempo({ axis: 'distance', inputs: inputs.slice(-1) }).criticalFraction, null);
  assert.deepEqual(inputTempo({ axis: 'time', inputs: [] }).commands, []);
});
