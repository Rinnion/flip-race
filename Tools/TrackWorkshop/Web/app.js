import { clamp, parseInputs, distanceSeedFromReplay, MAX_REPLAY_INPUTS, sampleAt, viewMetrics, rewardAppearance, patchNumbers, translateObject, Revision, ReplayRoute } from './model.mjs';
import { MAX_LANDING_THRESHOLD_MS, nearestRouteInput, parseLandingThreshold, shortLandingIntervals } from './model.mjs';
import { createObject, deleteObject, resizeObject, getResizeHandles } from './edit-model.mjs';
import { DRAFT_KEY, readDraft, writeDraft, clearDraft } from './draft.mjs';
import { selectedObjectSource, replaceObjectSource } from './object-source.mjs';
import { LiveConnection } from './live.mjs';
import { RouteResults, restoreRouteInputs, isMatchingReplay, routeSearchTiming } from './route-analysis.mjs';
import { readSearchResponse } from './search-progress.mjs';
import { ToleranceRequestState, TOLERANCE_SPAN_MS, TOLERANCE_STEP_MS, adjacentSafeProbes, inputToleranceRequest,
  isMatchingToleranceReport, matrixCounts, matrixShares, resolveAnalysisTarget, singleMatrixCells, toleranceOffsets,
  toleranceOverlay } from './input-tolerance.mjs';
import { BATCH_SPAN_MS, batchCoverage, batchRequest, matchesBatchReplay, readBatchResponse, toleranceColor, toleranceScore } from './tolerance-batch.mjs';
import { inputTempo } from './input-tempo.mjs';
import { canLocateCommand, replaySummary } from './replay-summary.mjs';
import { PAIR_SPANS, PairGrid, matchesPairMinimum, matchesPairReport, pairAxis, pairKey, pairOverlay, pairRequest } from './pair-tolerance.mjs';
import { captureRunSettings, DEFAULT_SURFACE_RUN_SETTINGS, sameRunSettings, storedRunSettings } from './run-settings.mjs';
import { NEW_LEVEL_LENGTH, NEW_LEVEL_NAME, unusedTrackId, newLevelSource, copyBuiltinTrack,
  clearBotReferences, updateTrackMetadata, objectPropertyFields, updateObjectProperties } from './authoring-fields.mjs';

const $ = id => document.getElementById(id);
const svgNS = 'http://www.w3.org/2000/svg';
const number = (n, digits = 0) => Number(n).toLocaleString('ru-RU', { maximumFractionDigits: digits });
const state = { analysis: null, valid: false, catalog: [], zoom: 1, left: -20, route: new ReplayRoute(), time: 0, playing: false,
  sourceName: 'track.json', sourceText: '', loadRevision: 0, analysisRevision: new Revision(), debounce: null, analysisAbort: null,
  preview: null, moving: false, history: [], future: [], selection: null, addKind: null, pickingStart: false, referenceHints: [] };
let drag = null, spacePan = false;
let live = null, loadingTrack = null, replayPending = null;
let landingHighlights = [];
let replaySource = null;
let tempo = null;
const toleranceRequests = new ToleranceRequestState();
const targetAnalysis = { input: null, status: '' };
const tolerance = { snapshot: null, report: null, selected: '0,0', overlay: null, visible: true, error: '', buttons: new Map(), renderedSpan: null };
const pairTolerance = { cache: new Map(), snapshot: null, grid: null, selected: '0,0', buttons: new Map(),
  overlay: null, visible: true, status: '', previewStatus: '', renderedSpan: null };
const pairBatchSize = 128;
const toleranceBatch = { pending: null, batch: null, checkpoints: [], status: '', rows: [], rendered: null,
  view: null, selected: null, coverage: new WeakMap() };
const replaySummaryView = { revision: 0, cache: null, rendered: null, focusIndex: null };
let draftStorage, pendingDraft = null, draftBlocked = false, draftTimer = null, draftTouched = false;
const preparation = { revision: 0, abort: null, pending: false, result: null, sourceId: null };
const routeSearch = { pending: null, status: '', progress: null, buttons: [] };
const routeResults = new RouteResults();
const routePageSize = 100;
const surfaceControls = {
  enabled: 'surface-run-enabled', ratePercent: 'surface-run-rate', curve: 'surface-run-curve',
  capPercent: 'surface-run-cap', airDecayPercent: 'surface-run-decay', resetOnCollision: 'surface-run-reset',
};
const runSettingsControls = ['profile', 'warning', ...Object.values(surfaceControls)];
const objectEdit = { revision: 0, timer: null, abort: null, dirty: false, committing: false, error: '', source: null, target: null, kind: null, fields: {} };
const metadataInputs = ['name', 'id', 'length'];
const catalogIds = () => state.catalog.map(item => item.id);
const usedTrackIds = () => [...catalogIds(), state.analysis?.id, pendingDraft && trackIdFromText(pendingDraft.json), trackIdFromText($('json').value)].filter(Boolean);
function trackIdFromText(source) {
  try { return JSON.parse(source).track?.id; } catch { return null; }
}
function inputValue(input) {
  if (input.type === 'checkbox') return input.checked;
  return input.validity.badInput ? NaN : input.value;
}
function availableReference() {
  const profile = $('profile').value;
  const reference = state.analysis?.references?.find(item => item.profileId === profile);
  if (reference) return { reference, hint: false };
  const hint = state.referenceHints.find(item => item.profileId === profile);
  return hint && { reference: hint, hint: true };
}
function invalidatePrepared(reason = 'Исходник или настройки изменены. Подготовьте бота заново.') {
  preparation.revision++; preparation.abort?.abort(); preparation.abort = null;
  const existed = preparation.pending || preparation.result;
  preparation.pending = false; preparation.result = null;
  $('prepare-result').hidden = true; $('prepare-cancel').hidden = true;
  if (existed) message('prepare-state', reason);
}
function updatePrepareControls() {
  const a = state.analysis, reserved = state.catalog.some(e => e.id === a?.id);
  if (state.valid && (preparation.sourceId !== a.id || (reserved && $('output-track-id').value === a.id))) {
    preparation.sourceId = a.id;
    $('output-track-id').value = reserved ? `custom-${a.id}` : a.id;
  }
  $('prepare-copy-note').textContent = reserved
    ? 'Будет создан новый ID для импорта: ID встроенной карты занят. Проверьте ID выше. Исходный JSON останется в редакторе.'
    : 'Исходный JSON останется в редакторе. Подготовленный результат с этим ID скачивается отдельно.';
  $('prepare-bot').disabled = !state.valid || state.moving || preparation.pending || !!routeSearch.pending || !$('output-track-id').value.trim();
  $('output-track-id').disabled = preparation.pending;
  $('prepare-cancel').hidden = !preparation.pending;
  $('download-prepared').disabled = !preparation.result || preparation.pending || !state.valid || state.moving;
}
async function prepareBot() {
  if (!state.valid || state.moving || preparation.pending || routeSearch.pending) return;
  invalidatePrepared(); invalidateReplay('Подготавливаем новый эталон для текущего управления…');
  const revision = preparation.revision, replayRevision = state.route.revision.value;
  const source = $('json').value, settings = currentRunSettings(), { profile, warningDuration, surfaceRunSettings } = settings;
  const axis = $('axis').value, trackLength = state.analysis.length;
  const outputTrackId = $('output-track-id').value.trim(), abort = new AbortController();
  preparation.abort = abort; preparation.pending = true; controls();
  message('replay-error', '', false);
  const request = { json: source, profiles: [profile], warningDuration, surfaceRunSettings, outputTrackId };
  let authorInputs;
  try { authorInputs = parseInputs($('inputs').value); } catch { /* Invalid optional inputs do not prevent preparation. */ }
  try {
    let seedNote = '';
    if (axis === 'distance' && authorInputs) request.inputs = authorInputs;
    else if (axis === 'time' && authorInputs?.length) {
      message('prepare-state', 'Воспроизводим список по времени в Unity Core, чтобы получить подсказку по X…');
      try {
        const played = await api('/api/replay', { json: source, ...settings, axis, inputs: authorInputs }, abort.signal);
        if (revision !== preparation.revision || abort.signal.aborted) return;
        const seed = distanceSeedFromReplay(played.inputs, trackLength);
        if (seed.length) {
          request.inputs = seed;
          seedNote = `Подсказка по X из списка по времени · ${seed.length} нажатий. `;
        } else seedNote = 'Список по времени не дал подходящих нажатий по X. Поиск продолжится без подсказки. ';
      } catch (error) {
        if (revision !== preparation.revision || abort.signal.aborted || error.name === 'AbortError') return;
        seedNote = 'Не удалось перевести список по времени. Поиск продолжится без подсказки. ';
      }
    }
    message('prepare-state', `${seedNote}Ищем эталон для импорта с выключенным разгоном, затем проверяем Мастера с настройками заезда. Это может занять несколько десятков секунд.`);
    const result = await api('/api/prepare-bot', request, abort.signal);
    if (revision !== preparation.revision) return;
    const ready = result.profiles?.find(p => p.profileId === profile && p.status === 'ready');
    if (result.status !== 'prepared' || !result.preparedJson || !ready) {
      message('prepare-state', `${result.message || 'В пределах бюджета подходящий маршрут не найден.'} ${result.profiles?.map(p => p.message).filter(Boolean).join(' ') || ''} Это не доказывает непроходимость карты. Исходный JSON сохранён.`);
      return;
    }
    preparation.result = result;
    $('prepare-result').hidden = false;
    $('prepare-summary').textContent = `Совместимость с импортом · разгон выкл. · ${profileLabel(profile)} · ID ${result.outputTrackId} · эталон ${number(ready.finishTime, 2)} с · Мастер проверен. ${number(ready.expandedStates)} состояний; поиск ${number(ready.elapsedMilliseconds / 1000, 2)} с.`;
    const hiddenVerification = state.analysis.warningMode === 'selected' && state.analysis.obstacles.some(o => o.hidden)
      ? 'Проверены все пять длительностей предупреждения.' : state.analysis.warningMode === 'authored' ? 'Проверены индивидуальные таймеры скрытых камней.' : '';
    $('prepare-rewards').textContent = `Выбранные положительные награды: ${ready.rewardIds?.length ? ready.rewardIds.join(', ') : 'нет'}. Финишный рывок проверен полностью. ${hiddenVerification}`;
    const trial = ready.masterTrial;
    message('prepare-trial', `Пробный заезд Мастера · ${runSettingsLabel(trial ? storedRunSettings(trial) : settings)}. ${masterTrialOutcome(trial)}`);
    message('prepare-state', result.message || 'Подготовленный JSON готов для импорта в игру. Исходный текст в редакторе не изменён.');
    controls();
    if (!trial?.replayVerified) return;
    try {
      const trialSettings = storedRunSettings(trial);
      const route = await api('/api/replay', { json: result.preparedJson, ...trialSettings, axis: 'time', inputs: trial.inputTimes }, abort.signal);
      if (revision !== preparation.revision || !state.route.revision.current(replayRevision)) return;
      showResult(route, { source: result.preparedJson, editorSource: source, label: 'Пробный заезд Мастера' });
      $('replay-panel').open = true;
    } catch (error) {
      if (revision !== preparation.revision || error.name === 'AbortError') return;
      message('replay-error', `Не удалось построить траекторию пробного заезда Мастера. ${error.message}`);
    }
  } catch (error) {
    if (revision !== preparation.revision || error.name === 'AbortError') return;
    message('prepare-state', `Подготовка не выполнена: ${error.message} Исходный JSON сохранён.`);
  } finally {
    if (revision === preparation.revision) { preparation.pending = false; preparation.abort = null; controls(); }
  }
}
function masterTrialOutcome(trial) {
  if (!trial || trial.status === 'unavailable') return 'Пробный заезд недоступен.';
  if (trial.status === 'budget-limit') return 'Достигнут лимит расчёта пробного заезда.';
  if (trial.status === 'timed-out') return 'До лимита времени Мастер не финишировал.';
  const outcome = trial.goalsMet ? 'Выбранные награды и финишная цель получены.' : 'Часть целей подготовки не выполнена.';
  const replay = trial.replayVerified ? '' : ' Воспроизведение этого заезда не подтверждено.';
  return `Финиш ${number(trial.finishTime, 2)} с · удары ${number(trial.collisions)}. ${outcome}${replay}`;
}
function draftStatus(text, error = false) { $('draft-status').textContent = text; $('draft-status').classList.toggle('storage-error', error); }
function draftValue() {
  const { profile, warningDuration, surfaceRunSettings } = currentRunSettings();
  return { json: $('json').value, inputs: $('inputs').value, filename: state.sourceName, profile, axis: $('axis').value,
    warning: String(warningDuration), surfaceRunSettings };
}
function saveDraft() {
  clearTimeout(draftTimer);
  if (draftBlocked || !draftTouched) return;
  const result = writeDraft(draftStorage, draftValue());
  draftStatus(result.kind === 'saved' ? `Черновик сохранён в этом браузере · ${new Date(result.draft.savedAt).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}. Сервер файл не хранит.` : 'Автосохранение недоступно или превышен лимит. Скачайте исходный текст, чтобы сохранить правки.', result.kind === 'error');
}
function scheduleDraft() {
  draftTouched = true;
  clearTimeout(draftTimer);
  if (draftBlocked) { draftStatus('Сохранённый черновик ожидает решения. Текущие правки его не перезаписывают.'); return; }
  draftStatus('Изменения ожидают сохранения только в этом браузере…');
  draftTimer = setTimeout(saveDraft, 500);
}
function offerDraft() {
  try { draftStorage = window.localStorage; } catch { /* Storage may be disabled. */ }
  const result = readDraft(draftStorage);
  if (result.kind === 'empty') { draftStatus('Автосохранение — только в этом браузере. Сервер файл не хранит.'); return; }
  draftBlocked = true; pendingDraft = result.kind === 'saved' ? result.draft : null;
  $('draft-offer').hidden = false; $('draft-restore').hidden = !pendingDraft;
  $('draft-offer-text').textContent = pendingDraft ? `Есть черновик «${pendingDraft.filename}» от ${new Date(pendingDraft.savedAt).toLocaleString('ru-RU')}. Восстановить его вместе с нажатиями и настройками?` : 'Сохранённый черновик недоступен или повреждён. Редактор работает; удалите сохранение, чтобы снова включить автосохранение, либо скачивайте исходный текст.';
  draftStatus('Автосохранение приостановлено до решения о черновике.', !pendingDraft);
}
const eventNames = { collision: 'Удар', collected: 'Касание', reward: 'Бонус', failedReward: 'Срыв награды', negative: 'Ловушка', coin: 'Монета', finishBoost: 'Финишный рывок', hidden: 'Скрытый камень' };

function el(tag, attrs = {}, text) {
  const node = document.createElementNS(svgNS, tag);
  for (const [name, value] of Object.entries(attrs)) if (value !== null && value !== undefined) node.setAttribute(name, value);
  if (text !== undefined) node.textContent = text;
  return node;
}
function line(parent, x1, y1, x2, y2, attrs = {}) { parent.append(el('line', { x1, y1, x2, y2, ...attrs })); }
function message(id, text, visible = true) { $(id).textContent = text; $(id).hidden = !visible; }
function notice(text = '', error = false) { message('notice', text, !!text); $('notice').className = `notice${error ? ' error' : ''}`; }
async function api(path, data, signal) {
  const response = await fetch(path, data === undefined ? { signal } : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data), signal });
  let body;
  try { body = await response.json(); } catch { throw new Error('Сервер вернул неожиданный ответ. Проверьте, что запущена мастерская.'); }
  if (!response.ok) throw Object.assign(new Error(body.error || `Ошибка сервера: ${response.status}`),
    { status: response.status, retryAfter: response.headers.get('Retry-After') });
  return body;
}
function badge(text, kind) { $('validation-badge').textContent = text; $('validation-badge').className = `badge ${kind}`; }

const profileLabel = profile => profile === 'FastFlip' ? 'Быстрый переворот' : 'Основной';
const warningLabel = (mode, duration) => mode === 'authored' ? 'таймеры трассы' : `предупреждение ${number(duration, 1)} с`;
function surfaceRunLabel({ enabled, ratePercent, curve, capPercent, airDecayPercent, resetOnCollision }) {
  if (!enabled) return 'разгон от бега выкл.';
  return `разгон ${ratePercent} п. п./с, ${curve === 'Linear' ? 'линейный' : 'плавный'}, до +${capPercent}%, спад ${airDecayPercent}%, сброс при ударе ${resetOnCollision ? 'вкл.' : 'выкл.'}`;
}
function runSettingsLabel(settings, warningMode = state.analysis?.warningMode) {
  return `${profileLabel(settings.profile)} · ${warningLabel(warningMode, settings.warningDuration)} · ${surfaceRunLabel(settings.surfaceRunSettings)}`;
}
function currentRunSettings() {
  return captureRunSettings({ profile: $('profile').value, warningDuration: Number($('warning').value), surfaceRunSettings: {
    enabled: $(surfaceControls.enabled).value === 'true', ratePercent: Number($(surfaceControls.ratePercent).value),
    curve: $(surfaceControls.curve).value, capPercent: Number($(surfaceControls.capPercent).value),
    airDecayPercent: Number($(surfaceControls.airDecayPercent).value), resetOnCollision: $(surfaceControls.resetOnCollision).value === 'true',
  } });
}
function restoreRunSettings(settings) {
  const { profile, warningDuration, surfaceRunSettings } = captureRunSettings(settings);
  // A draft can be restored while the previous track's profile policy is still displayed.
  $('profile').value = profile;
  $('warning').value = String(warningDuration);
  for (const [key, id] of Object.entries(surfaceControls)) $(id).value = String(surfaceRunSettings[key]);
  updateSurfaceRunControls();
}
function updateSurfaceRunControls() {
  $('surface-run-fields').disabled = $(surfaceControls.enabled).value === 'false';
}
const routeStopReasons = {
  'frontier-exhausted': 'поиск завершён',
  'state-limit': 'лимит состояний',
  'time-limit': 'лимит времени поиска',
  'race-time-limit': 'лимит времени заезда',
  'verification-time-limit': 'лимит времени проверки маршрутов',
};
const routeLoadMessages = {
  ready: 'Результаты записаны в JSON трассы.',
  stale: 'Сохранённые результаты устарели. Запустите поиск для текущей трассы.',
  unsupported: 'Версия сохранённого анализа не поддерживается. Если новый поиск отклонён, удалите поле workshopAnalysis в JSON трассы.',
  invalid: 'Сохранённый анализ повреждён. Если новый поиск отклонён, исправьте или удалите поле workshopAnalysis в JSON трассы.',
};
const routeSearchPhases = {
  search: 'Перебор вариантов', verify: 'Проверка маршрутов', save: 'Подготовка JSON', done: 'Поиск завершён',
};

function canUseRouteSearch() {
  return state.valid && !state.moving && !drag && !objectEdit.dirty && !objectEdit.committing
    && loadingTrack === null && !live && !preparation.pending && !routeSearch.pending;
}

function renderRoutePage() {
  const start = routeResults.page * routePageSize;
  const list = $('route-results-list');
  list.start = start + 1;
  routeSearch.buttons = [];
  const items = routeResults.rows.slice(start, start + routePageSize).map(entry => {
    const { run, route } = entry;
    const item = document.createElement('li'), button = document.createElement('button');
    const time = document.createElement('strong'), detail = document.createElement('span');
    time.textContent = `${number(route.finishTime, 3)} с`;
    const settings = storedRunSettings(run);
    detail.textContent = `${profileLabel(run.profile)} · разгон ${settings.surfaceRunSettings.enabled ? 'вкл.' : 'выкл.'} · удары ${number(route.collisions)} · нажатия ${number(route.inputTicks.length)}`;
    button.type = 'button';
    button.title = runSettingsLabel(settings, run.warningMode);
    button.append(time, detail);
    button.addEventListener('click', () => selectFoundRoute(entry));
    item.append(button);
    routeSearch.buttons.push({ button, entry });
    return item;
  });
  list.replaceChildren(...items);
  list.scrollTop = 0;
  const total = routeResults.rows.length, end = start + items.length;
  $('route-results-prev').disabled = start === 0;
  $('route-results-next').disabled = end >= total;
  $('route-results-pages').hidden = total <= routePageSize;
  message('route-results-range', `${number(start + 1)}–${number(end)} из ${number(total)}`, total > routePageSize);
}

