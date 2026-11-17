import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { canLocateCommand, replaySummary } from '../replay-summary.mjs';
import { batchCoverage } from '../tolerance-batch.mjs';
import { DEFAULT_SURFACE_RUN_SETTINGS, sameRunSettings } from '../run-settings.mjs';

const settings = { profile: 'Chosen', warningDuration: .6, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS };
const replay = (times, axis = 'time') => ({ ...settings, axis,
  inputs: times.map((time, index) => ({ index, value: axis === 'time' ? time : index * 10, time, x: index * 10, y: 20, status: 'accepted' })) });
const pair = (index, safe = 0) => ({ kind: 'pair', inputIndex: index, firstInputIndex: index,
  pair: { status: 'ready', spanMs: 600, cells: Array.from({ length: 625 }, (_, i) => ({ computed: true, status: i < safe ? 'safe' : 'invalid' })) } });
const single = (index, safe = 0) => ({ kind: 'single', inputIndex: index,
  single: { status: 'ready', spanMs: 600, probes: Array.from({ length: 25 }, (_, i) => ({ computed: true, status: i < safe ? 'safe' : 'collision' })) } });
const batch = (result, targets, indices) => ({ ...settings, spanMs: 600, stepMs: 50, axis: result.axis,
  inputs: result.inputs.map(input => input.value), targets, commandTargetIndices: indices });

test('summary preserves 150/250 ms boundaries, critical episodes and tempo-only partial state', () => {
  const report = replaySummary(replay([0, .1, .2, .35, .6, .8501, .95, 1.05]));
  assert.deepEqual(report.tempo.counts, { critical: 4, normal: 2, easy: 1, unknown: 0 });
  assert.equal(report.criticalSeries, 2);
  assert.equal(report.tempo.longestCritical, 2);
  assert.equal(report.minimumIntervalMs, 99.9);
  assert.equal(report.accepted, 8);
  assert.deepEqual(report.groups.map(group => [group.start, group.end, group.commandCount]), [[0, 2, 3], [5, 7, 3]]);
  assert.equal(report.groups[1].gapMs, 650.1);
  assert.equal(report.groups[1].gapSource, 'actual');
  assert.equal(report.partial, true);
  assert.equal(report.tolerance.total, null);
  assert.equal(report.tolerance.minimum, null);
  assert.equal(report.coveredCommands, 6);
});

test('unique targets count once, overlapping pairs remain distinct and shared spans merge transitively', () => {
  const result = replay([0, .1, .5, .8, 1.1]);
  const first = pair(0), overlapping = pair(1), third = pair(2);
  const saved = batch(result, [first, { ...first }, overlapping, third, single(4, 20)], [0, 0, 2, 3, 4]);
  const report = replaySummary(result, saved);
  assert.deepEqual([report.tolerance.total, report.tolerance.ready, report.tolerance.low], [4, 4, 3]);
  assert.deepEqual(report.groups.map(group => [group.start, group.end, group.tempoEdges, group.targetIndices]), [[0, 3, [1], [0, 2, 3]]]);
  assert.equal(report.coveredCommands, 4);
  assert.equal(report.longestPlace, 4);
  assert.equal(report.partial, false);
  assert.equal(report.tolerance.minimum.total, 625);
});

test('adjacent disjoint low single spans stay separate and 7% exactly is moderate', () => {
  const result = replay([0, 1, 2]);
  const saved = batch(result, [single(0), single(1), single(2)], [0, 1, 2]);
  const report = replaySummary(result, saved, target => ({ state: 'ready', safe: 1, total: 25,
    fraction: target.inputIndex === 2 ? .07 : .069999 }));
  assert.equal(report.tolerance.low, 2);
  assert.deepEqual(report.groups.map(group => [group.start, group.end]), [[0, 0], [1, 1]]);
  assert.equal(report.groups[1].gapMs, 1000);
});

test('pending and unsupported are unknown, not low; all-cell denominators and saved-batch matching survive restore', () => {
  const result = replay([0, 1, 2, 3]);
  const pending = single(1); pending.single.probes[0].computed = false;
  const saved = batch(result, [single(0, 1), pending, { kind: 'unsupported', inputIndex: 2 }, single(3, 25)], [0, 1, 2, 3]);
  const restored = JSON.parse(JSON.stringify(saved));
  const report = replaySummary(result, restored);
  assert.deepEqual([report.tolerance.total, report.tolerance.ready, report.tolerance.pending, report.tolerance.unsupported, report.tolerance.low], [4, 2, 1, 1, 1]);
  assert.deepEqual(report.tolerance.minimum, { targetIndex: 0, fraction: 1 / 25, score: .5 * (1 / 25) / .07, safe: 1, total: 25 });
  assert.equal(report.partial, true);
  assert.equal(report.groups.length, 1);
  for (const changed of [{ ...result, axis: 'distance' }, { ...result, warningDuration: 2 }, replay([0, 1, 2, 4])])
    assert.equal(replaySummary(changed, restored).tolerance.total, null);
});

