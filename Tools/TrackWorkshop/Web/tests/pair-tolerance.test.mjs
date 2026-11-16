import test from 'node:test';
import assert from 'node:assert/strict';
import { ReplayRoute } from '../model.mjs';
import { ToleranceRequestState } from '../input-tolerance.mjs';
import { PAIR_SPANS, PairGrid, matchesPairMinimum, matchesPairReport, pairAxis, pairKey, pairOverlay, pairRequest } from '../pair-tolerance.mjs';
import { DEFAULT_SURFACE_RUN_SETTINGS } from '../run-settings.mjs';

const settings = { profile: 'Chosen', warningDuration: 0.8, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS };
const inputs = [0.35, 0.5, 0.7, 1.05];
const samples = Object.freeze([0, 0.5, 1, 1.5, 2].map(time => Object.freeze({ time, x: 100 * time, y: 20 * time })));
const result = Object.freeze({ ...settings, axis: 'time', inputs: inputs.map(time => ({ value: time, time })), samples });
const snapshot = { result, firstInputIndex: 1, provenance: { source: '{"track":"replay"}' }, editorSource: '{"track":"editor"}',
  axis: 'distance', inputText: '[5,8]', loadRevision: 1, settings };
const baseline = { inputs, firstInputTime: 0.5, secondInputTime: 0.7, startTime: 0.5, endTime: 1.75, endX: 175 };
const zero = { firstOffsetMs: 0, secondOffsetMs: 0 }, later = { firstOffsetMs: 0, secondOffsetMs: 50 };
const blank = { firstInputTime: null, secondInputTime: null, pairIntervalMs: null, minimumIntervalMs: null, startTime: null, endTime: null, endX: null };
function cell(offset, status = 'safe', extras = {}) {
  return { ...offset, status, message: '', computed: true, firstInputTime: 0.5 + offset.firstOffsetMs / 1000,
    secondInputTime: 0.7 + offset.secondOffsetMs / 1000, pairIntervalMs: 250, minimumIntervalMs: 150,
    startTime: baseline.startTime, endTime: baseline.endTime, endX: baseline.endX, collisions: status === 'collision' ? 1 : 0, ...extras };
}

test('all three nested grids contain the expected coordinates, with two axes and the original first in the work queue', () => {
  const grid = new PairGrid();
  for (const [span, width] of PAIR_SPANS.map((span, i) => [span, [13, 25, 41][i]])) {
    grid.spanMs = span;
    assert.equal(pairAxis(span).length, width);
    assert.equal(grid.offsets.length, width ** 2);
    assert.equal(new Set(grid.offsets.map(pairKey)).size, width ** 2);
    assert.deepEqual(grid.offsets[0], { firstOffsetMs: -span, secondOffsetMs: -span });
    assert.deepEqual(grid.offsets[1], { firstOffsetMs: -span + 50, secondOffsetMs: -span });
    assert.deepEqual(grid.offsets.at(-1), { firstOffsetMs: span, secondOffsetMs: span });
    assert.deepEqual(grid.missing()[0], zero);
  }
});

test('expansion and retries preserve all computed outcomes, including invalid and physically incomplete cells', () => {
  const grid = new PairGrid();
  const original = grid.offsets;
  const [invalid, physical, timeout] = original;
  grid.apply({ status: 'ready', baseline, cells: [cell(invalid, 'invalid', blank), cell(physical, 'incomplete'),
    cell(timeout, 'incomplete', { ...blank, computed: false }), cell(zero)] });
  assert.deepEqual(grid.counts(0), { total: 169, computed: 3, safe: 1, collision: 0, invalid: 1, rejected: 0, incomplete: 1,
    pending: 166, matching: 3, matchingSafe: 1, matchingCollision: 0 });
  const pending = new Set(grid.missing().map(pairKey));
  for (const offset of [invalid, physical, zero]) assert.equal(pending.has(pairKey(offset)), false);
  assert.equal(pending.has(pairKey(timeout)), true);

  grid.apply({ status: 'ready', baseline, cells: grid.missing().map(offset => cell(offset)) });
  assert.equal(grid.missing().length, 0);
  grid.spanMs = 600;
  assert.equal(grid.missing().length, 625 - 169);
  assert.ok(grid.missing().every(offset => Math.max(Math.abs(offset.firstOffsetMs), Math.abs(offset.secondOffsetMs)) > 300));
  grid.spanMs = 1000;
  assert.equal(grid.missing().length, 1681 - 169);
  grid.spanMs = 300;
  assert.equal(grid.cells.get(pairKey(physical)).status, 'incomplete');
  assert.equal(grid.missing().length, 0);
});

