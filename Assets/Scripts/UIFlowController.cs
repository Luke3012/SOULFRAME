using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using Random = UnityEngine.Random;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
public class UIFlowController : MonoBehaviour
{
    public enum UIState
    {
        Boot,
        MainMenu,
        AvatarLibrary,
        SetupVoice,
        SetupMemory,
        MainMode
    }

    [Header("Panels")]
    public GameObject pnlMainMenu;
    public GameObject pnlAvatarLibrary;
    public GameObject pnlSetupVoice;
    public GameObject pnlSetupMemory;
    public GameObject pnlMainMode;

    [Header("Services Config")]
    [SerializeField] private SoulframeServicesConfig servicesConfig;

    [Header("Main Menu Panel")]
    public Button btnNewAvatar;
    public Button btnShowList;
    public TextMeshProUGUI debugText;
    
    [Header("Main Menu Intro")]
    [SerializeField] private bool enableMainMenuIntro = true;
    [SerializeField] private float mainMenuIntroDelay = 1.5f;
    [SerializeField] private float mainMenuButtonsFadeDuration = 0.5f;
    private CanvasGroup _titleCanvasGroup;
    private CanvasGroup _btnNewAvatarGroup;
    private CanvasGroup _btnShowListGroup;
    private bool _mainMenuButtonsIntroDone;

    [Header("Setup Voice Panel")]
    public TextMeshProUGUI setupVoiceTitleText;
    public TextMeshProUGUI setupVoicePhraseText;
    public TextMeshProUGUI setupVoiceStatusText;
    public TextMeshProUGUI setupVoiceRecText;

    [Header("Setup Memory Panel")]
    public TextMeshProUGUI setupMemoryTitleText;
    public TextMeshProUGUI setupMemoryStatusText;
    public TextMeshProUGUI setupMemoryLogText;
    public GameObject pnlChooseMemory;
    public GameObject pnlSaveMemory;
    public TMP_InputField setupMemoryNoteInput;
    public Button btnSetupMemorySave;
    public Button btnSetupMemoryIngest;
    public Button btnSetupMemoryDescribe;
    public Button btnSetMemory;
    [SerializeField] private float memoryPanelTransitionDuration = 0.3f;

    [Header("Main Mode Panel")]
    public TextMeshProUGUI mainModeStatusText;
    public TextMeshProUGUI mainModeTranscriptText;
    public TextMeshProUGUI mainModeReplyText;
    [SerializeField] private Button btnMainModeMemory;
    [SerializeField] private Button btnMainModeVoice;

    [Header("Avatar Library 3D")]
    public AvatarLibraryCarousel avatarLibraryCarousel;

    [Header("Hint Bar")]
    public UIHintBar hintBar;
    public List<HintEntry> hintEntries = new List<HintEntry>();

    [Header("Carousel UI")]
    [SerializeField] private CanvasGroup soulframeTitleGroup;

    [Header("Navigation")]
    public UINavigator navigator;

    [Header("Background Rings")]
    [SerializeField] private Transform ringsTransform;
    [SerializeField] private Transform ringsSetupVoiceAnchor;
    [SerializeField] private Transform ringsSetupMemoryAnchor;
    [SerializeField] private Vector3 ringsHiddenOffset = new Vector3(0f, 0f, 2f);
    [SerializeField] private float ringsTransitionDuration = 0.6f;
    [SerializeField] private PS2BackgroundRings ringsController;

    [Header("Download State")]
    [SerializeField] private float downloadRingsSpeedMultiplier = 2f;

    [Header("Boot State")]
    [SerializeField] private float bootRingsSpeedMultiplier = 1.6f;
    [SerializeField] private int minVoiceBytes = 512;

    [Header("Setup Voice")]
    [SerializeField] private bool devMode = false;
    [SerializeField] private float setupVoiceMinSimilarity = 0.7f;
    [SerializeField] private AudioRecorder audioRecorder;
    [SerializeField] private float longRequestTimeoutSeconds = 0f;

    [Header("Web Overlay")]
    [SerializeField] private AvaturnWebController webController;

    [Header("Main Mode")]
    [SerializeField] private float mainModeMouseIdleSeconds = 4f;
    [SerializeField] private float mainModeRayDistance = 2.5f;
    [SerializeField] private Vector3 mainModeLookHeadOffset = new Vector3(0f, 0.06f, 0.05f);
    [SerializeField] private AudioSource ttsAudioSource;
    [SerializeField] private Transform camSetupVoiceLeftAnchor;
    [SerializeField] private Transform camSetupMemoryRightAnchor;
    [SerializeField] private float cameraSmoothTime = 0.25f;
    [SerializeField] private float cameraRotateSpeed = 6f;
    [SerializeField] private float cameraReturnDuration = 0.35f;

    [Header("TTS")]
    [SerializeField] private bool enableTtsWebGlStreaming = true;
    [SerializeField] private bool enableTtsWebGlStreamingLogs = false;
    [SerializeField] private bool enableTtsWebGlUnityAudio = true;
    [SerializeField, Range(40, 400)] private int ttsStreamMaxChunkChars = 140;


    [Header("Transitions")]
    [SerializeField] private float transitionDuration = 0.25f;
    [SerializeField] private float slideOffset = 40f;

    [Header("References")]
    public AvaturnSystem avaturnSystem;
    public AvatarManager avatarManager;
    public Transform avatarSpawnPoint;

    private readonly Stack<UIState> backStack = new Stack<UIState>();
    private readonly Dictionary<UIState, GameObject> panelMap = new Dictionary<UIState, GameObject>();
    private readonly Dictionary<GameObject, CanvasGroup> panelCanvasGroups = new Dictionary<GameObject, CanvasGroup>();
    private readonly Dictionary<GameObject, Vector2> panelDefaultPositions = new Dictionary<GameObject, Vector2>();
    private readonly Dictionary<UIState, string> hintMap = new Dictionary<UIState, string>();

    private UIState currentState;
    private Coroutine transitionRoutine;
    private Coroutine ringsRoutine;
    private Vector3 ringsDefaultPosition;
    private CanvasGroup mainMenuCanvasGroup;
    private float mainMenuBaseAlpha = 1f;
    private bool pendingNewAvatarDownload;
    private bool downloadStateActive;
    private bool carouselDownloading;
    private bool webOverlayOpen;
    private Coroutine bootRoutine;
    private Coroutine setupVoiceRoutine;
    private UnityWebRequest setupVoiceRequest;
    private string setupVoicePhrase;
    private bool setupVoiceRecording;
    private bool setupVoiceCancelling;
    private bool setupVoicePhraseReady;
    private bool setupVoiceAlreadyConfigured;
    private bool setupVoiceOperationInProgress;
    private Coroutine setupMemoryRoutine;
    private Coroutine setupMemoryCheckRoutine;
    private UnityWebRequest setupMemoryRequest;
    private bool setupMemoryInputFocused;
    private bool setupMemoryOperationInProgress;
    private bool setupMemoryAlreadyConfigured;
    private Coroutine memoryPanelTransitionRoutine;
    private Coroutine mainModeRoutine;
    private Coroutine mainModeCheckRoutine;
    private Coroutine mainModeEnableRoutine;
    private UnityWebRequest mainModeRequest;
    private readonly List<UnityWebRequest> mainModeRequests = new List<UnityWebRequest>();
#if UNITY_WEBGL && !UNITY_EDITOR
    private bool ttsStreamActive;
    private bool ttsStreamDone;
#endif
    private string ttsStreamError;
    private PcmStreamPlayer ttsStreamPlayer;
    private PcmChunkPlayer ttsChunkPlayer;
    private bool ttsUseChunkPlayer;
    private long ttsStreamBytes;
    private int ttsStreamSampleRate;
    private int ttsStreamChannels;
    private bool mainModeListening;
    private bool mainModeProcessing;
    private float lastMouseMoveTime;
    private Vector3 lastMousePosition;
    private Transform currentCameraAnchor;
    private Vector3 cameraVelocity;
    private Vector3 mainMenuCameraPosition;
    private Quaternion mainMenuCameraRotation;
    private bool mainMenuCameraSaved;
    private Coroutine cameraReturnRoutine;
    private Coroutine deleteAvatarRoutine;
    private bool deleteAvatarInProgress;
    private Coroutine uiBlockFadeRoutine;
    private bool uiInputLocked;
    private bool navigatorWasEnabled;
    private bool uiBlockHintWasActive;
    private bool uiBlockPanelWasActive;
    private float uiBlockPanelAlpha = 1f;
    private float uiBlockHintAlpha = 1f;
    private bool uiBlockActive;
    private Coroutine deferredStateRoutine;

    // Fix 1: Flag per controllare se la transizione a MainMode è stata richiesta dall'utente
    private bool _pendingMainModeTransition = false;
    private bool _previewModeActive = false;
    private bool setupVoiceFromMainMode = false;
    private bool setupMemoryFromMainMode = false;
    private bool ingestFilePickerActive = false;
    private bool describeFilePickerActive = false;

    private static readonly string[] waitPhraseKeys = { "hm", "beh", "aspetta", "si", "un_secondo" };
    private readonly Dictionary<string, AudioClip> waitPhraseCache = new Dictionary<string, AudioClip>();
    private Coroutine waitPhraseRoutine;

    public bool IsWebOverlayOpen => webOverlayOpen;
    public bool IsUiInputLocked => uiInputLocked;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int EnsureDynCallV();
    [DllImport("__Internal")] private static extern void TtsStream_Start(
        string url,
        string text,
        string avatarId,
        string language,
        string targetObject,
        int maxChunkChars);
    [DllImport("__Internal")] private static extern void TtsStream_Stop();
#endif

    void Awake()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            int ok = EnsureDynCallV();
            Debug.Log("[WebGL] EnsureDynCallV (UIFlowController Awake) => " + ok);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[WebGL] EnsureDynCallV (UIFlowController Awake) failed: " + e.Message);
        }
#endif
    }

    void Start()
    {
        Debug.Log("UIFlowController Start chiamato");

#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            int ok = EnsureDynCallV();
            Debug.Log("[WebGL] EnsureDynCallV => " + ok);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[WebGL] EnsureDynCallV failed: " + e.Message);
        }

