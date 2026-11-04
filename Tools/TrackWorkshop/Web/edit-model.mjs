import { clamp, scanJson } from './model.mjs';

const number = value => Math.round(value * 10) / 10;
const finite = (...values) => { if (!values.every(Number.isFinite)) throw new Error('Координаты должны быть конечными числами.'); };
const key = path => JSON.stringify(path);
const spanAt = (spans, path) => {
  const span = spans.get(key(path));
  if (!span) throw new Error('Поле JSON отсутствует: ' + path.join('.'));
  return span;
};
function applyText(source, edits) {
  let end = source.length;
  for (const edit of edits.sort((a, b) => b.start - a.start)) {
    if (edit.end > end) throw new Error('Пересекающиеся изменения JSON.');
    source = source.slice(0, edit.start) + edit.text + source.slice(edit.end);
    end = edit.start;
  }
  return source;
}
function appendItem(source, spans, path, object) {
  const array = spans.get(key(path));
  if (array) {
    if (array.kind !== 'array') throw new Error('Ожидается массив объектов.');
    return [{ start: array.end - 1, end: array.end - 1, text: `${array.items.length ? ',' : ''}\n      ${JSON.stringify(object)}\n    ` }];
  }
  // riskZones is optional in schema v1. Adding the first reward creates just
  // this property without rewriting the surrounding track or its numbers.
  if (path.at(-1) !== 'riskZones') throw new Error('Массив препятствий отсутствует.');
  const track = spanAt(spans, ['track']);
  return [{ start: track.end - 1, end: track.end - 1, text: `${track.items.length ? ',' : ''}\n    "riskZones": [${JSON.stringify(object)}]\n  ` }];
}
function removeItems(source, spans, path, indices) {
  if (!indices.length) return null;
  const array = spanAt(spans, path), removed = new Set(indices);
  const survivors = array.items.filter((_, index) => !removed.has(index)).map(item => source.slice(item.start, item.end));
  return { start: array.start, end: array.end, text: survivors.length ? '[\n      ' + survivors.join(',\n      ') + '\n    ]' : '[]' };
}
function uniqueId(document, prefix) {
  const ids = new Set([...document.track.obstacles, ...(document.track.riskZones || [])].map(item => item.id));
  for (let n = 1; ; n++) if (!ids.has(`${prefix}-${n}`)) return `${prefix}-${n}`;
}
function actualRight(obstacle) { return obstacle.kind === 'gate' ? Math.max(...obstacle.bands.map(b => b.x + b.width)) : obstacle.x + obstacle.width; }
function chooseObstacle(analysis, targetId) {
  const obstacle = analysis.obstacles.find(item => item.id === targetId);
  if (!obstacle) throw new Error('Сначала выберите связанное препятствие на схеме.');
  return obstacle;
}

