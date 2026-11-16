import { clipToleranceSamples, inputToleranceRequest, matrixCounts, TOLERANCE_STEP_MS } from './input-tolerance.mjs';

export const PAIR_SPANS = [300, 600, 1000];
const statuses = ['safe', 'collision', 'rejected', 'invalid', 'incomplete'];
const epsilon = 1e-7;
const near = (a, b) => Number.isFinite(a) && Number.isFinite(b) && Math.abs(a - b) <= epsilon;
const nullable = n => n === null || Number.isFinite(n);

export function pairAxis(span) {
  return Array.from({ length: span * 2 / TOLERANCE_STEP_MS + 1 }, (_, index) => index * TOLERANCE_STEP_MS - span);
}

export const pairKey = ({ firstOffsetMs, secondOffsetMs }) => `${firstOffsetMs},${secondOffsetMs}`;

export function pairRequest(snapshot, selection) {
  const { inputIndex, ...source } = inputToleranceRequest({ ...snapshot, inputIndex: snapshot.firstInputIndex });
  return { ...source, firstInputIndex: inputIndex, ...selection };
}

export function matchesPairMinimum(cell, minimum) {
  return minimum === 0 || (cell?.computed && Number.isFinite(cell.minimumIntervalMs) && cell.minimumIntervalMs >= minimum);
}

export class PairGrid {
  spanMs = PAIR_SPANS[0];
  cells = new Map();
  baseline = null;
  unsupported = '';

  get offsets() {
    const axis = pairAxis(this.spanMs);
    return axis.flatMap(secondOffsetMs => axis.map(firstOffsetMs => ({ firstOffsetMs, secondOffsetMs })));
  }

  missing() {
    const radius = cell => Math.max(Math.abs(cell.firstOffsetMs), Math.abs(cell.secondOffsetMs));
    return this.offsets.filter(offset => !this.cells.get(pairKey(offset))?.computed).sort((a, b) => radius(a) - radius(b));
  }

  apply(report) {
    if (report.status === 'unsupported') {
      if (report.complete) this.unsupported = report.message;
      return;
    }
    this.baseline = report.baseline;
    for (const cell of report.cells) this.cells.set(pairKey(cell), cell);
  }

  counts(minimum) {
    return matrixCounts(this.offsets.map(offset => this.cells.get(pairKey(offset))), cell => matchesPairMinimum(cell, minimum));
  }
}

function matchesCell(cell, offset, baseline, index) {
  if (!cell || cell.firstOffsetMs !== offset.firstOffsetMs || cell.secondOffsetMs !== offset.secondOffsetMs
    || !statuses.includes(cell.status) || typeof cell.computed !== 'boolean' || typeof cell.message !== 'string'
    || (!cell.computed && cell.status !== 'incomplete') || !Number.isInteger(cell.collisions) || cell.collisions < 0
    || !['firstInputTime', 'secondInputTime', 'pairIntervalMs', 'minimumIntervalMs', 'startTime', 'endTime', 'endX']
      .every(key => nullable(cell[key]))) return false;
  if (cell.startTime !== null && cell.endTime !== null && cell.endTime < cell.startTime - epsilon) return false;
  if (!baseline) return true;
  const first = baseline.inputs[index] + offset.firstOffsetMs / 1000;
  const second = baseline.inputs[index + 1] + offset.secondOffsetMs / 1000;
  return (cell.firstInputTime === null || near(cell.firstInputTime, first))
    && (cell.secondInputTime === null || near(cell.secondInputTime, second));
}

export function matchesPairReport(report, snapshot, spanMs, offsets, previousBaseline) {
  if (!['ready', 'unsupported'].includes(report?.status) || report.firstInputIndex !== snapshot.firstInputIndex
    || report.spanMs !== spanMs || report.stepMs !== TOLERANCE_STEP_MS || typeof report.message !== 'string'
    || typeof report.complete !== 'boolean' || report.stopReason !== (report.complete ? 'completed' : 'time-budget')
    || !Array.isArray(report.cells)) return false;
  if (report.status === 'unsupported') return report.baseline === null && report.cells.length === 0;
  const { baseline } = report, { firstInputIndex: index, result } = snapshot;
  if (!Array.isArray(baseline?.inputs) || baseline.inputs.length !== result.inputs.length
    || !baseline.inputs.every((time, i) => near(time, result.axis === 'time' ? result.inputs[i].value : result.inputs[i].time))
    || !near(baseline.firstInputTime, result.inputs[index]?.time) || !near(baseline.secondInputTime, result.inputs[index + 1]?.time)
    || !Number.isFinite(baseline.startTime) || !Number.isFinite(baseline.endTime) || !Number.isFinite(baseline.endX)
    || baseline.startTime > baseline.firstInputTime || baseline.secondInputTime > baseline.endTime) return false;
  if (previousBaseline && !['startTime', 'endTime', 'endX'].every(key => near(baseline[key], previousBaseline[key]))) return false;
  return report.cells.length === offsets.length
    && report.cells.every((cell, i) => matchesCell(cell, offsets[i], baseline, index))
    && report.complete === report.cells.every(cell => cell.computed);
}

export function pairOverlay(preview, candidate, baseline, index) {
  const { cell, samples } = preview ?? {};
  if (!matchesCell(cell, candidate, baseline, index) || (candidate.computed && cell.computed && candidate.status !== cell.status))
    return null;
  return clipToleranceSamples(cell, samples);
}
