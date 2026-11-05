export const DRAFT_KEY = 'flip-race.workshop.draft.v1';
export const DRAFT_VERSION = 1;
const MAX_JSON_BYTES = 2 * 1024 * 1024;
const MAX_INPUT_BYTES = 128 * 1024;
const bytes = text => new TextEncoder().encode(text).length;

// Preserve the author's text, even when it is not valid track/inputs JSON.
export function createDraft(value, savedAt = new Date().toISOString()) {
  if (!value || typeof value.json !== 'string' || typeof value.inputs !== 'string'
    || typeof value.filename !== 'string' || value.filename.length > 255
    || !['Chosen', 'FastFlip'].includes(value.profile)
    || !['distance', 'time'].includes(value.axis)
    || !['0.4', '0.6', '0.8', '1', '1.2'].includes(String(value.warning))
    || bytes(value.json) > MAX_JSON_BYTES || bytes(value.inputs) > MAX_INPUT_BYTES
    || typeof savedAt !== 'string' || !Number.isFinite(Date.parse(savedAt))) {
    throw new Error('Черновик имеет неподдерживаемый формат или превышает лимит (JSON 2 МиБ, нажатия 128 КиБ).');
  }
  return { version: DRAFT_VERSION, savedAt, json: value.json, inputs: value.inputs,
    filename: value.filename, profile: value.profile, axis: value.axis, warning: String(value.warning) };
}

export function readDraft(storage) {
  try {
    const raw = storage.getItem(DRAFT_KEY);
    if (raw === null) return { kind: 'empty' };
    const value = JSON.parse(raw);
    if (value?.version !== DRAFT_VERSION || typeof value.savedAt !== 'string') throw new Error('Неизвестная версия или формат черновика.');
    return { kind: 'saved', draft: createDraft(value, value.savedAt) };
  } catch (error) { return { kind: 'error', error }; }
}

export function writeDraft(storage, value) {
  try {
    const draft = createDraft(value);
    storage.setItem(DRAFT_KEY, JSON.stringify(draft));
    return { kind: 'saved', draft };
  } catch (error) { return { kind: 'error', error }; }
}

export function clearDraft(storage) {
  try { storage.removeItem(DRAFT_KEY); return { kind: 'empty' }; }
  catch (error) { return { kind: 'error', error }; }
}
