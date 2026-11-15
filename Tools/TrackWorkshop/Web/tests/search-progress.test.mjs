import test from 'node:test';
import assert from 'node:assert/strict';
import { readSearchResponse } from '../search-progress.mjs';

const result = { analysis: { version: 2, runs: [] }, sourceJson: '{"имя":"Трасса"}' };
const resultEvent = { type: 'result', result };
const progress = (percent, phase = 'search') => ({ type: 'progress', percent, phase });
const encoder = new TextEncoder();
const line = event => JSON.stringify(event) + '\n';

function response(text, chunkSize = text.length) {
  const bytes = encoder.encode(text);
  return new Response(new ReadableStream({
    start(controller) {
      for (let start = 0; start < bytes.length; start += chunkSize) controller.enqueue(bytes.slice(start, start + chunkSize));
      controller.close();
    },
  }));
}

test('NDJSON handles messages split across bytes, including Cyrillic and EOF after the final object', async () => {
  const updates = [];
  const text = line(progress(0)) + '\r\n' + line(progress(77, 'verify')) + line(progress(99, 'save')) + JSON.stringify(resultEvent);
  const received = await readSearchResponse(response(text, 1), { onProgress: update => updates.push(update) });
  assert.deepEqual(received, result);
  assert.deepEqual(updates, [{ percent: 0, phase: 'search' }, { percent: 77, phase: 'verify' }, { percent: 99, phase: 'save' }]);
});

test('progress and phases never move backward, and each request starts with fresh progress', async () => {
  const updates = [];
  const read = events => readSearchResponse(response(events.map(line).join('')), { onProgress: update => updates.push(update) });
  await read([progress(4), progress(4), progress(3), progress(71, 'verify'), progress(80), progress(99, 'save'), resultEvent]);
  await read([progress(0), resultEvent]);
  assert.deepEqual(updates.map(({ percent }) => percent), [4, 71, 99, 0]);
});

test('HTTP validation errors and streaming errors reach the caller', async () => {
  await assert.rejects(readSearchResponse(new Response('{"error":"Ошибка трассы"}', { status: 400 })), /Ошибка трассы/);
  await assert.rejects(readSearchResponse(new Response(null, { status: 503 })), /503/);
  const text = line(progress(0)) + line({ type: 'error', error: 'Места для результатов нет' });
  await assert.rejects(readSearchResponse(response(text, 7)), /Места для результатов нет/);
});

test('truncated and invalid streams cannot complete the search', async () => {
  await assert.rejects(readSearchResponse(response(line(progress(99, 'save')))), /до получения результата/);
  for (const text of [line(progress(100)), line(progress(10, 'invalid')), '{bad json}\n', line({ type: 'result', result: {} })])
    await assert.rejects(readSearchResponse(response(text)), /неожиданный ответ/);
});

test('abort cancels a pending reader and suppresses late progress and results', async () => {
  const abort = new AbortController(), updates = [];
  let cancelled = false;
  const body = new ReadableStream({
    start(controller) {
      controller.enqueue(encoder.encode(line(progress(8))));
    },
    cancel() { cancelled = true; },
  });
  const pending = readSearchResponse(new Response(body), {
    signal: abort.signal,
    onProgress(update) {
      updates.push(update.percent);
      setTimeout(() => abort.abort(), 0);
    },
  });
  await assert.rejects(pending, { name: 'AbortError' });
  assert.equal(cancelled, true);
  assert.deepEqual(updates, [8]);

  const sameChunk = new AbortController();
  await assert.rejects(readSearchResponse(response(line(progress(9)) + line(resultEvent)), {
    signal: sameChunk.signal, onProgress: () => sameChunk.abort(),
  }), { name: 'AbortError' });
});
