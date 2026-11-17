import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { canLocateCommand, replaySummary } from '../replay-summary.mjs';
import { batchCoverage, toleranceScore } from '../tolerance-batch.mjs';
import { DEFAULT_SURFACE_RUN_SETTINGS, sameRunSettings } from '../run-settings.mjs';

const settings = { profile: 'Chosen', warningDuration: .6, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS };
const replay = (times, axis = 'time') => ({ ...settings, axis,
  inputs: times.map((time, index) => ({ index, value: axis === 'time' ? time : index * 10, time, x: index * 10, y: 20, status: 'accepted' })) });
const pair = (index, safe = 0, earliest = index, latest = index + .5) => ({ kind: 'pair', inputIndex: index, firstInputIndex: index,
  pair: { status: 'ready', spanMs: 600, cells: Array.from({ length: 625 }, (_, i) => ({ computed: true,
    status: i < safe ? 'safe' : 'invalid', firstInputTime: earliest, secondInputTime: latest })) } });
const single = (index, safe = 0, earliest = index, latest = index + .5) => ({ kind: 'single', inputIndex: index,
  single: { status: 'ready', spanMs: 600, probes: Array.from({ length: 25 }, (_, i) => ({ computed: true, omit: false,
    status: i < safe ? 'safe' : 'collision', inputTime: i === 0 ? earliest : latest })),
    omission: { computed: true, omit: true, status: 'safe', inputTime: -100 } } });
const batch = (result, targets, indices = result.inputs.map((_, i) => targets.findIndex(target =>
  target.inputIndex === i || target.kind === 'pair' && target.firstInputIndex + 1 === i))) => ({ ...settings, spanMs: 600, stepMs: 50, axis: result.axis,
  inputs: result.inputs.map(input => input.value), targets, commandTargetIndices: indices });

test('calibrated whole-matrix scores have equal weight, including even median after calibration', () => {
  const result = replay([0, 1, 2, 3, 4, 5, 6]);
  const targets = [single(0), single(1), pair(2), single(4), single(5), single(6)];
  const fractions = [0, .02, .04, .1, .6, 1];
  const report = replaySummary(result, batch(result, targets), target => ({ state: 'ready', fraction: fractions[targets.indexOf(target)], safe: 1, total: target.kind === 'pair' ? 625 : 25 }));
  assert.equal(report.scores.count, 6);
  assert.equal(report.scores.minimum, 0);
  assert.equal(report.scores.maximum, 1);
  assert.equal(report.scores.median, (toleranceScore(.04) + toleranceScore(.1)) / 2);
  assert.notEqual(report.scores.median, toleranceScore((.04 + .1) / 2));
  assert.equal(toleranceScore(.07), .5);
  assert.equal(toleranceScore(.6), 1);
  assert.equal(report.tolerance.low, 3);
});

test('whole pair remains atomic; duplicate targets removed; raw <7% alone defines distinct hard places', () => {
  const result = replay([0, .01, .02, .03, .04]);
  const first = pair(0, 43), overlap = pair(1, 44), third = pair(2, 0);
  const saved = batch(result, [first, { ...first }, overlap, third, single(4, 25)], [0, 0, 2, 3, 4]);
  const report = replaySummary(result, saved);
  assert.deepEqual([report.tolerance.total, report.tolerance.ready, report.tolerance.low], [4, 4, 2]);
  assert.deepEqual(report.hardPlaces.map(place => [place.start, place.end]), [[0, 1], [2, 3]]);
  assert.deepEqual(report.maneuvers.map(maneuver => maneuver.targetIndex), [0, 2, 3, 4]);
  assert.equal(report.scores.minimum, 0);
  assert.equal(report.maneuvers[1].sharedCommand, true);
  assert.equal(report.maneuvers[2].sharedCommand, true);
  assert.equal(report.maneuvers[3].sharedCommand, false);
  const atBoundary = replaySummary(result, saved, () => ({ state: 'ready', fraction: .07, safe: 1, total: 25 }));
  assert.equal(atBoundary.hardPlaces.length, 0);
});

test('actual safe press extrema determine windows, signed gaps and overlap median in milliseconds', () => {
  const result = replay([50, 51, 52, 53, 54]);
  const first = pair(0, 3, .1, 1.2);
  Object.assign(first.pair.cells[1], { firstInputTime: .2, secondInputTime: 1.5, startTime: -9, endTime: 90 });
  Object.assign(first.pair.cells[2], { firstInputTime: .05, secondInputTime: 1.3 });
  const targets = [first, pair(1, 2, 1.1, 2), single(3, 2, 2, 2.3), single(4, 2, 2.6, 2.8)];
  const report = replaySummary(result, batch(result, targets, [0, 0, 1, 2, 3]));
  assert.deepEqual(report.maneuvers[0].window, { earliestMs: 50, latestMs: 1500 });
  assert.deepEqual(report.maneuvers.map(maneuver => maneuver.gapMs), [null, -400, 0, 300]);
  assert.deepEqual(report.gaps, { count: 3, minimum: -400, maximum: 300, median: 0 });
  assert.equal(report.overlaps.length, 1);
  assert.equal(report.overlaps[0].sharedCommand, true);
  assert.equal(report.knownWindows, 4);
  assert.deepEqual(report.maneuvers[2].window, { earliestMs: 2000, latestMs: 2300 });
});

