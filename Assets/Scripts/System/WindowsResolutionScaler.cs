using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class WindowsResolutionScaler : MonoBehaviour
{
    [Header("3D Render Scale")]
    [SerializeField] private bool enable3dScaling = true;
    [SerializeField, Range(0.35f, 1f)] private float target3dPixelRatio = 0.5f;
    [SerializeField] private bool forceNativeOutputResolution = true;
    [SerializeField] private bool verboseLogs;

    private static WindowsResolutionScaler instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (!IsWindowsPlayerRuntime())
        {
            return;
        }

        if (FindFirstObjectByType<WindowsResolutionScaler>() != null)
        {
            return;
        }

        var go = new GameObject("WindowsResolutionScaler");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<WindowsResolutionScaler>();
    }

    private void Awake()
    {
        if (!IsWindowsPlayerRuntime())
        {
            return;
        }

        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private Camera boundCamera;
    private int originalCullingMask;
    private bool cullingMaskModified;

    private RenderTexture scaled3dTexture;
    private Canvas blitCanvas;
    private RawImage blitImage;

    private int lastScreenWidth;
    private int lastScreenHeight;
    private float lastTarget3dPixelRatio = -1f;

    private void OnEnable()
    {
        if (!IsWindowsPlayerRuntime())
        {
            return;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        RebindForCurrentScene();
    }

    private void OnDisable()
    {
        if (!IsWindowsPlayerRuntime())
        {
            return;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnbindCamera();
        DestroyBlitUi();
        ReleaseTexture();
    }

    private void Update()
    {
        if (!IsWindowsPlayerRuntime())
        {
            return;
        }

        if (!enable3dScaling)
        {
            DisableScalingOutput();
            return;
        }

        if (boundCamera == null)
        {
            RebindForCurrentScene();
            return;
        }

        bool resolutionChanged = Screen.width != lastScreenWidth || Screen.height != lastScreenHeight;
        bool ratioChanged = !Mathf.Approximately(lastTarget3dPixelRatio, target3dPixelRatio);
        if (resolutionChanged || ratioChanged)
        {
            ApplyOrRefresh();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RebindForCurrentScene();
    }

    private void RebindForCurrentScene()
    {
        if (!enable3dScaling)
        {
            DisableScalingOutput();
            return;
        }

        UnbindCamera();
        EnsureBlitUi();

        boundCamera = ResolveWorldCamera();
        if (boundCamera == null)
        {
            Debug.LogWarning("[WindowsResolutionScaler] Nessuna camera 3D trovata: scaling non applicato.");
            return;
        }

        originalCullingMask = boundCamera.cullingMask;
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
        {
            boundCamera.cullingMask = originalCullingMask & ~(1 << uiLayer);
            cullingMaskModified = true;
        }

        ApplyOrRefresh();
    }

    private void ApplyOrRefresh()
    {
        if (forceNativeOutputResolution)
        {
            EnsureNativeOutputResolution();
        }

        EnsureBlitUi();

        float linearScale = Mathf.Sqrt(Mathf.Clamp(target3dPixelRatio, 0.35f, 1f));
        int width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * linearScale));
        int height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * linearScale));

        if (scaled3dTexture == null || scaled3dTexture.width != width || scaled3dTexture.height != height)
        {
            ReleaseTexture();
            scaled3dTexture = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
            {
                name = "Windows3DScaledRT",
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false
            };
            scaled3dTexture.Create();
        }

        if (boundCamera != null)
        {
            boundCamera.targetTexture = scaled3dTexture;
        }

        if (blitImage != null)
        {
            blitImage.texture = scaled3dTexture;
        }

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        lastTarget3dPixelRatio = target3dPixelRatio;

        if (verboseLogs)
        {
            Debug.Log($"[WindowsResolutionScaler] 3D pixel ratio={target3dPixelRatio:F2} ({width}x{height}), UI nativa ({Screen.width}x{Screen.height}).");
        }
    }

    private void DisableScalingOutput()
    {
        lastTarget3dPixelRatio = -1f;
        UnbindCamera();
        DestroyBlitUi();
        ReleaseTexture();
    }

    private static void EnsureNativeOutputResolution()
    {
        Resolution current = Screen.currentResolution;
        int targetWidth = current.width;
        int targetHeight = current.height;

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return;
        }

        if (Screen.width != targetWidth || Screen.height != targetHeight || Screen.fullScreenMode != FullScreenMode.FullScreenWindow)
        {
            Screen.SetResolution(targetWidth, targetHeight, FullScreenMode.FullScreenWindow);
        }
    }

    private static bool IsWindowsPlayerRuntime()
    {
        return Application.isPlaying && Application.platform == RuntimePlatform.WindowsPlayer;
    }

    private static Camera ResolveWorldCamera()
    {
        if (Camera.main != null && Camera.main.enabled)
        {
            return Camera.main;
        }

        Camera fallback = null;
        var cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            var cam = cameras[i];
            if (cam == null || !cam.enabled)
            {
                continue;
            }

            if (cam.cameraType != CameraType.Game)
            {
                continue;
            }

            if (fallback == null || cam.depth < fallback.depth)
            {
                fallback = cam;
            }
        }

        return fallback;
    }

    private void EnsureBlitUi()
    {
        if (blitCanvas != null && blitImage != null)
        {
            return;
        }

        var canvasGo = new GameObject("WindowsScaled3DCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(canvasGo);

        blitCanvas = canvasGo.GetComponent<Canvas>();
        blitCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        blitCanvas.sortingOrder = -1000;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var raycaster = canvasGo.GetComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        var imageGo = new GameObject("WindowsScaled3DImage", typeof(RectTransform), typeof(RawImage));
        imageGo.transform.SetParent(canvasGo.transform, false);

        var rect = imageGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        blitImage = imageGo.GetComponent<RawImage>();
        blitImage.raycastTarget = false;
        blitImage.color = Color.white;
    }

    private void UnbindCamera()
    {
        if (boundCamera != null)
        {
            boundCamera.targetTexture = null;
            if (cullingMaskModified)
            {
                boundCamera.cullingMask = originalCullingMask;
                cullingMaskModified = false;
            }
        }

        boundCamera = null;
    }

    private void DestroyBlitUi()
    {
        if (blitCanvas != null)
        {
            Object.Destroy(blitCanvas.gameObject);
        }

        blitCanvas = null;
        blitImage = null;
    }

    private void ReleaseTexture()
    {
        if (scaled3dTexture == null)
        {
            return;
        }

        if (scaled3dTexture.IsCreated())
        {
            scaled3dTexture.Release();
        }

        Object.Destroy(scaled3dTexture);
        scaled3dTexture = null;
    }
}
