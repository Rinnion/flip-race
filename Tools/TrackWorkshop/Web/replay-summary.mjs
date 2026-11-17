import { inputTempo } from './input-tempo.mjs';
import { batchCoverage, matchesBatchReplay, toleranceScore } from './tolerance-batch.mjs';

export const LOW_TOLERANCE_FRACTION = .07;

// Shared command endpoints join places. Merely consecutive commands do not.
function mergePlaces(spans, commands) {
  const groups = [];
  for (const span of spans.sort((a, b) => a.start - b.start || a.end - b.end)) {
    let group = groups.at(-1);
    if (!group || span.start > group.end) {
      group = { start: span.start, end: span.end, tempoEdges: [], targetIndices: [] };
      groups.push(group);
    }
    group.end = Math.max(group.end, span.end);
    if (span.tempoEdge !== undefined) group.tempoEdges.push(span.tempoEdge);
    if (span.targetIndex !== undefined) group.targetIndices.push(span.targetIndex);
  }
  return groups.map((group, index) => {
    const previous = groups[index - 1];
    const fromCommand = previous && commands[previous.end], toCommand = commands[group.start];
    const actual = previous && [fromCommand.input, toCommand.input]
      .every(input => input.status !== 'unreached' && Number.isFinite(input.time));
    const from = actual ? fromCommand.input.time : fromCommand?.time;
    const to = actual ? toCommand.input.time : toCommand?.time;
    const gapKnown = previous && Number.isFinite(from) && Number.isFinite(to);
    return { ...group, commandCount: group.end - group.start + 1,
      gapMs: gapKnown ? Math.round((to - from) * 1e10) / 1e7 : null,
      gapSource: gapKnown ? actual ? 'actual' : 'scheduled' : null,
      unlocated: commands.slice(group.start, group.end + 1).filter(({ input }) => !canLocateCommand(input)).length };
  });
}

export function canLocateCommand(input) {
  return !!input && input.status !== 'unreached' && [input.time, input.x, input.y].every(Number.isFinite);
}

export function replaySummary(replay, candidateBatch = null, coverageOf = batchCoverage) {
  const tempo = inputTempo(replay);
  const batch = matchesBatchReplay(candidateBatch, replay) ? candidateBatch : null;
  const spans = [];
  for (let i = 1; i < tempo.commands.length; i++) {
    if (tempo.commands[i].interval.kind === 'critical') spans.push({ start: i - 1, end: i, tempoEdge: i });
  }
  const tolerance = { total: batch ? 0 : null, ready: 0, pending: 0, unsupported: 0, low: 0, minimum: null };
  const seen = new Set();
  for (const [targetIndex, target] of (batch?.targets ?? []).entries()) {
    const start = target.kind === 'pair' ? target.firstInputIndex : target.inputIndex;
    const key = `${target.kind}:${start}`;
    if (seen.has(key)) continue;
    seen.add(key);
    tolerance.total++;
    const coverage = coverageOf(target);
    tolerance[coverage.state]++;
    if (coverage.state !== 'ready') continue;
    const score = toleranceScore(coverage.fraction);
    if (!tolerance.minimum || coverage.fraction < tolerance.minimum.fraction)
      tolerance.minimum = { targetIndex, fraction: coverage.fraction, score, safe: coverage.safe, total: coverage.total };
    if (coverage.fraction < LOW_TOLERANCE_FRACTION) {
      tolerance.low++;
      spans.push({ start, end: target.kind === 'pair' ? start + 1 : start, targetIndex });
    }
  }
  const groups = mergePlaces(spans, tempo.commands);
  const measuredIntervals = tempo.commands.flatMap(command => Number.isFinite(command.interval?.ms) ? [command.interval.ms] : []);
  return { tempo, tolerance, groups, batch,
    minimumIntervalMs: measuredIntervals.length ? Math.min(...measuredIntervals) : null,
    criticalSeries: tempo.commands.filter((command, index) => command.interval?.kind === 'critical'
      && tempo.commands[index - 1].interval?.kind !== 'critical').length,
    partial: !batch || tolerance.pending > 0 || tolerance.unsupported > 0,
    longestPlace: groups.reduce((longest, group) => Math.max(longest, group.commandCount), 0),
    coveredCommands: groups.reduce((count, group) => count + group.commandCount, 0),
    accepted: replay.inputs.filter(input => input.status === 'accepted').length,
    ignored: replay.inputs.filter(input => input.status === 'ignored').length,
    unreached: replay.inputs.filter(input => input.status === 'unreached').length };
}