function updateRouteSearch() {
  const pending = routeSearch.pending, available = canUseRouteSearch();
  const saved = state.valid ? state.analysis.routeAnalysis : null;
  const analysis = saved?.analysis ?? null;
  const settings = currentRunSettings();
  $('route-search').disabled = !available || replayPending !== null || !!toleranceBatch.pending;
  $('route-search-cancel').hidden = !pending;
  message('route-search-settings', `${runSettingsLabel(pending?.settings ?? settings)} · старт с начала.`, state.valid);
  if (routeResults.update(analysis, settings)) {
    const summaries = routeResults.runs.map(run => {
      const item = document.createElement('li');
      const { stepMs, minimumIntervalMs } = routeSearchTiming(run);
      const details = [`шаг ${number(stepMs)} мс, минимум ${number(minimumIntervalMs)} мс`,
        `кандидатов ${number(run.foundCount)}`, `проверено ${number(run.verifiedCount)}`, `в JSON ${number(run.routes.length)}`, routeStopReasons[run.stopReason] ?? 'поиск остановлен',
        `${number(run.expandedStates)} состояний`, `${number(run.elapsedMilliseconds / 1000, 2)} с поиска`];
      if (run.beamPruned) details.push('часть ветвей отсечена');
      if (run.storageLimited) details.push('список сокращён по лимиту JSON');
      item.textContent = `${runSettingsLabel(storedRunSettings(run), run.warningMode)} · ${details.join(' · ')}`;
      return item;
    });
    $('route-search-runs').replaceChildren(...summaries);
    $('route-search-runs').hidden = !summaries.length;
    $('route-search-results').hidden = !routeResults.rows.length;
    renderRoutePage();
  }
  const hasOtherRuns = (analysis?.runs?.length ?? 0) > routeResults.runs.length;
  const noCurrentRoutes = !pending && hasOtherRuns && !routeResults.rows.length;
  const emptyMessage = 'Для текущих настроек нет сохранённых маршрутов. Результаты для других настроек остаются в JSON.';
  const status = routeSearch.status || (state.valid
    ? (noCurrentRoutes ? emptyMessage : routeLoadMessages[saved?.status] ?? 'Поиск использует выбранную настройку забега.')
    : 'Для поиска нужен корректный JSON трассы.');
  message('route-search-state', status + (routeSearch.status && noCurrentRoutes ? ` ${emptyMessage}` : ''));
  for (const { button, entry } of routeSearch.buttons) {
    button.disabled = !available;
    button.setAttribute('aria-pressed', String(entry === routeResults.selected));
  }
  renderSearchProgress();
}

function renderSearchProgress() {
  const progress = routeSearch.progress;
  $('route-search-progress').hidden = !progress;
  if (!progress) return;
  const { percent, phase } = progress;
  $('route-progress-bar').value = percent;
  $('route-progress-percent').textContent = `${percent}%`;
  $('route-progress-phase').textContent = routeSearchPhases[phase];
}

function cancelRouteSearch(reason = '') {
  if (routeSearch.pending) {
    routeSearch.pending.abort.abort();
    routeSearch.pending = null;
  }
  routeSearch.progress = null;
  routeSearch.status = reason;
}

async function searchRoutes() {
  if (!canUseRouteSearch() || replayPending !== null || toleranceBatch.pending) return;
  resetTolerance();
  const source = $('json').value, revision = state.analysisRevision.value, loadRevision = state.loadRevision;
  const request = { settings: currentRunSettings(), abort: new AbortController() };
  routeSearch.pending = request;
  routeSearch.progress = { percent: 0, phase: 'search' };
  routeSearch.status = 'Core ищет и проверяет маршруты в пределах бюджета. Поиск можно отменить.';
  controls();
  const current = () => routeSearch.pending === request && state.analysisRevision.current(revision)
    && loadRevision === state.loadRevision && source === $('json').value && sameRunSettings(request.settings, currentRunSettings());
  let completed = false;
  try {
    const response = await fetch('/api/route-search/stream', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ json: source, ...request.settings }), signal: request.abort.signal,
    });
    const { analysis, sourceJson } = await readSearchResponse(response, {
      signal: request.abort.signal,
      onProgress(progress) {
        if (!current()) return;
        routeSearch.progress = progress;
        renderSearchProgress();
      },
    });
    if (!current()) return;
    const saved = JSON.parse(sourceJson);
    const matching = analysis.runs.filter(run => sameRunSettings(run, request.settings));
    if (matching.length !== 1 || analysis.runs.some(run => !Array.isArray(run.routes) || run.routes.some(route => !Array.isArray(route?.inputTicks)))
      || saved?.track?.id !== state.analysis.id || analysis.version !== 2 || saved?.workshopAnalysis?.version !== analysis.version
      || !Array.isArray(saved?.workshopAnalysis?.groups))
      throw new Error('Сервер не вернул результаты поиска и исходник.');
    const [run] = matching;
    if (sourceJson !== source) {
      remember(source, sourceJson, 'Обновлён анализ трассы', state.selection, state.selection);
      $('json').value = sourceJson;
      if (replaySource?.editorSource === source) {
        replaySource = { ...replaySource, editorSource: sourceJson, source: replaySource.source === source ? sourceJson : replaySource.source };
      }
      $('char-count').textContent = `${number(sourceJson.length)} символов`;
      scheduleDraft();
    }
    state.analysis.routeAnalysis = { status: 'ready', analysis };
    routeSearch.status = run.routes.length ? 'Результаты записаны в JSON трассы.'
      : 'В этом поиске нет сохранённых финишей. Бюджет ограничен, проходимость трассы остаётся неизвестной.';
    completed = true;
  } catch (error) {
    if (current()) routeSearch.status = error.name === 'AbortError' ? 'Поиск отменён.' : `Поиск не выполнен. ${error.message}`;
  } finally {
    if (routeSearch.pending === request) {
      routeSearch.pending = null;
      routeSearch.progress = completed ? { percent: 100, phase: 'done' } : null;
      controls();
    }
  }
}

function selectFoundRoute(entry) {
  if (!canUseRouteSearch() || !routeResults.canSelect(entry, currentRunSettings())) return;
  const inputs = restoreRouteInputs(entry);
  $('axis').value = 'time';
  $('inputs').value = JSON.stringify(inputs);
  $('replay-panel').open = true;
  invalidatePrepared();
  scheduleDraft();
  replay(entry);
}

function controls() {
  updateSurfaceRunControls();
  updatePrepareControls();
  updateRouteSearch();
  $('download').disabled = !state.valid || state.moving;
  $('replay').disabled = !state.valid || state.moving;
  const a = state.analysis;
  const reference = availableReference();
  $('reference').disabled = !state.valid || state.moving || objectEdit.dirty || !reference;
  $('reference').textContent = reference?.hint ? 'Взять нажатия оригинала' : 'Взять эталон из файла';
  $('load-catalog').disabled = !$('catalog').value || loadingTrack !== null;
  $('undo-move').disabled = state.moving || !state.history.length || state.history.at(-1).after !== $('json').value;
  $('redo').disabled = state.moving || !state.future.length || state.future.at(-1).before !== $('json').value;
  for (const button of document.querySelectorAll('[data-add-kind]')) {
    button.disabled = !state.valid || state.moving;
    button.setAttribute('aria-pressed', String(button.dataset.addKind === state.addKind));
  }
  updateSelectionPanel();
  updateTrackMetadataFields();
  if (objectEdit.dirty) for (const id of ['undo-move', 'redo', 'delete-object', 'download', 'replay', 'prepare-bot']) $(id).disabled = true;
  if (objectEdit.dirty) for (const button of document.querySelectorAll('[data-add-kind]')) button.disabled = true;
  $('download').textContent = a?.staleReferences && state.valid ? '↓ Скачать без эталонов' : '↓ Скачать JSON';
  updateLiveStartControls();
  updateToleranceControls();
  renderBatchResults();
}
function updateRouteControls() {
  const route = state.route, toggle = $('toggle-route');
  toggle.disabled = !route.path;
  toggle.textContent = route.visible ? 'Скрыть траекторию' : 'Показать траекторию';
  $('route-stale').hidden = !route.stale;
  updateLandingReport();
  renderToleranceResults();
  renderPairTolerance();
  renderBatchResults();
}
function landingThreshold() {
  const input = $('landing-threshold');
  return input.validity.valid ? parseLandingThreshold(input.value) : null;
}

function toleranceContext() {
  return { editorSource: $('json').value, result: state.route.result, provenance: replaySource, settings: currentRunSettings(),
    inputText: $('inputs').value, axis: $('axis').value, loadRevision: state.loadRevision };
}

function canSelectAnalysisTarget() {
  return canUseRouteSearch() && !state.addKind && !state.pickingStart && !replayPending && !!state.route.result && !state.route.stale
    && replaySource?.editorSource === $('json').value && sameRunSettings(state.route.result, currentRunSettings());
}

function canCheckTolerance() { return canSelectAnalysisTarget() && !toleranceRequests.pending && !toleranceBatch.pending; }

function canUseBatchReplay() {
  return state.valid && !state.moving && !objectEdit.dirty && !objectEdit.committing && loadingTrack === null
    && !live && !state.route.stale && !!state.route.result?.inputs.length
    && replaySource?.source === $('json').value && replaySource.editorSource === replaySource.source
    && sameRunSettings(state.route.result, currentRunSettings());
}

function coverageOf(target) {
  if (!toleranceBatch.coverage.has(target)) toleranceBatch.coverage.set(target, batchCoverage(target));
  return toleranceBatch.coverage.get(target);
}

function batchTargetLabel(target) {
  return target.kind === 'pair' ? `Пара №${target.firstInputIndex + 1}–${target.firstInputIndex + 2}`
    : `Нажатие №${target.inputIndex + 1}`;
}

function batchCoverageLabel(target) {
  const coverage = coverageOf(target);
  if (coverage.state === 'unsupported') return 'Недоступно';
  if (coverage.state === 'pending') return coverage.computed
    ? `Клетки: ${number(coverage.computed)} из ${number(coverage.total)}` : 'Ожидает расчёта';
  return `Допуск ${number(100 * toleranceScore(coverage.fraction), 0)}% · безопасных ${number(100 * coverage.fraction, 2)}% (${number(coverage.safe)} / ${number(coverage.total)})`;
}

function canOpenBatchTarget(target) {
  const coverage = coverageOf(target);
  return coverage.state !== 'pending' || coverage.computed > 0;
}

function renderBatchResults() {
  const pending = toleranceBatch.pending, usable = canUseBatchReplay(), result = state.route.result;
  const persisted = state.analysis?.toleranceBatch;
  const saved = persisted?.status === 'ready' && matchesBatchReplay(persisted.batch, result) ? persisted.batch : null;
  const canRestore = state.valid && persisted?.status === 'ready' && !pending && (!usable || !saved);
  const batch = usable ? toleranceBatch.batch ?? saved : null;
  toleranceBatch.view = batch;
  $('batch-start').disabled = !usable || !canCheckTolerance() || !!pending || !!batch?.complete;
  $('batch-start').textContent = batch && !batch.complete ? 'Продолжить анализ' : 'Рассчитать все матрицы';
  $('batch-restore').hidden = !canRestore;
  $('batch-restore').disabled = !canUseRouteSearch() || !!replayPending;
  $('batch-cancel').hidden = pending?.phase !== 'running';
  $('batch-results').hidden = !batch?.targets.length;
  $('batch-progress').hidden = !pending || !batch;
  if (pending && batch) {
    const total = batch.targets.length;
    const finished = pending.completed ?? batch.targets.filter(target => coverageOf(target).state !== 'pending').length;
    $('batch-progress-bar').max = total || 1;
    $('batch-progress-bar').value = finished;
    message('batch-progress-label', `${number(total ? 100 * finished / total : 0)}% · ${number(finished)} из ${number(total)} манёвров`);
  }
  const savedStatus = saved?.complete
    ? 'Матрицы из JSON совпадают с этим прохождением.'
    : 'В JSON есть частичный расчёт для этого прохождения.';
  const persistenceStatus = {
    stale: 'Сохранённые матрицы устарели из-за изменения трассы или правил расчёта.',
    invalid: 'Сохранённые матрицы не удалось прочитать.',
    unsupported: 'Эта версия сохранённых матриц не поддерживается.',
  }[persisted?.status];
  const status = !usable ? (canRestore ? 'В JSON есть сохранённое прохождение с матрицами.'
    : state.valid && persistenceStatus || 'Постройте траекторию с нажатиями для текущей карты и настроек.')
    : toleranceBatch.status || (saved ? savedStatus : persisted?.status === 'ready'
      ? 'Матрицы в JSON относятся к другому прохождению или настройкам.' : persistenceStatus
        || 'Анализ использует выбранную траекторию и её настройки.');
  message('batch-status', status);

  if (toleranceBatch.rendered !== batch) {
    toleranceBatch.rendered = batch;
    toleranceBatch.rows = (batch?.targets ?? []).map((_, index) => {
      const item = document.createElement('li'), button = document.createElement('button');
      const label = document.createElement('span'), summary = document.createElement('strong');
      button.type = 'button';
      button.append(label, summary); item.append(button);
      button.addEventListener('click', () => openBatchTarget(toleranceBatch.view?.targets[index]));
      return { item, button, label, summary };
    });
    $('batch-list').replaceChildren(...toleranceBatch.rows.map(row => row.item));
  }
  for (const [index, row] of toleranceBatch.rows.entries()) {
    const target = batch.targets[index], coverage = coverageOf(target);
    row.label.textContent = batchTargetLabel(target);
    row.summary.textContent = batchCoverageLabel(target);
    row.button.disabled = !!pending || !canOpenBatchTarget(target) || !canSelectAnalysisTarget();
    row.button.setAttribute('aria-pressed', String(toleranceBatch.selected === index));
    row.button.dataset.state = coverage.state;
    if (coverage.state === 'ready') row.button.style.setProperty('--batch-tone', toleranceColor(coverage.fraction));
    else row.button.style.removeProperty('--batch-tone');
  }
  renderReplaySummary();
}

function renderReplaySummary() {
  const result = state.route.result;
  const fresh = state.valid && !state.route.stale && !!result && !state.moving && !objectEdit.dirty
    && !objectEdit.committing && !live && loadingTrack === null && replaySource?.editorSource === $('json').value
    && sameRunSettings(result, currentRunSettings());
  $('replay-summary-content').hidden = !fresh;
  if (!fresh) {
    replaySummaryView.focusIndex = null;
    message('replay-summary-status', result ? 'Прохождение неактуально. Постройте траекторию для текущей карты и настроек.'
      : 'Постройте траекторию для текущей карты и настроек.');
    replaySummaryView.rendered = null;
    return;
  }
  const batch = toleranceBatch.view;
  let cached = replaySummaryView.cache;
  if (!cached || cached.result !== result || cached.batch !== batch || cached.revision !== replaySummaryView.revision) {
    cached = { result, batch, revision: replaySummaryView.revision, report: replaySummary(result, batch, coverageOf) };
    replaySummaryView.cache = cached;
  }
  const navigable = canSelectAnalysisTarget(), matrixAvailable = navigable && !toleranceBatch.pending;
  const rendered = replaySummaryView.rendered;
  if (rendered?.cache === cached && rendered.navigable === navigable && rendered.matrixAvailable === matrixAvailable) return;
  replaySummaryView.rendered = { cache: cached, navigable, matrixAvailable };
  const { report } = cached, { tempo: timing, tolerance: allowance, groups } = report;
  const percentage = value => value === null ? 'нет данных' : `${number(value * 100, 1)}%`;
  const outcome = result.summary?.status === 'Finished'
    ? result.summary.cleanFinish ? 'финиш без ударов' : 'финиш достигнут' : 'финиш не достигнут';
  message('replay-summary-status', `${replaySource.label || 'Текущая траектория'} · ${outcome} · ${number(result.inputs.length)} команд · ${runSettingsLabel(storedRunSettings(result))}`
    + ` · принято ${number(report.accepted)}, отклонено ${number(report.ignored)}, не достигнуто ${number(report.unreached)}`);
  const minimum = allowance.minimum;
  const metrics = [
    ['Темп · критические интервалы', `${number(timing.counts.critical)} / ${number(timing.measured)} · ${percentage(timing.criticalFraction)}`,
      `Минимум ${report.minimumIntervalMs === null ? 'неизвестен' : `${number(report.minimumIntervalMs, 2)} мс`}. Нормальных ${number(timing.counts.normal)}, простых ${number(timing.counts.easy)}, время неизвестно ${number(timing.counts.unknown)}. Всего ${number(Math.max(0, result.inputs.length - 1))} интервалов.`],
    ['Критические серии', `${number(report.criticalSeries)} · максимум ${number(timing.longestCritical)} подряд`,
      `Длина в интервалах${timing.longestCritical ? ` (${number(timing.longestCritical + 1)} команд в самой длинной серии)` : ''}. Нормальный интервал 150–250 мс, простой >250 мс.`],
    ['Минимальный допуск', minimum ? `${number(minimum.score * 100)}%` : 'нет данных',
      minimum ? `Безопасных ${number(minimum.fraction * 100, 2)}% (${number(minimum.safe)} / ${number(minimum.total)}). Низкий допуск: ${number(allowance.low)} из ${number(allowance.ready)} рассчитанных манёвров.` : 'Нужны рассчитанные матрицы манёвров.'],
    ['Трудные места', `${number(groups.length)} · максимум ${number(report.longestPlace)} команд`,
      `Охвачено ${number(report.coveredCommands)} разных команд.${report.partial ? ' По доступным данным.' : ''}`],
  ];
  $('replay-summary-metrics').replaceChildren(...metrics.map(([label, value, description], index) => {
    const card = document.createElement('div'), term = document.createElement('dt'), detail = document.createElement('dd');
    const strong = document.createElement('strong'), note = document.createElement('span');
    term.textContent = label; strong.textContent = value; note.textContent = description;
    detail.append(strong, note); card.append(term, detail);
    if (index === 2 && minimum) {
      const target = report.batch.targets[minimum.targetIndex];
      card.append(summaryButton('На схеме', () => focusSummaryCommand(target.inputIndex), !navigable || !canLocateCommand(result.inputs[target.inputIndex])));
      card.append(summaryButton('Матрица', () => openBatchTarget(target), !matrixAvailable));
    }
    return card;
  }));
  message('replay-summary-coverage', allowance.total === null
    ? 'Допуск ещё не рассчитан. Трудные места пока определены только по темпу; число манёвров неизвестно до построения плана.'
    : `Матрицы: ${number(allowance.ready)} из ${number(allowance.total)} манёвров рассчитано · ожидает ${number(allowance.pending)} · недоступно ${number(allowance.unsupported)}.`
      + (report.partial ? ' Частичное покрытие: неизвестные результаты не считаются простыми. Число и границы трудных мест могут измениться.' : ' Покрытие полное.')
      + ' Доля каждой матрицы — от всех 625 клеток пары или 25 одиночных, без пробы пропуска.');
  $('replay-summary-places-label').textContent = `${report.partial ? 'Трудные места по доступным данным' : 'Трудные места'} · ${number(groups.length)}`;
  $('replay-summary-place-list').replaceChildren(...groups.map((group, index) => {
    const item = document.createElement('li'), description = document.createElement('p'), actions = document.createElement('div');
    const start = result.inputs[group.start], end = result.inputs[group.end];
    const time = position => {
      const input = result.inputs[position];
      if (input.status !== 'unreached' && Number.isFinite(input.time)) return `${number(input.time, 3)} с`;
      return Number.isFinite(timing.commands[position]?.time)
        ? `${number(timing.commands[position].time, 3)} с по ленте (не достигнуто)` : 'время неизвестно';
    };
    const x = input => Number.isFinite(input.x) && input.status !== 'unreached' ? number(input.x, 1) : 'неизвестно';
    description.textContent = `№${group.start + 1}${group.end !== group.start ? `–${group.end + 1}` : ''} · ${number(group.commandCount)} команд · ${time(group.start)} → ${time(group.end)} · X ${x(start)} → ${x(end)}`
      + ` · критических интервалов ${number(group.tempoEdges.length)}, манёвров с низким допуском ${number(group.targetIndices.length)}`
      + (group.unlocated ? ` · без точки на схеме ${number(group.unlocated)}` : '')
      + (index ? ` · пауза от предыдущего${group.gapSource === 'scheduled' ? ' по ленте' : ''}: ${group.gapMs === null ? 'неизвестна' : `${number(group.gapMs, 1)} мс`}` : '');
    actions.className = 'replay-summary-actions';
    actions.append(summaryButton('К началу на схеме', () => focusSummaryCommand(group.start), !navigable || !canLocateCommand(start)));
    if (group.end !== group.start) actions.append(summaryButton('К концу', () => focusSummaryCommand(group.end), !navigable || !canLocateCommand(end)));
    for (const targetIndex of group.targetIndices) {
      const target = report.batch.targets[targetIndex];
      actions.append(summaryButton(`Матрица · ${batchTargetLabel(target)}`, () => openBatchTarget(target), !matrixAvailable));
    }
    item.append(description, actions); return item;
  }));
  if (!groups.length) {
    const empty = document.createElement('li');
    empty.textContent = report.partial ? 'В доступных данных трудные места не найдены. Допуск известен не полностью.' : 'По этим критериям трудные места не найдены.';
    $('replay-summary-place-list').append(empty);
  }
}

