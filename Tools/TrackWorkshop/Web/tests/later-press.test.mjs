import test from 'node:test';
import assert from 'node:assert/strict';
import { LaterPressRequestState, isMatchingLaterPressReport, isMatchingLaterPressReplay, laterPressRequest } from '../later-press.mjs';
import { DEFAULT_SURFACE_RUN_SETTINGS } from '../run-settings.mjs';

const settings = { profile: 'Chosen', warningDuration: 0.8, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS };
const summary = { status: 'Finished', finishTime: 4, collisions: 0, boosts: 1, instantBoosts: 0, slows: 0, coins: 1,
  finishBoosts: 1, acceptedInputs: 3, ignoredInputs: 0, unreachedInputs: 0, hitIds: [], failedIds: [],
  successfulIds: ['bonus', 'finish'], collectedIds: ['bonus', 'coin', 'finish'] };
const landing = { inputIndex: 1, inputTime: 1.1, landingTime: 1 };
const result = { ...settings, axis: 'distance', summary, landingIntervals: [landing], inputs: [
  { time: 0.2, value: 24 }, { time: 1.1, value: 206 }, { time: 2, value: 410 },
] };
const snapshot = { source: '{"track":"prepared"}', result, inputIndex: 1, minimumMs: 200 };
const candidateSummary = { ...summary, finishTime: 4.1 };
const candidate = { delayMs: 250, inputTime: 1.25, finishTime: 4.1, finishDelta: 0.1, collisions: 0,
  inputs: [0.2, 1.25, 2], summary: candidateSummary };
const report = { status: 'ready', attempted: 3, candidateCount: 8, complete: false, stepMs: 50, spanMs: 1000,
  baseline: { ...landing, finishTime: 4, collisions: 0, inputs: [0.2, 1.1, 2] }, alternatives: [candidate] };

test('later analysis uses the displayed replay source and original distance values', () => {
  const request = laterPressRequest({ ...snapshot, inputText: '[1.7]', axis: 'time', settings: { ...settings, profile: 'FastFlip' } });
  assert.deepEqual(request, { json: snapshot.source, ...settings, axis: 'distance', inputs: [24, 206, 410], inputIndex: 1, minimumMs: 200 });
});

test('report accepts partial and empty results but rejects mismatched baseline or moved neighboring presses', () => {
  assert.equal(isMatchingLaterPressReport(report, snapshot), true);
  assert.equal(isMatchingLaterPressReport({ ...report, attempted: 0, candidateCount: 0, complete: true, alternatives: [] }, snapshot), true);
  assert.equal(isMatchingLaterPressReport({ ...report, status: 'unsupported', message: 'Требуется финиш.', baseline: null, alternatives: [] }, snapshot), true);
  assert.equal(isMatchingLaterPressReport(report, { ...snapshot, minimumMs: 300 }), false);
  assert.equal(isMatchingLaterPressReport({ ...report, baseline: { ...report.baseline, landingTime: 0.93 } }, snapshot), false);
  for (const different of [{ inputs: [0.25, 1.25, 2] }, { inputs: [0.2, 1.25, 2.05] }, { delayMs: 225 }, { finishDelta: 0 }])
    assert.equal(isMatchingLaterPressReport({ ...report, alternatives: [{ ...candidate, ...different }] }, snapshot), false);
});

test('only a verified replay under the same settings and landing becomes the selected variant', () => {
  const replay = { ...settings, axis: 'time', summary: candidateSummary, landingIntervals: [{ ...landing, inputTime: 1.25 }] };
  assert.equal(isMatchingLaterPressReplay(replay, candidate, snapshot), true);
  for (const different of [{ axis: 'distance' }, { warningDuration: 1 }, { landingIntervals: [] },
    { summary: { ...candidateSummary, finishTime: 4.2 } }, { summary: { ...candidateSummary, successfulIds: ['bonus'] } },
    { summary: { ...candidateSummary, collisions: 1 } }])
    assert.equal(isMatchingLaterPressReplay({ ...replay, ...different }, candidate, snapshot), false);
});

test('pending search and replay reject stale editor data and cannot commit after cancellation or replacement', () => {
  const context = { source: '{"track":"editor"}', result, settings, inputText: '[24, 206, 410]', axis: 'distance', minimumMs: 200, loadRevision: 0 };
  const state = new LaterPressRequestState();
  const original = state.start('search', context);
  assert.equal(state.current(original, { ...context, settings: { ...settings } }), true);
  for (const changed of [{ source: '{}' }, { result: { ...result } }, { inputText: '[24]' }, { axis: 'time' },
    { minimumMs: 250 }, { loadRevision: 1 }, { settings: { ...settings, warningDuration: 1 } }])
    assert.equal(state.current(original, { ...context, ...changed }), false);
  const replacement = state.start('replay', context);
  assert.equal(original.abort.signal.aborted, true);
  assert.equal(state.current(original, context), false);
  state.finish(original);
  assert.equal(state.current(replacement, context), true);
  state.cancel();
  assert.equal(replacement.abort.signal.aborted, true);
  assert.equal(state.current(replacement, context), false);
});