#endif

        SaveMainMenuCameraPosition();

        ValidateReferencesAtRuntime();

        if (avatarLibraryCarousel != null)
        {
            avatarLibraryCarousel.Initialize(avatarManager, this);
        }

        if (ringsTransform != null)
        {
            ringsDefaultPosition = ringsTransform.position;
        }
        
        // Fix 1: Risolvi il CanvasGroup del titolo
        ResolveTitleGroup();

        if (btnNewAvatar != null) btnNewAvatar.onClick.AddListener(OnNewAvatar);
        if (btnShowList != null) btnShowList.onClick.AddListener(GoToAvatarLibrary);
        if (btnMainModeMemory != null) btnMainModeMemory.onClick.AddListener(GoToSetupMemory);
        if (btnMainModeVoice != null) btnMainModeVoice.onClick.AddListener(GoToSetupVoice);
        if (btnSetupMemorySave != null) btnSetupMemorySave.onClick.AddListener(ShowSaveMemoryPanel);
        if (btnSetupMemoryIngest != null) btnSetupMemoryIngest.onClick.AddListener(StartIngestFile);
        if (btnSetupMemoryDescribe != null) btnSetupMemoryDescribe.onClick.AddListener(StartDescribeImage);
        if (btnSetMemory != null) btnSetMemory.onClick.AddListener(OnSetMemory);
        
        // NOTE: All UI back/navigation buttons removed in favor of keyboard Backspace handling.
        // Hint bar shows keyboard prompts to guide users (Arrows/Enter/Backspace).

        BuildPanelMap();
        CachePanelDefaults();
        BuildHintMap();

        if (pnlMainMenu != null)
        {
            mainMenuCanvasGroup = GetOrAddCanvasGroup(pnlMainMenu);
            if (mainMenuCanvasGroup != null)
            {
                mainMenuBaseAlpha = mainMenuCanvasGroup.alpha;
            }
        }

        if (navigator != null)
        {
            navigator.SetActions(GoBack, ExitApplication);
        }
        
        // Fix 1: Nascondi i bottoni se l'intro è abilitata
        if (enableMainMenuIntro)
        {
            SetMainMenuButtonsVisible(false, immediate: true);
        }

        SetStateImmediate(UIState.Boot);

        UpdateHintBar(UIState.Boot);

        UpdateDebugText("INIZIALIZZAZIONE");

        if (avaturnSystem != null)
        {
            avaturnSystem.SetupAvatarCallbacks(OnAvatarReceived, null);
        }

        // Fix 1: Avvia intro se abilitata
        if (enableMainMenuIntro)
        {
            StartCoroutine(MainMenuButtonsIntroRoutine());
        }

        EnsureCameraAnchors();

        if (bootRoutine != null)
        {
            StopCoroutine(bootRoutine);
        }
        BeginBootSequence();
    }

    void Update()
    {
        if (uiInputLocked)
        {
            UpdateCameraRig();
            return;
        }

        if (currentState == UIState.SetupVoice)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                StartSetupVoiceRecording();
            }
            if (Input.GetKeyUp(KeyCode.Space))
            {
                StopSetupVoiceRecording();
            }
        }

        if (currentState == UIState.SetupMemory)
        {
            bool focused = setupMemoryNoteInput != null && setupMemoryNoteInput.isFocused;
            if (focused != setupMemoryInputFocused)
            {
                setupMemoryInputFocused = focused;
                UpdateHintBar(currentState);
                ConfigureNavigatorForState(currentState, true);
            }

            if (focused && Input.GetKeyDown(KeyCode.Return))
            {
                if (pnlSaveMemory != null && pnlSaveMemory.activeSelf)
                {
                    OnSetMemory();
                }
            }
        }

        if (currentState == UIState.MainMode)
        {
            HandleMainModeInput();
            UpdateMainModeMouseLook();
        }

        UpdateCameraRig();
    }

    private void BuildPanelMap()
    {
        panelMap.Clear();
        panelMap[UIState.MainMenu] = pnlMainMenu;
        panelMap[UIState.AvatarLibrary] = pnlAvatarLibrary;
        panelMap[UIState.SetupVoice] = pnlSetupVoice;
        panelMap[UIState.SetupMemory] = pnlSetupMemory;
        panelMap[UIState.MainMode] = pnlMainMode;
    }
    
    // Fix 1 parte 3: Metodi helper per l'intro
    private void ResolveTitleGroup()
    {
        if (soulframeTitleGroup != null)
        {
            _titleCanvasGroup = soulframeTitleGroup;
        }
        else
        {
            Debug.LogError("[UIFlowController] soulframeTitleGroup non assegnato in Inspector (Title_SOULFRAME CanvasGroup).");
        }

        if (btnNewAvatar != null)
        {
            _btnNewAvatarGroup = btnNewAvatar.GetComponent<CanvasGroup>();
            if (_btnNewAvatarGroup == null)
            {
                Debug.LogError("[UIFlowController] Btn_New richiede CanvasGroup per intro (assegnalo in scena). Intro disabilitata.");
                enableMainMenuIntro = false;
            }
        }

        if (btnShowList != null)
        {
            _btnShowListGroup = btnShowList.GetComponent<CanvasGroup>();
            if (_btnShowListGroup == null)
            {
                Debug.LogError("[UIFlowController] Btn_LoadList richiede CanvasGroup per intro (assegnalo in scena). Intro disabilitata.");
                enableMainMenuIntro = false;
            }
        }
        
        // If either button is missing entirely, also disable intro
        if (enableMainMenuIntro && (!btnNewAvatar || !btnShowList))
        {
            Debug.LogError("[UIFlowController] Btn_New o Btn_LoadList mancante. Intro disabilitata.");
            enableMainMenuIntro = false;
        }
    }
    
    private void SetMainMenuButtonsVisible(bool visible, bool immediate = false)
    {
        float targetAlpha = visible ? 1f : 0f;
        bool interactable = visible;
        
        if (_btnNewAvatarGroup != null)
        {
            if (immediate)
            {
                _btnNewAvatarGroup.alpha = targetAlpha;
            }
            _btnNewAvatarGroup.interactable = interactable;
            _btnNewAvatarGroup.blocksRaycasts = interactable;
        }
        
        if (_btnShowListGroup != null)
        {
            if (immediate)
            {
                _btnShowListGroup.alpha = targetAlpha;
            }
            _btnShowListGroup.interactable = interactable;
            _btnShowListGroup.blocksRaycasts = interactable;
        }
    }
    
    private IEnumerator MainMenuButtonsIntroRoutine()
    {
        // Aspetta che il titolo abbia completato il fade-in
        if (_titleCanvasGroup != null)
        {
            while (_titleCanvasGroup.alpha < 0.99f)
            {
                yield return null;
            }
        }
        
        // Delay extra
        if (mainMenuIntroDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(mainMenuIntroDelay);
        }
        
        // Fade-in dei bottoni
        float elapsed = 0f;
        while (elapsed < mainMenuButtonsFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / mainMenuButtonsFadeDuration);
            
            if (_btnNewAvatarGroup != null)
            {
                _btnNewAvatarGroup.alpha = t;
            }
            if (_btnShowListGroup != null)
            {
                _btnShowListGroup.alpha = t;
            }
            
            yield return null;
        }
        
        // Completa e rendi interattivi
        SetMainMenuButtonsVisible(true, immediate: false);
        _mainMenuButtonsIntroDone = true;
        
        // Aggiorna il navigator
        ConfigureNavigatorForState(UIState.MainMenu, true);
    }

    private void BuildHintMap()
    {
        hintMap.Clear();
        if (hintEntries.Count == 0)
        {
            hintMap[UIState.Boot] = "[BACK] Skip";
            hintMap[UIState.MainMenu] = "[X] Enter   [Tri] Close";
            hintMap[UIState.AvatarLibrary] = "[X] Select   [O] Back";
            hintMap[UIState.SetupVoice] = "[SPACE] Record   [O] Back";
            hintMap[UIState.SetupMemory] = "[X] Save   [O] Back";
            hintMap[UIState.MainMode] = "[X] Confirm   [O] Back";
            return;
        }
        foreach (var entry in hintEntries)
        {
            hintMap[entry.state] = entry.hints;
        }
    }

    private void CachePanelDefaults()
    {
        foreach (var panel in panelMap.Values)
        {
            if (panel == null)
            {
                continue;
            }

            var rect = panel.GetComponent<RectTransform>();
            if (rect != null && !panelDefaultPositions.ContainsKey(panel))
            {
                panelDefaultPositions[panel] = rect.anchoredPosition;
            }

            GetOrAddCanvasGroup(panel);
        }
    }

    void OnNewAvatar()
    {
        pendingNewAvatarDownload = true;
#if UNITY_EDITOR
        string url = avaturnSystem != null ? avaturnSystem.GetAvaturnUrl() : "https://demo.avaturn.dev";
        Application.OpenURL(url);
        UpdateDebugText("Editor: Apri URL nel browser esterno");
#elif UNITY_WEBGL && !UNITY_EDITOR
        SetWebOverlayOpen(true);
        if (webController != null)
        {
            webController.OnClick_NewAvatar();
            UpdateDebugText("Iframe Avaturn aperto. Crea il tuo avatar!");
        }
        else
        {
            if (avaturnSystem != null)
            {
                avaturnSystem.ShowAvaturnIframe();
                UpdateDebugText("Iframe Avaturn aperto tramite sistema.");
            }
            else
            {
                UpdateDebugText("Errore: Nessun controller Avaturn trovato!");
            }
        }
#else
        UpdateDebugText("Funzionalità mobile - usa il prefab originale per mobile");
#endif
    }

    public void GoToMainMenu()
    {
        // Fix 1: Resetta il flag quando torni a casa
        _pendingMainModeTransition = false;
        pendingNewAvatarDownload = false;
        ExitDownloadState();
        avatarManager?.CancelAllDownloads();
        avatarManager?.RemoveCurrentAvatar();
        backStack.Clear();
        GoToState(UIState.MainMenu);
    }

    // Fix 1: Metodo pubblico per notificare che l'utente ha richiesto un main avatar load
    public void NotifyMainAvatarLoadRequested()
    {
        _pendingMainModeTransition = true;
        Debug.Log("[UIFlowController] Main avatar load requested by user.");
    }

    public void GoToAvatarLibrary()
    {
        GoToState(UIState.AvatarLibrary);
    }

    public void OnAvatarLibrarySelectionChanged()
    {
        if (currentState == UIState.AvatarLibrary)
        {
            UpdateHintBar(currentState);
        }
    }

    public void RequestDeleteSelectedAvatar()
    {
        if (currentState != UIState.AvatarLibrary)
        {
            return;
        }

        if (deleteAvatarInProgress)
        {
            UpdateDebugText("Eliminazione avatar gia' in corso.");
            return;
        }

        if (avatarLibraryCarousel == null || !avatarLibraryCarousel.TryGetSelectedAvatarData(out var data))
        {
            UpdateDebugText("Nessun avatar selezionato.");
            return;
        }

        if (string.IsNullOrEmpty(data.avatarId))
        {
            UpdateDebugText("Avatar ID mancante.");
            return;
        }

        if (deleteAvatarRoutine != null)
        {
            StopCoroutine(deleteAvatarRoutine);
        }

        deleteAvatarRoutine = StartCoroutine(DeleteSelectedAvatarRoutine(data));
    }

    public void GoToSetupVoice()
    {
        GoToState(UIState.SetupVoice);
    }

    public void GoToSetupMemory()
    {
        GoToState(UIState.SetupMemory);
    }

    public void GoToMainMode()
    {
        GoToState(UIState.MainMode);
    }

    private IEnumerator DeleteSelectedAvatarRoutine(AvatarManager.AvatarData data)
    {
        deleteAvatarInProgress = true;

        if (servicesConfig == null)
        {
            UpdateDebugText("ServicesConfig mancante.");
            PlayErrorClip();
            deleteAvatarInProgress = false;
            deleteAvatarRoutine = null;
            yield break;
        }

        string avatarId = data != null ? data.avatarId : null;
        if (string.IsNullOrEmpty(avatarId))
        {
            UpdateDebugText("Avatar ID mancante.");
            PlayErrorClip();
            deleteAvatarInProgress = false;
            deleteAvatarRoutine = null;
            yield break;
        }

        bool isLocal = IsLocalAvatar(data);
        UpdateDebugText(isLocal ? "Reset avatar in corso..." : "Eliminazione avatar in corso...");

        bool ok = false;
        string error = null;

        yield return StartCoroutine(DeleteCoquiAvatarVoice(avatarId,
            success => ok = success,
            err => error = err));

        if (!ok)
        {
            UpdateDebugText($"Eliminazione fallita (TTS): {error}");
            PlayErrorClip();
            deleteAvatarInProgress = false;
            deleteAvatarRoutine = null;
            yield break;
        }

        ok = false;
        error = null;
        yield return StartCoroutine(ClearRagAvatar(avatarId,
            success => ok = success,
            err => error = err));

        if (!ok)
        {
            UpdateDebugText($"Eliminazione fallita (RAG): {error}");
            PlayErrorClip();
            deleteAvatarInProgress = false;
            deleteAvatarRoutine = null;
            yield break;
        }

        if (!isLocal)
        {
            ok = false;
            error = null;
            yield return StartCoroutine(DeleteAvatarAsset(avatarId,
                success => ok = success,
                err => error = err));

            if (!ok)
            {
                UpdateDebugText($"Eliminazione fallita (Avatar): {error}");
                PlayErrorClip();
                deleteAvatarInProgress = false;
                deleteAvatarRoutine = null;
                yield break;
            }

            avatarManager?.RemoveAvatarFromSavedData(avatarId);
        }

        UpdateDebugText(isLocal ? "Reset avatar completato." : "Avatar eliminato.");

        if (navigator != null)
        {
            if (isLocal)
            {
                navigator.PlayResetClip();
            }
            else
            {
                navigator.PlayDeleteClip();
            }
        }

        if (avatarLibraryCarousel != null)
        {
            if (isLocal)
            {
                yield return StartCoroutine(avatarLibraryCarousel.PlayResetEffect());
            }
            else
            {
                yield return StartCoroutine(avatarLibraryCarousel.PlayDeleteEffect());
            }
        }

        if (!isLocal && avatarLibraryCarousel != null)
        {
            avatarLibraryCarousel.ShowLibrary(true);
        }

        deleteAvatarInProgress = false;
        deleteAvatarRoutine = null;
    }

    private IEnumerator DeleteCoquiAvatarVoice(
        string avatarId,
        System.Action<bool> onSuccess,
        System.Action<string> onFailure)
    {
        string url = BuildServiceUrl(
            servicesConfig.coquiBaseUrl,
            $"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}");

        using (var request = UnityWebRequest.Delete(url))
        {
            request.timeout = GetRequestTimeoutSeconds(longOperation: true);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = request.error ?? "Network error";
                ReportServiceError("Coqui", error);
                onFailure?.Invoke(error);
                yield break;
            }

            onSuccess?.Invoke(true);
        }
    }

    private IEnumerator ClearRagAvatar(
        string avatarId,
        System.Action<bool> onSuccess,
        System.Action<string> onFailure)
    {
        var form = new WWWForm();
        form.AddField("avatar_id", avatarId);
        form.AddField("hard", "true");

        using (var request = UnityWebRequest.Post(BuildServiceUrl(servicesConfig.ragBaseUrl, "clear_avatar"), form))
        {
            request.timeout = GetRequestTimeoutSeconds(longOperation: true);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = request.error ?? "Network error";
                ReportServiceError("RAG", error);
                onFailure?.Invoke(error);
                yield break;
            }

            try
            {
                var payload = JsonUtility.FromJson<ClearAvatarResponse>(request.downloadHandler.text);
                if (payload != null && payload.ok)
                {
                    onSuccess?.Invoke(true);
                    yield break;
                }

                onFailure?.Invoke("Risposta RAG non valida");
            }
            catch (System.Exception ex)
            {
                onFailure?.Invoke(ex.Message);
            }
        }
    }

    private IEnumerator DeleteAvatarAsset(
        string avatarId,
        System.Action<bool> onSuccess,
        System.Action<string> onFailure)
    {
        string url = BuildServiceUrl(servicesConfig.avatarAssetBaseUrl, $"avatars/{avatarId}");

        using (var request = UnityWebRequest.Delete(url))
        {
            request.timeout = GetRequestTimeoutSeconds(longOperation: true);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = request.error ?? "Network error";
                ReportServiceError("AvatarAsset", error);
                onFailure?.Invoke(error);
                yield break;
            }

            onSuccess?.Invoke(true);
        }
    }

    private static bool IsLocalAvatar(AvatarManager.AvatarData data)
    {
        if (data == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(data.source) && data.source == "local")
        {
            return true;
        }

        if (!string.IsNullOrEmpty(data.bodyId) && data.bodyId == "local")
        {
            return true;
        }

        return !string.IsNullOrEmpty(data.avatarId) && data.avatarId.StartsWith("LOCAL_");
    }

    private string GetAvatarLibraryDeleteLabel()
    {
        if (avatarLibraryCarousel != null && avatarLibraryCarousel.TryGetSelectedAvatarData(out var data))
        {
            return IsLocalAvatar(data) ? "Reset Avatar" : "Elimina Avatar";
        }

        return "Elimina Avatar";
    }

    private void PlayErrorClip()
    {
        if (navigator != null)
        {
            navigator.PlayErrorClip();
        }
    }

    private void BeginSetupVoice()
    {
        CancelSetupVoice();
        setupVoiceCancelling = false;
        setupVoicePhraseReady = false;
        setupVoiceAlreadyConfigured = false;

        if (setupVoiceRecText != null)
        {
            setupVoiceRecText.text = string.Empty;
        }

        if (audioRecorder == null)
        {
            UpdateSetupVoiceStatus("AudioRecorder mancante in scena.");
            UpdateDebugText("AudioRecorder mancante in scena.");
            Debug.LogError("[UIFlowController] audioRecorder non assegnato in Inspector.");
            PlayErrorClip();
            return;
        }

        string gender = avatarManager != null ? avatarManager.CurrentAvatarGender : "unknown";
        string readyMessage = gender == "female" ? "Sei pronta?" : "Sei pronto?";
        UpdateSetupVoiceStatus(readyMessage);
        if (setupVoicePhraseText != null)
        {
            setupVoicePhraseText.text = "Preparati ad enunciare ciò che ti sarà chiesto ;)";
        }
        UpdateDebugText($"Setup Voice iniziato - Gender: {gender}");

        if (setupVoiceRoutine != null)
        {
            StopCoroutine(setupVoiceRoutine);
        }
        setupVoiceRoutine = StartCoroutine(EnsureVoiceSetupNeededRoutine());
    }

    private IEnumerator EnsureVoiceSetupNeededRoutine()
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId) || servicesConfig == null)
        {
            UpdateSetupVoiceStatus("Dati mancanti. Riprova.");
            setupVoicePhraseReady = false;
            PlayErrorClip();
            yield break;
        }

        UpdateSetupVoiceStatus("Verifica voce...");
        AvatarVoiceInfo voiceInfo = null;
        string voiceError = null;
        yield return StartCoroutine(FetchJson<AvatarVoiceInfo>(
            BuildServiceUrl(servicesConfig.coquiBaseUrl, $"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "Coqui",
            info => voiceInfo = info,
            error => voiceError = error
        ));

        if (!string.IsNullOrEmpty(voiceError))
        {
            UpdateDebugText($"Verifica voce fallita: {voiceError}");
            PlayErrorClip();
        }

        bool voiceConfigured = voiceInfo != null && voiceInfo.exists && voiceInfo.bytes >= minVoiceBytes;
        setupVoiceAlreadyConfigured = voiceConfigured;
        if (setupVoicePhraseText != null)
        {
            string gender = avatarManager != null ? avatarManager.CurrentAvatarGender : "unknown";
            string reconfigureMessage = gender == "female"
                ? "Rieccoti qui. Sei pronta a modificare la tua voce?"
                : "Rieccoti qui. Sei pronto a modificare la tua voce?";
            setupVoicePhraseText.text = voiceConfigured
                ? reconfigureMessage
                : "Preparati ad enunciare ciò che ti sarà chiesto ;)";
        }
        if (voiceConfigured)
        {
            UpdateSetupVoiceStatus("Voce già configurata.");
            yield return StartCoroutine(SetupVoicePhraseRoutine());
            yield break;
        }

        yield return StartCoroutine(SetupVoicePhraseRoutine());
    }

    private void StartSetupVoiceRecording()
    {
        if (currentState != UIState.SetupVoice)
        {
            return;
        }

        if (setupVoiceRecording || setupVoiceCancelling || !setupVoicePhraseReady)
        {
            if (!setupVoicePhraseReady)
            {
                UpdateSetupVoiceStatus("Attendi la frase prima di registrare.");
            }
            return;
        }

        if (audioRecorder == null)
        {
            UpdateSetupVoiceStatus("Recorder mancante.");
            PlayErrorClip();
            return;
        }

        if (!audioRecorder.HasMicrophoneAvailable())
        {
            UpdateSetupVoiceStatus("Consenti microfono per continuare.");
            PlayErrorClip();
            return;
        }

        if (!audioRecorder.StartRecording())
        {
            UpdateSetupVoiceStatus("Consenti microfono per continuare.");
            PlayErrorClip();
            return;
        }

        setupVoiceRecording = true;
        setupVoiceCancelling = false;

        if (setupVoiceRecText != null)
        {
            setupVoiceRecText.text = "REC";
        }

        UpdateSetupVoiceStatus("Registrazione... (rilascia SPACE per terminare)");
        UpdateDebugText("Setup Voice: registrazione in corso...");
        UpdateHintBar(UIState.SetupVoice);
    }

    private void StopSetupVoiceRecording()
    {
        if (currentState != UIState.SetupVoice)
        {
            return;
        }

        if (!setupVoiceRecording || setupVoiceCancelling)
        {
            return;
        }

        setupVoiceRecording = false;
        UpdateHintBar(UIState.SetupVoice);
        if (setupVoiceRoutine != null)
        {
            StopCoroutine(setupVoiceRoutine);
        }
        setupVoiceRoutine = StartCoroutine(ProcessSetupVoiceRecording());
    }

    private void CancelSetupVoice()
    {
        setupVoiceCancelling = true;
        setupVoiceRecording = false;
        setupVoicePhraseReady = false;
        if (setupVoiceRoutine != null)
        {
            StopCoroutine(setupVoiceRoutine);
            setupVoiceRoutine = null;
        }

        if (setupVoiceRequest != null)
        {
            setupVoiceRequest.Abort();
            setupVoiceRequest.Dispose();
            setupVoiceRequest = null;
        }

        if (audioRecorder != null)
        {
            audioRecorder.StopRecording();
        }

        if (setupVoiceRecText != null)
        {
            setupVoiceRecText.text = string.Empty;
        }

        UpdateHintBar(UIState.SetupVoice);
    }

    private void OnSetupVoiceConfirmed()
    {
        if (setupVoiceFromMainMode)
        {
            setupVoiceFromMainMode = false;
            GoToMainMode();
            return;
        }

        if (bootRoutine != null)
        {
            StopCoroutine(bootRoutine);
        }

        bootRoutine = StartCoroutine(RouteAfterVoiceSetup());
    }

    private void OnSetupMemoryConfirmed()
    {
        if (bootRoutine != null)
        {
            StopCoroutine(bootRoutine);
        }

        bootRoutine = StartCoroutine(RouteAfterMemorySetup());
    }

    private void StartIngestFile()
    {
        if (currentState != UIState.SetupMemory)
        {
            return;
        }

        if (uiInputLocked)
        {
            return;
        }

        if (ingestFilePickerActive)
        {
            return;
        }

        if (setupMemoryRoutine != null)
        {
            StopCoroutine(setupMemoryRoutine);
        }

        ingestFilePickerActive = true;
        setupMemoryRoutine = StartCoroutine(IngestFileRoutine());
    }

    private void StartDescribeImage()
    {
        if (currentState != UIState.SetupMemory)
        {
            return;
        }

        if (uiInputLocked)
        {
            return;
        }

        if (describeFilePickerActive)
        {
            return;
        }

        if (setupMemoryRoutine != null)
        {
            StopCoroutine(setupMemoryRoutine);
        }

        describeFilePickerActive = true;
        setupMemoryRoutine = StartCoroutine(DescribeImageRoutine());
    }

    private void OnRememberNote()
    {
        if (currentState != UIState.SetupMemory)
        {
            return;
        }

        if (uiInputLocked)
        {
            return;
        }

        if (setupMemoryRoutine != null)
        {
            StopCoroutine(setupMemoryRoutine);
        }

        setupMemoryRoutine = StartCoroutine(RememberNoteRoutine());
    }

    private void ShowChooseMemoryPanel()
    {
        var chooseGroup = pnlChooseMemory != null ? GetOrAddCanvasGroup(pnlChooseMemory) : null;
        var saveGroup = pnlSaveMemory != null ? GetOrAddCanvasGroup(pnlSaveMemory) : null;

        if (pnlChooseMemory != null)
        {
            pnlChooseMemory.SetActive(true);
            if (chooseGroup != null)
            {
                chooseGroup.alpha = 1f;
                chooseGroup.interactable = true;
                chooseGroup.blocksRaycasts = true;
            }
        }
        if (pnlSaveMemory != null)
        {
            pnlSaveMemory.SetActive(false);
            if (saveGroup != null)
            {
                saveGroup.alpha = 0f;
                saveGroup.interactable = false;
                saveGroup.blocksRaycasts = false;
            }
        }
        setupMemoryInputFocused = false;
        ConfigureNavigatorForState(UIState.SetupMemory, true);
    }

    private void ShowSaveMemoryPanel()
    {
        if (memoryPanelTransitionRoutine != null)
        {
            StopCoroutine(memoryPanelTransitionRoutine);
        }
        memoryPanelTransitionRoutine = StartCoroutine(TransitionToSaveMemory());
    }

    private IEnumerator EnsureMemorySetupNeededRoutine()
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId) || servicesConfig == null)
        {
            yield break;
        }

        UpdateSetupMemoryLog("Verifica memoria...");
        AvatarStatsInfo statsInfo = null;
        string statsError = null;
        yield return StartCoroutine(FetchJson<AvatarStatsInfo>(
            BuildServiceUrl(servicesConfig.ragBaseUrl, $"avatar_stats?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "RAG",
            info => statsInfo = info,
            error => statsError = error
        ));

        if (!string.IsNullOrEmpty(statsError))
        {
            UpdateDebugText($"Verifica memoria fallita: {statsError}");
            PlayErrorClip();
            yield break;
        }

        setupMemoryAlreadyConfigured = statsInfo != null && statsInfo.has_memory;
        if (setupMemoryAlreadyConfigured)
        {
            UpdateSetupMemoryLog("Memoria già presente.");
            yield break;
        }
    }

    private IEnumerator TransitionToSaveMemory()
    {
        var chooseGroup = pnlChooseMemory != null ? GetOrAddCanvasGroup(pnlChooseMemory) : null;
        var saveGroup = pnlSaveMemory != null ? GetOrAddCanvasGroup(pnlSaveMemory) : null;

        if (pnlSaveMemory != null)
        {
            pnlSaveMemory.SetActive(true);
        }

        if (saveGroup != null)
        {
            saveGroup.alpha = 0f;
            saveGroup.interactable = false;
            saveGroup.blocksRaycasts = false;
        }

        float elapsed = 0f;
        while (elapsed < memoryPanelTransitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / memoryPanelTransitionDuration);

            if (chooseGroup != null)
            {
                chooseGroup.alpha = 1f - t;
            }
            if (saveGroup != null)
            {
                saveGroup.alpha = t;
            }

            yield return null;
        }

        if (chooseGroup != null)
        {
            chooseGroup.alpha = 0f;
            chooseGroup.interactable = false;
            chooseGroup.blocksRaycasts = false;
        }
        if (pnlChooseMemory != null)
        {
            pnlChooseMemory.SetActive(false);
        }

        if (saveGroup != null)
        {
            saveGroup.alpha = 1f;
            saveGroup.interactable = true;
            saveGroup.blocksRaycasts = true;
        }

        if (setupMemoryNoteInput != null)
        {
            setupMemoryNoteInput.Select();
            setupMemoryNoteInput.ActivateInputField();
        }

        ConfigureNavigatorForState(UIState.SetupMemory, true);
    }

    private IEnumerator TransitionToChooseMemory()
    {
        var chooseGroup = pnlChooseMemory != null ? GetOrAddCanvasGroup(pnlChooseMemory) : null;
        var saveGroup = pnlSaveMemory != null ? GetOrAddCanvasGroup(pnlSaveMemory) : null;

        if (pnlChooseMemory != null)
        {
            pnlChooseMemory.SetActive(true);
        }

        if (chooseGroup != null)
        {
            chooseGroup.alpha = 0f;
            chooseGroup.interactable = false;
            chooseGroup.blocksRaycasts = false;
        }

        float elapsed = 0f;
        while (elapsed < memoryPanelTransitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / memoryPanelTransitionDuration);

            if (saveGroup != null)
            {
                saveGroup.alpha = 1f - t;
            }
            if (chooseGroup != null)
            {
                chooseGroup.alpha = t;
            }

            yield return null;
        }

        if (saveGroup != null)
        {
            saveGroup.alpha = 0f;
            saveGroup.interactable = false;
            saveGroup.blocksRaycasts = false;
        }
        if (pnlSaveMemory != null)
        {
            pnlSaveMemory.SetActive(false);
        }

        if (chooseGroup != null)
        {
            chooseGroup.alpha = 1f;
            chooseGroup.interactable = true;
            chooseGroup.blocksRaycasts = true;
        }

        setupMemoryInputFocused = false;
        ConfigureNavigatorForState(UIState.SetupMemory, true);
    }

    private void OnSetMemory()
    {
        if (currentState != UIState.SetupMemory)
        {
            return;
        }
        if (uiInputLocked)
        {
            return;
        }
        if (setupMemoryRoutine != null)
        {
            StopCoroutine(setupMemoryRoutine);
        }

        setupMemoryRoutine = StartCoroutine(SetMemoryRoutine());
    }

    private IEnumerator SetMemoryRoutine()
    {
        string text = setupMemoryNoteInput != null ? setupMemoryNoteInput.text : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            ReturnToChooseMemory("Nota vuota.");
            PlayErrorClip();
            yield break;
        }

        if (servicesConfig == null)
        {
            ReturnToChooseMemory("ServicesConfig mancante.");
            PlayErrorClip();
            yield break;
        }

        yield return StartCoroutine(ShowRingsForOperation());

        bool rememberOk = false;
        yield return StartCoroutine(RememberText(
            text,
            new RememberMeta { source_type = "manual_note" },
            ok => rememberOk = ok,
            false
        ));

        yield return StartCoroutine(HideRingsAfterOperation());

        if (!rememberOk)
        {
            ReturnToChooseMemory("Errore salvataggio memoria. Riprova.");
            PlayErrorClip();
            yield break;
        }

        if (setupMemoryNoteInput != null)
        {
            setupMemoryNoteInput.text = string.Empty;
        }

        yield return new WaitForSeconds(0.5f);
        
        GoToMainMode();
    }

    private void ReturnToChooseMemory(string message)
    {
        UpdateSetupMemoryLog(message);
        UpdateDebugText(message);

        if (memoryPanelTransitionRoutine != null)
        {
            StopCoroutine(memoryPanelTransitionRoutine);
        }

        if (pnlSaveMemory != null && pnlSaveMemory.activeSelf)
        {
            memoryPanelTransitionRoutine = StartCoroutine(TransitionToChooseMemory());
        }
        else
        {
            ShowChooseMemoryPanel();
        }
    }

    private IEnumerator ShowRingsForOperation()
    {
        setupMemoryOperationInProgress = true;
        if (ringsTransform != null)
        {
            UpdateRingsForState(UIState.SetupMemory);
        }

        if (ringsController != null)
        {
            ringsController.SetOrbitSpeedMultiplier(downloadRingsSpeedMultiplier);
        }

        yield return StartCoroutine(FadeUiBlocked(true));
    }

    private IEnumerator HideRingsAfterOperation()
    {
        setupMemoryOperationInProgress = false;
        if (ringsTransform != null)
        {
            SetRingsVisible(false);
        }

        if (ringsController != null && !downloadStateActive)
        {
            ringsController.SetOrbitSpeedMultiplier(1f);
        }

        yield return StartCoroutine(FadeUiBlocked(false));
    }

    private IEnumerator ShowRingsForVoiceOperation()
    {
        setupVoiceOperationInProgress = true;
        if (ringsTransform != null)
        {
            UpdateRingsForState(UIState.SetupVoice);
        }

        if (ringsController != null)
        {
            ringsController.SetOrbitSpeedMultiplier(downloadRingsSpeedMultiplier);
        }

        yield return StartCoroutine(FadeUiBlocked(true));
    }

    private IEnumerator HideRingsAfterVoiceOperation()
    {
        setupVoiceOperationInProgress = false;
        if (ringsTransform != null)
        {
            SetRingsVisible(false);
        }

        if (ringsController != null && !downloadStateActive)
        {
            ringsController.SetOrbitSpeedMultiplier(1f);
        }

        yield return StartCoroutine(FadeUiBlocked(false));
    }


    private IEnumerator FadeUiBlocked(bool blocked)
    {
        if (uiBlockFadeRoutine != null)
        {
            StopCoroutine(uiBlockFadeRoutine);
        }

        uiBlockFadeRoutine = StartCoroutine(FadeUiBlockedRoutine(blocked));
        yield return uiBlockFadeRoutine;
        uiBlockFadeRoutine = null;
    }

    private IEnumerator FadeUiBlockedRoutine(bool blocked)
    {
        GameObject panel = GetPanel(currentState);
        CanvasGroup panelGroup = panel != null ? GetOrAddCanvasGroup(panel) : null;
        CanvasGroup hintGroup = hintBar != null ? GetOrAddCanvasGroup(hintBar.gameObject) : null;

        if (blocked)
        {
            uiBlockActive = true;
            uiInputLocked = true;
            if (navigator != null)
            {
                navigatorWasEnabled = navigator.enabled;
                navigator.enabled = false;
            }

            if (panel != null)
            {
                uiBlockPanelWasActive = panel.activeSelf;
                uiBlockPanelAlpha = panelGroup != null ? panelGroup.alpha : 1f;
                if (uiBlockPanelWasActive && panelGroup != null)
                {
                    panelGroup.interactable = false;
                    panelGroup.blocksRaycasts = false;
                }
            }

            if (hintBar != null)
            {
                uiBlockHintWasActive = hintBar.gameObject.activeSelf;
                uiBlockHintAlpha = hintGroup != null ? hintGroup.alpha : 1f;
                if (uiBlockHintWasActive && hintGroup != null)
                {
                    hintGroup.interactable = false;
                    hintGroup.blocksRaycasts = false;
                }
            }
        }
        else if (!uiBlockActive)
        {
            uiInputLocked = false;
            if (navigator != null)
            {
                navigator.enabled = navigatorWasEnabled;
            }
            yield break;
        }

        float duration = transitionDuration;
        float elapsed = 0f;
        float fromPanel = panelGroup != null ? panelGroup.alpha : 0f;
        float toPanel = blocked ? 0f : uiBlockPanelAlpha;
        float fromHint = hintGroup != null ? hintGroup.alpha : 0f;
        float toHint = blocked ? 0f : uiBlockHintAlpha;

        if (uiBlockPanelWasActive && panel != null)
        {
            panel.SetActive(true);
        }

        if (uiBlockHintWasActive && hintBar != null)
        {
            hintBar.gameObject.SetActive(true);
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);

            if (panelGroup != null && uiBlockPanelWasActive)
            {
                panelGroup.alpha = Mathf.Lerp(fromPanel, toPanel, t);
            }

            if (hintGroup != null && uiBlockHintWasActive)
            {
                hintGroup.alpha = Mathf.Lerp(fromHint, toHint, t);
            }

            yield return null;
        }

        if (panelGroup != null && uiBlockPanelWasActive)
        {
            panelGroup.alpha = toPanel;
            panelGroup.interactable = !blocked;
            panelGroup.blocksRaycasts = !blocked;
        }

        if (hintGroup != null && uiBlockHintWasActive)
        {
            hintGroup.alpha = toHint;
            hintGroup.interactable = !blocked;
            hintGroup.blocksRaycasts = !blocked;
        }

        if (!blocked)
        {
            uiInputLocked = false;
            if (navigator != null)
            {
                navigator.enabled = navigatorWasEnabled;
            }

            uiBlockActive = false;

            if (hintBar != null && !uiBlockHintWasActive)
            {
                hintBar.gameObject.SetActive(false);
            }

            if (panel != null && !uiBlockPanelWasActive)
            {
                panel.SetActive(false);
            }
        }
    }

    private Vector3 GetRingsVisiblePosition(UIState state)
    {
        if (state == UIState.SetupVoice && setupVoiceOperationInProgress && ringsSetupVoiceAnchor != null)
        {
            return ringsSetupVoiceAnchor.position;
        }

        if (state == UIState.SetupMemory && setupMemoryOperationInProgress && ringsSetupMemoryAnchor != null)
        {
            return ringsSetupMemoryAnchor.position;
        }

        return ringsDefaultPosition;
    }

    private Vector3 GetRingsHiddenPosition()
    {
        Vector3 offset = ringsHiddenOffset;
        if (Camera.main != null)
        {
            offset = Camera.main.transform.right * ringsHiddenOffset.x +
                     Camera.main.transform.up * ringsHiddenOffset.y +
                     Camera.main.transform.forward * ringsHiddenOffset.z;
        }

        return ringsDefaultPosition + offset;
    }

    private IEnumerator RouteAfterVoiceSetup()
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId))
        {
            UpdateDebugText("Nessun avatar attivo dopo setup voce.");
            GoToMainMenu();
            yield break;
        }

        if (servicesConfig == null)
        {
            UpdateDebugText("ServicesConfig mancante dopo setup voce.");
            GoToMainMenu();
            yield break;
        }

        UpdateDebugText("Verifica voce salvata...");
        AvatarVoiceInfo voiceInfo = null;
        string voiceError = null;
        yield return StartCoroutine(FetchJson(
            BuildServiceUrl(servicesConfig.coquiBaseUrl, $"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "Coqui",
            (AvatarVoiceInfo info) => voiceInfo = info,
            error => voiceError = error
        ));

        if (!string.IsNullOrEmpty(voiceError))
        {
            UpdateDebugText($"Errore verifica voce: {voiceError}");
            PlayErrorClip();
        }

        bool voiceConfigured = voiceInfo != null && voiceInfo.exists && voiceInfo.bytes >= minVoiceBytes;
        if (!voiceConfigured)
        {
            UpdateDebugText("Voce non configurata correttamente. Riprova setup voce.");
            PlayErrorClip();
            GoToSetupVoice();
            yield break;
        }

        UpdateDebugText("Verifica memoria...");
        AvatarStatsInfo statsInfo = null;
        string statsError = null;
        yield return StartCoroutine(FetchJson(
            BuildServiceUrl(servicesConfig.ragBaseUrl, $"avatar_stats?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "RAG",
            (AvatarStatsInfo info) => statsInfo = info,
            error => statsError = error
        ));

        if (!string.IsNullOrEmpty(statsError))
        {
            ReportServiceError("RAG", statsError);
            PlayErrorClip();
            GoToMainMenu();
            yield break;
        }

        bool memoryConfigured = statsInfo != null && statsInfo.has_memory;
        if (memoryConfigured)
        {
            UpdateDebugText("Sistema configurato. Vai al menu.");
            GoToMainMenu();
        }
        else
        {
            UpdateDebugText("Setup memoria richiesto.");
            GoToSetupMemory();
        }
    }

    private IEnumerator RouteAfterMemorySetup()
    {
        UpdateDebugText("Setup memoria completato.");
        GoToMainMenu();
        yield break;
    }

    public void GoBack()
    {
        if (uiInputLocked)
        {
            return;
        }

        if (currentState == UIState.MainMenu)
            return;

        if (currentState == UIState.Boot)
        {
            GoToMainMenu();
            return;
        }

        if (currentState == UIState.SetupVoice)
        {
            CancelSetupVoice();
            if (setupVoiceFromMainMode && setupVoiceAlreadyConfigured)
            {
                setupVoiceFromMainMode = false;
                GoToMainMode();
            }
            else
            {
                setupVoiceFromMainMode = false;
                CancelBootRoutine();
                GoBackToPreviousSetupState(skipMainMode: true);
            }
            return;
        }

        if (currentState == UIState.SetupMemory)
        {
            CancelSetupMemory();
            if (setupMemoryFromMainMode && setupMemoryAlreadyConfigured)
            {
                setupMemoryFromMainMode = false;
                GoToMainMode();
            }
            else
            {
                setupMemoryFromMainMode = false;
                CancelBootRoutine();
                GoBackToPreviousSetupState(skipMainMode: true);
            }
            return;
        }

        if (currentState == UIState.MainMode)
        {
            CancelMainMode();
            GoBackFromMainMode();
            return;
        }

        if (backStack.Count == 0)
        {
            GoToMainMenu();
            return;
        }

        var previous = backStack.Pop();
        SetState(previous, false);
    }

    private void GoToState(UIState targetState)
    {
        SetState(targetState, true);
    }

    private void CancelBootRoutine()
    {
        if (bootRoutine != null)
        {
            StopCoroutine(bootRoutine);
            bootRoutine = null;
        }
    }

    private void GoBackToPreviousSetupState(bool skipMainMode)
    {
        while (backStack.Count > 0)
        {
            var candidate = backStack.Pop();
            if (candidate == currentState)
            {
                continue;
            }
            if (skipMainMode && candidate == UIState.MainMode)
            {
                continue;
            }

            if (candidate == UIState.MainMenu)
            {
                GoToMainMenu();
                return;
            }

            SetState(candidate, false);
            return;
        }

        GoToMainMenu();
    }

    private void GoBackFromMainMode()
    {
        while (backStack.Count > 0)
        {
            var candidate = backStack.Pop();
            if (candidate == UIState.SetupVoice || candidate == UIState.SetupMemory || candidate == UIState.MainMode)
            {
                continue;
            }

            if (candidate == UIState.MainMenu)
            {
                GoToMainMenu();
                return;
            }

            if (candidate == UIState.AvatarLibrary)
            {
                SetState(candidate, false);
                return;
            }
        }

        GoToMainMenu();
    }

    private void SetState(UIState targetState, bool pushCurrent)
    {
        SetStateInternal(targetState, pushCurrent, true);
    }

    private void SetStateInternal(UIState targetState, bool pushCurrent, bool allowDefer)
    {
        if (currentState.Equals(targetState))
        {
            return;
        }

        if (allowDefer && (uiBlockActive || uiBlockFadeRoutine != null))
        {
            if (deferredStateRoutine != null)
            {
                StopCoroutine(deferredStateRoutine);
            }
            deferredStateRoutine = StartCoroutine(DeferStateChange(targetState, pushCurrent));
            return;
        }

        if (targetState == UIState.SetupVoice)
        {
            setupVoiceFromMainMode = currentState == UIState.MainMode;
        }
        else if (targetState == UIState.SetupMemory)
        {
            setupMemoryFromMainMode = currentState == UIState.MainMode;
        }

        if (pushCurrent && currentState != UIState.Boot && targetState != UIState.Boot)
        {
            backStack.Push(currentState);
        }

        GameObject fromPanel = GetPanel(currentState);
        GameObject toPanel = GetPanel(targetState);

        currentState = targetState;

        UpdateHintBar(targetState);
        ConfigureNavigatorForState(targetState, true);
        UpdateStateEffects(targetState);
        UpdateDebugTextForState(targetState);
        HandleStateEnter(targetState);

        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
        }

        transitionRoutine = StartCoroutine(TransitionPanels(fromPanel, toPanel));
    }

    private IEnumerator DeferStateChange(UIState targetState, bool pushCurrent)
    {
        if (uiBlockActive)
        {
            yield return StartCoroutine(FadeUiBlocked(false));
        }
        else if (uiBlockFadeRoutine != null)
        {
            yield return uiBlockFadeRoutine;
        }

        deferredStateRoutine = null;
        SetStateInternal(targetState, pushCurrent, false);
    }

    private void SetStateImmediate(UIState targetState)
    {
        currentState = targetState;

        if (targetState == UIState.SetupVoice)
        {
            setupVoiceFromMainMode = false;
        }
        else if (targetState == UIState.SetupMemory)
        {
            setupMemoryFromMainMode = false;
        }

        foreach (var pair in panelMap)
        {
            if (pair.Value == null)
            {
                continue;
            }

            bool active = pair.Key == targetState;
            pair.Value.SetActive(active);

            var canvasGroup = GetOrAddCanvasGroup(pair.Value);
            canvasGroup.alpha = active ? 1f : 0f;
            canvasGroup.interactable = active;
            canvasGroup.blocksRaycasts = active;

            ResetPanelPosition(pair.Value);
        }

        UpdateHintBar(targetState);
        ConfigureNavigatorForState(targetState, true);
        UpdateStateEffects(targetState);
        UpdateDebugTextForState(targetState);
        HandleStateEnter(targetState);
    }

    private void HandleStateEnter(UIState state)
    {
        if (uiBlockActive || uiInputLocked)
        {
            setupMemoryOperationInProgress = false;
            setupVoiceOperationInProgress = false;
            StartCoroutine(FadeUiBlocked(false));
        }

        if (state == UIState.Boot)
        {
            BeginBootSequence();
        }
        if (state == UIState.MainMenu || state == UIState.MainMode || state == UIState.AvatarLibrary)
        {
            RestoreMainMenuCameraPosition();
        }
        if (state == UIState.SetupVoice)
        {
            BeginSetupVoice();
        }
        if (state == UIState.SetupMemory)
        {
            setupMemoryInputFocused = setupMemoryNoteInput != null && setupMemoryNoteInput.isFocused;
            if (setupMemoryLogText != null)
            {
                setupMemoryLogText.text = string.Empty;
            }
            ShowChooseMemoryPanel();
            UpdateHintBar(state);
            if (setupMemoryCheckRoutine != null)
            {
                StopCoroutine(setupMemoryCheckRoutine);
            }
            setupMemoryCheckRoutine = StartCoroutine(EnsureMemorySetupNeededRoutine());
        }
        if (state == UIState.MainMode)
        {
            BeginMainMode();
        }

        UpdateCameraAnchorForState(state);
    }

    private GameObject GetPanel(UIState state)
    {
        return panelMap.TryGetValue(state, out var panel) ? panel : null;
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject panel)
    {
        if (panel == null)
        {
            return null;
        }

        if (panelCanvasGroups.TryGetValue(panel, out var existing))
        {
            return existing;
        }

        var group = panel.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = panel.AddComponent<CanvasGroup>();
        }

        panelCanvasGroups[panel] = group;
        return group;
    }

    private void ResetPanelPosition(GameObject panel)
    {
        if (panel == null)
        {
            return;
        }

        if (!panelDefaultPositions.TryGetValue(panel, out var defaultPosition))
        {
            return;
        }

        var rect = panel.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = defaultPosition;
        }
    }

    private IEnumerator TransitionPanels(GameObject fromPanel, GameObject toPanel)
    {
        if (fromPanel == toPanel)
        {
            yield break;
        }

        if (toPanel == null)
        {
            if (fromPanel != null)
            {
                fromPanel.SetActive(false);
            }
            yield break;
        }

        var toGroup = GetOrAddCanvasGroup(toPanel);
        var fromGroup = fromPanel != null ? GetOrAddCanvasGroup(fromPanel) : null;

        if (fromPanel != null)
        {
            fromGroup.interactable = false;
            fromGroup.blocksRaycasts = false;
        }

        toPanel.SetActive(true);
        toGroup.alpha = 0f;
        toGroup.interactable = false;
        toGroup.blocksRaycasts = false;

        ApplySlideOffset(toPanel, slideOffset);

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);

            if (fromPanel != null)
            {
                fromGroup.alpha = Mathf.Lerp(1f, 0f, t);
                ApplySlideOffset(fromPanel, Mathf.Lerp(0f, -slideOffset, t));
            }

            toGroup.alpha = Mathf.Lerp(0f, 1f, t);
            ApplySlideOffset(toPanel, Mathf.Lerp(slideOffset, 0f, t));

            yield return null;
        }

        if (fromPanel != null)
        {
            fromGroup.alpha = 0f;
            fromPanel.SetActive(false);
            ResetPanelPosition(fromPanel);
        }

        toGroup.alpha = 1f;
        toGroup.interactable = true;
        toGroup.blocksRaycasts = true;
        ResetPanelPosition(toPanel);

        foreach (var pair in panelMap)
        {
            if (pair.Value == null || pair.Value == toPanel)
            {
                continue;
            }

            pair.Value.SetActive(false);
            var otherGroup = GetOrAddCanvasGroup(pair.Value);
            if (otherGroup != null)
            {
                otherGroup.alpha = 0f;
                otherGroup.interactable = false;
                otherGroup.blocksRaycasts = false;
            }
            ResetPanelPosition(pair.Value);
        }
    }

    private void ApplySlideOffset(GameObject panel, float offset)
    {
        if (panel == null)
        {
            return;
        }

        if (!panelDefaultPositions.TryGetValue(panel, out var defaultPosition))
        {
            return;
        }

        var rect = panel.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = defaultPosition + new Vector2(offset, 0f);
        }
    }

    // OnCloseAvatarClick() rimosso - ora la pulizia avatar avviene via GoBack()

    public void OnAvatarReceived(Avaturn.Core.Runtime.Scripts.Avatar.Data.AvatarInfo avatarInfo)
    {
        UpdateDebugText($"Avatar ricevuto: {avatarInfo.AvatarId}");

        if (avatarManager != null)
        {
            NotifyMainAvatarLoadRequested();
            avatarManager.OnAvatarReceived(avatarInfo);
        }
    }

    public void OnAvatarDownloadStarted(Avaturn.Core.Runtime.Scripts.Avatar.Data.AvatarInfo avatarInfo)
    {
        UpdateDebugText("Download avatar in corso...");

        if (pendingNewAvatarDownload && avatarManager != null && !avatarManager.IsPreviewDownloadActive)
        {
            pendingNewAvatarDownload = false;
            EnterDownloadState();
        }
    }

    public void OnAvatarDownloaded(Transform avatarTransform)
    {
        UpdateDebugText("Avatar caricato nella scena!");

        ExitDownloadState();

        // Fix 1: Transizione SOLO se era una richiesta utente (main load)
        if (_pendingMainModeTransition)
        {
            _pendingMainModeTransition = false;
            // Go directly to MainMode to avoid re-running boot checks for already configured avatar
            GoToState(UIState.MainMode);
        }
        else
        {
            // preview completata: NON cambiare pannello
            Debug.Log("[UIFlowController] Download completato senza transizione (preview).");
        }
    }

    public void SetPreviewModeUI(bool active)
    {
        if (active)
        {
            _previewModeActive = true;
            if (hintBar != null) hintBar.gameObject.SetActive(false);
            SetTitleVisible(false);
        }
        else
        {
            // Ripristina SOLO se eravamo in modalità preview (evita di rompere l'intro al boot)
            if (_previewModeActive)
            {
                _previewModeActive = false;
                if (!downloadStateActive && !carouselDownloading)
                {
                    if (hintBar != null) hintBar.gameObject.SetActive(true);
                    SetTitleVisible(true);
                }
            }
        }
    }

    public void SetCarouselDownloading(bool on)
    {
        carouselDownloading = on;
        if (on)
        {
            if (hintBar != null) hintBar.gameObject.SetActive(false);
            SetTitleVisible(false);
        }
        else
        {
            if (!downloadStateActive && !_previewModeActive)
            {
                if (hintBar != null) hintBar.gameObject.SetActive(true);
                SetTitleVisible(true);
            }
        }
    }

    private void SetTitleVisible(bool visible)
    {
        if (_titleCanvasGroup != null)
        {
            _titleCanvasGroup.alpha = visible ? 1f : 0f;
        }
    }

    public void UpdateDebugText(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
        Debug.Log("[UIFlowController] " + message);
    }

    private void UpdateDebugTextForState(UIState state)
    {
        UpdateDebugText($"Stato UI: {state}");
    }

    private void UpdateSetupVoiceStatus(string message)
    {
        if (setupVoiceStatusText != null)
        {
            setupVoiceStatusText.text = message;
        }

        UpdateDebugText(message);
    }

    private void UpdateSetupMemoryLog(string message, bool append = true)
    {
        if (setupMemoryLogText == null)
        {
            UpdateDebugText(message);
            return;
        }

        if (append && !string.IsNullOrEmpty(setupMemoryLogText.text))
        {
            setupMemoryLogText.text += "\n" + message;
        }
        else
        {
            setupMemoryLogText.text = message;
        }
    }

    private void UpdateMainModeStatus(string message)
    {
        if (mainModeStatusText != null)
        {
            mainModeStatusText.text = message;
        }

        UpdateDebugText(message);
    }

    private void BeginBootSequence()
    {
        if (bootRoutine != null)
        {
            StopCoroutine(bootRoutine);
        }
        bootRoutine = StartCoroutine(BootstrapRoutine());
    }

    private IEnumerator BootstrapRoutine()
    {
        UpdateDebugText("INIZIALIZZAZIONE");
        yield return null;

        if (avatarManager == null)
        {
            UpdateDebugText("Nessun AvatarManager trovato. Vai al menu principale.");
            GoToMainMenu();
            yield break;
        }

        string avatarId = avatarManager.CurrentAvatarId;
        if (string.IsNullOrEmpty(avatarId))
        {
            UpdateDebugText("Nessun avatar attivo. Vai al menu principale.");
            GoToMainMenu();
            yield break;
        }

        if (servicesConfig == null)
        {
            UpdateDebugText("ServicesConfig mancante. Vai al menu principale.");
            GoToMainMenu();
            yield break;
        }

        UpdateDebugText("Inizializzazione profilo vocale...");
        AvatarVoiceInfo voiceInfo = null;
        string voiceError = null;
        yield return StartCoroutine(FetchJson(
            BuildServiceUrl(servicesConfig.coquiBaseUrl, $"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "Coqui",
            (AvatarVoiceInfo info) => voiceInfo = info,
            error => voiceError = error
        ));

        if (!string.IsNullOrEmpty(voiceError))
        {
            ReportServiceError("Coqui", voiceError);
            GoToMainMenu();
            yield break;
        }

        bool voiceConfigured = voiceInfo != null && voiceInfo.exists && voiceInfo.bytes >= minVoiceBytes;

        UpdateDebugText("Inizializzazione memoria...");
        AvatarStatsInfo statsInfo = null;
        string statsError = null;
        yield return StartCoroutine(FetchJson(
            BuildServiceUrl(servicesConfig.ragBaseUrl, $"avatar_stats?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "RAG",
            (AvatarStatsInfo info) => statsInfo = info,
            error => statsError = error
        ));

        if (!string.IsNullOrEmpty(statsError))
        {
            ReportServiceError("RAG", statsError);
            GoToMainMenu();
            yield break;
        }

        bool memoryConfigured = statsInfo != null && statsInfo.has_memory;

        if (!voiceConfigured)
        {
            UpdateDebugText("Voice mancante. Setup voce richiesto.");
            GoToSetupVoice();
            yield break;
        }

        if (!memoryConfigured)
        {
            UpdateDebugText("Memoria vuota. Setup memoria richiesto.");
            GoToSetupMemory();
            yield break;
        }

        UpdateDebugText("Sistema pronto.");
        GoToMainMenu();
    }

    private int GetRequestTimeoutSeconds(bool longOperation = false)
    {
        float baseSeconds = servicesConfig != null ? servicesConfig.requestTimeoutSeconds : 10f;
        if (longOperation && longRequestTimeoutSeconds <= 0f)
        {
            return 0;
        }
        float targetSeconds = longOperation ? Mathf.Max(baseSeconds, longRequestTimeoutSeconds) : baseSeconds;
        return Mathf.Max(1, Mathf.CeilToInt(targetSeconds));
    }

    private IEnumerator SetupVoicePhraseRoutine()
    {
        UpdateSetupVoiceStatus("Generazione frase...");

        setupVoicePhrase = null;
        setupVoicePhraseReady = false;
        string ragError = null;

        if (servicesConfig != null && !string.IsNullOrEmpty(servicesConfig.ragBaseUrl))
        {
            string prompt =
                "Genera una frase in italiano di circa 20 parole, includi 2 parole difficili, senza punteggiatura complessa";
            var payload = JsonUtility.ToJson(new RagChatPayload
            {
                avatar_id = "system",
                user_text = prompt,
                top_k = 0
            });

            yield return StartCoroutine(PostJson(
                BuildServiceUrl(servicesConfig.ragBaseUrl, "chat"),
                payload,
                "RAG",
                (RagChatResponse response) => setupVoicePhrase = ExtractRagPhrase(response),
                error => ragError = error
            ));
        }

        if (string.IsNullOrEmpty(setupVoicePhrase))
        {
            setupVoicePhrase = PickFallbackPhrase();
            if (!string.IsNullOrEmpty(ragError))
            {
                UpdateDebugText($"RAG fallback: {ragError}");
            }
        }

        setupVoicePhrase = NormalizeSetupPhrase(setupVoicePhrase, 20);
        if (setupVoicePhraseText != null)
        {
            setupVoicePhraseText.text = setupVoicePhrase;
        }

        setupVoicePhraseReady = true;
        UpdateSetupVoiceStatus("Tieni premuto SPACE per registrare.");
    }

    private IEnumerator IngestFileRoutine()
    {
        try
        {
            string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
            if (string.IsNullOrEmpty(avatarId))
            {
                UpdateSetupMemoryLog("Avatar ID mancante.");
                UpdateDebugText("Ingest file: Avatar ID mancante.");
                PlayErrorClip();
                yield return StartCoroutine(HideRingsAfterOperation());
                yield break;
            }

            if (servicesConfig == null)
            {
                UpdateSetupMemoryLog("ServicesConfig mancante.");
                UpdateDebugText("Ingest file: ServicesConfig mancante.");
                PlayErrorClip();
                yield return StartCoroutine(HideRingsAfterOperation());
                yield break;
            }

            byte[] bytes = null;
            string filename = null;

#if UNITY_WEBGL && !UNITY_EDITOR
            FilePickResult pickResult = default;
            yield return StartCoroutine(MinimalFilePicker.PickFileWebGL("pdf,txt", result => pickResult = result));
            bytes = pickResult.Bytes;
            filename = pickResult.FileName;
#else
            string path = MinimalFilePicker.OpenFilePanel("Seleziona documento", "", "pdf,txt");
            if (!string.IsNullOrEmpty(path))
            {
                bytes = System.IO.File.ReadAllBytes(path);
                filename = System.IO.Path.GetFileName(path);
            }
#endif

            if (bytes == null || bytes.Length == 0)
            {
                UpdateSetupMemoryLog("Selezione documento annullata.");
                UpdateDebugText("Ingest file: selezione annullata.");
                yield return StartCoroutine(HideRingsAfterOperation());
                yield break;
            }

            yield return StartCoroutine(ShowRingsForOperation());
            UpdateSetupMemoryLog("Uploading file...");
            UpdateDebugText($"Ingest file: Upload {filename} ({bytes.Length} bytes)");
            yield return null;
            UpdateSetupMemoryLog("OCR in corso...");
            UpdateDebugText("Ingest file: OCR in corso...");

            var form = new WWWForm();
            form.AddField("avatar_id", avatarId);
            form.AddBinaryData("file", bytes, string.IsNullOrEmpty(filename) ? "upload.bin" : filename);

            using (var request = UnityWebRequest.Post(BuildServiceUrl(servicesConfig.ragBaseUrl, "ingest_file"), form))
            {
                setupMemoryRequest = request;
                request.timeout = GetRequestTimeoutSeconds(longOperation: true);
                yield return request.SendWebRequest();
                setupMemoryRequest = null;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = request.error ?? "Network error";
                    ReportServiceError("RAG", error);
                    UpdateSetupMemoryLog($"Errore ingest: {error}");
                    PlayErrorClip();
                    yield return StartCoroutine(HideRingsAfterOperation());
                    yield break;
                }

                UpdateSetupMemoryLog("Embedding in corso...");
                UpdateDebugText("Ingest file: Embedding in corso...");
                var response = JsonUtility.FromJson<IngestResponse>(request.downloadHandler.text);
                if (response != null && response.ok)
                {
                    UpdateSetupMemoryLog($"Ingest OK: {response.filename} (chunks {response.chunks_added})");
                    UpdateDebugText($"Ingest file: OK - {response.chunks_added} chunks aggiunti");
                    yield return StartCoroutine(HideRingsAfterOperation());
                    yield return new WaitForSeconds(0.5f);
                    GoToMainMode();
                }
                else
                {
                    UpdateSetupMemoryLog("Ingest completato con risposta inattesa.");
                    UpdateDebugText("Ingest file: risposta inattesa.");
                    PlayErrorClip();
                    yield return StartCoroutine(HideRingsAfterOperation());
                }
            }
        }
        finally
        {
            ingestFilePickerActive = false;
        }
    }

    private IEnumerator DescribeImageRoutine()
    {
        try
        {
            string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
            if (string.IsNullOrEmpty(avatarId))
            {
                UpdateSetupMemoryLog("Avatar ID mancante.");
                UpdateDebugText("Describe image: Avatar ID mancante.");
                PlayErrorClip();
                yield return StartCoroutine(HideRingsAfterOperation());
                yield break;
            }

            if (servicesConfig == null)
            {
                UpdateSetupMemoryLog("ServicesConfig mancante.");
                UpdateDebugText("Describe image: ServicesConfig mancante.");
                PlayErrorClip();
                yield return StartCoroutine(HideRingsAfterOperation());
                yield break;
            }

            byte[] bytes = null;
            string filename = null;

#if UNITY_WEBGL && !UNITY_EDITOR
            FilePickResult pickResult = default;
            yield return StartCoroutine(MinimalFilePicker.PickFileWebGL("png,jpg,jpeg", result => pickResult = result));
            bytes = pickResult.Bytes;
            filename = pickResult.FileName;
#else
            string path = MinimalFilePicker.OpenFilePanel("Seleziona immagine", "", "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(path))
            {
                bytes = System.IO.File.ReadAllBytes(path);
                filename = System.IO.Path.GetFileName(path);
            }
#endif

            if (bytes == null || bytes.Length == 0)
            {
                UpdateSetupMemoryLog("Selezione immagine annullata.");
                UpdateDebugText("Describe image: selezione annullata.");
                yield return StartCoroutine(HideRingsAfterOperation());
                yield break;
            }

            yield return StartCoroutine(ShowRingsForOperation());
            UpdateSetupMemoryLog("Descrizione immagine...");
            UpdateDebugText($"Describe image: Upload {filename} ({bytes.Length} bytes)");
            var form = new WWWForm();
            form.AddField("avatar_id", avatarId);
            form.AddField("remember", "1");
            form.AddField("prompt", "Descrivi la scena in modo utile come memoria.");
            form.AddBinaryData("file", bytes, string.IsNullOrEmpty(filename) ? "image.bin" : filename);

            using (var request = UnityWebRequest.Post(BuildServiceUrl(servicesConfig.ragBaseUrl, "describe_image"), form))
            {
                setupMemoryRequest = request;
                request.timeout = GetRequestTimeoutSeconds(longOperation: true);
                yield return request.SendWebRequest();
                setupMemoryRequest = null;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = request.error ?? "Network error";
                    ReportServiceError("RAG", error);
                    UpdateSetupMemoryLog($"Errore describe: {error}");
                    UpdateDebugText($"Describe image: errore - {error}");
                    PlayErrorClip();
                    UpdateSetupMemoryLog("Fallback consigliato: usa 'Scansiona documento' sull'immagine.");
                    yield return StartCoroutine(HideRingsAfterOperation());
                    yield break;
                }

                var response = JsonUtility.FromJson<DescribeImageResponse>(request.downloadHandler.text);
                if (response == null || !response.ok)
                {
                    UpdateSetupMemoryLog("Describe completato con risposta inattesa.");
                    UpdateDebugText("Describe image: risposta inattesa.");
                    PlayErrorClip();
                    yield return StartCoroutine(HideRingsAfterOperation());
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(response.description))
                {
                    UpdateSetupMemoryLog("Descrizione vuota ricevuta dal server.");
                    UpdateDebugText("Describe image: descrizione vuota.");
                    PlayErrorClip();
                    yield return StartCoroutine(HideRingsAfterOperation());
                    yield break;
                }

                UpdateSetupMemoryLog("Descrizione ok. Salvataggio...");
                UpdateDebugText($"Describe image: {response.description.Substring(0, Mathf.Min(50, response.description.Length))}...");
                bool rememberOk = response.saved;
                if (!rememberOk)
                {
                    // Check if backend provided a save error
                    if (!string.IsNullOrEmpty(response.save_error))
                    {
                        UpdateSetupMemoryLog($"Errore salvataggio backend: {response.save_error}");
                    }
                    
                    yield return StartCoroutine(RememberText(response.description, new RememberMeta
                    {
                        source_type = "image_description",
                        filename = response.filename
                    }, ok => rememberOk = ok));
                }

                yield return StartCoroutine(HideRingsAfterOperation());
                if (rememberOk)
                {
                    yield return new WaitForSeconds(0.5f);
                    GoToMainMode();
                }
                else
                {
                    UpdateSetupMemoryLog("Errore salvataggio descrizione. Riprova.");
                    PlayErrorClip();
                }
            }
        }
        finally
        {
            describeFilePickerActive = false;
        }
    }

    private IEnumerator RememberNoteRoutine()
    {
        string text = setupMemoryNoteInput != null ? setupMemoryNoteInput.text : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            UpdateSetupMemoryLog("Nota vuota.");
            UpdateDebugText("Remember note: nota vuota.");
            PlayErrorClip();
            yield break;
        }

        if (servicesConfig == null)
        {
            UpdateSetupMemoryLog("ServicesConfig mancante.");
            UpdateDebugText("Remember note: ServicesConfig mancante.");
            PlayErrorClip();
            yield break;
        }

        UpdateDebugText($"Remember note: {text.Substring(0, Mathf.Min(30, text.Length))}...");
        yield return StartCoroutine(RememberText(text, new RememberMeta { source_type = "manual_note" }));
        if (setupMemoryNoteInput != null)
        {
            setupMemoryNoteInput.text = string.Empty;
        }
    }

    private IEnumerator RememberText(
        string text,
        RememberMeta meta,
        System.Action<bool> onComplete = null,
        bool notifySetupConfirmed = true)
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId))
        {
            UpdateSetupMemoryLog("Avatar ID mancante.");
            UpdateDebugText("Remember text: Avatar ID mancante.");
            PlayErrorClip();
            onComplete?.Invoke(false);
            yield break;
        }

        UpdateSetupMemoryLog("Salvataggio memoria...");
        UpdateDebugText("Remember text: salvataggio in corso...");
        var payload = JsonUtility.ToJson(new RememberPayload
        {
            avatar_id = avatarId,
            text = text,
            meta = meta
        });

        bool ok = false;
        yield return StartCoroutine(PostJson(
            BuildServiceUrl(servicesConfig.ragBaseUrl, "remember"),
            payload,
            "RAG",
            (RememberResponse response) => ok = response != null && response.ok,
            error =>
            {
                UpdateSetupMemoryLog($"Errore remember: {error}");
                UpdateDebugText($"Remember text: errore - {error}");
                PlayErrorClip();
            }
        ));

        if (ok)
        {
            UpdateSetupMemoryLog("Memoria salvata.");
            UpdateDebugText("Remember text: OK - memoria salvata.");
            if (notifySetupConfirmed)
            {
                OnSetupMemoryConfirmed();
            }
        }
        onComplete?.Invoke(ok);
    }

    private void CancelSetupMemory()
    {
        if (setupMemoryRoutine != null)
        {
            StopCoroutine(setupMemoryRoutine);
            setupMemoryRoutine = null;
        }

        if (setupMemoryRequest != null)
        {
            setupMemoryRequest.Abort();
            setupMemoryRequest.Dispose();
            setupMemoryRequest = null;
        }
    }

    private void BeginMainMode()
    {
        mainModeListening = false;
        mainModeProcessing = false;
        ttsAudioSource = GetOrCreateLipSyncAudioSource();
        if (mainModeTranscriptText != null)
        {
            mainModeTranscriptText.text = string.Empty;
        }
        if (mainModeReplyText != null)
        {
            mainModeReplyText.text = string.Empty;
        }
        if (hintBar != null)
        {
            hintBar.SetSpacePressed(false);
        }

        var idleLook = avatarManager != null ? avatarManager.idleLook : null;
        if (idleLook != null)
        {
            idleLook.SetMainModeEnabled(true);
        }

        lastMousePosition = Input.mousePosition;
        lastMouseMoveTime = Time.time;
        UpdateMainModeStatus("Tieni premuto SPACE per parlare.");

        if (mainModeCheckRoutine != null)
        {
            StopCoroutine(mainModeCheckRoutine);
        }
        mainModeCheckRoutine = StartCoroutine(EnsureMainModeRequirementsRoutine());

        if (mainModeEnableRoutine != null)
        {
            StopCoroutine(mainModeEnableRoutine);
        }
        mainModeEnableRoutine = StartCoroutine(EnsureMainModeIdleLookReady());
    }

    private IEnumerator EnsureMainModeIdleLookReady()
    {
        yield return null;
        if (currentState != UIState.MainMode)
        {
            yield break;
        }

        var idleLook = avatarManager != null ? avatarManager.idleLook : null;
        if (idleLook != null)
        {
            idleLook.SetMainModeEnabled(true);
        }
    }

    private IEnumerator EnsureMainModeRequirementsRoutine()
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId) || servicesConfig == null)
        {
            yield break;
        }

        AvatarVoiceInfo voiceInfo = null;
        string voiceError = null;
        yield return StartCoroutine(FetchJson<AvatarVoiceInfo>(
            BuildServiceUrl(servicesConfig.coquiBaseUrl, $"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "Coqui",
            info => voiceInfo = info,
            error => voiceError = error
        ));

        if (string.IsNullOrEmpty(voiceError))
        {
            bool voiceConfigured = voiceInfo != null && voiceInfo.exists && voiceInfo.bytes >= minVoiceBytes;
            if (!voiceConfigured)
            {
                UpdateMainModeStatus("Voce mancante. Setup richiesto.");
                GoToSetupVoice();
                yield break;
            }
        }

        AvatarStatsInfo statsInfo = null;
        string statsError = null;
        yield return StartCoroutine(FetchJson<AvatarStatsInfo>(
            BuildServiceUrl(servicesConfig.ragBaseUrl, $"avatar_stats?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "RAG",
            info => statsInfo = info,
            error => statsError = error
        ));

        if (string.IsNullOrEmpty(statsError) && statsInfo != null && !statsInfo.has_memory)
        {
            UpdateMainModeStatus("Memoria mancante. Setup richiesto.");
            GoToSetupMemory();
        }
    }

    private void HandleMainModeInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            StartMainModeListening();
        }
        if (Input.GetKeyUp(KeyCode.Space))
        {
            StopMainModeListening();
        }
    }

    private void StartMainModeListening()
    {
        if (mainModeListening || mainModeProcessing)
        {
            return;
        }

        if (audioRecorder == null)
        {
            Debug.LogError("[UIFlowController] audioRecorder non assegnato in Inspector.");
            PlayErrorClip();
            return;
        }

        if (!audioRecorder.HasMicrophoneAvailable())
        {
            UpdateMainModeStatus("Microfono non disponibile.");
            PlayErrorClip();
            return;
        }

        mainModeListening = audioRecorder.StartRecording();
        if (!mainModeListening)
        {
            UpdateMainModeStatus("Consenti microfono per continuare.");
            PlayErrorClip();
            return;
        }

        UpdateMainModeStatus("In ascolto... (rilascia SPACE per terminare)");
        if (hintBar != null)
        {
            hintBar.SetSpacePressed(true);
        }
        UpdateHintBar(UIState.MainMode);
        var idleLook = avatarManager != null ? avatarManager.idleLook : null;
        if (idleLook != null)
        {
            idleLook.SetListening(true);
        }
    }

    private void StopMainModeListening()
    {
        if (!mainModeListening || mainModeProcessing)
        {
            return;
        }

        mainModeListening = false;
        if (hintBar != null)
        {
            hintBar.SetSpacePressed(false);
        }
        UpdateHintBar(UIState.MainMode);
        var idleLook = avatarManager != null ? avatarManager.idleLook : null;
        if (idleLook != null)
        {
            idleLook.SetListening(false);
        }

        if (mainModeRoutine != null)
        {
            StopCoroutine(mainModeRoutine);
        }
        mainModeRoutine = StartCoroutine(StopMainModeListeningRoutine());
    }

    private IEnumerator StopMainModeListeningRoutine()
    {
        UpdateMainModeStatus("Elaborazione registrazione...");
        byte[] wavBytes = null;
        yield return StartCoroutine(audioRecorder.StopRecordingAsync(bytes => wavBytes = bytes));
        if (wavBytes == null || wavBytes.Length == 0)
        {
            UpdateMainModeStatus("Non ho capito, riprova.");
            PlayErrorClip();
            yield break;
        }

        mainModeRoutine = StartCoroutine(ProcessMainModePipeline(wavBytes));
    }

    private IEnumerator ProcessMainModePipeline(byte[] wavBytes)
    {
        mainModeProcessing = true;
        UpdateMainModeStatus("Trascrizione in corso...");

        string transcript = null;
        string whisperError = null;
        yield return StartCoroutine(PostWavToWhisper(wavBytes, text => transcript = text, error => whisperError = error));

        if (!string.IsNullOrEmpty(whisperError))
        {
            UpdateMainModeStatus($"Errore Whisper: {whisperError}");
            PlayErrorClip();
            mainModeProcessing = false;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            UpdateMainModeStatus("Non ho capito, riprova.");
            PlayErrorClip();
            mainModeProcessing = false;
            yield break;
        }

        if (mainModeTranscriptText != null)
        {
            mainModeTranscriptText.text = transcript;
        }

        if (servicesConfig == null)
        {
            UpdateMainModeStatus("ServicesConfig mancante.");
            PlayErrorClip();
            mainModeProcessing = false;
            yield break;
        }

        UpdateMainModeStatus("Sto pensando...");
        string reply = null;
        string ragError = null;
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        var payload = JsonUtility.ToJson(new RagChatPayload
        {
            avatar_id = string.IsNullOrEmpty(avatarId) ? "default" : avatarId,
            user_text = transcript,
            top_k = 20
        });

        yield return StartCoroutine(PostJson(
            BuildServiceUrl(servicesConfig.ragBaseUrl, "chat"),
            payload,
            "RAG",
            (RagChatResponse response) => reply = response != null ? response.text : null,
            error => ragError = error
        ));

        if (!string.IsNullOrEmpty(ragError))
        {
            UpdateMainModeStatus($"Errore RAG: {ragError}");
            PlayErrorClip();
            mainModeProcessing = false;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(reply))
        {
            UpdateMainModeStatus("Risposta vuota.");
            PlayErrorClip();
            mainModeProcessing = false;
            yield break;
        }

        if (mainModeReplyText != null)
        {
            mainModeReplyText.text = reply;
        }

        TriggerMainModeWaitPhrase();
        UpdateMainModeStatus("Sto parlando...");
        yield return StartCoroutine(PlayTtsReply(reply));

        UpdateMainModeStatus("Tieni premuto SPACE per parlare.");
        mainModeProcessing = false;
    }

    private void TriggerMainModeWaitPhrase()
    {
        if (waitPhraseRoutine != null)
        {
            StopCoroutine(waitPhraseRoutine);
        }

        waitPhraseRoutine = StartCoroutine(PlayRandomWaitPhraseOnce());
    }

    private IEnumerator PlayRandomWaitPhraseOnce()
    {
        if (servicesConfig == null)
        {
            yield break;
        }

        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId))
        {
            yield break;
        }

        string key = waitPhraseKeys[Random.Range(0, waitPhraseKeys.Length)];
        string cacheKey = $"{avatarId}:{key}";

        if (!waitPhraseCache.TryGetValue(cacheKey, out var clip) || clip == null)
        {
            string url = BuildServiceUrl(
                servicesConfig.coquiBaseUrl,
                $"wait_phrase?avatar_id={UnityWebRequest.EscapeURL(avatarId)}&name={UnityWebRequest.EscapeURL(key)}"
            );

            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                request.timeout = GetRequestTimeoutSeconds(longOperation: true);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    UpdateDebugText($"Wait phrase error: {request.error}");
                    yield break;
                }

                clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip != null)
                {
                    waitPhraseCache[cacheKey] = clip;
                }
            }
        }

        if (clip == null)
        {
            yield break;
        }

        var source = GetOrCreateLipSyncAudioSource();
        if (source == null)
        {
            yield break;
        }

        source.Stop();
        source.loop = false;
        source.clip = clip;
        source.volume = 0.8f;
        source.Play();
    }

    private IEnumerator PlayTtsReply(string text)
    {
        if (servicesConfig == null)
        {
            UpdateMainModeStatus("ServicesConfig mancante.");
            PlayErrorClip();
            yield break;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            if (enableTtsWebGlUnityAudio)
            {
                yield return StartCoroutine(PlayTtsReplyNativeStream(text));
                yield break;
            }

            if (enableTtsWebGlStreaming)
            {
                yield return StartCoroutine(PlayTtsReplyWebGl(text));
                yield break;
            }
        }

        yield return StartCoroutine(PlayTtsReplyNativeStream(text));
    }

    private IEnumerator PlayTtsReplyWebGl(string text)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        string url = BuildServiceUrl(servicesConfig.coquiBaseUrl, "tts_stream");
        string safeAvatarId = string.IsNullOrEmpty(avatarId) ? "default" : avatarId;

        if (enableTtsWebGlStreamingLogs)
        {
            int len = string.IsNullOrEmpty(text) ? 0 : text.Length;
            Debug.Log($"[UIFlowController] WebGL TTS stream start (len={len}, avatar={safeAvatarId}).");
        }

        ttsStreamDone = false;
        ttsStreamActive = true;
        ttsStreamError = null;
        ttsStreamBytes = 0;
        ttsStreamSampleRate = 0;
        ttsStreamChannels = 0;

        int chunkChars = Mathf.Clamp(ttsStreamMaxChunkChars, 40, 400);
        TtsStream_Start(url, text ?? string.Empty, safeAvatarId, "it", gameObject.name, chunkChars);

        while (!ttsStreamDone)
        {
            yield return null;
        }

        ttsStreamActive = false;
        if (!string.IsNullOrEmpty(ttsStreamError))
        {
            UpdateMainModeStatus($"Errore TTS: {ttsStreamError}");
            PlayErrorClip();
            if (enableTtsWebGlStreamingLogs)
            {
                Debug.LogWarning($"[UIFlowController] WebGL TTS stream error: {ttsStreamError}");
            }
            yield break;
        }

        if (enableTtsWebGlStreamingLogs)
        {
            Debug.Log("[UIFlowController] WebGL TTS stream completed.");
        }