function summaryButton(label, action, disabled) {
  const button = document.createElement('button');
  button.type = 'button'; button.textContent = label; button.disabled = disabled;
  button.addEventListener('click', action);
  return button;
}

function focusSummaryCommand(index) {
  const input = state.route.result?.inputs[index];
  if (!canSelectAnalysisTarget() || !canLocateCommand(input)) return;
  replaySummaryView.focusIndex = index;
  if (!state.route.visible) { state.route.toggle(); updateRouteControls(); }
  stopPlayback();
  state.left = input.x - dimensions().width * .4;
  seek(input.time, false);
  $('diagram').querySelector(`[data-route-input="${input.index}"]`)?.focus({ preventScroll: true });
  $('diagram').scrollIntoView({ block: 'nearest' });
}

function restoreBatchReplay() {
  const saved = state.analysis?.toleranceBatch;
  if (saved?.status !== 'ready' || !canUseRouteSearch() || replayPending || toleranceBatch.pending) return;
  const { axis, inputs } = saved.batch;
  restoreRunSettings(storedRunSettings(saved.batch));
  $('axis').value = axis;
  $('inputs').value = JSON.stringify(inputs);
  $('replay-panel').open = true;
  invalidatePrepared();
  scheduleDraft();
  replay();
}

function resetBatch() {
  toleranceBatch.pending?.abort.abort();
  Object.assign(toleranceBatch, { pending: null, batch: null, checkpoints: [], status: '', selected: null, view: null });
  replaySummaryView.revision++;
  replaySummaryView.focusIndex = null;
}

function openBatchTarget(target, activatedIndex) {
  if (!target || !canSelectAnalysisTarget() || toleranceBatch.pending || !canOpenBatchTarget(target)) return;
  const result = state.route.result, input = result.inputs[activatedIndex ?? target.inputIndex];
  toleranceRequests.cancel();
  clearAnalysisTarget();
  clearSingleTolerance();
  hidePairTolerance();
  toleranceBatch.selected = toleranceBatch.view.targets.indexOf(target);
  renderBatchResults();
  if (Number.isFinite(input.time)) {
    if (!state.route.visible) { state.route.toggle(); updateRouteControls(); }
    stopPlayback();
    if (Number.isFinite(input.x)) state.left = input.x - dimensions().width * .4;
    seek(input.time, false);
  }
  if (coverageOf(target).state === 'unsupported') {
    Object.assign(targetAnalysis, { input, status: `${batchTargetLabel(target)}: допуск недоступен. ${target.message || target.single?.message || target.pair?.message || ''}` });
    renderAnalysisTarget();
    $('analysis-target').scrollIntoView({ block: 'nearest' });
  } else if (target.kind === 'single') {
    Object.assign(tolerance, { snapshot: { ...toleranceContext(), inputIndex: target.inputIndex, spanMs: BATCH_SPAN_MS },
      report: target.single, visible: true });
    renderToleranceResults();
    $('tolerance-panel').scrollIntoView({ block: 'nearest' });
  } else {
    const snapshot = { ...toleranceContext(), firstInputIndex: target.firstInputIndex, activatedInputIndex: activatedIndex ?? null };
    const grid = new PairGrid();
    grid.spanMs = BATCH_SPAN_MS;
    grid.apply(target.pair);
    pairTolerance.cache.set(target.firstInputIndex, { snapshot, grid });
    Object.assign(pairTolerance, { snapshot, grid, visible: true });
    $('pair-filter').value = '0';
    renderPairTolerance();
    $('pair-panel').scrollIntoView({ block: 'nearest' });
  }
  draw();
}

function isCurrentBatch(ticket) {
  return toleranceBatch.pending === ticket && !ticket.abort.signal.aborted
    && toleranceRequests.matches(ticket.snapshot, toleranceContext());
}

function applyBatchSave({ canonicalJson, batch }) {
  const source = $('json').value;
  const parsed = typeof canonicalJson === 'string' && JSON.parse(canonicalJson.replace(/^\uFEFF/, ''));
  if (!parsed?.workshopAnalysis?.toleranceBatch || parsed.track?.id !== state.analysis.id
    || new TextEncoder().encode(canonicalJson).length > 2 * 1024 * 1024
    || !matchesBatchReplay(batch, state.route.result)) throw new Error('Сервер не вернул подходящие матрицы и исходник.');
  if (canonicalJson !== source) {
    remember(source, canonicalJson, 'Обновлён анализ допуска', state.selection, state.selection);
    $('json').value = canonicalJson;
    replaySource = { ...replaySource, editorSource: canonicalJson, source: canonicalJson };
    $('char-count').textContent = `${number(canonicalJson.length)} символов`;
    scheduleDraft();
  }
  state.analysis.toleranceBatch = { status: 'ready', batch };
  Object.assign(toleranceBatch, { batch, checkpoints: [], status: batch.complete
    ? 'Матрицы записаны в JSON трассы. Скачайте JSON, чтобы сохранить их в файле.'
    : batch.stopReason === 'storage-limit' ? 'Достигнут лимит JSON. Сохранена только часть матриц.'
      : batch.stopReason === 'cancelled' ? 'Расчёт остановлен. Готовые матрицы записаны в JSON; анализ можно продолжить.'
        : 'Достигнут лимит расчёта. Готовые матрицы записаны в JSON; анализ можно продолжить.' });
}

async function saveBatchCheckpoint(ticket) {
  const signal = ticket.abort.signal;
  const request = { ...batchRequest(ticket.snapshot), checkpoints: toleranceBatch.checkpoints };
  for (let retry = 0; ; retry++) {
    try {
      return await api('/api/tolerance-batch/checkpoint', request, signal);
    } catch (error) {
      signal.throwIfAborted();
      if (error.status !== 429 || retry >= 2) throw error;
      // The cancelled stream may still be releasing its server request slot.
      const waitMs = 1000 * clamp(Number(error.retryAfter) || 1, 1, 2);
      await new Promise((resolve, reject) => {
        const abort = () => { clearTimeout(timer); reject(signal.reason); };
        const timer = setTimeout(() => { signal.removeEventListener('abort', abort); resolve(); }, waitMs);
        signal.addEventListener('abort', abort, { once: true });
      });
    }
  }
}

async function runBatch() {
  if (!canUseBatchReplay() || !canCheckTolerance() || toleranceBatch.view?.complete) return;
  resetTolerance();
  toleranceBatch.selected = null;
  const ticket = { snapshot: toleranceContext(), abort: new AbortController(), phase: 'running' };
  toleranceBatch.pending = ticket;
  toleranceBatch.status = 'Определяем манёвры выбранной траектории…';
  controls();
  try {
    if (toleranceBatch.checkpoints.length) {
      const saved = await saveBatchCheckpoint(ticket);
      if (!isCurrentBatch(ticket)) return;
      applyBatchSave(saved);
      ticket.snapshot = toleranceContext();
    }
    const response = await fetch('/api/tolerance-batch/stream', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(batchRequest(ticket.snapshot)), signal: ticket.abort.signal,
    });
    const saved = await readBatchResponse(response, {
      signal: ticket.abort.signal,
      onPlan({ batch, checkpoints }) {
        if (!isCurrentBatch(ticket)) return;
        if (!matchesBatchReplay(batch, ticket.snapshot.result)) throw new Error('План не совпал с выбранной траекторией.');
        Object.assign(toleranceBatch, { batch, checkpoints, status: 'Core рассчитывает матрицы. Готовые участки отмечаются на схеме.' });
        renderBatchResults();
        draw();
      },
      onProgress({ completed, total, target, checkpoint }) {
        if (!isCurrentBatch(ticket)) return;
        const batch = toleranceBatch.batch;
        if (!batch || total !== batch.targets.length) throw new Error('Изменилось число манёвров в пакете.');
        if (target) {
          const index = batch.targets.findIndex(item => item.kind === target.kind && item.inputIndex === target.inputIndex);
          if (index < 0) throw new Error('Манёвр не найден в плане анализа.');
          batch.targets[index] = target;
        }
        if (checkpoint) {
          const index = toleranceBatch.checkpoints.findIndex(item => item.inputIndex === checkpoint.inputIndex);
          if (index < 0) toleranceBatch.checkpoints.push(checkpoint);
          else toleranceBatch.checkpoints[index] = checkpoint;
        }
        ticket.completed = completed;
        replaySummaryView.revision++;
        renderBatchResults();
        draw();
      },
    });
    if (isCurrentBatch(ticket)) applyBatchSave(saved);
  } catch (error) {
    if (isCurrentBatch(ticket)) toleranceBatch.status = `Анализ остановлен. ${error.message}`;
  } finally {
    if (toleranceBatch.pending === ticket) {
      toleranceBatch.pending = null;
      controls(); draw();
    }
  }
}

async function cancelBatch() {
  const task = toleranceBatch.pending;
  if (task?.phase !== 'running') return;
  task.abort.abort();
  toleranceBatch.pending = null;
  if (!toleranceBatch.checkpoints.length) {
    toleranceBatch.status = 'Расчёт остановлен.';
    controls(); draw();
    return;
  }
  const ticket = { ...task, phase: 'saving', abort: new AbortController() };
  toleranceBatch.pending = ticket;
  toleranceBatch.status = 'Записываем готовые матрицы в JSON…';
  controls();
  try {
    const saved = await saveBatchCheckpoint(ticket);
    if (isCurrentBatch(ticket)) applyBatchSave(saved);
  } catch (error) {
    if (isCurrentBatch(ticket)) toleranceBatch.status = `Расчёт остановлен; запись в JSON не выполнена. ${error.message}`;
  } finally {
    if (toleranceBatch.pending === ticket) {
      toleranceBatch.pending = null;
      controls(); draw();
    }
  }
}

function renderAnalysisTarget() {
  $('analysis-target').hidden = !targetAnalysis.status;
  if (targetAnalysis.status) message('analysis-target-status', targetAnalysis.status);
  $('analysis-target-cancel').hidden = toleranceRequests.pending?.kind !== 'target';
}

function clearAnalysisTarget() {
  Object.assign(targetAnalysis, { input: null, status: '' });
  renderAnalysisTarget();
}

async function analyzeCommand(input) {
  if (!canSelectAnalysisTarget() || toleranceBatch.pending || !state.route.result.inputs.includes(input)) return;
  const batch = toleranceBatch.view, storedTarget = batch?.targets[batch.commandTargetIndices[input.index]];
  if (storedTarget && canOpenBatchTarget(storedTarget)) {
    openBatchTarget(storedTarget, input.index);
    return;
  }
  const active = toleranceRequests.pending?.snapshot;
  if ((active?.activatedInputIndex ?? active?.inputIndex) === input.index) return;
  const snapshot = { ...toleranceContext(), inputIndex: input.index };
  const resolving = resolveAnalysisTarget(toleranceRequests, snapshot, toleranceContext, api);
  Object.assign(targetAnalysis, { input, status: `Определяем область допуска для нажатия №${input.index + 1}…` });
  clearSingleTolerance();
  hidePairTolerance();
  renderAnalysisTarget();
  draw();
  try {
    const target = await resolving;
    if (!target) return;
    if (target.kind === 'unsupported') {
      targetAnalysis.status = `Допуск нажатия №${input.index + 1} недоступен. ${target.message}`;
      $('analysis-target').scrollIntoView({ block: 'nearest' });
    } else {
      clearAnalysisTarget();
      if (target.kind === 'pair') checkPair(snapshot.result.inputs[target.firstInputIndex], input.index);
      else {
        checkTolerance(input);
        $('tolerance-panel').scrollIntoView({ block: 'nearest' });
      }
    }
  } catch (error) {
    targetAnalysis.status = `Проверка нажатия №${input.index + 1} не выполнена. ${error.message}`;
    $('analysis-target').scrollIntoView({ block: 'nearest' });
  } finally {
    renderAnalysisTarget();
    updateToleranceControls();
  }
}

function resetTolerance() {
  const hadOverlay = (tolerance.overlay ?? pairTolerance.overlay)?.startX != null;
  toleranceRequests.cancel();
  clearAnalysisTarget();
  clearSingleTolerance();
  hidePairTolerance();
  pairTolerance.cache.clear();
  if (hadOverlay) draw();
}

function clearSingleTolerance() {
  Object.assign(tolerance, { snapshot: null, report: null, selected: '0,0', overlay: null, error: '' });
  renderToleranceResults();
}

function updateToleranceControls() {
  const available = canCheckTolerance();
  for (const button of $('tempo-commands').querySelectorAll('[data-check-tolerance], [data-check-pair]')) button.disabled = !available;
  if (tolerance.snapshot) updateSingleCellSelection();
}

const probeStatuses = {
  safe: 'Безопасно на участке', collision: 'Удар', rejected: 'Команда отклонена',
  invalid: 'Недопустимое время', incomplete: 'Проверка не завершена',
};
const probeLabel = probe => probe.omit ? 'Пропуск нажатия'
  : `${probe.offsetMs < 0 ? '−' : probe.offsetMs > 0 ? '+' : ''}${number(Math.abs(probe.offsetMs))} мс`;
const probeOutcome = probe => probe.omit && probe.status === 'safe'
  ? (probe.message || 'Без удара до следующей команды') : probeStatuses[probe.status];

function renderToleranceResults() {
  const { snapshot, report, selected, error } = tolerance;
  const pending = toleranceRequests.pending;
  $('tolerance-panel').hidden = !snapshot;
  if (!snapshot) { updateToleranceControls(); return; }
  $('tolerance-title').textContent = `Допуск нажатия №${snapshot.inputIndex + 1}`;
  $('tolerance-cancel').hidden = !['report', 'preview'].includes(pending?.kind);
  const baseline = report?.baseline;
  const original = baseline ? `Исходное нажатие в ${number(baseline.inputTime, 3)} с · участок ${number(baseline.startTime, 3)}–${number(baseline.endTime, 3)} с.` : '';
  message('tolerance-baseline', original, !!original);
  const adjacent = report?.status === 'ready' ? adjacentSafeProbes(report) : null;
  let safeSummary = '';
  if (adjacent) {
    const shifts = [];
    if (adjacent.earlierMs) shifts.push(`${number(adjacent.earlierMs)} мс раньше`);
    if (adjacent.laterMs) shifts.push(`${number(adjacent.laterMs)} мс позже`);
    safeSummary = shifts.length
      ? `Крайние соседние безопасные точки на сетке 50 мс · ${shifts.join(' · ')}.`
      : 'В соседних точках вокруг 0 мс безопасность не подтверждена.';
    if (adjacent.disconnected) safeSummary += ` Безопасных точек за разрывами ${number(adjacent.disconnected)}.`;
  }
  message('tolerance-safe-summary', safeSummary, !!safeSummary);
  let status = '';
  if (pending?.kind === 'report') status = 'Проверяем сдвиги и пропуск исходного нажатия…';
  else if (report?.status === 'unsupported') status = report.message;
  else if (report && !report.complete) status = report.message || 'Достигнут лимит времени.';
  message('tolerance-status', [status, error].filter(Boolean).join(' '), !!(status || error));
  $('tolerance-workspace').hidden = report?.status === 'unsupported' && report.complete;
  const cells = singleMatrixCells(report);
  tolerance.cells = new Map(cells.map(cell => [pairKey(cell), cell]));
  const span = snapshot.spanMs ?? TOLERANCE_SPAN_MS;
  if (tolerance.renderedSpan !== span) {
    makeMatrix($('tolerance-matrix'), tolerance.buttons, toleranceOffsets(span), [0]);
    tolerance.renderedSpan = span;
  }
  renderMatrixCells(tolerance.buttons, tolerance.cells, selected === 'omit' ? '0,0' : selected);
  if (selected === 'omit') tolerance.buttons.get('0,0').button.setAttribute('aria-pressed', 'false');
  renderMatrixCounts('tolerance', matrixCounts(cells));
  const omission = report?.omission;
  $('tolerance-omission-status').textContent = omission ? probeOutcome(omission) : 'Ожидает расчёта';
  updateToleranceControls();
}

function updateSingleCellSelection() {
  const { selected, overlay, visible } = tolerance;
  const omission = selected === 'omit';
  const cell = selectedSingleProbe();
  const outcome = omission ? probeOutcome(cell) : matrixStatuses[matrixCellStatus(cell)][1];
  const coordinates = omission ? 'Пропуск нажатия' : matrixCellDescription(tolerance.buttons.get(selected).offset);
  const minimum = cell?.minimumIntervalMs;
  message('tolerance-selected', [coordinates, outcome, Number.isFinite(minimum)
    && `минимум между соседними командами ${number(minimum, 2)} мс${cell.botCompatible ? '' : ' · короче минимума поиска (100 мс)'}`,
  (!omission || cell?.status !== 'safe') && cell?.message].filter(Boolean).join(' · '));
  $('tolerance-preview').disabled = !canCheckTolerance() || !cell || matrixCellStatus(cell) === 'pending' || cell.status === 'invalid';
  const omissionResult = tolerance.report?.omission;
  $('tolerance-omission').disabled = !canCheckTolerance() || !omissionResult || omissionResult.endTime === null;
  $('tolerance-omission').setAttribute('aria-pressed', String(omission));
  const pending = toleranceRequests.pending;
  const previewStatus = pending?.kind === 'preview' ? `Строим траекторию пробы «${probeLabel(pending.probe)}»…`
    : overlay ? [probeLabel(overlay.probe), probeOutcome(overlay.probe), overlay.startX !== null
      ? (visible && state.route.visible ? 'Наложение на схеме ниже' : 'Наложение скрыто')
      : 'Траектория недоступна', overlay.probe.status !== cell?.status && overlay.probe.message]
      .filter(Boolean).join(' · ') : '';
  message('tolerance-preview-status', previewStatus, !!previewStatus);
  $('tolerance-hide').hidden = overlay?.startX == null;
  $('tolerance-hide').textContent = visible ? 'Скрыть наложение' : 'Показать наложение';
  $('tolerance-hide').setAttribute('aria-pressed', String(visible));
  $('tolerance-remove').hidden = !overlay;
}

function selectedSingleProbe() {
  const { selected, report, cells } = tolerance;
  if (selected !== 'omit') return cells?.get(selected);
  const omission = report?.omission;
  return omission ? { ...omission, computed: omission.endTime !== null } : null;
}

async function checkTolerance(input) {
  if (!canCheckTolerance() || !state.route.result.inputs.includes(input)) return;
  clearAnalysisTarget();
  clearSingleTolerance();
  hidePairTolerance();
  draw();
  const snapshot = { ...toleranceContext(), inputIndex: input.index };
  tolerance.snapshot = snapshot;
  const ticket = toleranceRequests.start('report', snapshot);
  renderToleranceResults();
  const current = () => toleranceRequests.current(ticket, toleranceContext());
  try {
    const report = await api('/api/input-tolerance', inputToleranceRequest(snapshot), ticket.abort.signal);
    if (!current()) return;
    if (!isMatchingToleranceReport(report, snapshot)) throw new Error('Ответ не совпал с исходной траекторией.');
    tolerance.report = report;
  } catch (error) {
    if (current()) tolerance.error = `Проверка не выполнена. ${error.message}`;
  } finally {
    toleranceRequests.finish(ticket);
    renderToleranceResults();
  }
}

