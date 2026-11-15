import test from 'node:test';
import assert from 'node:assert/strict';
import { LiveConnection, LiveStepClock } from '../live.mjs';
import { MAX_REPLAY_INPUTS } from '../model.mjs';

test('separate actions keep their real tick spacing across batch boundaries', () => {
  const clock = new LiveStepClock();
  clock.resume(0);
  clock.press(1);
  clock.press(32);
  assert.equal(clock.advance(30), null);
  assert.deepEqual(clock.advance(34), { type: 'step', seq: 1, steps: 4, inputs: [1] });
  assert.deepEqual(clock.advance(67), { type: 'step', seq: 2, steps: 4, inputs: [0] });
  assert.deepEqual(clock.acknowledge(1), [1]);
  assert.deepEqual(clock.acknowledge(2), [32]);
});

test('a late press is never backdated into overdue batches', () => {
  const clock = new LiveStepClock();
  clock.resume(0);
  clock.press(98);
  assert.deepEqual([clock.advance(100), clock.advance(100), clock.advance(100)].map(step => step.inputs), [[], [], []]);
  assert.deepEqual(clock.advance(134).inputs, [0]);
});

test('distinct presses in one tick remain distinct and acknowledgements stay ordered', () => {
  const clock = new LiveStepClock();
  clock.resume(0);
  clock.press(1);
  clock.press(2);
  clock.press(3);
  assert.deepEqual(clock.advance(34).inputs, [1, 2, 3]);
  assert.throws(() => clock.acknowledge(2), /порядок/);
  assert.deepEqual(clock.acknowledge(1), [1, 2, 3]);
});

test('pause discards unsent presses and elapsed time while retaining outstanding acknowledgements', () => {
  const clock = new LiveStepClock();
  clock.resume(0);
  clock.press(1);
  clock.advance(34);
  clock.press(35);
  clock.pause();
  assert.equal(clock.advance(9000), null);
  assert.deepEqual(clock.acknowledge(1), [1]);
  clock.resume(9000);
  assert.equal(clock.advance(9001), null);
  assert.deepEqual(clock.advance(9034), { type: 'step', seq: 2, steps: 4, inputs: [] });
});

test('clock caps the unacknowledged window at 28 ticks and detects long frame gaps', () => {
  const clock = new LiveStepClock();
  clock.resume(0);
  for (let n = 1; n <= 7; n++) assert.equal(clock.advance(n * 34).steps, 4);
  assert.deepEqual(clock.advance(8 * 34), { stalled: true });
  assert.equal(clock.pending.size, 7);
  const delayed = new LiveStepClock();
  delayed.resume(0);
  assert.deepEqual(delayed.advance(1000), { stalled: true });
  assert.equal(delayed.pending.size, 0);
});

test('authoritative reward transitions keep untouched rewards available after collisions', () => {
  const run = new LiveConnection(() => {});
  run.paused = true;
  const state = (seq, events, hiddenStates = []) => run.receive(JSON.stringify({ type: 'state', seq, phase: 'racing', events, hiddenStates, inputs: [] }));
  state(0, [{ type: 'collected', id: 'collected' }, { type: 'collision', id: 'uncollected' }], [{ id: 'stone', phase: 'Warning' }]);
  assert.equal(run.rewards.get('collected'), 'pending');
  assert.equal(run.rewards.has('uncollected'), false);
  assert.equal(run.hidden.get('stone'), 'Warning');
  run.clock.resume(0);
  run.clock.advance(34);
  state(1, [{ type: 'failedReward', id: 'collected' }], [{ id: 'stone', phase: 'Solid' }]);
  assert.equal(run.rewards.get('collected'), 'failed');
  assert.equal(run.collected.size, 1);
  assert.equal(run.hidden.get('stone'), 'Solid');
});

