import { clamp, parseInputs, sampleAt, viewMetrics, rewardAppearance, patchNumbers, translateObject, Revision } from './model.mjs';
import { createObject, deleteObject, resizeObject, getResizeHandles } from './edit-model.mjs';
import { DRAFT_KEY, readDraft, writeDraft, clearDraft } from './draft.mjs';

const $ = id => document.getElementById(id);
const svgNS = 'http://www.w3.org/2000/svg';
const number = (n, digits = 0) => Number(n).toLocaleString('ru-RU', { maximumFractionDigits: digits });
const state = { analysis: null, valid: false, catalog: [], zoom: 1, left: -20, replay: null, time: 0, playing: false,
  sourceName: 'track.json', sourceText: '', loadRevision: 0, analysisRevision: new Revision(), replayRevision: new Revision(), debounce: null, analysisAbort: null,
  preview: null, moving: false, history: [], future: [], selection: null, addKind: null };
let drag = null, spacePan = false;
let draftStorage, pendingDraft = null, draftBlocked = false, draftTimer = null, draftTouched = false;
const preparation = { revision: 0, abort: null, pending: false, result: null, sourceId: null };
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
  $('prepare-bot').disabled = !state.valid || state.moving || preparation.pending || !$('output-track-id').value.trim();
  $('output-track-id').disabled = preparation.pending;
  $('prepare-cancel').hidden = !preparation.pending;
  $('download-prepared').disabled = !preparation.result || preparation.pending || !state.valid || state.moving;
}
async function prepareBot() {
  if (!state.valid || state.moving || preparation.pending) return;
  invalidatePrepared(); invalidateReplay('Подготавливаем новый эталон для текущего управления…');
  const revision = preparation.revision, replayRevision = state.replayRevision.value;
  const source = $('json').value, profile = $('profile').value, warning = Number($('warning').value);
  const outputTrackId = $('output-track-id').value.trim(), abort = new AbortController();
  preparation.abort = abort; preparation.pending = true; controls();
  message('prepare-state', 'Поиск ограничен бюджетом. Затем проверяем эталон и настоящего Мастера; это может занять несколько десятков секунд. Отмена прерывает запрос.');
  message('replay-error', '', false);
  const request = { json: source, profiles: [profile], warningDuration: warning, outputTrackId };
  // An optional author seed is only a hint, never a prerequisite for search.
  if ($('axis').value === 'distance') { try { request.inputs = parseInputs($('inputs').value); } catch { /* Invalid manual inputs do not prevent preparation. */ } }
  try {
    const result = await api('/api/prepare-bot', request, abort.signal);
    if (revision !== preparation.revision) return;
    const ready = result.profiles?.find(p => p.profileId === profile && p.status === 'ready');
    if (result.status !== 'prepared' || !result.preparedJson || !ready) {
      message('prepare-state', `${result.message || 'В пределах бюджета подходящий маршрут не найден.'} ${result.profiles?.map(p => p.message).filter(Boolean).join(' ') || ''} Это не доказывает непроходимость карты. Исходный JSON сохранён.`);
      return;
    }
    preparation.result = result;
    $('prepare-result').hidden = false;
    $('prepare-summary').textContent = `${$('profile').selectedOptions[0].textContent} · ID ${result.outputTrackId} · эталон ${number(ready.finishTime, 2)} с · Мастер проверен. ${number(ready.expandedStates)} состояний; поиск ${number(ready.elapsedMilliseconds / 1000, 2)} с. Оптимальность не утверждается.`;
    const hiddenVerification = state.analysis.warningMode === 'selected' && state.analysis.obstacles.some(o => o.hidden)
      ? 'Проверены все пять длительностей предупреждения.' : state.analysis.warningMode === 'authored' ? 'Проверены индивидуальные таймеры скрытых камней.' : '';
    $('prepare-rewards').textContent = `Выбранные положительные награды: ${ready.rewardIds?.length ? ready.rewardIds.join(', ') : 'нет'}. Финишный рывок проверен полностью. ${hiddenVerification}`;
    message('prepare-state', result.message || 'Подготовленный JSON готов для импорта в игру. Исходный текст в редакторе не изменён.');
    controls();
    try {
      const route = await api('/api/replay', { json: result.preparedJson, profile, warningDuration: warning, axis: 'distance', inputs: ready.inputs }, abort.signal);
      if (revision !== preparation.revision || !state.replayRevision.current(replayRevision)) return;
      showResult(route);
      message('replay-state', `Подготовленный эталон · ${$('profile').selectedOptions[0].textContent} · нажатия по X. Список ручных нажатий выше сохранён; на схеме показан найденный маршрут.`);
    } catch (error) {
      if (revision !== preparation.revision || error.name === 'AbortError') return;
      message('replay-error', `JSON подготовлен, но траекторию не удалось показать: ${error.message}`);
    }
  } catch (error) {
    if (revision !== preparation.revision || error.name === 'AbortError') return;
    message('prepare-state', `Подготовка не выполнена: ${error.message} Исходный JSON сохранён.`);
  } finally {
    if (revision === preparation.revision) { preparation.pending = false; preparation.abort = null; controls(); }
  }
}
function draftStatus(text, error = false) { $('draft-status').textContent = text; $('draft-status').classList.toggle('storage-error', error); }
function draftValue() { return { json: $('json').value, inputs: $('inputs').value, filename: state.sourceName, profile: $('profile').value, axis: $('axis').value, warning: $('warning').value }; }
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
  if (!response.ok) throw new Error(body.error || `Ошибка сервера: ${response.status}`);
  return body;
}
function badge(text, kind) { $('validation-badge').textContent = text; $('validation-badge').className = `badge ${kind}`; }
function controls() {
  updatePrepareControls();
  $('download').disabled = !state.valid || state.moving;
  $('replay').disabled = !state.valid || state.moving;
  const a = state.analysis;
  $('reference').disabled = !state.valid || state.moving || !a?.references?.some(r => r.profileId === $('profile').value);
  $('undo-move').disabled = state.moving || !state.history.length || state.history.at(-1).after !== $('json').value;
  $('redo').disabled = state.moving || !state.future.length || state.future.at(-1).before !== $('json').value;
  $('add-type').disabled = !state.valid || state.moving;
  $('add-object').disabled = !state.valid || state.moving;
  updateSelectionPanel();
  $('download').textContent = a?.staleReferences && state.valid ? '↓ Скачать без эталонов' : '↓ Скачать JSON';
}
function invalidateReplay(reason) {
  const hadReplay = !!state.replay || state.playing || !$('playback').hidden;
  state.replayRevision.bump();
  stopPlayback(); state.replay = null; state.time = 0;
  $('playback').hidden = true; $('replay').textContent = '▶ Построить траекторию';
  if (hadReplay) { message('replay-state', reason || 'Параметры изменены. Постройте траекторию заново.'); $('replay-state').className = 'replay-state dirty'; }
  else if (reason) message('replay-state', reason);
  draw();
}
function edited(immediate = false, options = {}) {
  if (drag) finishDrag(false);
  if (!options.keepPreview) state.preview = null;
  if (!options.preserveHistory) {
    const hadHistory = state.history.length || state.future.length;
    state.history = []; state.future = []; state.selection = null; state.addKind = null;
    if (hadHistory) notice('Текст изменён или загружен файл. История визуальных правок очищена.');
  }
  state.loadRevision++;
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
  invalidateReplay('JSON изменён. Для новой геометрии нужно новое прохождение.');
  state.debounce = setTimeout(analyze, immediate ? 0 : 300);
}
async function analyze() {
  const revision = state.analysisRevision.value;
  const text = $('json').value;
  const abort = new AbortController(); state.analysisAbort = abort;
  try {
    if (new TextEncoder().encode(text).length > 2 * 1024 * 1024) throw new Error('Файл больше 2 МиБ.');
    JSON.parse(text);
    const result = await api('/api/analyze', { json: text }, abort.signal);
    if (!state.analysisRevision.current(revision)) return;
    const newTrack = state.analysis?.id !== result.id;
    state.preview = null; state.analysis = result; state.valid = true;
    if (state.selection && !selectedObject(result)) state.selection = null;
    if (newTrack) { state.left = -20; state.zoom = 1; }
    $('stale').hidden = true; $('empty-diagram').hidden = true;
    badge(result.staleReferences ? 'Геометрия верна' : 'JSON корректен', 'good');
    $('validation-message').textContent = `${result.obstacles.length} препятствий · ${result.rewards.length} наград · формат принят Unity`;
    $('track-title').textContent = result.name;
    $('track-meta').textContent = `${number(result.length)} × ${number(result.corridorHeight)} ед. · Y ↓`;
    const fast = $('profile').querySelector('[value="FastFlip"]'); fast.disabled = result.profilePolicy === 'chosen-only';
    if (fast.disabled && $('profile').value === 'FastFlip') {
      $('profile').value = 'Chosen';
      scheduleDraft();
      notice('Эта трасса разрешает только Основное управление. Профиль изменён с «Быстрый переворот» на «Основной»; прежняя траектория сброшена.');
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
function loadText(text, filename) {
  state.sourceName = filename; state.sourceText = text;
  $('json').value = text; $('filename').textContent = filename;
  $('inputs').value = '[]'; $('axis').value = 'distance';
  notice(); edited(true);
}
async function loadBuiltin(id) {
  if (!id) return;
  const revision = ++state.loadRevision;
  try {
    const result = await api(`/api/catalog/${encodeURIComponent(id)}`);
    if (revision !== state.loadRevision) return;
    loadText(result.json, state.catalog.find(e => e.id === id)?.file || `${id}.json`);
  } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
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
  g.addEventListener('click', show); g.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); if (target) { state.selection = target; controls(); draw(); } show(); } });
  parent.append(g); return g;
}
function hiddenPhase(obstacle) {
  if (!obstacle.hidden || !state.replay) return obstacle.hidden ? 'Author' : 'Solid';
  const h = state.replay.hiddenStates.find(h => h.id === obstacle.id);
  if (!h || h.revealedAt === null || state.time < h.revealedAt) return 'Hidden';
  if (h.solidAt === null || state.time < h.solidAt) return 'Warning';
  return 'Solid';
}
function drawGeometry(parent, overview = false) {
  const a = state.preview || state.analysis;
  const h = a.corridorHeight;
  parent.append(el('rect', { x: -30, y: 0, width: a.length + 70, height: h, fill: '#f3f6eb' }));
  for (const y of [0, h]) line(parent, -30, y, a.length + 40, y, { stroke: '#bac9ae', 'stroke-width': overview ? 4 : 2 });
  if (!overview) {
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
    const phase = hiddenPhase(o), isGate = o.bands.length > 1;
    const desc = `${o.id} · ${isGate ? 'ворота, обе части' : 'камень'}${o.hidden ? ` · скрытый (${phase})` : ''} · X ${number(o.x, 2)} · ширина ${number(o.width, 2)}${isGate ? ` · проём Y ${number(o.gapTop, 2)}…${number(o.gapBottom, 2)} · смещения ${number(o.topOffsetX, 2)} / ${number(o.bottomOffsetX, 2)}` : ` · высота ${number(o.height, 2)}`}`;
    const g = overview ? parent : objectGroup(parent, desc, { collection: 'obstacles', id: o.id });
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
    const appearance = rewardAppearance(r.kind), name = appearance.label;
    const g = overview ? parent : objectGroup(parent, `${r.id} · ${name} · X ${number(r.x, 2)}, Y ${number(r.y, 2)}${r.shape === 'landing' ? ` · окно X ${number(r.left, 2)}…${number(r.right, 2)} · плоскость Y ${number(r.planeY, 2)}` : r.shape === 'circle' ? ` · радиус ${number(r.radius, 2)}` : ' · 4 физических штриха'}`, { collection: 'rewards', id: r.id });
    if (r.shape === 'finish') {
      if (!overview) g.append(el('rect', { x: r.x - 12, y: r.y - 10, width: 24, height: 20, fill: 'transparent', 'pointer-events': 'all' }));
      for (const s of r.segments) line(g, s.x1, s.y1, s.x2, s.y2, { stroke: '#9982b2', 'stroke-width': r.strokeWidth, 'stroke-linecap': 'round' });
    } else if (r.shape === 'landing') {
      const linked = a.obstacles.find(o => o.id === r.obstacleId);
      const muted = linked?.hidden && hiddenPhase(linked) !== 'Solid';
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
  const replay = state.replay;
  if (!replay) return;
  parent.append(el('path', { d: replay.samples.map((s, i) => `${i ? 'L' : 'M'}${s.x.toFixed(3)},${s.y.toFixed(3)}`).join(' '), class: 'trajectory', 'stroke-width': overview ? 5 : 2 }));
  if (!overview) {
    for (const input of replay.inputs) {
      if (input.x === null || input.y === null) continue;
      const g = objectGroup(parent, `Нажатие №${input.index + 1} · ${input.status === 'accepted' ? 'принято' : 'отклонено'} · ${number(input.time, 3)} с · X ${number(input.x, 2)}${input.reason ? ` · ${input.reason}` : ''}`);
      if (input.status === 'accepted') g.append(el('circle', { cx: input.x, cy: input.y, r: 3.5, class: 'press-marker' }));
      else { line(g, input.x - 4, input.y - 4, input.x + 4, input.y + 4, { stroke: '#b6757d', 'stroke-width': 1.8 }); line(g, input.x + 4, input.y - 4, input.x - 4, input.y + 4, { stroke: '#b6757d', 'stroke-width': 1.8 }); }
    }
    for (const event of replay.events) {
      if (event.type !== 'collision') continue;
      const g = objectGroup(parent, `Удар · ${event.id} · ${number(event.time, 3)} с`);
      g.append(el('circle', { cx: event.x, cy: event.y, r: 6, fill: '#c37f8430', stroke: '#bc777d', 'stroke-width': 1.3 }));
    }
  }
  const s = sampleAt(replay.samples, state.time);
  if (s) {
    parent.append(el('circle', { cx: s.x, cy: s.y, r: state.analysis.playerRadius + 5, class: 'runner-aura' }));
    parent.append(el('circle', { cx: s.x, cy: s.y, r: state.analysis.playerRadius, class: 'runner' }));
  }
}
function draw() {
  if (!state.analysis) return;
  const v = dimensions(); state.left = v.left;
  const diagram = $('diagram'); diagram.replaceChildren();
  diagram.setAttribute('viewBox', `${v.left} ${v.top} ${v.width} ${v.height}`);
  diagram.setAttribute('preserveAspectRatio', 'xMidYMid meet');
  drawGeometry(diagram); drawReplay(diagram); drawSelection(diagram);
  const overview = $('overview'); overview.replaceChildren();
  overview.setAttribute('viewBox', `-20 -8 ${state.analysis.length + 50} ${state.analysis.corridorHeight + 16}`);
  overview.setAttribute('preserveAspectRatio', 'none');
  drawGeometry(overview, true); drawReplay(overview, true);
  overview.append(el('rect', { x: Math.max(0, v.left), y: 0, width: Math.min(v.width, state.analysis.length), height: state.analysis.corridorHeight, class: 'view-window' }));
  $('pan').value = String(clamp((v.left + 20) / Math.max(1, state.analysis.length + 50 - v.width) * 1000, 0, 1000));
  $('view-start').textContent = number(Math.max(0, v.left));
  $('view-end').textContent = `${number(Math.min(state.analysis.length, v.left + v.width))} ед.`;
}
function zoom(factor, anchor = .5) {
  if (drag) return;
  if (!state.analysis) return;
  const old = dimensions(); state.zoom = clamp(state.zoom * factor, 1, 3);
  const next = dimensions(); state.left = old.left + anchor * (old.width - next.width); draw();
}
function seek(time, follow = true) {
  if (!state.replay) return;
  state.time = clamp(time, 0, state.replay.summary.elapsed);
  $('timeline').value = String(state.time); $('clock').textContent = `${number(state.time, 2)} с`;
  if (follow && $('follow').checked) {
    const sample = sampleAt(state.replay.samples, state.time), v = dimensions();
    if (sample && (sample.x < v.left + v.width * .15 || sample.x > v.left + v.width * .8)) state.left = sample.x - v.width * .35;
  }
  draw();
}
function stopPlayback() { state.playing = false; $('play').textContent = '▶'; $('play').setAttribute('aria-label', 'Воспроизвести'); }
function play() {
  if (!state.replay) return;
  if (state.playing) { stopPlayback(); return; }
  if (state.time >= state.replay.summary.elapsed) seek(0);
  state.playing = true; $('play').textContent = 'Ⅱ'; $('play').setAttribute('aria-label', 'Пауза');
  let previous = performance.now();
  const tick = now => {
    if (!state.playing || !state.replay) return;
    seek(state.time + Math.min(.15, (now - previous) / 1000)); previous = now;
    if (state.time >= state.replay.summary.elapsed) stopPlayback(); else requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
}
function showResult(result) {
  state.replay = result; state.time = 0;
  const s = result.summary;
  $('playback').hidden = false;
  $('replay-state').className = 'replay-state';
  const finish = s.status === 'Finished' ? (s.cleanFinish ? 'Финиш без ударов' : 'Финиш достигнут') : 'Лимит 120 с: финиш не достигнут';
  message('replay-state', `${finish}. SOLO · ${$('profile').selectedOptions[0].textContent} · ${$('axis').value === 'distance' ? 'нажатия по X' : 'нажатия по времени'}. Между игроками взаимодействий нет.`);
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
    button.addEventListener('click', () => { stopPlayback(); seek(event.time); if (event.x !== null) { state.left = event.x - dimensions().width * .4; draw(); } });
    $('events').append(button);
  }
  if (!events.length) $('events').textContent = 'Нажатий и событий нет.';
  seek(0);
}
async function replay() {
  if (!state.valid) return;
  invalidateReplay(); message('replay-error', '', false);
  let inputs;
  try { inputs = parseInputs($('inputs').value); } catch (error) { message('replay-error', error.message); return; }
  const revision = state.replayRevision.value;
  $('replay').disabled = true; $('replay').textContent = 'Симуляция…';
  message('replay-state', 'Unity Core рассчитывает SOLO-прохождение…');
  try {
    const result = await api('/api/replay', { json: $('json').value, profile: $('profile').value, warningDuration: Number($('warning').value), axis: $('axis').value, inputs });
    if (!state.replayRevision.current(revision)) return;
    showResult(result);
  } catch (error) {
    if (!state.replayRevision.current(revision)) return;
    message('replay-error', error.message); message('replay-state', 'Прохождение не построено. Исправьте параметры и повторите.');
  } finally {
    if (state.replayRevision.current(revision)) { $('replay').textContent = '▶ Построить траекторию'; controls(); }
  }
}

$('json').addEventListener('input', () => edited());
$('json').addEventListener('keydown', event => { if (event.key === 'Tab') { event.preventDefault(); const text = $('json'), start = text.selectionStart, end = text.selectionEnd; text.setRangeText('  ', start, end, 'end'); edited(); } });
$('format').addEventListener('click', () => { try { $('json').value = JSON.stringify(JSON.parse($('json').value), null, 2) + '\n'; edited(true); } catch (error) { notice(`Форматирование: ${error.message}`, true); } });
$('open').addEventListener('click', () => $('file').click());
$('file').addEventListener('change', async () => {
  const file = $('file').files[0]; if (!file) return;
  const revision = ++state.loadRevision;
  try {
    if (file.size > 2 * 1024 * 1024) throw new Error('Файл больше 2 МиБ.');
    const text = await file.text(); if (revision !== state.loadRevision) return;
    $('catalog').value = ''; loadText(text, file.name);
  } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
  finally { $('file').value = ''; }
});
$('catalog').addEventListener('change', () => loadBuiltin($('catalog').value));
$('download').addEventListener('click', () => {
  if (!state.valid) return;
  const a = state.analysis;
  download(a.staleReferences ? a.cleanedJson : $('json').value, `${a.id}.track.json`);
  notice(a.staleReferences ? 'Скачан проверенный JSON без устаревших botReferences. Исходный текст в редакторе сохранён. Для импорта копии штатной карты задайте новый track.id.' : 'JSON скачан. Для импорта копии встроенной карты нужен новый track.id.');
});
$('download-source').addEventListener('click', () => download($('json').value, state.sourceName));
for (const id of ['profile', 'warning', 'axis']) $(id).addEventListener('change', () => {
  if (id !== 'axis') invalidatePrepared();
  scheduleDraft(); invalidateReplay(id === 'axis' ? 'Единицы нажатий изменены. Числа в списке сохранены без пересчёта — проверьте их перед запуском.' : 'Настройки изменены. Постройте траекторию заново.'); controls();
});
$('inputs').addEventListener('input', () => { scheduleDraft(); invalidateReplay('Список нажатий изменён. Постройте траекторию заново.'); message('replay-error', '', false); controls(); });
$('reference').addEventListener('click', () => {
  const r = state.analysis?.references?.find(r => r.profileId === $('profile').value); if (!r || !state.valid) return;
  $('inputs').value = JSON.stringify(r.inputs); $('axis').value = 'distance';
  scheduleDraft();
  invalidateReplay('Загружены нажатия эталона из файла для выбранного профиля. Это не новый найденный маршрут.'); controls();
});
$('replay').addEventListener('click', replay); $('play').addEventListener('click', play);
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
$('overview').addEventListener('click', event => { if (!state.analysis) return; const box = $('overview').getBoundingClientRect(); state.left = (event.clientX - box.left) / box.width * (state.analysis.length + 50) - 20 - dimensions().width / 2; draw(); });
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
  $('add-object').textContent = state.addKind ? 'Укажите место на схеме…' : '＋ Разместить на схеме';
  const chosenKind = state.addKind || $('add-type').value;
  const linked = ['landing', 'positive-before', 'positive-after'].includes(chosenKind), anchorName = chosenKind === 'landing' ? 'камень' : 'препятствие';
  $('add-help').textContent = state.addKind ? linked ? `Нажмите ${anchorName} на подробной схеме; либо выберите его заранее и нажмите место награды. Esc — отмена добавления.` : 'Нажмите место на подробной схеме. Будет добавлен один объект, затем вернётся выбор. Esc — отмена.' : linked ? `Для этого типа нужно связанное ${anchorName === 'камень' ? 'основание — камень' : 'препятствие'}: выберите его заранее или нажмите на него при размещении.` : 'Выберите тип и нажмите «Разместить на схеме», затем укажите место на подробной схеме.';
  $('diagram').classList.toggle('adding-object', !!state.addKind);
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
function remember(before, after, description) {
  state.history.push({ before, after, description }); state.future = [];
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
  if (state.moving || drag) return;
  const from = redo ? state.future : state.history, to = redo ? state.history : state.future, step = from.at(-1);
  if (!step || $('json').value !== (redo ? step.before : step.after)) return;
  from.pop(); to.push(step); state.addKind = null;
  $('json').value = redo ? step.after : step.before;
  edited(true, { preserveHistory: true });
  notice(`${redo ? 'Повторено' : 'Отменено'}: ${step.description}. Восстановлен точный текст JSON.`);
}
function removeSelection() {
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
}
$('diagram').addEventListener('pointerdown', event => {
  if (!state.analysis || event.button !== 0 || drag) return;
  const handleElement = event.target.closest('[data-handle]'), group = event.target.closest('.object');
  const collection = handleElement?.dataset.collection || group?.dataset.collection;
  const id = handleElement?.dataset.id || group?.dataset.id;
  const mode = event.altKey || spacePan ? 'pan' : state.addKind ? 'add' : handleElement ? 'resize' : collection ? 'object' : 'pan';
  if (mode !== 'pan' && !state.valid) { notice('Для редактирования объектов сначала исправьте JSON и дождитесь проверки текущего текста.', true); return; }
  try {
    const view = dimensions(), box = $('diagram').getBoundingClientRect(), target = { collection, id };
    if (mode === 'object' || mode === 'resize') state.selection = target;
    else if (mode === 'pan' && !collection && !event.altKey && !spacePan) state.selection = null;
    const anchorAllowed = o => state.addKind !== 'landing' || o.kind !== 'gate';
    const clickedAnchor = collection === 'obstacles' && state.analysis.obstacles.find(o => o.id === id && anchorAllowed(o));
    const selectedAnchor = state.selection?.collection === 'obstacles' && state.analysis.obstacles.find(o => o.id === state.selection.id && anchorAllowed(o));
    drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, left: state.left, scale: view.scale,
      mode, active: false, target, description: group?.getAttribute('aria-label'),
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
      invalidateReplay('Объект редактируется. Прежняя траектория сброшена; после проверки постройте её заново.'); controls();
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
  if (event.key === 'Escape' && drag) { event.preventDefault(); if (drag.mode === 'add') state.addKind = null; finishDrag(false); return; }
  if (event.key === 'Escape' && state.addKind) { event.preventDefault(); state.addKind = null; controls(); draw(); notice('Добавление отменено.'); return; }
  if (event.key === 'Delete' && !event.target.closest('textarea,input,select,[contenteditable="true"]') && state.selection) { event.preventDefault(); removeSelection(); return; }
  if (event.code === 'Space' && !event.target.closest('textarea,input,select,button')) { spacePan = true; event.preventDefault(); $('diagram').classList.add('pan-ready'); }
});
window.addEventListener('keyup', event => { if (event.code === 'Space') { spacePan = false; $('diagram').classList.remove('pan-ready'); } });
window.addEventListener('blur', () => { spacePan = false; $('diagram').classList.remove('pan-ready'); finishDrag(false); });
$('undo-move').addEventListener('click', () => historyStep());
$('redo').addEventListener('click', () => historyStep(true));
$('delete-object').addEventListener('click', removeSelection);
$('add-type').addEventListener('change', () => { state.addKind = null; controls(); draw(); });
$('add-object').addEventListener('click', () => { if (!state.valid || state.moving) return; state.addKind = $('add-type').value; controls(); draw(); $('diagram').focus({ preventScroll: true }); });
$('cancel-add').addEventListener('click', () => { state.addKind = null; controls(); draw(); });
$('diagram').addEventListener('keydown', event => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') { event.preventDefault(); historyStep(event.shiftKey); }
});
$('diagram').addEventListener('keydown', event => { if (!state.analysis) return; if (event.key === '+' || event.key === '=') zoom(1.25); else if (event.key === '-') zoom(.8); else if (event.key === 'ArrowRight') { state.left += dimensions().width / 5; draw(); } else if (event.key === 'ArrowLeft') { state.left -= dimensions().width / 5; draw(); } else return; event.preventDefault(); });
new ResizeObserver(() => draw()).observe($('diagram'));
window.addEventListener('beforeunload', event => { if ($('json').value !== state.sourceText) { event.preventDefault(); event.returnValue = ''; } });
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
  const draft = pendingDraft;
  draftBlocked = false; pendingDraft = null; $('draft-offer').hidden = true; $('catalog').value = '';
  state.sourceName = draft.filename; state.sourceText = '';
  $('filename').textContent = draft.filename; $('json').value = draft.json; $('inputs').value = draft.inputs;
  $('profile').value = draft.profile; $('axis').value = draft.axis; $('warning').value = draft.warning;
  notice('Черновик восстановлен из этого браузера. Исходный текст сохранён точно; проверяем JSON заново. История визуальных правок очищена.'); edited(true);
});
$('draft-clear').addEventListener('click', () => {
  const result = clearDraft(draftStorage);
  if (result.kind === 'error') { draftStatus('Не удалось удалить сохранение: хранилище браузера недоступно. Редактор продолжает работать; скачайте исходный текст.', true); return; }
  draftBlocked = false; pendingDraft = null; $('draft-offer').hidden = true;
  draftStatus('Прежний черновик удалён из этого браузера.');
  if ($('json').value) scheduleDraft();
});
async function start() {
  offerDraft();
  const allowInitialCatalog = !draftBlocked;
  try {
    const result = await api('/api/catalog'); state.catalog = result.entries;
    $('catalog').replaceChildren(new Option('Выберите трассу…', ''), ...result.entries.map(e => new Option(e.name, e.id)));
    controls();
    if (allowInitialCatalog && !draftBlocked && !draftTouched && result.entries.length && !$('json').value) { const id = result.entries.find(e => e.id === 'mixed-tricks')?.id || result.entries[0].id; $('catalog').value = id; await loadBuiltin(id); }
  } catch (error) { $('catalog').replaceChildren(new Option('Каталог недоступен', '')); notice(error.message, true); }
}
start();
