import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { MANEUVER_OFFSETS, countFraction, conditionalCell, cellKey, cellProjection, maneuverRequest, maneuverOverlay } from '../maneuver-tolerance.mjs';
import { ToleranceRequestState } from '../input-tolerance.mjs';
import { toleranceColor } from '../tolerance-batch.mjs';
import { maneuver, replay } from './deep-fixtures.mjs';

const targetFor = () => maneuver([0, 1, 2], { baseline: { inputs: [.7, 1, 1.3, 2], startTime: .7, endTime: 1.7, endX: 170 },
  cells: [0, 1, 2].flatMap(inputIndex => MANEUVER_OFFSETS.map(offsetMs => ({ inputIndex, offsetMs, total: '625', valid: '100', computed: '100', safe: '7', witnessOffsetsMs: [0, -50, 50] }))) });

test('all commands have identical 25-offset rows and unconditional aggregate projections', () => {
  const target = targetFor();
  assert.equal(MANEUVER_OFFSETS.length, 25); assert.equal(MANEUVER_OFFSETS[0], -600); assert.equal(MANEUVER_OFFSETS.at(-1), 600);
  const before = structuredClone(target.maneuver.cells);
  const selected = conditionalCell(target.maneuver, 1, -50);
  assert.equal(cellProjection(selected, true).fraction, .07);
  assert.equal(cellProjection(selected, false).state, 'pending');
  assert.equal(cellProjection({ ...selected, valid: '0', computed: '0', safe: '0' }, true).state, 'empty');
  assert.equal(cellProjection({ ...selected, safe: '0' }, true).fraction, 0);
  assert.deepEqual(target.maneuver.cells, before);
  assert.equal(countFraction(10n ** 400n, 2n * 10n ** 400n), .5);
});

test('witness preview carries the whole tuple and actual alternative press points without mutating the reference', () => {
  const target = targetFor(), result = replay([.7, 1, 1.3, 2]), source = structuredClone(result);
  const selected = conditionalCell(target.maneuver, 1, -50), snapshot = { editorSource: 'exact JSON', result };
  const request = maneuverRequest(snapshot, target, selected);
  assert.equal(request.inputIndex, 1); assert.equal(request.offsetMs, -50); assert.deepEqual(request.offsetsMs, [0, -50, 50]);
  const preview = { status: 'safe', offsetsMs: [0, -50, 50], inputs: [.7, .95, 1.35, 2],
    samples: [.5, .7, .95, 1.35, 1.7].map(time => ({ time, x: time * 100, y: 20 })),
    pressPoints: [.7, .95, 1.35].map((time, index) => ({ index, time, x: time * 100, y: 20, status: 'accepted' })) };
  const overlay = maneuverOverlay(preview, target, selected);
  assert.deepEqual(overlay.commands.map(point => point.index), [0, 1, 2]);
  assert.deepEqual(overlay.commands.map(point => point.time), [.7, .95, 1.35]);
  assert.deepEqual(overlay.offsetsMs, [0, -50, 50]); assert.ok(overlay.path);
  for (const changed of [{ ...preview, status: 'collision' }, { ...preview, offsetsMs: [0, 0, 50] },
    { ...preview, inputs: [.7, .95, 1.35, 2.1] }, { ...preview, pressPoints: preview.pressPoints.slice(1) }])
    assert.equal(maneuverOverlay(changed, target, selected), null);
  assert.deepEqual(result, source);
});

class Element {
  children = []; dataset = {}; handlers = {}; attributes = {}; textContent = '';
  style = { setProperty() {} };
  classes = new Set(); classList = { toggle: (name, enabled) => enabled ? this.classes.add(name) : this.classes.delete(name) };
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  setAttribute(name, value) { this.attributes[name] = value; }
  addEventListener(name, callback) { this.handlers[name] = callback; }
  scrollIntoView() { this.scrollCount = (this.scrollCount ?? 0) + 1; }
}

async function ui() {
  const source = await readFile(new URL('../app.js', import.meta.url), 'utf8');
  const render = source.slice(source.indexOf('function renderDeepTolerance()'), source.indexOf('async function runDeepManeuver('));
  const selection = source.slice(source.indexOf('async function selectDeepCell('), source.indexOf('\nfunction resetTolerance()'));
  const execution = source.slice(source.indexOf('async function runDeepManeuver('), source.indexOf('async function selectDeepCell('));
  const elements = new Map(), $ = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
  const target = targetFor(), result = replay([.7, 1, 1.3, 2]);
  const snapshot = { editorSource: 'source', provenance: {}, result, settings: result, inputText: '[]', axis: 'time', loadRevision: 1 };
  let contextSnapshot = snapshot, requestCount = 0;
  const context = { $, document: { createElement: () => new Element() }, MANEUVER_OFFSETS, cellKey, conditionalCell, cellProjection,
    maneuverRequest, maneuverOverlay, toleranceColor, number: value => String(value),
    toleranceRequests: new ToleranceRequestState(), deepTolerance: { target, snapshot, selected: null, overlay: null, visible: true, status: '' },
    toleranceContext: () => contextSnapshot, canSelectAnalysisTarget: () => true, canCheckTolerance: () => true,
    showOverlayOnMap() {}, toleranceBatch: {}, batchTargetLabel: () => 'whole flight', batchCoverageLabel: () => 'G/V', draw() {},
    message: (id, value) => { $(id).textContent = value; }, api: async () => { requestCount++; return null; } };
  const saves = [];
  Object.assign(context, { state: { route: { result } }, targetAnalysis: {}, setTimeout, batchRequest: snapshot => ({ json: snapshot.editorSource }),
    canUseBatchReplay: () => true, controls: () => context.renderDeepTolerance(), renderAnalysisTarget() {},
    clearAnalysisTarget: () => { context.targetAnalysis.status = ''; },
    resetTolerance: () => { context.toleranceRequests.cancel(); Object.assign(context.deepTolerance, { target: null, snapshot: null, overlay: null, status: '' }); },
    applyBatchSave: saved => { saves.push(saved); contextSnapshot = { ...contextSnapshot, editorSource: saved.canonicalJson, provenance: {} }; } });
  vm.createContext(context); vm.runInContext(render + execution + selection, context);
  return { context, $, target, saves, setContext: value => { contextSnapshot = value; }, snapshot, requestCount: () => requestCount };
}

