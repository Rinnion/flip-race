import { batchCoverage, matchesBatchReplay, toleranceScore } from './tolerance-batch.mjs';

export const LOW_TOLERANCE_FRACTION = .07;

export function canLocateCommand(input) {
  return !!input && input.status !== 'unreached' && [input.time, input.x, input.y].every(Number.isFinite);
}

function statistics(values) {
  const sorted = values.filter(Number.isFinite).sort((a, b) => a - b), count = sorted.length;
  return { count, minimum: count ? sorted[0] : null, maximum: count ? sorted.at(-1) : null,
    median: count ? (sorted[Math.floor((count - 1) / 2)] + sorted[Math.floor(count / 2)]) / 2 : null };
}

function safeWindow(target, coverage) {
  if (coverage.state !== 'ready' || !coverage.safe) return null;
  const window = target.maneuver?.safeWindow;
  return window && [window.earliestMs, window.latestMs].every(Number.isFinite)
    ? { earliestMs: window.earliestMs, latestMs: window.latestMs } : null;
}

export function replaySummary(replay, candidateBatch = null, coverageOf = batchCoverage) {
  const batch = matchesBatchReplay(candidateBatch, replay) ? candidateBatch : null;
  const tolerance = { total: batch ? 0 : null, ready: 0, pending: 0, unsupported: 0, low: 0, minimum: null };
  const maneuvers = [], seen = new Set();
  for (const [targetIndex, target] of (batch?.targets ?? []).entries()) {
    const start = target.inputIndex;
    const key = `${target.kind}:${start}`;
    if (seen.has(key)) continue;
    seen.add(key);
    const coverage = coverageOf(target);
    tolerance.total++;
    tolerance[coverage.state]++;
    const ready = coverage.state === 'ready';
    const maneuver = { targetIndex, kind: target.kind, start, end: target.inputIndices?.at(-1) ?? start,
      state: coverage.state, fraction: ready ? coverage.fraction : null, score: ready ? toleranceScore(coverage.fraction) : null,
      safe: ready ? coverage.safe : null, total: coverage.valid ?? null, planned: coverage.total ?? null, window: safeWindow(target, coverage),
      gapMs: null, sharedCommand: false };
    maneuvers.push(maneuver);
    if (!ready) continue;
    if (maneuver.score !== null && (!tolerance.minimum || maneuver.score < tolerance.minimum.score)) tolerance.minimum = maneuver;
    if (coverage.fraction !== null && coverage.fraction < LOW_TOLERANCE_FRACTION) tolerance.low++;
  }
  maneuvers.sort((a, b) => a.start - b.start || a.end - b.end || a.targetIndex - b.targetIndex);
  for (let i = 1; i < maneuvers.length; i++) {
    const previous = maneuvers[i - 1], current = maneuvers[i];
    current.sharedCommand = current.start <= previous.end;
    // Keep unknown maneuvers in the chain: never bridge them to make a gap.
    if (previous.window && current.window)
      current.gapMs = Math.round((current.window.earliestMs - previous.window.latestMs) * 1e7) / 1e7;
  }
  const gaps = statistics(maneuvers.slice(1).map(maneuver => maneuver.gapMs));
  return { batch, tolerance, maneuvers,
    scores: statistics(maneuvers.map(maneuver => maneuver.score)), gaps,
    unknownGaps: Math.max(0, maneuvers.length - 1) - gaps.count,
    overlaps: maneuvers.filter(maneuver => maneuver.gapMs !== null && maneuver.gapMs < 0),
    hardPlaces: maneuvers.filter(maneuver => maneuver.fraction !== null && maneuver.fraction < LOW_TOLERANCE_FRACTION),
    knownWindows: maneuvers.filter(maneuver => maneuver.window).length,
    partial: !batch || tolerance.pending > 0 || tolerance.unsupported > 0,
    accepted: replay.inputs.filter(input => input.status === 'accepted').length,
    ignored: replay.inputs.filter(input => input.status === 'ignored').length,
    unreached: replay.inputs.filter(input => input.status === 'unreached').length };
}