test('save waits for final acknowledgement and exports exact racing times including ignored actions', () => {
  const run = new LiveConnection(() => {});
  run.paused = true;
  run.stopFrames = () => run.clock.pause();
  const state = (seq, phase, inputs) => run.receive(JSON.stringify({ type: 'state', seq, phase, inputs, hiddenStates: [], events: [] }));
  state(0, 'countdown', []);
  run.clock.resume(0);
  run.clock.press(1);
  run.clock.advance(34);
  state(1, 'racing', [{ status: 'ignored', time: 0 }]);
  assert.equal(run.recordedInputs.length, 0);
  run.clock.press(35);
  run.clock.press(42);
  run.clock.advance(67);
  run.clock.press(68);
  run.clock.advance(100);
  const preciseTime = 0.008333333333333333;
  state(2, 'racing', [{ status: 'accepted', time: 0 }, { status: 'ignored', time: preciseTime }]);
  assert.equal(run.recordingTimes(), null);
  run.clock.press(101); // Queued locally but never sent.
  state(3, 'finished', [{ status: 'unreached', time: null }]);
  assert.deepEqual(run.recordingTimes(), [0, preciseTime]);
  assert.equal(new LiveConnection(() => {}).recordingTimes(), null);
});

test('connection errors allow confirmed presses but an oversized recording is never truncated', () => {
  const run = new LiveConnection(() => {});
  run.error = 'Связь прервана';
  run.clock.pending.set(1, [12]);
  run.recordedInputs = Array.from({ length: MAX_REPLAY_INPUTS }, (_, i) => ({ time: i / 120, status: 'accepted' }));
  assert.equal(run.recordingTimes().length, MAX_REPLAY_INPUTS);
  run.recordedInputs.push({ time: MAX_REPLAY_INPUTS / 120, status: 'ignored' });
  assert.equal(run.recordingTimes(), null);
  assert.equal(run.recordedInputs.length, MAX_REPLAY_INPUTS + 1);
});

test('selected start waits in Ready and joins authoritative prefix with later confirmed presses', () => {
  const run = new LiveConnection(() => {});
  let started = 0;
  run.startFrames = () => { started++; run.clock.resume(0); };
  run.stopFrames = () => run.clock.pause();
  const receive = (seq, properties) => run.receive(JSON.stringify({ type: 'state', seq, phase: 'racing',
    inputs: [], hiddenStates: [], events: [], ...properties }));
  receive(0, { requestedStartX: 600, sample: { x: 600.5, time: 2.9 }, prefixInputs: [
    { status: 'accepted', time: 0 }, { status: 'ignored', time: 1.1250000000000002 },
  ] });
  assert.equal(run.readyAtStart, true);
  assert.equal(run.paused, true);
  assert.equal(started, 0);
  assert.deepEqual(run.recordingTimes(), [0, 1.1250000000000002]);
  run.press(1);
  assert.equal(run.clock.presses.length, 0);
  assert.equal(run.resume(), true);
  assert.equal(run.readyAtStart, false);
  assert.equal(started, 1);
  run.press(2);
  run.clock.advance(34);
  receive(1, { inputs: [{ status: 'accepted', time: 2.908333333333333 }] });
  run.pause();
  assert.deepEqual(run.recordingTimes(), [0, 1.1250000000000002, 2.908333333333333]);
  assert.equal(run.acceptedInputs, 2);
  assert.equal(run.ignoredInputs, 1);
});

test('quick restart starts from the restored prefix without a ready pause or an extra action', () => {
  const run = new LiveConnection(() => {}, { autoStart: true });
  let started = 0;
  run.startFrames = () => { started++; run.clock.resume(0); };
  run.receive(JSON.stringify({ type: 'state', seq: 0, phase: 'racing', requestedStartX: 600,
    sample: { x: 600.5, time: 2.9 }, inputs: [], events: [], hiddenStates: [],
    prefixInputs: [{ status: 'accepted', time: 1.125 }] }));
  assert.equal(started, 1);
  assert.equal(run.paused, false);
  assert.equal(run.readyAtStart, false);
  assert.deepEqual(run.recordedInputs.map(input => input.time), [1.125]);
  assert.deepEqual(run.clock.advance(34).inputs, []);
});

test('focus loss while quick restart is connecting prevents automatic movement', () => {
  const run = new LiveConnection(() => {}, { autoStart: true });
  let started = 0;
  run.startFrames = () => { started++; };
  run.stopFrames = () => run.clock.pause();
  run.pause('Окно потеряло фокус');
  run.receive(JSON.stringify({ type: 'state', seq: 0, phase: 'racing', requestedStartX: 600,
    inputs: [], events: [], hiddenStates: [], prefixInputs: [] }));
  assert.equal(started, 0);
  assert.equal(run.paused, true);
  assert.equal(run.reason, 'Окно потеряло фокус');
  assert.equal(run.resume(), true);
  assert.equal(started, 1);
});
