import { clipToleranceSamples } from './input-tolerance.mjs';

export const MANEUVER_OFFSETS = Array.from({ length: 25 }, (_, i) => i * 50 - 600);
export const countValue = value => typeof value === 'string' && /^(0|[1-9]\d*)$/.test(value) ? BigInt(value) : null;
export function countFraction(safe, valid) {
  if (!valid) return null;
  const ratio = Number(safe) / Number(valid);
  return Number.isFinite(ratio) ? ratio : Number(safe * 1000000000000000n / valid) / 1e15;
}
export const cellKey = cell => `${cell.inputIndex}:${cell.offsetMs}`;

export function maneuverCounts(report) {
  const keys = ['total', 'cursor', 'valid', 'computed', 'safe'];
  if (!report || keys.some(key => countValue(report[key]) === null)) return null;
  const values = Object.fromEntries(keys.map(key => [key, countValue(report[key])]));
  if (values.cursor > values.total || values.valid > values.cursor || values.computed !== values.valid || values.safe > values.valid) return null;
  return values;
}

export function conditionalCell(report, inputIndex, offsetMs) {
  return report?.cells?.find(cell => cell.inputIndex === inputIndex && cell.offsetMs === offsetMs) ?? null;
}

export function cellProjection(cell, complete) {
  if (!cell || ['total', 'valid', 'computed', 'safe'].some(key => countValue(cell[key]) === null)) return { state: 'pending', fraction: null };
  const valid = countValue(cell.valid), safe = countValue(cell.safe), computed = countValue(cell.computed);
  if (computed !== valid || safe > valid) return { state: 'pending', fraction: null };
  const fraction = countFraction(safe, valid);
  return { state: complete ? valid ? 'ready' : 'empty' : 'pending', fraction, valid, safe,
    provisional: !complete, witness: safe > 0n && Array.isArray(cell.witnessOffsetsMs) ? cell.witnessOffsetsMs : null };
}

export function matchesManeuverTarget(target, inputCount) {
  const indices = target?.inputIndices;
  return target?.kind === 'maneuver' && target.inputIndex === indices?.[0] && target.firstInputIndex === indices?.[0]
    && Array.isArray(indices) && indices.length > 0
    && indices.every((index, i) => Number.isInteger(index) && index >= 0 && index < inputCount && (!i || index === indices[i - 1] + 1))
    && (target.maneuver == null || target.maneuver.rulesVersion === 2);
}

export function maneuverRequest(snapshot, target, selection) {
  const { profile, warningDuration, surfaceRunSettings, axis, inputs } = snapshot.result;
  return { json: snapshot.editorSource, profile, warningDuration, surfaceRunSettings, axis, inputs: inputs.map(input => input.value), inputIndex: selection?.inputIndex ?? target.inputIndex,
    ...(selection ? { offsetMs: selection.offsetMs, offsetsMs: selection.witnessOffsetsMs } : {}) };
}

export function maneuverOverlay(preview, target, selected) {
  const offsets = selected?.witnessOffsetsMs, report = target?.maneuver;
  if (preview?.status !== 'safe' || !report?.baseline || !Array.isArray(offsets) || offsets.length !== target.inputIndices.length
    || !Array.isArray(preview.offsetsMs) || !offsets.every((offset, i) => offset === preview.offsetsMs[i])
    || offsets[target.inputIndices.indexOf(selected.inputIndex)] !== selected.offsetMs
    || !Array.isArray(preview.inputs) || preview.inputs.length !== report.baseline.inputs.length
    || !preview.inputs.every((time, index) => Math.abs(time - (report.baseline.inputs[index]
      + (target.inputIndices.includes(index) ? offsets[target.inputIndices.indexOf(index)] / 1000 : 0))) < 1e-7)) return null;
  const points = preview.pressPoints;
  if (!Array.isArray(points)) return null;
  const commands = target.inputIndices.map(index => points.find(point => point.index === index));
  if (commands.some(point => !point || point.status !== 'accepted' || ![point.time, point.x, point.y].every(Number.isFinite) || Math.abs(point.time - preview.inputs[point.index]) > 1e-7)) return null;
  const changed = new Map(target.inputIndices.map((index, i) => [index, preview.inputs[index]]));
  const probe = { status: 'safe', startTime: preview.startTime ?? preview.samples?.[0]?.time, endTime: preview.endTime ?? preview.samples?.at(-1)?.time };
  const overlay = clipToleranceSamples(probe, preview.samples, report.baseline.inputs, changed);
  return overlay && { ...overlay, commands, offsetsMs: [...offsets] };
}
