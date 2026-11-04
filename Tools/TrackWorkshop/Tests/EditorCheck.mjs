// Explicit integration check. Start the normal local Workshop server first:
// node Tools/TrackWorkshop/Tests/EditorCheck.mjs
// Not part of default unit tests; uses actual C# TrackJson via /api/analyze.
import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { createObject, deleteObject, resizeObject } from '../Web/edit-model.mjs';
import { patchNumbers } from '../Web/model.mjs';

const evidence = [];
async function analyze(source, context) {
  const response = await fetch('http://127.0.0.1:4186/api/analyze', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ json: source })
  });
  const result = await response.json();
  assert.equal(response.status, 200, `${context}: ${result.error}`);
  evidence.push({ context, gameValid: result.gameValid, staleReferences: result.staleReferences, obstacles: result.obstacles.length, rewards: result.rewards.length });
  return result;
}
const t = (collection, id) => ({ collection, id });
const document = {
  schemaVersion: 1, physicsVersion: 1, warningMode: 'selected', profilePolicy: 'any',
  track: { id: 'editor-core-check', name: 'Editor check', length: 1000, obstacles: [
    { id: 'base-stone', kind: 'floor', x: 100, width: 100, height: 48 },
    { id: 'base-gate', kind: 'gate', x: 400, width: 16, gapTop: 100, gapBottom: 120, topOffsetX: -20, bottomOffsetX: 80 }
  ], riskZones: [], safeRoute: [20, 40], riskRoute: [30], cues: [{ x: 99, text: 'Keep' }] },
  untouched: 2.9499999999999997, botReferences: []
};
let source = JSON.stringify(document), analysis = await analyze(source, 'base');
for (const kind of ['stone-floor', 'stone-ceiling', 'hidden-floor', 'hidden-ceiling', 'gate', 'instant', 'negative', 'coin', 'landing', 'positive-before', 'positive-after', 'finish']) {
  const operation = createObject(source, analysis, kind, 150, 120, 'base-stone');
  const created = await analyze(operation.source, `create:${kind}`);
  assert.equal(created.gameValid, true);
  assert.ok(operation.source.includes('2.9499999999999997'));
  source = operation.source; analysis = created;
}
async function resize(target, handle, x, y, context) {
  const plan = resizeObject(JSON.parse(source), analysis, target, handle, x, y);
  const nextSource = patchNumbers(source, plan.changes), next = await analyze(nextSource, context);
  const shown = (target.collection === 'obstacles' ? plan.analysis.obstacles : plan.analysis.rewards).find(o => o.id === target.id);
  const actual = (target.collection === 'obstacles' ? next.obstacles : next.rewards).find(o => o.id === target.id);
  if (target.collection === 'obstacles') {
    assert.deepEqual(shown.bands, actual.bands, 'preview physical bands match actual Core');
    for (const pad of next.rewards.filter(r => r.kind === 'landing' && r.obstacleId === target.id)) {
      const preview = plan.analysis.rewards.find(r => r.id === pad.id);
      assert.equal(preview.planeY, pad.planeY); assert.equal(preview.left, pad.left); assert.equal(preview.right, pad.right);
    }
  } else if (actual.shape === 'landing') {
    assert.equal(shown.left, actual.left); assert.equal(shown.right, actual.right);
  } else assert.equal(shown.radius, actual.radius);
  source = nextSource; analysis = next;
}
await resize(t('obstacles', 'base-stone'), 'corner', 110, 80, 'stone:shrink+height+landing-coupling');
await resize(t('obstacles', 'base-stone'), 'right', 9999, 0, 'stone:expand-right-bound+clearX');
await resize(t('obstacles', 'base-gate'), 'right', 9999, 0, 'gate:width-offset-bound');
await resize(t('obstacles', 'base-gate'), 'gap-top', 0, -999, 'gate:top-minimum');
await resize(t('obstacles', 'base-gate'), 'gap-bottom', 0, 999, 'gate:bottom-maximum');
const landing = analysis.rewards.find(r => r.kind === 'landing');
await resize(t('rewards', landing.id), 'left', -999, 0, 'landing:left-bound');
await resize(t('rewards', landing.id), 'right', 99999, 0, 'landing:right-bound');
const coin = analysis.rewards.find(r => r.kind === 'coin');
await resize(t('rewards', coin.id), 'radius', coin.x, coin.y, 'circle:radius-minimum');
await resize(t('rewards', coin.id), 'radius', 99999, 99999, 'circle:radius-maximum');
const deletion = deleteObject(source, analysis, t('obstacles', 'base-stone'));
assert.equal(deletion.removedIds.length, 4, 'stone + landing + both positives');
source = deletion.source; analysis = await analyze(source, 'delete:cascade-all-linked');
assert.ok(!analysis.rewards.some(r => r.obstacleId === 'base-stone'));

const authored = structuredClone(document); authored.warningMode = 'authored'; authored.track.obstacles = [
  { id: 'last-hidden', kind: 'floor', x: 100, width: 64, height: 48, hidden: true, hiddenWarningDuration: .35 }
];
let authoredSource = JSON.stringify(authored), authoredAnalysis = await analyze(authoredSource, 'authored:base');
const addHidden = createObject(authoredSource, authoredAnalysis, 'hidden-ceiling', 300, 20);
await analyze(addHidden.source, 'authored:add-hidden-default-timer');
const noHidden = deleteObject(authoredSource, authoredAnalysis, t('obstacles', 'last-hidden'));
const empty = await analyze(noHidden.source, 'authored:delete-last-hidden-selected-empty');
assert.equal(empty.warningMode, 'selected'); assert.equal(empty.obstacles.length, 0);

const builtInResponse = await fetch('http://127.0.0.1:4186/api/catalog/full-long');
const builtIn = (await builtInResponse.json()).json, builtInAnalysis = await analyze(builtIn, 'references:base');
const withCoin = createObject(builtIn, builtInAnalysis, 'coin', 450, 100);
const withCoinAnalysis = await analyze(withCoin.source, 'references:edit-stales-without-deleting-raw');
assert.equal(withCoinAnalysis.staleReferences, true);
assert.deepEqual(JSON.parse(withCoin.source).botReferences, JSON.parse(builtIn).botReferences);
const cleaned = await analyze(withCoinAnalysis.cleanedJson, 'references:explicit-clean-export'); assert.equal(cleaned.gameValid, true);

const result = { passed: true, method: 'actual local C# /api/analyze, bounded editor operations, no route searches', requests: evidence.length, evidence };
await writeFile(fileURLToPath(new URL('./editor-check-results.json', import.meta.url)), JSON.stringify(result, null, 2) + '\n');
console.log(`PASS EditorCheck: ${evidence.length} production API validations; all palette kinds, resize boundaries, landing coupling, cascade/authored cleanup, stale refs + clean export.`);
