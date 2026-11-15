import { sampleAt } from './model.mjs';
import { sameRunSettings } from './run-settings.mjs';

export const TOLERANCE_STEP_MS = 50;
export const TOLERANCE_SPAN_MS = 300;
const offsets = Array.from({ length: TOLERANCE_SPAN_MS * 2 / TOLERANCE_STEP_MS + 1 }, (_, index) =>
  -TOLERANCE_SPAN_MS + index * TOLERANCE_STEP_MS);
export const TOLERANCE_PROBE_COUNT = offsets.length + 1;
const statuses = new Set(['safe', 'collision', 'rejected', 'invalid', 'incomplete']);
const epsilon = 1e-7;
const nearTime = (left, right) => Number.isFinite(left) && Number.isFinite(right) && Math.abs(left - right) <= epsilon;
const nullableNumber = value => value === null || Number.isFinite(value);

export function inputToleranceRequest({ provenance, result, inputIndex }, probe) {
  const { profile, warningDuration, surfaceRunSettings, axis, inputs } = result;
  const request = { json: provenance.source, profile, warningDuration, surfaceRunSettings, axis,
    inputs: inputs.map(input => input.value), inputIndex };
  if (probe) Object.assign(request, { offsetMs: probe.offsetMs, omit: probe.omit });
  return request;
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
  if (!['ready', 'unsupported'].includes(report?.status) || report.inputIndex !== snapshot.inputIndex
    || report.stepMs !== TOLERANCE_STEP_MS || report.spanMs !== TOLERANCE_SPAN_MS
    || !Number.isInteger(report.attempted) || report.attempted < 0 || report.attempted > TOLERANCE_PROBE_COUNT
    || typeof report.complete !== 'boolean' || typeof report.stopReason !== 'string' || typeof report.message !== 'string'
    || !Array.isArray(report.probes)) return false;

  if (report.status === 'unsupported' && !report.probes.length)
    return report.baseline === null && report.omission === null && report.attempted === 0;
  if (report.probes.length !== offsets.length || !offsets.every((offset, index) => matchesProbe(report.probes[index], offset, false))
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
export function toleranceOverlay(preview, candidate) {
  const { probe, samples } = preview ?? {};
  if (!matchesProbe(probe, candidate.offsetMs, candidate.omit) || !Array.isArray(samples)
    || (probe.status !== candidate.status && probe.status !== 'incomplete' && candidate.status !== 'incomplete')
    || (candidate.inputTime !== null && probe.inputTime !== null && !nearTime(probe.inputTime, candidate.inputTime))) return null;
  return clipToleranceSamples(probe, samples);
}

export function clipToleranceSamples(probe, samples) {
  if (!Array.isArray(samples) || !samples.every((sample, index) => Number.isFinite(sample?.time)
    && Number.isFinite(sample.x) && Number.isFinite(sample.y) && (!index || sample.time >= samples[index - 1].time))) return null;

  const start = Math.max(probe.startTime ?? Infinity, samples[0]?.time ?? Infinity);
  const end = Math.min(probe.endTime ?? -Infinity, samples.at(-1)?.time ?? -Infinity);
  const local = start < end
    ? [sampleAt(samples, start), ...samples.filter(sample => sample.time > start && sample.time < end), sampleAt(samples, end)]
    : [];
  const path = local.map(({ x, y }, index) => `${index ? 'L' : 'M'}${x.toFixed(3)},${y.toFixed(3)}`).join(' ');
  const point = !path && nearTime(start, end) ? sampleAt(samples, end) : null;
  return { probe, path, point, startX: local[0]?.x ?? point?.x ?? null };
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
