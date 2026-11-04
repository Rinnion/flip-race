export const clamp = (value, min, max) => Math.max(min, Math.min(max, value));

export function rewardAppearance(kind) {
  return {
    positive: { label: 'Бонус', trap: false, coin: false },
    instant: { label: 'Мгновенный бонус', trap: false, coin: false },
    negative: { label: 'Ловушка', trap: true, coin: false },
    coin: { label: 'Монета', trap: false, coin: true },
    landing: { label: 'Посадка', trap: false, coin: false },
    finish: { label: 'Финишный рывок', trap: false, coin: false }
  }[kind] || { label: kind, trap: false, coin: false };
}

export function parseInputs(text) {
  let values;
  try { values = JSON.parse(text); } catch { throw new Error('Нажатия: нужен JSON-массив, например [430, 560, 980].'); }
  if (!Array.isArray(values)) throw new Error('Нажатия должны быть массивом чисел.');
  if (values.length > 4096) throw new Error('Максимум 4096 нажатий.');
  values.forEach((value, index) => {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0)
      throw new Error(`Нажатие №${index + 1}: нужно конечное неотрицательное число.`);
    if (index && value <= values[index - 1])
      throw new Error(`Нажатие №${index + 1}: значения должны строго возрастать. Список не сортируется автоматически.`);
  });
  return values;
}

export function viewMetrics(width, height, corridorHeight, zoom, length, left) {
  const scale = height / (corridorHeight + 52) * zoom;
  const worldWidth = width / scale;
  const worldHeight = height / scale;
  return { scale, width: worldWidth, height: worldHeight,
    left: clamp(left, -20, Math.max(-20, length + 30 - worldWidth)),
    top: (corridorHeight - worldHeight) / 2 };
}

export function sampleAt(samples, time) {
  if (!samples.length) return null;
  if (time <= samples[0].time) return samples[0];
  let low = 0, high = samples.length - 1;
  while (low < high) {
    const mid = Math.floor((low + high) / 2);
    if (samples[mid].time < time) low = mid + 1; else high = mid;
  }
  const after = samples[low], before = samples[Math.max(0, low - 1)];
  const ratio = after.time > before.time ? clamp((time - before.time) / (after.time - before.time), 0, 1) : 0;
  return { ...before, time, x: before.x + (after.x - before.x) * ratio, y: before.y + (after.y - before.y) * ratio };
}

// A request captures a revision. Increment synchronously on every user edit,
// before debounce, so a late successful response cannot resurrect old geometry.
export class Revision {
  value = 0;
  bump() { return ++this.value; }
  current(value) { return value === this.value; }
}

// Source coordinates include every value, so structural edits can retain the
// exact text of untouched objects instead of serializing the document again.
export function scanJson(source) {
  JSON.parse(source);
  let position = 0;
  const spans = new Map();
  const whitespace = () => { while (/\s/.test(source[position] || '') && position < source.length) position++; };
  const string = () => {
    const start = position++;
    while (position < source.length) {
      const ch = source[position++];
      if (ch === '\\') position++;
      else if (ch === '"') return JSON.parse(source.slice(start, position));
    }
    throw new Error('Незавершённая строка JSON.');
  };
  const value = path => {
    whitespace();
    const start = position, node = { start, end: null, kind: null, items: [] };
    spans.set(JSON.stringify(path), node);
    if (source[position] === '{') {
      node.kind = 'object';
      position++; whitespace(); const keys = new Set();
      if (source[position] !== '}') while (true) {
        whitespace(); const key = string();
        if (keys.has(key)) throw new Error('Повторяющиеся поля JSON нельзя редактировать мышью.');
        keys.add(key); node.items.push(key); whitespace(); position++; value([...path, key]); whitespace();
        if (source[position] !== ',') break;
        position++;
      }
      position++;
    } else if (source[position] === '[') {
      node.kind = 'array';
      position++; whitespace(); let index = 0;
      if (source[position] !== ']') while (true) {
        node.items.push(value([...path, index++])); whitespace(); if (source[position] !== ',') break; position++;
      }
      position++;
    } else if (source[position] === '"') { node.kind = 'string'; string(); }
    else {
      const token = /^(?:-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null)/.exec(source.slice(position));
      if (!token) throw new Error('Некорректный JSON.');
      node.kind = /^-?\d/.test(token[0]) ? 'number' : token[0];
      position += token[0].length;
    }
    node.end = position;
    return node;
  };
  value([]);
  return spans;
}

