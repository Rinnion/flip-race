import { sampleAt } from './model.mjs';
import { sameRunSettings } from './run-settings.mjs';

export const TOLERANCE_STEP_MS = 50;
export const TOLERANCE_SPAN_MS = 300;
export const toleranceOffsets = (span = TOLERANCE_SPAN_MS) => Array.from({ length: span * 2 / TOLERANCE_STEP_MS + 1 }, (_, index) =>
  -span + index * TOLERANCE_STEP_MS);
export const TOLERANCE_OFFSETS_MS = toleranceOffsets();
export const TOLERANCE_PROBE_COUNT = TOLERANCE_OFFSETS_MS.length + 1;
const statuses = new Set(['safe', 'collision', 'rejected', 'invalid', 'incomplete']);
const epsilon = 1e-7;
const nearTime = (left, right) => Number.isFinite(left) && Number.isFinite(right) && Math.abs(left - right) <= epsilon;
const nullableNumber = value => value === null || Number.isFinite(value);

export function inputToleranceRequest({ provenance, result, inputIndex, spanMs }, probe) {
  const { profile, warningDuration, surfaceRunSettings, axis, inputs } = result;
  const request = { json: provenance.source, profile, warningDuration, surfaceRunSettings, axis,
    inputs: inputs.map(input => input.value), inputIndex };
  if (spanMs !== undefined) request.spanMs = spanMs;
  if (probe) Object.assign(request, { offsetMs: probe.offsetMs, omit: probe.omit });
  return request;
}

export function isMatchingAnalysisTarget(target, { inputIndex, result }) {
  if (target?.inputIndex !== inputIndex || typeof target.message !== 'string') return false;
  if (target.kind === 'pair') {
    const first = target.firstInputIndex;
    return Number.isInteger(first) && first >= 0 && first + 1 < result.inputs.length
      && (inputIndex === first || inputIndex === first + 1);
  }
  return (target.kind === 'single' || (target.kind === 'unsupported' && target.message.length > 0))
    && target.firstInputIndex === null;
}

export async function resolveAnalysisTarget(requests, snapshot, context, post) {
  const ticket = requests.start('target', snapshot);
  const current = () => requests.current(ticket, context());
  try {
    const target = await post('/api/analysis-target', inputToleranceRequest(snapshot), ticket.abort.signal);
    if (!current()) return null;
    if (!isMatchingAnalysisTarget(target, snapshot)) throw new Error('Ответ не совпал с выбранной командой.');
    return target;
  } catch (error) {
    if (current()) throw error;
    return null;
  } finally {
    requests.finish(ticket);
  }
}

export function matrixCounts(cells, matches = () => true) {
  const counts = { total: cells.length, computed: 0, safe: 0, collision: 0, invalid: 0, rejected: 0, incomplete: 0,
    pending: 0, matching: 0, matchingSafe: 0, matchingCollision: 0 };
  for (const cell of cells) {
    if (!cell?.computed) {
      counts.pending++;
      continue;
    }
    counts.computed++;
    counts[cell.status]++;
    if (matches(cell)) {
      counts.matching++;
      if (cell.status === 'safe') counts.matchingSafe++;
      if (cell.status === 'collision') counts.matchingCollision++;
    }
  }
  return counts;
}

export function matrixShares({ total, safe, collision, invalid }) {
  const adjudicated = safe + collision;
  return { safe: total ? safe / total : null, collision: total ? collision / total : null,
    invalid: total ? invalid / total : null, safeAmongDecided: adjudicated ? safe / adjudicated : null,
    collisionAmongDecided: adjudicated ? collision / adjudicated : null };
}

export function singleMatrixCells(report) {
  const offsets = report?.spanMs === 600 ? toleranceOffsets(600) : TOLERANCE_OFFSETS_MS;
  return offsets.map((firstOffsetMs, index) => {
    const probe = report?.probes[index];
    const computed = report?.status === 'ready' && !!probe
      && (probe.computed ?? (probe.status !== 'incomplete' || probe.endTime !== null));
    return { ...probe, firstOffsetMs, secondOffsetMs: 0, computed };
  });
}

function matchesProbe(probe, offsetMs, omit) {
  return probe && probe.offsetMs === offsetMs && probe.omit === omit && statuses.has(probe.status)
    && typeof probe.message === 'string' && nullableNumber(probe.inputTime) && nullableNumber(probe.startTime)
    && nullableNumber(probe.endTime) && nullableNumber(probe.endX)
    && (probe.startTime === null || probe.endTime === null || probe.endTime >= probe.startTime - epsilon)
    && Number.isInteger(probe.collisions) && probe.collisions >= 0
    && nullableNumber(probe.minimumIntervalMs) && typeof probe.botCompatible === 'boolean';
}