#else
        yield break;
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    public void OnTtsStreamCompleted(string stats)
    {
        ttsStreamDone = true;
        ttsStreamError = null;

        if (enableTtsWebGlStreamingLogs && !string.IsNullOrEmpty(stats))
        {
            Debug.Log($"[UIFlowController] WebGL TTS stream stats: {stats}");
        }
    }

    public void OnTtsStreamError(string message)
    {
        ttsStreamDone = true;
        ttsStreamError = string.IsNullOrEmpty(message) ? "Stream error" : message;
        PlayErrorClip();
    }
#endif

    private IEnumerator PlayTtsReplyNativeStream(string text)
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        string url = BuildServiceUrl(servicesConfig.coquiBaseUrl, "tts_stream");
        string safeAvatarId = string.IsNullOrEmpty(avatarId) ? "default" : avatarId;

        ttsStreamError = null;
        ttsStreamBytes = 0;
        ttsStreamSampleRate = 0;
        ttsStreamChannels = 0;

        if (enableTtsWebGlStreamingLogs)
        {
            int len = string.IsNullOrEmpty(text) ? 0 : text.Length;
            Debug.Log($"[UIFlowController] Native TTS stream start (len={len}, avatar={safeAvatarId}).");
        }

        var form = new WWWForm();
        form.AddField("text", text ?? string.Empty);
        form.AddField("avatar_id", safeAvatarId);
        form.AddField("language", "it");
        form.AddField("split_sentences", "true");
        form.AddField("max_chunk_chars", Mathf.Clamp(ttsStreamMaxChunkChars, 40, 400).ToString());

        using (var request = UnityWebRequest.Post(url, form))
        {
            var handler = new PcmStreamDownloadHandler(
                (sampleRate, channels) =>
                {
                    ttsStreamSampleRate = sampleRate;
                    ttsStreamChannels = channels;
                    EnsureTtsStreamPlayer(sampleRate, channels);
                },
                samples =>
                {
                    EnqueueTtsSamples(samples);
                },
                bytes =>
                {
                    ttsStreamBytes += bytes;
                },
                error =>
                {
                    ttsStreamError = error;
                });

            request.downloadHandler = handler;
            mainModeRequest = request;
            if (mainModeRequests != null)
            {
                mainModeRequests.Add(request);
            }
            request.timeout = GetRequestTimeoutSeconds(longOperation: true);
            yield return request.SendWebRequest();
            if (mainModeRequests != null)
            {
                mainModeRequests.Remove(request);
            }
            if (mainModeRequest == request)
            {
                mainModeRequest = null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = request.error ?? "Network error";
                ReportServiceError("Coqui", error);
                ttsStreamError = error;
            }
        }

        EndTtsStreamPlayback();
        while (!IsTtsStreamDrained())
        {
            yield return null;
        }

        if (!string.IsNullOrEmpty(ttsStreamError))
        {
            UpdateMainModeStatus($"Errore TTS: {ttsStreamError}");
            PlayErrorClip();
            if (enableTtsWebGlStreamingLogs)
            {
                Debug.LogWarning($"[UIFlowController] Native TTS stream error: {ttsStreamError}");
            }
            yield break;
        }

        if (enableTtsWebGlStreamingLogs)
        {
            string stats = BuildTtsStreamStats();
            Debug.Log($"[UIFlowController] Native TTS stream completed. {stats}");
        }
    }


    private void CancelMainMode()
    {
        if (mainModeRoutine != null)
        {
            StopCoroutine(mainModeRoutine);
            mainModeRoutine = null;
        }

        if (mainModeEnableRoutine != null)
        {
            StopCoroutine(mainModeEnableRoutine);
            mainModeEnableRoutine = null;
        }

        bool abortedList = false;
        if (mainModeRequests != null && mainModeRequests.Count > 0)
        {
            foreach (var request in mainModeRequests)
            {
                if (request == null)
                {
                    continue;
                }
                request.Abort();
                request.Dispose();
            }
            mainModeRequests.Clear();
            abortedList = true;
        }

        if (abortedList)
        {
            mainModeRequest = null;
        }
        else if (mainModeRequest != null)
        {
            mainModeRequest.Abort();
            mainModeRequest.Dispose();
            mainModeRequest = null;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        if (ttsStreamActive)
        {
            TtsStream_Stop();
            ttsStreamActive = false;
            ttsStreamDone = true;
        }
#endif

        if (audioRecorder != null)
        {
            audioRecorder.StopRecording();
        }

        if (ttsAudioSource != null)
        {
            ttsAudioSource.Stop();
        }

        if (ttsStreamPlayer != null)
        {
            ttsStreamPlayer.StopStream();
        }

        if (ttsChunkPlayer != null)
        {
            ttsChunkPlayer.StopStream();
        }

        mainModeListening = false;
        mainModeProcessing = false;
        if (hintBar != null)
        {
            hintBar.SetSpacePressed(false);
        }
        var idleLook = avatarManager != null ? avatarManager.idleLook : null;
        if (idleLook != null)
        {
            idleLook.SetListening(false);
            idleLook.SetMainModeEnabled(false);
        }
    }

    private void UpdateMainModeMouseLook()
    {
        var idleLook = avatarManager != null ? avatarManager.idleLook : null;
        if (idleLook == null)
        {
            return;
        }

        bool anyKey = Input.anyKeyDown;
        if (anyKey)
        {
            if (!Input.GetKeyDown(KeyCode.Space))
            {
                idleLook.SetExternalLookTarget(null);
                return;
            }
        }

        if (Input.mousePosition != lastMousePosition)
        {
            lastMouseMoveTime = Time.time;
            lastMousePosition = Input.mousePosition;

            var cam = Camera.main;
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                Vector3 target;

                if (idleLook.TryGetHeadWorldPosition(out var headPos))
                {
                    var plane = new Plane(cam.transform.forward, headPos);
                    if (plane.Raycast(ray, out float enter) && enter > 0f)
                    {
                        target = ray.origin + ray.direction * enter;
                    }
                    else
                    {
                        target = ray.origin + ray.direction.normalized * mainModeRayDistance;
                    }

                    if (idleLook.TryGetHeadTransform(out var headTransform))
                    {
                        target += headTransform.TransformVector(mainModeLookHeadOffset);
                    }
                }
                else
                {
                    target = ray.origin + ray.direction.normalized * mainModeRayDistance;
                }
                idleLook.SetExternalLookTarget(target);
            }
        }
        else if (Time.time - lastMouseMoveTime > mainModeMouseIdleSeconds)
        {
            idleLook.SetExternalLookTarget(null);
        }
    }

    private void EnsureTtsStreamPlayer(int sampleRate, int channels)
    {
        if (ttsAudioSource == null)
        {
            ttsAudioSource = GetOrCreateLipSyncAudioSource();
        }

        bool preferChunk = Application.platform == RuntimePlatform.WebGLPlayer;
        ttsUseChunkPlayer = preferChunk;

        if (preferChunk)
        {
            if (ttsChunkPlayer == null)
            {
                ttsChunkPlayer = gameObject.GetComponent<PcmChunkPlayer>();
                if (ttsChunkPlayer == null)
                {
                    ttsChunkPlayer = gameObject.AddComponent<PcmChunkPlayer>();
                }
            }
            ttsChunkPlayer.Begin(ttsAudioSource, sampleRate, channels);
            if (ttsStreamPlayer != null)
            {
                ttsStreamPlayer.StopStream();
            }
        }
        else
        {
            if (ttsStreamPlayer == null)
            {
                ttsStreamPlayer = gameObject.GetComponent<PcmStreamPlayer>();
                if (ttsStreamPlayer == null)
                {
                    ttsStreamPlayer = gameObject.AddComponent<PcmStreamPlayer>();
                }
            }
            ttsStreamPlayer.Begin(ttsAudioSource, sampleRate, channels);
            if (ttsChunkPlayer != null)
            {
                ttsChunkPlayer.StopStream();
            }
        }
    }

    private void EnqueueTtsSamples(float[] samples)
    {
        if (ttsUseChunkPlayer)
        {
            ttsChunkPlayer?.Enqueue(samples);
        }
        else
        {
            ttsStreamPlayer?.Enqueue(samples);
        }
    }

    private void EndTtsStreamPlayback()
    {
        if (ttsUseChunkPlayer)
        {
            ttsChunkPlayer?.EndStream();
            return;
        }
        ttsStreamPlayer?.EndStream();
    }

    private bool IsTtsStreamDrained()
    {
        if (ttsUseChunkPlayer)
        {
            return ttsChunkPlayer == null || ttsChunkPlayer.IsDrained;
        }
        return ttsStreamPlayer == null || ttsStreamPlayer.IsDrained;
    }

    private void EnsureCameraAnchors()
    {
        if (camSetupVoiceLeftAnchor == null)
        {
            Debug.LogError("[UIFlowController] camSetupVoiceLeftAnchor non assegnato in Inspector (Cam_SetupVoice).");
        }

        if (camSetupMemoryRightAnchor == null)
        {
            Debug.LogError("[UIFlowController] camSetupMemoryRightAnchor non assegnato in Inspector (Cam_SetupMemory).");
        }
    }

    private AudioSource GetOrCreateLipSyncAudioSource()
    {
        if (ttsAudioSource != null)
        {
            return ttsAudioSource;
        }

        var lipSyncComponent = UnityEngine.Object
            .FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(component => component.GetType().Name == "uLipSyncAudioSource");
        if (lipSyncComponent != null)
        {
            var source = lipSyncComponent.GetComponent<AudioSource>();
            if (source == null)
            {
                source = lipSyncComponent.gameObject.AddComponent<AudioSource>();
            }
            ttsAudioSource = source;
            return ttsAudioSource;
        }

        ttsAudioSource = GetComponent<AudioSource>();
        if (ttsAudioSource == null)
        {
            ttsAudioSource = gameObject.AddComponent<AudioSource>();
        }
        return ttsAudioSource;
    }

    private void UpdateCameraAnchorForState(UIState state)
    {
        switch (state)
        {
            case UIState.SetupVoice:
                currentCameraAnchor = camSetupVoiceLeftAnchor;
                break;
            case UIState.SetupMemory:
                currentCameraAnchor = camSetupMemoryRightAnchor;
                break;
            default:
                break;
        }
    }

    private void UpdateCameraRig()
    {
        var cam = Camera.main;
        if (cam == null || currentCameraAnchor == null)
        {
            return;
        }

        cam.transform.position = Vector3.SmoothDamp(
            cam.transform.position,
            currentCameraAnchor.position,
            ref cameraVelocity,
            cameraSmoothTime
        );

        cam.transform.rotation = Quaternion.Slerp(
            cam.transform.rotation,
            currentCameraAnchor.rotation,
            Time.deltaTime * cameraRotateSpeed
        );
    }

    private sealed class PcmStreamDownloadHandler : DownloadHandlerScript
    {
        private readonly Action<int, int> onHeader;
        private readonly Action<float[]> onSamples;
        private readonly Action<int> onBytes;
        private readonly Action<string> onError;
        private readonly byte[] header = new byte[44];
        private int headerBytes;
        private bool headerReady;
        private byte[] leftover;

        public PcmStreamDownloadHandler(
            Action<int, int> onHeader,
            Action<float[]> onSamples,
            Action<int> onBytes,
            Action<string> onError)
        {
            this.onHeader = onHeader;
            this.onSamples = onSamples;
            this.onBytes = onBytes;
            this.onError = onError;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0)
            {
                return true;
            }

            int offset = 0;
            if (!headerReady)
            {
                int needed = 44 - headerBytes;
                int take = Math.Min(needed, dataLength);
                Buffer.BlockCopy(data, 0, header, headerBytes, take);
                headerBytes += take;
                offset += take;

                if (headerBytes >= 44)
                {
                    int channels = BitConverter.ToInt16(header, 22);
                    int sampleRate = BitConverter.ToInt32(header, 24);
                    headerReady = true;
                    onHeader?.Invoke(sampleRate, Mathf.Max(1, channels));
                }
            }

            int remaining = dataLength - offset;
            if (remaining <= 0)
            {
                return true;
            }

            byte[] payload = data;
            int payloadOffset = offset;

            if (leftover != null && leftover.Length > 0)
            {
                var combined = new byte[leftover.Length + remaining];
                Buffer.BlockCopy(leftover, 0, combined, 0, leftover.Length);
                Buffer.BlockCopy(data, offset, combined, leftover.Length, remaining);
                payload = combined;
                payloadOffset = 0;
                remaining = combined.Length;
                leftover = null;
            }

            int aligned = remaining - (remaining % 2);
            if (aligned <= 0)
            {
                leftover = payload.Skip(payloadOffset).ToArray();
                return true;
            }

            if (aligned < remaining)
            {
                leftover = payload.Skip(payloadOffset + aligned).ToArray();
            }

            int sampleCount = aligned / 2;
            var samples = new float[sampleCount];
            int idx = 0;
            int byteIndex = payloadOffset;
            for (idx = 0; idx < sampleCount; idx++)
            {
                short sample = BitConverter.ToInt16(payload, byteIndex);
                samples[idx] = sample / 32768f;
                byteIndex += 2;
            }

            onSamples?.Invoke(samples);
            onBytes?.Invoke(aligned);
            return true;
        }

        protected override void CompleteContent()
        {
            if (!headerReady)
            {
                onError?.Invoke("Invalid WAV header");
            }
        }
    }

    private string BuildTtsStreamStats()
    {
        if (ttsStreamSampleRate <= 0 || ttsStreamChannels <= 0 || ttsStreamBytes <= 0)
        {
            return "bytes=0";
        }

        double duration = ttsStreamBytes / (2.0 * ttsStreamChannels * ttsStreamSampleRate);
        return $"bytes={ttsStreamBytes}, duration={duration:0.00}s";
    }

    private sealed class PcmStreamPlayer : MonoBehaviour
    {
        private readonly Queue<float[]> queue = new Queue<float[]>();
        private float[] current;
        private int currentOffset;
        private int channels = 1;
        private int sampleRate = 24000;
        private bool streamEnded;
        private AudioSource source;
        private readonly object locker = new object();

        public bool IsDrained
        {
            get
            {
                lock (locker)
                {
                    bool empty = (current == null || currentOffset >= current.Length) && queue.Count == 0;
                    return streamEnded && empty;
                }
            }
        }

        public void Begin(AudioSource audioSource, int sampleRate, int channels)
        {
            if (audioSource == null)
            {
                return;
            }

            this.source = audioSource;
            this.sampleRate = Mathf.Max(8000, sampleRate);
            this.channels = Mathf.Max(1, channels);
            streamEnded = false;

            lock (locker)
            {
                queue.Clear();
                current = null;
                currentOffset = 0;
            }

            source.Stop();
            source.loop = true;
            source.clip = AudioClip.Create("tts_stream", this.sampleRate, this.channels, this.sampleRate, true, OnRead);
            source.Play();
        }

        public void Enqueue(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return;
            }

            lock (locker)
            {
                queue.Enqueue(samples);
            }
        }

        public void EndStream()
        {
            streamEnded = true;
        }

        public void StopStream()
        {
            streamEnded = true;
            lock (locker)
            {
                queue.Clear();
                current = null;
                currentOffset = 0;
            }
            if (source != null)
            {
                source.Stop();
            }
        }

        private void OnRead(float[] data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                if (current == null || currentOffset >= current.Length)
                {
                    lock (locker)
                    {
                        if (queue.Count > 0)
                        {
                            current = queue.Dequeue();
                            currentOffset = 0;
                        }
                        else
                        {
                            current = null;
                        }
                    }

                    if (current == null)
                    {
                        Array.Clear(data, offset, data.Length - offset);
                        return;
                    }
                }

                int available = current.Length - currentOffset;
                int needed = data.Length - offset;
                int take = Math.Min(available, needed);
                Array.Copy(current, currentOffset, data, offset, take);
                currentOffset += take;
                offset += take;
            }
        }
    }

    private sealed class PcmChunkPlayer : MonoBehaviour
    {
        private readonly Queue<float[]> queue = new Queue<float[]>();
        private AudioSource source;
        private int channels = 1;
        private int sampleRate = 24000;
        private bool streamEnded;
        private Coroutine playRoutine;
        private readonly object locker = new object();

        public bool IsDrained
        {
            get
            {
                lock (locker)
                {
                    return streamEnded && queue.Count == 0 && (source == null || !source.isPlaying);
                }
            }
        }

        public void Begin(AudioSource audioSource, int sampleRate, int channels)
        {
            if (audioSource == null)
            {
                return;
            }

            source = audioSource;
            this.sampleRate = Mathf.Max(8000, sampleRate);
            this.channels = Mathf.Max(1, channels);
            streamEnded = false;

            lock (locker)
            {
                queue.Clear();
            }

            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
            }
            playRoutine = StartCoroutine(PlayQueue());
        }

        public void Enqueue(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return;
            }

            lock (locker)
            {
                queue.Enqueue(samples);
            }
        }

        public void EndStream()
        {
            streamEnded = true;
        }

        public void StopStream()
        {
            streamEnded = true;
            lock (locker)
            {
                queue.Clear();
            }
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }
            if (source != null)
            {
                source.Stop();
            }
        }

        private IEnumerator PlayQueue()
        {
            while (true)
            {
                float[] chunk = null;
                lock (locker)
                {
                    if (queue.Count > 0)
                    {
                        chunk = queue.Dequeue();
                    }
                }

                if (chunk == null)
                {
                    if (streamEnded)
                    {
                        yield break;
                    }
                    yield return null;
                    continue;
                }

                int frames = Mathf.Max(1, chunk.Length / channels);
                var clip = AudioClip.Create("tts_stream_chunk", frames, channels, sampleRate, false);
                clip.SetData(chunk, 0);

                source.Stop();
                source.loop = false;
                source.clip = clip;
                source.Play();

                while (source != null && source.isPlaying)
                {
                    yield return null;
                }
            }
        }
    }

    private void SaveMainMenuCameraPosition()
    {
        var cam = Camera.main;
        if (cam != null && !mainMenuCameraSaved)
        {
            mainMenuCameraPosition = cam.transform.position;
            mainMenuCameraRotation = cam.transform.rotation;
            mainMenuCameraSaved = true;
            Debug.Log("[UIFlowController] Main Menu camera position saved.");
        }
    }

    private void RestoreMainMenuCameraPosition()
    {
        var cam = Camera.main;
        if (cam != null && mainMenuCameraSaved)
        {
            if (cameraReturnRoutine != null)
            {
                StopCoroutine(cameraReturnRoutine);
            }
            cameraReturnRoutine = StartCoroutine(SmoothReturnToMainMenu(cam.transform));
            currentCameraAnchor = null;
            Debug.Log("[UIFlowController] Main Menu camera position restored.");
        }
    }

    private IEnumerator SmoothReturnToMainMenu(Transform camTransform)
    {
        if (camTransform == null)
        {
            yield break;
        }

        Vector3 startPos = camTransform.position;
        Quaternion startRot = camTransform.rotation;
        float duration = Mathf.Max(0.01f, cameraReturnDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            camTransform.position = Vector3.Lerp(startPos, mainMenuCameraPosition, t);
            camTransform.rotation = Quaternion.Slerp(startRot, mainMenuCameraRotation, t);
            yield return null;
        }

        camTransform.position = mainMenuCameraPosition;
        camTransform.rotation = mainMenuCameraRotation;
    }

    private IEnumerator ProcessSetupVoiceRecording()
    {
        if (setupVoiceRecText != null)
        {
            setupVoiceRecText.text = string.Empty;
        }

        UpdateSetupVoiceStatus("Elaborazione registrazione...");
        UpdateDebugText("Setup Voice: stop registrazione...");
        byte[] wavBytes = null;
        yield return StartCoroutine(audioRecorder.StopRecordingAsync(bytes => wavBytes = bytes));

        if (setupVoiceCancelling)
        {
            UpdateDebugText("Setup Voice: registrazione annullata.");
            yield break;
        }

        if (wavBytes == null || wavBytes.Length == 0)
        {
            UpdateSetupVoiceStatus("Registrazione fallita. Riprova.");
            UpdateDebugText("Setup Voice: registrazione fallita.");
            PlayErrorClip();
            yield break;
        }

        if (devMode)
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "setup_voice.wav");
            System.IO.File.WriteAllBytes(path, wavBytes);
            UpdateDebugText($"WAV salvato: {path}");
        }

        UpdateSetupVoiceStatus("Trascrizione...");
        UpdateDebugText($"Setup Voice: trascrizione {wavBytes.Length} bytes...");
        string transcript = null;
        string whisperError = null;
        yield return StartCoroutine(PostWavToWhisper(wavBytes, text => transcript = text, error => whisperError = error));

        if (setupVoiceCancelling)
        {
            UpdateDebugText("Setup Voice: trascrizione annullata.");
            yield break;
        }

        if (!string.IsNullOrEmpty(whisperError))
        {
            UpdateSetupVoiceStatus($"Errore Whisper: {whisperError}");
            UpdateDebugText($"Setup Voice: errore Whisper - {whisperError}");
            PlayErrorClip();
            yield break;
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            UpdateSetupVoiceStatus("Trascrizione vuota. Riprova.");
            UpdateDebugText("Setup Voice: trascrizione vuota.");
            PlayErrorClip();
            yield break;
        }

        float similarity = CalculateSimilarity(setupVoicePhrase, transcript);
        int percent = Mathf.RoundToInt(similarity * 100f);
        UpdateSetupVoiceStatus($"Match {percent}%");
        UpdateDebugText($"Setup Voice: Match {percent}% | Atteso: '{setupVoicePhrase}' | Ricevuto: '{transcript}'");

        if (devMode)
        {
            UpdateDebugText($"Atteso: {setupVoicePhrase} | Ricevuto: {transcript} | Score: {similarity:0.00}");
        }

        if (similarity < setupVoiceMinSimilarity)
        {
            UpdateSetupVoiceStatus($"Match {percent}%: riprova");
            UpdateDebugText($"Setup Voice: similarità insufficiente ({percent}% < {Mathf.RoundToInt(setupVoiceMinSimilarity * 100)}%)");
            PlayErrorClip();
            yield break;
        }

        UpdateSetupVoiceStatus("Salvataggio voce...");
        UpdateDebugText("Setup Voice: upload a Coqui...");
        string coquiError = null;
        bool coquiOk = false;
        yield return StartCoroutine(PostWavToCoqui(wavBytes, ok => coquiOk = ok, error => coquiError = error));

        if (setupVoiceCancelling)
        {
            UpdateDebugText("Setup Voice: upload annullato.");
            yield break;
        }

        if (!coquiOk)
        {
            UpdateSetupVoiceStatus($"Errore Coqui: {coquiError}");
            UpdateDebugText($"Setup Voice: errore Coqui - {coquiError}");
            PlayErrorClip();
            yield break;
        }

        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        string waitError = null;
        bool waitOk = false;
        UpdateSetupVoiceStatus("Generazione frasi attesa...");
        UpdateDebugText("Setup Voice: generazione frasi attesa...");
        yield return StartCoroutine(GenerateWaitPhrasesForAvatar(
            avatarId,
            ok => waitOk = ok,
            error => waitError = error
        ));

        if (!waitOk)
        {
            UpdateSetupVoiceStatus($"Errore frasi attesa: {waitError}");
            UpdateDebugText($"Setup Voice: errore frasi attesa - {waitError}");
        }

        UpdateSetupVoiceStatus("Voce configurata.");
        UpdateDebugText("Setup Voice: OK - voce salvata.");
        OnSetupVoiceConfirmed();
    }

    private IEnumerator GenerateWaitPhrasesForAvatar(
        string avatarId,
        System.Action<bool> onSuccess,
        System.Action<string> onFailure)
    {
        if (servicesConfig == null)
        {
            onFailure?.Invoke("ServicesConfig mancante");
            yield break;
        }

        if (string.IsNullOrEmpty(avatarId))
        {
            onFailure?.Invoke("Avatar ID mancante");
            yield break;
        }

        yield return StartCoroutine(ShowRingsForVoiceOperation());

        bool requestOk = false;
        string requestError = null;
        bool reportError = false;

        var form = new WWWForm();
        form.AddField("avatar_id", avatarId);
        form.AddField("language", "it");

        using (var request = UnityWebRequest.Post(
            BuildServiceUrl(servicesConfig.coquiBaseUrl, "generate_wait_phrases"),
            form))
        {
            setupVoiceRequest = request;
            request.timeout = GetRequestTimeoutSeconds(longOperation: true);
            yield return request.SendWebRequest();
            setupVoiceRequest = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                requestError = request.error ?? "Network error";
                reportError = true;
            }
            else
            {
                try
                {
                    var payload = JsonUtility.FromJson<WaitPhrasesResponse>(request.downloadHandler.text);
                    if (payload != null && payload.ok)
                    {
                        requestOk = true;
                    }
                    else
                    {
                        requestError = "Risposta inattesa";
                    }
                }
                catch (System.Exception ex)
                {
                    requestError = ex.Message;
                }
            }
        }

        if (reportError && !string.IsNullOrEmpty(requestError))
        {
            ReportServiceError("Coqui", requestError);
        }

        if (requestOk)
        {
            onSuccess?.Invoke(true);
        }
        else
        {
            onFailure?.Invoke(requestError ?? "Risposta inattesa");
        }

        yield return StartCoroutine(HideRingsAfterVoiceOperation());

        if (!requestOk)
        {
            yield break;
        }
    }

    private IEnumerator PostWavToWhisper(byte[] wavBytes, System.Action<string> onSuccess, System.Action<string> onFailure)
    {
        if (servicesConfig == null)
        {
            onFailure?.Invoke("ServicesConfig mancante");
            yield break;
        }

        var form = new WWWForm();
        form.AddField("language", "it");
        form.AddBinaryData("file", wavBytes, "recording.wav", "audio/wav");

        using (var request = UnityWebRequest.Post(BuildServiceUrl(servicesConfig.whisperBaseUrl, "transcribe"), form))
        {
            setupVoiceRequest = request;
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(servicesConfig.requestTimeoutSeconds));
            yield return request.SendWebRequest();

            setupVoiceRequest = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = request.error ?? "Network error";
                ReportServiceError("Whisper", error);
                onFailure?.Invoke(error);
                yield break;
            }

            try
            {
                var payload = JsonUtility.FromJson<WhisperResponse>(request.downloadHandler.text);
                onSuccess?.Invoke(payload != null ? payload.text : null);
            }
            catch (System.Exception ex)
            {
                onFailure?.Invoke(ex.Message);
            }
        }
    }

    private IEnumerator PostWavToCoqui(byte[] wavBytes, System.Action<bool> onSuccess, System.Action<string> onFailure)
    {
        if (servicesConfig == null)
        {
            onFailure?.Invoke("ServicesConfig mancante");
            yield break;
        }

        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId))
        {
            onFailure?.Invoke("Avatar ID mancante");
            yield break;
        }

        var form = new WWWForm();
        form.AddField("avatar_id", avatarId);
        form.AddBinaryData("speaker_wav", wavBytes, "reference.wav", "audio/wav");

        using (var request = UnityWebRequest.Post(BuildServiceUrl(servicesConfig.coquiBaseUrl, "set_avatar_voice"), form))
        {
            setupVoiceRequest = request;
            request.timeout = GetRequestTimeoutSeconds(longOperation: true);
            yield return request.SendWebRequest();

            setupVoiceRequest = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = request.error ?? "Network error";
                ReportServiceError("Coqui", error);
                onFailure?.Invoke(error);
                yield break;
            }

            onSuccess?.Invoke(true);
        }
    }

    private IEnumerator PostJson<T>(
        string url,
        string jsonPayload,
        string serviceName,
        System.Action<T> onSuccess,
        System.Action<string> onFailure
    )
    {
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload ?? "{}");
        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = servicesConfig != null
                ? Mathf.Max(1, Mathf.CeilToInt(servicesConfig.requestTimeoutSeconds))
                : 10;

            setupVoiceRequest = request;
            yield return request.SendWebRequest();
            setupVoiceRequest = null;

            UnityWebRequest.Result result;
            try
            {
                result = request.result;
            }
            catch (System.NullReferenceException)
            {
                onFailure?.Invoke($"{serviceName} request cancelled");
                yield break;
            }

            if (result != UnityWebRequest.Result.Success)
            {
                string error = request.error ?? $"{serviceName} network error";
                ReportServiceError(serviceName, error);
                onFailure?.Invoke(error);
                yield break;
            }

            try
            {
                var payload = JsonUtility.FromJson<T>(request.downloadHandler.text);
                onSuccess?.Invoke(payload);
            }
            catch (System.Exception ex)
            {
                onFailure?.Invoke(ex.Message);
            }
        }
    }

    private string ExtractRagPhrase(RagChatResponse response)
    {
        if (response == null)
        {
            return null;
        }

        string content = response.text;
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        content = content.Trim();
        content = Regex.Replace(content, @"[\r\n]+", " ");
        return content;
    }

    private string NormalizeSetupPhrase(string phrase, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return phrase;
        }

        string cleaned = Regex.Replace(phrase, @"[^\p{L}\p{N}\s']", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            return phrase.Trim();
        }

        string[] words = cleaned.Split(' ');
        if (words.Length <= maxWords)
        {
            return cleaned;
        }

        return string.Join(" ", words.Take(maxWords));
    }

    private string PickFallbackPhrase()
    {
        string[] phrases =
        {
            "Nel porto silenzioso il faro guida i viaggiatori mentre le onde raccontano storie antiche di mare e vento",
            "Un gufo vigile osserva la valle montana mentre il cielo si apre e la foresta respira con calma",
            "Tra pietre umide e archi segreti l'acquedotto riflette luci turchesi che danzano lente nella sera",
            "Orchidee rare profumano il giardino nascosto e un drappo cremisi ondeggia sulla rocca al tramonto",
            "La bussola oscilla nella cabina mentre la nave vira e il meridiano illumina la piazza di pietra",
            "Un artista paziente scolpisce marmo lucente e una melodia gentile attraversa il corridoio del palazzo",
            "Il sentiero alpino serpeggia tra abeti e ruscelli chiari mentre la nebbia avvolge il passo remoto",
            "Nel laboratorio quieto una lente precisa rivela dettagli minuti e la memoria si fissa con cura"
        };

        int index = Random.Range(0, phrases.Length);
        return phrases[index];
    }

    private float CalculateSimilarity(string expected, string actual)
    {
        string normExpected = NormalizeForCompare(expected);
        string normActual = NormalizeForCompare(actual);

        if (string.IsNullOrEmpty(normExpected) || string.IsNullOrEmpty(normActual))
        {
            return 0f;
        }

        float seqScore = SequenceSimilarity(normExpected, normActual);
        float wordScore = WordOverlapScore(normExpected, normActual);
        return Mathf.Clamp01((seqScore * 0.6f) + (wordScore * 0.4f));
    }

    private static string NormalizeForCompare(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string lower = text.ToLowerInvariant();
        string normalized = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (char c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        string cleaned = sb.ToString().Normalize(NormalizationForm.FormC);
        cleaned = Regex.Replace(cleaned, @"[^a-z0-9\s]", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static float WordOverlapScore(string expected, string actual)
    {
        var expectedWords = expected.Split(' ').Where(w => !string.IsNullOrWhiteSpace(w)).ToHashSet();
        var actualWords = actual.Split(' ').Where(w => !string.IsNullOrWhiteSpace(w)).ToHashSet();
        if (expectedWords.Count == 0 || actualWords.Count == 0)
        {
            return 0f;
        }

        int intersection = expectedWords.Intersect(actualWords).Count();
        int union = expectedWords.Union(actualWords).Count();
        return union == 0 ? 0f : (float)intersection / union;
    }

    private static float SequenceSimilarity(string a, string b)
    {
        int dist = LevenshteinDistance(a, b);
        int maxLen = Mathf.Max(a.Length, b.Length);
        if (maxLen == 0)
        {
            return 1f;
        }

        return 1f - (float)dist / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int n = a.Length;
        int m = b.Length;
        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Mathf.Min(
                    Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }

        return d[n, m];
    }

    private IEnumerator FetchJson<T>(
        string url,
        string serviceName,
        System.Action<T> onSuccess,
        System.Action<string> onFailure
    )
    {
        int retries = servicesConfig != null ? servicesConfig.retryCount : 0;
        float timeout = servicesConfig != null ? servicesConfig.requestTimeoutSeconds : 10f;

        for (int attempt = 0; attempt <= retries; attempt++)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = Mathf.Max(1, Mathf.CeilToInt(timeout));
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var payload = JsonUtility.FromJson<T>(request.downloadHandler.text);
                        if (payload != null)
                        {
                            onSuccess?.Invoke(payload);
                            yield break;
                        }

                        onFailure?.Invoke("Empty response.");
                    }
                    catch (System.Exception ex)
                    {
                        onFailure?.Invoke(ex.Message);
                    }
                }
                else
                {
                    onFailure?.Invoke(request.error ?? "Network error");
                }
            }

            if (attempt < retries)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }
    }

    private static string BuildServiceUrl(string baseUrl, string pathAndQuery)
    {
        if (string.IsNullOrEmpty(baseUrl))
        {
            return pathAndQuery;
        }

        string trimmed = baseUrl.EndsWith("/") ? baseUrl.Substring(0, baseUrl.Length - 1) : baseUrl;
        if (!pathAndQuery.StartsWith("/"))
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        return trimmed + pathAndQuery;
    }

    private void ReportServiceError(string serviceName, string error)
    {
        string message = $"CORS/Network: controlla allow_origins sul servizio {serviceName}.";
        UpdateDebugText($"{message} ({error})");
    }

    private void ValidateReferencesAtRuntime()
    {
        var missing = new List<string>();

        void Require(UnityEngine.Object value, string label)
        {
            if (!value)
            {
                missing.Add(label);
            }
        }

        Require(pnlMainMenu, "pnlMainMenu (Pnl_MainMenu)");
        Require(pnlAvatarLibrary, "pnlAvatarLibrary (Pnl_AvatarLibrary)");
        Require(pnlSetupVoice, "pnlSetupVoice (Pnl_SetupVoice)");
        Require(pnlSetupMemory, "pnlSetupMemory (Pnl_SetupMemory)");
        Require(pnlMainMode, "pnlMainMode (Pnl_MainMode)");

        Require(btnNewAvatar, "btnNewAvatar (Btn_New)");
        Require(btnShowList, "btnShowList (Btn_LoadList)");
        Require(debugText, "debugText (Txt_Debug)");
        Require(btnMainModeMemory, "btnMainModeMemory (Pnl_MainMode/Btn_Memory)");
        Require(btnMainModeVoice, "btnMainModeVoice (Pnl_MainMode/Btn_Voice)");

        Require(setupVoiceTitleText, "setupVoiceTitleText (Txt_SetupVoiceTitle)");
        Require(setupVoicePhraseText, "setupVoicePhraseText (Txt_SetupVoicePhrase)");
        Require(setupVoiceStatusText, "setupVoiceStatusText (Txt_SetupVoiceStatus)");
        Require(setupVoiceRecText, "setupVoiceRecText (Txt_SetupVoiceRec)");

        Require(setupMemoryTitleText, "setupMemoryTitleText (Txt_SetupMemoryTitle)");
        Require(setupMemoryStatusText, "setupMemoryStatusText (Txt_SetupMemoryStatus)");
        Require(setupMemoryLogText, "setupMemoryLogText (Txt_SetupMemoryLog)");
        Require(setupMemoryNoteInput, "setupMemoryNoteInput (Inp_Note)");
        Require(btnSetupMemorySave, "btnSetupMemorySave (Btn_SaveMemory)");
        Require(btnSetupMemoryIngest, "btnSetupMemoryIngest (Btn_Ingest)");
        Require(btnSetupMemoryDescribe, "btnSetupMemoryDescribe (Btn_Describe)");

        Require(mainModeStatusText, "mainModeStatusText (Txt_MainModeStatus)");
        Require(mainModeTranscriptText, "mainModeTranscriptText (Txt_MainModeTranscript)");
        Require(mainModeReplyText, "mainModeReplyText (Txt_MainModeReply)");

        Require(hintBar, "hintBar (UI_HintBar)");
        Require(navigator, "navigator (UINavigator)");
        Require(avatarManager, "avatarManager (AvatarManager)");
        Require(avaturnSystem, "avaturnSystem (AvaturnSystem)");
        Require(avatarSpawnPoint, "avatarSpawnPoint (AvatarSpawnPoint)");
        Require(servicesConfig, "servicesConfig (SoulframeServicesConfig)");
        Require(ringsTransform, "ringsTransform (VFX_BackgroundRings)");
        Require(ringsController, "ringsController (PS2BackgroundRings)");
        Require(audioRecorder, "audioRecorder (AudioRecorder)");
        Require(webController, "webController (AvaturnWebController)");
        Require(soulframeTitleGroup, "soulframeTitleGroup (Title_SOULFRAME CanvasGroup)");

        if (missing.Count > 0)
        {
            string message = "[UIFlowController] Reference mancanti in Inspector (Scene1):\n- " +
                             string.Join("\n- ", missing);
            Debug.LogError(message);
            if (debugText != null)
            {
                debugText.text = "Missing references:\n" + string.Join("\n", missing);
            }
        }
    }

    private void UpdateHintBar(UIState state)
    {
        if (hintBar == null)
            return;

        // Horizontal arrows in AvatarLibrary/MainMode
        bool useHorizontal = state == UIState.AvatarLibrary || state == UIState.MainMode;
        hintBar.SetArrowsHorizontal(useHorizontal);
        bool showSpacePressed = (state == UIState.MainMode && mainModeListening) ||
                                (state == UIState.SetupVoice && setupVoiceRecording);
        hintBar.SetSpacePressed(showSpacePressed);

        // Se usi hintEntries nell'Inspector, mantieni il vecchio comportamento testuale (eccetto per setup voce/main mode)
        if (hintEntries != null && hintEntries.Count > 0 && hintMap.TryGetValue(state, out var hints) &&
            state != UIState.SetupVoice && state != UIState.MainMode && state != UIState.AvatarLibrary)
        {
            hintBar.SetHints(hints);
            return;
        }

        // Default: PC keyboard prompts
        var arrows = new UIHintBar.HintItem(UIHintBar.HintIcon.Arrows, "Seleziona");
        var enter = new UIHintBar.HintItem(UIHintBar.HintIcon.Enter, "Conferma");

        if (state == UIState.Boot)
        {
            var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Skip");
            hintBar.SetHints(back);
        }
        else if (state == UIState.SetupVoice)
        {
            string label = setupVoiceRecording ? "Lascia per terminare" : "Tieni premuto per parlare";
            var recordSpace = new UIHintBar.HintItem(UIHintBar.HintIcon.Space, label);
            var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
            hintBar.SetHints(recordSpace, back);
        }
        else if (state == UIState.SetupMemory)
        {
            if (setupMemoryInputFocused)
            {
                var enterSave = new UIHintBar.HintItem(UIHintBar.HintIcon.Enter, "Salva nota");
                var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
                hintBar.SetHints(enterSave, back);
            }
            else
            {
                var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
                hintBar.SetHints(arrows, enter, back);
            }
        }
        else if (state == UIState.MainMenu)
        {
            var esc = new UIHintBar.HintItem(UIHintBar.HintIcon.Esc, "Chiudi programma");
            hintBar.SetHints(arrows, enter, esc);
        }
        else if (state == UIState.AvatarLibrary)
        {
            var deleteHint = new UIHintBar.HintItem(UIHintBar.HintIcon.Delete, GetAvatarLibraryDeleteLabel());
            var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
            hintBar.SetHints(arrows, enter, deleteHint, back);
        }
        else if (state == UIState.MainMode)
        {
            string label = mainModeListening ? "Lascia per terminare" : "Tieni premuto per parlare";
            var talk = new UIHintBar.HintItem(UIHintBar.HintIcon.Space, label);
            var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
            hintBar.SetHints(arrows, enter, talk, back);
        }
        else
        {
            var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
            hintBar.SetHints(arrows, enter, back);
        }
    }

    private void UpdateStateEffects(UIState state)
    {
        bool inLibrary = state == UIState.AvatarLibrary;
        if (avatarLibraryCarousel != null)
        {
            avatarLibraryCarousel.ShowLibrary(inLibrary);
        }

        if (avatarManager != null)
        {
            avatarManager.SetCurrentAvatarVisible(!inLibrary);
        }

        UpdateRingsForState(state);
        if (ringsController != null)
        {
            if (state == UIState.Boot)
            {
                ringsController.SetOrbitSpeedMultiplier(bootRingsSpeedMultiplier);
            }
            else if (!downloadStateActive)
            {
                ringsController.SetOrbitSpeedMultiplier(1f);
            }
        }
    }

    private void ConfigureNavigatorForState(UIState state, bool resetIndex)
    {
        if (navigator == null)
        {
            return;
        }

        var selectables = new List<Selectable>();
        var axisMode = UINavigator.AxisMode.Vertical;

        switch (state)
        {
            case UIState.Boot:
                axisMode = UINavigator.AxisMode.Vertical;
                break;
            case UIState.MainMenu:
                // Fix 1 parte 4: Non includere i bottoni nel navigator finché l'intro non è completata
                if (!enableMainMenuIntro || _mainMenuButtonsIntroDone)
                {
                    if (btnNewAvatar != null) selectables.Add(btnNewAvatar);
                    if (btnShowList != null) selectables.Add(btnShowList);
                }
                axisMode = UINavigator.AxisMode.Vertical;
                break;
            case UIState.AvatarLibrary:
                // Carosello 3D: nessun selectable (gestito internamente da AvatarLibraryCarousel)
                axisMode = UINavigator.AxisMode.Horizontal;
                break;
            case UIState.SetupVoice:
            case UIState.SetupMemory:
                axisMode = UINavigator.AxisMode.Vertical;
                if (state == UIState.SetupMemory && !setupMemoryInputFocused)
                {
                    if (pnlChooseMemory != null && pnlChooseMemory.activeSelf)
                    {
                        if (btnSetupMemoryIngest != null) selectables.Add(btnSetupMemoryIngest);
                        if (btnSetupMemoryDescribe != null) selectables.Add(btnSetupMemoryDescribe);
                        if (btnSetupMemorySave != null) selectables.Add(btnSetupMemorySave);
                    }
                    else if (pnlSaveMemory != null && pnlSaveMemory.activeSelf)
                    {
                        if (btnSetMemory != null) selectables.Add(btnSetMemory);
                    }
                }
                break;
            case UIState.MainMode:
                axisMode = UINavigator.AxisMode.Horizontal;
                if (btnMainModeMemory != null) selectables.Add(btnMainModeMemory);
                if (btnMainModeVoice != null) selectables.Add(btnMainModeVoice);
                break;
        }

        navigator.SetExitAllowed(state == UIState.MainMenu);
        navigator.SetMenu(selectables, axisMode, resetIndex);
    }

    private void UpdateRingsForState(UIState state)
    {
        if (ringsTransform == null)
        {
            return;
        }

        bool showRings = state == UIState.MainMenu || state == UIState.Boot ||
                         (state == UIState.SetupMemory && setupMemoryOperationInProgress) ||
                         (state == UIState.SetupVoice && setupVoiceOperationInProgress);
        bool hideRings = !showRings;
        Vector3 target = hideRings ? GetRingsHiddenPosition() : GetRingsVisiblePosition(state);

        if (ringsRoutine != null)
        {
            StopCoroutine(ringsRoutine);
        }

        ringsRoutine = StartCoroutine(AnimateRings(target));
    }

    private void SetRingsVisible(bool visible)
    {
        if (ringsTransform == null)
        {
            return;
        }

        Vector3 target = visible ? GetRingsVisiblePosition(currentState) : GetRingsHiddenPosition();

        if (ringsRoutine != null)
        {
            StopCoroutine(ringsRoutine);
        }

        ringsRoutine = StartCoroutine(AnimateRings(target));
    }

    private void EnterDownloadState()
    {
        if (downloadStateActive)
        {
            return;
        }

        downloadStateActive = true;
        if (mainMenuCanvasGroup != null)
        {
            // FIX: Hide completely as requested
            mainMenuCanvasGroup.alpha = 0f; 
            mainMenuCanvasGroup.interactable = false;
            mainMenuCanvasGroup.blocksRaycasts = false;
        }
        
        // Fix: Hide Title and HintBar explicitly
        if (_titleCanvasGroup != null) _titleCanvasGroup.alpha = 0f;
        if (hintBar != null) hintBar.gameObject.SetActive(false);

        SetMainMenuButtonsVisible(false, immediate: true);

        if (ringsController != null)
        {
            ringsController.SetOrbitSpeedMultiplier(downloadRingsSpeedMultiplier);
        }
    }

    private void ExitDownloadState()
    {
        if (!downloadStateActive)
        {
            return;
        }

        downloadStateActive = false;
        if (mainMenuCanvasGroup != null)
        {
            mainMenuCanvasGroup.alpha = mainMenuBaseAlpha;
            mainMenuCanvasGroup.interactable = true;
            mainMenuCanvasGroup.blocksRaycasts = true;
        }
        
        // Fix: Restore Title and HintBar
        if (!carouselDownloading && !_previewModeActive)
        {
            SetTitleVisible(true);
            if (hintBar != null) hintBar.gameObject.SetActive(true);
        }

        if (!enableMainMenuIntro || _mainMenuButtonsIntroDone)
        {
            SetMainMenuButtonsVisible(true, immediate: true);
        }

        if (ringsController != null)
        {
            ringsController.SetOrbitSpeedMultiplier(1f);
        }
    }

    private IEnumerator AnimateRings(Vector3 targetPosition)
    {
        if (ringsTransform == null)
        {
            yield break;
        }

        Vector3 start = ringsTransform.position;
        float elapsed = 0f;

        while (elapsed < ringsTransitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = ringsTransitionDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / ringsTransitionDuration);
            ringsTransform.position = Vector3.Lerp(start, targetPosition, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        ringsTransform.position = targetPosition;
    }

    private void ExitApplication()
    {
        if (currentState != UIState.MainMenu)
        {
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }



    [System.Serializable]
    public struct HintEntry
    {
        public UIState state;
        [TextArea] public string hints;
    }

    public void OnAvatarJsonReceived(string json)
    {
        Debug.Log("JSON ricevuto dal bridge: " + json);

        try
        {
            var jsonData = JsonUtility.FromJson<AvatarJsonData>(json);
            if (jsonData != null && (jsonData.status == "closed" || jsonData.status == "error"))
            {
                SetWebOverlayOpen(false);
            }

            if (avatarManager != null)
            {
                avatarManager.OnAvatarJsonReceived(json);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Errore nel parsing JSON: " + e.Message);
            UpdateDebugText("Errore nel ricevere l'avatar: " + e.Message);
        }
    }

    public void OnWebOverlayOpened()
    {
        SetWebOverlayOpen(true);
    }

    public void OnWebOverlayClosed()
    {
        SetWebOverlayOpen(false);
    }

    private void SetWebOverlayOpen(bool isOpen)
    {
        webOverlayOpen = isOpen;
    }

    [System.Serializable]
    private class AvatarJsonData
    {
        public string url;
        public string urlType;
        public string bodyId;
        public string gender;
        public string avatarId;
        public string status;
    }

    [System.Serializable]
    private class WhisperResponse
    {
        public string text;
    }

    [System.Serializable]
    private class RagChatPayload
    {
        public string avatar_id;
        public string user_text;
        public int top_k;
    }

    [System.Serializable]
    private class RagChatResponse
    {
        public string text;
    }

    [System.Serializable]
    private class IngestResponse
    {
        public bool ok;
        public string filename;
        public int chunks_added;
    }

    [System.Serializable]
    private class DescribeImageResponse
    {
        public bool ok;
        public string filename;
        public string description;
        public bool saved;
        public string save_error;
    }

    [System.Serializable]
    private class RememberPayload
    {
        public string avatar_id;
        public string text;
        public RememberMeta meta;
    }

    [System.Serializable]
    private class RememberMeta
    {
        public string source_type;
        public string filename;
    }

    [System.Serializable]
    private class RememberResponse
    {
        public bool ok;
        public string id;
    }

    [System.Serializable]
    private class ClearAvatarResponse
    {
        public bool ok;
    }

    [System.Serializable]
    private class AvatarVoiceInfo
    {
        public string avatar_id;
        public bool exists;
        public int bytes;
    }

    [System.Serializable]
    private class AvatarStatsInfo
    {
        public string avatar_id;
        public int count;
        public bool has_memory;
    }

    [System.Serializable]
    private class WaitPhrasesResponse
    {
        public bool ok;
        public string avatar_id;
        public int count;
        public string[] files;
    }
}
