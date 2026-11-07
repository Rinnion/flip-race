import test from 'node:test';
import assert from 'node:assert/strict';
import { selectedObjectSource, replaceObjectSource } from '../object-source.mjs';

const rawStone = '{ "id": "stone-1", "x": 1.2300e2, "custom": { "note": "author data" } }';
const rawReward = '{"id":"coin-1","obstacleId":"stone-1","radius":8}';
const source = `{"track":{"obstacles":[${rawStone}],"riskZones":[${rawReward}]},"botReferences":[{"id":"stone-1"}]}`;

test('reads exact author object, preserving unknown fields, whitespace and numeric notation', () => {
  assert.equal(selectedObjectSource(source, { collection: 'obstacles', id: 'stone-1' }), rawStone);
  assert.equal(selectedObjectSource(source, { collection: 'rewards', id: 'coin-1' }), rawReward);
});

test('reads current source after edit and undo, without retaining deleted or unrelated objects', () => {
  const target = { collection: 'obstacles', id: 'stone-1' };
  const edited = source.replace('1.2300e2', '200');
  assert.equal(selectedObjectSource(edited, target), rawStone.replace('1.2300e2', '200'));
  assert.equal(selectedObjectSource(source, target), rawStone);
  assert.equal(selectedObjectSource('{"track":{"obstacles":[]}}', target), null);
  assert.equal(selectedObjectSource(source, { collection: 'rewards', id: 'stone-1' }), null);
  assert.equal(selectedObjectSource(source, null), null);
});

test('invalid or ambiguous source cannot yield a stale or rewritten object', () => {
  assert.throws(() => selectedObjectSource(source.slice(0, -1), { collection: 'obstacles', id: 'stone-1' }));
  const duplicate = source.replace('"x":', '"id":"other","x":');
  assert.throws(() => selectedObjectSource(duplicate, { collection: 'obstacles', id: 'other' }), /Повторяющиеся/);
});

test('replacement changes only selected object bytes and can return a renamed selection', () => {
  const replacement = '{ "id":"stone-new", "x": 9.876543210123456e1, "custom":true }';
  const result = replaceObjectSource(source, { collection: 'obstacles', id: 'stone-1' }, replacement);
  assert.equal(result.source, source.replace(rawStone, replacement));
  assert.deepEqual(result.target, { collection: 'obstacles', id: 'stone-new' });
  assert.equal(selectedObjectSource(result.source, result.target), replacement);
  assert.equal(selectedObjectSource(result.source, { collection: 'rewards', id: 'coin-1' }), rawReward);
  assert.equal(source.includes(rawStone), true);
});

test('rewards replace riskZones entry while all surrounding text remains exact', () => {
  const replacement = '{"id":"coin-1","radius":1.600e1,"extra":[1,2]}';
  assert.equal(replaceObjectSource(source, { collection: 'rewards', id: 'coin-1' }, replacement).source, source.replace(rawReward, replacement));
});

test('replacement rejects incomplete JSON, nonobject values, missing IDs, duplicate fields and lost targets', () => {
  const target = { collection: 'obstacles', id: 'stone-1' };
  for (const invalid of ['{', 'null', '[]', '2', '{}', '{"id":3}', '{"id":" "}', '{"id":"a","id":"b"}']) {
    assert.throws(() => replaceObjectSource(source, target, invalid));
  }
  assert.throws(() => replaceObjectSource(source, { ...target, id: 'missing' }, '{"id":"a"}'));
  assert.equal(selectedObjectSource(source, target), rawStone);
});