test('tempo filters compare the shortest adjacent interval and leave cached statuses and progress intact', () => {
  const grid = new PairGrid();
  const shortExternal = cell(zero, 'safe', { pairIntervalMs: 325, minimumIntervalMs: 149.99 });
  const minimum250 = cell(later, 'safe', { pairIntervalMs: 375, minimumIntervalMs: 250 });
  grid.apply({ status: 'ready', baseline, cells: [shortExternal, minimum250] });
  assert.equal(matchesPairMinimum(shortExternal, 0), true);
  assert.equal(matchesPairMinimum(shortExternal, 150), false);
  assert.equal(matchesPairMinimum(minimum250, 250), true);
  assert.equal(matchesPairMinimum({ ...minimum250, computed: false }, 250), false);
  assert.deepEqual(grid.counts(250), { ...grid.counts(0), matching: 1, matchingSafe: 1, matchingCollision: 0 });
  assert.equal(grid.cells.get(pairKey(zero)), shortExternal);
});

test('reports accept an exact subset and partial budget results; wrong identity, coordinates and baseline are rejected', () => {
  const report = { status: 'ready', message: '', firstInputIndex: 1, spanMs: 300, stepMs: 50, complete: false,
    stopReason: 'time-budget', baseline, cells: [cell(zero), cell(later, 'incomplete', { ...blank, computed: false })] };
  const match = reply => matchesPairReport(reply, snapshot, 300, [zero, later], baseline);
  assert.equal(match(report), true);
  for (const patch of [{ firstInputIndex: 0 }, { stepMs: 25 }, { spanMs: 600 }, { cells: report.cells.toReversed?.() ?? [...report.cells].reverse() },
    { cells: [report.cells[0]] }, { baseline: { ...baseline, inputs: [0.3, 0.5, 0.7, 1.05] } },
    { baseline: { ...baseline, endX: 180 } }, { complete: true, stopReason: 'completed' }]) {
    assert.equal(match({ ...report, ...patch }), false);
  }
  const unsupported = { ...report, status: 'unsupported', message: 'Разные полёты.', baseline: null, cells: [] };
  const grid = new PairGrid();
  assert.equal(match(unsupported), true);
  grid.apply(unsupported);
  assert.equal(grid.unsupported, '');
  grid.apply({ ...unsupported, complete: true, stopReason: 'completed' });
  assert.equal(grid.unsupported, unsupported.message);
});

test('preview uses the displayed replay provenance and returns a clipped collision path without changing the original', () => {
  const route = new ReplayRoute();
  route.setResult(result);
  const path = route.path;
  assert.deepEqual(pairRequest(snapshot, later), { ...settings, json: snapshot.provenance.source,
    axis: 'time', inputs, firstInputIndex: 1, ...later });
  const collision = cell(later, 'collision');
  const overlay = pairOverlay({ cell: collision, samples }, collision, baseline, 1);
  assert.equal(overlay.path, 'M50.000,10.000 L100.000,20.000 L150.000,30.000 L175.000,35.000');
  assert.equal(route.result, result);
  assert.equal(route.path, path);
  assert.equal(snapshot.inputText, '[5,8]');
  assert.equal(pairOverlay({ cell: cell(zero, 'collision'), samples }, collision, baseline, 1), null);
  assert.equal(pairOverlay({ cell: cell(later), samples }, collision, baseline, 1), null);
});

test('replacement or cancellation blocks a late paired report or overlay', () => {
  const requests = new ToleranceRequestState();
  const request = requests.start('pair-report', snapshot);
  assert.equal(requests.current(request, { ...snapshot }), true);
  for (const change of [{ editorSource: '{}' }, { inputText: '[1]' }, { loadRevision: 2 }, { provenance: { ...snapshot.provenance } },
    { settings: { ...settings, warningDuration: 1 } }]) assert.equal(requests.current(request, { ...snapshot, ...change }), false);
  const preview = requests.start('pair-preview', snapshot);
  requests.finish(request);
  assert.equal(request.abort.signal.aborted, true);
  assert.equal(requests.current(preview, snapshot), true);
  requests.cancel();
  assert.equal(requests.current(preview, snapshot), false);
});
