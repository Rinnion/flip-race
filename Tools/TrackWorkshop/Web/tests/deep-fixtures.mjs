import { DEFAULT_SURFACE_RUN_SETTINGS } from '../run-settings.mjs';
export const settings = { profile: 'Chosen', warningDuration: .6, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS };
export const replay = (times, axis = 'time') => ({ ...settings, axis,
  inputs: times.map((time, index) => ({ index, value: axis === 'time' ? time : index * 10, time, x: index * 10, y: 20, status: 'accepted' })) });
export const maneuver = (inputIndices, overrides = {}) => ({ kind: 'maneuver', inputIndex: inputIndices[0], firstInputIndex: inputIndices[0], inputIndices,
  maneuver: { rulesVersion: 2, status: 'ready', spanMs: 600, stepMs: 50, complete: true, total: String(25 ** inputIndices.length),
    cursor: String(25 ** inputIndices.length), valid: '25', computed: '25', safe: '1', safeWindow: { earliestMs: inputIndices[0] * 1000, latestMs: (inputIndices.at(-1) + .5) * 1000 }, cells: [], ...overrides } });
export const batchFor = (result, targets) => ({ ...settings, spanMs: 600, stepMs: 50, axis: result.axis,
  inputs: result.inputs.map(input => input.value), targets,
  commandTargetIndices: result.inputs.map((_, i) => targets.findIndex(target => target.inputIndices?.includes(i) || target.kind === 'unsupported' && target.inputIndex === i)) });
