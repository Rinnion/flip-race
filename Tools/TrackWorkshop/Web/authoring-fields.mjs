import { scanJson } from './model.mjs';

export const NEW_LEVEL_LENGTH = 4000;
export const NEW_LEVEL_NAME = 'Новый уровень';

export function unusedTrackId(base, taken) {
  const ids = new Set(taken);
  let id = base;
  for (let n = 2; ids.has(id); n++) id = `${base}-${n}`;
  return id;
}

function readId(value, label = 'ID') {
  if (typeof value !== 'string' || !value.trim() || value.length > 128 || /[\x00-\x1f\x7f-\x9f]/.test(value)) {
    throw new Error(`${label} должен содержать от 1 до 128 символов без управляющих символов.`);
  }
  return value;
}

function readNumber(value, label, nullable = false) {
  if (nullable && (value === '' || value === null)) return null;
  const number = typeof value === 'number' ? value : typeof value === 'string' && value.trim() ? Number(value) : NaN;
  if (!Number.isFinite(number)) throw new Error(`${label}: введите конечное число.`);
  return number;
}

function readMetadata(fields, reserved, previousId = null) {
  const id = readId(fields.id);
  if (id !== previousId && reserved.includes(id)) throw new Error('Этот ID занят встроенной трассой. Задайте другой ID.');
  const { name } = fields;
  if (typeof name !== 'string' || !name.length || name.length > 160) throw new Error('Название должно содержать от 1 до 160 символов.');
  const length = readNumber(fields.length, 'Длина трассы');
  if (length <= 0 || length > 1000000) throw new Error('Длина трассы должна быть больше 0 и не больше 1 000 000.');
  return { id, name, length };
}

export function newLevelSource(fields, reserved = []) {
  return JSON.stringify({ schemaVersion: 1, physicsVersion: 1, profilePolicy: 'any', warningMode: 'selected',
    track: { ...readMetadata(fields, reserved), obstacles: [], riskZones: [], negativeEnabled: true }, botReferences: [] }, null, 2) + '\n';
}

// A form changes only its source tokens, preserving unknown fields and exact numeric spelling elsewhere.
function patchValues(source, changes) {
  if (!changes.length) return source;
  const spans = scanJson(source), at = path => spans.get(JSON.stringify(path));
  const edits = [], insertions = new Map();
  for (const { path, value } of changes) {
    const span = at(path);
    if (span) { edits.push({ ...span, text: JSON.stringify(value) }); continue; }
    const parent = at(path.slice(0, -1));
    if (parent?.kind !== 'object') throw new Error('Поле исходника недоступно.');
    if (!insertions.has(parent)) insertions.set(parent, []);
    insertions.get(parent).push(`${JSON.stringify(path.at(-1))}: ${JSON.stringify(value)}`);
  }
  for (const [parent, fields] of insertions) {
    edits.push({ start: parent.end - 1, end: parent.end - 1, text: `${parent.items.length ? ', ' : ''}${fields.join(', ')}` });
  }
  for (const { start, end, text } of edits.sort((a, b) => b.start - a.start)) source = source.slice(0, start) + text + source.slice(end);
  return source;
}

export function updateTrackMetadata(source, fields, reserved = []) {
  const { track } = JSON.parse(source);
  const metadata = readMetadata({ id: track.id, name: track.name, length: track.length, ...fields }, reserved, track.id);
  return patchValues(source, Object.entries(metadata).filter(([key, value]) => track[key] !== value)
    .map(([key, value]) => ({ path: ['track', key], value })));
}

export function clearBotReferences(source) {
  const { botReferences } = JSON.parse(source);
  return botReferences === undefined || (Array.isArray(botReferences) && !botReferences.length) ? source : patchValues(source, [{ path: ['botReferences'], value: [] }]);
}

export function copyBuiltinTrack(source, taken) {
  const { track, botReferences } = JSON.parse(source);
  const id = unusedTrackId(`custom-${track.id}`.slice(0, 115), taken);
  return { id, source: updateTrackMetadata(clearBotReferences(source), { id }, taken), referenceHints: botReferences || [] };
}