test('actual rows render selected-cell ring plus every witness offset without conditioning percentages', async () => {
  const { context, $ } = await ui();
  context.deepTolerance.selected = '1:-50'; context.deepTolerance.overlay = { offsetsMs: [0, -50, 50] };
  context.renderDeepTolerance();
  const rows = $('deep-matrix').children.slice(1);
  assert.equal(rows.length, 3);
  for (const row of rows) assert.equal(row.children.length, 26);
  const buttons = rows.flatMap(row => row.children.slice(1).map(td => td.children[0]));
  assert.equal(buttons.filter(button => button.classes.has('deep-witness')).length, 3);
  assert.equal(buttons.filter(button => button.attributes['aria-pressed'] === 'true').length, 1);
  assert.equal(new Set(buttons.map(button => button.textContent)).size, 1);
});

test('no-safe cell clears previous overlay automatically; changed source hides panel and blocks preview', async () => {
  const { context, $, target, snapshot, setContext, requestCount } = await ui();
  Object.assign(conditionalCell(target.maneuver, 1, 0), { safe: '0', witnessOffsetsMs: null });
  context.deepTolerance.overlay = { offsetsMs: [0, -50, 50] };
  await context.selectDeepCell(1, 0);
  assert.equal(context.deepTolerance.overlay, null); assert.equal(requestCount(), 0);
  assert.equal(context.deepTolerance.selected, '1:0');
  setContext({ ...snapshot, editorSource: 'edited' }); context.renderDeepTolerance();
  assert.equal($('deep-panel').hidden, true);
  await context.selectDeepCell(1, -50); assert.equal(requestCount(), 0);
});


test('selected command advances bounded chunks from saved JSON and stops on no progress', async () => {
  const { context, $, target, saves } = await ui();
  context.deepTolerance.target = null;
  const calls = [];
  context.api = async (path, request) => {
    calls.push(request);
    const current = structuredClone(target);
    Object.assign(current.maneuver, { complete: false, cursor: '256' });
    return { canonicalJson: `saved ${calls.length}`, target: current, batch: { targets: [current] } };
  };
  await context.runDeepManeuver(1);
  assert.equal(calls.length, 2); assert.equal(saves.length, 2);
  assert.equal($('deep-panel').scrollCount, 1, 'Chunk updates and completion preserve the cancel button viewport position');
  assert.equal(calls[0].inputIndex, 1); assert.equal(calls[1].inputIndex, 1);
  assert.equal(calls[1].json, 'saved 1');
  assert.match(context.deepTolerance.status, /не продвинул/);
  assert.equal($('deep-continue').hidden, false);
  assert.equal(context.toleranceRequests.pending, null);
});

test('initial preparation is immediately cancellable and late response cannot overwrite JSON', async () => {
  const { context, $, target, saves } = await ui();
  context.deepTolerance.target = null;
  let finish;
  context.api = () => new Promise(resolve => { finish = resolve; });
  const operation = context.runDeepManeuver(2);
  assert.equal($('deep-panel').hidden, false);
  assert.equal($('deep-cancel').hidden, false);
  assert.match($('deep-title').textContent, /Подготовка/);
  context.cancelDeepManeuver();
  finish({ canonicalJson: 'late result', target, batch: { targets: [target] } });
  await operation;
  assert.equal(saves.length, 0); assert.equal(context.deepTolerance.target, null);
  assert.equal(context.toleranceRequests.pending, null);
});


test('pause after a partial checkpoint stops automatic continuation and rejects an in-flight later chunk', async () => {
  const { context, $, target, saves } = await ui();
  context.deepTolerance.target = null;
  context.canCheckTolerance = () => !context.toleranceRequests.pending;
  let requests = 0, release;
  const secondStarted = new Promise(resolve => {
    context.api = async () => {
      requests++;
      const next = structuredClone(target);
      Object.assign(next.maneuver, { complete: false, cursor: String(requests * 256) });
      const response = { canonicalJson: `saved ${requests}`, target: next, batch: { targets: [next] } };
      if (requests === 1) return response;
      resolve();
      return await new Promise(finish => { release = () => finish(response); });
    };
  });
  const operation = context.runDeepManeuver(2);
  await secondStarted;
  assert.equal(saves.length, 1); assert.equal($('deep-cancel').hidden, false);
  context.cancelDeepManeuver();
  assert.match($('deep-status').textContent, /приостановлен/);
  assert.equal($('deep-continue').disabled, false);
  assert.equal(context.deepTolerance.target.maneuver.cursor, '256');
  release();
  await operation;
  assert.equal(requests, 2); assert.equal(saves.length, 1);
  assert.equal(context.deepTolerance.target.maneuver.cursor, '256');
  assert.match($('deep-status').textContent, /приостановлен/);
  assert.equal($('deep-continue').disabled, false);
});
