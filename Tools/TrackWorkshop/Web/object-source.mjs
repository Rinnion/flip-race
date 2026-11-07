import { scanJson } from './model.mjs';

// Read the author's object verbatim, including unknown fields and numeric notation.
// Analysis objects contain derived geometry and are not a substitute for this source.
function objectSpan(source, target) {
  if (!target || !['obstacles', 'rewards'].includes(target.collection)) return null;
  const collection = target.collection === 'obstacles' ? 'obstacles' : 'riskZones';
  const document = JSON.parse(source);
  const index = document.track?.[collection]?.findIndex(object => object.id === target.id) ?? -1;
  if (index < 0) return null;
  const span = scanJson(source).get(JSON.stringify(['track', collection, index]));
  return span || null;
}

export function selectedObjectSource(source, target) {
  const span = objectSpan(source, target);
  return span ? source.slice(span.start, span.end) : null;
}

export function replaceObjectSource(source, target, replacement) {
  const object = JSON.parse(replacement);
  if (!object || typeof object !== 'object' || Array.isArray(object)) throw new Error('Нужен один JSON-объект.');
  if (typeof object.id !== 'string' || !object.id.trim()) throw new Error('У объекта должен быть непустой строковый id.');
  scanJson(replacement); // Reject ambiguous duplicate properties before asking Unity.
  const span = objectSpan(source, target);
  if (!span) throw new Error('Выбранного объекта нет в текущем исходнике.');
  const candidate = source.slice(0, span.start) + replacement.trim() + source.slice(span.end);
  if (new TextEncoder().encode(candidate).length > 2 * 1024 * 1024) throw new Error('Файл больше 2 МиБ.');
  return { source: candidate, target: { collection: target.collection, id: object.id } };
}
