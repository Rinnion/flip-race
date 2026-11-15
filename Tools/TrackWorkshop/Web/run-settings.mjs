export const DEFAULT_SURFACE_RUN_SETTINGS = Object.freeze({ enabled: true, ratePercent: 5, curve: 'Linear',
  capPercent: 30, airDecayPercent: 200, resetOnCollision: true });

const LEGACY_SURFACE_RUN_SETTINGS = { ...DEFAULT_SURFACE_RUN_SETTINGS, enabled: false };

export function normalizeSurfaceRunSettings(settings) {
  if (settings === undefined) return { ...LEGACY_SURFACE_RUN_SETTINGS };
  if (typeof settings?.enabled !== 'boolean' || typeof settings.resetOnCollision !== 'boolean'
    || ![1, 2, 3, 4, 5].includes(settings.ratePercent) || !['Linear', 'Exponential'].includes(settings.curve)
    || ![20, 30, 40].includes(settings.capPercent) || ![0, 50, 100, 200, 400].includes(settings.airDecayPercent)) {
    throw new Error('Неподдерживаемые настройки разгона от бега.');
  }
  return { ...settings };
}

export function captureRunSettings({ profile, warningDuration, surfaceRunSettings }) {
  if (!['Chosen', 'FastFlip'].includes(profile) || ![0.4, 0.6, 0.8, 1, 1.2].includes(warningDuration)) {
    throw new Error('Неподдерживаемое управление или предупреждение.');
  }
  return { profile, warningDuration, surfaceRunSettings: normalizeSurfaceRunSettings(surfaceRunSettings) };
}

export function storedRunSettings(run) {
  const { surfaceRun, surfaceRunSettings = surfaceRun === false ? undefined : surfaceRun } = run;
  return captureRunSettings({ ...run, surfaceRunSettings });
}

export function sameRunSettings(left, right) {
  const a = storedRunSettings(left), b = storedRunSettings(right);
  return a.profile === b.profile && a.warningDuration === b.warningDuration
    && Object.keys(DEFAULT_SURFACE_RUN_SETTINGS).every(key => a.surfaceRunSettings[key] === b.surfaceRunSettings[key]);
}