test('unknown intermediate matrices are never bridged; zero-safe gives score zero and unknown window', () => {
  const result = replay([0, 1, 2, 3, 4, 5, 6, 7]);
  const pending = single(1, 25); pending.single.probes[0].computed = false;
  const targets = [single(0, 25), pending, single(2, 25), { kind: 'unsupported', inputIndex: 3 }, single(4, 25), single(5), single(6, 25), single(7, 25)];
  const report = replaySummary(result, batch(result, targets));
  assert.deepEqual([report.tolerance.ready, report.tolerance.pending, report.tolerance.unsupported], [6, 1, 1]);
  assert.equal(report.maneuvers[1].score, null);
  assert.equal(report.maneuvers[3].score, null);
  assert.equal(report.maneuvers[5].score, 0);
  assert.equal(report.maneuvers[5].window, null);
  assert.deepEqual(report.maneuvers.map(maneuver => maneuver.gapMs), [null, null, null, null, null, null, null, 500]);
  assert.equal(report.gaps.count, 1);
  assert.equal(report.unknownGaps, 6);
  assert.equal(report.partial, true);
});

test('missing any safe actual time leaves window unknown without baseline or flight-time substitution', () => {
  const result = replay([0, 1, 2]);
  const first = single(0, 2), second = pair(1, 2);
  first.single.baseline = { inputTime: .5 }; first.single.probes[1].inputTime = null;
  second.pair.baseline = { firstInputTime: 1, secondInputTime: 2 };
  delete second.pair.cells[1].secondInputTime;
  for (const cell of second.pair.cells) Object.assign(cell, { startTime: 1, endTime: 2 });
  const report = replaySummary(result, batch(result, [first, second]));
  assert.equal(report.tolerance.ready, 2);
  assert.equal(report.scores.count, 2);
  assert.deepEqual(report.maneuvers.map(maneuver => maneuver.window), [null, null]);
  assert.equal(report.gaps.count, 0);
});

test('legacy saved batches retain actual times, single computed inference and omission exclusion', () => {
  const result = replay([0, 1]);
  const targets = [single(0, 2, .2, .8), single(1, 25, 1.2, 1.8)];
  for (const target of targets) for (const probe of target.single.probes) delete probe.computed;
  const report = replaySummary(result, JSON.parse(JSON.stringify(batch(result, targets))));
  assert.equal(report.tolerance.ready, 2);
  assert.equal(report.maneuvers[0].fraction, 2 / 25);
  assert.deepEqual(report.maneuvers[0].window, { earliestMs: 200, latestMs: 800 });
  assert.equal(report.maneuvers[1].gapMs, 400);
});

test('baseline tempo does not affect score or hard places; stale batch is ignored', () => {
  const slow = replay([0, 1, 2]), fast = replay([0, .01, .02]);
  const targets = [single(0, 25), single(1, 25), single(2, 25)];
  const before = replaySummary(slow, batch(slow, targets)), after = replaySummary(fast, batch(fast, targets));
  assert.deepEqual(before.scores, after.scores);
  assert.deepEqual(before.hardPlaces, after.hardPlaces);
  assert.equal(after.hardPlaces.length, 0);
  for (const changed of [{ ...slow, axis: 'distance' }, { ...slow, warningDuration: 2 }, replay([0, 1, 3])]) {
    const stale = replaySummary(changed, batch(slow, targets));
    assert.equal(stale.tolerance.total, null);
    assert.equal(stale.scores.count, 0);
    assert.equal(stale.scores.minimum, null);
    assert.equal(stale.hardPlaces.length, 0);
  }
});

test('no batch, empty and one-command replays invent no scores, windows or gaps', () => {
  for (const result of [replay([]), replay([1]), replay([0, .01, .02])]) {
    const report = replaySummary(result);
    assert.equal(report.tolerance.total, null);
    assert.equal(report.scores.minimum, null);
    assert.equal(report.gaps.minimum, null);
    assert.deepEqual(report.maneuvers, []);
    assert.deepEqual(report.hardPlaces, []);
  }
  const input = { status: 'unreached', time: 1, x: 20, y: 10 };
  assert.equal(canLocateCommand(input), false);
  assert.equal(canLocateCommand({ ...input, status: 'accepted' }), true);
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
    canSelectAnalysisTarget: () => !route.stale, batchTargetLabel: target => String(target.inputIndex),
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

  const saved = batch(result, result.inputs.map((_, i) => ({ kind: 'single', inputIndex: i })));
  context.toleranceBatch.view = saved; context.renderReplaySummary();
  assert.match(get('replay-summary-coverage').textContent, /ожидает 6/);
  saved.targets[0] = single(0); context.replaySummaryView.revision++; context.renderReplaySummary();
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
