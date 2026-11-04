import test from 'node:test';
import assert from 'node:assert/strict';
import { parseInputs, sampleAt, viewMetrics, rewardAppearance, patchNumbers, translateObject, Revision } from '../model.mjs';

test('input list preserves exact numbers and rejects coercion, sorting and duplicates', () => {
  assert.deepEqual(parseInputs('[0, 0.00001, 430.1811187353192]'), [0, .00001, 430.1811187353192]);
  assert.deepEqual(parseInputs('[]'), []);
  for (const text of ['{}', '["430"]', '[null]', '[-1]', '[1,1]', '[2,1]', '[1e999]', 'NaN', '[1,]']) assert.throws(() => parseInputs(text), text);
  assert.throws(() => parseInputs(JSON.stringify(Array.from({ length: 4097 }, (_, i) => i))));
});

test('detail scale stays equal on wide/narrow viewports and clamps navigation', () => {
  for (const [width, height, zoom] of [[1200, 310, 1], [350, 240, 1], [800, 280, 3]]) {
    const view = viewMetrics(width, height, 240, zoom, 7920, 100000);
    assert.ok(Math.abs(width / view.width - height / view.height) < 1e-12);
    assert.ok(view.left + view.width <= 7950 + 1e-9);
    assert.ok(view.top <= 0 || zoom > 1);
  }
  assert.equal(viewMetrics(800, 280, 240, 1, 7920, -100).left, -20);
});

test('display interpolation handles edges and irregular input sample spacing', () => {
  const samples = [{ time: 0, x: 0, y: 226 }, { time: .004, x: 1, y: 225 }, { time: 1, x: 220, y: 100 }];
  assert.equal(sampleAt(samples, -1), samples[0]);
  assert.equal(sampleAt(samples, .002).x, .5);
  assert.equal(sampleAt(samples, 1).x, 220);
  assert.equal(sampleAt(samples, 100).x, 220);
  assert.equal(sampleAt([], 0), null);
});

test('an edit invalidates a pending response before next debounce request exists', () => {
  const revisions = new Revision(), pendingResponse = revisions.value;
  revisions.bump();
  assert.equal(revisions.current(pendingResponse), false);
  const newerResponse = revisions.value;
  assert.equal(revisions.current(newerResponse), true);
  revisions.bump();
  assert.equal(revisions.current(newerResponse), false);
});

test('actual Core reward kinds distinguish traps from positive bonuses and name every shape', () => {
  assert.equal(rewardAppearance('negative').trap, true);
  assert.equal(rewardAppearance('negative').label, 'Ловушка');
  assert.equal(rewardAppearance('positive').trap, false);
  assert.equal(rewardAppearance('instant').trap, false);
  assert.equal(rewardAppearance('coin').coin, true);
  for (const kind of ['positive', 'instant', 'negative', 'coin', 'landing', 'finish'])
    assert.notEqual(rewardAppearance(kind).label, kind);
});

function fixture() {
  const document = { track: { length: 1000, obstacles: [
    { id: 'stone', kind: 'floor', x: 100, width: 100, height: 48, hidden: true, hiddenWarningDuration: .35 },
    { id: 'gate', kind: 'gate', x: 400, width: 10, gapTop: 30, gapBottom: 170, topOffsetX: -20, bottomOffsetX: 80 }
  ], riskZones: [
    { id: 'pad', kind: 'landing', obstacleId: 'stone', x: 150, y: 192, landingWidth: 60, clearX: 250, safeFlipX: 0 },
    { id: 'linked', kind: 'positive', obstacleId: 'stone', x: 220, y: 100, radius: 10, clearX: 260, riskFlipX: 80 },
    { id: 'free', kind: 'negative', obstacleId: '', x: 600, y: 100, radius: 20 },
    { id: 'finish', kind: 'finish', x: 900, y: 120, radius: 6 }
  ], safeRoute: [10, 200], riskRoute: [20, 220], cues: [{ x: 100, text: 'Keep' }] }, botReferences: [{ inputs: [100, 200] }] };
  const analysis = { length: 1000, corridorHeight: 240,
    obstacles: document.track.obstacles.map(o => ({ ...o, bands: o.kind === 'gate' ? [
      { index: 0, x: o.x + o.topOffsetX, y: 0, width: o.width, height: o.gapTop },
      { index: 1, x: o.x + o.bottomOffsetX, y: o.gapBottom, width: o.width, height: 240 - o.gapBottom }
    ] : [{ index: 0, x: o.x, y: 240 - o.height, width: o.width, height: o.height }] })),
    rewards: document.track.riskZones.map(r => ({ ...r, shape: r.kind === 'landing' ? 'landing' : r.kind === 'finish' ? 'finish' : 'circle',
      left: r.kind === 'landing' ? 120 : null, right: r.kind === 'landing' ? 180 : null, planeY: r.kind === 'landing' ? 192 : null,
      segments: r.kind === 'finish' ? [{ x1: 889.5, y1: 111.5, x2: 897.9, y2: 120 }, { x1: 897.9, y1: 120, x2: 889.5, y2: 128.5 },
        { x1: 902.1, y1: 111.5, x2: 910.5, y2: 120 }, { x1: 910.5, y1: 120, x2: 902.1, y2: 128.5 }] : [], strokeWidth: 3 })) };
  return { document, analysis };
}

