import { MAX_REPLAY_INPUTS } from './model.mjs';

const batchSize = 4;
const batchMs = batchSize * 1000 / 120;
const maxBatches = 7;
const rewardStates = { collected: 'pending', reward: 'succeeded', failedReward: 'failed', negative: 'negative', coin: 'succeeded', finishBoost: 'succeeded' };

// Only schedules Core ticks. Positions, camera, and outcomes arrive from the server.
export class LiveStepClock {
  seq = 0;
  pending = new Map();
  presses = [];
  epoch = null;
  sentTicks = 0;

  resume(now) { this.epoch = now; this.sentTicks = 0; }
  pause() { this.epoch = null; this.presses.length = 0; }

  press(now) {
    if (this.epoch === null || this.presses.length >= maxBatches * batchSize) return false;
    const dueTick = Math.ceil((now - this.epoch) * 120 / 1000);
    const afterPrevious = (this.presses.at(-1)?.tick ?? -1) + 1;
    this.presses.push({ tick: Math.max(dueTick, this.sentTicks, afterPrevious), now });
    return true;
  }

  advance(now) {
    if (this.epoch === null) return null;
    const endTick = this.sentTicks + batchSize;
    const next = this.epoch + endTick * 1000 / 120;
    if (now < next) return null;
    if (now - next >= maxBatches * batchMs || this.pending.size >= maxBatches) return { stalled: true };
    const presses = [];
    while (this.presses.length && this.presses[0].tick < endTick) presses.push(this.presses.shift());
    const inputs = presses.map(({ tick }) => tick - this.sentTicks);
    this.sentTicks = endTick;
    const seq = ++this.seq;
    this.pending.set(seq, presses.map(press => press.now));
    return { type: 'step', seq, steps: batchSize, inputs };
  }

  acknowledge(seq) {
    if (this.pending.keys().next().value !== seq) throw new Error('Нарушен порядок кадров живого заезда.');
    const presses = this.pending.get(seq);
    this.pending.delete(seq);
    return presses;
  }
}

export class LiveConnection {
  clock = new LiveStepClock();
  hidden = new Map();
  rewards = new Map();
  collected = new Set();
  acceptedInputs = 0;
  ignoredInputs = 0;
  unreachedInputs = 0;
  recordedInputs = [];
  lastInput = null;
  lastEvent = null;
  state = null;
  paused = false;
  readyAtStart = false;
  reason = '';
  error = '';
  terminal = false;
  closed = false;

  constructor(change, { autoStart = false } = {}) {
    this.change = change;
    this.autoStart = autoStart;
  }

  get recordingSettled() {
    return this.terminal || !!this.error || (this.paused && this.clock.pending.size === 0);
  }

  recordingTimes() {
    if (!this.recordingSettled || !this.recordedInputs.length || this.recordedInputs.length > MAX_REPLAY_INPUTS) return null;
    return this.recordedInputs.map(input => input.time);
  }

  open(url, settings) {
    try {
      this.socket = new WebSocket(url);
      this.socket.addEventListener('open', () => this.send({ type: 'start', ...settings }));
      this.socket.addEventListener('message', event => this.receive(event.data));
      this.socket.addEventListener('error', () => this.fail('Не удалось подключиться к живому заезду.'));
      this.socket.addEventListener('close', () => {
        if (!this.closed && !this.terminal) this.fail('Связь с живым заездом прервалась. Начните заново.');
      });
      this.startTimer = setTimeout(() => this.fail('Сервер долго не отвечает. Начните заезд заново.'), 10000);
    } catch (error) { this.fail(error.message); }
  }

  send(message) {
    if (this.closed) return;
    try { this.socket.send(JSON.stringify(message)); }
    catch { this.fail('Не удалось отправить команду. Начните заезд заново.'); }
  }

  receive(text) {
    if (this.closed || this.terminal) return;
    try {
      const frame = JSON.parse(text);
      if (frame.type === 'error') { this.fail(frame.error); return; }
      if (frame.type !== 'state') throw new Error('Неизвестный ответ живого заезда.');
      const first = !this.state;
      const wasRacing = this.state?.phase === 'racing';
      if (first && frame.seq !== 0) throw new Error('Не получен стартовый кадр.');
      const presses = first ? [] : this.clock.acknowledge(frame.seq);
      clearTimeout(this.startTimer);
      this.state = frame;
      if (first && frame.requestedStartX > 0 && !this.autoStart) {
        this.paused = true;
        this.readyAtStart = true;
      }
      for (const item of frame.hiddenStates) this.hidden.set(item.id, item.phase);
      for (const event of frame.events) {
        if (event.type === 'collected') this.collected.add(event.id);
        const rewardState = rewardStates[event.type];
        if (rewardState) this.rewards.set(event.id, rewardState);
        if (event.type !== 'hidden') this.lastEvent = event;
      }
      if (first) for (const input of frame.prefixInputs || []) this.observeInput(input, true);
      frame.inputs.forEach((input, index) => this.observeInput(input, wasRacing && presses[index] !== undefined, presses[index]));
      this.terminal = !!frame.summary || frame.phase === 'finished' || frame.phase === 'timedOut';
      if (this.terminal) this.stopFrames();
      else if (first && !this.paused) this.startFrames();
      this.change();
    } catch (error) { this.fail(error.message); }
  }

  observeInput(input, record, pressedAt) {
    if (input.status === 'accepted') this.acceptedInputs++;
    else if (input.status === 'unreached') this.unreachedInputs++;
    else this.ignoredInputs++;
    if (record && (input.status === 'accepted' || input.status === 'ignored') && Number.isFinite(input.time) && input.time >= 0) {
      this.recordedInputs.push(input);
    }
    this.lastInput = { ...input, pressedAt };
  }

  startFrames() {
    this.clock.resume(performance.now());
    const tick = now => {
      if (this.paused || this.closed || this.terminal) return;
      // Normally one command every 33 ms. A short display stall can catch up
      // inside the same bounded window. Longer stalls require explicit resume.
      for (let batch = this.clock.advance(now); batch; batch = this.clock.advance(now)) {
        if (batch.stalled) { this.pause('Кадры задерживаются. Продолжите после ответа сервера.'); return; }
        this.send(batch);
        if (this.closed) return;
      }
      this.frameRequest = requestAnimationFrame(tick);
    };
    this.frameRequest = requestAnimationFrame(tick);
  }

  stopFrames() {
    cancelAnimationFrame(this.frameRequest);
    this.clock.pause();
  }

  press(now = performance.now()) {
    if (this.closed || this.paused || this.terminal || this.state?.phase !== 'racing') return;
    if (!this.clock.press(now)) this.pause('Очередь нажатий заполнена. Продолжите после ответа сервера.');
  }

  pause(reason = '') {
    if (this.closed || this.terminal || this.paused) return;
    this.paused = true;
    this.reason = reason;
    this.stopFrames();
    this.change();
  }

  resume() {
    if (this.closed || this.terminal || !this.paused || !this.state || this.clock.pending.size) return false;
    this.paused = false;
    this.readyAtStart = false;
    this.reason = '';
    this.startFrames();
    this.change();
    return true;
  }

  fail(error) {
    if (this.closed || this.terminal) return;
    this.error = error || 'Живой заезд завершён сервером.';
    this.close();
    this.change();
  }

  close() {
    this.closed = true;
    this.stopFrames();
    clearTimeout(this.startTimer);
    this.socket?.close();
  }
}
