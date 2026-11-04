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
