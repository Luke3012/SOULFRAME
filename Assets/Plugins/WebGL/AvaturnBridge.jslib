mergeInto(LibraryManager.library, {
  OpenAvaturnIframe: function (urlPtr, gameObjectNamePtr, callbackMethodPtr) {
    var url = UTF8ToString(urlPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);

    console.log("[JS] Avvio Avaturn SDK con URL: " + url);

    function sendToUnity(data) {
      try {
        var jsonData = JSON.stringify(data);
        console.log("[JS] Invio a Unity:", gameObjectName, callbackMethod, jsonData);

        if (typeof SendMessage !== "undefined") {
          SendMessage(gameObjectName, callbackMethod, jsonData);
          return;
        }

        if (typeof unityInstance !== "undefined" && unityInstance.SendMessage) {
          unityInstance.SendMessage(gameObjectName, callbackMethod, jsonData);
          return;
        }

        if (typeof window.gameInstance !== "undefined" && window.gameInstance.SendMessage) {
          window.gameInstance.SendMessage(gameObjectName, callbackMethod, jsonData);
          return;
        }

        console.warn("[JS] Impossibile trovare un'istanza Unity per SendMessage");
      } catch (e) {
        console.error("[JS] Errore nell'invio a Unity:", e);
      }
    }

    // ---- FIX 2: restituisci focus a Unity dopo aver chiuso overlay/iframe ----
    function refocusUnityCanvas() {
      try {
        // Module.canvas esiste spesso nei template Unity
        var canvas =
          (typeof Module !== "undefined" && Module.canvas) ? Module.canvas :
          document.querySelector("canvas");

        if (canvas) {
          // alcuni browser richiedono tabindex per il focus
          if (!canvas.hasAttribute("tabindex")) canvas.setAttribute("tabindex", "-1");
          canvas.focus();
        }
        window.focus();
      } catch (e) {
        console.warn("[JS] refocusUnityCanvas error:", e);
      }
    }

    // Rimuovi overlay e rifocalizza Unity
    function closeOverlayAndRefocus() {
      var el = document.getElementById("avaturn-overlay");
      if (el) document.body.removeChild(el);

      // micro-delay per lasciare al browser il tempo di rimuovere l'iframe
      setTimeout(refocusUnityCanvas, 0);
    }

    // Evita overlay duplicati (se l'utente apre due volte)
    var existing = document.getElementById("avaturn-overlay");
    if (existing) {
      try { document.body.removeChild(existing); } catch (e) {}
    }

    // Overlay
    var overlay = document.createElement("div");
    overlay.id = "avaturn-overlay";
    overlay.style.position = "fixed";
    overlay.style.top = "0";
    overlay.style.left = "0";
    overlay.style.width = "100%";
    overlay.style.height = "100%";
    overlay.style.zIndex = "9999";
    overlay.style.backgroundColor = "white";
    overlay.style.display = "flex";
    overlay.style.flexDirection = "column";

    var header = document.createElement("div");
    header.style.padding = "10px";
    header.style.backgroundColor = "#2c3e50";
    header.style.color = "white";
    header.style.display = "flex";
    header.style.justifyContent = "space-between";
    header.style.alignItems = "center";

    var title = document.createElement("span");
    title.textContent = "Avaturn Avatar Creator";
    title.style.fontWeight = "bold";

    var closeBtn = document.createElement("button");
    closeBtn.textContent = "✕ Chiudi";
    closeBtn.style.background = "#e74c3c";
    closeBtn.style.color = "white";
    closeBtn.style.border = "none";
    closeBtn.style.padding = "8px 16px";
    closeBtn.style.cursor = "pointer";
    closeBtn.style.borderRadius = "4px";

    header.appendChild(title);
    header.appendChild(closeBtn);
    overlay.appendChild(header);

    var avaturnContainer = document.createElement("div");
    avaturnContainer.id = "avaturn-sdk-container";
    avaturnContainer.style.flex = "1";
    avaturnContainer.style.width = "100%";
    avaturnContainer.style.height = "100%";
    avaturnContainer.style.border = "none";

    overlay.appendChild(avaturnContainer);
    document.body.appendChild(overlay);

    function cleanup() {
      console.log("[JS] Avaturn chiuso");
      sendToUnity({ status: "closed", avatarId: "none" });
      closeOverlayAndRefocus();
    }

    closeBtn.onclick = cleanup;

    function loadAvaturnSDK() {
      console.log("[JS] Caricamento Avaturn SDK...");

      // Esponi la funzione globalmente (mantiene la closure gameObjectName/callbackMethod)
      window.sendAvatarToUnity = sendToUnity;

      // Forza export HttpURL (utile su WebGL)
      window.avaturnForceExportHttpUrl = true;

      // Rimuovi eventuale script precedente (se riapri più volte)
      var old = document.getElementById("avaturn-sdk-module");
      if (old) {
        try { old.remove(); } catch (e) {}
      }

      var script = document.createElement("script");
      script.id = "avaturn-sdk-module";
      script.type = "module";
      script.innerHTML = `
        import { AvaturnSDK } from "https://cdn.jsdelivr.net/npm/@avaturn/sdk/dist/index.js";

        async function initAvaturn() {
          try {
            window.avaturnForceExportHttpUrl = true;

            const container = document.getElementById("avaturn-sdk-container");
            const sdk = new AvaturnSDK();

            await sdk.init(container, {
              url: "${url}",
              iframeClassName: "avaturn-iframe"
            });

            const iframe = container.querySelector("iframe");
            if (iframe) {
              iframe.style.width = "100%";
              iframe.style.height = "100%";
              iframe.style.border = "none";
            }

            sdk.on("export", (data) => {
              console.log("[JS] Avatar esportato:", data);

              const payload = {
                url: data.url || "",
                urlType: data.urlType || "glb",
                bodyId: data.bodyId || "default",
                gender: data.gender || "unknown",
                avatarId: data.avatarId || Date.now().toString()
              };

              if (typeof window.sendAvatarToUnity === "function") {
                window.sendAvatarToUnity(payload);
              } else {
                console.error("[JS] window.sendAvatarToUnity non definita");
              }

              // chiudi overlay + refocus Unity (Fix 2)
              setTimeout(() => {
                const el = document.getElementById("avaturn-overlay");
                if (el) document.body.removeChild(el);

                setTimeout(() => {
                  try {
                    const canvas = (typeof Module !== "undefined" && Module.canvas)
                      ? Module.canvas
                      : document.querySelector("canvas");
                    if (canvas) {
                      if (!canvas.hasAttribute("tabindex")) canvas.setAttribute("tabindex","-1");
                      canvas.focus();
                    }
                    window.focus();
                  } catch (e) {}
                }, 0);
              }, 50);
            });

            sdk.on("error", (err) => {
              console.error("[JS] Errore Avaturn SDK:", err);
              if (typeof window.sendAvatarToUnity === "function") {
                window.sendAvatarToUnity({ status: "error", error: err?.message || "unknown" });
              }
            });

            console.log("[JS] Avaturn SDK inizializzato correttamente");
          } catch (error) {
            console.error("[JS] Errore inizializzazione Avaturn SDK:", error);

            if (typeof window.sendAvatarToUnity === "function") {
              window.sendAvatarToUnity({ status: "error", error: error?.message || "init failed" });
            }

            const el = document.getElementById("avaturn-overlay");
            if (el) document.body.removeChild(el);

            setTimeout(() => {
              try {
                const canvas = (typeof Module !== "undefined" && Module.canvas)
                  ? Module.canvas
                  : document.querySelector("canvas");
                if (canvas) {
                  if (!canvas.hasAttribute("tabindex")) canvas.setAttribute("tabindex","-1");
                  canvas.focus();
                }
                window.focus();
              } catch (e) {}
            }, 0);
          }
        }

        initAvaturn();
      `;
      document.head.appendChild(script);
    }

    setTimeout(loadAvaturnSDK, 50);
  }
});