test('missing resolved time breaks distance streaks and gaps; unreached scheduled time never gains a map point', () => {
  const distance = replay([0, .1, null, .8, .9], 'distance');
  distance.inputs[2].status = 'unreached';
  const unknown = replaySummary(distance);
  assert.equal(unknown.tempo.counts.unknown, 2);
  assert.equal(unknown.groups[1].gapMs, 700);
  distance.inputs[3].time = null;
  assert.equal(replaySummary(distance).groups.length, 1);
  const unknownGap = replaySummary(distance, batch(distance,
    [single(0, 25), single(1, 25), single(2), single(3, 25), single(4, 25)], [0, 1, 2, 3, 4]));
  assert.equal(unknownGap.groups[1].gapMs, null);
  assert.equal(unknownGap.groups[1].gapSource, null);
  const scheduled = replay([0, .1, 1, 1.1]);
  scheduled.inputs[2] = { ...scheduled.inputs[2], status: 'unreached', time: null, x: null, y: null };
  const report = replaySummary(scheduled);
  assert.equal(report.groups[1].gapMs, 900);
  assert.equal(report.groups[1].gapSource, 'scheduled');
  assert.equal(report.groups[1].unlocated, 1);
  assert.equal(canLocateCommand(scheduled.inputs[2]), false);
  assert.equal(canLocateCommand({ ...scheduled.inputs[2], time: 1, x: 20, y: 10 }), false);
});

test('empty and one-command replays have no invented percentages or places', () => {
  for (const result of [replay([]), replay([1])]) {
    const report = replaySummary(result);
    assert.equal(report.tempo.criticalFraction, null);
    assert.equal(report.minimumIntervalMs, null);
    assert.equal(report.tolerance.minimum, null);
    assert.equal(report.longestPlace, 0);
    assert.equal(report.coveredCommands, 0);
    assert.deepEqual(report.groups, []);
  }
});

// Execute the actual app renderer/navigation functions with a small DOM facade.
// This catches source/settings/reset lifecycle wiring without reimplementing UI logic.
test('app summary refreshes progress and restore, hides stale results, and map navigation never starts analysis', async () => {
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
  const result = replay([.5, .6, .7, 2, 2.1, 4]);
  const route = { result, stale: false, visible: true };
  let calculations = 0, located = null, opened = null;
  const context = { state: { valid: true, route }, objectEdit: {}, loadingTrack: null, live: null,
    replaySource: { editorSource: 'source' }, replaySummaryView: { revision: 0 }, toleranceBatch: { view: null },
    $: get, document: { createElement: () => new Element() }, number: (value, digits = 0) => Number(value).toFixed(digits),
    sameRunSettings, currentRunSettings: () => settings, storedRunSettings: value => value, runSettingsLabel: () => 'settings',
    replaySummary: (...args) => { calculations++; return replaySummary(...args); }, coverageOf: batchCoverage, canLocateCommand,
    canSelectAnalysisTarget: () => !route.stale, batchTargetLabel: target => String(target.inputIndex),
    message: (id, text) => { get(id).textContent = text; }, openBatchTarget: target => { opened = target; },
    stopPlayback: () => {}, dimensions: () => ({ width: 100 }), seek: time => { located = time; } };
  vm.createContext(context); vm.runInContext(functions + reset, context);
  context.renderReplaySummary();
  assert.equal(get('replay-summary-content').hidden, false);
  assert.equal(get('replay-summary-place-list').children.length, 2);
  assert.match(get('replay-summary-coverage').textContent, /ещё не рассчитан/);
  context.renderReplaySummary(); assert.equal(calculations, 1);
  context.focusSummaryCommand(2); assert.equal(located, .7); assert.equal(marker.focused, true);
  assert.equal(context.replaySummaryView.focusIndex, 2); assert.equal(calculations, 1);

  const saved = batch(result, result.inputs.map((_, i) => ({ kind: 'single', inputIndex: i })), [0, 1, 2, 3, 4, 5]);
  context.toleranceBatch.view = saved; context.renderReplaySummary();
  assert.match(get('replay-summary-coverage').textContent, /ожидает 6/);
  saved.targets[0] = single(0); context.replaySummaryView.revision++; context.renderReplaySummary();
  assert.equal(context.replaySummaryView.cache.report.tolerance.low, 1);
  get('replay-summary-metrics').children[2].children.at(-1).handlers.click();
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
