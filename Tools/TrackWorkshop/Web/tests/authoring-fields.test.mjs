import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { clearBotReferences, copyBuiltinTrack, newLevelSource, objectPropertyFields, unusedTrackId,
  updateObjectProperties, updateTrackMetadata } from '../authoring-fields.mjs';

const fields = { id: 'custom-test', name: 'Уровень', length: '4000.5' };

test('a new level supplies schema v1 geometry and checks limits and catalog IDs', () => {
  const source = newLevelSource(fields);
  const document = JSON.parse(source);
  assert.deepEqual(document, { schemaVersion: 1, physicsVersion: 1, profilePolicy: 'any', warningMode: 'selected',
    track: { ...fields, length: 4000.5, obstacles: [], riskZones: [], negativeEnabled: true }, botReferences: [] });
  assert.throws(() => newLevelSource(fields, ['custom-test']), /ID занят/);
  for (const length of ['', '0', '-1', '1000001', NaN, Infinity]) {
    assert.throws(() => newLevelSource({ ...fields, length }), /Длина/);
  }
  assert.throws(() => newLevelSource({ ...fields, name: '' }), /Название/);
  assert.throws(() => newLevelSource({ ...fields, id: 'with\ncontrol' }), /управляющих/);
  assert.equal(unusedTrackId('custom-test', ['custom-test', 'custom-test-2']), 'custom-test-3');
});

test('metadata patches only affected values and never replaces geometry or unknown fields', () => {
  const source = '{ "note":1e2, "track":{"name":"Before", "id":"catalog", "length":1.5e3,"obstacles":[],"extension":{"raw":0.123456789123456789}},"botReferences":[{"inputs":[1]}] }\n';
  assert.equal(updateTrackMetadata(source, { length: '1500' }, ['catalog']), source);
  assert.equal(updateTrackMetadata(source, { name: 'Имя "с кавычками"', length: '1800' }, ['catalog']),
    source.replace('"Before"', JSON.stringify('Имя "с кавычками"')).replace('1.5e3', '1800'));
  assert.throws(() => updateTrackMetadata(source, { id: 'reserved' }, ['reserved']), /ID занят/);
  assert.throws(() => updateTrackMetadata(source, { length: '' }), /Длина/);
  assert.equal(clearBotReferences(source), source.replace('[{"inputs":[1]}]', '[]'));
});

test('cleaned download replaces stale references verbatim and rejects ambiguous JSON keys', () => {
  const references = '[{"profileId":"Chosen","trackHash":"stale","inputs":[1]}]';
  const source = `{"botReferences":${references},"track":{"id":"draft","name":"Draft","length":1.25e3,"obstacles":[],"extension":{"n":0.123456789123456789}}}\n`;
  assert.equal(clearBotReferences(source), source.replace(references, '[]'));
  assert.throws(() => clearBotReferences(source.replace('"inputs":[1]', '"inputs":[1],"inputs":[2]')), /Повторяющиеся/);
  assert.throws(() => clearBotReferences(source.replace('"length":1.25e3', '"length":1.25e3,"length":900')), /Повторяющиеся/);
  assert.throws(() => clearBotReferences(source.replace('"botReferences":', '"botReferences":[],"botReferences":')), /Повторяющиеся/);
});

test('copying the original catalog map preserves geometry and extracts input examples', async () => {
  const source = await readFile(new URL('../../../../Assets/StreamingAssets/Tracks/mixed-tricks.track.json', import.meta.url), 'utf8');
  const original = JSON.parse(source);
  const copy = copyBuiltinTrack(source, ['mixed-tricks', 'custom-mixed-tricks']);
  const result = JSON.parse(copy.source);
  assert.equal(copy.id, 'custom-mixed-tricks-2');
  assert.deepEqual(result.track, { ...original.track, id: copy.id });
  assert.deepEqual(result.botReferences, []);
  assert.deepEqual(copy.referenceHints, original.botReferences);
  assert.ok(copy.referenceHints.some(item => item.inputs.length));
});

test('changing gate offsets and hidden-stone settings keeps unknown values byte-for-byte', () => {
  const source = '{"track":{"obstacles":[{"id":"stone","kind":"floor","x":100,"width":60,"height":30,"extras":[1.00,2e2]}, {"id":"gate","kind":"gate","x":200,"width":16,"gapTop":80,"gapBottom":160}]}}';
  const stone = { collection: 'obstacles', id: 'stone' };
  const changed = updateObjectProperties(source, stone, { hidden: true, hiddenRevealDistance: '180', hiddenWarningDuration: '0.8' });
  assert.equal(changed.source, source.replace('[1.00,2e2]}', '[1.00,2e2], "hidden": true, "hiddenRevealDistance": 180, "hiddenWarningDuration": 0.8}'));
  assert.deepEqual(changed.target, stone);
  const cleared = updateObjectProperties(changed.source, stone, { hiddenWarningDuration: '' });
  assert.equal(JSON.parse(cleared.source).track.obstacles[0].hiddenWarningDuration, null);
  const gate = { collection: 'obstacles', id: 'gate' };
  assert.deepEqual(JSON.parse(updateObjectProperties(source, gate, { gapTop: '90', topOffsetX: '-20' }).source).track.obstacles[1],
    { id: 'gate', kind: 'gate', x: 200, width: 16, gapTop: 90, gapBottom: 160, topOffsetX: -20 });
  assert.throws(() => updateObjectProperties(source, stone, { width: '' }), /Ширина/);
  assert.throws(() => updateObjectProperties(source, stone, { hiddenWarningDuration: NaN }), /Предупреждение/);
  assert.throws(() => updateObjectProperties(source, gate, { kind: 'floor' }), /JSON/);
});

test('properties expose linked rewards and leave fixed finish dimensions to the source', () => {
  const document = { track: { obstacles: [{ id: 'floor', kind: 'floor' }, { id: 'gate', kind: 'gate' }], riskZones: [
    { id: 'landing', kind: 'landing', obstacleId: 'floor', landingWidth: 32, x: 30, y: 100 },
    { id: 'positive', kind: 'positive', obstacleId: 'gate', x: 60, y: 80 },
    { id: 'finish', kind: 'finish', x: 300, y: 40, finishWidth: 24, finishHeight: 20 }
  ] } };
  const source = JSON.stringify(document);
  const [landing, positive, finish] = document.track.riskZones;
  const fields = object => objectPropertyFields(object, 'rewards', document.track.obstacles);
  assert.deepEqual(fields(landing).find(field => field.key === 'obstacleId').choices, [['floor', 'floor']]);
  assert.deepEqual(fields(finish).map(field => field.key), ['id', 'x', 'y']);
  const changed = updateObjectProperties(source, { collection: 'rewards', id: positive.id }, { id: 'edited', placement: 'after', clearX: '100', obstacleId: '', freeReward: true });
  assert.deepEqual(changed.target, { collection: 'rewards', id: 'edited' });
  assert.deepEqual(JSON.parse(changed.source).track.riskZones[1], { ...positive, id: 'edited', placement: 'after', clearX: 100, obstacleId: null, freeReward: true });
  assert.throws(() => updateObjectProperties(source, { collection: 'rewards', id: landing.id }, { obstacleId: 'gate' }), /допустимое/);
});
