mergeInto(LibraryManager.library, {
  EnsureDynCallV: function () {
    try {
      if (typeof Module === "undefined") return 0;
      if (typeof Module.dynCall_v === "function") return 1;

      // 1) Emscripten helper (spesso presente nello scope di Unity)
      if (typeof getWasmTableEntry === "function") {
        Module.dynCall_v = function (ptr) { getWasmTableEntry(ptr)(); };
        console.log("[dynCall] Installed via getWasmTableEntry");
        return 1;
      }

      // 2) wasmTable diretto (alcune versioni)
      if (typeof wasmTable !== "undefined" && wasmTable && typeof wasmTable.get === "function") {
        Module.dynCall_v = function (ptr) { wasmTable.get(ptr)(); };
        console.log("[dynCall] Installed via wasmTable.get");
        return 1;
      }

      // 3) fallback Unity/emscripten (vecchi layout)
      if (Module.asm && Module.asm.__indirect_function_table && typeof Module.asm.__indirect_function_table.get === "function") {
        var t = Module.asm.__indirect_function_table;
        Module.dynCall_v = function (ptr) { t.get(ptr)(); };
        console.log("[dynCall] Installed via asm.__indirect_function_table");
        return 1;
      }

      console.warn("[dynCall] Could not install dynCall_v (no table found yet)");
      return 0;
    } catch (e) {
      console.warn("[dynCall] install error:", e);
      return 0;
    }
  },

  JS_FileSystem_Sync: function () {
    try {
      if (typeof FS === "undefined" || !FS.syncfs) {
        console.warn("[JS] FS.syncfs non disponibile");
        return;
      }
      FS.syncfs(false, function (err) {
        if (err) console.error("[JS] syncfs failed:", err);
        else console.log("[JS] syncfs OK");
      });
    } catch (e) {
      console.error("[JS] syncfs exception:", e);
    }
  },

  JS_FileSystem_SyncFromDiskAndNotify: function (goNamePtr, callbackPtr) {
    var goName = UTF8ToString(goNamePtr);
    var callback = UTF8ToString(callbackPtr);

    function sendMessage(msg) {
      try {
        // Prova multipli metodi per trovare l'istanza Unity corretta
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
        console.warn("[JS] Impossibile trovare un'istanza Unity per SendMessage");
      } catch (e) {
        console.error("[JS] Errore invio a Unity:", e);
      }
    }

    try {
      if (typeof FS === "undefined" || !FS.syncfs) {
        console.warn("[JS] FS.syncfs non disponibile (populate)");
        sendMessage("ERR:no_fs");
        return;
      }

      FS.syncfs(true, function (err) {
        if (err) {
          console.error("[JS] syncfs(populate) failed:", err);
          sendMessage("ERR:" + (err.message || err.toString()));
        } else {
          console.log("[JS] syncfs(populate) OK");
          sendMessage("OK");
        }
      });
    } catch (e) {
      console.error("[JS] syncfs(populate) exception:", e);
      sendMessage("ERR:" + (e.message || e.toString()));
    }
  }
});
