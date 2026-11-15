const milliseconds = (from, to) => Math.round((to - from) * 1e10) / 1e7;

export function inputTempo({ axis, inputs }) {
  const commands = inputs.map(input => ({ input, time: axis === 'time' ? input.value : input.time, interval: null }));
  const counts = { critical: 0, normal: 0, easy: 0, unknown: 0 };
  const gaps = [];
  let longestCritical = 0;
  let streak = 0;
  let lastCriticalEnd = null;

  for (let index = 1; index < commands.length; index++) {
    const current = commands[index], previous = commands[index - 1];
    const ms = Number.isFinite(current.time) && Number.isFinite(previous.time)
      ? milliseconds(previous.time, current.time) : null;
    const kind = ms === null ? 'unknown' : ms < 150 ? 'critical' : ms <= 250 ? 'normal' : 'easy';
    current.interval = { ms, kind };
    counts[kind]++;

    if (kind === 'critical') {
      if (!streak && lastCriticalEnd !== null) gaps.push(milliseconds(lastCriticalEnd, previous.time));
      streak++;
      longestCritical = Math.max(longestCritical, streak);
      lastCriticalEnd = current.time;
    } else {
      streak = 0;
      if (kind === 'unknown') lastCriticalEnd = null;
    }
  }

  const measured = counts.critical + counts.normal + counts.easy;
  return { commands, counts, measured, criticalFraction: measured ? counts.critical / measured : null, longestCritical, gaps };
}
