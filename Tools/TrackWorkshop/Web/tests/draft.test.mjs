import test from 'node:test';
import assert from 'node:assert/strict';
import { DRAFT_KEY, createDraft, readDraft, writeDraft, clearDraft } from '../draft.mjs';

const sample = { json: '{\n  invalid JSON\t', inputs: '[broken', filename: 'мой файл.json', profile: 'FastFlip', axis: 'time', warning: '1' };
const memory = () => { const values = new Map(); return { getItem: k => values.get(k) ?? null, setItem: (k, v) => values.set(k, v), removeItem: k => values.delete(k) }; };

test('versioned draft preserves raw invalid author text and settings exactly', () => {
  const storage = memory();
  assert.equal(writeDraft(storage, sample).kind, 'saved');
  const saved = readDraft(storage);
  assert.equal(saved.kind, 'saved');
  for (const key of Object.keys(sample)) assert.equal(saved.draft[key], sample[key]);
  assert.equal(saved.draft.version, 1);
  assert.equal(clearDraft(storage).kind, 'empty');
  assert.equal(readDraft(storage).kind, 'empty');
});
test('corrupt and unsupported data are reported without deletion', () => {
  for (const raw of ['{broken', 'null', JSON.stringify({ ...sample, version: 2 }), JSON.stringify({ ...sample, version: 1 }), JSON.stringify({ ...sample, version: 1, savedAt: 'bad' })]) {
    const storage = memory(); storage.setItem(DRAFT_KEY, raw);
    assert.equal(readDraft(storage).kind, 'error');
    assert.equal(storage.getItem(DRAFT_KEY), raw);
  }
});
test('quota, disabled and unavailable storage remain recoverable errors', () => {
  const storage = { getItem() { throw new Error('disabled'); }, setItem() { throw new Error('quota'); }, removeItem() { throw new Error('disabled'); } };
  assert.equal(readDraft(storage).kind, 'error');
  assert.equal(writeDraft(storage, sample).kind, 'error');
  assert.equal(clearDraft(storage).kind, 'error');
  assert.equal(readDraft(undefined).kind, 'error');
});
test('schema validates limits and supported settings without parsing draft contents', () => {
  assert.throws(() => createDraft({ ...sample, profile: 'Manual' }));
  assert.throws(() => createDraft({ ...sample, axis: 'guess' }));
  assert.throws(() => createDraft({ ...sample, warning: '8' }));
  assert.throws(() => createDraft({ ...sample, filename: 'x'.repeat(256) }));
  assert.throws(() => createDraft({ ...sample, json: 'я'.repeat(1024 * 1024 + 1) }));
  assert.throws(() => createDraft({ ...sample, inputs: 'x'.repeat(128 * 1024 + 1) }));
  assert.equal(createDraft(sample).json, sample.json);
});
