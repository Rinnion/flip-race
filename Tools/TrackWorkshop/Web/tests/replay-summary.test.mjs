import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { canLocateCommand, replaySummary } from '../replay-summary.mjs';
import { batchCoverage } from '../tolerance-batch.mjs';
import { sameRunSettings } from '../run-settings.mjs';
import { settings, maneuver, batchFor, replay } from './deep-fixtures.mjs';

test('one joint maneuver score per original flight, direct equal-weight statistics and raw <7% hard places', () => {
  const result = replay([0, 1, 2, 3, 4, 5, 6]);
  const targets = [maneuver([0, 1, 2], { safe: '6', valid: '100', computed: '100' }), maneuver([3, 4], { safe: '7', valid: '100', computed: '100' }), maneuver([5, 6], { safe: '100', valid: '100', computed: '100' })];
  const report = replaySummary(result, batchFor(result, targets));
  assert.deepEqual(report.maneuvers.map(m => [m.start, m.end]), [[0, 2], [3, 4], [5, 6]]);
  assert.deepEqual(report.scores, { count: 3, minimum: .06, maximum: 1, median: .07 });
  assert.equal(report.hardPlaces.length, 1); assert.equal(report.tolerance.low, 1);
  assert.equal(report.partial, false);
});

test('safe envelope uses backend actual extrema and preserves signed neighbor gaps', () => {
  const result = replay([50, 51, 52, 53]);
  const targets = [maneuver([0], { safeWindow: { earliestMs: 50, latestMs: 1500 } }),
    maneuver([1], { safeWindow: { earliestMs: 1100, latestMs: 2000 } }),
    maneuver([2], { safeWindow: { earliestMs: 2000, latestMs: 2300 } }),
    maneuver([3], { safeWindow: { earliestMs: 2600, latestMs: 2800 } })];
  const report = replaySummary(result, batchFor(result, targets));
  assert.deepEqual(report.maneuvers.map(m => m.gapMs), [null, -400, 0, 300]);
  assert.deepEqual(report.gaps, { count: 3, minimum: -400, maximum: 300, median: 0 });
  assert.equal(report.overlaps.length, 1);
});

test('partial, zero-valid and unsupported do not invent scores or bridge unknown windows', () => {
  const result = replay([0, 1, 2, 3, 4]);
  const targets = [maneuver([0]), maneuver([1], { complete: false }), maneuver([2], { safe: '0', valid: '0', computed: '0' }),
    { kind: 'unsupported', inputIndex: 3 }, maneuver([4], { safe: '0' })];
  const report = replaySummary(result, batchFor(result, targets));
  assert.deepEqual(report.maneuvers.map(m => m.score), [.04, null, null, null, 0]);
  assert.equal(report.scores.count, 2); assert.equal(report.gaps.count, 0);
  assert.equal(report.tolerance.pending, 1); assert.equal(report.tolerance.unsupported, 1);
  assert.equal(report.partial, true);
});

test('old pair matrices and stale context never become whole-flight metrics', () => {
  const result = replay([0, 1, 2]), current = batchFor(result, [maneuver([0, 1, 2])]);
  const stale = [{ ...current, targets: [{ kind: 'pair', inputIndex: 0, firstInputIndex: 0 }] },
    { ...current, axis: 'distance' }, { ...current, warningDuration: 2 }, { ...current, inputs: [0, 1, 3] }];
  for (const batch of [null, ...stale]) { const report = replaySummary(result, batch); assert.equal(report.scores.count, 0); assert.equal(report.tolerance.total, null); }
  assert.equal(canLocateCommand({ status: 'unreached', time: 1, x: 20, y: 10 }), false);
  assert.equal(canLocateCommand({ status: 'accepted', time: 1, x: 20, y: 10 }), true);
});

