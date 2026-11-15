import test from 'node:test';
import assert from 'node:assert/strict';
import { isMatchingReplay, RouteResults, restoreRouteInputs, routeSearchTiming } from '../route-analysis.mjs';
import { readDraft, writeDraft } from '../draft.mjs';
import { DEFAULT_SURFACE_RUN_SETTINGS } from '../run-settings.mjs';

const expected = { finishTime: 3.57, collisions: 1, boosts: 2, slows: 1, coins: 0, finishBoosts: 1, inputTicks: [0, 13, 35] };
const chosen = { profile: 'Chosen', stepMs: 100, warningDuration: 0.4, routes: [{ ...expected, finishTime: 4.23 }] };
const fast = { profile: 'FastFlip', stepMs: 100, warningDuration: 0.8, routes: [{ ...expected, finishTime: 5.23 }, expected] };

test('legacy and current routes retain their own grid even when their minimum gaps agree', () => {
  const current = { ...fast, rulesVersion: 2, stepMs: 50, searchParameters: { minimumInputIntervalMs: 100 } };
  assert.deepEqual(routeSearchTiming(fast), { stepMs: 100, minimumIntervalMs: 100 });
  assert.deepEqual(routeSearchTiming(current), { stepMs: 50, minimumIntervalMs: 100 });
  assert.deepEqual(restoreRouteInputs({ run: fast, route: expected }), [0, 1.3, 3.5]);
  assert.deepEqual(restoreRouteInputs({ run: current, route: expected }), [0, 0.65, 1.75]);
});

test('route rows and run summaries show only exact settings, with the fastest finishes first', () => {
  const defaults = DEFAULT_SURFACE_RUN_SETTINGS;
  const current = { ...fast, surfaceRunSettings: defaults };
  const faster = { ...expected, finishTime: 2 };
  const otherRuns = [
    { ...current, profile: chosen.profile },
    { ...current, warningDuration: chosen.warningDuration },
    ...Object.entries({ enabled: false, ratePercent: 4, curve: 'Exponential', capPercent: 40,
      airDecayPercent: 100, resetOnCollision: false }).map(([key, value]) =>
      ({ ...current, surfaceRunSettings: { ...defaults, [key]: value } })),
  ].map(run => ({ ...run, routes: [faster] }));
  const analysis = { version: 2, runs: [fast, ...otherRuns, current] };
  const results = new RouteResults();
  results.update(null, current);
  assert.deepEqual(results.rows, []);
  results.update(analysis, current);
  assert.deepEqual(results.runs, [current]);
  assert.deepEqual(results.rows, [
    { run: current, route: fast.routes[1] },
    { run: current, route: fast.routes[0] },
  ]);
  assert.equal(results.rows[0].route.collisions, 1);
  assert.equal(analysis.runs.length, 10);
  assert.deepEqual(restoreRouteInputs({ run: fast, route: expected }), [0, 1.3, 3.5]);
  assert.deepEqual(restoreRouteInputs({ run: fast, route: { inputTicks: [] } }), []);
});

test('changing settings resets pagination and rejects stale selections without deleting saved routes', () => {
  const legacy = { ...DEFAULT_SURFACE_RUN_SETTINGS, enabled: false };
  const chosenSettings = { profile: chosen.profile, warningDuration: chosen.warningDuration, surfaceRunSettings: legacy };
  const fastSettings = { ...chosenSettings, profile: fast.profile, warningDuration: fast.warningDuration };
  const analysis = { version: 1, runs: [chosen, fast] };
  const results = new RouteResults();
  assert.equal(results.update(analysis, fastSettings), true);
  assert.deepEqual(results.runs, [fast]);
  assert.deepEqual(results.rows, [
    { run: fast, route: fast.routes[1] },
    { run: fast, route: fast.routes[0] },
  ]);
  const selected = results.rows[0];
  results.page = 1;
  results.selected = selected;
  assert.equal(results.update(analysis, { ...fastSettings, surfaceRunSettings: { ...legacy } }), false);
  assert.equal(results.page, 1);
  assert.equal(results.selected, selected);
  assert.equal(results.canSelect(selected, fastSettings), true);
  assert.equal(results.canSelect(selected, chosenSettings), false);
  assert.equal(results.update(analysis, chosenSettings), true);
  assert.deepEqual(results.runs, [chosen]);
  assert.equal(results.page, 0);
  assert.equal(results.selected, null);
  assert.equal(results.canSelect(selected, fastSettings), false);
  results.update(analysis, { ...fastSettings, surfaceRunSettings: { ...legacy, ratePercent: 4 } });
  assert.deepEqual(results.runs, []);
  assert.deepEqual(results.rows, []);
  results.update(analysis, fastSettings);
  assert.deepEqual(results.runs, [fast]);
  assert.equal(results.rows.length, 2);
  assert.equal(results.selected, null);
});

test('route selection displays a replay only when finish, accepted inputs and outcome agree', () => {
  const { inputTicks, ...outcome } = expected;
  const summary = { ...outcome, status: 'Finished', acceptedInputs: inputTicks.length, ignoredInputs: 0, unreachedInputs: 0 };
  const entry = { run: fast, route: expected };
  const result = { profile: fast.profile, warningDuration: fast.warningDuration, axis: 'time', summary,
    surfaceRunSettings: { ...DEFAULT_SURFACE_RUN_SETTINGS, enabled: false, unknown: 'extension' } };
  assert.equal(isMatchingReplay(result, entry), true);
  for (const mismatch of [
    { status: 'TimedOut' }, { finishTime: null }, { finishTime: 3.56 }, { collisions: 0 }, { slows: 0 },
    { ignoredInputs: 1 }, { unreachedInputs: 1 }, { acceptedInputs: 2 }, { boosts: 1 }, { coins: 1 }, { finishBoosts: 0 },
  ]) assert.equal(isMatchingReplay({ ...result, summary: { ...summary, ...mismatch } }, entry), false);
  for (const mismatch of [
    { profile: 'Chosen' }, { warningDuration: 1.2 }, { axis: 'distance' }, { surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS },
  ]) assert.equal(isMatchingReplay({ ...result, ...mismatch }, entry), false);
  const boosted = { ...DEFAULT_SURFACE_RUN_SETTINGS, curve: 'Exponential', capPercent: 40 };
  const boostedEntry = { ...entry, run: { ...fast, surfaceRunSettings: { ...boosted, unknown: 'author extension' } } };
  assert.equal(isMatchingReplay({ ...result, surfaceRunSettings: boosted }, boostedEntry), true);
  assert.equal(isMatchingReplay({ ...result, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS }, boostedEntry), false);
});

test('draft retains exact developer analysis and manual inputs and settings', () => {
  let saved = null;
  const storage = { getItem: () => saved, setItem: (_key, value) => { saved = value; } };
  const json = ` { "unknownExtension": 1e2, "workshopAnalysis": ${JSON.stringify({ version: 1, runs: [chosen, fast] })} }\n`;
  const input = { json, inputs: '[12, 134]', axis: 'distance', filename: 'track.json', profile: 'Chosen', warning: '0.6' };
  assert.equal(writeDraft(storage, input).kind, 'saved');
  const restored = readDraft(storage);
  assert.equal(restored.kind, 'saved');
  for (const key of Object.keys(input)) assert.equal(restored.draft[key], input[key]);
  assert.equal(JSON.parse(restored.draft.json).workshopAnalysis.runs.flatMap(run => run.routes).length, 3);
});
