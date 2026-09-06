mergeInto(LibraryManager.library, {
  FleePickWorksheetTextFile: function (receiverNamePointer) {
    var receiverName = UTF8ToString(receiverNamePointer);
    var input = document.createElement('input');
    input.type = 'file';
    input.accept = '.pdf,.png,.jpg,.jpeg,.webp,application/pdf,image/*';
    input.style.display = 'none';

    input.onchange = function () {
      var file = input.files && input.files.length > 0 ? input.files[0] : null;
      if (!file) {
        document.body.removeChild(input);
        return;
      }

      var reader = new FileReader();
      reader.onload = function () {
        try {
          var bytes = new Uint8Array(reader.result || new ArrayBuffer(0));
          var binary = '';
          var chunkSize = 0x8000;
          for (var offset = 0; offset < bytes.length; offset += chunkSize) {
            binary += String.fromCharCode.apply(null, bytes.subarray(offset, offset + chunkSize));
          }

          SendMessage(
            receiverName,
            'OnWorksheetFilePicked',
            encodeURIComponent(file.name)
              + '\n'
              + encodeURIComponent(file.type || 'application/octet-stream')
              + '\n'
              + btoa(binary));
        } catch (error) {
          SendMessage(receiverName, 'OnWorksheetFileFailed', 'The selected file could not be read.');
        }

        document.body.removeChild(input);
      };
      reader.onerror = function () {
        SendMessage(receiverName, 'OnWorksheetFileFailed', 'The selected file could not be read.');
        document.body.removeChild(input);
      };
      reader.readAsArrayBuffer(file);
    };

    document.body.appendChild(input);
    input.click();
  }
});