async function previewTolerance() {
  const { snapshot, report, selected } = tolerance;
  const probe = selectedSingleProbe();
  if (!canCheckTolerance() || !snapshot || !probe?.computed || probe.status === 'invalid'
    || !toleranceRequests.matches(snapshot, toleranceContext())) return;
  const ticket = toleranceRequests.start('preview', snapshot);
  ticket.probe = probe;
  tolerance.error = '';
  renderToleranceResults();
  const current = () => toleranceRequests.current(ticket, toleranceContext()) && tolerance.selected === selected;
  try {
    const preview = await api('/api/input-tolerance/preview', inputToleranceRequest(snapshot, probe), ticket.abort.signal);
    if (!current()) return;
    const overlay = toleranceOverlay(preview, probe, report.baseline, snapshot.inputIndex);
    if (!overlay) throw new Error('Ответ не совпал с выбранной пробой.');
    Object.assign(tolerance, { overlay, visible: true });
    showOverlayOnMap(overlay);
  } catch (error) {
    if (current()) tolerance.error = `Траектория не построена. ${error.message}`;
  } finally {
    toleranceRequests.finish(ticket);
    renderToleranceResults();
  }
}

function removeToleranceOverlay(render = true) {
  if (toleranceRequests.pending?.kind === 'preview') toleranceRequests.cancel();
  Object.assign(tolerance, { overlay: null, error: '' });
  if (render) renderToleranceResults();
  draw();
}

function showOverlayOnMap(overlay) {
  if (overlay.startX !== null) {
    if (!state.route.visible) { state.route.toggle(); updateRouteControls(); }
    stopPlayback();
    state.left = overlay.startX - dimensions().width * .2;
  }
  draw();
}

const matrixStatuses = {
  safe: ['●', 'Безопасно на участке'], collision: ['×', 'Удар'], rejected: ['!', 'Команда отклонена'],
  invalid: ['–', 'Недопустимое время'], incomplete: ['≈', 'Манёвр не завершён'], pending: ['·', 'Ожидает расчёта'],
};
const offsetLabel = offset => `${offset < 0 ? '−' : offset > 0 ? '+' : ''}${Math.abs(offset)}`;
const matrixCellStatus = cell => cell?.computed ? cell.status : 'pending';
const matrixCellDescription = ({ firstOffsetMs, secondOffsetMs }) =>
  `${firstOffsetMs === 0 && secondOffsetMs === 0 ? 'Исходная точка · ' : ''}X ${offsetLabel(firstOffsetMs)} мс · Y ${offsetLabel(secondOffsetMs)} мс`;

function renderMatrixCounts(prefix, counts, minimum = 0) {
  const shares = matrixShares(counts);
  const percent = share => share === null ? '—' : `${number(share * 100, 1)}%`;
  const countShare = (count, share) => `${number(count)}/${number(counts.total)} (${percent(share)})`;
  message(`${prefix}-progress`, `Проверено клеток ${number(counts.computed)} из ${number(counts.total)}. Ожидают ${number(counts.pending)}.`);
  for (const status of ['safe', 'collision', 'invalid']) message(`${prefix}-count-${status}`, countShare(counts[status], shares[status]));
  message(`${prefix}-other-counts`, `Команда отклонена ${number(counts.rejected)} · манёвр не завершён ${number(counts.incomplete)}.`);
  const ratio = counts.safe + counts.collision ? `${number(counts.collision)}:${number(counts.safe)}` : '—';
  message(`${prefix}-ratio`, `Красные : зелёные — ${ratio}. Среди красных и зелёных · красные ${percent(shares.collisionAmongDecided)} · зелёные ${percent(shares.safeAmongDecided)}.`);
  if (prefix === 'pair') message('pair-filter-counts', `Темпу ≥${number(minimum)} мс соответствуют ${number(counts.matching)} проверенных клеток · зелёных ${number(counts.matchingSafe)} · красных ${number(counts.matchingCollision)}.`, minimum > 0);
}

function hidePairTolerance() {
  Object.assign(pairTolerance, { snapshot: null, grid: null, overlay: null, status: '', previewStatus: '' });
  renderPairTolerance();
}

function makeMatrix(table, buttons, columns, rows) {
  const head = document.createElement('thead'), body = document.createElement('tbody');
  const header = head.insertRow();
  const coordinate = document.createElement('th');
  coordinate.textContent = 'Y ╲ X';
  header.append(coordinate);
  for (const offset of columns) {
    const label = document.createElement('th');
    label.scope = 'col'; label.textContent = offsetLabel(offset);
    header.append(label);
  }
  buttons.clear();
  for (const secondOffsetMs of rows) {
    const row = body.insertRow();
    const label = document.createElement('th');
    label.scope = 'row'; label.textContent = offsetLabel(secondOffsetMs);
    row.append(label);
    for (const firstOffsetMs of columns) {
      const offset = { firstOffsetMs, secondOffsetMs }, key = pairKey(offset);
      const button = document.createElement('button');
      button.type = 'button'; button.dataset.matrixCell = key;
      if (table.id === 'pair-matrix') button.dataset.pairCell = key;
      if (key === '0,0') button.dataset.source = '';
      row.insertCell().append(button);
      buttons.set(key, { button, offset });
    }
  }
  table.replaceChildren(head, body);
}

function renderMatrixCells(buttons, cells, selected, minimum = 0) {
  for (const [key, { button, offset }] of buttons) {
    const cell = cells.get(key), status = matrixCellStatus(cell);
    const [symbol, label] = matrixStatuses[status];
    button.dataset.status = status;
    button.classList.toggle('pair-filtered', !!cell?.computed && !matchesPairMinimum(cell, minimum));
    button.textContent = symbol;
    const description = `${matrixCellDescription(offset)} · ${label}`;
    button.title = description;
    button.setAttribute('aria-label', description);
    button.setAttribute('aria-pressed', String(key === selected));
    button.tabIndex = key === selected ? 0 : -1;
  }
}

function updatePairCellSelection() {
  const { grid, selected, buttons, overlay, visible } = pairTolerance;
  const cell = grid.cells.get(selected), offset = buttons.get(selected).offset;
  const pairPending = !!toleranceRequests.pending;
  const interval = cell?.pairIntervalMs, minimum = cell?.minimumIntervalMs;
  const outcome = matrixStatuses[matrixCellStatus(cell)][1];
  message('pair-selected', [matrixCellDescription(offset), outcome,
    Number.isFinite(interval) && `между нажатиями пары ${number(interval, 2)} мс`,
    Number.isFinite(minimum) && `минимум между соседними командами ${number(minimum, 2)} мс`, cell?.message]
    .filter(Boolean).join(' · '));
  $('pair-preview').disabled = pairPending || !cell?.computed || cell.status === 'invalid';
  const hasOverlay = overlay?.startX != null;
  $('pair-overlay-toggle').hidden = !hasOverlay;
  $('pair-overlay-toggle').textContent = visible ? 'Скрыть наложение' : 'Показать наложение';
  $('pair-overlay-toggle').setAttribute('aria-pressed', String(visible));
  $('pair-overlay-remove').hidden = !overlay;
  const overlayStatus = hasOverlay ? (visible && state.route.visible ? 'Наложение на схеме ниже.' : 'Наложение скрыто.') : '';
  const status = pairTolerance.previewStatus || overlayStatus;
  message('pair-preview-status', status, !!status);
}

function renderPairTolerance() {
  const { snapshot, grid, selected } = pairTolerance;
  const pending = toleranceRequests.pending;
  $('pair-panel').hidden = !snapshot;
  if (!snapshot) return;
  const statusLine = grid.unsupported || pairTolerance.status;
  message('pair-status', statusLine, !!statusLine);
  const pairLabel = `Допуск пары №${snapshot.firstInputIndex + 1}–${snapshot.firstInputIndex + 2}`;
  $('pair-title').textContent = snapshot.activatedInputIndex === null ? pairLabel : `${pairLabel} · выбрано нажатие №${snapshot.activatedInputIndex + 1}`;
  $('pair-cancel').hidden = !pending?.kind.startsWith('pair-');
  $('pair-workspace').hidden = !!grid.unsupported;
  if (grid.unsupported) { updateToleranceControls(); return; }
  $('pair-span').value = String(grid.spanMs);
  $('pair-span').disabled = !!pending;
  const minimum = Number($('pair-filter').value), counts = grid.counts(minimum);
  $('pair-continue').hidden = counts.computed === counts.total;
  $('pair-continue').disabled = !!pending;
  renderMatrixCounts('pair', counts, minimum);
  if (pairTolerance.renderedSpan !== grid.spanMs) {
    const axis = pairAxis(grid.spanMs);
    makeMatrix($('pair-matrix'), pairTolerance.buttons, axis, [...axis].reverse());
    pairTolerance.renderedSpan = grid.spanMs;
  }
  renderMatrixCells(pairTolerance.buttons, grid.cells, selected, minimum);
  updatePairCellSelection();
  updateToleranceControls();
}

function selectMatrixCell(review, key, removeOverlay, render, preview) {
  const item = review.buttons.get(key);
  if (!item) return;
  const previous = (review.buttons.get(review.selected) ?? review.buttons.get('0,0'))?.button;
  if (previous) { previous.tabIndex = -1; previous.setAttribute('aria-pressed', 'false'); }
  item.button.tabIndex = 0;
  item.button.setAttribute('aria-pressed', 'true');
  item.button.focus({ preventScroll: true });
  removeOverlay(false);
  review.selected = key;
  const previousRequest = toleranceRequests.pending;
  preview();
  if (toleranceRequests.pending === previousRequest) render();
}

function checkPair(input, activatedInputIndex = null) {
  if (!canCheckTolerance() || !state.route.result.inputs.includes(input) || !state.route.result.inputs[input.index + 1]) return;
  clearAnalysisTarget();
  clearSingleTolerance();
  hidePairTolerance();
  const snapshot = { ...toleranceContext(), firstInputIndex: input.index, activatedInputIndex };
  const cached = pairTolerance.cache.get(input.index);
  const grid = cached && toleranceRequests.matches(cached.snapshot, snapshot) ? cached.grid : new PairGrid();
  pairTolerance.cache.set(input.index, { snapshot, grid });
  Object.assign(pairTolerance, { snapshot, grid, selected: '0,0', visible: true });
  $('pair-filter').value = '0';
  renderPairTolerance();
  draw();
  $('pair-panel').scrollIntoView({ block: 'nearest' });
  if (!grid.unsupported) fillPairGrid();
}

async function fillPairGrid() {
  const { snapshot, grid } = pairTolerance;
  if (!canCheckTolerance() || !grid || grid.unsupported || !toleranceRequests.matches(snapshot, toleranceContext())) return;
  const missing = grid.missing(), span = grid.spanMs;
  if (!missing.length) return;
  const ticket = toleranceRequests.start('pair-report', snapshot);
  const current = () => toleranceRequests.current(ticket, toleranceContext());
  pairTolerance.status = 'Проверяем комбинации сдвигов…';
  renderPairTolerance();
  try {
    for (let start = 0; start < missing.length; start += pairBatchSize) {
      const offsets = missing.slice(start, start + pairBatchSize);
      const report = await api('/api/pair-tolerance', pairRequest(snapshot, { spanMs: span, offsets }), ticket.abort.signal);
      if (!current()) return;
      if (!matchesPairReport(report, snapshot, span, offsets, grid.baseline))
        throw new Error('Ответ не совпал с исходной траекторией и запрошенными клетками.');
      grid.apply(report);
      if (report.status === 'unsupported' || !report.complete) {
        pairTolerance.status = report.message || 'Достигнут лимит времени. Расчёт можно продолжить.';
        return;
      }
      renderPairTolerance();
    }
    pairTolerance.status = '';
  } catch (error) {
    if (current()) pairTolerance.status = `Проверка не выполнена. ${error.message}`;
  } finally {
    toleranceRequests.finish(ticket);
    renderPairTolerance();
  }
}

async function previewPair() {
  const { snapshot, grid, selected } = pairTolerance;
  const candidate = grid?.cells.get(selected);
  const pending = toleranceRequests.pending;
  if (!canSelectAnalysisTarget() || (pending && pending.kind !== 'pair-report')
    || !candidate?.computed || candidate.status === 'invalid'
    || !toleranceRequests.matches(snapshot, toleranceContext())) return;
  const { firstOffsetMs, secondOffsetMs } = candidate;
  if (pending?.kind === 'pair-report') pairTolerance.status = 'Расчёт приостановлен. Готовые клетки сохранены.';
  const ticket = toleranceRequests.start('pair-preview', snapshot);
  pairTolerance.previewStatus = 'Строим траекторию выбранной клетки…';
  renderPairTolerance();
  const current = () => toleranceRequests.current(ticket, toleranceContext()) && pairTolerance.selected === selected;
  try {
    const preview = await api('/api/pair-tolerance/preview', pairRequest(snapshot, { firstOffsetMs, secondOffsetMs }), ticket.abort.signal);
    if (!current()) return;
    const overlay = pairOverlay(preview, candidate, grid.baseline, snapshot.firstInputIndex);
    if (!overlay) throw new Error('Ответ не совпал с выбранной клеткой.');
    Object.assign(pairTolerance, { overlay, visible: true,
      previewStatus: overlay.startX === null ? overlay.probe.message || 'Траектория недоступна.' : '' });
    showOverlayOnMap(overlay);
  } catch (error) {
    if (current()) pairTolerance.previewStatus = `Траектория не построена. ${error.message}`;
  } finally {
    toleranceRequests.finish(ticket);
    renderPairTolerance();
  }
}

function removePairOverlay(render = true) {
  if (toleranceRequests.pending?.kind === 'pair-preview') toleranceRequests.cancel();
  Object.assign(pairTolerance, { overlay: null, previewStatus: '' });
  if (render) renderPairTolerance();
  draw();
}

function selectTrajectoryCommand(input, alignment = .4) {
  if (!state.route.result?.inputs.includes(input) || !Number.isFinite(input.time)) return;
  if (!state.route.visible) { state.route.toggle(); updateRouteControls(); }
  stopPlayback();
  if (Number.isFinite(input.x)) state.left = input.x - dimensions().width * alignment;
  seek(input.time, false);
  analyzeCommand(input);
}

const tempoKinds = { critical: 'критический', normal: 'нормальный', easy: 'простой', unknown: 'время неизвестно' };
const commandStatuses = { ignored: 'отклонено', unreached: 'не достигнуто' };

function updateTempoReport() {
  const replay = state.route.result;
  tempo = replay ? inputTempo(replay) : null;
  $('tempo-details').hidden = !tempo?.commands.length;
  $('tempo-legend').hidden = !tempo?.measured;
  $('tempo-gaps').hidden = !tempo?.gaps.length;
  const status = !tempo ? 'Постройте траекторию, чтобы увидеть темп команд.'
    : tempo.commands.length < 2 ? 'Для интервала нужны две команды.'
    : `Критических ${number(tempo.counts.critical)}${tempo.measured ? ` (${number(tempo.criticalFraction * 100, 1)}% измеренных)` : ''}`
      + ` · нормальных ${number(tempo.counts.normal)} · простых ${number(tempo.counts.easy)}`
      + (tempo.counts.unknown ? ` · без времени ${number(tempo.counts.unknown)}` : '')
      + `. Максимум подряд ${number(tempo.longestCritical)}.`;
  message('tempo-status', status);
  $('tempo-gap-values').textContent = tempo?.gaps.map((gap, index) => `№${index + 1} · ${number(gap, 2)} мс`).join(' · ') ?? '';
  $('tempo-commands').replaceChildren(...(tempo?.commands ?? []).map(({ input, time, interval }) => {
    const item = document.createElement('li'), jump = document.createElement('button'), check = document.createElement('button');
    jump.type = check.type = 'button';
    jump.disabled = input.time === null;
    jump.className = 'tempo-command';
    if (interval) jump.dataset.tempo = interval.kind;
    jump.textContent = [
      `№${number(input.index + 1)}`,
      Number.isFinite(time) ? `${number(time, 3)} с` : `X ${number(input.value, 2)}`,
      interval?.ms != null ? `${number(interval.ms, 3)} мс · ${tempoKinds[interval.kind]}` : interval && tempoKinds[interval.kind],
      commandStatuses[input.status],
    ].filter(Boolean).join(' · ');
    jump.title = input.reason || 'Перейти к команде и проверить допуск';
    jump.addEventListener('click', () => selectTrajectoryCommand(input));
    check.dataset.checkTolerance = '';
    check.textContent = 'Проверить допуск';
    check.setAttribute('aria-label', `Проверить допуск нажатия №${input.index + 1}`);
    check.addEventListener('click', () => {
      checkTolerance(input);
      $('tolerance-panel').scrollIntoView({ block: 'nearest' });
    });
    item.append(jump, check);
    if (input.index + 1 < replay.inputs.length) {
      const pair = document.createElement('button');
      pair.type = 'button'; pair.dataset.checkPair = '';
      pair.textContent = `Проверить пару №${input.index + 1}–${input.index + 2}`;
      pair.addEventListener('click', () => checkPair(input));
      item.append(pair);
    }
    return item;
  }));
}