test('stone drag moves linked group and positive triggers by one bounded delta, preserving sentinels/wall', () => {
  const { document, analysis } = fixture();
  const source = JSON.stringify(document), before = structuredClone(analysis);
  const plan = translateObject(document, analysis, { collection: 'obstacles', id: 'stone' }, -500, 90);
  assert.equal(plan.dx, -80); // authored positive riskFlipX is the limiting group member
  assert.equal(plan.dy, 0);
  const edited = JSON.parse(patchNumbers(source, plan.changes));
  assert.equal(edited.track.obstacles[0].x, 20);
  assert.equal(edited.track.obstacles[0].height, 48);
  assert.equal(edited.track.obstacles[0].hiddenWarningDuration, .35);
  assert.equal(edited.track.riskZones[0].x, 70);
  assert.equal(edited.track.riskZones[0].y, 192);
  assert.equal(edited.track.riskZones[0].clearX, 170);
  assert.equal(edited.track.riskZones[0].safeFlipX, 0);
  assert.equal(edited.track.riskZones[1].riskFlipX, 0);
  assert.equal(plan.analysis.rewards[0].left, 40);
  assert.equal(plan.analysis.rewards[0].planeY, 192);
  assert.deepEqual(analysis, before);
  assert.equal(JSON.stringify(document), source);
  assert.deepEqual(edited.botReferences, document.botReferences);
  assert.deepEqual(edited.track.cues, document.track.cues);
  assert.deepEqual(edited.track.safeRoute, document.track.safeRoute);
});

test('gate moves opening and both offset bands without deleting parts at vertical/horizontal boundaries', () => {
  const { document, analysis } = fixture();
  const plan = translateObject(document, analysis, { collection: 'obstacles', id: 'gate' }, 5000, -500);
  const gate = plan.analysis.obstacles[1];
  assert.equal(gate.x, 910);
  assert.equal(gate.gapTop, 1);
  assert.equal(gate.gapBottom - gate.gapTop, 140);
  assert.equal(gate.topOffsetX, -20); assert.equal(gate.bottomOffsetX, 80);
  assert.equal(gate.bands[0].x, 890); assert.equal(gate.bands[1].x + gate.width, 1000);
  assert.ok(gate.bands.every(b => b.height > 0));
  assert.equal(gate.bands[1].y + gate.bands[1].height, 240);
});

test('free circular and finish rewards stay within corridor using actual shape, landing remains on stone', () => {
  const { document, analysis } = fixture();
  const free = translateObject(document, analysis, { collection: 'rewards', id: 'free' }, 9999, -9999).analysis.rewards[2];
  assert.equal(free.x, 980); assert.equal(free.y, 20); assert.equal(free.kind, 'negative');
  const finish = translateObject(document, analysis, { collection: 'rewards', id: 'finish' }, 9999, 9999).analysis.rewards[3];
  assert.equal(finish.x, 988); assert.equal(finish.y, 230);
  assert.equal(Math.max(...finish.segments.flatMap(s => [s.x1, s.x2])) + 1.5, 1000);
  assert.equal(finish.segments.length, 4);
  const pad = translateObject(document, analysis, { collection: 'rewards', id: 'pad' }, 9999, 90);
  assert.equal(pad.analysis.rewards[0].x, 200);
  assert.equal(pad.analysis.rewards[0].right, 200);
  assert.equal(pad.analysis.rewards[0].planeY, 192);
  assert.equal(pad.analysis.rewards[0].y, 192);
  assert.ok(pad.changes.every(c => c.path.at(-1) === 'x'));
});

test('source patch preserves untouched number spelling, formatting, escaped keys and unknown fields', () => {
  const source = '{\n "track": {"obstacles":[{"x":1e2, "y":2.0000}],"custom\\u004bey":2.9499999999999997}, "botReferences": [1e-9] }';
  const result = patchNumbers(source, [{ path: ['track', 'obstacles', 0, 'x'], value: 120.5 }]);
  assert.equal(result, source.replace('1e2', '120.5'));
  assert.equal(patchNumbers(source, []), source);
  assert.throws(() => patchNumbers('{"x":1,"x":2}', [{ path: ['x'], value: 3 }]));
  assert.throws(() => patchNumbers('{"x":1,}', []));
  assert.throws(() => patchNumbers('{"x":1}', [{ path: ['missing'], value: 3 }]));
});

test('repeated preview plans and cancellation do not commit or accumulate prior deltas', () => {
  const { document, analysis } = fixture(), source = JSON.stringify(document);
  const first = translateObject(document, analysis, { collection: 'obstacles', id: 'stone' }, 10, 0);
  const second = translateObject(document, analysis, { collection: 'obstacles', id: 'stone' }, 20, 0);
  assert.equal(first.analysis.obstacles[0].x, 110);
  assert.equal(second.analysis.obstacles[0].x, 120);
  // Cancel discards plans; exact source is still intact, with no serialization.
  assert.equal(JSON.stringify(document), source);
  assert.equal(analysis.obstacles[0].x, 100);
});
