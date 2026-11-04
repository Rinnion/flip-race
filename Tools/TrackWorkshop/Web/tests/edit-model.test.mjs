import test from 'node:test';
import assert from 'node:assert/strict';
import { createObject, deleteObject, resizeObject, getResizeHandles } from '../edit-model.mjs';
import { patchNumbers, scanJson } from '../model.mjs';

// Shape fixture only: production validation is covered by the explicit HTTP
// EditorCheck integration, rather than duplicated in this client model.
function analyze(document) {
  const track = document.track;
  const obstacles = track.obstacles.map(o => ({ height: 0, gapTop: 0, gapBottom: 0, topOffsetX: 0, bottomOffsetX: 0, ...o,
    bands: o.kind === 'gate' ? [
      { index: 0, x: o.x + (o.topOffsetX || 0), y: 0, width: o.width, height: o.gapTop },
      { index: 1, x: o.x + (o.bottomOffsetX || 0), y: o.gapBottom, width: o.width, height: 240 - o.gapBottom }
    ] : [{ index: 0, x: o.x, y: o.kind === 'floor' ? 240 - o.height : 0, width: o.width, height: o.height }] }));
  const rewards = (track.riskZones || []).map(z => {
    const stone = obstacles.find(o => o.id === z.obstacleId);
    const landing = z.kind === 'landing';
    return { radius: 6, clearX: 0, ...z, shape: landing ? 'landing' : z.kind === 'finish' ? 'finish' : 'circle',
      left: landing ? Math.max(stone.x, z.x - z.landingWidth / 2) : null,
      right: landing ? Math.min(stone.x + stone.width, z.x + z.landingWidth / 2) : null,
      planeY: landing ? stone.kind === 'floor' ? 240 - stone.height : stone.height : null,
      segments: [], strokeWidth: 3 };
  });
  return { length: track.length, corridorHeight: 240, obstacles, rewards };
}
function fixture() {
  return { schemaVersion: 1, physicsVersion: 1, warningMode: 'selected', track: { id: 'edit', name: 'Edit', length: 1000,
    obstacles: [
      { id: 'stone-1', kind: 'floor', x: 100, width: 100, height: 48 },
      { id: 'gate-1', kind: 'gate', x: 400, width: 10, gapTop: 100, gapBottom: 120, topOffsetX: -20, bottomOffsetX: 80 },
      { id: 'ceiling', kind: 'ceiling', x: 700, width: 64, height: 48 }
    ], riskZones: [
      { id: 'landing-1', kind: 'landing', obstacleId: 'stone-1', x: 180, y: 192, landingWidth: 40 },
      { id: 'positive-1', kind: 'positive', obstacleId: 'stone-1', x: 220, y: 100, radius: 8, clearX: 230, safeFlipX: 0 },
      { id: 'instant-1', kind: 'instant', obstacleId: 'stone-1', x: 50, y: 100, radius: 6 },
      { id: 'coin-1', kind: 'coin', x: 600, y: 100, radius: 8 }
    ], safeRoute: [10, 20], riskRoute: [30], cues: [{ x: 99, text: 'Keep' }] },
    unknown: { exact: 1 }, botReferences: [{ trackHash: 'stale', inputs: [3, 4], rewardIds: ['landing-1'] }] };
}
const target = (collection, id) => ({ collection, id });
function resized(doc, t, handle, x, y) {
  const plan = resizeObject(doc, analyze(doc), t, handle, x, y);
  return { plan, document: JSON.parse(patchNumbers(JSON.stringify(doc), plan.changes)) };
}

test('source scanner retains tokens and rejects duplicates; optional numeric insertion is explicit', () => {
  const source = '{ "track":{"riskZones":[{"x":1e2,"extra":2.9499999999999997}]},"refs":[1e-9] }';
  const spans = scanJson(source), x = spans.get(JSON.stringify(['track', 'riskZones', 0, 'x']));
  assert.equal(source.slice(x.start, x.end), '1e2');
  const result = patchNumbers(source, [{ path: ['track', 'riskZones', 0, 'radius'], value: 8, insert: true }]);
  assert.ok(result.includes('"x":1e2,"extra":2.9499999999999997')); assert.ok(result.endsWith('"refs":[1e-9] }'));
  assert.equal(JSON.parse(result).track.riskZones[0].radius, 8);
  assert.throws(() => patchNumbers(source, [{ path: ['missing'], value: 3 }]));
  assert.throws(() => scanJson('{"x":1,"x":2}'));
});

