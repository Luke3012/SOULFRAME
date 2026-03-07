function unityShowBanner(msg, type) {
        
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
        matchWebGLToCanvasSize: true,
    };

    // Rimuoviamo dimensioni fisse del canvas e lasciamo gestire tutto al CSS
    canvas.style.width = "";
    canvas.style.height = "";
    applyWebGLCursor();

    createUnityInstance(canvas, config, (progress) => {
    })
        .then((unityInstance) => {
            gameInstance = unityInstance;

            // Alcuni browser possono resettare lo style del canvas dopo focus/fullscreen.
            applyWebGLCursor();
            canvas.addEventListener("mouseenter", applyWebGLCursor);
            canvas.addEventListener("focus", applyWebGLCursor);
            document.addEventListener("fullscreenchange", applyWebGLCursor);
        })
        .catch((message) => {
            alert(message);
        });