export function createObject(source, analysis, kind, x, y, targetId = null) {
  finite(x, y);
  const spans = scanJson(source), document = JSON.parse(source), track = document.track;
  const length = analysis.length, height = analysis.corridorHeight;
  let object, collection, description;
  if (['stone-floor', 'stone-ceiling', 'hidden-floor', 'hidden-ceiling', 'gate'].includes(kind)) {
    if (track.obstacles.length >= 1024) throw new Error('Достигнут предел: 1024 препятствия.');
    if (length < 1) throw new Error('Для нового препятствия нужна длина трассы не меньше 1.');
    collection = 'obstacles';
    const gate = kind === 'gate', hidden = kind.startsWith('hidden-'), width = Math.min(gate ? 16 : 64, length);
    object = { id: uniqueId(document, gate ? 'gate' : hidden ? 'hidden' : 'stone'), kind: gate ? 'gate' : kind.endsWith('ceiling') ? 'ceiling' : 'floor',
      x: clamp(number(x), 0, length - width), width };
    if (gate) Object.assign(object, { gapTop: 80, gapBottom: 160, topOffsetX: 0, bottomOffsetX: 0 });
    else Object.assign(object, { height: 48, hidden });
    if (hidden && document.warningMode === 'authored') object.hiddenWarningDuration = .8;
    description = gate ? 'Добавлены ворота с двумя частями.' : hidden ? 'Добавлен скрытый камень.' : 'Добавлен камень.';
  } else {
    if (!['instant', 'negative', 'coin', 'landing', 'positive-before', 'positive-after', 'finish'].includes(kind)) throw new Error('Неизвестный тип объекта.');
    if ((track.riskZones || []).length >= 4096) throw new Error('Достигнут предел: 4096 наград.');
    collection = 'rewards';
    const positive = kind.startsWith('positive-'), landing = kind === 'landing', finish = kind === 'finish';
    if (finish && analysis.rewards.some(r => r.kind === 'finish')) throw new Error('На трассе уже есть финишный рывок. Допустим один.');
    const radius = 8, halfWidth = finish ? 12 : radius, halfHeight = finish ? 10 : radius;
    if (length < 2 * halfWidth && !landing) throw new Error('Форма нового объекта не помещается в длину трассы.');
    object = { id: uniqueId(document, landing ? 'landing' : positive ? 'positive' : kind), kind: positive ? 'positive' : kind,
      x: clamp(number(x), halfWidth, length - halfWidth), y: clamp(number(y), halfHeight, height - halfHeight), radius };
    if (landing || positive) {
      const obstacle = chooseObstacle(analysis, targetId);
      if (landing && obstacle.kind === 'gate') throw new Error('Посадочная метка требует камень, а не ворота.');
      object.obstacleId = obstacle.id;
      if (landing) {
        const width = Math.min(32, obstacle.width);
        object.x = clamp(number(x), obstacle.x + width / 2, obstacle.x + obstacle.width - width / 2);
        object.y = obstacle.kind === 'floor' ? height - obstacle.height : obstacle.height;
        object.landingWidth = width;
      } else {
        object.placement = kind.endsWith('after') ? 'after' : 'before';
        object.clearX = Math.min(length, actualRight(obstacle) + 15);
      }
    }
    description = landing ? 'Добавлена посадочная метка на выбранном камне.' : positive ? 'Добавлен бонус со связью с выбранным препятствием.' : finish ? 'Добавлен финишный рывок фиксированного размера.' : 'Добавлен объект: ' + kind + '.';
  }
  const path = ['track', collection === 'obstacles' ? 'obstacles' : 'riskZones'];
  return { source: applyText(source, appendItem(source, spans, path, object)), target: { collection, id: object.id }, description };
}

export function deleteObject(source, analysis, target) {
  const spans = scanJson(source), document = JSON.parse(source), track = document.track, zones = track.riskZones || [];
  const obstacle = target.collection === 'obstacles' ? track.obstacles.find(o => o.id === target.id) : null;
  const edits = [], removedIds = [];
  if (target.collection === 'obstacles') {
    if (!obstacle) throw new Error('Препятствие не найдено.');
    const obstacleIndex = track.obstacles.findIndex(o => o.id === target.id);
    removedIds.push(obstacle.id);
    edits.push(removeItems(source, spans, ['track', 'obstacles'], [obstacleIndex]));
    const related = zones.flatMap((z, index) => z.obstacleId === obstacle.id ? [index] : []);
    removedIds.push(...related.map(index => zones[index].id));
    const removeRelated = removeItems(source, spans, ['track', 'riskZones'], related);
    if (removeRelated) edits.push(removeRelated);
  } else if (target.collection === 'rewards') {
    const index = zones.findIndex(z => z.id === target.id);
    if (index < 0) throw new Error('Награда не найдена.');
    removedIds.push(zones[index].id);
    edits.push(removeItems(source, spans, ['track', 'riskZones'], [index]));
  } else throw new Error('Объект нельзя удалить.');
  let description = `Удалено объектов: ${removedIds.length}.`;
  if (obstacle?.hidden && document.warningMode === 'authored' && !track.obstacles.some(o => o.id !== obstacle.id && o.hidden)) {
    const mode = spanAt(spans, ['warningMode']);
    edits.push({ start: mode.start, end: mode.end, text: '"selected"' });
    description += ' Последний скрытый камень удалён; режим предупреждения переключён с authored на selected.';
  }
  return { source: applyText(source, edits), removedIds, description };
}