// Replace only selected numeric tokens. All other bytes (including unknown
// properties, exponent notation, long decimals and botReferences) survive.
export function patchNumbers(source, changes) {
  const spans = scanJson(source);
  const replacements = changes.map(change => {
    const span = spans.get(JSON.stringify(change.path));
    if (!span && change.insert === true && Number.isFinite(change.value)) {
      const parent = spans.get(JSON.stringify(change.path.slice(0, -1))), property = change.path.at(-1);
      if (!parent || parent.kind !== 'object' || typeof property !== 'string') throw new Error('Нельзя добавить числовое поле.');
      return { start: parent.end - 1, end: parent.end - 1, text: `${parent.items.length ? ',' : ''}${JSON.stringify(property)}:${change.value}` };
    }
    if (!span || span.kind !== 'number' || !Number.isFinite(change.value)) throw new Error('Поле координаты отсутствует или не является числом.');
    return { ...span, text: String(change.value) };
  }).sort((a, b) => b.start - a.start);
  let result = source;
  for (const replacement of replacements) result = result.slice(0, replacement.start) + replacement.text + result.slice(replacement.end);
  return result;
}

function rewardBounds(reward) {
  if (reward.shape === 'landing') return { left: reward.left, right: reward.right, top: reward.planeY, bottom: reward.planeY };
  if (reward.shape === 'finish') {
    const x = reward.segments.flatMap(s => [s.x1, s.x2]), y = reward.segments.flatMap(s => [s.y1, s.y2]);
    const halfStroke = reward.strokeWidth / 2;
    return { left: Math.min(...x) - halfStroke, right: Math.max(...x) + halfStroke, top: Math.min(...y) - halfStroke, bottom: Math.max(...y) + halfStroke };
  }
  return { left: reward.x - reward.radius, right: reward.x + reward.radius, top: reward.y - reward.radius, bottom: reward.y + reward.radius };
}

