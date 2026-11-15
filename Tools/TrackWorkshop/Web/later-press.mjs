import { sameRunSettings } from './run-settings.mjs';

const nearTime = (left, right) => Number.isFinite(left) && Number.isFinite(right) && Math.abs(left - right) <= 1e-7;
const summaryCounts = ['collisions', 'boosts', 'instantBoosts', 'slows', 'coins', 'finishBoosts',
  'acceptedInputs', 'ignoredInputs', 'unreachedInputs'];
const summaryIds = ['hitIds', 'failedIds', 'successfulIds', 'collectedIds'];

export function laterPressRequest({ source, result, inputIndex, minimumMs }) {
  const { profile, warningDuration, surfaceRunSettings, axis, inputs } = result;
  return { json: source, profile, warningDuration, surfaceRunSettings, axis, inputs: inputs.map(input => input.value), inputIndex, minimumMs };
}

export function isMatchingLaterPressReport(report, snapshot) {
  if (!['ready', 'unsupported'].includes(report?.status) || !Array.isArray(report.alternatives)
    || !Number.isInteger(report.attempted) || !Number.isInteger(report.candidateCount)
    || report.attempted < 0 || report.attempted > report.candidateCount || report.alternatives.length > report.attempted
    || typeof report.complete !== 'boolean' || !Number.isInteger(report.stepMs) || report.stepMs <= 0
    || !Number.isInteger(report.spanMs) || report.spanMs < 0) return false;
  if (report.status === 'unsupported') return report.alternatives.length === 0 && typeof report.message === 'string';

  const { baseline } = report;
  const { result, inputIndex, minimumMs } = snapshot;
  const landing = result.landingIntervals.find(item => item.inputIndex === inputIndex);
  if (!baseline || !landing || baseline.inputIndex !== inputIndex || !nearTime(baseline.landingTime, landing.landingTime)
    || !nearTime(baseline.inputTime, landing.inputTime) || !nearTime(baseline.finishTime, result.summary.finishTime)
    || baseline.collisions !== result.summary.collisions || !Array.isArray(baseline.inputs)
    || baseline.inputs.length !== result.inputs.length
    || !baseline.inputs.every((time, index) => nearTime(time, result.inputs[index].time))) return false;

  return report.alternatives.every(candidate => {
    const { delayMs, inputTime, finishTime, finishDelta, collisions, inputs, summary } = candidate ?? {};
    return Number.isInteger(delayMs) && delayMs >= minimumMs && delayMs <= minimumMs + report.spanMs
      && (delayMs - minimumMs) % report.stepMs === 0 && inputTime > baseline.inputTime
      && nearTime(inputTime, baseline.landingTime + delayMs / 1000)
      && nearTime(finishTime, summary?.finishTime) && nearTime(finishDelta, finishTime - baseline.finishTime)
      && Number.isInteger(collisions) && collisions === summary.collisions && collisions <= baseline.collisions
      && Array.isArray(inputs) && inputs.length === baseline.inputs.length
      && inputs.every((time, index) => nearTime(time, index === inputIndex ? inputTime : baseline.inputs[index]));
  });
}

export function isMatchingLaterPressReplay(result, candidate, snapshot) {
  const { summary } = result;
  const expected = candidate.summary;
  const landing = result.landingIntervals.find(item => item.inputIndex === snapshot.inputIndex);
  return result.axis === 'time' && sameRunSettings(result, snapshot.result)
    && summary.status === 'Finished' && nearTime(summary.finishTime, candidate.finishTime)
    && summary.acceptedInputs === candidate.inputs.length && summary.ignoredInputs === 0 && summary.unreachedInputs === 0
    && summaryCounts.every(key => summary[key] === expected[key])
    && summaryIds.every(key => JSON.stringify(summary[key]) === JSON.stringify(expected[key]))
    && nearTime(landing?.inputTime, candidate.inputTime)
    && nearTime(landing?.landingTime, snapshot.result.landingIntervals.find(item => item.inputIndex === snapshot.inputIndex)?.landingTime);
}

// Each request is tied to the visible replay and the exact editor values at launch.
export class LaterPressRequestState {
  pending = null;

  start(kind, context) {
    this.cancel();
    this.pending = { kind, context, abort: new AbortController() };
    return this.pending;
  }

  current(ticket, context) {
    const original = ticket.context;
    return this.pending === ticket && !ticket.abort.signal.aborted && original.result === context.result
      && original.source === context.source && original.inputText === context.inputText && original.axis === context.axis
      && original.minimumMs === context.minimumMs && original.loadRevision === context.loadRevision
      && sameRunSettings(original.settings, context.settings);
  }

  finish(ticket) {
    if (this.pending === ticket) this.pending = null;
  }

  cancel() {
    this.pending?.abort.abort();
    this.pending = null;
  }
}
