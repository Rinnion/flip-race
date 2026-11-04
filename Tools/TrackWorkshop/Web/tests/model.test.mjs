import test from 'node:test';
import assert from 'node:assert/strict';
import { parseInputs, sampleAt, viewMetrics, rewardAppearance, Revision } from '../model.mjs';

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