test('create every palette kind with unique IDs and existing links, preserve untouched raw source', () => {
  const doc = fixture(), source = JSON.stringify(doc).replace('"exact":1', '"exact":2.9499999999999997');
  const a = analyze(doc);
  for (const kind of ['stone-floor', 'stone-ceiling', 'hidden-floor', 'hidden-ceiling', 'gate', 'instant', 'negative', 'coin', 'landing', 'positive-before', 'positive-after', 'finish']) {
    const result = createObject(source, a, kind, 150, 120, 'stone-1'), edited = JSON.parse(result.source);
    assert.equal(result.target.collection, ['stone-floor', 'stone-ceiling', 'hidden-floor', 'hidden-ceiling', 'gate'].includes(kind) ? 'obstacles' : 'rewards');
    const all = [...edited.track.obstacles, ...edited.track.riskZones];
    assert.equal(new Set(all.map(o => o.id)).size, all.length);
    assert.ok(result.source.includes('"exact":2.9499999999999997'));
    assert.deepEqual(edited.botReferences, doc.botReferences); assert.deepEqual(edited.track.safeRoute, doc.track.safeRoute);
    assert.deepEqual(edited.track.cues, doc.track.cues);
  }
});

test('create into empty and absent optional arrays, then delete back to empty', () => {
  const doc = fixture(); doc.track.obstacles = []; delete doc.track.riskZones;
  let source = JSON.stringify(doc), a = analyze(doc);
  let result = createObject(source, a, 'stone-floor', -999, 900); source = result.source;
  assert.equal(JSON.parse(source).track.obstacles[0].x, 0);
  let parsed = JSON.parse(source); a = analyze(parsed);
  const reward = createObject(source, a, 'coin', 9999, -999); source = reward.source;
  parsed = JSON.parse(source); assert.equal(parsed.track.riskZones.length, 1); assert.equal(parsed.track.riskZones[0].x, 992); assert.equal(parsed.track.riskZones[0].y, 8);
  const noReward = deleteObject(source, analyze(parsed), reward.target); parsed = JSON.parse(noReward.source); assert.deepEqual(parsed.track.riskZones, []);
  const empty = deleteObject(noReward.source, analyze(parsed), result.target); assert.deepEqual(JSON.parse(empty.source).track.obstacles, []);
});

test('IDs are unique across obstacle and reward collections, not just own type', () => {
  const doc = fixture(); doc.track.riskZones.push({ id: 'stone-2', kind: 'coin', x: 1, y: 1 });
  const result = createObject(JSON.stringify(doc), analyze(doc), 'stone-floor', 300, 120);
  assert.equal(result.target.id, 'stone-3');
});

test('delete first/middle/last array items and preserve all surviving raw numeric lexemes', () => {
  for (const index of [0, 1, 2]) {
    const doc = fixture(); doc.track.riskZones = [];
    const source = JSON.stringify(doc).replace('"x":700', '"x":7e2').replace('"exact":1', '"exact":2.9499999999999997');
    const result = deleteObject(source, analyze(doc), target('obstacles', doc.track.obstacles[index].id));
    const parsed = JSON.parse(result.source);
    assert.equal(parsed.track.obstacles.length, 2); assert.ok(!parsed.track.obstacles.some(o => o.id === doc.track.obstacles[index].id));
    assert.ok(result.source.includes('"exact":2.9499999999999997'));
    if (index !== 2) assert.ok(result.source.includes('"x":7e2'));
    assert.deepEqual(parsed.botReferences, doc.botReferences);
  }
});

