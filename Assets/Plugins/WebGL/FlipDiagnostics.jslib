mergeInto(LibraryManager.library, {
  FlipPixelRatio: function() {
    var canvas = Module.canvas;
    // Unity Screen dimensions use render pixels; layout is authored in CSS
    // pixels. The actual buffer ratio also covers capped DPR and browser zoom.
    if (!canvas || canvas.clientWidth <= 0 || canvas.width <= 0) return 1;
    return canvas.width / canvas.clientWidth;
  },
  FlipReport: function(pointer) {
    var json = UTF8ToString(pointer);
    if (Module.canvas) Module.canvas.dataset.flipState = json;
  }
});
