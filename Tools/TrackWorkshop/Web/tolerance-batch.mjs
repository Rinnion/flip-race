import { TOLERANCE_STEP_MS } from './input-tolerance.mjs';
import { matchesManeuverTarget, maneuverCounts, countFraction } from './maneuver-tolerance.mjs';
import { sameRunSettings } from './run-settings.mjs';

export const BATCH_SPAN_MS = 600;

export function batchRequest({ editorSource, result }) {
  const { profile, warningDuration, surfaceRunSettings, axis, inputs } = result;
  return { json: editorSource, profile, warningDuration, surfaceRunSettings, axis, inputs: inputs.map(input => input.value) };
}

export function matchesBatchReplay(batch, result) {
  if (!Array.isArray(result?.inputs) || batch?.spanMs !== BATCH_SPAN_MS || batch.stepMs !== TOLERANCE_STEP_MS || batch.axis !== result.axis
    || !Array.isArray(batch.inputs) || batch.inputs.length !== result.inputs.length
    || !batch.inputs.every((value, index) => value === result.inputs[index].value)
    || !Array.isArray(batch.targets) || batch.commandTargetIndices?.length !== result.inputs.length
    || !Array.isArray(batch.commandTargetIndices) || !batch.commandTargetIndices.every((targetIndex, index) => {
      const target = batch.targets[targetIndex];
      if (!Number.isInteger(targetIndex) || !target || !Number.isInteger(target.inputIndex)) return false;
      if (target.kind === 'maneuver') return matchesManeuverTarget(target, result.inputs.length) && target.inputIndices.includes(index);
      return target.kind === 'unsupported' && target.inputIndex === index;
    })) return false;
  try { return sameRunSettings(batch, result); } catch { return false; }
}

export function batchCoverage(target) {
  if (target?.kind === 'unsupported') return { state: 'unsupported' };
  const report = target?.maneuver, counts = maneuverCounts(report);
  if (report?.status === 'unsupported') return { state: 'unsupported' };
  if (!counts || report.rulesVersion !== 2 || report.spanMs !== 600 || report.stepMs !== 50)
    return { state: 'pending', computed: 0n, cursor: 0n, valid: 0n, safe: 0n, fraction: null, total: 25n ** BigInt(target?.inputIndices?.length ?? 1) };
  const fraction = countFraction(counts.safe, counts.valid);
  return { ...counts, fraction, state: report.complete && counts.cursor === counts.total ? 'ready' : 'pending' };
}

export function toleranceScore(fraction) {
  return fraction === null ? null : Math.max(0, Math.min(fraction, 1));
}

export function toleranceColor(fraction) {
  const score = toleranceScore(fraction);
  const hues = [[176, 74, 67], [167, 118, 32], [47, 121, 81]];
  const side = score < .5 ? 0 : 1, t = score * 2 - side;
  return `rgb(${hues[side].map((value, i) => Math.round(value + t * (hues[side + 1][i] - value))).join(' ')})`;
}

export async function readBatchResponse(response, { signal, onPlan = () => {}, onProgress = () => {} } = {}) {
  signal?.throwIfAborted();
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    signal?.throwIfAborted();
    throw new Error(body?.error || `Ошибка сервера: ${response.status}`);
  }
  const unexpected = 'Сервер вернул неожиданный ответ пакетного анализа.';
  if (!response.body) throw new Error(unexpected);
  const reader = response.body.getReader(), decoder = new TextDecoder('utf-8', { fatal: true });
  const cancel = () => { void reader.cancel().catch(() => {}); };
  let buffer = '';
  signal?.addEventListener('abort', cancel, { once: true });

  function consume(line) {
    if (!line.trim()) return;
    let event;
    try { event = JSON.parse(line); } catch { throw new Error(unexpected); }
    if (event?.type === 'error' && typeof event.error === 'string') throw new Error(event.error);
    if (event?.type === 'result' && event.batch && typeof event.canonicalJson === 'string') return event;
    if (event?.type === 'plan' && Array.isArray(event.batch?.targets) && Array.isArray(event.checkpoints)) onPlan(event);
    else if (event?.type === 'progress' && Number.isInteger(event.completed) && Number.isInteger(event.total)
      && event.completed >= 0 && event.completed <= event.total) onProgress(event);
    else throw new Error(unexpected);
  }
  try {
    while (true) {
      const { done, value } = await reader.read();
      signal?.throwIfAborted();
      buffer += decoder.decode(value, { stream: !done });
      let start = 0;
      for (let end = buffer.indexOf('\n'); end !== -1; end = buffer.indexOf('\n', start)) {
        const result = consume(buffer.slice(start, end));
        signal?.throwIfAborted();
        if (result) return result;
        start = end + 1;
      }
      buffer = buffer.slice(start);
      if (done) {
        const result = consume(buffer);
        signal?.throwIfAborted();
        if (result) return result;
        throw new Error('Ответ сервера оборвался до сохранения матриц.');
      }
    }
  } finally {
    signal?.removeEventListener('abort', cancel);
    cancel();
    reader.releaseLock();
  }
}