test('obstacle deletion cascades every linked reward kind, unrelated rewards/routes/refs survive', () => {
  const doc = fixture(), result = deleteObject(JSON.stringify(doc), analyze(doc), target('obstacles', 'stone-1')), parsed = JSON.parse(result.source);
  assert.deepEqual(result.removedIds, ['stone-1', 'landing-1', 'positive-1', 'instant-1']);
  assert.deepEqual(parsed.track.riskZones.map(r => r.id), ['coin-1']); assert.deepEqual(parsed.botReferences, doc.botReferences);
  assert.deepEqual(parsed.track.riskRoute, doc.track.riskRoute);
});

test('authored hidden additions have explicit timer; deleting final hidden changes mode atomically and visibly', () => {
  const doc = fixture(); doc.warningMode = 'authored'; doc.track.obstacles[0].hidden = true; doc.track.obstacles[0].hiddenWarningDuration = .35;
  const source = JSON.stringify(doc);
  const added = createObject(source, analyze(doc), 'hidden-ceiling', 300, 100);
  const newHidden = JSON.parse(added.source).track.obstacles.at(-1); assert.equal(newHidden.hiddenWarningDuration, .8);
  const deleted = deleteObject(source, analyze(doc), target('obstacles', 'stone-1'));
  assert.equal(JSON.parse(deleted.source).warningMode, 'selected'); assert.match(deleted.description, /authored.*selected/);
  const notLast = deleteObject(added.source, analyze(JSON.parse(added.source)), target('obstacles', 'stone-1'));
  assert.equal(JSON.parse(notLast.source).warningMode, 'authored');
});

test('linked creations require obstacle, landing rejects gate, finish remains unique/fixed', () => {
  const doc = fixture(), source = JSON.stringify(doc), a = analyze(doc);
  assert.throws(() => createObject(source, a, 'landing', 0, 0));
  assert.throws(() => createObject(source, a, 'landing', 0, 0, 'gate-1'));
  assert.throws(() => createObject(source, a, 'positive-before', 0, 0, 'missing'));
  const added = createObject(source, a, 'finish', 9999, -20), parsed = JSON.parse(added.source), finish = parsed.track.riskZones.at(-1);
  assert.equal(finish.x, 988); assert.equal(finish.y, 10); assert.deepEqual(getResizeHandles(analyze(parsed), added.target), []);
  assert.throws(() => createObject(added.source, analyze(parsed), 'finish', 100, 120));
  assert.throws(() => resizeObject(parsed, analyze(parsed), added.target, 'right', 100, 100));
});

test('stone corner resize keeps wall, clamps landing center/width and follows actual plane', () => {
  const doc = fixture(), { plan, document: edited } = resized(doc, target('obstacles', 'stone-1'), 'corner', 110, 80);
  assert.equal(edited.track.obstacles[0].width, 10); assert.equal(edited.track.obstacles[0].height, 160);
  const pad = edited.track.riskZones[0], shown = plan.analysis.rewards[0];
  assert.equal(pad.x, 105); assert.equal(pad.landingWidth, 10); assert.equal(pad.y, 192);
  assert.equal(shown.left, 100); assert.equal(shown.right, 110); assert.equal(shown.planeY, 80);
  assert.equal(edited.track.riskZones[1].clearX, 140); assert.equal(edited.track.riskZones[1].safeFlipX, 0);
  assert.deepEqual(edited.botReferences, doc.botReferences);
  assert.equal(doc.track.obstacles[0].width, 100);
});

test('ceiling inner resize remains attached to ceiling with height bounds 1..240', () => {
  const doc = fixture();
  const small = resized(doc, target('obstacles', 'ceiling'), 'inner', 0, -100);
  assert.equal(small.document.track.obstacles[2].height, 1); assert.equal(small.plan.analysis.obstacles[2].bands[0].y, 0);
  assert.equal(resized(doc, target('obstacles', 'ceiling'), 'inner', 0, 999).document.track.obstacles[2].height, 240);
});

