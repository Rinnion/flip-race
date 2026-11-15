import { sameRunSettings, storedRunSettings } from './run-settings.mjs';

export class RouteResults {
  analysis = null;
  settings = null;
  runs = [];
  rows = [];
  page = 0;
  selected = null;

  update(analysis, settings) {
    if (this.analysis === analysis && this.settings && sameRunSettings(this.settings, settings)) return false;
    this.analysis = analysis;
    this.settings = storedRunSettings(settings);
    this.runs = (analysis?.runs ?? []).filter(run => sameRunSettings(run, this.settings));
    this.rows = this.runs.flatMap(run => run.routes.map(route => ({ run, route })))
      .sort((a, b) => a.route.finishTime - b.route.finishTime);
    this.page = 0;
    this.selected = null;
    return true;
  }

  canSelect(entry, settings) {
    return this.rows.includes(entry) && sameRunSettings(entry.run, settings);
  }
}

export function restoreRouteInputs({ run, route }) {
  return route.inputTicks.map(tick => tick * run.stepMs / 1000);
}

export function isMatchingReplay(result, { run, route }) {
  const { summary } = result;
  return sameRunSettings(result, run) && result.axis === 'time'
    && summary.status === 'Finished' && Math.abs(summary.finishTime - route.finishTime) <= 1e-6
    && summary.acceptedInputs === route.inputTicks.length && summary.ignoredInputs === 0 && summary.unreachedInputs === 0
    && summary.collisions === route.collisions && summary.boosts === route.boosts && summary.slows === route.slows
    && summary.coins === route.coins && summary.finishBoosts === route.finishBoosts;
}