const id = { key: 'id', label: 'Идентификатор', type: 'id' };
const numeric = (key, label, defaultValue, options) => ({ key, label, type: 'number', defaultValue, ...options });
const select = (key, label, choices, defaultValue, nullable = false) => ({ key, label, type: 'select', choices, defaultValue, nullable });
const x = numeric('x', 'Позиция X');
const y = numeric('y', 'Позиция Y');
const width = numeric('width', 'Ширина');
const obstacleKinds = [['floor', 'Пол'], ['ceiling', 'Потолок']];

export function objectPropertyFields(object, collection, obstacles = []) {
  if (collection === 'obstacles' && object.kind === 'gate') {
    return [id, x, width, numeric('gapTop', 'Верх проёма · Y'), numeric('gapBottom', 'Низ проёма · Y'),
      numeric('topOffsetX', 'Сдвиг верха · X', 0), numeric('bottomOffsetX', 'Сдвиг низа · X', 0)];
  }
  if (collection === 'obstacles') {
    return [id, x, select('kind', 'Опора', obstacleKinds), width, numeric('height', 'Высота'),
      { key: 'hidden', label: 'Скрытый камень', type: 'checkbox', defaultValue: false },
      numeric('hiddenRevealDistance', 'Расстояние раскрытия', null, { nullable: true, advanced: true }),
      numeric('hiddenWarningDuration', 'Предупреждение · с', null, { nullable: true, advanced: true })];
  }
  const landing = object.kind === 'landing', finish = object.kind === 'finish';
  const fields = [id, x];
  if (!landing) fields.push(y);
  if (landing) fields.push(numeric('landingWidth', 'Ширина окна'));
  else if (!finish) fields.push(numeric('radius', 'Радиус', 6));
  if (landing || object.kind === 'positive' || object.obstacleId != null) {
    const choices = obstacles.filter(item => !landing || item.kind !== 'gate').map(item => [item.id, item.id]);
    fields.push(select('obstacleId', 'Препятствие', landing ? choices : [['', 'Без связи'], ...choices], null, !landing));
  }
  if (object.kind === 'positive') {
    fields.push(select('placement', 'Положение', [['before', 'До препятствия'], ['after', 'После препятствия']], 'before'),
      numeric('clearX', 'Граница зачёта · X', 0), { key: 'freeReward', label: 'Свободная награда', type: 'checkbox', defaultValue: false });
  }
  return fields;
}

function readProperty(field, value) {
  if (field.type === 'id') return readId(value, field.label);
  if (field.type === 'number') return readNumber(value, field.label, field.nullable);
  if (field.type === 'select' && field.choices.some(([key]) => key === value)) return value === '' && field.nullable ? null : value;
  if (field.type === 'checkbox' && typeof value === 'boolean') return value;
  throw new Error(`${field.label}: выберите допустимое значение.`);
}

export function updateObjectProperties(source, target, fields) {
  const { track } = JSON.parse(source);
  const collection = target.collection === 'obstacles' ? 'obstacles' : 'riskZones';
  const index = track[collection]?.findIndex(object => object.id === target.id) ?? -1;
  if (index < 0) throw new Error('Выбранного объекта нет в текущем исходнике.');
  const object = track[collection][index], path = ['track', collection, index];
  const available = objectPropertyFields(object, target.collection, track.obstacles), changes = [];
  let nextId = target.id;
  for (const [key, input] of Object.entries(fields)) {
    const field = available.find(field => field.key === key);
    if (!field) throw new Error(`Поле «${key}» редактируется в JSON.`);
    const value = readProperty(field, input);
    if (value !== (object[key] ?? field.defaultValue)) changes.push({ path: [...path, key], value });
    if (key === 'id') nextId = value;
  }
  return { source: patchValues(source, changes), target: { ...target, id: nextId } };
}
