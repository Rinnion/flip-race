mergeInto(LibraryManager.library, {
  FlipPixelRatio: function() {
    var canvas = Module.canvas;
    // Unity Screen dimensions use render pixels; layout is authored in CSS
    // pixels. The actual buffer ratio also covers capped DPR and browser zoom.
    if (!canvas || canvas.clientWidth <= 0 || canvas.width <= 0) return 1;
    return canvas.width / canvas.clientWidth;
  },
  // Kept independently of the 20Hz scalar snapshot until the next attempt.
  FlipBotPerformance: function(pointer) {
    var canvas = Module.canvas;
    if (!canvas) return;
    var json = UTF8ToString(pointer);
    if (!json) {
      delete canvas.dataset.flipBotPerformance;
      delete canvas.dataset.flipBotPerformanceBrowserExportMilliseconds;
      delete canvas.dataset.flipBotPerformanceBrowserExportOver50;
      canvas.flipBotDiagnosticAudit = {
        capacity:10000,totalCalls:0,overflowCalls:0,samples:[],sealed:false,
        record:function(index,started,context) {
          if(this.sealed || !context || context.cohort < 0) return;
          this.totalCalls++;
          if(this.samples.length >= this.capacity) {this.overflowCalls++;return;}
          var sample={frame:context.frame,reportOrdinal:context.ordinal,cohort:context.cohort,seed:context.seed,callback:index,milliseconds:0};
          this.samples.push(sample);
          sample.milliseconds=performance.now()-started;
        }
      };
      return;
    }
    var audit = canvas.flipBotDiagnosticAudit;
    // MutationObservers queued by FlipReport finish before this final join.
    queueMicrotask(function() {
      var started=performance.now(),raw=JSON.parse(json);
      var callbacks=audit ? audit.samples.slice() : [],byFrame=new Map();
      callbacks.forEach(function(c) {
        if(c.seed !== raw.attemptSeed) return;
        byFrame.set(c.frame,(byFrame.get(c.frame)||0)+c.milliseconds);
      });
      var bins=new Array(1001).fill(0),count=0,sum=0,max=0,over50=0;
      raw.frames.forEach(function(f) {
        if(f.cohort !== 'Active') return;
        var cost=f.advanceMilliseconds+f.diagnosticsMilliseconds+(byFrame.get(f.frame)||0);
        count++;sum+=cost;max=Math.max(max,cost);if(cost>50)over50++;
        bins[Math.min(1000,Math.ceil(Math.min(50,cost)/.05))]++;
      });
      var rank=Math.ceil(count*.95),seen=0,p95=0;
      if(count)for(var i=0;i<bins.length;i++){seen+=bins[i];if(seen>=rank){p95=i===1000?max:i*.05;break;}}
      raw.browserCallbacks={capacity:audit?audit.capacity:10000,totalCalls:audit?audit.totalCalls:0,overflowCalls:audit?audit.overflowCalls:0,samples:callbacks};
      raw.activeAdvanceDiagnosticsAndCallbacks={count:count,sumMilliseconds:sum,maximumMilliseconds:max,over50:over50,p95UpperBoundMilliseconds:p95,histogramBinMilliseconds:.05};
      raw.measurementComplete=raw.activeCaptureFaultFrames===0&&raw.overflowFrames===0&&raw.totalFrames===raw.frames.length&&!!audit&&audit.overflowCalls===0;
      raw.scope='Conservative CPU upper bound: entire Advance (including ordinary shared simulation), synchronous diagnostic build/JSON/publish, and all three existing HTML observer bodies including parse/layout. All active frames including zero-plan frames; matched frame and attempt seed only. No baseline subtraction. Terminal export work excluded from active cohort and reported separately.';
      canvas.dataset.flipBotPerformance=JSON.stringify(raw);
      var milliseconds=performance.now()-started;
      canvas.dataset.flipBotPerformanceBrowserExportMilliseconds=String(milliseconds);
      canvas.dataset.flipBotPerformanceBrowserExportOver50=String(milliseconds>50);
      if(audit)audit.sealed=true;
    });
  },
  FlipReport: function(pointer,frame,cohort,seed) {
    var json = UTF8ToString(pointer);
    if (Module.canvas) {
      var canvas=Module.canvas;
      canvas.flipDiagnosticContext={frame:frame,cohort:cohort,seed:seed>>>0,ordinal:(canvas.flipDiagnosticOrdinal||0)+1};
      canvas.flipDiagnosticOrdinal=canvas.flipDiagnosticContext.ordinal;
      canvas.dataset.flipState = json;
      // Publication time lets local input QA account for the diagnostic cadence.
      canvas.dataset.flipReportedAt = String(performance.timeOrigin + performance.now());
    }
  }
});