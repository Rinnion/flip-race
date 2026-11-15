const phases = ['search', 'verify', 'save'];
const unexpectedResponse = 'Сервер вернул неожиданный ответ. Проверьте, что запущена мастерская.';

export async function readSearchResponse(response, { signal, onProgress = () => {} } = {}) {
  signal?.throwIfAborted();
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    signal?.throwIfAborted();
    throw new Error(body?.error || `Ошибка сервера: ${response.status}`);
  }
  if (!response.body) throw new Error(unexpectedResponse);

  const reader = response.body.getReader();
  const decoder = new TextDecoder('utf-8', { fatal: true });
  const cancel = () => { void reader.cancel().catch(() => {}); };
  let buffer = '', percent = -1, phase = -1;
  signal?.addEventListener('abort', cancel, { once: true });

  function consume(line) {
    if (!line.trim()) return;
    let event;
    try { event = JSON.parse(line); } catch { throw new Error(unexpectedResponse); }
    if (event?.type === 'error' && typeof event.error === 'string' && event.error) throw new Error(event.error);
    if (event?.type === 'result' && Array.isArray(event.result?.analysis?.runs) && typeof event.result.sourceJson === 'string')
      return event.result;
    const nextPhase = phases.indexOf(event?.phase);
    if (event?.type !== 'progress' || !Number.isInteger(event.percent) || event.percent < 0 || event.percent > 99 || nextPhase < 0)
      throw new Error(unexpectedResponse);
    if (event.percent >= percent && nextPhase >= phase && (event.percent !== percent || nextPhase !== phase)) {
      percent = event.percent;
      phase = nextPhase;
      onProgress({ percent, phase: event.phase });
    }
  }

  try {
    while (true) {
      const { done, value } = await reader.read();
      signal?.throwIfAborted();
      buffer += decoder.decode(value, { stream: !done });
      let start = 0;
      for (let end = buffer.indexOf('\n'); end !== -1; end = buffer.indexOf('\n', start)) {
        const result = consume(buffer.slice(start, end));
        signal?.throwIfAborted();
        if (result) return result;
        start = end + 1;
      }
      buffer = buffer.slice(start);
      if (done) {
        const result = consume(buffer);
        signal?.throwIfAborted();
        if (result) return result;
        throw new Error('Ответ сервера оборвался до получения результата поиска.');
      }
    }
  } finally {
    signal?.removeEventListener('abort', cancel);
    cancel();
    reader.releaseLock();
  }
}
