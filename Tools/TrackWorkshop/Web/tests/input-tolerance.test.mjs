import test from 'node:test';
import assert from 'node:assert/strict';
import { ReplayRoute } from '../model.mjs';
import { ToleranceRequestState, adjacentSafeProbes, inputToleranceRequest, isMatchingToleranceReport, toleranceOverlay } from '../input-tolerance.mjs';
import { DEFAULT_SURFACE_RUN_SETTINGS } from '../run-settings.mjs';

const settings = { profile: 'Chosen', warningDuration: 0.8, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS };
const result = { ...settings, axis: 'distance', inputs: [
  { time: 0.2, value: 24 }, { time: 1.1, value: 206 }, { time: 2, value: 410 },
] };
const snapshot = { provenance: { source: '{"track":"prepared"}' }, editorSource: '{"track":"editor"}',
  result, inputIndex: 1, inputText: '[1.7]', axis: 'time', settings, loadRevision: 0 };

function probe(offsetMs, status, fields = {}) {
  return { offsetMs, omit: offsetMs === null, status, message: '', inputTime: offsetMs === null ? null : 1.1 + offsetMs / 1000,
    startTime: 0.9, endTime: 1.9, endX: 380, collisions: status === 'collision' ? 1 : 0,
    minimumIntervalMs: 700, botCompatible: true, ...fields };
}

const statuses = ['invalid', 'safe', 'collision', 'safe', 'safe', 'safe', 'safe', 'safe', 'rejected', 'safe', 'incomplete', 'incomplete', 'incomplete'];
const report = { status: 'ready', message: 'Бюджет.', inputIndex: 1, stepMs: 50, spanMs: 300, attempted: 10,
  complete: false, stopReason: 'time-budget', baseline: { inputs: [0.2, 1.1, 2], inputTime: 1.1, startTime: 0.9, endTime: 1.9, endX: 380 },
  probes: statuses.map((status, index) => probe(index * 50 - 300, status)), omission: probe(null, 'safe') };

test('requests use the displayed replay source, resolution axis and settings even with a different manual draft', () => {
  const expected = { json: snapshot.provenance.source, ...settings, axis: 'distance', inputs: [24, 206, 410], inputIndex: 1 };
  const draft = { ...snapshot, settings: { ...settings, profile: 'FastFlip' } };
  assert.deepEqual(inputToleranceRequest(draft), expected);
  assert.deepEqual(inputToleranceRequest(draft, report.probes[4]), { ...expected, offsetMs: -100, omit: false });
  assert.deepEqual(inputToleranceRequest(draft, report.omission), { ...expected, offsetMs: null, omit: true });
});

test('report validates the entire discrete grid, omission, attempted count and reference tape', () => {
  assert.equal(isMatchingToleranceReport(report, snapshot), true);
  for (const change of [
    { inputIndex: 0 }, { stepMs: 100 }, { attempted: 11 }, { omission: null },
    { probes: report.probes.slice(1) },
    { probes: [...report.probes.slice(0, -1), { ...report.probes.at(-1), offsetMs: 250 }] },
    { baseline: { ...report.baseline, inputs: [0.2, 1.1, 2.05] } },
  ]) assert.equal(isMatchingToleranceReport({ ...report, ...change }, snapshot), false);

  const unsupported = { ...report, status: 'unsupported', baseline: null, attempted: 0 };
  assert.equal(isMatchingToleranceReport({ ...unsupported, probes: [], omission: null }, snapshot), true);
  const incomplete = value => ({ ...value, status: 'incomplete', inputTime: null, startTime: null, endTime: null, endX: null });
  assert.equal(isMatchingToleranceReport({ ...unsupported, probes: report.probes.map(incomplete), omission: incomplete(report.omission) }, snapshot), true);
  assert.equal(isMatchingToleranceReport({ ...unsupported, probes: report.probes, omission: report.omission }, snapshot), false);
});

test('a local manoeuvre may be evaluable when a later scheduled command is unreached', () => {
  const timed = { ...result, axis: 'time', inputs: [0.2, 1.1, 2].map((value, index) => ({ value, time: index === 2 ? null : value })) };
  assert.equal(isMatchingToleranceReport(report, { ...snapshot, result: timed }), true);
  assert.equal(isMatchingToleranceReport(report, { ...snapshot, result: { ...timed, axis: 'distance' } }), false);
});

test('adjacent safe points stop at each gap and disconnected safe shifts remain individually available', () => {
  assert.deepEqual(adjacentSafeProbes(report), { earlierMs: 150, laterMs: 50, disconnected: 2 });
  const withoutZero = report.probes.map(value => value.offsetMs ? value : { ...value, status: 'incomplete' });
  assert.equal(adjacentSafeProbes({ probes: withoutZero }), null);
});

test('a failure preview clips the common prefix and preserves the original replay and author tape', () => {
  const samples = Object.freeze([0, 1, 1.5, 2].map(time => Object.freeze({ time, x: time * 100, y: time * 10 })));
  const replay = Object.freeze({ ...result, samples });
  const route = new ReplayRoute();
  route.setResult(replay);
  const originalPath = route.path;
  const candidate = probe(-200, 'collision');
  const overlay = toleranceOverlay({ probe: candidate, samples }, candidate);
  assert.equal(overlay.path, 'M90.000,9.000 L100.000,10.000 L150.000,15.000 L190.000,19.000');
  assert.equal(overlay.startX, 90);
  assert.equal(route.result, replay);
  assert.equal(route.path, originalPath);
  assert.equal(snapshot.inputText, '[1.7]');
  assert.equal(toleranceOverlay({ probe: probe(-150, 'collision'), samples }, candidate), null);
  assert.equal(toleranceOverlay({ probe: candidate, samples: [...samples].reverse() }, candidate), null);

  const invalid = probe(-300, 'invalid', { startTime: null, endTime: null, endX: null });
  assert.deepEqual(toleranceOverlay({ probe: invalid, samples: [] }, invalid), { probe: invalid, path: '', point: null, startX: null });
  const atImpact = probe(-200, 'collision', { startTime: 1, endTime: 1 });
  assert.deepEqual(toleranceOverlay({ probe: atImpact, samples }, atImpact).point, samples[1]);
});

test('report and preview ignore stale source, settings and tape, and never apply after cancellation', () => {
  const state = new ToleranceRequestState();
  const original = state.start('report', snapshot);
  assert.equal(state.current(original, { ...snapshot, settings: { ...settings } }), true);
  for (const changed of [{ editorSource: '{}' }, { provenance: { ...snapshot.provenance } }, { result: { ...result } },
    { inputText: '[24]' }, { axis: 'distance' }, { loadRevision: 1 }, { settings: { ...settings, warningDuration: 1 } }]) {
    assert.equal(state.current(original, { ...snapshot, ...changed }), false);
  }
  const replacement = state.start('preview', snapshot);
  assert.equal(original.abort.signal.aborted, true);
  state.finish(original);
  assert.equal(state.current(replacement, snapshot), true);
  state.cancel();
  assert.equal(replacement.abort.signal.aborted, true);
  assert.equal(state.current(replacement, snapshot), false);
});
