mergeInto(LibraryManager.library, {
  FlipCatalogState: function(phase, message) {
    if (window.flipTrackFiles) window.flipTrackFiles.catalog(UTF8ToString(phase), UTF8ToString(message));
  },
  FlipTrackImportResult: function(success, message) {
    if (window.flipTrackFiles) window.flipTrackFiles.result(Boolean(success), UTF8ToString(message));
  }
});