export function getResizeHandles(analysis, target) {
  if (!target) return [];
  const h = analysis.corridorHeight;
  if (target.collection === 'obstacles') {
    const o = analysis.obstacles.find(o => o.id === target.id); if (!o) return [];
    if (o.kind === 'gate') {
      const band = o.bands[0];
      return [
        { id: 'right', x: band.x + band.width, y: o.gapTop / 2, label: 'Ширина обеих частей ворот', cursor: 'ew-resize' },
        { id: 'gap-top', x: band.x + o.width / 2, y: o.gapTop, label: 'Верхний край проёма', cursor: 'ns-resize' },
        { id: 'gap-bottom', x: o.x + (o.bottomOffsetX || 0) + o.width / 2, y: o.gapBottom, label: 'Нижний край проёма', cursor: 'ns-resize' }
      ];
    }
    const inner = o.kind === 'floor' ? h - o.height : o.height, middle = o.kind === 'floor' ? h - o.height / 2 : o.height / 2;
    return [
      { id: 'right', x: o.x + o.width, y: middle, label: 'Ширина камня', cursor: 'ew-resize' },
      { id: 'inner', x: o.x + o.width / 2, y: inner, label: 'Высота камня', cursor: 'ns-resize' },
      { id: 'corner', x: o.x + o.width, y: inner, label: 'Ширина и высота камня', cursor: o.kind === 'floor' ? 'nesw-resize' : 'nwse-resize' }
    ];
  }
  const r = analysis.rewards.find(r => r.id === target.id); if (!r || r.shape === 'finish') return [];
  if (r.shape === 'landing') return [
    { id: 'left', x: r.left, y: r.planeY, label: 'Левый край посадочной метки', cursor: 'ew-resize' },
    { id: 'right', x: r.right, y: r.planeY, label: 'Правый край посадочной метки', cursor: 'ew-resize' }
  ];
  return [{ id: 'radius', x: r.x + r.radius, y: r.y, label: 'Радиус объекта', cursor: 'ew-resize' }];
}