export function isMatchingToleranceReport(report, snapshot) {
  const span = snapshot.spanMs ?? TOLERANCE_SPAN_MS;
  const offsets = toleranceOffsets(span);
  if (!['ready', 'unsupported'].includes(report?.status) || report.inputIndex !== snapshot.inputIndex
    || report.stepMs !== TOLERANCE_STEP_MS || report.spanMs !== span
    || !Number.isInteger(report.attempted) || report.attempted < 0 || report.attempted > offsets.length + 1
    || typeof report.complete !== 'boolean' || typeof report.stopReason !== 'string' || typeof report.message !== 'string'
    || !Array.isArray(report.probes)) return false;

  if (report.status === 'unsupported' && !report.probes.length)
    return report.baseline === null && report.omission === null && report.attempted === 0;
  if (report.probes.length !== offsets.length
    || !offsets.every((offset, index) => matchesProbe(report.probes[index], offset, false))
    || !matchesProbe(report.omission, null, true)) return false;
  const probes = [...report.probes, report.omission];
  if (report.status === 'unsupported') return report.baseline === null && !report.complete
    && report.attempted === 0 && probes.every(probe => probe.status === 'incomplete');
  if (report.attempted !== probes.filter(probe => ['safe', 'collision', 'rejected'].includes(probe.status)).length) return false;

  const { baseline } = report;
  const { axis, inputs } = snapshot.result;
  if (!baseline || !Array.isArray(baseline.inputs) || baseline.inputs.length !== inputs.length
    || !baseline.inputs.every((time, index) => nearTime(time, axis === 'time' ? inputs[index].value : inputs[index].time))
    || !nearTime(baseline.inputTime, inputs[snapshot.inputIndex]?.time)
    || !Number.isFinite(baseline.startTime) || !Number.isFinite(baseline.endTime) || !Number.isFinite(baseline.endX)
    || baseline.startTime > baseline.inputTime || baseline.inputTime > baseline.endTime) return false;
  if (report.omission.inputTime !== null) return false;
  return report.probes.every(probe => probe.inputTime === null
    ? probe.status !== 'safe'
    : nearTime(probe.inputTime, baseline.inputs[snapshot.inputIndex] + probe.offsetMs / 1000));
}

export function adjacentSafeProbes(report) {
  const probes = report.probes;
  const zero = probes.findIndex(probe => probe.offsetMs === 0);
  if (probes[zero]?.status !== 'safe') return null;

  let first = zero, last = zero;
  while (probes[first - 1]?.status === 'safe') first--;
  while (probes[last + 1]?.status === 'safe') last++;
  return { earlierMs: Math.abs(probes[first].offsetMs), laterMs: probes[last].offsetMs,
    disconnected: probes.filter(probe => probe.status === 'safe').length - (last - first + 1) };
}

// Clip to the affected interval. Some servers include the shared prefix in the samples.
export function toleranceOverlay(preview, candidate, baseline, index) {
  const { probe, samples } = preview ?? {};
  if (!matchesProbe(probe, candidate.offsetMs, candidate.omit) || !Array.isArray(samples)
    || (probe.status !== candidate.status && probe.status !== 'incomplete' && candidate.status !== 'incomplete')
    || (candidate.inputTime !== null && probe.inputTime !== null && !nearTime(probe.inputTime, candidate.inputTime))) return null;
  return clipToleranceSamples(probe, samples, baseline.inputs, new Map([[index, probe.inputTime]]));
}

export function clipToleranceSamples(probe, samples, inputTimes, changedTimes) {
  if (!Array.isArray(samples) || !samples.every((sample, index) => Number.isFinite(sample?.time)
    && Number.isFinite(sample.x) && Number.isFinite(sample.y) && (!index || sample.time >= samples[index - 1].time))) return null;

  const start = Math.max(probe.startTime ?? Infinity, samples[0]?.time ?? Infinity);
  const end = Math.min(probe.endTime ?? -Infinity, samples.at(-1)?.time ?? -Infinity);
  const local = start < end
    ? [sampleAt(samples, start), ...samples.filter(sample => sample.time > start && sample.time < end), sampleAt(samples, end)]
    : [];
  const path = local.map(({ x, y }, index) => `${index ? 'L' : 'M'}${x.toFixed(3)},${y.toFixed(3)}`).join(' ');
  const point = !path && nearTime(start, end) ? sampleAt(samples, end) : null;
  const commands = [];
  for (const [index, originalTime] of inputTimes.entries()) {
    const changed = changedTimes.has(index);
    const time = changed ? changedTimes.get(index) : originalTime;
    // For unchanged inputs, the final sample may be the boundary before dispatch.
    if (!Number.isFinite(time) || time < start || (changed ? time > end : time >= end)) continue;
    const { x, y } = sampleAt(samples, time);
    commands.push({ index, time, x, y });
  }
  return { probe, path, point, commands, startX: local[0]?.x ?? point?.x ?? null };
}

function sameReplayContext(original, current) {
  return original.result === current.result && original.provenance === current.provenance
    && original.editorSource === current.editorSource && original.inputText === current.inputText
    && original.axis === current.axis && original.loadRevision === current.loadRevision
    && sameRunSettings(original.settings, current.settings);
}

export class ToleranceRequestState {
  pending = null;

  start(kind, snapshot) {
    this.cancel();
    this.pending = { kind, snapshot, abort: new AbortController() };
    return this.pending;
  }

  matches(snapshot, context) { return sameReplayContext(snapshot, context); }

  current(ticket, context) {
    return this.pending === ticket && !ticket.abort.signal.aborted && this.matches(ticket.snapshot, context);
  }

  finish(ticket) {
    if (this.pending === ticket) this.pending = null;
  }

  cancel() {
    this.pending?.abort.abort();
    this.pending = null;
  }
}
