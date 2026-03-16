function unityShowBanner(msg, type) {
        
    }

    var loadingOverlay = document.querySelector("#soulframe-loading-overlay");
    var loadingProgressText = document.querySelector("#soulframe-loading-progress");
    var loadingBarFill = document.querySelector("#soulframe-loading-bar-fill");

    function updateLoadingProgress(progress) {
        var percent = Math.max(0, Math.min(100, Math.round(progress * 100)));

        if (loadingProgressText) {
            loadingProgressText.textContent = "Inizializzazione " + percent + "%";
        }

        if (loadingBarFill) {
            loadingBarFill.style.width = percent + "%";
        }

        if (!loadingProgressText && !loadingBarFill) {
            return;
        }
    }

    function hideLoadingOverlay() {
        if (!loadingOverlay) {
            return;
        }

        loadingOverlay.classList.add("is-hidden");
    }

    function applyWebGLCursor() {
        if (!canvas) {
            return;
        }

        var cursorValue = 'url("TemplateData/SOULFRAME_Cursor_32.png") 0 0, auto';
        canvas.style.cursor = cursorValue;

        var unityContainer = document.querySelector("#unity-container");
        if (unityContainer) {
            unityContainer.style.cursor = cursorValue;
        }
    }

    var soulframeRenderScale = 0.8;
    var soulframeMaxDevicePixelRatio = 2.0;

    function getCanvasCssWidth() {
        if (!canvas) {
            return 0;
        }

        return Math.max(1, Math.round(canvas.clientWidth || window.innerWidth || 1));
    }

    function getCanvasCssHeight() {
        if (!canvas) {
            return 0;
        }

        return Math.max(1, Math.round(canvas.clientHeight || window.innerHeight || 1));
    }

    function applyCanvasRenderResolution() {
        if (!canvas) {
            return;
        }

        var cssWidth = getCanvasCssWidth();
        var cssHeight = getCanvasCssHeight();
        var devicePixelRatio = Math.max(1, Math.min(window.devicePixelRatio || 1, soulframeMaxDevicePixelRatio));
        var internalWidth = Math.max(1, Math.round(cssWidth * devicePixelRatio * soulframeRenderScale));
        var internalHeight = Math.max(1, Math.round(cssHeight * devicePixelRatio * soulframeRenderScale));

        canvas.style.width = "100%";
        canvas.style.height = "100%";

        if (gameInstance && gameInstance.Module && typeof gameInstance.Module.setCanvasSize === "function") {
            gameInstance.Module.setCanvasSize(internalWidth, internalHeight);
            return;
        }

        if (canvas.width !== internalWidth) {
            canvas.width = internalWidth;
        }

        if (canvas.height !== internalHeight) {
            canvas.height = internalHeight;
        }
    }

    function requestCanvasRenderResolutionUpdate() {
        window.requestAnimationFrame(applyCanvasRenderResolution);
    }
    
    var buildUrl = "Build";
    var loaderUrl = buildUrl + "/{{{ LOADER_FILENAME }}}";
    var config = {
        dataUrl: buildUrl + "/{{{ DATA_FILENAME }}}",
        frameworkUrl: buildUrl + "/{{{ FRAMEWORK_FILENAME }}}",
        codeUrl: buildUrl + "/{{{ CODE_FILENAME }}}",
        #if MEMORY_FILENAME
        memoryUrl: buildUrl + "/{{{ MEMORY_FILENAME }}}",
        #endif
        #if SYMBOLS_FILENAME
        symbolsUrl: buildUrl + "/{{{ SYMBOLS_FILENAME }}}",
        #endif
        streamingAssetsUrl: "StreamingAssets",
        companyName: "{{{ COMPANY_NAME }}}",
        productName: "{{{ PRODUCT_NAME }}}",
        productVersion: "{{{ PRODUCT_VERSION }}}",
        showBanner: unityShowBanner,
        matchWebGLToCanvasSize: false,
    };

    // Rimuoviamo dimensioni fisse del canvas e lasciamo gestire tutto al CSS
    canvas.style.width = "";
    canvas.style.height = "";
    applyWebGLCursor();
    applyCanvasRenderResolution();
    updateLoadingProgress(0);

    createUnityInstance(canvas, config, (progress) => {
        updateLoadingProgress(progress);
    })
        .then((unityInstance) => {
            gameInstance = unityInstance;
            updateLoadingProgress(1);
            window.setTimeout(hideLoadingOverlay, 150);

            // Alcuni browser possono resettare lo style del canvas dopo focus/fullscreen.
            applyWebGLCursor();
            requestCanvasRenderResolutionUpdate();
            canvas.addEventListener("mouseenter", applyWebGLCursor);
            canvas.addEventListener("focus", applyWebGLCursor);
            document.addEventListener("fullscreenchange", applyWebGLCursor);
            window.addEventListener("resize", requestCanvasRenderResolutionUpdate);
            document.addEventListener("fullscreenchange", requestCanvasRenderResolutionUpdate);
        })
        .catch((message) => {
            alert(message);
        });