function updateLandingReport() {
  const route = state.route, threshold = landingThreshold();
  const result = route.result, intervals = result?.landingIntervals ?? [];
  landingHighlights = route.visible && result ? shortLandingIntervals(intervals, threshold) : [];
  $('landing-threshold').setAttribute('aria-invalid', String(threshold === null));
  const profile = result && (result.profile === 'FastFlip' ? 'Быстрый переворот' : 'Основной');
  let status = 'Постройте траекторию, чтобы измерить интервалы после посадок.';
  if (threshold === null) status = `Введите целое число от 0 до ${MAX_LANDING_THRESHOLD_MS} мс.`;
  else if (result && !route.visible) status = `${profile} · отметки скрыты вместе с траекторией.`;
  else if (result && !intervals.length) status = `${profile} · в этом прохождении нет пар «посадка → нажатие с опоры».`;
  else if (result) {
    status = `${profile} · короче ${number(threshold)} мс: ${landingHighlights.length} из ${intervals.length}.`;
    if (landingHighlights.length) status += ' Короткие интервалы выделены оранжевым.';
  }
  message('landing-status', status);
  $('landing-details').hidden = !landingHighlights.length;
  $('landing-list').replaceChildren(...landingHighlights.map(interval => {
    const item = document.createElement('li'), button = document.createElement('button');
    const milliseconds = (interval.inputTime - interval.landingTime) * 1000;
    button.type = 'button';
    button.textContent = `≈ ${number(milliseconds, 1)} мс · X ≈ ${number(interval.landingX, 1)} · нажатие №${interval.inputIndex + 1}`;
    button.addEventListener('click', () => {
      stopPlayback();
      state.left = interval.landingX - dimensions().width * .4;
      seek(interval.landingTime, false);
    });
    item.append(button);
    return item;
  }));
}
function invalidateReplay(reason, clearRoute = false) {
  resetBatch();
  resetTolerance();
  replaySource = null;
  state.route.invalidate(clearRoute);
  replayPending?.abort.abort();
  replayPending = null;
  routeResults.selected = null;
  stopPlayback(); state.time = 0;
  $('playback').hidden = true; $('replay').textContent = '▶ Построить траекторию';
  if (state.route.stale) message('replay-state', reason || 'Постройте траекторию заново.');
  else if (reason) message('replay-state', reason);
  $('replay-state').classList.toggle('dirty', state.route.stale);
  updateTempoReport();
  updateRouteControls();
  draw();
}
function edited(immediate = false, options = {}) {
  if (!options.preserveObjectBuffer) resetObjectEdit();
  if (drag) finishDrag(false);
  if (!options.keepPreview) state.preview = null;
  if (!options.preserveHistory) {
    const hadHistory = state.history.length || state.future.length;
    state.history = []; state.future = []; state.selection = null; state.addKind = null;
    if (hadHistory) notice('Текст изменён или загружен файл. История визуальных правок очищена.');
  }
  state.loadRevision++;
  cancelRouteSearch();
  invalidatePrepared();
  scheduleDraft();
  state.analysisRevision.bump();
  state.analysisAbort?.abort();
  clearTimeout(state.debounce);
  state.valid = false; controls();
  badge('Проверка…', 'pending');
  $('char-count').textContent = `${number($('json').value.length)} символов`;
  $('validation-message').textContent = 'Проверяем текущий текст…';
  $('stale').hidden = !state.analysis;
  $('stale').textContent = options.keepPreview ? 'Правка записана в JSON; схема ожидает проверки Unity.' : 'Схема последней корректной версии. Исправьте JSON, чтобы обновить её.';
  message('validation-errors', '', false);
  message('reference-warning', '', false);
  invalidateReplay(options.clearRoute
    ? 'Запустите прохождение, чтобы увидеть путь, нажатия и события.'
    : 'JSON изменён. Для новой геометрии нужно новое прохождение.', options.clearRoute);
  if (options.acceptedAnalysis) analyze(options.acceptedAnalysis, options.preserveView);
  else state.debounce = setTimeout(analyze, immediate ? 0 : 300);
}
async function analyze(acceptedAnalysis = null, preserveView = false) {
  const revision = state.analysisRevision.value;
  const text = $('json').value;
  const abort = new AbortController(); state.analysisAbort = abort;
  try {
    if (new TextEncoder().encode(text).length > 2 * 1024 * 1024) throw new Error('Файл больше 2 МиБ.');
    JSON.parse(text);
    const result = acceptedAnalysis || await api('/api/analyze', { json: text }, abort.signal);
    if (!state.analysisRevision.current(revision)) return;
    const newTrack = state.analysis?.id !== result.id;
    state.preview = null; state.analysis = result; state.valid = true; objectEdit.committing = false;
    if (state.selection && !selectedObject(result)) state.selection = null;
    if (newTrack && !preserveView) { state.left = -20; state.zoom = 1; resetStartChoice(); }
    $('stale').hidden = true; $('empty-diagram').hidden = true;
    badge(result.staleReferences ? 'Геометрия верна' : 'JSON корректен', 'good');
    $('validation-message').textContent = `${result.obstacles.length} препятствий · ${result.rewards.length} наград · формат принят Unity`;
    $('track-title').textContent = result.name;
    $('track-meta').textContent = `${number(result.length)} × ${number(result.corridorHeight)} ед. · Y ↓`;
    const fast = $('profile').querySelector('[value="FastFlip"]'); fast.disabled = result.profilePolicy === 'chosen-only';
    if (fast.disabled && $('profile').value === 'FastFlip') {
      $('profile').value = 'Chosen';
      scheduleDraft();
      notice('Эта трасса разрешает только Основное управление. Профиль изменён с «Быстрый переворот» на «Основной»; постройте траекторию заново.');
    }
    $('warning').disabled = result.warningMode === 'authored';
    $('warning').title = result.warningMode === 'authored' ? 'Таймеры появления заданы самой трассой.' : '';
    const warnings = [...(result.warnings || [])];
    if (result.staleReferences) warnings.unshift('Эталоны устарели: схема и SOLO работают с проверенной копией без них. Скачивание JSON удалит botReferences; исходный текст останется прежним. BOT для новой карты не подтверждён.');
    if (state.catalog.some(item => item.id === result.id)) warnings.push(`Для импорта копии в игру измените track.id, например на «my-${result.id}»: ID встроенной трассы занят.`);
    if (result.warningMode === 'authored') warnings.push('Появление камней задано трассой; общий таймер здесь не применяется.');
    message('reference-warning', warnings.join('\n'), warnings.length > 0);
    controls(); draw();
  } catch (error) {
    if (!state.analysisRevision.current(revision) || error.name === 'AbortError') return;
    state.preview = null; state.valid = false; badge('Ошибка JSON', 'bad');
    $('validation-message').textContent = state.analysis ? 'Схема сохранена от последней корректной версии.' : 'Исправьте файл, чтобы увидеть геометрию.';
    message('validation-errors', error.message);
    $('stale').hidden = !state.analysis;
    $('stale').textContent = 'Схема последней корректной версии. Исправьте JSON, чтобы обновить её.';
    controls(); draw();
  }
}
function loadText(text, filename, referenceHints = [], unsaved = false) {
  resetStartChoice();
  state.sourceName = filename; state.sourceText = unsaved ? '' : text; state.referenceHints = referenceHints;
  $('json').value = text; $('filename').textContent = filename;
  $('inputs').value = '[]'; $('axis').value = 'distance';
  notice(); edited(true, { clearRoute: true });
}
async function loadBuiltin(id, asCopy = true) {
  if (!id) return;
  const revision = ++state.loadRevision;
  cancelPendingRouteWork('Поиск остановлен при открытии файла.');
  loadingTrack = revision; controls();
  try {
    const result = await api(`/api/catalog/${encodeURIComponent(id)}`);
    if (revision !== state.loadRevision) return;
    if (asCopy) {
      const copy = copyBuiltinTrack(result.json, usedTrackIds());
      loadText(copy.source, `${copy.id}.track.json`, copy.referenceHints, true);
      notice(copy.referenceHints.length
        ? 'Открыта копия встроенной трассы. Новый ID требует нового эталона бота. В панели нажатий доступны нажатия оригинала.'
        : 'Открыта копия встроенной трассы с новым ID.');
    } else loadText(result.json, state.catalog.find(e => e.id === id)?.file || `${id}.json`);
  } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
  finally { finishTrackLoad(revision); }
}
function finishTrackLoad(revision) {
  if (loadingTrack !== revision) return;
  loadingTrack = null; controls();
  if (objectEdit.dirty) { clearTimeout(objectEdit.timer); validateFields(); }
}
function download(text, name) {
  const url = URL.createObjectURL(new Blob([text], { type: 'application/json;charset=utf-8' }));
  const link = document.createElement('a'); link.href = url; link.download = name; document.body.append(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30000);
}
function dimensions() {
  const a = state.analysis;
  const box = $('diagram').getBoundingClientRect();
  return viewMetrics(Math.max(1, box.width), Math.max(1, box.height), a?.corridorHeight || 240, state.zoom, a?.length || 1000, state.left);
}
function objectGroup(parent, description, target = null) {
  const g = el('g', { class: 'object', tabindex: 0, role: 'button', 'aria-label': description,
    'data-collection': target?.collection, 'data-id': target?.id });
  g.append(el('title', {}, description));
  const show = () => { $('object-detail').textContent = description; };
  g.addEventListener('click', show); g.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); if (objectEditBlocked()) return; if (target) { state.selection = target; controls(); draw(); } show(); } });
  parent.append(g); return g;
}
function hiddenPhase(obstacle) {
  const replay = state.route.result;
  if (!obstacle.hidden || !replay) return obstacle.hidden ? 'Author' : 'Solid';
  const h = replay.hiddenStates.find(h => h.id === obstacle.id);
  if (!h || h.revealedAt === null || state.time < h.revealedAt) return 'Hidden';
  if (h.solidAt === null || state.time < h.solidAt) return 'Warning';
  return 'Solid';
}
function drawGeometry(parent, overview = false, liveView = null) {
  const a = liveView?.analysis || state.preview || state.analysis;
  const h = a.corridorHeight;
  const left = liveView?.left ?? -30, right = liveView?.right ?? a.length + 40;
  parent.append(el('rect', { x: left, y: 0, width: right - left, height: h, fill: '#f3f6eb' }));
  for (const y of [0, h]) line(parent, left, y, right, y, { stroke: '#bac9ae', 'stroke-width': overview ? 4 : 2 });
  if (!overview && !liveView) {
    line(parent, 0, h / 2, a.length, h / 2, { stroke: '#dce5d5', 'stroke-dasharray': '4 7', 'stroke-width': .8 });
    const v = dimensions(), step = state.zoom > 1.8 ? 50 : 100;
    for (let x = Math.max(0, Math.ceil(v.left / step) * step); x < Math.min(a.length + 1, v.left + v.width); x += step) {
      line(parent, x, 0, x, h, { stroke: '#e5eadf', 'stroke-width': .6 });
      parent.append(el('text', { x, y: h + 17, class: 'svg-label', 'text-anchor': 'middle' }, number(x)));
    }
    parent.append(el('text', { x: v.left + 7, y: -8, class: 'svg-label' }, 'Y = 0 · потолок'));
    parent.append(el('text', { x: v.left + 7, y: h - 7, class: 'svg-label' }, `Y = ${h} · пол`));
  }
  for (const o of a.obstacles) {
    const phase = liveView && o.hidden ? liveView.connection.hidden.get(o.id) || 'Hidden' : hiddenPhase(o);
    if (liveView && (phase === 'Hidden' || !o.bands.some(b => b.x + b.width >= left && b.x <= right))) continue;
    const isGate = o.bands.length > 1;
    let g = parent;
    if (liveView) {
      g = el('g', { 'data-obstacle-id': o.id, 'data-phase': phase });
      parent.append(g);
    } else if (!overview) {
      const desc = `${o.id} · ${isGate ? 'ворота, обе части' : 'камень'}${o.hidden ? ` · скрытый (${phase})` : ''} · X ${number(o.x, 2)} · ширина ${number(o.width, 2)}${isGate ? ` · проём Y ${number(o.gapTop, 2)}…${number(o.gapBottom, 2)} · смещения ${number(o.topOffsetX, 2)} / ${number(o.bottomOffsetX, 2)}` : ` · высота ${number(o.height, 2)}`}`;
      g = objectGroup(parent, desc, { collection: 'obstacles', id: o.id });
    }
    for (const b of o.bands) {
      const outlined = o.hidden && phase !== 'Solid';
      g.append(el('rect', { x: b.x, y: b.y, width: b.width, height: b.height,
        fill: outlined ? phase === 'Warning' ? '#c8b9d566' : '#d2c7df18' : isGate ? '#b6c7ad' : o.height > 60 ? '#b8bcb8' : '#d1d4cd',
        stroke: outlined ? '#9a85ae' : isGate ? '#7f9775' : '#8e9790', 'stroke-width': overview ? 2 : 1.2,
        'stroke-dasharray': outlined ? '5 3' : null }));
    }
    if (!overview && !isGate && o.width >= 28 && !o.hidden) {
      const b = o.bands[0];
      if (b) line(g, b.x + 5, b.y + b.height / 2, b.x + b.width - 5, b.y + b.height / 2, { stroke: '#98a096', 'stroke-width': 1, opacity: .5 });
    }
  }
  for (const r of a.rewards) {
    if (liveView) {
      const radius = Math.max(r.radius || 0, 15);
      if ((r.right ?? r.x + radius) < left || (r.left ?? r.x - radius) > right) continue;
    }
    const linked = r.shape === 'landing' ? a.obstacles.find(o => o.id === r.obstacleId) : null;
    if (liveView && linked?.hidden && liveView.connection.hidden.get(linked.id) !== 'Solid') continue;
    const appearance = rewardAppearance(r.kind), name = appearance.label;
    let g = parent;
    if (liveView) {
      const rewardState = liveView.connection.rewards.get(r.id) || 'available';
      g = el('g', { 'data-reward-id': r.id, 'data-reward-state': rewardState });
      parent.append(g);
    } else if (!overview) g = objectGroup(parent, `${r.id} · ${name} · X ${number(r.x, 2)}, Y ${number(r.y, 2)}${r.shape === 'landing' ? ` · окно X ${number(r.left, 2)}…${number(r.right, 2)} · плоскость Y ${number(r.planeY, 2)}` : r.shape === 'circle' ? ` · радиус ${number(r.radius, 2)}` : ' · 4 физических штриха'}`, { collection: 'rewards', id: r.id });
    if (r.shape === 'finish') {
      if (!overview) g.append(el('rect', { x: r.x - 12, y: r.y - 10, width: 24, height: 20, fill: 'transparent', 'pointer-events': 'all' }));
      for (const s of r.segments) line(g, s.x1, s.y1, s.x2, s.y2, { stroke: '#9982b2', 'stroke-width': r.strokeWidth, 'stroke-linecap': 'round' });
    } else if (r.shape === 'landing') {
      const muted = !liveView && linked?.hidden && hiddenPhase(linked) !== 'Solid';
      if (!overview) line(g, r.left, r.planeY, r.right, r.planeY, { stroke: 'transparent', 'stroke-width': 14, 'pointer-events': 'stroke' });
      line(g, r.left, r.planeY, r.right, r.planeY, { stroke: '#739eaa', 'stroke-width': 3, 'stroke-dasharray': muted ? '4 3' : null, opacity: muted ? .5 : 1 });
      for (const x of [r.left, r.right]) line(g, x, r.planeY - 4, x, r.planeY + 4, { stroke: '#739eaa', 'stroke-width': 1.5, opacity: muted ? .5 : 1 });
    } else {
      const { trap, coin } = appearance;
      g.append(el('circle', { cx: r.x, cy: r.y, r: r.radius, fill: trap ? '#eed9d5' : coin ? '#f1dfb4' : '#e2ebce', stroke: trap ? '#b17b80' : coin ? '#b59b59' : '#8c9f68', 'stroke-width': 1.5 }));
      if (!overview) {
        if (trap) g.append(el('path', { d: `M${r.x},${r.y-5} L${r.x+5},${r.y} L${r.x},${r.y+5} L${r.x-5},${r.y}Z`, fill: '#ae7c82' }));
        else if (coin) line(g, r.x, r.y - 4, r.x, r.y + 4, { stroke: '#b49953', 'stroke-width': 2 });
        else { line(g, r.x - 4, r.y, r.x + 4, r.y, { stroke: '#8c9f68', 'stroke-width': 1.5 }); line(g, r.x, r.y - 4, r.x, r.y + 4, { stroke: '#8c9f68', 'stroke-width': 1.5 }); }
      }
    }
  }
  line(parent, a.length, 0, a.length, h, { stroke: '#9d8caa', 'stroke-width': 2, 'stroke-dasharray': '6 5' });
  if (!overview) parent.append(el('text', { x: a.length - 5, y: -8, class: 'svg-label', 'text-anchor': 'end' }, 'ФИНИШ'));
}
function drawReplay(parent, overview = false) {
  const route = state.route;
  if (!route.visible) return;
  parent.append(el('path', {
    d: route.path,
    class: route.stale ? 'trajectory trajectory-stale' : 'trajectory',
    'pointer-events': 'none',
    'stroke-width': overview ? 5 : 2
  }));
  const replay = route.result;
  if (!replay) return;
  const review = pairTolerance.snapshot ? pairTolerance : tolerance;
  if (review.visible && review.overlay?.startX != null) {
    const { path, point, probe } = review.overlay;
    const attributes = { class: 'tolerance-trajectory', 'aria-hidden': 'true', 'data-status': probe.status };
    parent.append(path ? el('path', { ...attributes, d: path })
      : el('circle', { ...attributes, cx: point.x, cy: point.y, r: overview ? 3 : 6 }));
  }
  for (const { input, interval } of tempo?.commands ?? []) {
    if (!interval || interval.kind === 'unknown' || input.x === null || input.y === null) continue;
    const classes = `tempo-mark tempo-${interval.kind}`;
    const radius = overview ? 3.25 : 6.5;
    if (interval.kind === 'critical') parent.append(el('rect', { x: input.x - radius, y: input.y - radius,
      width: radius * 2, height: radius * 2, rx: 1, class: classes, 'aria-hidden': 'true' }));
    else parent.append(el('circle', { cx: input.x, cy: input.y, r: radius, class: classes, 'aria-hidden': 'true' }));
  }
  if (!overview) {
    for (const event of replay.events) {
      if (event.type !== 'collision') continue;
      const g = objectGroup(parent, `Удар · ${event.id} · ${number(event.time, 3)} с`);
      g.append(el('circle', { cx: event.x, cy: event.y, r: 6, fill: '#c37f8430', stroke: '#bc777d', 'stroke-width': 1.3 }));
    }
  }
  for (const input of replay.inputs) {
    if (input.x === null || input.y === null) continue;
    const batch = toleranceBatch.view;
    const target = batch?.targets[batch.commandTargetIndices[input.index]];
    const coverage = target && coverageOf(target);
    const batchDescription = target ? ` · ${batchTargetLabel(target)}: ${batchCoverageLabel(target)}` : '';
    const description = `Нажатие №${input.index + 1} · ${input.status === 'accepted' ? 'принято' : 'отклонено'} · ${number(input.time, 3)} с · X ${number(input.x, 2)}${input.reason ? ` · ${input.reason}` : ''}`;
    const marker = el('g', { class: `object route-command${replaySummaryView.focusIndex === input.index ? ' summary-command-selected' : ''}`, tabindex: 0, role: 'button', 'aria-label': `${description}${batchDescription} · Проверить допуск`,
      'data-route-input': input.index });
    marker.append(el('title', {}, description + batchDescription));
    const radius = overview ? 2.5 : 3.5;
    marker.append(el('circle', { cx: input.x, cy: input.y, r: radius, class: 'route-command-hit' }));
    if (input.status === 'accepted') marker.append(el('circle', { cx: input.x, cy: input.y, r: radius, class: 'press-marker' }));
    else {
      line(marker, input.x - radius, input.y - radius, input.x + radius, input.y + radius, { stroke: '#b6757d', 'stroke-width': 1.8 });
      line(marker, input.x + radius, input.y - radius, input.x - radius, input.y + radius, { stroke: '#b6757d', 'stroke-width': 1.8 });
    }
    if (coverage) marker.append(el('circle', {
      cx: input.x, cy: input.y, r: overview ? 7 : 10, class: 'batch-route-mark',
      'data-state': coverage.state, 'aria-hidden': 'true',
      stroke: coverage.state === 'ready' ? toleranceColor(coverage.fraction) : '#687472',
    }));
    parent.append(marker);
  }
  const s = sampleAt(replay.samples, state.time);
  if (s) {
    parent.append(el('circle', { cx: s.x, cy: s.y, r: state.analysis.playerRadius + 5, class: 'runner-aura' }));
    parent.append(el('circle', { cx: s.x, cy: s.y, r: state.analysis.playerRadius, class: 'runner' }));
  }
  for (const interval of landingHighlights) {
    const overlapsPlayer = !overview && s &&
      Math.hypot(s.x - interval.landingX, s.y - interval.landingY) <= state.analysis.playerRadius;
    const radius = overview ? 2.25 : overlapsPlayer ? state.analysis.playerRadius + 4 : 4;
    const mark = el('g', { class: 'landing-interval-mark', 'aria-hidden': 'true' });
    line(mark, interval.landingX, interval.landingY, interval.inputX, interval.inputY, { class: 'landing-interval-line' });
    mark.append(el('circle', { cx: interval.landingX, cy: interval.landingY, r: radius,
      class: overlapsPlayer ? 'landing-interval-point landing-interval-outline' : 'landing-interval-point' }));
    parent.append(mark);
  }
  if (review.visible && review.overlay?.startX != null) {
    for (const { index, time, x, y } of review.overlay.commands) {
      const label = `№${index + 1} · ${number(time, 3)} с`;
      const marker = el('g', { class: 'tolerance-command', role: 'img', 'aria-label': `Пробное нажатие ${label}` });
      const radius = overview ? 5 : 6;
      marker.append(el('circle', { cx: x, cy: y, r: radius, class: 'tolerance-command-ring' }));
      if (!overview) marker.append(el('text', {
        x: x + 8, y: y + (index % 2 ? 12 : -7), class: 'tolerance-command-label', 'aria-hidden': 'true',
      }, label));
      parent.append(marker);
    }
  }
}
function draw() {
  if (live || !state.analysis) return;
  const v = dimensions(); state.left = v.left;
  const diagram = $('diagram'); diagram.replaceChildren();
  diagram.setAttribute('viewBox', `${v.left} ${v.top} ${v.width} ${v.height}`);
  diagram.setAttribute('preserveAspectRatio', 'xMidYMid meet');
  drawGeometry(diagram); drawReplay(diagram); drawSelection(diagram);
  drawStartMarker(diagram);
  const overview = $('overview'); overview.replaceChildren();
  overview.setAttribute('viewBox', `-20 -8 ${state.analysis.length + 50} ${state.analysis.corridorHeight + 16}`);
  overview.setAttribute('preserveAspectRatio', 'none');
  drawGeometry(overview, true);
  overview.append(el('rect', { x: Math.max(0, v.left), y: 0, width: Math.min(v.width, state.analysis.length),
    height: state.analysis.corridorHeight, class: 'view-window', 'pointer-events': 'none', 'aria-hidden': 'true' }));
  drawReplay(overview, true);
  drawStartMarker(overview);
  $('pan').value = String(clamp((v.left + 20) / Math.max(1, state.analysis.length + 50 - v.width) * 1000, 0, 1000));
  $('view-start').textContent = number(Math.max(0, v.left));
  $('view-end').textContent = `${number(Math.min(state.analysis.length, v.left + v.width))} ед.`;
}