test('height-only resize preserves authored clipped landing X/width while updating physical plane', () => {
  const doc = fixture(); doc.track.riskZones[0].landingWidth = 200; doc.track.riskZones[0].x = 190;
  const { plan, document: edited } = resized(doc, target('obstacles', 'stone-1'), 'inner', 150, 80);
  assert.equal(edited.track.riskZones[0].x, 190); assert.equal(edited.track.riskZones[0].landingWidth, 200);
  assert.equal(plan.analysis.rewards[0].left, 100); assert.equal(plan.analysis.rewards[0].right, 200);
  assert.equal(plan.analysis.rewards[0].planeY, 80);
});

test('stone width uses group clearX bounds, sentinel zero stays zero', () => {
  const doc = fixture(); doc.track.riskZones[1].clearX = 999;
  assert.equal(resized(doc, target('obstacles', 'stone-1'), 'right', 99999, 0).document.track.obstacles[0].width, 101);
  doc.track.riskZones[1].clearX = 1;
  assert.equal(resized(doc, target('obstacles', 'stone-1'), 'right', -999, 0).document.track.obstacles[0].width, 99);
  doc.track.riskZones[1].clearX = 0;
  const expanded = resized(doc, target('obstacles', 'stone-1'), 'right', 9999, 0).document;
  assert.equal(expanded.track.obstacles[0].width, 900); assert.equal(expanded.track.riskZones[1].clearX, 0);
});

test('offset gate width respects both bands and independent opening resize keeps parts and small gaps', () => {
  const doc = fixture(), t = target('obstacles', 'gate-1');
  const wide = resized(doc, t, 'right', 99999, 0), gate = wide.document.track.obstacles[1];
  assert.equal(gate.width, 520); assert.equal(gate.topOffsetX, -20); assert.equal(gate.bottomOffsetX, 80);
  assert.equal(Math.max(...wide.plan.analysis.obstacles[1].bands.map(b => b.x + b.width)), 1000);
  const top = resized(doc, t, 'gap-top', 0, 999).document.track.obstacles[1]; assert.equal(top.gapTop, 119); assert.equal(top.gapBottom, 120);
  const bottom = resized(doc, t, 'gap-bottom', 0, -999).document.track.obstacles[1]; assert.equal(bottom.gapTop, 100); assert.equal(bottom.gapBottom, 101);
  const tiny = resized(doc, t, 'right', -9999, 0).document.track.obstacles[1]; assert.equal(tiny.width, 1);
});

test('landing width handles keep full positive window on stone without changing raw Y', () => {
  const doc = fixture(), t = target('rewards', 'landing-1');
  const left = resized(doc, t, 'left', -999, 0).document.track.riskZones[0]; assert.equal(left.x, 150); assert.equal(left.landingWidth, 100); assert.equal(left.y, 192);
  const right = resized(doc, t, 'right', -999, 999).document.track.riskZones[0]; assert.equal(right.landingWidth, 1);
  assert.equal(right.x - right.landingWidth / 2, 160);
});

test('radius resize clamps actual circles inside corridor, supports omitted default radius without manual JSON', () => {
  const doc = fixture(), t = target('rewards', 'coin-1');
  assert.equal(resized(doc, t, 'radius', 99999, 99999).document.track.riskZones[3].radius, 100);
  assert.equal(resized(doc, t, 'radius', 600, 100).document.track.riskZones[3].radius, 1);
  delete doc.track.riskZones[3].radius;
  assert.equal(resized(doc, t, 'radius', 620, 100).document.track.riskZones[3].radius, 20);
});

test('all operations reject duplicate keys and repeated resize preview never changes source snapshot', () => {
  const doc = fixture(), source = JSON.stringify(doc), a = analyze(doc), t = target('obstacles', 'stone-1');
  assert.throws(() => createObject(source.replace('"length":1000', '"length":1000,"length":1000'), a, 'coin', 1, 1));
  assert.throws(() => deleteObject(source.replace('"length":1000', '"length":1000,"length":1000'), a, t));
  const first = resizeObject(doc, a, t, 'right', 220, 0), second = resizeObject(doc, a, t, 'right', 240, 0);
  assert.equal(first.analysis.obstacles[0].width, 120); assert.equal(second.analysis.obstacles[0].width, 140);
  assert.equal(JSON.stringify(doc), source); assert.equal(a.obstacles[0].width, 100);
});
