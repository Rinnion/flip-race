// Passive, diagnostic-only browser timestamps. Never sends game input or state.
(() => {
  const canvas = document.getElementById('unity-canvas');
  const samples = [], events = [];
  let lastFrame = performance.now(), phase = 'loading', lastPhase = phase;
  const frameStats = {};
  function publish() { canvas.dataset.selectionProbe = JSON.stringify({samples, events, frameStats}); }
  canvas.addEventListener('pointerdown', event => {
    let state;
    try { state = JSON.parse(canvas.dataset.flipState); } catch { return; }
    if (!state.characterSelectionVisible) return;
    const box = canvas.getBoundingClientRect(), x = event.clientX - box.left, y = event.clientY - box.top;
    for (let slot = 0; slot < 2; slot++) for (let hero = 0; hero < 4; hero++) {
      const [rx, ry, w, h] = state.characterRects[slot][hero];
      if (x >= rx && x < rx + w && y >= ry && y < ry + h) {
        samples.push({slot, hero, previous:state.players[slot].character, eventTime:event.timeStamp,
          received:performance.now(), stateAt:null, nextFrameAt:null, secondFrameAt:null, maxFrameGap:0});
        publish();
      }
    }
  }, true);
  new MutationObserver(() => {
    const state = JSON.parse(canvas.dataset.flipState);
    phase = state.phase;
    for (const s of samples) if (s.stateAt === null && state.players[s.slot].character === s.hero) {
      s.stateAt = performance.now();
      requestAnimationFrame(t => { s.nextFrameAt = t; requestAnimationFrame(t2 => { s.secondFrameAt = t2; publish(); }); });
    }
    publish();
  }).observe(canvas, {attributes:true, attributeFilter:['data-flip-state']});
  function frame(t) {
    if (phase === lastPhase) {
      const stats = frameStats[phase] || (frameStats[phase] = {count:0,sumMs:0,maxMs:0,over50:0,over100:0});
      const gap = t - lastFrame;
      stats.count++; stats.sumMs += gap; stats.maxMs = Math.max(stats.maxMs,gap);
      if(gap>50) stats.over50++; if(gap>100) stats.over100++;
    }
    lastPhase = phase;
    for (const s of samples) if (s.secondFrameAt === null) s.maxFrameGap = Math.max(s.maxFrameGap, t - lastFrame);
    lastFrame = t; requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
  if (PerformanceObserver.supportedEntryTypes.includes('event')) {
    new PerformanceObserver(list => {
      for (const e of list.getEntries()) if (e.interactionId) events.push({name:e.name, startTime:e.startTime,
        processingStart:e.processingStart, processingEnd:e.processingEnd, duration:e.duration, interactionId:e.interactionId});
      publish();
    }).observe({type:'event', buffered:true, durationThreshold:16});
  }
})();
