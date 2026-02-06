mergeInto(LibraryManager.library, {
  WebGLFilePicker_IsSupported: function () {
    return (typeof document !== "undefined") ? 1 : 0;
  },

  WebGLFilePicker_PickFile: function (acceptPtr, goNamePtr, callbackPtr) {
    var accept = UTF8ToString(acceptPtr || 0);
    var goName = UTF8ToString(goNamePtr || 0);
    var callback = UTF8ToString(callbackPtr || 0);

    function sendMessage(msg) {
      try {
        if (typeof SendMessage !== "undefined") {
          SendMessage(goName, callback, msg);
          return;
        }
        if (typeof unityInstance !== "undefined" && unityInstance.SendMessage) {
          unityInstance.SendMessage(goName, callback, msg);
          return;
        }
        if (typeof window.gameInstance !== "undefined" && window.gameInstance.SendMessage) {
          window.gameInstance.SendMessage(goName, callback, msg);
          return;
        }
      } catch (e) {}
    }

    var input = document.createElement("input");
    input.type = "file";
    input.style.display = "none";

    if (accept) {
      var parts = accept.split(",");
      var normalized = [];
      for (var i = 0; i < parts.length; i++) {
        var ext = parts[i].trim();
        if (!ext) continue;
        if (ext[0] !== ".") ext = "." + ext;
        normalized.push(ext);
      }
      if (normalized.length > 0) {
        input.accept = normalized.join(",");
      }
    }

    input.onchange = function () {
      if (!input.files || input.files.length === 0) {
        sendMessage("CANCEL");
        cleanup();
        return;
      }

      var file = input.files[0];
      var reader = new FileReader();
      reader.onload = function () {
        var bytes = new Uint8Array(reader.result);
        var binary = "";
        for (var i = 0; i < bytes.length; i++) {
          binary += String.fromCharCode(bytes[i]);
        }
        var b64 = btoa(binary);
        var payload = JSON.stringify({ name: file.name || "file", data: b64 });
        sendMessage(payload);
        cleanup();
      };
      reader.onerror = function () {
        sendMessage("ERR:read");
        cleanup();
      };
      reader.readAsArrayBuffer(file);
    };

    function cleanup() {
      if (input && input.parentNode) {
        input.parentNode.removeChild(input);
      }
    }

    document.body.appendChild(input);
    input.click();
  }
});