function canStartLive() {
  return !live && state.valid && !state.moving && !drag && !objectEdit.dirty && !preparation.pending
    && !routeSearch.pending && loadingTrack === null && replayPending === null;
}

function selectedStartX() {
  const x = $('live-start-x').valueAsNumber;
  return Number.isFinite(x) && x >= 0 && x < state.analysis?.length ? x : null;
}

function resetStartChoice() {
  $('live-start-x').value = '0';
  state.pickingStart = false;
}

function updateLiveStartControls() {
  const available = canStartLive(), x = selectedStartX();
  if (!available) state.pickingStart = false;
  let help = 'Старт с начала трассы.';
  let invalid = false;
  if (available && x === null) {
    help = `Введите X от 0 до ${number(state.analysis.length, 2)}, не включая финиш.`;
    invalid = true;
  } else if (available && x > 0) {
    help = 'До выбранного X Core проиграет текущий список. Пустой список означает путь без нажатий. Команды после точки не воспроизводятся.';
    try { parseInputs($('inputs').value); }
    catch (error) { help = `Для старта внутри трассы исправьте список нажатий. ${error.message}`; invalid = true; }
  }
  if (state.pickingStart) help = 'Щёлкните на обзоре или подробной схеме, чтобы выбрать X. Esc отменяет выбор.';
  $('live-start').disabled = !available || invalid;
  $('live-start-x').disabled = !available;
  $('live-start-x').setAttribute('aria-invalid', String(available && x === null));
  $('live-pick-start').disabled = !available;
  $('live-pick-start').setAttribute('aria-pressed', String(state.pickingStart));
  $('live-reset-start').disabled = !available || (x === 0 && !state.pickingStart);
  $('live-start-help').textContent = help;
  for (const id of ['diagram', 'overview']) $(id).classList.toggle('picking-start', state.pickingStart);
}

function drawStartMarker(parent) {
  const x = selectedStartX();
  if (!state.valid || x === null) return;
  const marker = el('g', { class: 'start-marker', 'data-start-x': x });
  marker.append(el('title', {}, `Старт с X ${number(x, 2)}`));
  line(marker, x, 0, x, state.analysis.corridorHeight, { 'vector-effect': 'non-scaling-stroke' });
  parent.append(marker);
}

function pickStart(event, diagram, center = false) {
  const transform = diagram.getScreenCTM();
  if (!transform) return;
  const { x } = new DOMPoint(event.clientX, event.clientY).matrixTransform(transform.inverse());
  if (!Number.isFinite(x) || x < 0 || x >= state.analysis.length) {
    notice('Выберите точку на трассе до линии финиша.', true);
    return;
  }
  const rounded = Math.round(x * 100) / 100;
  $('live-start-x').value = String(rounded < state.analysis.length ? rounded : x);
  state.pickingStart = false;
  if (center) state.left = x - dimensions().width / 2;
  controls(); draw();
}

function drawLive() {
  const { analysis, connection } = live;
  const frame = connection.state, sample = frame?.sample;
  const field = $('live-diagram');
  const left = frame?.cameraLeft ?? (live.settings.startX || 0) - 288.8;
  field.setAttribute('viewBox', `${left} 0 1520 ${analysis.corridorHeight}`);
  const geometry = document.createDocumentFragment();
  drawGeometry(geometry, false, { analysis, connection, left, right: left + 1520 });
  if (sample) {
    geometry.append(el('circle', { cx: sample.x, cy: sample.y, r: analysis.playerRadius,
      class: sample.recovering > 0 ? 'live-runner recovering' : 'live-runner' }));
  }
  field.replaceChildren(geometry);

  let status = 'Подключаемся к Unity Core…';
  if (connection.error) status = connection.error;
  else if (frame?.phase === 'finished') status = `Финиш · ${number(frame.summary?.finishTime ?? sample.time, 2)} с`;
  else if (frame?.phase === 'timedOut') status = 'Лимит 120 с · финиш не достигнут';
  else if (connection.readyAtStart) status = 'Точка достигнута. Нажмите «Начать отсюда».';
  else if (connection.paused) status = `Пауза${connection.reason ? ` · ${connection.reason}` : ''}${connection.clock.pending.size ? ' · получаем последний кадр' : ''}`;
  else if (frame?.phase === 'countdown') status = `Старт через ${Math.ceil(frame.countdownRemaining)}`;
  else if (frame?.phase === 'racing') status = 'Заезд идёт';
  if (frame?.staleReferences) status += ' · эталоны устарели';
  const statusLabel = $('live-status');
  if (statusLabel.textContent !== status) statusLabel.textContent = status;
  statusLabel.classList.toggle('error', !!connection.error);
  $('live-mode').dataset.phase = connection.error ? 'error' : frame?.phase || 'connecting';
  $('live-mode').dataset.paused = String(connection.paused);
  const settingsLabel = live.settingsLabel + (live.settings.startX > 0
    ? ` · Выбрано X ${number(live.settings.startX, 2)}${sample ? ` · Фактический старт X ${number(live.actualStartX ??= sample.x, 2)}` : ''}` : '');
  if ($('live-settings').textContent !== settingsLabel) $('live-settings').textContent = settingsLabel;
  $('live-pause').disabled = !frame || connection.closed || connection.terminal || (connection.paused && connection.clock.pending.size > 0);
  $('live-pause').textContent = connection.readyAtStart ? 'Начать отсюда' : connection.paused ? 'Продолжить' : 'Пауза';
  $('live-input').disabled = connection.closed || connection.paused || connection.terminal || frame?.phase !== 'racing';

  $('live-metrics').textContent = sample
    ? `Время ${number(sample.time, 2)} с · Удары ${sample.collisions} · Бонусы ${sample.boosts} · Монеты ${sample.coins} · Касания целей ${connection.collected.size}${sample.recovering > 0 ? ' · Восстановление' : ''}`
    : 'Время 0 с';
  const feedback = $('live-input-feedback'), input = connection.lastInput;
  const outcomes = { accepted: 'принято', ignored: 'отклонено', unreached: 'не достигло симуляции' };
  const unreached = connection.unreachedInputs ? `, не достигли симуляции ${connection.unreachedInputs}` : '';
  const inputText = input
    ? `Нажатия · принято ${connection.acceptedInputs}, отклонено ${connection.ignoredInputs}${unreached}. Последнее ${outcomes[input.status]}${input.reason ? ` (${input.reason})` : ''}.`
    : 'Нажатий пока нет.';
  if (feedback.textContent !== inputText) {
    feedback.textContent = inputText;
    if (input?.pressedAt !== undefined) feedback.dataset.latencyMs = String(performance.now() - input.pressedAt);
    else delete feedback.dataset.latencyMs;
  }
  feedback.dataset.accepted = String(connection.acceptedInputs);
  feedback.dataset.ignored = String(connection.ignoredInputs);
  feedback.dataset.unreached = String(connection.unreachedInputs);
  $('live-event-feedback').textContent = connection.lastEvent
    ? `${eventNames[connection.lastEvent.type] || connection.lastEvent.type} · ${connection.lastEvent.id}` : '';
  updateLiveRecording(connection);
}

function updateLiveRecording(connection) {
  const count = connection.recordedInputs.length;
  const tooMany = count > MAX_REPLAY_INPUTS;
  $('live-save-inputs').disabled = !connection.recordingSettled || !count || tooMany;
  let help = 'Поставьте заезд на паузу или завершите его, чтобы сохранить нажатия.';
  if (tooMany) help = `Подтверждено ${number(count)} нажатий. Лимит редактора ${number(MAX_REPLAY_INPUTS)}. Запись нельзя перенести целиком.`;
  else if (connection.recordingSettled) help = count
    ? `Сохранится ${number(count)} подтверждённых нажатий по времени. Они заменят список в редакторе.`
    : 'Подтверждённых нажатий пока нет.';
  else if (connection.paused) help = 'Ждём подтверждения последних отправленных команд.';
  const label = $('live-save-help');
  if (label.textContent !== help) label.textContent = help;
}

function restartLive(autoStart = false) {
  live.connection?.close();
  live.actualStartX = null;
  const connection = new LiveConnection(() => { if (live?.connection === connection) drawLive(); }, { autoStart });
  live.connection = connection;
  const url = new URL('/api/live', window.location.href);
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
  connection.open(url, live.settings);
  $('live-mode').focus({ preventScroll: true });
  drawLive();
}

function enterLive() {
  if (!canStartLive()) return;
  const startX = selectedStartX();
  if (startX === null) return;
  const settings = { json: $('json').value, ...currentRunSettings() };
  if (startX > 0) {
    try { Object.assign(settings, { startX, axis: $('axis').value, inputs: parseInputs($('inputs').value) }); }
    catch { updateLiveStartControls(); return; }
  }
  resetTolerance();
  stopPlayback();
  state.pickingStart = false;
  live = {
    analysis: state.analysis, connection: null, scroll: window.scrollY,
    settings, settingsLabel: runSettingsLabel(settings),
  };
  $('live-title').textContent = state.analysis.name;
  $('editor-mode').hidden = true;
  $('live-mode').hidden = false;
  window.scrollTo(0, 0);
  restartLive();
}

function exitLive() {
  if (!live) return;
  const { connection, scroll } = live;
  connection.close();
  live = null;
  $('live-mode').hidden = true;
  $('editor-mode').hidden = false;
  controls(); draw();
  $('live-start').focus({ preventScroll: true });
  window.scrollTo(0, scroll);
}

function saveLiveInputs() {
  if (!live) return;
  const times = live.connection.recordingTimes();
  if (!times) return;
  const { settings } = live;
  exitLive();
  $('inputs').value = JSON.stringify(times);
  $('axis').value = 'time';
  restoreRunSettings(settings);
  invalidatePrepared('Список нажатий из заезда сохранён. Подготовьте бота заново.');
  invalidateReplay(`Сохранено ${number(times.length)} нажатий по времени. Постройте траекторию или подготовьте бота.`);
  message('replay-error', '', false);
  scheduleDraft(); controls();
  $('replay-panel').open = true;
  $('inputs').focus({ preventScroll: true });
  $('inputs').scrollIntoView({ block: 'center' });
}