export function resizeObject(document, analysis, target, handle, x, y) {
  finite(x, y);
  const preview = structuredClone(analysis), changes = [], height = analysis.corridorHeight, length = analysis.length;
  const zones = document.track.riskZones || [];
  const change = (path, before, after) => { if (after !== before) changes.push({ path, value: after }); };
  if (!getResizeHandles(analysis, target).some(h => h.id === handle)) throw new Error('У объекта нет такой ручки размера. Финишный рывок имеет фиксированный размер.');
  if (target.collection === 'obstacles') {
    const index = analysis.obstacles.findIndex(o => o.id === target.id), before = analysis.obstacles[index], after = preview.obstacles[index];
    const rawIndex = document.track.obstacles.findIndex(o => o.id === target.id), raw = document.track.obstacles[rawIndex];
    if (!raw) throw new Error('Препятствие отсутствует в JSON.');
    if (handle === 'right' || handle === 'corner') {
      let minWidth = 1, maxWidth = length - before.x;
      if (before.kind === 'gate') maxWidth = Math.min(...before.bands.map(b => length - b.x));
      // Positive clearX is an authored absolute boundary following the right
      // edge. Zero remains the engine's automatic boundary, never materialized.
      for (const zone of zones) if (zone.obstacleId === before.id && zone.kind === 'positive' && zone.clearX > 0) {
        maxWidth = Math.min(maxWidth, before.width + length - zone.clearX);
        minWidth = Math.max(minWidth, before.width - zone.clearX);
      }
      if (maxWidth < minWidth) throw new Error('Для ширины 1 не хватает места до края трассы.');
      const origin = before.kind === 'gate' ? before.x + (before.topOffsetX || 0) : before.x;
      after.width = clamp(number(x - origin), minWidth, maxWidth);
      change(['track', 'obstacles', rawIndex, 'width'], raw.width, after.width);
    }
    if (before.kind === 'gate') {
      if (handle === 'gap-top') {
        if (before.gapBottom < 2) throw new Error('Сначала опустите нижний край проёма: для верхней части и проёма нужно по 1.');
        after.gapTop = clamp(number(y), 1, before.gapBottom - 1);
      }
      if (handle === 'gap-bottom') {
        if (before.gapTop > height - 2) throw new Error('Сначала поднимите верхний край проёма: для нижней части и проёма нужно по 1.');
        after.gapBottom = clamp(number(y), before.gapTop + 1, height - 1);
      }
      change(['track', 'obstacles', rawIndex, 'gapTop'], raw.gapTop, after.gapTop);
      change(['track', 'obstacles', rawIndex, 'gapBottom'], raw.gapBottom, after.gapBottom);
    } else if (handle === 'inner' || handle === 'corner') {
      after.height = clamp(number(before.kind === 'floor' ? height - y : y), 1, height);
      change(['track', 'obstacles', rawIndex, 'height'], raw.height, after.height);
    }
    for (const band of after.bands) {
      band.width = after.width;
      if (after.kind !== 'gate') { band.y = after.kind === 'floor' ? height - after.height : 0; band.height = after.height; }
      else if (band.index === 0) band.height = after.gapTop;
      else { band.y = after.gapBottom; band.height = height - after.gapBottom; }
    }
    const rightDelta = actualRight(after) - actualRight(before);
    for (let i = 0; i < preview.rewards.length; i++) {
      const reward = preview.rewards[i]; if (reward.obstacleId !== after.id) continue;
      const z = zones.findIndex(z => z.id === reward.id), original = zones[z];
      if (reward.shape === 'landing') {
        const width = after.width === before.width ? original.landingWidth : Math.min(original.landingWidth, after.width);
        if (after.width !== before.width) reward.x = clamp(reward.x, after.x + width / 2, after.x + after.width - width / 2);
        reward.left = Math.max(after.x, reward.x - width / 2); reward.right = Math.min(after.x + after.width, reward.x + width / 2);
        reward.planeY = after.kind === 'floor' ? height - after.height : after.height;
        // Y is legacy data for landing. Its real plane comes from the stone.
        change(['track', 'riskZones', z, 'x'], original.x, reward.x);
        change(['track', 'riskZones', z, 'landingWidth'], original.landingWidth, width);
      } else if (reward.kind === 'positive' && original.clearX > 0 && rightDelta !== 0) {
        reward.clearX = original.clearX + rightDelta;
        change(['track', 'riskZones', z, 'clearX'], original.clearX, reward.clearX);
      }
    }
  } else {
    const i = analysis.rewards.findIndex(r => r.id === target.id), before = analysis.rewards[i], after = preview.rewards[i];
    const rawIndex = zones.findIndex(r => r.id === target.id), raw = zones[rawIndex];
    if (before.shape === 'landing') {
      const stone = analysis.obstacles.find(o => o.id === before.obstacleId);
      if (!stone || stone.kind === 'gate' || stone.width < 1) throw new Error('Посадочная метка требует камень шириной не меньше 1.');
      let left = clamp(before.left, stone.x, stone.x + stone.width - 1), right = clamp(before.right, left + 1, stone.x + stone.width);
      if (handle === 'left') left = clamp(number(x), stone.x, right - 1);
      else right = clamp(number(x), left + 1, stone.x + stone.width);
      after.x = (left + right) / 2; after.left = left; after.right = right;
      change(['track', 'riskZones', rawIndex, 'x'], raw.x, after.x);
      change(['track', 'riskZones', rawIndex, 'landingWidth'], raw.landingWidth, right - left);
    } else {
      const maxRadius = Math.min(240, before.x, length - before.x, before.y, height - before.y);
      if (maxRadius < 1) throw new Error('Радиус 1 не помещается: сначала отодвиньте центр от края.');
      after.radius = clamp(number(Math.hypot(x - before.x, y - before.y)), 1, maxRadius);
      if (!Object.hasOwn(raw, 'radius')) changes.push({ path: ['track', 'riskZones', rawIndex, 'radius'], value: after.radius, insert: true });
      else change(['track', 'riskZones', rawIndex, 'radius'], raw.radius, after.radius);
    }
  }
  return { analysis: preview, changes, description: 'Размер изменён; связанные посадочные метки и граница clearX сохранены.' };
}