// Run the actual UI functions to check freshness/cache and navigation lifecycle.
test('app updates matrices and restore, hides stale results, and navigation uses exact target without new analysis', async () => {
  const source = await readFile(new URL('../app.js', import.meta.url), 'utf8');
  const functions = source.slice(source.indexOf('function renderReplaySummary()'), source.indexOf('function restoreBatchReplay()'));
  const reset = source.slice(source.indexOf('function resetBatch()'), source.indexOf('function openBatchTarget('));
  class Element {
    constructor() { this.children = []; this.handlers = {}; this.textContent = ''; }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children; }
    addEventListener(type, handler) { this.handlers[type] = handler; }
    querySelector() { return marker; }
    focus() { this.focused = true; }
    scrollIntoView() {}
  }
  const elements = new Map(), marker = new Element();
  const get = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
  get('json').value = 'source';
  const result = replay([.5, .6, .7, 2, 2.1, 4]), route = { result, stale: false, visible: true };
  let calculations = 0, located = null, opened = null;
  const context = { state: { valid: true, route }, objectEdit: {}, loadingTrack: null, live: null,
    replaySource: { editorSource: 'source' }, replaySummaryView: { revision: 0 }, toleranceBatch: { view: null },
    $: get, document: { createElement: () => new Element() }, number: (value, digits = 0) => Number(value).toFixed(digits),
    sameRunSettings, currentRunSettings: () => settings, storedRunSettings: value => value, runSettingsLabel: () => 'settings',
    replaySummary: (...args) => { calculations++; return replaySummary(...args); }, coverageOf: batchCoverage, canLocateCommand,
    batchCoverageLabel: () => 'partial', canSelectAnalysisTarget: () => !route.stale, batchTargetLabel: target => String(target.inputIndex),
    message: (id, text) => { get(id).textContent = text; }, openBatchTarget: target => { opened = target; },
    stopPlayback: () => {}, dimensions: () => ({ width: 100 }), seek: time => { located = time; } };
  vm.createContext(context); vm.runInContext(functions + reset, context);
  context.renderReplaySummary();
  assert.equal(get('replay-summary-content').hidden, false);
  assert.match(get('replay-summary-coverage').textContent, /ещё не рассчитаны/);
  assert.match(get('replay-summary-metrics').children[0].children[1].children[0].textContent, /нет данных/);
  context.renderReplaySummary(); assert.equal(calculations, 1);
  context.focusSummaryCommand(2); assert.equal(located, .7); assert.equal(marker.focused, true);
  assert.equal(context.replaySummaryView.focusIndex, 2); assert.equal(calculations, 1);

  const saved = batchFor(result, result.inputs.map((_, i) => maneuver([i], { complete: false })));
  context.toleranceBatch.view = saved; context.renderReplaySummary();
  assert.match(get('replay-summary-coverage').textContent, /ожидает 6/);
  saved.targets[0] = maneuver([0], { safe: '0' }); context.replaySummaryView.revision++; context.renderReplaySummary();
  assert.equal(context.replaySummaryView.cache.report.tolerance.low, 1);
  assert.equal(get('replay-summary-maneuver-list').children.length, 6);
  assert.equal(get('replay-summary-place-list').children.length, 1);
  get('replay-summary-place-list').children[0].children[1].children.at(-1).handlers.click();
  assert.equal(opened, saved.targets[0]);
  get('replay-summary-metrics').children[0].children.at(-1).handlers.click();
  assert.equal(opened, saved.targets[0]);

  route.stale = true; context.renderReplaySummary();
  assert.equal(get('replay-summary-content').hidden, true);
  assert.equal(context.replaySummaryView.focusIndex, null);
  route.stale = false; context.replaySource.editorSource = 'other'; context.renderReplaySummary();
  assert.equal(get('replay-summary-content').hidden, true);
  context.replaySource.editorSource = 'source'; route.result = { ...result, warningDuration: .8 }; context.renderReplaySummary();
  assert.equal(get('replay-summary-content').hidden, true);
  route.result = JSON.parse(JSON.stringify(result)); context.toleranceBatch.view = JSON.parse(JSON.stringify(saved)); context.renderReplaySummary();
  assert.equal(get('replay-summary-content').hidden, false);
  assert.equal(context.replaySummaryView.cache.report.tolerance.ready, 1);
  context.resetBatch(); context.renderReplaySummary();
  assert.equal(context.replaySummaryView.cache.report.tolerance.total, null);
  assert.equal(context.replaySummaryView.focusIndex, null);
});
