import test from 'node:test';
import assert from 'node:assert/strict';
import { captureRunSettings, DEFAULT_SURFACE_RUN_SETTINGS, storedRunSettings } from '../run-settings.mjs';
import { DRAFT_KEY, readDraft, writeDraft } from '../draft.mjs';

const profile = 'FastFlip', warningDuration = 1.2;
const custom = { enabled: true, ratePercent: 2, curve: 'Exponential', capPercent: 40, airDecayPercent: 0, resetOnCollision: false };
const legacy = { ...DEFAULT_SURFACE_RUN_SETTINGS, enabled: false };

test('game defaults start ON and a captured run is isolated from later edits', () => {
  assert.deepEqual(DEFAULT_SURFACE_RUN_SETTINGS, { enabled: true, ratePercent: 5, curve: 'Linear', capPercent: 30,
    airDecayPercent: 200, resetOnCollision: true });
  const draft = { profile, warningDuration, surfaceRunSettings: { ...custom } };
  const snapshot = captureRunSettings(draft);
  draft.profile = 'Chosen';
  draft.surfaceRunSettings.curve = 'Linear';
  assert.deepEqual(snapshot, { profile, warningDuration, surfaceRunSettings: custom });
});

test('saved rows restore complete options; old routes restore OFF; custom OFF parameters survive', () => {
  assert.deepEqual(storedRunSettings({ profile, warningDuration, surfaceRun: false }), { profile, warningDuration, surfaceRunSettings: legacy });
  assert.deepEqual(storedRunSettings({ profile, warningDuration }), { profile, warningDuration, surfaceRunSettings: legacy });
  for (const enabled of [true, false]) {
    const surfaceRunSettings = { ...custom, enabled };
    assert.deepEqual(storedRunSettings({ profile, warningDuration, surfaceRun: enabled, surfaceRunSettings }),
      { profile, warningDuration, surfaceRunSettings });
  }
});

test('unsupported profiles, warning and malformed acceleration options cannot enter a run snapshot', () => {
  const valid = { profile, warningDuration, surfaceRunSettings: custom };
  assert.throws(() => captureRunSettings({ ...valid, profile: 'Custom' }));
  assert.throws(() => captureRunSettings({ ...valid, warningDuration: '0.8' }));
  assert.throws(() => captureRunSettings({ ...valid, warningDuration: 0.5 }));
  for (const invalid of [false, null, {}, { ...custom, enabled: 1 }, { ...custom, ratePercent: 0 }, { ...custom, ratePercent: 2.5 },
    { ...custom, curve: 'linear' }, { ...custom, capPercent: 25 }, { ...custom, airDecayPercent: 150 }, { ...custom, resetOnCollision: 'true' }]) {
    assert.throws(() => captureRunSettings({ ...valid, surfaceRunSettings: invalid }));
  }
});

test('draft v1 migrates to OFF and preserves unknown metadata when new settings are saved', () => {
  const json = '{"workshopAnalysis":{"version":1}}\n';
  const input = { profile, warning: String(warningDuration), axis: 'time', json, inputs: '[0,1.3]', filename: 'track.json' };
  const extension = { version: 9, message: 'custom draft data' };
  const values = new Map([[DRAFT_KEY, JSON.stringify({ ...input, version: 1, savedAt: new Date().toISOString(), extension })]]);
  const storage = { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value) };
  const rawLegacy = values.get(DRAFT_KEY);
  assert.deepEqual(readDraft(storage).draft.surfaceRunSettings, legacy);
  assert.equal(values.get(DRAFT_KEY), rawLegacy);
  assert.equal(writeDraft(storage, { ...input, surfaceRunSettings: custom }).kind, 'saved');
  const stored = readDraft(storage).draft;
  assert.deepEqual(stored.surfaceRunSettings, custom);
  assert.deepEqual(stored.extension, extension);
  assert.equal(stored.json, json);
  assert.equal(stored.inputs, input.inputs);
  assert.equal(stored.version, 1);
  values.set(DRAFT_KEY, JSON.stringify({ ...stored, surfaceRunSettings: { ...custom, extension } }));
  assert.equal(writeDraft(storage, { ...input, surfaceRunSettings: { ...custom, enabled: false } }).kind, 'saved');
  assert.deepEqual(readDraft(storage).draft.surfaceRunSettings, { ...custom, enabled: false, extension });
});