// Translation always starts from an immutable pointerdown snapshot. Previewing
// is separate from source commit, so cancel simply discards the returned plan.
export function translateObject(document, analysis, target, requestedDx, requestedDy) {
  const preview = structuredClone(analysis), changes = [], length = analysis.length, height = analysis.corridorHeight;
  let minX = -Infinity, maxX = Infinity, minY = -Infinity, maxY = Infinity;
  const xBounds = (left, right) => { minX = Math.max(minX, -left); maxX = Math.min(maxX, length - right); };
  const yBounds = (top, bottom) => { minY = Math.max(minY, -top); maxY = Math.min(maxY, height - bottom); };
  const triggers = ['clearX', 'safeFlipX', 'riskFlipX'];
  const zones = document.track.riskZones || [];
  const movableTriggers = raw => triggers.filter(key => Object.hasOwn(raw, key) && typeof raw[key] === 'number' && raw[key] > 0);
  const addRewardBounds = (reward, raw, includeY, includeTriggers) => {
    const bounds = rewardBounds(reward); xBounds(bounds.left, bounds.right); xBounds(reward.x, reward.x);
    if (includeY) { yBounds(bounds.top, bounds.bottom); yBounds(reward.y, reward.y); }
    if (includeTriggers) for (const key of movableTriggers(raw)) xBounds(raw[key], raw[key]);
  };
  let obstacleIndex = -1, rewardIndex = -1, linkedIndices = [], moveY = false;
  if (target.collection === 'obstacles') {
    obstacleIndex = analysis.obstacles.findIndex(o => o.id === target.id);
    if (obstacleIndex < 0) throw new Error('Препятствие не найдено.');
    const obstacle = analysis.obstacles[obstacleIndex];
    xBounds(obstacle.x, obstacle.x);
    for (const band of obstacle.bands) xBounds(band.x, band.x + band.width);
    moveY = obstacle.kind === 'gate';
    if (moveY) {
      const minimumBand = Math.min(1, obstacle.gapTop, height - obstacle.gapBottom);
      if (minimumBand <= 0) throw new Error('Для перемещения ворот нужны обе ненулевые части.');
      minY = minimumBand - obstacle.gapTop; maxY = height - minimumBand - obstacle.gapBottom;
    }
    linkedIndices = analysis.rewards.flatMap((reward, index) => reward.obstacleId === obstacle.id ? [index] : []);
    for (const index of linkedIndices) {
      const reward = analysis.rewards[index], raw = zones.find(z => z.id === reward.id);
      if (!raw) throw new Error('Связанная награда отсутствует в JSON.');
      addRewardBounds(reward, raw, moveY && reward.shape !== 'landing', true);
    }
  } else if (target.collection === 'rewards') {
    rewardIndex = analysis.rewards.findIndex(r => r.id === target.id);
    if (rewardIndex < 0) throw new Error('Награда не найдена.');
    const reward = analysis.rewards[rewardIndex];
    if (reward.shape === 'landing') {
      const stone = analysis.obstacles.find(o => o.id === reward.obstacleId);
      if (!stone || stone.kind === 'gate') throw new Error('Посадочная метка должна быть связана с камнем.');
      minX = stone.x - reward.x; maxX = stone.x + stone.width - reward.x;
    } else { moveY = true; addRewardBounds(reward, null, true, false); }
  } else throw new Error('Объект нельзя перемещать.');
  if (minX > maxX || (moveY && minY > maxY)) throw new Error('Группа не помещается в трассу. Измените её параметры в JSON.');
  const dx = clamp(Math.round(requestedDx * 10) / 10, minX, maxX);
  const dy = moveY ? clamp(Math.round(requestedDy * 10) / 10, minY, maxY) : 0;
  const change = (path, before, after) => { if (after !== before) changes.push({ path, value: after }); };
  const moveReward = (index, group) => {
    const before = analysis.rewards[index], after = preview.rewards[index];
    const rawIndex = zones.findIndex(z => z.id === before.id), raw = zones[rawIndex];
    if (!raw) throw new Error('Награда отсутствует в JSON.');
    after.x += dx;
    if (before.shape !== 'landing') after.y += dy;
    change(['track', 'riskZones', rawIndex, 'x'], raw.x, after.x);
    if (before.shape !== 'landing') change(['track', 'riskZones', rawIndex, 'y'], raw.y, after.y);
    if (group) for (const key of movableTriggers(raw)) {
      change(['track', 'riskZones', rawIndex, key], raw[key], raw[key] + dx);
      if (key === 'clearX') after.clearX = raw[key] + dx;
    }
    if (before.shape === 'landing') {
      const stone = preview.obstacles.find(o => o.id === before.obstacleId);
      after.left = Math.max(stone.x, after.x - raw.landingWidth / 2);
      after.right = Math.min(stone.x + stone.width, after.x + raw.landingWidth / 2);
    }
    for (const segment of after.segments) { segment.x1 += dx; segment.x2 += dx; segment.y1 += dy; segment.y2 += dy; }
  };
  if (obstacleIndex >= 0) {
    const before = analysis.obstacles[obstacleIndex], after = preview.obstacles[obstacleIndex];
    const rawIndex = document.track.obstacles.findIndex(o => o.id === before.id), raw = document.track.obstacles[rawIndex];
    if (!raw) throw new Error('Препятствие отсутствует в JSON.');
    after.x += dx; change(['track', 'obstacles', rawIndex, 'x'], raw.x, after.x);
    if (moveY) {
      after.gapTop += dy; after.gapBottom += dy;
      change(['track', 'obstacles', rawIndex, 'gapTop'], raw.gapTop, after.gapTop);
      change(['track', 'obstacles', rawIndex, 'gapBottom'], raw.gapBottom, after.gapBottom);
    }
    for (const band of after.bands) {
      band.x += dx;
      if (moveY && band.index === 0) band.height = after.gapTop;
      if (moveY && band.index === 1) { band.y = after.gapBottom; band.height = height - after.gapBottom; }
    }
    for (const index of linkedIndices) moveReward(index, true);
  } else moveReward(rewardIndex, false);
  return { analysis: preview, changes, dx, dy };
}
