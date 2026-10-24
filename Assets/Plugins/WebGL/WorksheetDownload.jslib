mergeInto(LibraryManager.library, {
  FleeDownloadWorksheet: function (namePtr, typePtr, dataPtr) {
    var bytes = Uint8Array.from(atob(UTF8ToString(dataPtr)), function (c) { return c.charCodeAt(0); });
    var url = URL.createObjectURL(new Blob([bytes], { type: UTF8ToString(typePtr) }));
    var link = document.createElement('a');
    link.href = url;
    link.download = UTF8ToString(namePtr);
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
  },
  FleeSyncWorksheetFiles__deps: ['$FS'],
  FleeSyncWorksheetFiles: function () {
    FS.syncfs(false, function (error) { if (error) console.error('Could not save classroom source file:', error); });
  }
});