function zoom(factor, anchor = .5) {
  if (drag) return;
  if (!state.analysis) return;
  const old = dimensions(); state.zoom = clamp(state.zoom * factor, 1, 3);
  const next = dimensions(); state.left = old.left + anchor * (old.width - next.width); draw();
}
function seek(time, follow = true) {
  const replay = state.route.result;
  if (!replay) return;
  state.time = clamp(time, 0, replay.summary.elapsed);
  $('timeline').value = String(state.time); $('clock').textContent = `${number(state.time, 2)} с`;
  if (follow && $('follow').checked) {
    const sample = sampleAt(replay.samples, state.time), v = dimensions();
    if (sample && (sample.x < v.left + v.width * .15 || sample.x > v.left + v.width * .8)) state.left = sample.x - v.width * .35;
  }
  draw();
}
function stopPlayback() { state.playing = false; $('play').textContent = '▶'; $('play').setAttribute('aria-label', 'Воспроизвести'); }
function play() {
  const replay = state.route.result;
  if (!replay) return;
  if (state.playing) { stopPlayback(); return; }
  if (state.time >= replay.summary.elapsed) seek(0);
  state.playing = true; $('play').textContent = 'Ⅱ'; $('play').setAttribute('aria-label', 'Пауза');
  let previous = performance.now();
  const tick = now => {
    if (!state.playing || state.route.result !== replay) return;
    seek(state.time + Math.min(.15, (now - previous) / 1000)); previous = now;
    if (state.time >= replay.summary.elapsed) stopPlayback(); else requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
}
function showResult(result, provenance) {
  stopPlayback();
  resetBatch();
  resetTolerance();
  replaySource = provenance;
  state.route.setResult(result); state.time = 0;
  updateTempoReport();
  updateRouteControls();
  const s = result.summary;
  $('playback').hidden = false;
  $('replay-state').className = 'replay-state';
  const finish = s.status === 'Finished' ? (s.cleanFinish ? 'Финиш без ударов' : 'Финиш достигнут') : 'Лимит 120 с: финиш не достигнут';
  const label = provenance.label ? `${provenance.label} · ` : '';
  message('replay-state', `${label}${finish}. SOLO · ${runSettingsLabel(storedRunSettings(result))} · ${result.axis === 'distance' ? 'нажатия по X' : 'нажатия по времени'}.`);
  const metrics = [[`${number(s.finishTime ?? s.elapsed, 2)} с`, s.finishTime === null ? 'Время симуляции' : 'Время финиша'], [s.collisions, 'Удары'], [s.boosts, 'Бонусы'], [s.coins, 'Монеты'], [s.ignoredInputs + s.unreachedInputs, 'Не приняты / не достигнуты']];
  $('summary').replaceChildren(...metrics.map(([value, label]) => {
    const div = document.createElement('div'); div.className = 'metric';
    const strong = document.createElement('strong'); strong.textContent = value;
    const span = document.createElement('span'); span.textContent = label; div.append(strong, span); return div;
  }));
  $('timeline').max = s.elapsed; $('timeline').value = 0;
  $('events').replaceChildren();
  const events = [ ...result.inputs.map(i => ({ ...i, type: 'input', time: i.time, label: `#${i.index + 1} ${i.status === 'accepted' ? '●' : '×'} ${i.time === null ? `не достигнуто (${number(i.value, 2)})` : `${number(i.time, 2)} с`}`, css: i.status === 'accepted' ? '' : 'rejected', detail: i.reason || i.status })),
    ...result.events.map(e => ({ ...e, label: `${eventNames[e.type] || e.type}${e.phase ? ` · ${e.phase}` : ''} · ${number(e.time, 2)} с`, css: e.type === 'collision' || e.type === 'failedReward' || e.type === 'negative' ? 'hit' : 'reward', detail: e.id })) ];
  events.sort((a, b) => (a.time ?? Infinity) - (b.time ?? Infinity));
  for (const event of events) {
    const button = document.createElement('button'); button.className = `event-chip ${event.css}`; button.textContent = event.label;
    button.title = event.detail; button.disabled = event.time === null;
    button.addEventListener('click', () => {
      if (event.type === 'input') { selectTrajectoryCommand(result.inputs[event.index]); return; }
      stopPlayback(); seek(event.time);
      if (event.x !== null) { state.left = event.x - dimensions().width * .4; draw(); }
    });
    $('events').append(button);
  }
  if (!events.length) $('events').textContent = 'Нажатий и событий нет.';
  seek(0);
}
async function replay(entry = null) {
  if (!state.valid || state.moving || objectEdit.dirty) return;
  invalidateReplay(); message('replay-error', '', false);
  let inputs;
  try { inputs = parseInputs($('inputs').value); } catch (error) { message('replay-error', error.message); return; }
  const revision = state.route.revision.value;
  const pending = { abort: new AbortController(), entry };
  replayPending = pending;
  routeResults.selected = entry;
  controls();
  $('replay').disabled = true; $('replay').textContent = 'Симуляция…';
  message('replay-state', 'Unity Core рассчитывает SOLO-прохождение…');
  try {
    const source = $('json').value;
    const result = await api('/api/replay', { json: source, ...currentRunSettings(), axis: $('axis').value, inputs }, pending.abort.signal);
    if (!state.route.revision.current(revision)) return;
    if (entry && !isMatchingReplay(result, entry)) throw new Error('Повторное прохождение отличается от сохранённого. Запустите новый поиск.');
    showResult(result, { source, editorSource: source, label: entry ? 'Маршрут из анализа трассы' : '' });
  } catch (error) {
    if (!state.route.revision.current(revision)) return;
    routeResults.selected = null;
    message('replay-error', error.message); message('replay-state', 'Прохождение не построено. Исправьте параметры и повторите.');
  } finally {
    if (replayPending === pending) replayPending = null;
    if (state.route.revision.current(revision)) { $('replay').textContent = '▶ Построить траекторию'; controls(); }
  }
}

function cancelPendingRouteWork(reason) {
  resetBatch();
  resetTolerance();
  if (routeSearch.pending) cancelRouteSearch(reason);
  else routeSearch.progress = null;
  if (replayPending?.entry) invalidateReplay('Воспроизведение сохранённого маршрута остановлено из-за правки или загрузки.');
}

$('json').addEventListener('input', () => edited());
$('json').addEventListener('keydown', event => { if (event.key === 'Tab') { event.preventDefault(); const text = $('json'), start = text.selectionStart, end = text.selectionEnd; text.setRangeText('  ', start, end, 'end'); edited(); } });
$('format').addEventListener('click', () => {
  try {
    const formatted = JSON.stringify(JSON.parse($('json').value), null, 2) + '\n';
    if (new TextEncoder().encode(formatted).length > 2 * 1024 * 1024) throw new Error('После форматирования файл превысит 2 МиБ.');
    $('json').value = formatted;
    edited(true);
  } catch (error) { notice(`Форматирование: ${error.message}`, true); }
});
function confirmReplacement() {
  return !(objectEdit.dirty || $('json').value !== state.sourceText || $('inputs').value.trim() !== '[]')
    || window.confirm('Заменить текущий уровень и список нажатий? Сначала скачайте нужный JSON, если хотите сохранить трассу.');
}
function setFileMenu(open) {
  $('file-menu').hidden = !open;
  $('file-menu-toggle').setAttribute('aria-expanded', String(open));
}
$('file-menu-toggle').addEventListener('click', () => setFileMenu($('file-menu').hidden));
document.addEventListener('click', event => {
  if (!event.target.closest('#file-menu, #file-menu-toggle')) setFileMenu(false);
});
$('open').addEventListener('click', () => { setFileMenu(false); $('file').click(); });
$('file').addEventListener('change', async () => {
  const file = $('file').files[0]; if (!file) return;
  if (!confirmReplacement()) { $('file').value = ''; return; }
  const revision = ++state.loadRevision;
  cancelPendingRouteWork('Поиск остановлен при открытии файла.');
  loadingTrack = revision; controls();
  try {
    if (file.size > 2 * 1024 * 1024) throw new Error('Файл больше 2 МиБ.');
    const text = await file.text(); if (revision !== state.loadRevision) return;
    $('catalog').value = ''; loadText(text, file.name);
  } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
  finally { $('file').value = ''; finishTrackLoad(revision); }
});
$('catalog').addEventListener('change', controls);
$('load-catalog').addEventListener('click', () => {
  if (!$('catalog').value || !confirmReplacement()) return;
  setFileMenu(false); $('file-menu-toggle').focus(); loadBuiltin($('catalog').value);
});
$('new-level').addEventListener('click', () => {
  setFileMenu(false);
  $('new-level-name').value = NEW_LEVEL_NAME;
  $('new-level-id').value = unusedTrackId('custom-new-level', usedTrackIds());
  $('new-level-length').value = NEW_LEVEL_LENGTH;
  message('new-level-status', '', false);
  $('new-level-dialog').showModal(); $('new-level-name').select();
});
$('new-level-cancel').addEventListener('click', () => $('new-level-dialog').close());
$('new-level-form').addEventListener('submit', event => {
  event.preventDefault();
  const fields = Object.fromEntries(metadataInputs.map(key => [key, inputValue($(`new-level-${key}`))]));
  try {
    const source = newLevelSource(fields, catalogIds());
    if (!confirmReplacement()) return;
    $('new-level-dialog').close(); $('catalog').value = '';
    loadText(source, `${fields.id}.track.json`, [], true);
    notice('Создан пустой уровень. Добавьте объекты на схеме или откройте JSON трассы.');
  } catch (error) { message('new-level-status', error.message); }
});
$('download').addEventListener('click', () => {
  if (!state.valid || objectEdit.dirty) return;
  const a = state.analysis;
  const source = $('json').value;
  download(a.staleReferences ? clearBotReferences(source) : source, `${a.id}.track.json`);
  if (!a.staleReferences) state.sourceText = source;
  setFileMenu(false); $('file-menu-toggle').focus();
  const reserved = state.catalog.some(item => item.id === a.id);
  notice(`${a.staleReferences ? 'Скачан JSON без устаревших эталонов.' : 'JSON скачан.'}${reserved ? ' Для импорта копии встроенной карты задайте новый ID.' : ''}`);
});
$('download-source').addEventListener('click', () => {
  download($('json').value, state.sourceName);
  state.sourceText = $('json').value;
  setFileMenu(false); $('file-menu-toggle').focus();
});
for (const id of runSettingsControls) $(id).addEventListener('change', () => {
  cancelRouteSearch(routeSearch.pending ? 'Поиск остановлен после изменения настройки забега.' : '');
  invalidatePrepared();
  scheduleDraft(); invalidateReplay('Настройка забега изменена. Постройте траекторию заново.'); controls();
});
$('axis').addEventListener('change', () => {
  if (preparation.pending) invalidatePrepared();
  scheduleDraft(); invalidateReplay('Единицы нажатий изменены. Числа в списке сохранены без пересчёта — проверьте их перед запуском.'); controls();
});
$('inputs').addEventListener('input', () => { if (preparation.pending) invalidatePrepared(); scheduleDraft(); invalidateReplay('Список нажатий изменён. Постройте траекторию заново.'); message('replay-error', '', false); controls(); });
$('reference').addEventListener('click', () => {
  const found = availableReference(); if (!found || !state.valid || objectEdit.dirty) return;
  $('inputs').value = JSON.stringify(found.reference.inputs); $('axis').value = 'distance';
  if (preparation.pending) invalidatePrepared();
  scheduleDraft();
  invalidateReplay(found.hint ? 'Загружены нажатия оригинала по X. После правок проверьте их прохождением.' : 'Загружены нажатия эталона из файла для выбранного профиля.'); controls();
});
$('replay').addEventListener('click', () => replay()); $('play').addEventListener('click', play);
$('route-search').addEventListener('click', searchRoutes);
$('route-search-cancel').addEventListener('click', () => { cancelRouteSearch('Поиск отменён.'); controls(); });
$('batch-start').addEventListener('click', runBatch);
$('batch-cancel').addEventListener('click', cancelBatch);
$('batch-restore').addEventListener('click', restoreBatchReplay);
for (const [id, offset] of [['route-results-prev', -1], ['route-results-next', 1]]) $(id).addEventListener('click', () => {
  routeResults.page += offset;
  renderRoutePage();
  updateRouteSearch();
});
$('toggle-route').addEventListener('click', () => {
  state.route.toggle(); updateRouteControls(); draw();
});
$('landing-threshold').addEventListener('input', () => { updateLandingReport(); draw(); });
$('analysis-target-cancel').addEventListener('click', () => {
  if (toleranceRequests.pending?.kind !== 'target') return;
  toleranceRequests.cancel();
  targetAnalysis.status = `Проверка нажатия №${targetAnalysis.input.index + 1} отменена.`;
  renderAnalysisTarget();
  updateToleranceControls();
});
$('tolerance-preview').addEventListener('click', previewTolerance);
$('tolerance-omission').addEventListener('click', () => {
  const previous = tolerance.buttons.get(tolerance.selected)?.button;
  previous?.setAttribute('aria-pressed', 'false');
  removeToleranceOverlay(false);
  tolerance.selected = 'omit';
  previewTolerance();
});
$('tolerance-hide').addEventListener('click', () => {
  tolerance.visible = !tolerance.visible;
  renderToleranceResults();
  draw();
});
$('tolerance-remove').addEventListener('click', () => removeToleranceOverlay());
$('tolerance-cancel').addEventListener('click', () => {
  const pending = toleranceRequests.pending;
  if (!pending) return;
  toleranceRequests.cancel();
  tolerance.error = pending.kind === 'report' ? 'Проверка отменена.' : 'Построение траектории отменено.';
  renderToleranceResults();
});
$('pair-span').addEventListener('change', () => {
  const span = Number($('pair-span').value), grid = pairTolerance.grid;
  if (!canCheckTolerance() || !grid || !PAIR_SPANS.includes(span)) return;
  grid.spanMs = span;
  const selected = pairTolerance.buttons.get(pairTolerance.selected).offset;
  if ([selected.firstOffsetMs, selected.secondOffsetMs].some(offset => Math.abs(offset) > span)) {
    pairTolerance.selected = '0,0';
    removePairOverlay(false);
  }
  pairTolerance.status = '';
  renderPairTolerance();
  fillPairGrid();
});
$('pair-filter').addEventListener('change', renderPairTolerance);
$('pair-continue').addEventListener('click', fillPairGrid);
$('pair-cancel').addEventListener('click', () => {
  const kind = toleranceRequests.pending?.kind;
  if (!kind?.startsWith('pair-')) return;
  toleranceRequests.cancel();
  if (kind === 'pair-report') pairTolerance.status = 'Расчёт остановлен. Готовые клетки сохранены.';
  else pairTolerance.previewStatus = 'Построение траектории остановлено.';
  renderPairTolerance();
});
function bindMatrixSelection(table, review, removeOverlay, render, preview) {
  const moves = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, 1], ArrowDown: [0, -1] };
  const select = key => selectMatrixCell(review, key, removeOverlay, render, preview);
  table.addEventListener('click', event => {
    const key = event.target.closest('[data-matrix-cell]')?.dataset.matrixCell;
    if (key) select(key);
  });
  table.addEventListener('keydown', event => {
    const move = moves[event.key], key = event.target.dataset.matrixCell;
    if (!move || !key) return;
    event.preventDefault();
    const { firstOffsetMs, secondOffsetMs } = review.buttons.get(key).offset;
    const next = pairKey({ firstOffsetMs: firstOffsetMs + TOLERANCE_STEP_MS * move[0],
      secondOffsetMs: secondOffsetMs + TOLERANCE_STEP_MS * move[1] });
    select(next);
    review.buttons.get(next)?.button.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  });
}
bindMatrixSelection($('pair-matrix'), pairTolerance, removePairOverlay, renderPairTolerance, previewPair);
bindMatrixSelection($('tolerance-matrix'), tolerance, removeToleranceOverlay, renderToleranceResults, previewTolerance);
$('tolerance-legend').append(document.querySelector('#pair-workspace .pair-legend').cloneNode(true));
$('pair-preview').addEventListener('click', previewPair);
$('pair-overlay-toggle').addEventListener('click', () => {
  pairTolerance.visible = !pairTolerance.visible;
  updatePairCellSelection();
  draw();
});
$('pair-overlay-remove').addEventListener('click', () => removePairOverlay());
$('prepare-bot').addEventListener('click', prepareBot);
$('prepare-cancel').addEventListener('click', () => { invalidatePrepared('Подготовка отменена. Исходный JSON и ручные нажатия сохранены.'); controls(); });
$('output-track-id').addEventListener('input', () => { invalidatePrepared('ID результата изменён. Подготовьте бота заново.'); controls(); });
$('download-prepared').addEventListener('click', () => {
  if (!preparation.result || preparation.pending || !state.valid || state.moving) return;
  download(preparation.result.preparedJson, `${preparation.result.outputTrackId}.bot.track.json`);
  notice('Скачан отдельный JSON для игры с проверенными botReferences. В игре выберите «Импорт JSON», затем подготовленное управление. Исходный текст редактора сохранён.');
});
$('timeline').addEventListener('input', () => { stopPlayback(); seek(Number($('timeline').value)); });
$('zoom-in').addEventListener('click', () => zoom(1.25)); $('zoom-out').addEventListener('click', () => zoom(.8));
$('fit').addEventListener('click', () => { state.zoom = 1; draw(); });
$('pan').addEventListener('input', () => { if (state.analysis) { state.left = Number($('pan').value) / 1000 * Math.max(0, state.analysis.length + 50 - dimensions().width) - 20; draw(); } });
$('overview').addEventListener('click', event => {
  if (!state.analysis) return;
  const overview = $('overview');
  if (state.pickingStart) { pickStart(event, overview, true); return; }
  if (!state.addKind && !drag && !live && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey
    && !spacePan && !objectEdit.dirty && !objectEdit.committing && state.route.visible && state.route.result) {
    const transform = overview.getScreenCTM();
    const activatedMarker = event.detail === 0 ? inputAtRouteMarker(event.target) : null;
    const input = activatedMarker ?? (transform && nearestRouteInput(state.route.result.inputs, { x: event.clientX, y: event.clientY },
      ({ x, y }) => new DOMPoint(x, y).matrixTransform(transform)));
    if (input) { selectTrajectoryCommand(input, .5); return; }
  }
  const box = overview.getBoundingClientRect();
  state.left = (event.clientX - box.left) / box.width * (state.analysis.length + 50) - 20 - dimensions().width / 2;
  draw();
});
function inputAtRouteMarker(target) {
  const index = target.closest('[data-route-input]')?.dataset.routeInput;
  return state.route.result?.inputs[index];
}
$('diagram').addEventListener('click', event => {
  if (event.detail !== 0 || state.pickingStart || state.addKind) return;
  const input = inputAtRouteMarker(event.target);
  if (input && !objectEditBlocked()) selectTrajectoryCommand(input);
});
for (const id of ['overview', 'diagram']) $(id).addEventListener('keydown', event => {
  if (event.key !== 'Enter' && event.key !== ' ') return;
  const input = inputAtRouteMarker(event.target);
  if (!input || state.pickingStart || state.addKind || objectEditBlocked()) return;
  event.preventDefault();
  selectTrajectoryCommand(input, id === 'overview' ? .5 : .4);
  $(id).querySelector(`[data-route-input="${input.index}"]`)?.focus({ preventScroll: true });
});
$('diagram').addEventListener('wheel', event => { if (!state.analysis) return; event.preventDefault(); const box = $('diagram').getBoundingClientRect(); zoom(event.deltaY < 0 ? 1.12 : 1 / 1.12, (event.clientX - box.left) / box.width); }, { passive: false });
function selectedObject(analysis = state.preview || state.analysis) {
  return state.selection && analysis?.[state.selection.collection]?.find(object => object.id === state.selection.id);
}
function updateSelectionPanel() {
  const object = selectedObject(), target = state.selection;
  const count = object ? 1 + (target.collection === 'obstacles' ? (state.preview || state.analysis).rewards.filter(r => r.obstacleId === object.id).length : 0) : 0;
  $('selection-name').textContent = object ? object.id : 'Объект не выбран';
  $('selection-help').textContent = !object ? 'Нажмите объект, чтобы увидеть ручки изменения размера.' : object.shape === 'finish' ? 'Размер финишного рывка фиксирован. Можно переносить и удалять.' : target.collection === 'obstacles' && object.kind === 'gate' ? 'Ручки: ширина частей и верхняя / нижняя границы проёма.' : object.shape === 'landing' ? 'Ручки по краям меняют ширину посадочного окна на камне.' : target.collection === 'obstacles' ? 'Ручки: ширина, высота и угол. Связанные награды сохраняются.' : 'Квадратная ручка справа меняет радиус физической области.';
  $('delete-object').textContent = count > 1 ? `Удалить группу · ${count} объектов` : 'Удалить объект';
  $('delete-object').disabled = !object || !state.valid || state.moving || !!state.addKind;
  $('cancel-add').hidden = !state.addKind;
  updateObjectInspector();
  const chosenKind = state.addKind;
  const linked = ['landing', 'positive-before', 'positive-after'].includes(chosenKind), anchorName = chosenKind === 'landing' ? 'камень' : 'препятствие';
  $('add-help').textContent = state.addKind ? linked ? `Нажмите ${anchorName} на подробной схеме; либо выберите его заранее и нажмите место награды. Повторное нажатие кнопки палитры или Esc — отмена.` : 'Нажмите место на подробной схеме. Будет добавлен один объект, затем вернётся обычный режим. Повторное нажатие кнопки палитры или Esc — отмена.' : 'Нажмите кнопку палитры, затем место на подробной схеме. Сейчас доступны навигация и редактирование объектов.';
  $('diagram').classList.toggle('adding-object', !!state.addKind);
}
function updateObjectInspector() {
  let source = null, status = 'Выберите объект на схеме.';
  if (!state.valid && !objectEdit.committing) status = 'Дождитесь проверки текущего JSON или исправьте его.';
  else if (state.selection) {
    try { source = selectedObjectSource($('json').value, state.selection); }
    catch { status = 'Не удалось прочитать объект из текущего исходника.'; }
    if (source) status = 'Правки применяются после проверки Unity. Остальные параметры доступны в JSON.';
  }
  updateObjectPropertiesPanel(source, status);
  if ((objectEdit.dirty || objectEdit.committing) && objectEdit.kind === 'json') {
    $('object-json').hidden = false; $('object-json').readOnly = false;
    $('select-object-json').disabled = false; $('reset-object-json').disabled = false;
    $('object-json-status').textContent = objectEdit.error || 'Проверяем правку…';
    $('object-json-status').classList.toggle('storage-error', !!objectEdit.error);
    return;
  }
  const focusedEquivalent = document.activeElement === $('object-json') && source && $('object-json').value.trim() === source;
  if (!focusedEquivalent && $('object-json').value !== (source || '')) $('object-json').value = source || '';
  $('object-json').hidden = !source;
  $('object-json').readOnly = !source || !canEditFields('json');
  $('select-object-json').disabled = !source;
  $('reset-object-json').disabled = true;
  $('object-json-status').textContent = blockedFieldStatus() || status;
  $('object-json-status').classList.remove('storage-error');
}
function canEditFields(kind) {
  return state.valid && !state.moving && !drag && loadingTrack === null && (!objectEdit.dirty || objectEdit.kind === kind);
}
function blockedFieldStatus() {
  if (objectEdit.dirty) return 'Завершите или сбросьте текущую правку в другой панели.';
  if (state.moving || drag) return 'Завершите жест на схеме.';
  if (loadingTrack !== null) return 'Загружаем трассу…';
  return '';
}
function syncField(input, value) {
  if (input.type === 'checkbox') input.checked = value === true;
  else if (input.type === 'number' && document.activeElement === input && input.valueAsNumber === value) return;
  else if (input.value !== String(value ?? '')) input.value = value ?? '';
}
function updateTrackMetadataFields() {
  const editing = objectEdit.dirty && objectEdit.kind === 'metadata';
  for (const key of metadataInputs) {
    const input = $(`track-${key}`);
    if (!editing && !objectEdit.committing) syncField(input, state.analysis?.[key]);
    input.disabled = !state.analysis;
    input.readOnly = !canEditFields('metadata');
  }
  $('reset-track-metadata').hidden = !editing;
  const status = editing ? objectEdit.error || 'Проверяем свойства уровня…'
    : blockedFieldStatus() || (state.valid ? '' : 'Свойства доступны после проверки JSON трассы.');
  message('track-metadata-status', status, !!status);
  $('track-metadata-status').classList.toggle('storage-error', editing && !!objectEdit.error);
}
let propertySchema = '';
function updateObjectPropertiesPanel(source, status) {
  const panel = $('object-properties'), editing = objectEdit.dirty && objectEdit.kind === 'properties';
  $('reset-object-properties').hidden = !editing;
  $('object-properties-status').textContent = editing ? objectEdit.error || 'Проверяем свойства объекта…' : blockedFieldStatus() || status;
  $('object-properties-status').classList.toggle('storage-error', editing && !!objectEdit.error);
  if (objectEdit.committing) return;
  if (editing) {
    for (const input of panel.querySelectorAll('[data-property]')) input.disabled = !canEditFields('properties');
    return;
  }
  const object = source && JSON.parse(source);
  const fields = object ? objectPropertyFields(object, state.selection.collection, state.analysis.obstacles) : [];
  const schema = JSON.stringify(fields);
  if (propertySchema !== schema) {
    propertySchema = schema;
    panel.replaceChildren(...fields.map(field => {
      const label = document.createElement('label'), title = document.createElement('span');
      label.className = field.type === 'checkbox' ? 'property-field property-check' : 'property-field';
      title.textContent = field.label;
      const input = document.createElement(field.type === 'select' ? 'select' : 'input');
      if (field.type === 'select') input.append(...field.choices.map(([value, text]) => new Option(text, value)));
      else if (field.type === 'id') { input.type = 'text'; input.maxLength = 128; input.spellcheck = false; }
      else { input.type = field.type; if (field.type === 'number') input.step = 'any'; }
      input.dataset.property = field.key;
      label.append(...(field.type === 'checkbox' ? [input, title] : [title, input]));
      return label;
    }));
  }
  for (const [index, input] of [...panel.querySelectorAll('[data-property]')].entries()) {
    const field = fields[index];
    input.disabled = !canEditFields('properties');
    syncField(input, object[field.key] ?? field.defaultValue);
  }
}
function resetObjectEdit() {
  objectEdit.revision++; clearTimeout(objectEdit.timer); objectEdit.abort?.abort();
  objectEdit.abort = null; objectEdit.dirty = false; objectEdit.committing = false; objectEdit.error = '';
  objectEdit.source = null; objectEdit.target = null; objectEdit.kind = null; objectEdit.fields = {};
}
function objectEditBlocked() {
  if (!objectEdit.dirty) return false;
  notice('Сначала исправьте текущую правку или нажмите «Сбросить правку».', true);
  return true;
}
function editFields(kind, field, value) {
  if (!canEditFields(kind) || (kind !== 'metadata' && !state.selection)) return;
  cancelPendingRouteWork('Поиск остановлен при изменении трассы.');
  if (!objectEdit.dirty) {
    objectEdit.source = $('json').value; objectEdit.target = state.selection && { ...state.selection };
    objectEdit.kind = kind; objectEdit.fields = {};
  }
  if (field) objectEdit.fields[field] = value;
  objectEdit.dirty = true; objectEdit.error = ''; objectEdit.revision++;
  clearTimeout(objectEdit.timer); objectEdit.abort?.abort(); state.addKind = null;
  objectEdit.timer = setTimeout(validateFields, 300);
  controls(); draw();
}
async function validateFields() {
  if (!objectEdit.dirty || loadingTrack !== null) return;
  const { revision, source: before, target, kind, fields } = objectEdit;
  const analysisRevision = state.analysisRevision.value, loadRevision = state.loadRevision, buffer = $('object-json').value;
  const abort = new AbortController(); objectEdit.abort = abort;
  const current = () => revision === objectEdit.revision && objectEdit.dirty && loadRevision === state.loadRevision && before === $('json').value
    && state.analysisRevision.current(analysisRevision) && state.selection?.collection === target?.collection && state.selection?.id === target?.id;
  try {
    const candidate = kind === 'metadata' ? { source: updateTrackMetadata(before, fields, catalogIds()), target }
      : kind === 'properties' ? updateObjectProperties(before, target, fields) : replaceObjectSource(before, target, buffer);
    if (candidate.source === before) { if (current()) { resetObjectEdit(); controls(); } return; }
    if (new TextEncoder().encode(candidate.source).length > 2 * 1024 * 1024) throw new Error('Файл больше 2 МиБ.');
    const result = await api('/api/analyze', { json: candidate.source }, abort.signal);
    if (!current()) return;
    if (kind !== 'metadata' && !result[candidate.target.collection]?.some(object => object.id === candidate.target.id)) throw new Error('Unity не вернул изменённый объект.');
    const description = kind === 'metadata' ? 'Изменены свойства уровня' : kind === 'properties' ? 'Изменены свойства объекта' : 'Изменён JSON объекта';
    remember(before, candidate.source, description, target, candidate.target);
    $('json').value = candidate.source; state.selection = candidate.target;
    objectEdit.dirty = false; objectEdit.committing = true; objectEdit.abort = null;
    edited(true, { preserveHistory: true, preserveObjectBuffer: true, acceptedAnalysis: result, preserveView: kind === 'metadata' });
    if (kind !== 'metadata') $('object-detail').textContent = `${candidate.target.id} · параметры обновлены`;
  } catch (error) {
    if (!current() || error.name === 'AbortError') return;
    objectEdit.error = `Не применено: ${error.message} Исправьте значение или сбросьте правку.`;
    controls();
  }
}
function drawSelection(parent) {
  const analysis = state.preview || state.analysis, object = selectedObject(analysis);
  if (!object) return;
  const scale = dimensions().scale;
  const frame = bounds => parent.append(el('rect', { x: bounds.x - 2 / scale, y: bounds.y - 2 / scale, width: bounds.width + 4 / scale, height: Math.max(bounds.height, 1) + 4 / scale, fill: 'none', stroke: '#806496', 'stroke-width': 1.3 / scale, 'stroke-dasharray': `${4 / scale} ${3 / scale}`, 'pointer-events': 'none' }));
  if (state.selection.collection === 'obstacles') for (const band of object.bands) frame(band);
  else if (object.shape === 'landing') frame({ x: object.left, y: object.planeY - 1, width: object.right - object.left, height: 2 });
  else if (object.shape === 'finish') frame({ x: object.x - 12, y: object.y - 10, width: 24, height: 20 });
  else frame({ x: object.x - object.radius, y: object.y - object.radius, width: 2 * object.radius, height: 2 * object.radius });
  if (!state.valid || state.addKind) return;
  for (const handle of getResizeHandles(analysis, state.selection)) {
    const size = 9 / scale;
    const rect = el('rect', { x: handle.x - size / 2, y: handle.y - size / 2, width: size, height: size, rx: 1 / scale,
      fill: '#fffdf7', stroke: '#806496', 'stroke-width': 1.6 / scale, class: 'resize-handle',
      'data-handle': handle.id, 'data-collection': state.selection.collection, 'data-id': state.selection.id,
      tabindex: 0, role: 'img', 'aria-label': handle.label });
    rect.style.cursor = handle.cursor; rect.append(el('title', {}, handle.label)); parent.append(rect);
  }
}
function remember(before, after, description, beforeTarget, afterTarget) {
  state.history.push({ before, after, description, beforeTarget, afterTarget }); state.future = [];
  const size = () => state.history.reduce((sum, step) => sum + step.before.length + step.after.length, 0);
  while (state.history.length > 20 || (state.history.length > 1 && size() > 8 * 1024 * 1024)) state.history.shift();
}
function commitVisual(before, after, description, preview = null, target = state.selection, revision = state.analysisRevision.value) {
  if (!state.valid || state.moving || $('json').value !== before || !state.analysisRevision.current(revision)) throw new Error('Текст изменился или ещё не проверен. Дождитесь проверки текущего JSON.');
  if (before === after) return false;
  remember(before, after, description);
  $('json').value = after; state.selection = target; state.preview = preview;
  edited(true, { keepPreview: !!preview, preserveHistory: true });
  $('object-detail').textContent = `${description} · записано в JSON`;
  notice(`${description.replace(/[.\s]+$/, '')}. JSON проверяется; эталоны сохранены в исходнике и могут устареть.`);
  return true;
}
function historyStep(redo = false) {
  if (live) return;
  if (objectEditBlocked()) return;
  if (state.moving || drag) return;
  const from = redo ? state.future : state.history, to = redo ? state.history : state.future, step = from.at(-1);
  if (!step || $('json').value !== (redo ? step.before : step.after)) return;
  from.pop(); to.push(step); state.addKind = null;
  if (step.beforeTarget) state.selection = redo ? step.afterTarget : step.beforeTarget;
  $('json').value = redo ? step.after : step.before;
  edited(true, { preserveHistory: true });
  notice(`${redo ? 'Повторено' : 'Отменено'}: ${step.description}. Восстановлен точный текст JSON.`);
}
function removeSelection() {
  if (live) return;
  if (objectEditBlocked()) return;
  if (!state.valid || state.moving || !selectedObject() || state.addKind) return;
  try {
    const before = $('json').value, revision = state.analysisRevision.value;
    const result = deleteObject(before, state.analysis, state.selection);
    commitVisual(before, result.source, result.description, null, null, revision);
  } catch (error) { notice(error.message, true); }
}
function placeObject(gesture) {
  if (!state.valid || $('json').value !== gesture.source || !state.analysisRevision.current(gesture.revision)) return;
  try {
    const result = createObject(gesture.source, gesture.analysis, gesture.addKind, gesture.worldX, gesture.worldY, gesture.anchorId);
    state.addKind = null;
    commitVisual(gesture.source, result.source, result.description, null, result.target, gesture.revision);
  } catch (error) { notice(error.message, true); controls(); }
}
function finishDrag(commit) {
  if (!drag) return;
  const gesture = drag; drag = null;
  state.moving = false; $('json').readOnly = false;
  $('diagram').classList.remove('dragging', 'moving-object', 'resizing-object');
  if ($('diagram').hasPointerCapture(gesture.pointerId)) $('diagram').releasePointerCapture(gesture.pointerId);
  if (gesture.mode === 'add') {
    if (commit && !gesture.active) placeObject(gesture);
    else if (!commit) notice('Добавление отменено. JSON не изменён.');
  } else if (['object', 'resize'].includes(gesture.mode) && gesture.active) {
    if (commit && gesture.plan?.changes.length && $('json').value === gesture.source && state.analysisRevision.current(gesture.revision)) {
      try {
        const after = patchNumbers(gesture.source, gesture.plan.changes);
        const description = gesture.mode === 'resize' ? gesture.plan.description : `${gesture.target.id} · перемещено: ΔX ${number(gesture.plan.dx, 1)}, ΔY ${number(gesture.plan.dy, 1)}`;
        commitVisual(gesture.source, after, description, gesture.plan.analysis, gesture.target, gesture.revision);
      } catch (error) { state.preview = null; $('object-detail').textContent = 'Правка не записана. JSON не изменён.'; notice(`Правка не записана: ${error.message}`, true); }
    } else {
      state.preview = null;
      $('object-detail').textContent = commit ? 'Объект не изменился.' : 'Правка отменена. JSON не изменён.';
      notice(commit ? 'Объект не изменился.' : 'Правка отменена. JSON не изменён.');
    }
  } else if (!gesture.active && gesture.description) $('object-detail').textContent = gesture.description;
  if (!commit && gesture.mode === 'pan') state.left = gesture.left;
  controls(); draw();
  if (commit && !gesture.active && gesture.routeInput) selectTrajectoryCommand(gesture.routeInput);
}
$('diagram').addEventListener('pointerdown', event => {
  if (!state.analysis || event.button !== 0 || drag) return;
  if (state.pickingStart) {
    event.preventDefault();
    pickStart(event, $('diagram'));
    return;
  }
  const handleElement = event.target.closest('[data-handle]'), group = event.target.closest('.object');
  const collection = handleElement?.dataset.collection || group?.dataset.collection;
  const id = handleElement?.dataset.id || group?.dataset.id;
  const mode = event.altKey || spacePan ? 'pan' : state.addKind ? 'add' : handleElement ? 'resize' : collection ? 'object' : 'pan';
  const routeInput = mode === 'pan' && !event.altKey && !spacePan ? inputAtRouteMarker(event.target) : null;
  if (objectEditBlocked()) return;
  if (mode !== 'pan' && !state.valid) { notice('Для редактирования объектов сначала исправьте JSON и дождитесь проверки текущего текста.', true); return; }
  try {
    const view = dimensions(), box = $('diagram').getBoundingClientRect(), target = { collection, id };
    if (mode === 'object' || mode === 'resize') state.selection = target;
    else if (mode === 'pan' && !collection && !event.altKey && !spacePan) state.selection = null;
    const anchorAllowed = o => state.addKind !== 'landing' || o.kind !== 'gate';
    const clickedAnchor = collection === 'obstacles' && state.analysis.obstacles.find(o => o.id === id && anchorAllowed(o));
    const selectedAnchor = state.selection?.collection === 'obstacles' && state.analysis.obstacles.find(o => o.id === state.selection.id && anchorAllowed(o));
    drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, left: state.left, scale: view.scale,
      mode, active: false, target, description: group?.getAttribute('aria-label'), routeInput,
      handle: handleElement?.dataset.handle, handlePoint: mode === 'resize' ? getResizeHandles(state.analysis, target).find(h => h.id === handleElement.dataset.handle) : null,
      worldX: view.left + (event.clientX - box.left) / view.scale, worldY: view.top + (event.clientY - box.top) / view.scale,
      addKind: state.addKind, anchorId: clickedAnchor?.id || selectedAnchor?.id || null,
      source: $('json').value, document: mode !== 'pan' && mode !== 'add' ? JSON.parse($('json').value) : null,
      analysis: state.analysis, revision: state.analysisRevision.value, plan: null };
    $('diagram').setPointerCapture(event.pointerId); $('diagram').focus({ preventScroll: true }); event.preventDefault();
    controls(); draw();
  } catch (error) { drag = null; notice(`Перемещение недоступно: ${error.message}`, true); }
});
$('diagram').addEventListener('pointermove', event => {
  if (!drag || event.pointerId !== drag.pointerId) return;
  const pixelDx = event.clientX - drag.x, pixelDy = event.clientY - drag.y;
  if (!drag.active && Math.hypot(pixelDx, pixelDy) < 4) return;
  if (drag.mode === 'add') { drag.active = true; return; }
  if (!drag.active) {
    drag.active = true;
    if (drag.mode === 'object' || drag.mode === 'resize') {
      state.moving = true; $('json').readOnly = true;
      if (routeSearch.pending) cancelRouteSearch('Поиск остановлен при изменении трассы.');
      else routeSearch.progress = null;
      invalidateReplay('Объект редактируется. После правки постройте траекторию заново.'); controls();
      $('diagram').classList.add(drag.mode === 'resize' ? 'resizing-object' : 'moving-object');
    } else $('diagram').classList.add('dragging');
  }
  if (drag.mode === 'pan') { state.left = drag.left - pixelDx / drag.scale; draw(); return; }
  try {
    drag.plan = drag.mode === 'resize' ? resizeObject(drag.document, drag.analysis, drag.target, drag.handle, drag.handlePoint.x + pixelDx / drag.scale, drag.handlePoint.y + pixelDy / drag.scale)
      : translateObject(drag.document, drag.analysis, drag.target, pixelDx / drag.scale, pixelDy / drag.scale);
    state.preview = drag.plan.analysis; draw();
    $('object-detail').textContent = `${drag.mode === 'resize' ? drag.plan.description : `${drag.target.id} · ΔX ${number(drag.plan.dx, 1)}, ΔY ${number(drag.plan.dy, 1)}`} · отпустите для записи · Esc — отмена`;
  } catch (error) { finishDrag(false); notice(error.message, true); }
});
$('diagram').addEventListener('pointerup', event => { if (drag?.pointerId === event.pointerId) finishDrag(true); });
for (const type of ['pointercancel', 'lostpointercapture']) $('diagram').addEventListener(type, event => { if (drag?.pointerId === event.pointerId) finishDrag(false); });
window.addEventListener('keydown', event => {
  if (live) return;
  if (event.key === 'Escape' && state.pickingStart) { event.preventDefault(); state.pickingStart = false; controls(); return; }
  if (event.key === 'Escape' && drag) { event.preventDefault(); if (drag.mode === 'add') state.addKind = null; finishDrag(false); return; }
  if (event.key === 'Escape' && state.addKind) { event.preventDefault(); state.addKind = null; controls(); draw(); notice('Добавление отменено.'); return; }
  if (event.key === 'Delete' && !event.target.closest('textarea,input,select,[contenteditable="true"]') && state.selection) { event.preventDefault(); removeSelection(); return; }
  if (event.code === 'Space' && !event.defaultPrevented && !event.target.closest('textarea,input,select,button')) { spacePan = true; event.preventDefault(); $('diagram').classList.add('pan-ready'); }
});
window.addEventListener('keyup', event => { if (event.code === 'Space') { spacePan = false; $('diagram').classList.remove('pan-ready'); } });
window.addEventListener('blur', () => { spacePan = false; $('diagram').classList.remove('pan-ready'); finishDrag(false); });
$('undo-move').addEventListener('click', () => historyStep());
$('redo').addEventListener('click', () => historyStep(true));
$('delete-object').addEventListener('click', removeSelection);
for (const button of document.querySelectorAll('[data-add-kind]')) button.addEventListener('click', () => {
  if (!state.valid || state.moving || objectEditBlocked()) return;
  state.pickingStart = false;
  state.addKind = state.addKind === button.dataset.addKind ? null : button.dataset.addKind;
  controls(); draw();
});
$('toggle-source').addEventListener('click', () => {
  const collapsed = !$('source-panel').hidden;
  $('source-panel').hidden = collapsed;
  $('toggle-source').setAttribute('aria-expanded', String(!collapsed));
  $('toggle-source').textContent = collapsed ? 'Показать JSON трассы' : 'Скрыть JSON трассы';
  draw();
});
function selectObjectTab(properties, focus = false) {
  for (const [id, selected] of [['object-properties-tab', properties], ['object-json-tab', !properties]]) {
    const button = $(id);
    button.setAttribute('aria-selected', String(selected)); button.tabIndex = selected ? 0 : -1;
    if (selected && focus) button.focus();
  }
  $('object-properties').hidden = !properties;
  $('object-json-panel').hidden = properties;
}
for (const id of ['object-properties-tab', 'object-json-tab']) {
  const button = $(id);
  button.addEventListener('click', () => selectObjectTab(id === 'object-properties-tab'));
  button.addEventListener('keydown', event => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault(); selectObjectTab(event.key === 'Home' || (event.key !== 'End' && id === 'object-json-tab'), true);
  });
}
function discardFieldEdit() { resetObjectEdit(); controls(); draw(); }
for (const key of metadataInputs) {
  const input = $(`track-${key}`);
  input.addEventListener('input', () => editFields('metadata', key, inputValue(input)));
}
$('object-properties').addEventListener('input', event => {
  const input = event.target;
  if (input.dataset.property) editFields('properties', input.dataset.property, inputValue(input));
});
for (const id of ['reset-track-metadata', 'reset-object-properties', 'reset-object-json']) $(id).addEventListener('click', discardFieldEdit);
document.addEventListener('keydown', event => {
  if (event.key !== 'Escape') return;
  if (!$('file-menu').hidden) { event.preventDefault(); setFileMenu(false); $('file-menu-toggle').focus(); }
  if (objectEdit.dirty && (event.target.matches?.('[data-property], #object-json, #track-name, #track-id, #track-length'))) {
    event.preventDefault(); discardFieldEdit();
  }
});
$('select-object-json').addEventListener('click', () => { $('object-json').focus(); $('object-json').select(); });
$('object-json').addEventListener('input', () => editFields('json'));
$('cancel-add').addEventListener('click', () => { state.addKind = null; controls(); draw(); });
$('diagram').addEventListener('keydown', event => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') { event.preventDefault(); historyStep(event.shiftKey); }
});
$('diagram').addEventListener('keydown', event => { if (!state.analysis) return; if (event.key === '+' || event.key === '=') zoom(1.25); else if (event.key === '-') zoom(.8); else if (event.key === 'ArrowRight') { state.left += dimensions().width / 5; draw(); } else if (event.key === 'ArrowLeft') { state.left -= dimensions().width / 5; draw(); } else return; event.preventDefault(); });
$('live-start').addEventListener('click', enterLive);
$('live-start-x').addEventListener('input', () => { state.pickingStart = false; updateLiveStartControls(); draw(); });
$('live-pick-start').addEventListener('click', () => {
  if (!canStartLive()) return;
  state.pickingStart = !state.pickingStart;
  if (state.pickingStart) state.addKind = null;
  controls(); draw();
});
$('live-reset-start').addEventListener('click', () => { resetStartChoice(); updateLiveStartControls(); draw(); });
$('live-exit').addEventListener('click', exitLive);
$('live-save-inputs').addEventListener('click', saveLiveInputs);
$('live-restart').addEventListener('click', () => { if (live) restartLive(); });
$('live-pause').addEventListener('click', () => {
  const connection = live?.connection;
  if (!connection) return;
  if (!connection.paused) connection.pause();
  else if (!document.hidden && connection.resume()) $('live-mode').focus({ preventScroll: true });
});
$('live-input').addEventListener('pointerdown', event => {
  if (event.button !== 0) return;
  event.preventDefault();
  live?.connection.press(event.timeStamp);
});
$('live-input').addEventListener('click', event => {
  // The pointer already acted on pointerdown. Keyboard and assistive activation can click without a pointer.
  if (event.detail === 0) live?.connection.press(event.timeStamp);
});
const liveKeys = new Set();
window.addEventListener('keydown', event => {
  if (!live || event.altKey || event.ctrlKey || event.metaKey) return;
  const restartKey = event.code === 'KeyR';
  if (restartKey && (event.isComposing || event.target.closest?.('input, textarea, select, [contenteditable]:not([contenteditable="false"])'))) return;
  const actionButton = event.target === $('live-input');
  const enterAction = actionButton && (event.code === 'Enter' || event.code === 'NumpadEnter');
  if (!restartKey && !enterAction && event.code !== 'KeyA' && event.code !== 'Space') return;
  if (event.code === 'Space' && !actionButton && event.target.closest?.('button')) return;
  event.preventDefault();
  if (event.repeat || liveKeys.has(event.code)) return;
  liveKeys.add(event.code);
  if (restartKey) restartLive(true);
  else live.connection.press(event.timeStamp);
}, { capture: true });
window.addEventListener('keyup', event => { liveKeys.delete(event.code); });
function pauseLiveOnFocusLoss() {
  liveKeys.clear();
  live?.connection.pause('Окно потеряло фокус. Нажмите «Продолжить».');
}
window.addEventListener('blur', pauseLiveOnFocusLoss);
document.addEventListener('visibilitychange', () => { if (document.hidden) pauseLiveOnFocusLoss(); });
new ResizeObserver(() => draw()).observe($('diagram'));
window.addEventListener('beforeunload', event => { if (objectEdit.dirty || $('json').value !== state.sourceText) { event.preventDefault(); event.returnValue = ''; } });
window.addEventListener('pagehide', saveDraft);
window.addEventListener('storage', event => {
  if (event.key !== DRAFT_KEY) return;
  clearTimeout(draftTimer);
  if (event.newValue !== null) offerDraft();
  else {
    draftBlocked = true; pendingDraft = null; $('draft-offer').hidden = false; $('draft-restore').hidden = true;
    $('draft-offer-text').textContent = 'Сохранение удалено в другой вкладке. Текущий текст оставлен в редакторе. Нажмите «Удалить сохранённый черновик», чтобы продолжить автосохранение текущих правок.';
    draftStatus('Автосохранение приостановлено: другая вкладка изменила черновик.');
  }
});
$('draft-restore').addEventListener('click', () => {
  if (!pendingDraft) return;
  resetStartChoice();
  const draft = pendingDraft;
  draftBlocked = false; pendingDraft = null; $('draft-offer').hidden = true; $('catalog').value = '';
  state.sourceName = draft.filename; state.sourceText = ''; state.referenceHints = [];
  $('filename').textContent = draft.filename; $('json').value = draft.json; $('inputs').value = draft.inputs;
  restoreRunSettings({ ...draft, warningDuration: Number(draft.warning) });
  $('axis').value = draft.axis;
  notice('Черновик восстановлен из этого браузера. Исходный текст сохранён точно; проверяем JSON заново. История визуальных правок очищена.'); edited(true, { clearRoute: true });
});
$('draft-clear').addEventListener('click', () => {
  const result = clearDraft(draftStorage);
  if (result.kind === 'error') { draftStatus('Не удалось удалить сохранение: хранилище браузера недоступно. Редактор продолжает работать; скачайте исходный текст.', true); return; }
  draftBlocked = false; pendingDraft = null; $('draft-offer').hidden = true;
  draftStatus('Прежний черновик удалён из этого браузера.');
  if ($('json').value) scheduleDraft();
});
async function start() {
  restoreRunSettings({ profile: 'Chosen', warningDuration: 0.8, surfaceRunSettings: DEFAULT_SURFACE_RUN_SETTINGS });
  offerDraft();
  controls();
  const allowInitialCatalog = !draftBlocked;
  try {
    const result = await api('/api/catalog'); state.catalog = result.entries;
    $('catalog').replaceChildren(new Option('Выберите трассу…', ''), ...result.entries.map(e => new Option(e.name, e.id)));
    controls();
    if (allowInitialCatalog && !draftBlocked && !draftTouched && result.entries.length && !$('json').value && loadingTrack === null) { const id = result.entries.find(e => e.id === 'mixed-tricks')?.id || result.entries[0].id; $('catalog').value = id; await loadBuiltin(id, false); }
  } catch (error) { $('catalog').replaceChildren(new Option('Каталог недоступен', '')); notice(error.message, true); }
}
start();
