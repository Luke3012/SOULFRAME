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
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Random = UnityEngine.Random;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
public class UIFlowController : MonoBehaviour
{
    private enum MainModeReplyTimingResolutionState
    {
        Pending,
        Partial,
        Available,
        Unavailable
    }

    private enum TouchMainModeTextView
    {
        Transcript,
        Reply
    }

    private enum TtsWebGlStreamProfile
    {
        Balanced,
        Smooth,
        MaxStability
    }

    public enum EmpiricalAvatarCarouselGroup
    {
        General,
        Personal
    }

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
    public GameObject pnlLoading;

    [Header("Services Config")]
    [SerializeField] private SoulframeServicesConfig servicesConfig;
    [SerializeField] private bool resetAvatarLogsOnDelete = false;

    [Header("Main Menu Panel")]
    public Button btnNewAvatar;
    public Button btnShowList;
    public TextMeshProUGUI debugText;
    
    [Header("Main Menu Intro")]
    [SerializeField] private UIIntroSequence uiIntroSequence;
    private CanvasGroup _titleCanvasGroup;
    private CanvasGroup _btnNewAvatarGroup;
    private CanvasGroup _btnShowListGroup;

    [Header("Setup Voice Panel")]
    public TextMeshProUGUI setupVoiceTitleText;
    public TextMeshProUGUI setupVoicePhraseText;
    public TextMeshProUGUI setupVoiceStatusText;
    public TextMeshProUGUI setupVoiceRecText;

    [Header("Setup Memory Panel")]
    public TextMeshProUGUI setupMemoryTitleText;
    public TextMeshProUGUI setupMemoryStatusText;
    public TextMeshProUGUI setupMemoryLogText;
    public TextMeshProUGUI saveMemoryTitleText;
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
    [SerializeField] private Image mainModeReplyBackgroundImage;
    [SerializeField] private Image touchMainModeConversationBackgroundImage;
    [SerializeField] private Button btnMainModeMemory;
    [SerializeField] private Button btnMainModeVoice;
    [SerializeField] private TMP_InputField chatNoteInput;
    [SerializeField, Min(0f)] private float mainModeReplyBackgroundTransitionDuration = 0.18f;
    [SerializeField, Min(0f)] private float chatNoteTransitionDuration = 0.08f;

    [Header("Touch UI")]
    [SerializeField] private bool enableTouchUi = true;
    [SerializeField] private bool forceTouchUi;
    [SerializeField] private bool enableTouchUiOnTouchscreenDesktop;
    [SerializeField] private CanvasScaler touchCanvasScaler;
    [SerializeField, Min(0.5f)] private float touchUiScaleMultiplier = 1f;
    [SerializeField] private GameObject pnlTouchOverlay;
    [SerializeField] private GameObject pnlTouchMainMode;
    [SerializeField] private GameObject pnlTouchSetupVoice;
    [SerializeField] private GameObject pnlTouchSetupMemory;
    [SerializeField] private GameObject pnlTouchAvatarLibrary;
    [SerializeField] private GameObject pnlTouchChooseMemory;
    [SerializeField] private GameObject pnlTouchSaveMemory;
    [SerializeField] private GameObject touchHintBarObject;
    [SerializeField] private Transform camTouchSetupVoiceAnchor;
    [SerializeField] private Transform camTouchSetupMemoryAnchor;
    [SerializeField] private Transform ringsTouchSetupVoiceAnchor;
    [SerializeField] private Transform ringsTouchSetupMemoryAnchor;
    [SerializeField] private TextMeshProUGUI touchDebugText;
    [SerializeField] private TextMeshProUGUI touchSetupVoiceTitleText;
    [SerializeField] private TextMeshProUGUI touchSetupVoicePhraseText;
    [SerializeField] private TextMeshProUGUI touchSetupVoiceStatusText;
    [SerializeField] private TextMeshProUGUI touchSetupVoiceRecText;
    [SerializeField] private TextMeshProUGUI touchSetupMemoryTitleText;
    [SerializeField] private TextMeshProUGUI touchSetupMemoryStatusText;
    [SerializeField] private TextMeshProUGUI touchSetupMemoryLogText;
    [SerializeField] private TextMeshProUGUI touchSaveMemoryTitleText;
    [SerializeField] private TMP_InputField touchSetupMemoryNoteInput;
    [SerializeField] private Button btnTouchSetupMemorySave;
    [SerializeField] private Button btnTouchSetupMemoryIngest;
    [SerializeField] private Button btnTouchSetupMemoryDescribe;
    [SerializeField] private TextMeshProUGUI touchMainModeStatusText;
    [SerializeField] private TextMeshProUGUI touchMainModeTranscriptText;
    [SerializeField] private TextMeshProUGUI touchMainModeReplyText;
    [SerializeField] private Button btnTouchMainModeMemory;
    [SerializeField] private Button btnTouchMainModeVoice;
    [SerializeField] private TMP_InputField touchMainModeChatNoteInput;
    [SerializeField] private Button btnTouchBackMainMode;
    [SerializeField] private Button btnTouchBackSetupVoice;
    [SerializeField] private Button btnTouchBackSetupMemory;
    [SerializeField] private Button btnTouchBackAvatarLibrary;
    [SerializeField] private Button btnTouchPttMainMode;
    [SerializeField] private Button btnTouchPttSetupVoice;
    [SerializeField] private Button btnTouchKeyboardMainMode;
    [SerializeField] private Button btnTouchConfirmMainMode;
    [SerializeField] private Button btnTouchCancelMainMode;
    [SerializeField] private Button btnTouchConfirmSetMemory;
    [SerializeField] private Button btnTouchCancelSetMemory;
    [SerializeField] private Button btnTouchDeleteRestoreAvatarLibrary;
    [SerializeField] private string touchPttMainModeIdleLabel = "Push to Talk";
    [SerializeField] private string touchPttMainModeActiveLabel = "Rilascia";
    [SerializeField] private string touchPttSetupVoiceIdleLabel = "Registra";
    [SerializeField] private string touchPttSetupVoiceActiveLabel = "Stop";
    [SerializeField, Min(0f)] private float touchMainModeTextSwitchDuration = 0.18f;
    [SerializeField, Min(0f)] private float touchMainModeTextSwitchOffset = 110f;
    [SerializeField, Min(10f)] private float touchMainModeSwipeMinDistance = 70f;
    [SerializeField, Range(0.2f, 1f)] private float touchChatAvatarDimMultiplier = 0.55f;
    [SerializeField] private bool enableTouchHintBarDebugTapToggle = true;
    [SerializeField, Min(2)] private int touchHintBarDebugTapCount = 3;
    [SerializeField] private bool applyTouchMainMenuButtonLayout = true;
    [SerializeField, Min(120f)] private float touchMainMenuButtonWidth = 360f;
    [SerializeField, Min(60f)] private float touchMainMenuButtonHeight = 104f;
    [SerializeField, Min(20f)] private float touchMainMenuButtonsVerticalSpacing = 128f;
    [SerializeField] private float touchMainMenuButtonsCenterY = -205f;

    private const float TouchHintBarDebugTapMaxInterval = 0.45f;
    private const float TouchHintBarDebugTapMaxMove = 35f;
    private const float CoquiBootProbeTimeoutSeconds = 3f;
    private const float CoquiBootPollIntervalSeconds = 0.75f;
    private const float CoquiBootMaxWaitSeconds = 180f;
    private const float RagBootProbeTimeoutSeconds = 3f;
    private const float RagBootPollIntervalSeconds = 0.75f;
    private const float RagBootMaxWaitSeconds = 180f;
    private const float CoquiBootRingsSlowFactor = 0.08f;
    private const float MinWhisperInputDurationSeconds = 0.55f;

    [Header("Main Avatar Spawn Animation")]
    [SerializeField] private bool enableMainAvatarSpawnAnimation = true;
    [SerializeField, Min(0f)] private float mainAvatarSpawnFadeDuration = 0.45f;
    [SerializeField, Range(0.6f, 1f)] private float mainAvatarSpawnStartScale = 0.92f;
    [SerializeField] private AnimationCurve mainAvatarSpawnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Avatar Library 3D")]
    public AvatarLibraryCarousel avatarLibraryCarousel;

    [Header("Hint Bar")]
    public UIHintBar hintBar;
    public List<HintEntry> hintEntries = new List<HintEntry>();

    [Header("Debug UI")]
    [SerializeField] private bool debugUiHiddenAtStart = false;

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
    [SerializeField] private PS2PostProcessingBootstrap postProcessingBootstrap;

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
    [SerializeField, Min(5)] private int setupVoiceTargetWords = 32;
    [SerializeField, Min(0)] private int setupVoiceWordSlack = 10;
    [SerializeField, Min(0)] private int setupVoiceMinCharsOverride = 0;

    [Header("Empirical Test Mode")]
    [SerializeField] private TMP_Text empiricalTestModeBadgeText;
    [SerializeField, Min(0.05f)] private float empiricalTestModeBadgeAnimationDuration = 0.45f;
    [SerializeField] private Vector2 empiricalTestModeBadgeAnimatedOffset = new Vector2(22f, -12f);
    [SerializeField, Range(0.6f, 1.4f)] private float empiricalTestModeBadgeStartScale = 0.9f;

    [Header("Web Overlay")]
    [SerializeField] private AvaturnWebController webController;

    [Header("Main Mode")]
    [SerializeField] private float mainModeMouseIdleSeconds = 4f;
    [SerializeField] private float mainModeRayDistance = 2.5f;
    [SerializeField] private Vector3 mainModeLookHeadOffset = new Vector3(0f, 0.06f, 0.05f);
    [SerializeField, Min(0f)] private float mainModeHintRefreshDelay = 0.05f;
    [SerializeField] private AudioSource ttsAudioSource;
    [SerializeField] private Transform camSetupVoiceLeftAnchor;
    [SerializeField] private Transform camSetupMemoryRightAnchor;
    [SerializeField] private float cameraSmoothTime = 0.25f;
    [SerializeField] private float cameraRotateSpeed = 6f;
    [SerializeField] private float cameraReturnDuration = 0.35f;

    [Header("Main Mode Reply Flow")]
    [SerializeField] private bool enableMainModeReplyWordFlow = true;
    [SerializeField, Min(0.02f)] private float replyWordFlowUiUpdateIntervalSeconds = 0.12f;
    [SerializeField, Min(0.05f)] private float backendTimingPollMinSeconds = 0.14f;
    [SerializeField, Min(0.10f)] private float backendTimingPollMaxSeconds = 0.50f;
    [SerializeField, Min(100)] private int backendTimingTargetLeadMs = 750;
    [SerializeField, Min(1)] private int backendTimingPollMaxRetries = 3;
    [SerializeField, Min(1f)] private float backendTimingPollTimeoutSeconds = 5f;
    [SerializeField, Min(0f)] private float backendTimingInitialDelaySeconds = 0.12f;
    [SerializeField, Min(4)] private int replyDesktopWindowWords = 22;
    [SerializeField, Min(4)] private int replyTouchWindowWords = 14;
    [SerializeField, Min(0)] private int replyLookAheadWords = 6;
    [SerializeField, Min(0.1f)] private float replyCaretBlinkSeconds = 0.78f;
    [SerializeField] private bool replyShowTrailingCaret = true;
    [SerializeField] private Color replyPastWordColor = new Color(0.62f, 0.72f, 0.82f, 1f);
    [SerializeField] private Color replyCurrentWordColor = new Color(0.95f, 0.99f, 1f, 1f);
    [SerializeField] private Color replyFutureWordColor = new Color(0.76f, 0.86f, 0.94f, 1f);

    [Header("TTS")]
    [SerializeField] private bool enableTtsWebGlStreamingLogs = false;
    [SerializeField, Range(40, 400)] private int ttsReplySegmentMaxChars = 200;
    [SerializeField, Min(0f)] private float ttsWaitPhraseDelaySeconds = 3f;
    [SerializeField] private TtsWebGlStreamProfile ttsWebGlStreamProfile = TtsWebGlStreamProfile.Balanced;
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
    private bool coquiInitializationRingsVisualActive;
    private Coroutine setupVoiceRoutine;
    private UnityWebRequest setupVoiceRequest;
    private string setupVoicePhrase;
    private bool setupVoiceRecording;
    private bool setupVoiceCancelling;
    private bool setupVoicePhraseReady;
    private bool setupVoiceAlreadyConfigured;
    private bool setupVoiceOperationInProgress;
    private Coroutine setupMemoryRoutine;
    private int setupMemoryRoutineToken;
    private Coroutine setupMemoryCheckRoutine;
    private UnityWebRequest setupMemoryRequest;
    private bool setupMemoryInputFocused;
    private bool setupMemoryNoteJustDismissed;
    private bool setupMemoryOperationInProgress;
    private bool setupMemoryAlreadyConfigured;
    private string setupMemoryLastErrorDetail;
    private Coroutine memoryPanelTransitionRoutine;
    private Coroutine mainModeRoutine;
    private Coroutine mainModeCheckRoutine;
    private Coroutine mainModeEnableRoutine;
    private Coroutine mainModeHintRefreshRoutine;
    private Coroutine mainModeSessionStartRoutine;
    private UnityWebRequest mainModeRequest;
    private readonly List<UnityWebRequest> mainModeRequests = new List<UnityWebRequest>();
    private string ttsStreamError;
    private PcmChunkPlayer ttsChunkPlayer;
    private int ttsPlaybackSessionId;
    private int ttsActiveSessionId = -1;
    private bool ttsAcceptIncomingSamples;
    private long ttsStreamBytes;
    private int ttsStreamSampleRate;
    private int ttsStreamChannels;
    private bool mainModeListening;
    private bool mainModeProcessing;
    private bool mainModeSpeaking;
    private bool mainModeTtsInterruptedByUser;
    private Coroutine mainModeReplyFlowRoutine;
    private Coroutine mainModeReplyTimingPollRoutine;
    private string mainModeReplyFullText;
    private string mainModeReplyTtsRequestId;
    private readonly List<ReplyTokenInfo> mainModeReplyTokens = new List<ReplyTokenInfo>();
    private readonly List<ReplyWordSpan> mainModeReplyWords = new List<ReplyWordSpan>();
    private readonly List<int> mainModeReplyWordEndMs = new List<int>();
    private readonly List<int> mainModeReplySegmentEndMs = new List<int>();
    private readonly List<int> mainModeReplySegmentEndWordIndices = new List<int>();
    private int mainModeReplyLastResolvedWordIndex = -1;
    private MainModeReplyTimingResolutionState mainModeReplyTimingState = MainModeReplyTimingResolutionState.Pending;
    private bool mainModeReplyTimingComplete;
    private int mainModeReplyTimingRetryCount;
    private bool mainModeReplyUseStaticFallback;
    private bool mainModeReplyLoggedFirstStreamByte;
    private bool mainModeReplyLoggedFirstChunk;
    private bool mainModeReplyLoggedStableClock;
    private bool mainModeReplyLoggedFirstReveal;
    private bool mainModeReplySpeechStarted;
    private float mainModeReplyBackgroundBaseAlpha = -1f;
    private float touchMainModeConversationBackgroundBaseAlpha = -1f;
    private Coroutine mainModeReplyBackgroundFadeRoutine;
    private Coroutine touchMainModeConversationBackgroundFadeRoutine;
    private string mainModeConversationSessionId;
    private string mainModeConversationAvatarId;
    private bool mainModeSessionStartInFlight;
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
    // Teniamo questo flag per sapere se la transizione a MainMode arriva da richiesta utente.
    private bool _pendingMainModeTransition = false;
    private bool _previewModeActive = false;
    private bool setupVoiceFromMainMode = false;
    private bool setupMemoryFromMainMode = false;
    private bool setupRedirectFromMainModeRequirements = false;
    private bool setupMemoryBackspaceArmed = false;
    private bool setupMemoryNoteWasEmpty = false;
    private bool ingestFilePickerActive = false;
    private bool describeFilePickerActive = false;
    private bool mainModeChatNoteActive;
    private Coroutine chatNoteTransitionRoutine;
    private CanvasGroup chatNoteCanvasGroup;
    private bool chatNoteJustDismissed;
    private bool debugUiHidden;
    private bool touchUiActive;
    private UIHintBar touchHintBarComponent;
    private TextMeshProUGUI touchPttMainModeText;
    private TextMeshProUGUI touchPttSetupVoiceText;
    private TextMeshProUGUI touchDeleteRestoreText;
    private bool touchCanvasScalerInitialized;
    private Vector2 touchCanvasBaseReferenceResolution;
    private float touchCanvasBaseScaleFactor;
    private TouchMainModeTextView touchMainModeTextView = TouchMainModeTextView.Transcript;
    private bool touchMainModeReplyAvailable;
    private bool touchMainModeReplyShownOnce;
    private Coroutine touchMainModeTextSwitchRoutine;
    private CanvasGroup touchMainModeTranscriptGroup;
    private CanvasGroup touchMainModeReplyGroup;
    private Vector2 touchMainModeTranscriptDefaultPos;
    private Vector2 touchMainModeReplyDefaultPos;
    private bool touchMainModeTextLayoutCached;
    private bool touchMainModeSwipeTracking;
    private int touchMainModeSwipeFingerId = -1;
    private Vector2 touchMainModeSwipeStart;
    private int touchHintBarTapCounter;
    private float touchHintBarLastTapTime;
    private int touchHintBarTapFingerId = -1;
    private Vector2 touchHintBarTapStart;
    private bool empiricalTestModeEnabled;
    private bool fullTestModeEnabled;
    private CanvasGroup empiricalTestModeBadgeCanvasGroup;
    private RectTransform empiricalTestModeBadgeRect;
    private Vector2 empiricalTestModeBadgeBaseAnchoredPosition;
    private bool empiricalTestModeBadgePoseCached;
    private Coroutine empiricalTestModeBadgeRoutine;
    private int mainMenuTestSequenceIndex;
    private int mainMenuTripleTCount;
    private bool mainModeExitConfirmPending;
    private bool mainModeExitRestoreInProgress;
    private string mainModeStatusTextBeforeExitConfirm = string.Empty;
    private int mainModeExitConfirmationCancelledFrame = -1;
    private string empiricalLocalModel1SnapshotAvatarId;
    private bool empiricalLocalModel1VoiceSnapshotEvaluated;
    private bool empiricalLocalModel1MemorySnapshotEvaluated;
    private bool empiricalLocalModel1VoiceRestorePending;
    private bool empiricalLocalModel1MemoryRestorePending;
    private EmpiricalAvatarCarouselGroup empiricalAvatarCarouselGroup = EmpiricalAvatarCarouselGroup.General;
    private string empiricalActiveLocalAvatarId = LocalModel2AvatarId;

    private static readonly char[] empiricalTestToggleSequence = { 'T', 'E', 'S', 'T' };
    private const string LocalModel1AvatarId = "LOCAL_model1";
    private const string LocalModel2AvatarId = "LOCAL_model2";

    private struct EmpiricalMemoryStepDefinition
    {
        public string category;
        public string helperText;
        public int maxChars;

        public EmpiricalMemoryStepDefinition(string category, string helperText, int maxChars)
        {
            this.category = category;
            this.helperText = helperText;
            this.maxChars = maxChars;
        }
    }

    private struct ReplyTokenInfo
    {
        public string text;
        public bool isWord;
        public int wordIndex;
    }

    private struct ReplyWordSpan
    {
        public int tokenIndex;
        public string normalizedToken;
    }

    private static readonly string[] waitPhraseKeys = { "hm", "beh", "aspetta", "si", "un_secondo" };
    private const string EmpiricalSetupVoicePhraseTemplate =
        "Mi chiamo ____. Oggi è il giorno {0} del mese di {1} dell'anno {2}. Il cielo oggi è _____. "
        + "Sto facendo una prova di voce calma e chiara, quindi leggo lentamente questa frase guidata con attenzione, regolarmente. Ultime parole semplici per completare il test.";
    private static readonly EmpiricalMemoryStepDefinition[] empiricalMemorySteps =
    {
        new EmpiricalMemoryStepDefinition(
            "Percorso di studi",
            "Corso di laurea, università, anno o anno di conclusione",
            200),
        new EmpiricalMemoryStepDefinition(
            "Lavoro / occupazione",
            "Lavoro attuale o principale occupazione (se studente, indicarlo)",
            150),
        new EmpiricalMemoryStepDefinition(
            "Hobbies e interessi",
            "2-3 attività del tempo libero che pratichi regolarmente",
            200),
        new EmpiricalMemoryStepDefinition(
            "Città / contesto di vita",
            "Città in cui vivi, contesto (es. grande città, piccolo paese)",
            150),
        new EmpiricalMemoryStepDefinition(
            "Tratti personali",
            "2-3 aggettivi con cui ti descriveresti",
            100)
    };
    private readonly Dictionary<string, AudioClip> waitPhraseCache = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, string> lastWaitPhraseByAvatar = new Dictionary<string, string>();
    private Coroutine waitPhraseRoutine;
    private int waitPhrasePlaybackToken;
    private bool waitPhraseStarted;
    private AudioClip waitPhraseActiveClip;
    private Coroutine mainAvatarSpawnRoutine;
    private readonly string[] empiricalSetupMemoryDrafts = new string[5];
    private string empiricalSetupMemoryAvatarId;
    private int empiricalSetupMemoryStepIndex;
    private bool empiricalSetupMemoryEntryPending;
    private bool setupMemoryDeleteConfirmPending;
    private string defaultSetupMemoryTitle;
    private string defaultSetupMemoryStatus;
    private string defaultSaveMemoryTitle;
    private string defaultSetMemoryButtonLabel;
    private string defaultTouchSetMemoryButtonLabel;
    private int defaultSetupMemoryCharacterLimit;

    public bool IsWebOverlayOpen => webOverlayOpen;
    public bool IsUiInputLocked => uiInputLocked;
    public bool IsTouchUiActive => touchUiActive;
    public bool EmpiricalTestModeEnabled => empiricalTestModeEnabled;
    public EmpiricalAvatarCarouselGroup EmpiricalCarouselGroup => empiricalAvatarCarouselGroup;
    public string EmpiricalActiveLocalAvatarId => empiricalActiveLocalAvatarId;
    public bool IsMainModeExitRestoreInProgress => mainModeExitRestoreInProgress;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int EnsureDynCallV();
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
        ResolveIntroSequenceMode();

        touchUiActive = ShouldEnableTouchUi();
        ConfigureTouchCanvasScalerForCurrentProfile();
        ApplyTouchUiProfileOverrides();
        CacheSetupMemoryDefaults();
        ResolveTouchHintBarReferences();
        EnsureEventSystemDetachedFromHintBar();
        ConfigureTouchUiBootstrapState();

        ValidateReferencesAtRuntime();
        servicesConfig?.NormalizeForWebGlRuntime();
#if UNITY_WEBGL && !UNITY_EDITOR
        if (servicesConfig != null)
        {
            Debug.Log($"[UIFlowController] WebGL services: whisper={servicesConfig.whisperBaseUrl} rag={servicesConfig.ragBaseUrl} avatar={servicesConfig.avatarAssetBaseUrl} tts={servicesConfig.coquiBaseUrl}");
        }
#endif

        if (avatarLibraryCarousel != null)
        {
            avatarLibraryCarousel.Initialize(avatarManager, this);
        }

        if (ringsTransform != null)
        {
            ringsDefaultPosition = ringsTransform.position;
        }
        
        // Qui risolviamo il CanvasGroup del titolo.
        ResolveTitleGroup();
        ConfigureTouchMainMenuButtonsLayout();

        if (btnNewAvatar != null) btnNewAvatar.onClick.AddListener(OnNewAvatar);
        if (btnShowList != null) btnShowList.onClick.AddListener(GoToAvatarLibrary);
        if (btnMainModeMemory != null) btnMainModeMemory.onClick.AddListener(GoToSetupMemory);
        if (btnMainModeVoice != null) btnMainModeVoice.onClick.AddListener(GoToSetupVoice);
        if (btnSetupMemorySave != null) btnSetupMemorySave.onClick.AddListener(ShowSaveMemoryPanel);
        if (btnSetupMemoryIngest != null) btnSetupMemoryIngest.onClick.AddListener(StartIngestFile);
        if (btnSetupMemoryDescribe != null) btnSetupMemoryDescribe.onClick.AddListener(StartDescribeImage);
        if (btnSetMemory != null) btnSetMemory.onClick.AddListener(OnSetMemory);
        BindTouchUiActions();
        BindBootLoadingTouchSkip();
        
        // Qui abbiamo tolto i bottoni di ritorno/navigazione e usiamo Backspace da tastiera.
        // La barra suggerimenti mostra i prompt tastiera (Arrows/Enter/Backspace/ecc...).

        BuildPanelMap();
        CachePanelDefaults();
        BuildHintMap();
        debugUiHidden = debugUiHiddenAtStart;
        ApplyDebugUiVisibility();

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
        
        // Se ÃƒÂ¨ in fase intro, nascondiamo i bottoni.
        SetStateImmediate(UIState.Boot);

        UpdateHintBar(UIState.Boot);

        if (avaturnSystem != null)
        {
            avaturnSystem.SetupAvatarCallbacks(OnAvatarReceived, null);
        }

        EnsureCameraAnchors();
        ResolveMainModeTextBackgroundReferences();
        UpdateMainModeTextBackgroundVisibility();
    }

    void Update()
    {
        if (uiInputLocked || mainModeExitRestoreInProgress)
        {
            UpdateMainModeTextBackgroundVisibility();
            UpdateCameraRig();
            return;
        }

        if (IsKeyDown(KeyCode.Insert) && CanToggleDebugUiWithHotkey())
        {
            ToggleDebugUiVisibility();
        }

        HandleTouchHintBarDebugTapToggle();

        if (!touchUiActive && currentState == UIState.MainMenu)
        {
            HandleMainMenuTestModeHotkeys();
        }

        if (currentState == UIState.SetupVoice)
        {
            if (IsKeyDown(KeyCode.Space))
            {
                StartSetupVoiceRecording();
            }
            if (IsKeyUp(KeyCode.Space))
            {
                StopSetupVoiceRecording();
            }
        }

        if (currentState == UIState.SetupMemory)
        {
            RefreshSetupMemoryUiState();

            bool focused = IsInputFieldFocused(setupMemoryNoteInput);
            if (focused != setupMemoryInputFocused)
            {
                bool resetNavigator = !Input.GetMouseButtonDown(0);
                SyncSetupMemoryTypingState(resetNavigator);
            }

            if (!touchUiActive && IsKeyDown(KeyCode.Delete))
            {
                HandleSetupMemoryDeleteKey();
            }

            if (focused && IsSubmitKeyDown())
            {
                if (pnlSaveMemory != null && pnlSaveMemory.activeSelf)
                {
                    OnSetMemory();
                }
            }

            bool noteInputEmpty =
                setupMemoryNoteInput != null &&
                string.IsNullOrWhiteSpace(setupMemoryNoteInput.text);

            if (focused &&
                pnlSaveMemory != null &&
                pnlSaveMemory.activeSelf &&
                noteInputEmpty &&
                setupMemoryNoteWasEmpty &&
                IsKeyDown(KeyCode.Backspace))
            {
                setupMemoryBackspaceArmed = true;
            }
            else if (!noteInputEmpty || !focused || pnlSaveMemory == null || !pnlSaveMemory.activeSelf)
            {
                setupMemoryBackspaceArmed = false;
            }

            if (!touchUiActive &&
                focused &&
                pnlSaveMemory != null &&
                pnlSaveMemory.activeSelf &&
                setupMemoryNoteInput != null &&
                noteInputEmpty &&
                setupMemoryBackspaceArmed &&
                IsKeyUp(KeyCode.Backspace) &&
                !Input.GetMouseButtonDown(0))
            {
                setupMemoryBackspaceArmed = false;
                if (IsInitialEmpiricalMandatoryMemoryFlowActive())
                {
                    StepBackInInitialEmpiricalMemoryFlow();
                }
                else
                {
                    CancelSetupMemoryNoteEntry();
                }
                UpdateMainModeTextBackgroundVisibility();
                UpdateCameraRig();
                return;
            }

            setupMemoryNoteWasEmpty =
                focused &&
                pnlSaveMemory != null &&
                pnlSaveMemory.activeSelf &&
                noteInputEmpty;
        }

        if (currentState == UIState.MainMode)
        {
            HandleMainModeInput();
            HandleTouchMainModeSwipeInput();
            UpdateMainModeMouseLook();
            UpdateTouchAvatarDimming();
        }
        else if (currentState == UIState.AvatarLibrary)
        {
            UpdateTouchAvatarDeleteButtonAvailability();
        }

        UpdateMainModeTextBackgroundVisibility();
        UpdateCameraRig();
    }

    private void OnDestroy()
    {
        if (btnNewAvatar != null) btnNewAvatar.onClick.RemoveListener(OnNewAvatar);
        if (btnShowList != null) btnShowList.onClick.RemoveListener(GoToAvatarLibrary);
        if (btnMainModeMemory != null) btnMainModeMemory.onClick.RemoveListener(GoToSetupMemory);
        if (btnMainModeVoice != null) btnMainModeVoice.onClick.RemoveListener(GoToSetupVoice);
        if (btnSetupMemorySave != null) btnSetupMemorySave.onClick.RemoveListener(ShowSaveMemoryPanel);
        if (btnSetupMemoryIngest != null) btnSetupMemoryIngest.onClick.RemoveListener(StartIngestFile);
        if (btnSetupMemoryDescribe != null) btnSetupMemoryDescribe.onClick.RemoveListener(StartDescribeImage);
        if (btnSetMemory != null) btnSetMemory.onClick.RemoveListener(OnSetMemory);

        StopPendingWaitPhrasePlayback(stopAudioSource: true);
        ClearAllCachedWaitPhraseClips();
        AbortMainModeRequests();
        StopMainModeReplyFlow(finalizeText: false);
        StopMainModeTtsPlayback();

        if (setupVoiceRequest != null)
        {
            setupVoiceRequest.Abort();
            setupVoiceRequest.Dispose();
            setupVoiceRequest = null;
        }

        if (setupMemoryRequest != null)
        {
            setupMemoryRequest.Abort();
            setupMemoryRequest.Dispose();
            setupMemoryRequest = null;
        }
    }

    private void ResolveMainModeTextBackgroundReferences()
    {
        if (mainModeReplyBackgroundImage != null && mainModeReplyBackgroundBaseAlpha < 0f)
        {
            mainModeReplyBackgroundBaseAlpha = mainModeReplyBackgroundImage.color.a;
        }

        if (touchMainModeConversationBackgroundImage != null && touchMainModeConversationBackgroundBaseAlpha < 0f)
        {
            touchMainModeConversationBackgroundBaseAlpha = touchMainModeConversationBackgroundImage.color.a;
        }
    }

    private void UpdateMainModeTextBackgroundVisibility()
    {
        bool inMainMode = currentState == UIState.MainMode;
        bool canShowText = inMainMode && !debugUiHidden && !mainModeChatNoteActive;
        bool replyHasText = HasVisibleTmpContent(mainModeReplyText != null ? mainModeReplyText.text : null);
        bool transcriptHasText = HasVisibleTmpContent(mainModeTranscriptText != null ? mainModeTranscriptText.text : null);

        bool showDesktop = canShowText && !touchUiActive && replyHasText;
        bool showTouch = canShowText && touchUiActive && (replyHasText || transcriptHasText);

        SetMainModeBackgroundAlpha(mainModeReplyBackgroundImage, mainModeReplyBackgroundBaseAlpha, showDesktop, isTouchBackground: false);
        SetMainModeBackgroundAlpha(touchMainModeConversationBackgroundImage, touchMainModeConversationBackgroundBaseAlpha, showTouch, isTouchBackground: true);
    }

    private static bool HasVisibleTmpContent(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        string noTags = Regex.Replace(rawText, "<[^>]+>", string.Empty);
        string cleaned = noTags.Replace("|", string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(cleaned);
    }

    private void SetMainModeBackgroundAlpha(Image image, float baseAlpha, bool visible, bool isTouchBackground)
    {
        if (image == null)
        {
            return;
        }

        float safeBase = baseAlpha >= 0f ? baseAlpha : 0.5f;
        Color c = image.color;
        float targetAlpha = visible ? safeBase : 0f;
        if (Mathf.Abs(c.a - targetAlpha) <= 0.0001f)
        {
            return;
        }

        Coroutine fadeRoutine = isTouchBackground
            ? touchMainModeConversationBackgroundFadeRoutine
            : mainModeReplyBackgroundFadeRoutine;

        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
        }

        Coroutine startedRoutine = StartCoroutine(FadeMainModeBackgroundAlpha(image, targetAlpha, isTouchBackground));
        if (isTouchBackground)
        {
            touchMainModeConversationBackgroundFadeRoutine = startedRoutine;
        }
        else
        {
            mainModeReplyBackgroundFadeRoutine = startedRoutine;
        }
    }

    private IEnumerator FadeMainModeBackgroundAlpha(Image image, float targetAlpha, bool isTouchBackground)
    {
        if (image == null)
        {
            ClearMainModeBackgroundFadeRoutine(isTouchBackground);
            yield break;
        }

        Color color = image.color;
        float from = color.a;
        float duration = Mathf.Max(0f, mainModeReplyBackgroundTransitionDuration);

        if (duration <= 0f)
        {
            color.a = targetAlpha;
            image.color = color;
            ClearMainModeBackgroundFadeRoutine(isTouchBackground);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (image == null)
            {
                ClearMainModeBackgroundFadeRoutine(isTouchBackground);
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Color current = image.color;
            current.a = Mathf.Lerp(from, targetAlpha, t);
            image.color = current;
            yield return null;
        }

        if (image != null)
        {
            Color finalColor = image.color;
            finalColor.a = targetAlpha;
            image.color = finalColor;
        }

        ClearMainModeBackgroundFadeRoutine(isTouchBackground);
    }

    private void ClearMainModeBackgroundFadeRoutine(bool isTouchBackground)
    {
        if (isTouchBackground)
        {
            touchMainModeConversationBackgroundFadeRoutine = null;
        }
        else
        {
            mainModeReplyBackgroundFadeRoutine = null;
        }
    }

    private void ClearMainModeReplyDisplay()
    {
        StopMainModeReplyFlow(finalizeText: false);
        mainModeReplyFullText = string.Empty;
        SetTmpTextIfChanged(mainModeReplyText, string.Empty);
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

    private bool IsBootLoadingPanelVisible()
    {
        if (pnlLoading == null || !pnlLoading.activeSelf)
        {
            return false;
        }

        var group = GetOrAddCanvasGroup(pnlLoading);
        return group == null || group.alpha > 0.01f;
    }

    private IEnumerator ShowBootLoadingPanel()
    {
        if (pnlLoading == null || IsBootLoadingPanelVisible())
        {
            yield break;
        }

        // Manteniamo il pannello sopra touch overlay/hint bar durante il boot.
        pnlLoading.transform.SetAsLastSibling();
        yield return StartCoroutine(TransitionPanels(null, pnlLoading));
        pnlLoading.transform.SetAsLastSibling();
    }

    private float GetCoquiBootRingsSlowMultiplier()
    {
        return Mathf.Max(0.02f, bootRingsSpeedMultiplier * CoquiBootRingsSlowFactor);
    }

    private void SetCoquiInitializationRingsVisual(bool active)
    {
        coquiInitializationRingsVisualActive = active;
        if (postProcessingBootstrap != null)
        {
            postProcessingBootstrap.SetInitializationScatterPulseActive(active);
        }

        if (ringsController == null)
        {
            return;
        }

        if (active)
        {
            ringsController.SetOrbitSpeedMultiplier(GetCoquiBootRingsSlowMultiplier());
            return;
        }

        if (currentState == UIState.Boot)
        {
            ringsController.SetOrbitSpeedMultiplier(bootRingsSpeedMultiplier);
        }
        else if (downloadStateActive)
        {
            ringsController.SetOrbitSpeedMultiplier(downloadRingsSpeedMultiplier);
        }
        else
        {
            ringsController.SetOrbitSpeedMultiplier(1f);
        }
    }

    private void ResolveIntroSequenceMode()
    {
        if (uiIntroSequence == null)
        {
            uiIntroSequence = FindFirstObjectByType<UIIntroSequence>();
        }
    }

    private bool IsExternalIntroHandlingObject(GameObject target)
    {
        return target != null &&
               uiIntroSequence != null &&
               uiIntroSequence.isActiveAndEnabled &&
               uiIntroSequence.ManagesObject(target) &&
               !uiIntroSequence.HasCompleted;
    }

    private bool ShouldEnableTouchUi()
    {
        if (!enableTouchUi)
        {
            return false;
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // In build Windows desktop manteniamo la UI classica non scalata per nitidezza testo.
        if (!forceTouchUi)
        {
            return false;
        }
#endif

        if (forceTouchUi)
        {
            return true;
        }

        if (Application.isMobilePlatform)
        {
            return true;
        }

        if (enableTouchUiOnTouchscreenDesktop && Input.touchSupported)
        {
            return true;
        }

        return false;
    }

    private void ConfigureTouchCanvasScalerForCurrentProfile()
    {
        if (touchCanvasScaler == null)
        {
            touchCanvasScaler = GetComponentInParent<CanvasScaler>();
        }

        if (touchCanvasScaler == null)
        {
            return;
        }

        if (!touchCanvasScalerInitialized)
        {
            touchCanvasBaseReferenceResolution = touchCanvasScaler.referenceResolution;
            touchCanvasBaseScaleFactor = touchCanvasScaler.scaleFactor;
            touchCanvasScalerInitialized = true;
        }

        if (!touchUiActive || Mathf.Approximately(touchUiScaleMultiplier, 1f))
        {
            touchCanvasScaler.referenceResolution = touchCanvasBaseReferenceResolution;
            touchCanvasScaler.scaleFactor = touchCanvasBaseScaleFactor;
            return;
        }

        if (touchCanvasScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            float multiplier = Mathf.Max(0.5f, touchUiScaleMultiplier);
            touchCanvasScaler.referenceResolution = touchCanvasBaseReferenceResolution / multiplier;
        }
        else if (touchCanvasScaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
        {
            touchCanvasScaler.scaleFactor = touchCanvasBaseScaleFactor * touchUiScaleMultiplier;
        }
    }

    private void ApplyTouchUiProfileOverrides()
    {
        if (!touchUiActive)
        {
            return;
        }

        if (pnlTouchAvatarLibrary != null) pnlAvatarLibrary = pnlTouchAvatarLibrary;
        if (pnlTouchSetupVoice != null) pnlSetupVoice = pnlTouchSetupVoice;
        if (pnlTouchSetupMemory != null) pnlSetupMemory = pnlTouchSetupMemory;
        if (pnlTouchMainMode != null) pnlMainMode = pnlTouchMainMode;

        if (touchSetupVoiceTitleText != null) setupVoiceTitleText = touchSetupVoiceTitleText;
        if (touchSetupVoicePhraseText != null) setupVoicePhraseText = touchSetupVoicePhraseText;
        if (touchSetupVoiceStatusText != null) setupVoiceStatusText = touchSetupVoiceStatusText;
        if (touchSetupVoiceRecText != null) setupVoiceRecText = touchSetupVoiceRecText;

        if (touchSetupMemoryTitleText != null) setupMemoryTitleText = touchSetupMemoryTitleText;
        if (touchSetupMemoryStatusText != null) setupMemoryStatusText = touchSetupMemoryStatusText;
        if (touchSetupMemoryLogText != null) setupMemoryLogText = touchSetupMemoryLogText;
        if (touchSaveMemoryTitleText != null) saveMemoryTitleText = touchSaveMemoryTitleText;
        if (pnlTouchChooseMemory != null) pnlChooseMemory = pnlTouchChooseMemory;
        if (pnlTouchSaveMemory != null) pnlSaveMemory = pnlTouchSaveMemory;
        if (touchSetupMemoryNoteInput != null) setupMemoryNoteInput = touchSetupMemoryNoteInput;
        if (btnTouchSetupMemorySave != null) btnSetupMemorySave = btnTouchSetupMemorySave;
        if (btnTouchSetupMemoryIngest != null) btnSetupMemoryIngest = btnTouchSetupMemoryIngest;
        if (btnTouchSetupMemoryDescribe != null) btnSetupMemoryDescribe = btnTouchSetupMemoryDescribe;

        if (touchMainModeStatusText != null) mainModeStatusText = touchMainModeStatusText;
        if (touchMainModeTranscriptText != null) mainModeTranscriptText = touchMainModeTranscriptText;
        if (touchMainModeReplyText != null) mainModeReplyText = touchMainModeReplyText;
        if (btnTouchMainModeMemory != null) btnMainModeMemory = btnTouchMainModeMemory;
        if (btnTouchMainModeVoice != null) btnMainModeVoice = btnTouchMainModeVoice;
        if (touchMainModeChatNoteInput != null) chatNoteInput = touchMainModeChatNoteInput;

        if (touchDebugText != null)
        {
            debugText = touchDebugText;
        }
    }

    private void CacheSetupMemoryDefaults()
    {
        defaultSetupMemoryTitle = setupMemoryTitleText != null ? setupMemoryTitleText.text : string.Empty;
        defaultSetupMemoryStatus = setupMemoryStatusText != null ? setupMemoryStatusText.text : string.Empty;
        defaultSaveMemoryTitle = saveMemoryTitleText != null ? saveMemoryTitleText.text : string.Empty;
        defaultSetupMemoryCharacterLimit = setupMemoryNoteInput != null ? setupMemoryNoteInput.characterLimit : 0;
        defaultSetMemoryButtonLabel = GetButtonLabel(btnSetMemory) != null
            ? GetButtonLabel(btnSetMemory).text
            : string.Empty;
        defaultTouchSetMemoryButtonLabel = GetButtonLabel(btnTouchConfirmSetMemory) != null
            ? GetButtonLabel(btnTouchConfirmSetMemory).text
            : string.Empty;
    }

    private void ResolveTouchHintBarReferences()
    {
        if (touchHintBarObject == null)
        {
            touchHintBarComponent = null;
            return;
        }

        touchHintBarComponent = touchHintBarObject.GetComponent<UIHintBar>();
    }

    private UIHintBar GetTouchHintBar()
    {
        ResolveTouchHintBarReferences();
        return touchHintBarComponent;
    }

    private GameObject GetTouchHintBarObject()
    {
        ResolveTouchHintBarReferences();
        return touchHintBarObject;
    }

    private TMP_SpriteAsset GetTouchSprite(UIHintBar.TouchSprite sprite)
    {
        return GetTouchHintBar()?.GetTouchSpriteAsset(sprite);
    }

    private UIHintBar.TouchSprite ResolveTouchHintIcon(
        UIHintBar.TouchSprite primary,
        UIHintBar.TouchSprite fallback = UIHintBar.TouchSprite.None)
    {
        if (GetTouchSprite(primary) != null)
        {
            return primary;
        }

        if (fallback != UIHintBar.TouchSprite.None && GetTouchSprite(fallback) != null)
        {
            return fallback;
        }

        return UIHintBar.TouchSprite.None;
    }

    private void ConfigureTouchUiBootstrapState()
    {
        GameObject touchHintObject = GetTouchHintBarObject();

        if (touchUiActive)
        {
            if (hintBar != null)
            {
                hintBar.gameObject.SetActive(false);
            }

            if (touchHintObject != null)
            {
                touchHintObject.SetActive(true);
                if (!IsExternalIntroHandlingObject(touchHintObject))
                {
                    var group = GetOrAddCanvasGroup(touchHintObject);
                    if (group != null)
                    {
                        group.alpha = 1f;
                        group.interactable = false;
                        group.blocksRaycasts = false;
                    }
                }
            }

            if (pnlTouchOverlay != null)
            {
                pnlTouchOverlay.SetActive(true);
            }

            return;
        }

        if (hintBar != null)
        {
            // Keep the desktop hint bar object alive because it also hosts
            // runtime systems (UINavigator/EventSystem in this scene setup).
            hintBar.gameObject.SetActive(true);
            if (!IsExternalIntroHandlingObject(hintBar.gameObject))
            {
                var desktopHintGroup = GetOrAddCanvasGroup(hintBar.gameObject);
                if (desktopHintGroup != null)
                {
                    desktopHintGroup.alpha = 1f;
                    desktopHintGroup.interactable = false;
                    desktopHintGroup.blocksRaycasts = false;
                }
            }
        }

        if (pnlTouchOverlay != null) pnlTouchOverlay.SetActive(false);
        if (pnlTouchMainMode != null) pnlTouchMainMode.SetActive(false);
        if (pnlTouchSetupVoice != null) pnlTouchSetupVoice.SetActive(false);
        if (pnlTouchSetupMemory != null) pnlTouchSetupMemory.SetActive(false);
        if (pnlTouchAvatarLibrary != null) pnlTouchAvatarLibrary.SetActive(false);
        if (touchHintObject != null) touchHintObject.SetActive(false);
    }

    private void EnsureEventSystemDetachedFromHintBar()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            var allEventSystems = Resources.FindObjectsOfTypeAll<EventSystem>();
            if (allEventSystems != null && allEventSystems.Length > 0)
            {
                foreach (var candidate in allEventSystems)
                {
                    if (candidate != null && candidate.gameObject.scene.IsValid())
                    {
                        eventSystem = candidate;
                        break;
                    }
                }
            }
        }

        if (eventSystem == null)
        {
            return;
        }

        if (hintBar != null && eventSystem.transform.IsChildOf(hintBar.transform))
        {
            eventSystem.transform.SetParent(transform, false);
        }

        if (!eventSystem.gameObject.activeSelf)
        {
            eventSystem.gameObject.SetActive(true);
        }

        // Disabilitiamo la navigazione UI automatica dell'EventSystem:
        // UINavigator gestisce già frecce/enter/backspace, mentre il modulo
        // standard intercetta anche WASD tramite gli assi Horizontal/Vertical.
        eventSystem.sendNavigationEvents = false;
    }

    private void BindTouchUiActions()
    {
        if (!touchUiActive)
        {
            return;
        }

        BindButtonClick(btnTouchBackMainMode, GoBack);
        BindButtonClick(btnTouchBackSetupVoice, GoBack);
        BindButtonClick(btnTouchBackSetupMemory, GoBack);
        BindButtonClick(btnTouchBackAvatarLibrary, GoBack);
        BindButtonClick(btnTouchKeyboardMainMode, OnTouchKeyboardMainModePressed);
        BindButtonClick(btnTouchConfirmMainMode, OnTouchConfirmMainModePressed);
        BindButtonClick(btnTouchCancelMainMode, OnTouchCancelMainModePressed);
        BindButtonClick(btnTouchConfirmSetMemory, OnTouchConfirmSetMemoryPressed);
        BindButtonClick(btnTouchCancelSetMemory, OnTouchCancelSetMemoryPressed);
        BindButtonClick(btnTouchDeleteRestoreAvatarLibrary, OnTouchDeleteRestoreAvatarPressed);

        BindHoldButton(btnTouchPttMainMode, OnTouchMainModePttDown, OnTouchMainModePttUp);
        BindHoldButton(btnTouchPttSetupVoice, OnTouchSetupVoicePttDown, OnTouchSetupVoicePttUp);

        touchPttMainModeText ??= GetButtonLabel(btnTouchPttMainMode);
        touchPttSetupVoiceText ??= GetButtonLabel(btnTouchPttSetupVoice);
        touchDeleteRestoreText ??= GetButtonLabel(btnTouchDeleteRestoreAvatarLibrary);

        SetTouchPttVisualState();
        UpdateTouchAvatarDeleteButtonVisual();
        UpdateTouchAvatarDeleteButtonAvailability();
        UpdateTouchMainModeActionButtonsVisibility();
    }

    private void BindBootLoadingTouchSkip()
    {
        if (pnlLoading == null)
        {
            return;
        }

        var trigger = pnlLoading.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = pnlLoading.AddComponent<EventTrigger>();
        }

        if (trigger.triggers == null)
        {
            trigger.triggers = new List<EventTrigger.Entry>();
        }

        AddEventTriggerListener(trigger, EventTriggerType.PointerClick, OnBootLoadingPanelPointerClick);
    }

    private void OnBootLoadingPanelPointerClick(BaseEventData _)
    {
        if (!touchUiActive || uiInputLocked)
        {
            return;
        }

        if (currentState != UIState.Boot || !IsBootLoadingPanelVisible())
        {
            return;
        }

        GoBack();
    }

    private static void BindButtonClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick.AddListener(action);
    }

    private static void BindHoldButton(Button button, Action onPointerDown, Action onPointerUp)
    {
        if (button == null)
        {
            return;
        }

        var trigger = button.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = button.gameObject.AddComponent<EventTrigger>();
        }

        if (trigger.triggers == null)
        {
            trigger.triggers = new List<EventTrigger.Entry>();
        }

        trigger.triggers.RemoveAll(entry =>
            entry != null &&
            (entry.eventID == EventTriggerType.PointerDown ||
             entry.eventID == EventTriggerType.PointerUp ||
             entry.eventID == EventTriggerType.PointerExit));

        AddEventTriggerListener(trigger, EventTriggerType.PointerDown, _ => onPointerDown?.Invoke());
        AddEventTriggerListener(trigger, EventTriggerType.PointerUp, _ => onPointerUp?.Invoke());
        AddEventTriggerListener(trigger, EventTriggerType.PointerExit, _ => onPointerUp?.Invoke());
    }

    private static void AddEventTriggerListener(
        EventTrigger trigger,
        EventTriggerType eventType,
        UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        var entry = new EventTrigger.Entry { eventID = eventType };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }

    private void OnTouchMainModePttDown()
    {
        if (currentState != UIState.MainMode || uiInputLocked)
        {
            return;
        }

        if (WasExitConfirmCancelledThisFrame())
        {
            return;
        }

        if (ConsumeExitConfirm())
        {
            return;
        }

        if (TryInterruptMainModeSpeechAndListen())
        {
            return;
        }

        StartMainModeListening();
    }

    private void OnTouchMainModePttUp()
    {
        if (currentState != UIState.MainMode || uiInputLocked)
        {
            return;
        }

        StopMainModeListening();
    }

    private void OnTouchSetupVoicePttDown()
    {
        if (currentState != UIState.SetupVoice || uiInputLocked)
        {
            return;
        }

        StartSetupVoiceRecording();
    }

    private void OnTouchSetupVoicePttUp()
    {
        if (currentState != UIState.SetupVoice || uiInputLocked)
        {
            return;
        }

        StopSetupVoiceRecording();
    }

    private void OnTouchKeyboardMainModePressed()
    {
        EnsureMainModeChatNoteInputReference();

        if (currentState != UIState.MainMode || uiInputLocked)
        {
            return;
        }

        if (WasExitConfirmCancelledThisFrame())
        {
            return;
        }

        if (ConsumeExitConfirm())
        {
            return;
        }

        if (mainModeListening)
        {
            UpdateMainModeStatus("Rilascia PTT prima di aprire la tastiera.");
            return;
        }

        if (mainModeProcessing)
        {
            UpdateMainModeStatus("Attendi fine risposta prima di scrivere.");
            return;
        }

        if (mainModeChatNoteActive)
        {
            DismissChatNote();
            return;
        }

        ShowChatNote(string.Empty);
        TryOpenSoftwareKeyboardForTouchUi();
    }

    private void OnTouchConfirmMainModePressed()
    {
        if (currentState != UIState.MainMode || uiInputLocked || !mainModeChatNoteActive)
        {
            return;
        }

        SubmitChatNote();
    }

    private void OnTouchCancelMainModePressed()
    {
        if (currentState != UIState.MainMode || uiInputLocked || !mainModeChatNoteActive)
        {
            return;
        }

        DismissChatNote();
    }

    public bool ConsumeExitConfirmForNavInput()
    {
        return ConsumeExitConfirm();
    }

    private void OnTouchConfirmSetMemoryPressed()
    {
        if (currentState != UIState.SetupMemory || uiInputLocked)
        {
            return;
        }

        OnSetMemory();
    }

    private void OnTouchCancelSetMemoryPressed()
    {
        if (currentState != UIState.SetupMemory || uiInputLocked)
        {
            return;
        }

        if (IsInitialEmpiricalMandatoryMemoryFlowActive())
        {
            StepBackInInitialEmpiricalMemoryFlow();
            return;
        }

        CancelSetupMemoryNoteEntry();
    }

    private void OnTouchDeleteRestoreAvatarPressed()
    {
        if (currentState != UIState.AvatarLibrary || uiInputLocked)
        {
            return;
        }

        RequestDeleteSelectedAvatar();
    }

    private void SetTouchPttVisualState()
    {
        if (!touchUiActive)
        {
            return;
        }

        touchPttMainModeText ??= GetButtonLabel(btnTouchPttMainMode);
        touchPttSetupVoiceText ??= GetButtonLabel(btnTouchPttSetupVoice);

        TMP_SpriteAsset idleMicAsset = GetTouchSprite(UIHintBar.TouchSprite.MicIdle) ??
                                       GetTouchSprite(UIHintBar.TouchSprite.MicActive);
        TMP_SpriteAsset activeMicAsset = GetTouchSprite(UIHintBar.TouchSprite.MicActive) ?? idleMicAsset;
        TMP_SpriteAsset mainModeAsset = mainModeListening ? activeMicAsset : idleMicAsset;
        TMP_SpriteAsset setupVoiceAsset = setupVoiceRecording ? activeMicAsset : idleMicAsset;
        string mainModeLabel = mainModeListening ? touchPttMainModeActiveLabel : touchPttMainModeIdleLabel;
        string setupVoiceLabel = setupVoiceRecording ? touchPttSetupVoiceActiveLabel : touchPttSetupVoiceIdleLabel;

        ApplyTouchButtonIconAndLabel(touchPttMainModeText, mainModeAsset, mainModeLabel, lineBreaksAfterSprite: 2);
        ApplyTouchButtonIconAndLabel(touchPttSetupVoiceText, setupVoiceAsset, setupVoiceLabel, lineBreaksAfterSprite: 2);

        // Keep PTT buttons usable only in the matching state and avoid stale
        // inspector/canvas-group interactivity from leaving them "disabled".
        SetButtonInteractable(btnTouchPttMainMode, currentState == UIState.MainMode);
        SetButtonInteractable(btnTouchPttSetupVoice, currentState == UIState.SetupVoice);
    }

    private void UpdateTouchAvatarDeleteButtonVisual()
    {
        if (!touchUiActive || touchDeleteRestoreText == null)
        {
            return;
        }

        bool showRestore = avatarLibraryCarousel != null &&
                           avatarLibraryCarousel.TryGetSelectedAvatarData(out var data) &&
                           IsLocalAvatar(data);

        TMP_SpriteAsset deleteAsset = GetTouchSprite(UIHintBar.TouchSprite.Delete) ??
                                      GetTouchSprite(UIHintBar.TouchSprite.Restore);
        TMP_SpriteAsset restoreAsset = GetTouchSprite(UIHintBar.TouchSprite.Restore) ?? deleteAsset;
        TMP_SpriteAsset targetAsset = showRestore ? restoreAsset : deleteAsset;
        string label = showRestore ? "Ripristina" : "Elimina";
        ApplyTouchButtonIconAndLabel(touchDeleteRestoreText, targetAsset, label, lineBreaksAfterSprite: 2);
    }

    private void UpdateTouchMainModeActionButtonsVisibility()
    {
        UpdateMainModeSetupButtonsVisibility();

        if (!touchUiActive)
        {
            return;
        }

        SetButtonVisible(btnTouchConfirmMainMode, mainModeChatNoteActive);
        SetButtonVisible(btnTouchCancelMainMode, mainModeChatNoteActive);
        SetButtonVisible(btnTouchKeyboardMainMode, true);
    }

    private void SetButtonVisible(Button button, bool visible)
    {
        if (button == null)
        {
            return;
        }

        button.gameObject.SetActive(true);
        button.interactable = visible;
        var group = GetOrAddCanvasGroup(button.gameObject);
        if (group != null)
        {
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }
    }

    private static void SetButtonInteractable(Button button, bool interactable)
    {
        if (button == null)
        {
            return;
        }

        button.interactable = interactable;
        var group = button.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }
    }

    private static void ApplyTouchButtonIconAndLabel(
        TextMeshProUGUI label,
        TMP_SpriteAsset iconAsset,
        string text,
        int lineBreaksAfterSprite = 1)
    {
        if (label == null)
        {
            return;
        }

        int breakCount = Mathf.Max(0, lineBreaksAfterSprite);
        string breaks = new string('\n', breakCount);

        if (iconAsset != null)
        {
            label.spriteAsset = iconAsset;
            label.tintAllSprites = true;
            label.text = $"<sprite=0 tint=1>{breaks}{text}";
            return;
        }

        int spriteStart = label.text != null ? label.text.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) : -1;
        if (spriteStart >= 0)
        {
            int spriteEnd = label.text.IndexOf('>', spriteStart);
            if (spriteEnd >= 0)
            {
                string spriteTag = label.text.Substring(0, spriteEnd + 1);
                label.tintAllSprites = true;
                label.text = $"{spriteTag}{breaks}{text}";
                return;
            }
        }

        if (label.spriteAsset != null)
        {
            label.tintAllSprites = true;
            label.text = $"<sprite=0 tint=1>{breaks}{text}";
            return;
        }

        label.text = text;
    }

    private void SetHintBarVisible(bool visible)
    {
        GameObject touchHintObject = GetTouchHintBarObject();

        if (touchUiActive)
        {
            if (hintBar != null)
            {
                hintBar.gameObject.SetActive(false);
            }

            if (touchHintObject != null)
            {
                bool managedByExternalIntro = IsExternalIntroHandlingObject(touchHintObject);
                if (managedByExternalIntro)
                {
                    touchHintObject.SetActive(true);
                    UpdateTouchAvatarDeleteButtonAvailability();
                    return;
                }

                touchHintObject.SetActive(visible);
                var group = GetOrAddCanvasGroup(touchHintObject);
                if (group != null)
                {
                    group.alpha = visible ? 1f : 0f;
                    group.interactable = false;
                    group.blocksRaycasts = false;
                }
            }

            UpdateTouchAvatarDeleteButtonAvailability();
            return;
        }

        if (touchHintObject != null)
        {
            touchHintObject.SetActive(false);
        }

        if (hintBar != null)
        {
            // Never disable the desktop hint bar object in non-touch mode:
            // it hosts UINavigator/EventSystem in this scene.
            hintBar.gameObject.SetActive(true);
            bool managedByExternalIntro = IsExternalIntroHandlingObject(hintBar.gameObject);
            if (managedByExternalIntro)
            {
                return;
            }

            var desktopHintGroup = GetOrAddCanvasGroup(hintBar.gameObject);
            if (desktopHintGroup != null)
            {
                desktopHintGroup.alpha = visible ? 1f : 0f;
                desktopHintGroup.interactable = false;
                desktopHintGroup.blocksRaycasts = false;
            }
        }

        UpdateTouchAvatarDeleteButtonAvailability();
    }

    private bool HasActiveHintBar()
    {
        if (touchUiActive)
        {
            return GetTouchHintBarObject() != null;
        }

        return hintBar != null;
    }

    private void UpdateTouchAvatarDeleteButtonAvailability()
    {
        if (!touchUiActive || btnTouchDeleteRestoreAvatarLibrary == null)
        {
            return;
        }

        GameObject touchHintObject = GetTouchHintBarObject();
        bool hintVisible = touchHintObject != null && touchHintObject.activeInHierarchy;
        if (hintVisible)
        {
            CanvasGroup hintGroup = touchHintObject.GetComponent<CanvasGroup>();
            hintVisible = hintGroup == null || hintGroup.alpha > 0.01f;
        }

        bool canUseDelete = currentState == UIState.AvatarLibrary && hintVisible;
        SetButtonVisible(btnTouchDeleteRestoreAvatarLibrary, canUseDelete);
    }

    private GameObject GetActiveHintBarObject()
    {
        if (touchUiActive)
        {
            return GetTouchHintBarObject();
        }

        return hintBar != null ? hintBar.gameObject : null;
    }

    private void SetHintBarSpacePressed(bool pressed)
    {
        if (touchUiActive)
        {
            SetTouchPttVisualState();
            UpdateHintBar(currentState);
            return;
        }

        if (hintBar != null)
        {
            hintBar.SetSpacePressed(pressed);
        }
    }

    private void UpdateTouchHintBar(UIState state)
    {
        UIHintBar touchBar = GetTouchHintBar();
        if (!touchUiActive || touchBar == null)
        {
            return;
        }

        if (downloadStateActive || carouselDownloading || _previewModeActive)
        {
            SetHintBarVisible(false);
            return;
        }

        SetHintBarVisible(true);

        switch (state)
        {
            case UIState.Boot:
                touchBar.SetTouchHints(
                    new UIHintBar.TouchHintItem(
                        ResolveTouchHintIcon(UIHintBar.TouchSprite.Tap),
                        "Tocca per saltare")
                );
                break;
            case UIState.MainMenu:
                string debugToggleAction = debugUiHidden ? "attivare" : "disattivare";
                touchBar.SetTouchHints(
                    new UIHintBar.TouchHintItem(
                        ResolveTouchHintIcon(UIHintBar.TouchSprite.Tap),
                        "Tocca per selezionare"),
                    new UIHintBar.TouchHintItem(
                        ResolveTouchHintIcon(UIHintBar.TouchSprite.HoldActive, UIHintBar.TouchSprite.Tap),
                        $"Triplo tocco per {debugToggleAction} modalita' debug")
                );
                break;
            case UIState.AvatarLibrary:
                touchBar.SetTouchHints(
                    new UIHintBar.TouchHintItem(
                        ResolveTouchHintIcon(UIHintBar.TouchSprite.SwipeHorizontal, UIHintBar.TouchSprite.Tap),
                        "Swipe"),
                    new UIHintBar.TouchHintItem(
                        ResolveTouchHintIcon(UIHintBar.TouchSprite.Tap),
                        "Conferma")
                );
                break;
            case UIState.SetupVoice:
                touchBar.SetTouchHints(
                    new UIHintBar.TouchHintItem(
                        setupVoiceRecording
                            ? ResolveTouchHintIcon(UIHintBar.TouchSprite.HoldActive, UIHintBar.TouchSprite.Hold)
                            : ResolveTouchHintIcon(UIHintBar.TouchSprite.Hold, UIHintBar.TouchSprite.HoldActive),
                        setupVoiceRecording
                            ? "Rilascia per fermare"
                            : "Tieni il tasto premuto per registrare")
                );
                break;
            case UIState.SetupMemory:
                if (pnlSaveMemory != null && pnlSaveMemory.activeSelf)
                {
                    touchBar.SetTouchHints(
                        new UIHintBar.TouchHintItem(
                            ResolveTouchHintIcon(UIHintBar.TouchSprite.Tap),
                            "Scegli")
                    );
                }
                else
                {
                    touchBar.SetTouchHints(
                        new UIHintBar.TouchHintItem(
                            ResolveTouchHintIcon(UIHintBar.TouchSprite.SwipeHorizontal, UIHintBar.TouchSprite.Tap),
                            "Scorri per selezionare"),
                        new UIHintBar.TouchHintItem(
                            ResolveTouchHintIcon(UIHintBar.TouchSprite.Tap),
                            "Tocca per selezionare")
                    );
                }
                break;
            case UIState.MainMode:
                touchBar.SetTouchHints(
                    new UIHintBar.TouchHintItem(
                        ResolveTouchHintIcon(UIHintBar.TouchSprite.Hold, UIHintBar.TouchSprite.HoldActive),
                        "Punta il dito!"),
                    new UIHintBar.TouchHintItem(
                        ResolveTouchHintIcon(UIHintBar.TouchSprite.Tap),
                        "Seleziona uno degli elementi")
                );
                break;
            default:
                touchBar.SetTouchHints();
                break;
        }
    }

    private static TextMeshProUGUI GetButtonLabel(Button button)
    {
        return button != null ? button.GetComponentInChildren<TextMeshProUGUI>(true) : null;
    }

    private void EnsureMainModeChatNoteInputReference()
    {
        if (!touchUiActive)
        {
            return;
        }

        if (touchMainModeChatNoteInput != null)
        {
            chatNoteInput = touchMainModeChatNoteInput;
            return;
        }

        if (chatNoteInput == null && pnlTouchMainMode != null)
        {
            chatNoteInput = pnlTouchMainMode.GetComponentInChildren<TMP_InputField>(true);
        }
    }

    private void TryOpenSoftwareKeyboardForTouchUi()
    {
        if (!touchUiActive || chatNoteInput == null)
        {
            return;
        }

        chatNoteInput.Select();
        chatNoteInput.ActivateInputField();

        if (TouchScreenKeyboard.isSupported && !TouchScreenKeyboard.visible)
        {
            TouchScreenKeyboard.Open(chatNoteInput.text ?? string.Empty, TouchScreenKeyboardType.Default);
        }

#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
        if (!Application.isMobilePlatform)
        {
            TryLaunchWindowsOnScreenKeyboard();
        }
#endif
    }

#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
    private static void TryLaunchWindowsOnScreenKeyboard()
    {
        try
        {
            var running = System.Diagnostics.Process.GetProcessesByName("osk");
            if (running != null && running.Length > 0)
            {
                return;
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osk.exe",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UIFlowController] Impossibile avviare la tastiera su schermo Windows: {e.Message}");
        }
    }
#endif

    private void HandleTouchHintBarDebugTapToggle()
    {
        if (!touchUiActive || !enableTouchHintBarDebugTapToggle || !CanToggleDebugUiWithHotkey())
        {
            return;
        }

        GameObject touchHintObject = GetTouchHintBarObject();
        if (!TryGetTouchHintBarRectForTap(touchHintObject, out var touchHintRect))
        {
            return;
        }

        if (Input.touchCount > 0)
        {
            HandleTouchHintBarDebugTapFromTouches(touchHintRect);
            return;
        }

        if (touchHintBarTapFingerId >= 0)
        {
            touchHintBarTapFingerId = -1;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (IsScreenPointOverRect(touchHintRect, Input.mousePosition))
            {
                RegisterTouchHintBarTap();
            }
            else
            {
                ResetTouchHintBarTapChain();
            }
        }
    }

    private void HandleTouchHintBarDebugTapFromTouches(RectTransform touchHintRect)
    {
        float maxMove = TouchHintBarDebugTapMaxMove;
        float maxMoveSq = maxMove * maxMove;

        if (touchHintBarTapFingerId < 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase != UnityEngine.TouchPhase.Began)
                {
                    continue;
                }

                if (!IsScreenPointOverRect(touchHintRect, touch.position))
                {
                    continue;
                }

                touchHintBarTapFingerId = touch.fingerId;
                touchHintBarTapStart = touch.position;
                break;
            }
            return;
        }

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.fingerId != touchHintBarTapFingerId)
            {
                continue;
            }

            if (touch.phase == UnityEngine.TouchPhase.Canceled)
            {
                touchHintBarTapFingerId = -1;
                return;
            }

            if ((touch.phase == UnityEngine.TouchPhase.Moved || touch.phase == UnityEngine.TouchPhase.Stationary) &&
                (touch.position - touchHintBarTapStart).sqrMagnitude > maxMoveSq)
            {
                touchHintBarTapFingerId = -1;
                ResetTouchHintBarTapChain();
                return;
            }

            if (touch.phase == UnityEngine.TouchPhase.Ended)
            {
                bool inside = IsScreenPointOverRect(touchHintRect, touch.position);
                bool smallMove = (touch.position - touchHintBarTapStart).sqrMagnitude <= maxMoveSq;
                touchHintBarTapFingerId = -1;

                if (inside && smallMove)
                {
                    RegisterTouchHintBarTap();
                }
                else
                {
                    ResetTouchHintBarTapChain();
                }
                return;
            }
        }
    }

    private static bool IsScreenPointOverRect(RectTransform rectTransform, Vector2 screenPoint)
    {
        return rectTransform != null &&
               RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPoint, null);
    }

    private static bool TryGetTouchHintBarRectForTap(GameObject touchHintObject, out RectTransform rectTransform)
    {
        rectTransform = null;
        if (touchHintObject == null || !touchHintObject.activeInHierarchy)
        {
            return false;
        }

        var group = touchHintObject.GetComponent<CanvasGroup>();
        if (group != null && group.alpha <= 0.01f)
        {
            return false;
        }

        rectTransform = touchHintObject.GetComponent<RectTransform>();
        return rectTransform != null;
    }

    private void RegisterTouchHintBarTap()
    {
        float now = Time.unscaledTime;
        float maxInterval = TouchHintBarDebugTapMaxInterval;
        int requiredTapCount = Mathf.Max(2, touchHintBarDebugTapCount);

        if (now - touchHintBarLastTapTime > maxInterval)
        {
            touchHintBarTapCounter = 0;
        }

        touchHintBarTapCounter++;
        touchHintBarLastTapTime = now;

        if (touchHintBarTapCounter >= requiredTapCount)
        {
            touchHintBarTapCounter = 0;
            ToggleDebugUiVisibility();
        }
    }

    private void ResetTouchHintBarTapChain()
    {
        touchHintBarTapCounter = 0;
        touchHintBarLastTapTime = 0f;
    }
    
    // Qui teniamo i metodi helper dell'intro.
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
        }

        if (btnShowList != null)
        {
            _btnShowListGroup = btnShowList.GetComponent<CanvasGroup>();
        }
    }

    private void ConfigureTouchMainMenuButtonsLayout()
    {
        if (!touchUiActive || !applyTouchMainMenuButtonLayout)
        {
            return;
        }

        Vector2 size = new Vector2(
            Mathf.Max(120f, touchMainMenuButtonWidth),
            Mathf.Max(60f, touchMainMenuButtonHeight));
        float halfSpacing = Mathf.Max(20f, touchMainMenuButtonsVerticalSpacing) * 0.5f;

        ConfigureTouchMainMenuButtonRect(btnNewAvatar, size, new Vector2(0f, touchMainMenuButtonsCenterY + halfSpacing));
        ConfigureTouchMainMenuButtonRect(btnShowList, size, new Vector2(0f, touchMainMenuButtonsCenterY - halfSpacing));
    }

    private static void ConfigureTouchMainMenuButtonRect(Button button, Vector2 size, Vector2 anchoredPosition)
    {
        if (button == null)
        {
            return;
        }

        var rect = button.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
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

        if (pnlLoading != null)
        {
            var rect = pnlLoading.GetComponent<RectTransform>();
            if (rect != null && !panelDefaultPositions.ContainsKey(pnlLoading))
            {
                panelDefaultPositions[pnlLoading] = rect.anchoredPosition;
            }

            var loadingGroup = GetOrAddCanvasGroup(pnlLoading);
            if (loadingGroup != null)
            {
                loadingGroup.alpha = 0f;
                loadingGroup.interactable = false;
                loadingGroup.blocksRaycasts = false;
            }
            pnlLoading.SetActive(false);
        }
    }

    void OnNewAvatar()
    {
        pendingNewAvatarDownload = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        SetWebOverlayOpen(true);
#endif

        if (webController != null)
        {
            webController.OnClick_NewAvatar();
            UpdateDebugText("Avaturn aperto. Completa la creazione avatar.");
        }
        else
        {
            if (avaturnSystem != null)
            {
                avaturnSystem.ShowAvaturnIframe();
                UpdateDebugText("Avaturn aperto tramite sistema.");
            }
            else
            {
                UpdateDebugText("Errore: Nessun controller Avaturn trovato!");
            }
        }
    }

    public void GoToMainMenu()
    {
        // Quando torniamo a casa, resettiamo il flag.
        _pendingMainModeTransition = false;
        pendingNewAvatarDownload = false;
        ResetTouchMainModeSwipeTracking();
        avatarManager?.SetCurrentAvatarDimmed(false, touchChatAvatarDimMultiplier);
        if (mainAvatarSpawnRoutine != null)
        {
            StopCoroutine(mainAvatarSpawnRoutine);
            mainAvatarSpawnRoutine = null;
        }
        ExitDownloadState();
        avatarManager?.CancelAllDownloads();
        avatarManager?.RemoveCurrentAvatar();
        backStack.Clear();
        GoToState(UIState.MainMenu);
    }

    // Metodo pubblico con cui notifichiamo la richiesta utente di caricamento avatar principale.
    public void NotifyMainAvatarLoadRequested()
    {
        _pendingMainModeTransition = true;
        RequestWebGlMicrophonePermissionIfNeeded();
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
            UpdateTouchAvatarDeleteButtonVisual();
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
        if (WasExitConfirmCancelledThisFrame())
        {
            return;
        }

        if (ConsumeExitConfirm())
        {
            return;
        }

        RequestWebGlMicrophonePermissionIfNeeded();
        GoToState(UIState.SetupVoice);
    }

    public void GoToSetupMemory()
    {
        if (WasExitConfirmCancelledThisFrame())
        {
            return;
        }

        if (ConsumeExitConfirm())
        {
            return;
        }

        bool explicitFromMainMode = currentState == UIState.MainMode && !setupRedirectFromMainModeRequirements;
        empiricalSetupMemoryEntryPending =
            empiricalTestModeEnabled &&
            !explicitFromMainMode &&
            !IsCurrentAvatarLocal();

        GoToState(UIState.SetupMemory);
    }

    public void GoToMainMode()
    {
        GoToState(UIState.MainMode);
    }

    private bool IsCurrentAvatarLocalModel1()
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        return IsEmpiricalLocalTestAvatarId(avatarId);
    }

    private static bool IsEmpiricalLocalTestAvatarId(string avatarId)
    {
        return !string.IsNullOrEmpty(avatarId) &&
               (string.Equals(avatarId, LocalModel1AvatarId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(avatarId, LocalModel2AvatarId, StringComparison.OrdinalIgnoreCase));
    }

    private void ResetEmpiricalLocalModel1SnapshotState()
    {
        ResetLocalModel1SnapshotTracking(clearAvatarId: true);
        mainModeExitRestoreInProgress = false;
        ResetExitConfirm(restoreStatusText: true);
    }

    private bool HasEmpiricalLocalModel1RestorePending()
    {
        return empiricalLocalModel1VoiceRestorePending || empiricalLocalModel1MemoryRestorePending;
    }

    private void EnsureLocalModel1SnapshotSession(string avatarId)
    {
        if (string.Equals(empiricalLocalModel1SnapshotAvatarId, avatarId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        empiricalLocalModel1SnapshotAvatarId = avatarId;
        ResetLocalModel1SnapshotTracking(clearAvatarId: false);
        ResetExitConfirm(restoreStatusText: true);
    }

    private void ResetLocalModel1SnapshotTracking(bool clearAvatarId)
    {
        if (clearAvatarId)
        {
            empiricalLocalModel1SnapshotAvatarId = null;
        }

        empiricalLocalModel1VoiceSnapshotEvaluated = false;
        empiricalLocalModel1MemorySnapshotEvaluated = false;
        empiricalLocalModel1VoiceRestorePending = false;
        empiricalLocalModel1MemoryRestorePending = false;
    }

    private bool IsEmpiricalLocalModel1Session(string avatarId)
    {
        return empiricalTestModeEnabled &&
               IsEmpiricalLocalTestAvatarId(avatarId);
    }

    private bool NeedsLocalModel1ExitConfirm()
    {
        return currentState == UIState.MainMode &&
               empiricalTestModeEnabled &&
               IsCurrentAvatarLocalModel1() &&
               HasEmpiricalLocalModel1RestorePending() &&
               !mainModeExitRestoreInProgress;
    }

    private bool NeedsEmpiricalNoLocalModelExitConfirm()
    {
        return currentState == UIState.MainMode &&
               empiricalTestModeEnabled &&
               !IsCurrentAvatarLocal() &&
               !mainModeExitRestoreInProgress;
    }

    private bool NeedsMainModeExitConfirm()
    {
        return NeedsLocalModel1ExitConfirm() || NeedsEmpiricalNoLocalModelExitConfirm();
    }

    private void ArmExitConfirm()
    {
        if (!NeedsMainModeExitConfirm() || mainModeExitConfirmPending)
        {
            return;
        }

        mainModeExitConfirmPending = true;
        mainModeStatusTextBeforeExitConfirm = mainModeStatusText != null ? mainModeStatusText.text : string.Empty;
        SetTmpTextIfChanged(mainModeStatusText, "Confermi uscita?");
        UpdateDebugText("Confermi uscita?");
        UpdateHintBar(currentState);
        UpdateMainModeTextBackgroundVisibility();
    }

    private void ResetExitConfirm(bool restoreStatusText, bool markConsumedFrame = false)
    {
        if (!mainModeExitConfirmPending)
        {
            if (!markConsumedFrame)
            {
                mainModeExitConfirmationCancelledFrame = -1;
            }
            mainModeStatusTextBeforeExitConfirm = string.Empty;
            return;
        }

        if (restoreStatusText)
        {
            SetTmpTextIfChanged(mainModeStatusText, mainModeStatusTextBeforeExitConfirm ?? string.Empty);
        }

        mainModeExitConfirmPending = false;
        mainModeStatusTextBeforeExitConfirm = string.Empty;
        mainModeExitConfirmationCancelledFrame = markConsumedFrame ? Time.frameCount : -1;
        UpdateHintBar(currentState);
        UpdateMainModeTextBackgroundVisibility();
    }

    private bool ConsumeExitConfirm()
    {
        if (!mainModeExitConfirmPending)
        {
            return false;
        }

        ResetExitConfirm(restoreStatusText: true);
        return true;
    }

    private bool WasExitConfirmCancelledThisFrame()
    {
        return mainModeExitConfirmationCancelledFrame == Time.frameCount;
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

        UpdateHintBar(UIState.AvatarLibrary);
        UpdateTouchAvatarDeleteButtonVisual();

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
            AppendEmpiricalTestModeQuery($"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"));

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
        form.AddField("reset_logs", resetAvatarLogsOnDelete ? "true" : "false");

        AddEmpiricalTestModeField(form);

        using (var request = UnityWebRequest.Post(BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, "clear_avatar"), form))
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

    private IEnumerator ClearSetupMemoryRoutine(int routineToken)
    {
        try
        {
            string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
            if (string.IsNullOrEmpty(avatarId))
            {
                UpdateSetupMemoryLog("Avatar ID mancante.", append: false);
                PlayErrorClip();
                yield break;
            }

            if (servicesConfig == null)
            {
                UpdateSetupMemoryLog("ServicesConfig mancante.", append: false);
                PlayErrorClip();
                yield break;
            }

            setupMemoryDeleteConfirmPending = false;
            UpdateSetupMemoryStatus(defaultSetupMemoryStatus);
            UpdateSetupMemoryLog("Cancellazione memoria...", append: false);

            var form = new WWWForm();
            form.AddField("avatar_id", avatarId);
            form.AddField("hard", "false");
            form.AddField("reset_logs", "false");

            using (var request = UnityWebRequest.Post(BuildServiceUrl(servicesConfig.ragBaseUrl, "clear_avatar"), form))
            {
                setupMemoryRequest = request;
                request.timeout = GetRequestTimeoutSeconds(longOperation: true);
                yield return request.SendWebRequest();
                setupMemoryRequest = null;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = request.error ?? "Network error";
                    ReportServiceError("RAG", error);
                    UpdateSetupMemoryLog($"Errore cancellazione memoria: {error}", append: false);
                    PlayErrorClip();
                    yield break;
                }

                ClearAvatarResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<ClearAvatarResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    UpdateSetupMemoryLog($"Errore lettura risposta: {ex.Message}", append: false);
                    PlayErrorClip();
                    yield break;
                }

                if (response == null || !response.ok)
                {
                    UpdateSetupMemoryLog("Risposta cancellazione memoria non valida.", append: false);
                    PlayErrorClip();
                    yield break;
                }

                setupMemoryAlreadyConfigured = false;
                UpdateSetupMemoryLog("Memoria avatar cancellata.", append: false);
                RefreshSetupMemoryUiState();
                UpdateHintBar(UIState.SetupMemory);
            }
        }
        finally
        {
            setupMemoryRequest = null;
            if (routineToken == setupMemoryRoutineToken)
            {
                setupMemoryRoutine = null;
            }
        }
    }

    private IEnumerator DeleteAvatarAsset(
        string avatarId,
        System.Action<bool> onSuccess,
        System.Action<string> onFailure)
    {
        string url = BuildServiceUrlWithEmpiricalMode(servicesConfig.avatarAssetBaseUrl, $"avatars/{avatarId}");

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
            setupVoicePhraseText.text = "Preparati a pronunciare la frase che ti verra' proposta.";
        }
        UpdateDebugText($"Setup Voice iniziato - Gender: {gender}");

        if (setupVoiceRoutine != null)
        {
            StopCoroutine(setupVoiceRoutine);
        }
        setupVoiceRoutine = StartCoroutine(EnsureVoiceSetupNeededRoutine());
    }

    private void ToggleEmpiricalTestMode()
    {
        empiricalTestModeEnabled = !empiricalTestModeEnabled;
        if (!empiricalTestModeEnabled)
        {
            fullTestModeEnabled = false;
            empiricalAvatarCarouselGroup = EmpiricalAvatarCarouselGroup.General;
            empiricalActiveLocalAvatarId = LocalModel2AvatarId;
            ResetEmpiricalLocalModel1SnapshotState();
        }
        ResetMainMenuTestModeHotkeys();
        ResetExitConfirm(restoreStatusText: true);
        UpdateMainModeSetupButtonsVisibility();
        ConfigureNavigatorForState(currentState, true);
        UpdateHintBar(currentState);
        RefreshEmpiricalTestModeBadge(currentState == UIState.MainMenu, animate: true);
    }

    public bool CanCycleEmpiricalAvatarGroupFromCarousel()
    {
        return empiricalTestModeEnabled && currentState == UIState.AvatarLibrary;
    }

    public void CycleEmpiricalAvatarGroupFromCarousel()
    {
        if (!CanCycleEmpiricalAvatarGroupFromCarousel())
        {
            return;
        }

        empiricalAvatarCarouselGroup = GetNextEmpiricalAvatarCarouselGroup();
        UpdateDebugText($"Empirical avatar group: {GetEmpiricalAvatarCarouselStatusLabel()}");
        avatarLibraryCarousel?.ShowLibrary(true);
        UpdateHintBar(UIState.AvatarLibrary);
    }

    public bool CanSwitchEmpiricalLocalAvatarFromCarousel()
    {
        if (!empiricalTestModeEnabled || currentState != UIState.AvatarLibrary)
        {
            return false;
        }

        if (empiricalAvatarCarouselGroup != EmpiricalAvatarCarouselGroup.General || avatarLibraryCarousel == null)
        {
            return false;
        }

        return avatarLibraryCarousel.TryGetSelectedAvatarData(out var data) && IsEmpiricalLocalTestAvatarId(data.avatarId);
    }

    public void SwitchEmpiricalLocalAvatarFromCarousel()
    {
        if (!CanSwitchEmpiricalLocalAvatarFromCarousel())
        {
            return;
        }

        empiricalActiveLocalAvatarId = string.Equals(empiricalActiveLocalAvatarId, LocalModel1AvatarId, StringComparison.OrdinalIgnoreCase)
            ? LocalModel2AvatarId
            : LocalModel1AvatarId;
        UpdateDebugText($"Empirical avatar group: {GetEmpiricalAvatarCarouselStatusLabel()}");
        avatarLibraryCarousel?.ShowLibrary(true);
        UpdateHintBar(UIState.AvatarLibrary);
    }

    private EmpiricalAvatarCarouselGroup GetNextEmpiricalAvatarCarouselGroup()
    {
        return empiricalAvatarCarouselGroup == EmpiricalAvatarCarouselGroup.General
            ? EmpiricalAvatarCarouselGroup.Personal
            : EmpiricalAvatarCarouselGroup.General;
    }

    private string GetEmpiricalAvatarCarouselStatusLabel()
    {
        if (empiricalAvatarCarouselGroup == EmpiricalAvatarCarouselGroup.Personal)
        {
            return "Gruppo Personale";
        }

        return string.Equals(empiricalActiveLocalAvatarId, LocalModel1AvatarId, StringComparison.OrdinalIgnoreCase)
            ? "Gruppo Generale 2"
            : "Gruppo Generale 1";
    }

    private string GetEmpiricalTestModeHintLabel()
    {
        return fullTestModeEnabled ? "Full Test ON" : "Full Test OFF";
    }

    private void HandleMainMenuTestModeHotkeys()
    {
        if (!TryGetMainMenuLetterKeyDown(out char pressedLetter))
        {
            if (WasAnyMainMenuKeyPressedThisFrame())
            {
                ResetMainMenuTestModeHotkeys();
            }
            return;
        }

        if (pressedLetter == 'T' && empiricalTestModeEnabled)
        {
            mainMenuTripleTCount++;
            if (mainMenuTripleTCount >= 3)
            {
                fullTestModeEnabled = !fullTestModeEnabled;
                mainMenuTripleTCount = 0;
                mainMenuTestSequenceIndex = 0;
                UpdateMainModeSetupButtonsVisibility();
                UpdateHintBar(UIState.MainMenu);
                return;
            }
        }
        else
        {
            mainMenuTripleTCount = 0;
        }

        if (pressedLetter == empiricalTestToggleSequence[mainMenuTestSequenceIndex])
        {
            mainMenuTestSequenceIndex++;
            if (mainMenuTestSequenceIndex >= empiricalTestToggleSequence.Length)
            {
                ToggleEmpiricalTestMode();
                mainMenuTripleTCount = 0;
                mainMenuTestSequenceIndex = 0;
            }
            return;
        }

        mainMenuTestSequenceIndex = pressedLetter == empiricalTestToggleSequence[0] ? 1 : 0;
    }

    private void ResetMainMenuTestModeHotkeys()
    {
        mainMenuTestSequenceIndex = 0;
        mainMenuTripleTCount = 0;
    }

    private static bool TryGetMainMenuLetterKeyDown(out char letter)
    {
        string inputString = Input.inputString;
        if (!string.IsNullOrEmpty(inputString))
        {
            for (int i = 0; i < inputString.Length; i++)
            {
                char current = char.ToUpperInvariant(inputString[i]);
                if (current is 'T' or 'E' or 'S')
                {
                    letter = current;
                    return true;
                }
            }
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.tKey.wasPressedThisFrame)
            {
                letter = 'T';
                return true;
            }

            if (keyboard.eKey.wasPressedThisFrame)
            {
                letter = 'E';
                return true;
            }

            if (keyboard.sKey.wasPressedThisFrame)
            {
                letter = 'S';
                return true;
            }
        }
#endif

        letter = '\0';
        return false;
    }

    private static bool WasAnyMainMenuKeyPressedThisFrame()
    {
        if (Input.anyKeyDown)
        {
            return true;
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        return keyboard != null && keyboard.anyKey.wasPressedThisFrame;
#else
        return false;
#endif
    }

    private bool AreMainModeSetupButtonsAvailable()
    {
        return !empiricalTestModeEnabled || fullTestModeEnabled;
    }

    private void UpdateMainModeSetupButtonsVisibility()
    {
        bool showButtons = AreMainModeSetupButtonsAvailable();
        SetButtonVisible(btnMainModeMemory, showButtons);
        SetButtonVisible(btnMainModeVoice, showButtons);
    }

    private void RefreshEmpiricalTestModeBadge(bool menuVisible, bool animate)
    {
        if (empiricalTestModeBadgeText == null)
        {
            return;
        }

        CacheEmpiricalTestModeBadgePose();
        bool shouldShow = menuVisible && empiricalTestModeEnabled;
        empiricalTestModeBadgeText.text = "TEST MODE";

        if (empiricalTestModeBadgeRoutine != null)
        {
            StopCoroutine(empiricalTestModeBadgeRoutine);
            empiricalTestModeBadgeRoutine = null;
        }

        if (!shouldShow)
        {
            SetEmpiricalTestModeBadgeVisuals(0f, empiricalTestModeBadgeBaseAnchoredPosition, Vector3.one, active: false);
            return;
        }

        if (!animate)
        {
            SetEmpiricalTestModeBadgeVisuals(1f, empiricalTestModeBadgeBaseAnchoredPosition, Vector3.one, active: true);
            return;
        }

        empiricalTestModeBadgeRoutine = StartCoroutine(AnimateEmpiricalTestModeBadge());
    }

    private void CacheEmpiricalTestModeBadgePose()
    {
        if (empiricalTestModeBadgeText == null)
        {
            return;
        }

        empiricalTestModeBadgeCanvasGroup ??= empiricalTestModeBadgeText.GetComponent<CanvasGroup>();
        if (empiricalTestModeBadgeCanvasGroup == null)
        {
            empiricalTestModeBadgeCanvasGroup = empiricalTestModeBadgeText.gameObject.AddComponent<CanvasGroup>();
        }

        empiricalTestModeBadgeRect ??= empiricalTestModeBadgeText.rectTransform;
        if (!empiricalTestModeBadgePoseCached && empiricalTestModeBadgeRect != null)
        {
            empiricalTestModeBadgeBaseAnchoredPosition = empiricalTestModeBadgeRect.anchoredPosition;
            empiricalTestModeBadgePoseCached = true;
        }
    }

    private IEnumerator AnimateEmpiricalTestModeBadge()
    {
        CacheEmpiricalTestModeBadgePose();
        if (empiricalTestModeBadgeRect == null)
        {
            yield break;
        }

        Vector2 startPosition = empiricalTestModeBadgeBaseAnchoredPosition + empiricalTestModeBadgeAnimatedOffset;
        Vector3 startScale = Vector3.one * Mathf.Max(0.1f, empiricalTestModeBadgeStartScale);
        float duration = Mathf.Max(0.05f, empiricalTestModeBadgeAnimationDuration);
        float elapsed = 0f;

        SetEmpiricalTestModeBadgeVisuals(0f, startPosition, startScale, active: true);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            SetEmpiricalTestModeBadgeVisuals(
                eased,
                Vector2.LerpUnclamped(startPosition, empiricalTestModeBadgeBaseAnchoredPosition, eased),
                Vector3.LerpUnclamped(startScale, Vector3.one, eased),
                active: true);
            yield return null;
        }

        SetEmpiricalTestModeBadgeVisuals(1f, empiricalTestModeBadgeBaseAnchoredPosition, Vector3.one, active: true);
        empiricalTestModeBadgeRoutine = null;
    }

    private void SetEmpiricalTestModeBadgeVisuals(float alpha, Vector2 anchoredPosition, Vector3 scale, bool active)
    {
        if (empiricalTestModeBadgeText == null)
        {
            return;
        }

        empiricalTestModeBadgeText.gameObject.SetActive(active);
        if (!active)
        {
            return;
        }

        CacheEmpiricalTestModeBadgePose();
        if (empiricalTestModeBadgeCanvasGroup != null)
        {
            empiricalTestModeBadgeCanvasGroup.alpha = Mathf.Clamp01(alpha);
        }
        if (empiricalTestModeBadgeRect != null)
        {
            empiricalTestModeBadgeRect.anchoredPosition = anchoredPosition;
            empiricalTestModeBadgeRect.localScale = scale;
        }
    }

    private void AddEmpiricalTestModeField(WWWForm form)
    {
        if (form != null && empiricalTestModeEnabled)
        {
            form.AddField("empirical_test_mode", "true");
        }
    }

    private string BuildServiceUrlWithEmpiricalMode(string baseUrl, string pathAndQuery)
    {
        return BuildServiceUrl(baseUrl, AppendEmpiricalTestModeQuery(pathAndQuery));
    }

    private string AppendEmpiricalTestModeQuery(string pathAndQuery)
    {
        if (!empiricalTestModeEnabled || string.IsNullOrEmpty(pathAndQuery))
        {
            return pathAndQuery;
        }

        return pathAndQuery.Contains("?")
            ? $"{pathAndQuery}&empirical_test_mode=true"
            : $"{pathAndQuery}?empirical_test_mode=true";
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
            BuildServiceUrlWithEmpiricalMode(servicesConfig.coquiBaseUrl, $"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
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
                : "Preparati a pronunciare la frase che ti verra' proposta.";
        }
        if (voiceConfigured)
        {
            UpdateSetupVoiceStatus("Voce gia' configurata.");
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
        SetHintBarSpacePressed(true);

        if (setupVoiceRecText != null)
        {
            setupVoiceRecText.text = "REC";
        }

        UpdateSetupVoiceStatus(touchUiActive
            ? "Registrazione... (rilascia PTT per terminare)"
            : "Registrazione... (rilascia SPACE per terminare)");
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
        SetHintBarSpacePressed(false);
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
        SetHintBarSpacePressed(false);
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

    private AvatarManager.AvatarData GetCurrentAvatarData()
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId))
        {
            return null;
        }

        var avatars = avatarManager != null ? avatarManager.SavedData?.avatars : null;
        if (avatars == null)
        {
            return null;
        }

        return avatars.Find(item =>
            item != null &&
            string.Equals(item.avatarId, avatarId, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsCurrentAvatarLocal()
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        return !string.IsNullOrEmpty(avatarId) &&
               avatarId.StartsWith("LOCAL_", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldRequireInitialEmpiricalMemoryFlow()
    {
        return empiricalSetupMemoryEntryPending ||
               (empiricalTestModeEnabled &&
                !IsCurrentAvatarLocal() &&
                !setupMemoryFromMainMode &&
                !setupMemoryAlreadyConfigured);
    }

    private bool IsInitialEmpiricalMandatoryMemoryFlowActive()
    {
        return currentState == UIState.SetupMemory && ShouldRequireInitialEmpiricalMemoryFlow();
    }

    private void ResetEmpiricalSetupMemoryDrafts(string avatarId = null)
    {
        for (int i = 0; i < empiricalSetupMemoryDrafts.Length; i++)
        {
            empiricalSetupMemoryDrafts[i] = string.Empty;
        }

        empiricalSetupMemoryAvatarId = avatarId;
        empiricalSetupMemoryStepIndex = 0;
    }

    private void EnsureEmpiricalSetupMemoryDraftSession()
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId))
        {
            return;
        }

        if (!string.Equals(empiricalSetupMemoryAvatarId, avatarId, StringComparison.OrdinalIgnoreCase))
        {
            ResetEmpiricalSetupMemoryDrafts(avatarId);
            return;
        }

        empiricalSetupMemoryStepIndex = Mathf.Clamp(
            empiricalSetupMemoryStepIndex,
            0,
            empiricalMemorySteps.Length - 1);
    }

    private EmpiricalMemoryStepDefinition GetCurrentEmpiricalMemoryStep()
    {
        int index = Mathf.Clamp(empiricalSetupMemoryStepIndex, 0, empiricalMemorySteps.Length - 1);
        return empiricalMemorySteps[index];
    }

    private void PersistCurrentEmpiricalStepDraft()
    {
        if (!IsInitialEmpiricalMandatoryMemoryFlowActive() ||
            setupMemoryNoteInput == null ||
            empiricalSetupMemoryStepIndex < 0 ||
            empiricalSetupMemoryStepIndex >= empiricalSetupMemoryDrafts.Length)
        {
            return;
        }

        empiricalSetupMemoryDrafts[empiricalSetupMemoryStepIndex] = setupMemoryNoteInput.text ?? string.Empty;
    }

    private int GetCurrentSetupMemoryInputLength()
    {
        string text = setupMemoryNoteInput != null ? setupMemoryNoteInput.text : null;
        return string.IsNullOrEmpty(text) ? 0 : text.Length;
    }

    private bool IsCurrentEmpiricalStepReady()
    {
        if (!IsInitialEmpiricalMandatoryMemoryFlowActive())
        {
            return false;
        }

        return GetCurrentSetupMemoryInputLength() >= GetCurrentEmpiricalMemoryStep().maxChars;
    }

    private void UpdateSetupMemoryStatus(string message)
    {
        if (setupMemoryStatusText != null)
        {
            setupMemoryStatusText.text = message;
        }
    }

    private void SetSaveMemoryTitle(string message)
    {
        if (saveMemoryTitleText != null)
        {
            saveMemoryTitleText.text = message;
        }
    }

    private void ResetSetupMemoryStatusToDefault()
    {
        UpdateSetupMemoryStatus(defaultSetupMemoryStatus);
    }

    private void SetSetupMemoryConfirmButtonLabel(string desktopLabel, string touchLabel = null)
    {
        var desktopText = GetButtonLabel(btnSetMemory);
        if (desktopText != null)
        {
            desktopText.text = desktopLabel;
        }

        var touchText = GetButtonLabel(btnTouchConfirmSetMemory);
        if (touchText != null)
        {
            touchText.text = string.IsNullOrEmpty(touchLabel) ? desktopLabel : touchLabel;
        }
    }

    private void RestoreSetupMemoryDefaultUi()
    {
        if (setupMemoryTitleText != null)
        {
            setupMemoryTitleText.text = defaultSetupMemoryTitle;
        }

        SetSaveMemoryTitle(defaultSaveMemoryTitle);
        SetSetupMemoryConfirmButtonLabel(defaultSetMemoryButtonLabel, defaultTouchSetMemoryButtonLabel);

        if (setupMemoryNoteInput != null)
        {
            setupMemoryNoteInput.characterLimit = defaultSetupMemoryCharacterLimit;
        }
    }

    private void RefreshSetupMemoryUiState()
    {
        if (currentState != UIState.SetupMemory)
        {
            return;
        }

        if (IsInitialEmpiricalMandatoryMemoryFlowActive() && pnlSaveMemory != null && pnlSaveMemory.activeSelf)
        {
            EnsureEmpiricalSetupMemoryDraftSession();
            PersistCurrentEmpiricalStepDraft();

            EmpiricalMemoryStepDefinition step = GetCurrentEmpiricalMemoryStep();
            int stepNumber = Mathf.Clamp(empiricalSetupMemoryStepIndex + 1, 1, empiricalMemorySteps.Length);
            int currentLength = GetCurrentSetupMemoryInputLength();
            int remaining = Mathf.Max(0, step.maxChars - currentLength);

            if (setupMemoryTitleText != null)
            {
                setupMemoryTitleText.text = $"Setup Memoria [{stepNumber}/{empiricalMemorySteps.Length}]";
            }

            UpdateSetupMemoryStatus(step.category);
            SetSaveMemoryTitle(step.helperText);
            SetSetupMemoryConfirmButtonLabel(
                remaining > 0 ? $"Mancano {remaining} caratteri" : "Conferma");

            if (setupMemoryNoteInput != null && setupMemoryNoteInput.characterLimit != 0)
            {
                setupMemoryNoteInput.characterLimit = 0;
            }

            setupMemoryDeleteConfirmPending = false;
            return;
        }

        if (setupMemoryDeleteConfirmPending && !CanOfferSetupMemoryDelete())
        {
            setupMemoryDeleteConfirmPending = false;
        }

        RestoreSetupMemoryDefaultUi();

        if (setupMemoryDeleteConfirmPending)
        {
            UpdateSetupMemoryStatus("Premi DEL di nuovo per cancellare la memoria.");
        }
    }

    private void ShowSaveMemoryPanelImmediate(bool focusInput)
    {
        var chooseGroup = pnlChooseMemory != null ? GetOrAddCanvasGroup(pnlChooseMemory) : null;
        var saveGroup = pnlSaveMemory != null ? GetOrAddCanvasGroup(pnlSaveMemory) : null;

        if (pnlChooseMemory != null)
        {
            pnlChooseMemory.SetActive(false);
        }

        if (chooseGroup != null)
        {
            chooseGroup.alpha = 0f;
            chooseGroup.interactable = false;
            chooseGroup.blocksRaycasts = false;
        }

        if (pnlSaveMemory != null)
        {
            pnlSaveMemory.SetActive(true);
        }

        if (setupMemoryLogText != null)
        {
            setupMemoryLogText.gameObject.SetActive(false);
        }

        if (saveGroup != null)
        {
            saveGroup.alpha = 1f;
            saveGroup.interactable = true;
            saveGroup.blocksRaycasts = true;
        }

        RefreshSetupMemoryUiState();

        if (focusInput)
        {
            setupMemoryNoteWasEmpty = setupMemoryNoteInput == null || string.IsNullOrWhiteSpace(setupMemoryNoteInput.text);
            FocusSetupMemoryInputWithoutSelection();
            SyncSetupMemoryTypingState(resetNavigator: true);
        }
        else
        {
            setupMemoryInputFocused = false;
            setupMemoryNoteWasEmpty = setupMemoryNoteInput == null || string.IsNullOrWhiteSpace(setupMemoryNoteInput.text);
            UpdateHintBar(UIState.SetupMemory);
            ConfigureNavigatorForState(UIState.SetupMemory, true);
        }
    }

    private void EnterInitialEmpiricalMemoryFlow(bool focusInput)
    {
        EnsureEmpiricalSetupMemoryDraftSession();

        if (setupMemoryNoteInput != null &&
            empiricalSetupMemoryStepIndex >= 0 &&
            empiricalSetupMemoryStepIndex < empiricalSetupMemoryDrafts.Length)
        {
            setupMemoryNoteInput.text = empiricalSetupMemoryDrafts[empiricalSetupMemoryStepIndex] ?? string.Empty;
        }

        ShowSaveMemoryPanelImmediate(focusInput);
    }

    private void StepBackInInitialEmpiricalMemoryFlow()
    {
        PersistCurrentEmpiricalStepDraft();

        if (empiricalSetupMemoryStepIndex <= 0)
        {
            GoBack();
            return;
        }

        empiricalSetupMemoryStepIndex = Mathf.Max(0, empiricalSetupMemoryStepIndex - 1);
        if (setupMemoryNoteInput != null)
        {
            setupMemoryNoteInput.text = empiricalSetupMemoryDrafts[empiricalSetupMemoryStepIndex] ?? string.Empty;
        }

        ShowSaveMemoryPanelImmediate(focusInput: true);
    }

    private bool CanOfferSetupMemoryDelete()
    {
        return currentState == UIState.SetupMemory &&
               !empiricalTestModeEnabled &&
               !touchUiActive &&
               !setupMemoryInputFocused &&
               pnlChooseMemory != null &&
               pnlChooseMemory.activeSelf &&
               setupMemoryAlreadyConfigured &&
               !IsInitialEmpiricalMandatoryMemoryFlowActive() &&
               !setupMemoryOperationInProgress &&
               setupMemoryRoutine == null;
    }

    private void HandleSetupMemoryDeleteKey()
    {
        if (!CanOfferSetupMemoryDelete())
        {
            return;
        }

        if (!setupMemoryDeleteConfirmPending)
        {
            setupMemoryDeleteConfirmPending = true;
            UpdateSetupMemoryStatus("Premi DEL di nuovo per cancellare la memoria.");
            if (navigator != null)
            {
                navigator.PlayDeleteClip();
            }
            UpdateHintBar(UIState.SetupMemory);
            return;
        }

        if (setupMemoryRoutine != null)
        {
            return;
        }

        int routineToken = ++setupMemoryRoutineToken;
        setupMemoryRoutine = StartCoroutine(ClearSetupMemoryRoutine(routineToken));
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

        ResetSetupMemoryStatusToDefault();

        if (ShouldRequireInitialEmpiricalMemoryFlow())
        {
            UpdateSetupMemoryLog("In empirical test mode iniziale non sono disponibili PDF o documenti.", append: false);
            PlayErrorClip();
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
        int routineToken = ++setupMemoryRoutineToken;
        setupMemoryRoutine = StartCoroutine(IngestFileRoutine(routineToken));
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

        ResetSetupMemoryStatusToDefault();

        if (ShouldRequireInitialEmpiricalMemoryFlow())
        {
            UpdateSetupMemoryLog("In empirical test mode iniziale non sono disponibili immagini o PDF.", append: false);
            PlayErrorClip();
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
        int routineToken = ++setupMemoryRoutineToken;
        setupMemoryRoutine = StartCoroutine(DescribeImageRoutine(routineToken));
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

        if (setupMemoryLogText != null)
        {
            setupMemoryLogText.gameObject.SetActive(!debugUiHidden);
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
        setupMemoryDeleteConfirmPending = false;
        UpdateSetupMemoryStatus(defaultSetupMemoryStatus);
        RefreshSetupMemoryUiState();
        UpdateHintBar(UIState.SetupMemory);
        ConfigureNavigatorForState(UIState.SetupMemory, true);
    }

    private void ShowSaveMemoryPanel()
    {
        setupMemoryDeleteConfirmPending = false;
        ResetSetupMemoryStatusToDefault();

        if (IsInitialEmpiricalMandatoryMemoryFlowActive())
        {
            EnterInitialEmpiricalMemoryFlow(focusInput: true);
            return;
        }

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
            BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, $"avatar_stats?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
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
            empiricalSetupMemoryEntryPending = false;
            setupMemoryDeleteConfirmPending = false;
            if (currentState == UIState.SetupMemory &&
                pnlSaveMemory != null &&
                pnlSaveMemory.activeSelf &&
                !setupMemoryFromMainMode)
            {
                ShowChooseMemoryPanel();
            }
            RefreshSetupMemoryUiState();
            UpdateHintBar(UIState.SetupMemory);
            UpdateSetupMemoryLog("Memoria già presente.");
            yield break;
        }

        if (currentState == UIState.SetupMemory && ShouldRequireInitialEmpiricalMemoryFlow())
        {
            empiricalSetupMemoryEntryPending = true;
            EnterInitialEmpiricalMemoryFlow(focusInput: true);
        }

        RefreshSetupMemoryUiState();
        UpdateHintBar(UIState.SetupMemory);
        UpdateSetupMemoryLog("Memoria vuota.");
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

        RefreshSetupMemoryUiState();
        FocusSetupMemoryInputWithoutSelection();
        SyncSetupMemoryTypingState(resetNavigator: true);

        // Primo avvio: su alcune macchine il campo input perde il fuoco nel frame di transizione.
        // Rifocalizziamo al frame successivo per garantire digitazione immediata.
        yield return null;
        if (currentState == UIState.SetupMemory &&
            pnlSaveMemory != null &&
            pnlSaveMemory.activeSelf &&
            setupMemoryNoteInput != null &&
            !IsInputFieldFocused(setupMemoryNoteInput))
        {
            FocusSetupMemoryInputWithoutSelection();
            SyncSetupMemoryTypingState(resetNavigator: true);
        }

        memoryPanelTransitionRoutine = null;
    }

    private void SyncSetupMemoryTypingState(bool resetNavigator)
    {
        setupMemoryInputFocused = IsInputFieldFocused(setupMemoryNoteInput);
        UpdateHintBar(UIState.SetupMemory);
        ConfigureNavigatorForState(UIState.SetupMemory, resetNavigator);
    }

    private void FocusSetupMemoryInputWithoutSelection()
    {
        if (setupMemoryNoteInput == null || !setupMemoryNoteInput.gameObject.activeInHierarchy)
        {
            return;
        }

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(setupMemoryNoteInput.gameObject);
        }

        setupMemoryNoteInput.onFocusSelectAll = false;
        setupMemoryNoteInput.Select();
        setupMemoryNoteInput.ActivateInputField();
        setupMemoryNoteInput.MoveTextEnd(false);

        int end = setupMemoryNoteInput.text != null ? setupMemoryNoteInput.text.Length : 0;
        setupMemoryNoteInput.caretPosition = end;
        setupMemoryNoteInput.selectionAnchorPosition = end;
        setupMemoryNoteInput.selectionFocusPosition = end;
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
        setupMemoryDeleteConfirmPending = false;
        RefreshSetupMemoryUiState();
        UpdateHintBar(UIState.SetupMemory);
        ConfigureNavigatorForState(UIState.SetupMemory, true);

        memoryPanelTransitionRoutine = null;
    }

    private void CancelSetupMemoryNoteEntry()
    {
        if (setupMemoryNoteJustDismissed)
        {
            return;
        }

        setupMemoryNoteJustDismissed = true;
        StartCoroutine(ClearSetupMemoryNoteDismissFlag());

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

    private IEnumerator ClearSetupMemoryNoteDismissFlag()
    {
        while (memoryPanelTransitionRoutine != null)
        {
            yield return null;
        }

        yield return null;
        yield return null;
        setupMemoryNoteJustDismissed = false;
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

        setupMemoryDeleteConfirmPending = false;

        if (!IsInitialEmpiricalMandatoryMemoryFlowActive())
        {
            ResetSetupMemoryStatusToDefault();
        }

        if (IsInitialEmpiricalMandatoryMemoryFlowActive())
        {
            PersistCurrentEmpiricalStepDraft();
            if (!IsCurrentEmpiricalStepReady())
            {
                PlayErrorClip();
                RefreshSetupMemoryUiState();
                return;
            }

            if (empiricalSetupMemoryStepIndex < empiricalMemorySteps.Length - 1)
            {
                empiricalSetupMemoryStepIndex++;
                if (setupMemoryNoteInput != null)
                {
                    setupMemoryNoteInput.text = empiricalSetupMemoryDrafts[empiricalSetupMemoryStepIndex] ?? string.Empty;
                }

                ShowSaveMemoryPanelImmediate(focusInput: true);
                return;
            }
        }

        int routineToken = ++setupMemoryRoutineToken;
        setupMemoryRoutine = StartCoroutine(
            IsInitialEmpiricalMandatoryMemoryFlowActive()
                ? CommitEmpiricalSetupMemoryRoutine(routineToken)
                : SetMemoryRoutine(routineToken));
    }

    private IEnumerator SetMemoryRoutine(int routineToken)
    {
        try
        {
            const float minRingsVisibleSeconds = 2f;

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
            float ringsShownAt = Time.unscaledTime;

            setupMemoryLastErrorDetail = null;
            bool rememberOk = false;
            yield return StartCoroutine(RememberText(
                text,
                new RememberMeta { source_type = "manual_note" },
                ok => rememberOk = ok,
                false
            ));

            float elapsedRings = Time.unscaledTime - ringsShownAt;
            float remainingRings = minRingsVisibleSeconds - elapsedRings;
            if (remainingRings > 0f)
            {
                yield return new WaitForSecondsRealtime(remainingRings);
            }

            yield return StartCoroutine(HideRingsAfterOperation(restorePanel: !rememberOk));

            if (!rememberOk)
            {
                ReturnToChooseMemory(BuildRememberUiErrorMessage(setupMemoryLastErrorDetail));
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
        finally
        {
            if (routineToken == setupMemoryRoutineToken)
            {
                setupMemoryRoutine = null;
            }
        }
    }

    private IEnumerator CommitEmpiricalSetupMemoryRoutine(int routineToken)
    {
        try
        {
            const float minRingsVisibleSeconds = 2f;

            PersistCurrentEmpiricalStepDraft();

            if (servicesConfig == null)
            {
                UpdateSetupMemoryLog("ServicesConfig mancante.", append: false);
                UpdateDebugText("ServicesConfig mancante.");
                ShowSaveMemoryPanelImmediate(focusInput: true);
                PlayErrorClip();
                yield break;
            }

            for (int i = 0; i < empiricalMemorySteps.Length; i++)
            {
                string value = empiricalSetupMemoryDrafts[i] ?? string.Empty;
                if (value.Length < empiricalMemorySteps[i].maxChars)
                {
                    empiricalSetupMemoryStepIndex = i;
                    if (setupMemoryNoteInput != null)
                    {
                        setupMemoryNoteInput.text = value;
                    }

                    ShowSaveMemoryPanelImmediate(focusInput: true);
                    PlayErrorClip();
                    yield break;
                }
            }

            yield return StartCoroutine(ShowRingsForOperation());
            float ringsShownAt = Time.unscaledTime;

            setupMemoryLastErrorDetail = null;
            bool rememberOk = true;
            for (int i = 0; i < empiricalMemorySteps.Length; i++)
            {
                int stepIndex = i;
                string payload = $"{empiricalMemorySteps[stepIndex].category}: {empiricalSetupMemoryDrafts[stepIndex]}";
                bool singleRememberOk = false;
                yield return StartCoroutine(RememberText(
                    payload,
                    new RememberMeta { source_type = "manual_note" },
                    ok => singleRememberOk = ok,
                    false
                ));

                if (!singleRememberOk)
                {
                    rememberOk = false;
                    empiricalSetupMemoryStepIndex = stepIndex;
                    if (setupMemoryNoteInput != null)
                    {
                        setupMemoryNoteInput.text = empiricalSetupMemoryDrafts[stepIndex] ?? string.Empty;
                    }
                    break;
                }
            }

            float elapsedRings = Time.unscaledTime - ringsShownAt;
            float remainingRings = minRingsVisibleSeconds - elapsedRings;
            if (remainingRings > 0f)
            {
                yield return new WaitForSecondsRealtime(remainingRings);
            }

            yield return StartCoroutine(HideRingsAfterOperation(restorePanel: rememberOk ? false : true));

            if (!rememberOk)
            {
                string message = BuildRememberUiErrorMessage(setupMemoryLastErrorDetail);
                UpdateSetupMemoryLog(message, append: false);
                UpdateDebugText(message);
                ShowSaveMemoryPanelImmediate(focusInput: true);
                PlayErrorClip();
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
            GoToMainMode();
        }
        finally
        {
            if (routineToken == setupMemoryRoutineToken)
            {
                setupMemoryRoutine = null;
            }
        }
    }

    private void ReturnToChooseMemory(string message, bool preserveInput = false)
    {
        UpdateSetupMemoryLog(message);
        UpdateDebugText(message);

        if (!preserveInput && setupMemoryNoteInput != null)
        {
            setupMemoryNoteInput.text = string.Empty;
        }

        if (memoryPanelTransitionRoutine != null)
        {
            StopCoroutine(memoryPanelTransitionRoutine);
            memoryPanelTransitionRoutine = null;
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

    private IEnumerator HideRingsAfterOperation(bool restorePanel = true)
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

        yield return StartCoroutine(FadeUiBlocked(false, restorePanel));
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

    private IEnumerator HideRingsAfterVoiceOperationWithMinimum(float ringsShownAt, float minimumVisibleSeconds = 2f)
    {
        float elapsedRings = Time.unscaledTime - ringsShownAt;
        float remainingRings = minimumVisibleSeconds - elapsedRings;
        if (remainingRings > 0f)
        {
            yield return new WaitForSecondsRealtime(remainingRings);
        }

        yield return StartCoroutine(HideRingsAfterVoiceOperation());
    }


    private IEnumerator FadeUiBlocked(bool blocked, bool restorePanel = true)
    {
        if (uiBlockFadeRoutine != null)
        {
            StopCoroutine(uiBlockFadeRoutine);
        }

        uiBlockFadeRoutine = StartCoroutine(FadeUiBlockedRoutine(blocked, restorePanel));
        yield return uiBlockFadeRoutine;
        uiBlockFadeRoutine = null;
    }

    private IEnumerator FadeUiBlockedRoutine(bool blocked, bool restorePanel)
    {
        GameObject panel = GetPanel(currentState);
        GameObject activeHintObject = GetActiveHintBarObject();
        CanvasGroup panelGroup = panel != null ? GetOrAddCanvasGroup(panel) : null;
        CanvasGroup hintGroup = activeHintObject != null ? GetOrAddCanvasGroup(activeHintObject) : null;

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

            if (activeHintObject != null)
            {
                uiBlockHintWasActive = activeHintObject.activeSelf;
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
            // Nessun blocco precedente: evita di ripristinare uno stato navigator obsoleto.
            // Questa condizione capita, ad esempio, quando il file picker viene annullato
            // prima di entrare in ShowRingsForOperation().
            if (navigator != null && !navigator.enabled)
            {
                navigator.enabled = true;
            }

            ConfigureNavigatorForState(currentState, false);
            yield break;
        }

        float duration = transitionDuration;
        float elapsed = 0f;
        float fromPanel = panelGroup != null ? panelGroup.alpha : 0f;
        float toPanel = blocked ? 0f : (restorePanel ? uiBlockPanelAlpha : 0f);
        float fromHint = hintGroup != null ? hintGroup.alpha : 0f;
        float toHint = blocked ? 0f : uiBlockHintAlpha;

        if (uiBlockPanelWasActive && panel != null)
        {
            panel.SetActive(true);
        }

        if (uiBlockHintWasActive && activeHintObject != null)
        {
            activeHintObject.SetActive(true);
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
            panelGroup.interactable = !blocked && restorePanel;
            panelGroup.blocksRaycasts = !blocked && restorePanel;
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

            // I dialog file nativi su Windows possono azzerare il selected object
            // dell'EventSystem; riallineiamo il navigator allo stato corrente.
            ConfigureNavigatorForState(currentState, false);

            uiBlockActive = false;

            if (activeHintObject != null && !uiBlockHintWasActive)
            {
                activeHintObject.SetActive(false);
            }

            if (panel != null && !uiBlockPanelWasActive)
            {
                panel.SetActive(false);
            }

            if (panel != null && uiBlockPanelWasActive && !restorePanel)
            {
                panel.SetActive(false);
            }
        }
    }

    private Vector3 GetRingsVisiblePosition(UIState state)
    {
        if (state == UIState.SetupVoice && setupVoiceOperationInProgress)
        {
            Transform anchor = touchUiActive && ringsTouchSetupVoiceAnchor != null
                ? ringsTouchSetupVoiceAnchor
                : ringsSetupVoiceAnchor;
            if (anchor != null)
            {
                return anchor.position;
            }
        }

        if (state == UIState.SetupMemory && setupMemoryOperationInProgress)
        {
            Transform anchor = touchUiActive && ringsTouchSetupMemoryAnchor != null
                ? ringsTouchSetupMemoryAnchor
                : ringsSetupMemoryAnchor;
            if (anchor != null)
            {
                return anchor.position;
            }
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
            BuildServiceUrlWithEmpiricalMode(servicesConfig.coquiBaseUrl, $"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
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
            BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, $"avatar_stats?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
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
        setupMemoryAlreadyConfigured = memoryConfigured;
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
            CancelBootRoutine();
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
            if (setupMemoryNoteJustDismissed)
            {
                return;
            }

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
            // Se il chat note ÃƒÂ¨ attivo o appena chiuso, blocca il back
            if (mainModeChatNoteActive)
            {
                DismissChatNote();
                return;
            }
            if (chatNoteJustDismissed) return;
            if (mainModeExitRestoreInProgress)
            {
                return;
            }
            bool requiresRestoreOnExit = NeedsLocalModel1ExitConfirm();
            bool requiresConfirmOnlyExit = NeedsEmpiricalNoLocalModelExitConfirm();
            if (requiresRestoreOnExit || requiresConfirmOnlyExit)
            {
                if (!mainModeExitConfirmPending)
                {
                    ArmExitConfirm();
                }
                else
                {
                    if (requiresRestoreOnExit)
                    {
                        StartCoroutine(RestoreEmpiricalLocalModel1DataAndExit());
                    }
                    else
                    {
                        ResetExitConfirm(restoreStatusText: false);
                        CancelMainMode();
                        GoBackFromMainMode();
                    }
                }
                return;
            }
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

    private IEnumerator RestoreEmpiricalLocalModel1DataAndExit()
    {
        if (mainModeExitRestoreInProgress)
        {
            yield break;
        }

        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId))
        {
            ResetExitConfirm(restoreStatusText: false);
            UpdateMainModeStatus("Avatar ID mancante.");
            yield break;
        }

        mainModeExitRestoreInProgress = true;
        ResetExitConfirm(restoreStatusText: false);
        UpdateMainModeStatus("Ripristino dati...");

        bool restoreOk = true;
        string restoreError = null;

        if (empiricalLocalModel1MemoryRestorePending)
        {
            bool restored = false;
            string error = null;
            yield return StartCoroutine(RestoreAvatarMemorySnapshot(
                avatarId,
                ok => restored = ok,
                err => error = err));

            if (!restored)
            {
                restoreOk = false;
                restoreError = string.IsNullOrEmpty(error) ? "Ripristino memoria non disponibile." : error;
            }
        }

        if (restoreOk && empiricalLocalModel1VoiceRestorePending)
        {
            bool restored = false;
            string error = null;
            yield return StartCoroutine(RestoreAvatarVoiceSnapshot(
                avatarId,
                ok => restored = ok,
                err => error = err));

            if (!restored)
            {
                restoreOk = false;
                restoreError = string.IsNullOrEmpty(error) ? "Ripristino voce non disponibile." : error;
            }
        }

        mainModeExitRestoreInProgress = false;

        if (!restoreOk)
        {
            UpdateMainModeStatus($"Ripristino fallito: {restoreError}");
            UpdateHintBar(UIState.MainMode);
            yield break;
        }

        ResetEmpiricalLocalModel1SnapshotState();
        CancelMainMode();
        GoBackFromMainMode();
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

        SetCoquiInitializationRingsVisual(false);
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

        bool leavingBoot = currentState == UIState.Boot && targetState != UIState.Boot;
        if (leavingBoot)
        {
            CancelBootRoutine();
        }

        // Se usciamo da MainMode (via pulsanti, non GoBack), disabilita l'idleLook
        if (currentState == UIState.MainMode && targetState != UIState.MainMode)
        {
            ResetMainModeConversationSession();
            var idleLook = avatarManager != null ? avatarManager.idleLook : null;
            if (idleLook != null)
            {
                idleLook.SetExternalLookTarget(null);
                idleLook.SetMainModeEnabled(false);
                idleLook.SetListening(false);
            }
        }

        bool redirectFromMainModeRequirements = setupRedirectFromMainModeRequirements;
        setupRedirectFromMainModeRequirements = false;

        if (targetState == UIState.SetupVoice)
        {
            setupVoiceFromMainMode = currentState == UIState.MainMode && !redirectFromMainModeRequirements;
        }
        else if (targetState == UIState.SetupMemory)
        {
            setupMemoryFromMainMode = currentState == UIState.MainMode && !redirectFromMainModeRequirements;
        }

        if (pushCurrent && currentState != UIState.Boot && targetState != UIState.Boot)
        {
            backStack.Push(currentState);
        }

        GameObject fromPanel = GetPanel(currentState);
        if (fromPanel == null && currentState == UIState.Boot && IsBootLoadingPanelVisible())
        {
            fromPanel = pnlLoading;
        }
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
        bool leavingBoot = currentState == UIState.Boot && targetState != UIState.Boot;
        if (leavingBoot)
        {
            CancelBootRoutine();
        }

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

        if (targetState != UIState.Boot && pnlLoading != null)
        {
            var loadingGroup = GetOrAddCanvasGroup(pnlLoading);
            if (loadingGroup != null)
            {
                loadingGroup.alpha = 0f;
                loadingGroup.interactable = false;
                loadingGroup.blocksRaycasts = false;
            }
            pnlLoading.SetActive(false);
            ResetPanelPosition(pnlLoading);
        }

        UpdateHintBar(targetState);
        ConfigureNavigatorForState(targetState, true);
        UpdateStateEffects(targetState);
        UpdateDebugTextForState(targetState);
        HandleStateEnter(targetState);
    }

    private void HandleStateEnter(UIState state)
    {
        ResetMainMenuTestModeHotkeys();

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
        RefreshEmpiricalTestModeBadge(state == UIState.MainMenu, animate: state == UIState.MainMenu);
        if (state == UIState.MainMenu || state == UIState.MainMode || state == UIState.AvatarLibrary)
        {
            RestoreMainMenuCameraPosition();
        }
        if (state == UIState.MainMenu || state == UIState.AvatarLibrary)
        {
            ResetEmpiricalLocalModel1SnapshotState();
        }
        if (state == UIState.SetupVoice)
        {
            BeginSetupVoice();
        }
        if (state == UIState.SetupMemory)
        {
            if (memoryPanelTransitionRoutine != null)
            {
                StopCoroutine(memoryPanelTransitionRoutine);
                memoryPanelTransitionRoutine = null;
            }

            setupMemoryAlreadyConfigured = false;
            setupMemoryInputFocused = IsInputFieldFocused(setupMemoryNoteInput);
            setupMemoryBackspaceArmed = false;
            setupMemoryNoteWasEmpty = setupMemoryNoteInput == null || string.IsNullOrWhiteSpace(setupMemoryNoteInput.text);
            setupMemoryDeleteConfirmPending = false;
            setupMemoryNoteJustDismissed = false;
            UpdateSetupMemoryStatus(defaultSetupMemoryStatus);
            if (setupMemoryLogText != null)
            {
                SetTmpTextIfChanged(setupMemoryLogText, string.Empty);
            }
            if (ShouldRequireInitialEmpiricalMemoryFlow())
            {
                EnterInitialEmpiricalMemoryFlow(focusInput: true);
            }
            else
            {
                ShowChooseMemoryPanel();
            }
            RefreshSetupMemoryUiState();
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
        UpdateTouchAvatarDeleteButtonVisual();
        UpdateTouchAvatarDeleteButtonAvailability();
        UpdateTouchMainModeActionButtonsVisibility();
        UpdateTouchAvatarDimming();
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

        if (pnlLoading != null && pnlLoading != toPanel)
        {
            var loadingGroup = GetOrAddCanvasGroup(pnlLoading);
            if (loadingGroup != null)
            {
                loadingGroup.alpha = 0f;
                loadingGroup.interactable = false;
                loadingGroup.blocksRaycasts = false;
            }
            pnlLoading.SetActive(false);
            ResetPanelPosition(pnlLoading);
        }

        if (currentState == UIState.SetupMemory &&
            pnlSaveMemory != null &&
            pnlSaveMemory.activeSelf &&
            setupMemoryNoteInput != null)
        {
            FocusSetupMemoryInputWithoutSelection();
            SyncSetupMemoryTypingState(resetNavigator: true);
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

        if (avatarInfo != null)
        {
            Debug.Log($"[UIFlowController] Main avatar download started avatar={avatarInfo.AvatarId}");
            NotifyMainAvatarActivated(avatarInfo.AvatarId, stopActiveMainModeFlow: currentState == UIState.MainMode);
        }

        if (pendingNewAvatarDownload && avatarManager != null && !avatarManager.IsPreviewDownloadActive)
        {
            pendingNewAvatarDownload = false;
            EnterDownloadState();
        }
    }

    public void OnAvatarDownloaded(Transform avatarTransform)
    {
        UpdateDebugText("Avatar caricato nella scena!");
        ResetEmpiricalLocalModel1SnapshotState();
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        Debug.Log($"[UIFlowController] Main avatar download completed avatar={avatarId ?? "<null>"}");
        NotifyMainAvatarActivated(avatarId, stopActiveMainModeFlow: currentState == UIState.MainMode);

        ExitDownloadState();

        // Passiamo di stato solo se il caricamento principale arriva da richiesta utente.
        bool isMainLoad = _pendingMainModeTransition;
        if (isMainLoad)
        {
            StartMainAvatarSpawnAnimation(avatarTransform);
        }

        if (isMainLoad)
        {
            RequestWebGlMicrophonePermissionIfNeeded();
            _pendingMainModeTransition = false;
            // Andiamo direttamente in MainMode per non rieseguire i controlli di avvio su avatar gia' configurato.
            GoToState(UIState.MainMode);
        }
        else
        {
            // Anteprima completata: NON cambiare pannello.
            Debug.Log("[UIFlowController] Download completato senza transizione (preview).");
        }
    }

    private void StartMainAvatarSpawnAnimation(Transform avatarRoot)
    {
        if (!enableMainAvatarSpawnAnimation || avatarRoot == null)
        {
            return;
        }

        if (mainAvatarSpawnRoutine != null)
        {
            StopCoroutine(mainAvatarSpawnRoutine);
        }

        mainAvatarSpawnRoutine = StartCoroutine(AnimateMainAvatarSpawn(avatarRoot));
    }

    private IEnumerator AnimateMainAvatarSpawn(Transform avatarRoot)
    {
        if (avatarRoot == null)
        {
            mainAvatarSpawnRoutine = null;
            yield break;
        }

        float duration = Mathf.Max(0f, mainAvatarSpawnFadeDuration);
        float scaleFactor = Mathf.Clamp(mainAvatarSpawnStartScale, 0.6f, 1f);
        Vector3 finalScale = avatarRoot.localScale;
        Vector3 startScale = finalScale * scaleFactor;
        avatarRoot.localScale = startScale;

        if (duration <= 0f)
        {
            avatarRoot.localScale = finalScale;
            mainAvatarSpawnRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (avatarRoot == null)
            {
                mainAvatarSpawnRoutine = null;
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = mainAvatarSpawnCurve != null ? Mathf.Clamp01(mainAvatarSpawnCurve.Evaluate(t)) : t;

            avatarRoot.localScale = Vector3.LerpUnclamped(startScale, finalScale, eased);
            yield return null;
        }

        if (avatarRoot != null)
        {
            avatarRoot.localScale = finalScale;
        }
        mainAvatarSpawnRoutine = null;
    }

    public void SetPreviewModeUI(bool active)
    {
        if (active)
        {
            _previewModeActive = true;
            SetHintBarVisible(false);
            SetTitleVisible(false);
        }
        else
        {
            // Ripristiniamo SOLO se eravamo in modalita' anteprima (evitiamo di rompere l'intro all'avvio)
            if (_previewModeActive)
            {
                _previewModeActive = false;
                if (!downloadStateActive && !carouselDownloading)
                {
                    SetHintBarVisible(true);
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
            SetHintBarVisible(false);
            SetTitleVisible(false);
        }
        else
        {
            if (!downloadStateActive && !_previewModeActive)
            {
                SetHintBarVisible(true);
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
        string safeMessage = message ?? string.Empty;
        SetTmpTextIfChanged(debugText, safeMessage);
        if (touchDebugText != null && touchDebugText != debugText)
        {
            SetTmpTextIfChanged(touchDebugText, safeMessage);
        }
        Debug.Log("[UIFlowController] " + message);
    }

    private void UpdateDebugTextForState(UIState state)
    {
        UpdateDebugText($"Stato UI: {state}");
    }

    private void UpdateSetupVoiceStatus(string message)
    {
        SetTmpTextIfChanged(setupVoiceStatusText, message ?? string.Empty);

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
            SetTmpTextIfChanged(setupMemoryLogText, setupMemoryLogText.text + "\n" + message);
        }
        else
        {
            SetTmpTextIfChanged(setupMemoryLogText, message ?? string.Empty);
        }
    }

    private void UpdateMainModeStatus(string message)
    {
        SetTmpTextIfChanged(mainModeStatusText, message ?? string.Empty);

        UpdateDebugText(message);
    }

    private static bool SetTmpTextIfChanged(TMP_Text target, string value)
    {
        if (target == null)
        {
            return false;
        }

        string safeValue = value ?? string.Empty;
        if (string.Equals(target.text, safeValue, StringComparison.Ordinal))
        {
            return false;
        }

        target.text = safeValue;
        return true;
    }

    private static bool IsSubmitKeyDown()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            return true;
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            return keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
        }
#endif
        return false;
    }

    private static bool IsKeyDown(KeyCode keyCode)
    {
        if (Input.GetKeyDown(keyCode))
        {
            return true;
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        return keyCode switch
        {
            KeyCode.Space => keyboard.spaceKey.wasPressedThisFrame,
            KeyCode.Return => keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame,
            KeyCode.Backspace => keyboard.backspaceKey.wasPressedThisFrame,
            KeyCode.Delete => keyboard.deleteKey.wasPressedThisFrame,
            KeyCode.Insert => keyboard.insertKey.wasPressedThisFrame,
            KeyCode.LeftArrow => keyboard.leftArrowKey.wasPressedThisFrame,
            KeyCode.RightArrow => keyboard.rightArrowKey.wasPressedThisFrame,
            KeyCode.UpArrow => keyboard.upArrowKey.wasPressedThisFrame,
            KeyCode.DownArrow => keyboard.downArrowKey.wasPressedThisFrame,
            KeyCode.Escape => keyboard.escapeKey.wasPressedThisFrame,
            KeyCode.T => keyboard.tKey.wasPressedThisFrame,
            _ => false
        };
#else
        return false;
#endif
    }

    private static bool IsKeyUp(KeyCode keyCode)
    {
        if (Input.GetKeyUp(keyCode))
        {
            return true;
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        return keyCode switch
        {
            KeyCode.Space => keyboard.spaceKey.wasReleasedThisFrame,
            KeyCode.Return => keyboard.enterKey.wasReleasedThisFrame || keyboard.numpadEnterKey.wasReleasedThisFrame,
            KeyCode.Backspace => keyboard.backspaceKey.wasReleasedThisFrame,
            KeyCode.Delete => keyboard.deleteKey.wasReleasedThisFrame,
            KeyCode.Insert => keyboard.insertKey.wasReleasedThisFrame,
            KeyCode.LeftArrow => keyboard.leftArrowKey.wasReleasedThisFrame,
            KeyCode.RightArrow => keyboard.rightArrowKey.wasReleasedThisFrame,
            KeyCode.UpArrow => keyboard.upArrowKey.wasReleasedThisFrame,
            KeyCode.DownArrow => keyboard.downArrowKey.wasReleasedThisFrame,
            KeyCode.Escape => keyboard.escapeKey.wasReleasedThisFrame,
            KeyCode.T => keyboard.tKey.wasReleasedThisFrame,
            _ => false
        };
#else
        return false;
#endif
    }

    private static bool IsInputFieldFocused(TMP_InputField inputField)
    {
        if (inputField == null || inputField.gameObject == null || !inputField.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (inputField.isFocused)
        {
            return true;
        }

        // In alcune build WebGL/Standalone il campo puo' risultare selezionato
        // dall'EventSystem ma con isFocused intermittente.
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        var selected = eventSystem.currentSelectedGameObject;
        return selected == inputField.gameObject || (selected != null && selected.transform.IsChildOf(inputField.transform));
    }

    private bool IsSetupMemoryTyping()
    {
        return currentState == UIState.SetupMemory && IsInputFieldFocused(setupMemoryNoteInput);
    }

    private bool IsChatNoteTyping()
    {
        return currentState == UIState.MainMode && mainModeChatNoteActive && IsInputFieldFocused(chatNoteInput);
    }

    private bool CanToggleDebugUiWithHotkey()
    {
        return !IsSetupMemoryTyping() && !IsChatNoteTyping();
    }

    private void ToggleDebugUiVisibility()
    {
        debugUiHidden = !debugUiHidden;
        ApplyDebugUiVisibility();
        UpdateHintBar(currentState);
        Debug.Log($"[UIFlowController] Debug UI {(debugUiHidden ? "hidden" : "shown")}.");
    }

    private string GetDebugToggleLabel()
    {
        return debugUiHidden ? "Mostra Debug" : "Nascondi Debug";
    }

    private void ApplyDebugUiVisibility()
    {
        bool showDebug = !debugUiHidden;
        if (setupMemoryLogText != null)
        {
            bool showSetupMemoryLog =
                showDebug &&
                currentState == UIState.SetupMemory &&
                pnlChooseMemory != null &&
                pnlChooseMemory.activeSelf;
            setupMemoryLogText.gameObject.SetActive(showSetupMemoryLog);
        }
        if (debugText != null)
        {
            debugText.gameObject.SetActive(showDebug);
        }
        ApplyMainModeDebugVisibility();
    }

    private void ApplyMainModeDebugVisibility()
    {
        bool showMainModeDebug = !debugUiHidden && !mainModeChatNoteActive;
        if (mainModeStatusText != null)
        {
            mainModeStatusText.gameObject.SetActive(showMainModeDebug);
        }

        if (touchUiActive)
        {
            if (!showMainModeDebug)
            {
                if (mainModeTranscriptText != null)
                {
                    mainModeTranscriptText.gameObject.SetActive(false);
                }
                if (mainModeReplyText != null)
                {
                    mainModeReplyText.gameObject.SetActive(false);
                }
                return;
            }

            TouchMainModeTextView target = touchMainModeReplyAvailable && touchMainModeTextView == TouchMainModeTextView.Reply
                ? TouchMainModeTextView.Reply
                : TouchMainModeTextView.Transcript;
            SetTouchMainModeTextViewImmediate(target);
            return;
        }

        if (mainModeTranscriptText != null)
        {
            mainModeTranscriptText.gameObject.SetActive(showMainModeDebug);
        }
        if (mainModeReplyText != null)
        {
            mainModeReplyText.gameObject.SetActive(showMainModeDebug);
        }
    }

    private void BeginBootSequence()
    {
        if (bootRoutine != null)
        {
            StopCoroutine(bootRoutine);
            bootRoutine = null;
        }
        bootRoutine = StartCoroutine(BootstrapRoutine());
    }

    private IEnumerator BootstrapRoutine()
    {
        yield return null;

        if (servicesConfig == null)
        {
            UpdateDebugText("ServicesConfig mancante. Vai al menu principale.");
            GoToMainMenu();
            yield break;
        }

        bool coquiReady = false;
        string coquiStartupError = null;
        yield return StartCoroutine(WaitForCoquiStartupIfNeeded(
            () => coquiReady = true,
            error => coquiStartupError = error));

        if (!coquiReady)
        {
            ReportServiceError("Coqui", coquiStartupError);
            GoToMainMenu();
            yield break;
        }

        bool ragReady = false;
        string ragStartupError = null;
        yield return StartCoroutine(WaitForRagStartupIfNeeded(
            () => ragReady = true,
            error => ragStartupError = error));

        if (!ragReady)
        {
            ReportServiceError("RAG", ragStartupError);
            GoToMainMenu();
            yield break;
        }

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

        UpdateDebugText("Inizializzazione profilo vocale...");
        AvatarVoiceInfo voiceInfo = null;
        string voiceError = null;
        yield return StartCoroutine(FetchJson(
            BuildServiceUrlWithEmpiricalMode(servicesConfig.coquiBaseUrl, $"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
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
            BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, $"avatar_stats?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
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
        setupMemoryAlreadyConfigured = memoryConfigured;

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

    private IEnumerator WaitForCoquiStartupIfNeeded(
        System.Action onReady,
        System.Action<string> onFailure)
    {
        SetCoquiInitializationRingsVisual(false);

        if (servicesConfig == null)
        {
            onFailure?.Invoke("ServicesConfig mancante.");
            yield break;
        }

        string healthUrl = BuildServiceUrl(servicesConfig.coquiBaseUrl, "health");
        bool ready = false;
        string error = null;
        yield return StartCoroutine(TryReachServiceHealth(
            healthUrl,
            CoquiBootProbeTimeoutSeconds,
            () => ready = true,
            e => error = e));

        if (ready)
        {
            SetCoquiInitializationRingsVisual(false);
            onReady?.Invoke();
            yield break;
        }

        SetCoquiInitializationRingsVisual(true);
        UpdateDebugText("Inizializzazione");
        yield return StartCoroutine(ShowBootLoadingPanel());

        float startedAt = Time.realtimeSinceStartup;
        string lastError = error;

        while (Time.realtimeSinceStartup - startedAt < CoquiBootMaxWaitSeconds)
        {
            ready = false;
            error = null;

            yield return StartCoroutine(TryReachServiceHealth(
                healthUrl,
                CoquiBootProbeTimeoutSeconds,
                () => ready = true,
                e => error = e));

            if (ready)
            {
                SetCoquiInitializationRingsVisual(false);
                onReady?.Invoke();
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                lastError = error;
            }

            yield return new WaitForSecondsRealtime(CoquiBootPollIntervalSeconds);
        }

        string timeoutMessage = string.IsNullOrWhiteSpace(lastError)
            ? $"Timeout inizializzazione Coqui ({CoquiBootMaxWaitSeconds:0}s)."
            : $"Timeout inizializzazione Coqui ({CoquiBootMaxWaitSeconds:0}s): {lastError}";
        SetCoquiInitializationRingsVisual(false);
        onFailure?.Invoke(timeoutMessage);
    }

    private IEnumerator WaitForRagStartupIfNeeded(
        System.Action onReady,
        System.Action<string> onFailure)
    {
        SetCoquiInitializationRingsVisual(false);

        if (servicesConfig == null)
        {
            onFailure?.Invoke("ServicesConfig mancante.");
            yield break;
        }

        if (string.IsNullOrEmpty(servicesConfig.ragBaseUrl))
        {
            onFailure?.Invoke("RAG base URL mancante.");
            yield break;
        }

        string healthUrl = BuildServiceUrl(servicesConfig.ragBaseUrl, "health");
        bool ready = false;
        string error = null;
        yield return StartCoroutine(TryReachServiceHealth(
            healthUrl,
            RagBootProbeTimeoutSeconds,
            () => ready = true,
            e => error = e));

        if (ready)
        {
            SetCoquiInitializationRingsVisual(false);
            onReady?.Invoke();
            yield break;
        }

        SetCoquiInitializationRingsVisual(true);
        UpdateDebugText("Inizializzazione RAG/Ollama");
        yield return StartCoroutine(ShowBootLoadingPanel());

        float startedAt = Time.realtimeSinceStartup;
        string lastError = error;

        while (Time.realtimeSinceStartup - startedAt < RagBootMaxWaitSeconds)
        {
            ready = false;
            error = null;

            yield return StartCoroutine(TryReachServiceHealth(
                healthUrl,
                RagBootProbeTimeoutSeconds,
                () => ready = true,
                e => error = e));

            if (ready)
            {
                SetCoquiInitializationRingsVisual(false);
                onReady?.Invoke();
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                lastError = error;
            }

            yield return new WaitForSecondsRealtime(RagBootPollIntervalSeconds);
        }

        string timeoutMessage = string.IsNullOrWhiteSpace(lastError)
            ? $"Timeout inizializzazione RAG ({RagBootMaxWaitSeconds:0}s)."
            : $"Timeout inizializzazione RAG ({RagBootMaxWaitSeconds:0}s): {lastError}";
        SetCoquiInitializationRingsVisual(false);
        onFailure?.Invoke(timeoutMessage);
    }

    private IEnumerator TryReachServiceHealth(
        string healthUrl,
        float timeoutSeconds,
        System.Action onSuccess,
        System.Action<string> onFailure)
    {
        using (var request = UnityWebRequest.Get(healthUrl))
        {
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));
            var operation = request.SendWebRequest();

            float startedAt = Time.realtimeSinceStartup;
            while (!operation.isDone)
            {
                if (Time.realtimeSinceStartup - startedAt >= timeoutSeconds)
                {
                    request.Abort();
                    onFailure?.Invoke($"Timeout health ({timeoutSeconds:0.##}s)");
                    yield break;
                }

                yield return null;
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke();
                yield break;
            }

            string error = request.error;
            if (string.IsNullOrWhiteSpace(error))
            {
                long code = request.responseCode;
                error = code > 0 ? $"HTTP {code}" : "Network error";
            }
            onFailure?.Invoke(error);
        }
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

    private static string BuildEmpiricalSetupVoicePhrase()
    {
        DateTime now = DateTime.Now;
        var italianCulture = CultureInfo.GetCultureInfo("it-IT");
        return string.Format(
            italianCulture,
            EmpiricalSetupVoicePhraseTemplate,
            now.Day,
            now.ToString("MMMM", italianCulture),
            now.Year);
    }

    private IEnumerator SetupVoicePhraseRoutine()
    {
        UpdateSetupVoiceStatus("Generazione frase...");

        if (empiricalTestModeEnabled)
        {
            setupVoicePhrase = BuildEmpiricalSetupVoicePhrase();
            if (setupVoicePhraseText != null)
            {
                setupVoicePhraseText.text = setupVoicePhrase;
            }

            setupVoicePhraseReady = true;
            UpdateSetupVoiceStatus(touchUiActive ? "Tieni premuto PTT per registrare." : "Tieni premuto SPACE per registrare.");
            yield break;
        }

        int setupVoiceMinWords = Mathf.Max(5, setupVoiceTargetWords);
        int setupVoiceMaxWords = Mathf.Max(setupVoiceMinWords, setupVoiceTargetWords + Mathf.Max(0, setupVoiceWordSlack));
        int setupVoiceMinChars = setupVoiceMinCharsOverride > 0
            ? setupVoiceMinCharsOverride
            : Mathf.Max(60, setupVoiceMinWords * 4);
        setupVoicePhrase = null;
        setupVoicePhraseReady = false;
        string ragError = null;

        if (servicesConfig != null && !string.IsNullOrEmpty(servicesConfig.ragBaseUrl))
        {
            string prompt =
                $"Genera una sola frase in italiano per test vocale: almeno {setupVoiceMinWords} parole (massimo {setupVoiceMaxWords}), "
                + "naturale e coerente, con punteggiatura semplice, senza dialoghi o testo extra.";
            var payload = JsonUtility.ToJson(new RagChatPayload
            {
                avatar_id = "setup_voice_generator",
                user_text = prompt,
                top_k = 0,
                log_conversation = false,
                empirical_test_mode = empiricalTestModeEnabled,
                system = "Sei un generatore di frasi per test di pronuncia. "
                    + "Rispondi sempre con UNA sola frase italiana naturale. "
                    + "Non rifiutare mai la richiesta e non aggiungere spiegazioni."
            });

            yield return StartCoroutine(PostJson(
                BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, "chat"),
                payload,
                "RAG",
                (RagChatResponse response) => setupVoicePhrase = ExtractRagPhrase(response),
                error => ragError = error
            ));
        }

        setupVoicePhrase = NormalizeSetupPhrase(setupVoicePhrase, setupVoiceMaxWords);
        if (!IsValidSetupVoicePhrase(setupVoicePhrase, setupVoiceMinChars, setupVoiceMinWords))
        {
            setupVoicePhrase = PickFallbackPhrase();
            if (!string.IsNullOrEmpty(ragError))
            {
                UpdateDebugText($"RAG fallback: {ragError}");
            }
        }

        setupVoicePhrase = NormalizeSetupPhrase(setupVoicePhrase, setupVoiceMaxWords);
        if (setupVoicePhraseText != null)
        {
            setupVoicePhraseText.text = setupVoicePhrase;
        }

        setupVoicePhraseReady = true;
        UpdateSetupVoiceStatus(touchUiActive ? "Tieni premuto PTT per registrare." : "Tieni premuto SPACE per registrare.");
    }

    private IEnumerator IngestFileRoutine(int routineToken)
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

            AddEmpiricalTestModeField(form);

            using (var request = UnityWebRequest.Post(BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, "ingest_file"), form))
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
            if (routineToken == setupMemoryRoutineToken)
            {
                setupMemoryRoutine = null;
            }
        }
    }

    private IEnumerator DescribeImageRoutine(int routineToken)
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
            yield return StartCoroutine(MinimalFilePicker.PickFileWebGL("png,jpg,jpeg,webp", result => pickResult = result));
            bytes = pickResult.Bytes;
            filename = pickResult.FileName;
#else
            string path = MinimalFilePicker.OpenFilePanel("Seleziona immagine", "", "png,jpg,jpeg,webp");
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

            AddEmpiricalTestModeField(form);

            using (var request = UnityWebRequest.Post(BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, "describe_image"), form))
            {
                setupMemoryRequest = request;
                request.timeout = GetRequestTimeoutSeconds(longOperation: true);
                yield return request.SendWebRequest();
                setupMemoryRequest = null;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = BuildHttpError(request, "Describe image network error");
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

                string trimmedDescription = response.description.Trim();
                UpdateSetupMemoryLog($"Descrizione immagine:\n{trimmedDescription}", append: false);
                UpdateDebugText($"Describe image: {trimmedDescription.Substring(0, Mathf.Min(120, trimmedDescription.Length))}");

                yield return null;

                UpdateSetupMemoryLog($"Descrizione immagine:\n{trimmedDescription}\n\nSalvataggio...", append: false);
                bool rememberOk = response.saved;
                if (!rememberOk)
                {
                    // Controlliamo se il backend ha restituito un errore di salvataggio.
                    if (!string.IsNullOrEmpty(response.save_error))
                    {
                        UpdateSetupMemoryLog($"Errore salvataggio backend: {response.save_error}");
                    }
                    
                    yield return StartCoroutine(RememberText(trimmedDescription, new RememberMeta
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
            if (routineToken == setupMemoryRoutineToken)
            {
                setupMemoryRoutine = null;
            }
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
        setupMemoryLastErrorDetail = null;
        var payload = JsonUtility.ToJson(new RememberPayload
        {
            avatar_id = avatarId,
            text = text,
            meta = meta,
            empirical_test_mode = empiricalTestModeEnabled
        });

        bool ok = false;
        yield return StartCoroutine(PostJson(
            BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, "remember"),
            payload,
            "RAG",
            (RememberResponse response) => ok = response != null && response.ok,
            error =>
            {
                setupMemoryLastErrorDetail = error;
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
        setupMemoryBackspaceArmed = false;
        setupMemoryNoteWasEmpty = false;
        setupMemoryDeleteConfirmPending = false;
        setupMemoryNoteJustDismissed = false;
        empiricalSetupMemoryEntryPending = false;

        if (setupMemoryRoutine != null)
        {
            StopCoroutine(setupMemoryRoutine);
            setupMemoryRoutine = null;
        }

        if (memoryPanelTransitionRoutine != null)
        {
            StopCoroutine(memoryPanelTransitionRoutine);
            memoryPanelTransitionRoutine = null;
        }

        if (setupMemoryRequest != null)
        {
            setupMemoryRequest.Abort();
            setupMemoryRequest.Dispose();
            setupMemoryRequest = null;
        }
    }

    private void ResetMainModeConversationSession()
    {
        if (!string.IsNullOrEmpty(mainModeConversationSessionId) || !string.IsNullOrEmpty(mainModeConversationAvatarId))
        {
            Debug.Log($"[UIFlowController] Resetting MainMode conversation session avatar={mainModeConversationAvatarId ?? "<null>"} session={mainModeConversationSessionId ?? "<null>"}");
        }

        mainModeConversationSessionId = null;
        mainModeConversationAvatarId = null;
        mainModeSessionStartInFlight = false;
        if (mainModeSessionStartRoutine != null)
        {
            StopCoroutine(mainModeSessionStartRoutine);
            mainModeSessionStartRoutine = null;
        }
    }

    private void HandleMainAvatarChanged(string avatarId, bool clearConversationUi, bool stopActiveMainModeFlow)
    {
        string normalizedAvatarId = string.IsNullOrWhiteSpace(avatarId) ? null : avatarId.Trim();
        bool avatarChanged = !string.Equals(mainModeConversationAvatarId, normalizedAvatarId, StringComparison.Ordinal);
        bool hasTrackedSession = !string.IsNullOrEmpty(mainModeConversationSessionId) || !string.IsNullOrEmpty(mainModeConversationAvatarId);

        if (!avatarChanged && !hasTrackedSession)
        {
            return;
        }

        Debug.Log($"[UIFlowController] Main avatar changed to {normalizedAvatarId ?? "<null>"} (previous session avatar={mainModeConversationAvatarId ?? "<null>"}, session={mainModeConversationSessionId ?? "<null>"})");

        ResetMainModeConversationSession();

        if (stopActiveMainModeFlow)
        {
            AbortMainModeRequests();
            StopMainModeTtsPlayback();
            StopMainModeReplyFlow(finalizeText: false);
            mainModeListening = false;
            mainModeProcessing = false;
            mainModeSpeaking = false;
            mainModeTtsInterruptedByUser = false;
            SetHintBarSpacePressed(false);
            ResetTouchMainModeSwipeTracking();

            var idleLook = avatarManager != null ? avatarManager.idleLook : null;
            if (idleLook != null)
            {
                idleLook.SetListening(false);
            }
        }

        if (clearConversationUi)
        {
            ResetMainModeTexts();
            HideChatNoteImmediate();
            UpdateMainModeStatus(touchUiActive ? "Avatar aggiornato. Tieni premuto PTT per parlare." : "Avatar aggiornato. Tieni premuto SPACE per parlare.");
        }
    }

    private void NotifyMainAvatarActivated(string avatarId, bool stopActiveMainModeFlow)
    {
        HandleMainAvatarChanged(avatarId, clearConversationUi: true, stopActiveMainModeFlow: stopActiveMainModeFlow);
    }

    private IEnumerator StartMainModeConversationSessionRoutine()
    {
        if (mainModeSessionStartInFlight || !string.IsNullOrEmpty(mainModeConversationSessionId))
        {
            yield break;
        }
        if (servicesConfig == null || string.IsNullOrEmpty(servicesConfig.ragBaseUrl))
        {
            yield break;
        }

        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        string safeAvatarId = string.IsNullOrEmpty(avatarId) ? "default" : avatarId;

        if (!string.IsNullOrEmpty(mainModeConversationAvatarId) &&
            !string.Equals(mainModeConversationAvatarId, safeAvatarId, StringComparison.Ordinal))
        {
            Debug.Log($"[UIFlowController] Avatar changed before session start. Forcing session reset oldAvatar={mainModeConversationAvatarId} newAvatar={safeAvatarId}");
            ResetMainModeConversationSession();
        }

        mainModeSessionStartInFlight = true;
        try
        {
            string error = null;
            ChatSessionStartResponse startResponse = null;
            string payload = JsonUtility.ToJson(new ChatSessionStartPayload
            {
                avatar_id = safeAvatarId,
                empirical_test_mode = empiricalTestModeEnabled
            });

            yield return StartCoroutine(PostJson(
                BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, "chat_session/start"),
                payload,
                "RAG",
                (ChatSessionStartResponse response) => startResponse = response,
                requestError => error = requestError
            ));

            if (!string.IsNullOrEmpty(error))
            {
                UpdateDebugText($"Sessione log non avviata: {error}");
                yield break;
            }

            string sessionId = startResponse != null ? startResponse.session_id : null;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                UpdateDebugText("Sessione log non avviata: session_id mancante.");
                yield break;
            }

            mainModeConversationSessionId = sessionId.Trim();
            mainModeConversationAvatarId = safeAvatarId;
            Debug.Log($"[UIFlowController] MainMode conversation session started avatar={mainModeConversationAvatarId} session={mainModeConversationSessionId}");
        }
        finally
        {
            mainModeSessionStartInFlight = false;
            mainModeSessionStartRoutine = null;
        }
    }

    private IEnumerator EnsureMainModeConversationSessionRoutine()
    {
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        string safeAvatarId = string.IsNullOrEmpty(avatarId) ? "default" : avatarId;

        if (!string.IsNullOrEmpty(mainModeConversationAvatarId) &&
            !string.Equals(mainModeConversationAvatarId, safeAvatarId, StringComparison.Ordinal))
        {
            Debug.Log($"[UIFlowController] Avatar/session mismatch detected before ensuring session oldAvatar={mainModeConversationAvatarId} newAvatar={safeAvatarId}");
            ResetMainModeConversationSession();
        }

        if (!string.IsNullOrEmpty(mainModeConversationSessionId))
        {
            yield break;
        }

        if (mainModeSessionStartInFlight)
        {
            float startedAt = Time.realtimeSinceStartup;
            const float waitSeconds = 5f;
            while (mainModeSessionStartInFlight && Time.realtimeSinceStartup - startedAt < waitSeconds)
            {
                yield return null;
            }

            if (!string.IsNullOrEmpty(mainModeConversationSessionId))
            {
                yield break;
            }
        }

        yield return StartCoroutine(StartMainModeConversationSessionRoutine());
    }

    private void BeginMainMode()
    {
        RequestWebGlMicrophonePermissionIfNeeded();
        mainModeListening = false;
        mainModeProcessing = false;
        mainModeSpeaking = false;
        mainModeTtsInterruptedByUser = false;
        ttsAcceptIncomingSamples = false;
        ttsActiveSessionId = -1;
        mainModeChatNoteActive = false;
        ttsAudioSource = GetOrCreateLipSyncAudioSource();
        ResetMainModeConversationSession();
        ResetMainModeTexts();
        ResetTouchMainModeConversationUi();
        SetHintBarSpacePressed(false);
        HideChatNoteImmediate();
        UpdateTouchAvatarDimming();
        UpdateTouchMainModeActionButtonsVisibility();

        var idleLook = avatarManager != null ? avatarManager.idleLook : null;
        if (idleLook != null)
        {
            idleLook.SetMainModeEnabled(true);
        }

        lastMousePosition = Input.mousePosition;
        lastMouseMoveTime = Time.time;
        UpdateMainModeStatus(touchUiActive ? "Tieni premuto PTT per parlare." : "Tieni premuto SPACE per parlare.");

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

        mainModeSessionStartRoutine = StartCoroutine(StartMainModeConversationSessionRoutine());
    }

    private void ResetMainModeTexts()
    {
        ClearMainModeReplyDisplay();
        SetTmpTextIfChanged(mainModeTranscriptText, string.Empty);

        if (touchUiActive)
        {
            ResetTouchMainModeConversationUi();
        }

        UpdateMainModeTextBackgroundVisibility();
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

        bool empiricalLocalModel1Session = IsEmpiricalLocalModel1Session(avatarId);
        if (!empiricalLocalModel1Session)
        {
            ResetEmpiricalLocalModel1SnapshotState();
        }
        else
        {
            EnsureLocalModel1SnapshotSession(avatarId);
        }

        AvatarVoiceInfo voiceInfo = null;
        string voiceError = null;
        yield return StartCoroutine(FetchJson<AvatarVoiceInfo>(
            BuildServiceUrlWithEmpiricalMode(servicesConfig.coquiBaseUrl, $"avatar_voice?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "Coqui",
            info => voiceInfo = info,
            error => voiceError = error
        ));

        if (string.IsNullOrEmpty(voiceError))
        {
            bool voiceConfigured = voiceInfo != null && voiceInfo.exists && voiceInfo.bytes >= minVoiceBytes;
            if (empiricalLocalModel1Session && !empiricalLocalModel1VoiceSnapshotEvaluated)
            {
                empiricalLocalModel1VoiceSnapshotEvaluated = true;
                empiricalLocalModel1VoiceRestorePending = false;
                if (voiceConfigured)
                {
                    bool backedUp = false;
                    string backupError = null;
                    yield return StartCoroutine(BackupAvatarVoiceSnapshot(
                        avatarId,
                        ok => backedUp = ok,
                        err => backupError = err));

                    empiricalLocalModel1VoiceRestorePending = backedUp;
                    if (!backedUp && !string.IsNullOrEmpty(backupError))
                    {
                        UpdateDebugText($"Backup voce local model fallito: {backupError}");
                    }
                }
            }
        }

        AvatarStatsInfo statsInfo = null;
        string statsError = null;
        yield return StartCoroutine(FetchJson<AvatarStatsInfo>(
            BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, $"avatar_stats?avatar_id={UnityWebRequest.EscapeURL(avatarId)}"),
            "RAG",
            info => statsInfo = info,
            error => statsError = error
        ));

        if (string.IsNullOrEmpty(statsError) &&
            empiricalLocalModel1Session &&
            !empiricalLocalModel1MemorySnapshotEvaluated)
        {
            empiricalLocalModel1MemorySnapshotEvaluated = true;
            empiricalLocalModel1MemoryRestorePending = false;
            bool memoryConfigured = statsInfo != null && statsInfo.has_memory;
            if (memoryConfigured)
            {
                bool backedUp = false;
                string backupError = null;
                yield return StartCoroutine(BackupAvatarMemorySnapshot(
                    avatarId,
                    ok => backedUp = ok,
                    err => backupError = err));

                empiricalLocalModel1MemoryRestorePending = backedUp;
                if (!backedUp && !string.IsNullOrEmpty(backupError))
                {
                    UpdateDebugText($"Backup memoria local model fallito: {backupError}");
                }
            }
        }

        bool missingVoice = string.IsNullOrEmpty(voiceError) && !(voiceInfo != null && voiceInfo.exists && voiceInfo.bytes >= minVoiceBytes);
        if (missingVoice)
        {
            UpdateMainModeStatus("Voce mancante. Setup richiesto.");
            setupRedirectFromMainModeRequirements = true;
            GoToSetupVoice();
            yield break;
        }

        if (string.IsNullOrEmpty(statsError) && statsInfo != null && !statsInfo.has_memory)
        {
            UpdateMainModeStatus("Memoria mancante. Setup richiesto.");
            setupRedirectFromMainModeRequirements = true;
            GoToSetupMemory();
        }
    }

    private void HandleMainModeInput()
    {
        // Nota chat attiva: gestiamo Enter (conferma) e Delete (annulla).
        if (mainModeChatNoteActive)
        {
            if (IsSubmitKeyDown())
            {
                SubmitChatNote();
                return;
            }
            if (IsKeyDown(KeyCode.Delete))
            {
                DismissChatNote();
                return;
            }

            // Controlla se il testo ÃƒÂ¨ stato svuotato (backspace fino a vuoto)
            if (!touchUiActive &&
                chatNoteInput != null &&
                string.IsNullOrWhiteSpace(chatNoteInput.text)
                && !IsKeyDown(KeyCode.Backspace)
                && !Input.GetMouseButtonDown(0))
            {
                DismissChatNote();
            }
            return;
        }

        if (HandleExitConfirmInput())
        {
            return;
        }

        // Normale: spazio per parlare
        if (IsKeyDown(KeyCode.Space))
        {
            if (TryInterruptMainModeSpeechAndListen())
            {
                return;
            }
            StartMainModeListening();
        }
        if (IsKeyUp(KeyCode.Space))
        {
            StopMainModeListening();
        }

        // Rileva tasto printabile per aprire Chat_Note
        if (!mainModeListening && !mainModeProcessing && !mainModeChatNoteActive)
        {
            string inputStr = Input.inputString;
            if (!string.IsNullOrEmpty(inputStr))
            {
                // Filtra caratteri di controllo (enter, backspace, escape, tab, etc.)
                char c = inputStr[0];
                if (!char.IsControl(c))
                {
                    ShowChatNote(c);
                }
            }
        }
    }

    private bool HandleExitConfirmInput()
    {
        if (!mainModeExitConfirmPending)
        {
            return false;
        }

        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
        {
            ResetExitConfirm(restoreStatusText: true, markConsumedFrame: true);
            return true;
        }

        for (int i = 0; i < Input.touchCount; i++)
        {
            if (Input.GetTouch(i).phase == UnityEngine.TouchPhase.Began)
            {
                ResetExitConfirm(restoreStatusText: true, markConsumedFrame: true);
                return true;
            }
        }

        if (HasPrintableMainModeInputThisFrame() ||
            IsKeyDown(KeyCode.Space) ||
            IsKeyDown(KeyCode.Delete) ||
            IsKeyDown(KeyCode.LeftArrow) ||
            IsKeyDown(KeyCode.RightArrow) ||
            IsKeyDown(KeyCode.UpArrow) ||
            IsKeyDown(KeyCode.DownArrow) ||
            IsKeyDown(KeyCode.Tab) ||
            IsKeyDown(KeyCode.Escape) ||
            IsSubmitKeyDown())
        {
            ResetExitConfirm(restoreStatusText: true, markConsumedFrame: true);
            return true;
        }

        return false;
    }

    private static bool HasPrintableMainModeInputThisFrame()
    {
        string inputString = Input.inputString;
        if (string.IsNullOrEmpty(inputString))
        {
            return false;
        }

        for (int i = 0; i < inputString.Length; i++)
        {
            if (!char.IsControl(inputString[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void HandleTouchMainModeSwipeInput()
    {
        if (!touchUiActive ||
            currentState != UIState.MainMode ||
            mainModeChatNoteActive ||
            !touchMainModeReplyShownOnce ||
            uiInputLocked)
        {
            ResetTouchMainModeSwipeTracking();
            return;
        }

        if (Input.touchCount > 0)
        {
            HandleTouchMainModeSwipeFromTouches();
            return;
        }

        HandleTouchMainModeSwipeFromMouse();
    }

    private void HandleTouchMainModeSwipeFromTouches()
    {
        if (!touchMainModeSwipeTracking)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == UnityEngine.TouchPhase.Began)
                {
                    touchMainModeSwipeTracking = true;
                    touchMainModeSwipeFingerId = touch.fingerId;
                    touchMainModeSwipeStart = touch.position;
                    break;
                }
            }
            return;
        }

        bool fingerFound = false;
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.fingerId != touchMainModeSwipeFingerId)
            {
                continue;
            }

            fingerFound = true;
            if (touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled)
            {
                Vector2 delta = touch.position - touchMainModeSwipeStart;
                ProcessTouchMainModeSwipeDelta(delta);
                ResetTouchMainModeSwipeTracking();
            }
            break;
        }

        if (!fingerFound)
        {
            ResetTouchMainModeSwipeTracking();
        }
    }

    private void HandleTouchMainModeSwipeFromMouse()
    {
        if (!touchMainModeSwipeTracking)
        {
            if (Input.GetMouseButtonDown(0))
            {
                touchMainModeSwipeTracking = true;
                touchMainModeSwipeFingerId = -1;
                touchMainModeSwipeStart = Input.mousePosition;
            }
            return;
        }

        if (Input.GetMouseButtonUp(0))
        {
            Vector2 end = Input.mousePosition;
            ProcessTouchMainModeSwipeDelta(end - touchMainModeSwipeStart);
            ResetTouchMainModeSwipeTracking();
        }
    }

    private void ProcessTouchMainModeSwipeDelta(Vector2 delta)
    {
        float horizontal = Mathf.Abs(delta.x);
        float vertical = Mathf.Abs(delta.y);
        if (horizontal < touchMainModeSwipeMinDistance || horizontal <= vertical)
        {
            return;
        }

        if (delta.x < 0f && touchMainModeTextView == TouchMainModeTextView.Transcript && touchMainModeReplyAvailable)
        {
            RequestTouchMainModeTextView(TouchMainModeTextView.Reply, animated: true, direction: 1);
        }
        else if (delta.x > 0f && touchMainModeTextView == TouchMainModeTextView.Reply)
        {
            RequestTouchMainModeTextView(TouchMainModeTextView.Transcript, animated: true, direction: -1);
        }
    }

    private void ResetTouchMainModeSwipeTracking()
    {
        touchMainModeSwipeTracking = false;
        touchMainModeSwipeFingerId = -1;
    }

    private void ResetTouchMainModeConversationUi()
    {
        touchMainModeReplyAvailable = false;
        touchMainModeReplyShownOnce = false;
        ResetTouchMainModeSwipeTracking();
        RequestTouchMainModeTextView(TouchMainModeTextView.Transcript, animated: false, direction: 0);
    }

    private void OnMainModeTranscriptUpdated()
    {
        if (!touchUiActive)
        {
            return;
        }

        touchMainModeReplyAvailable = false;
        touchMainModeReplyShownOnce = false;
        RequestTouchMainModeTextView(TouchMainModeTextView.Transcript, animated: false, direction: 0);
        if (currentState == UIState.MainMode)
        {
            UpdateHintBar(UIState.MainMode);
        }
    }

    private void OnMainModeReplyReady()
    {
        if (!touchUiActive)
        {
            return;
        }

        touchMainModeReplyAvailable = true;
        if (currentState == UIState.MainMode)
        {
            UpdateHintBar(UIState.MainMode);
        }
    }

    private void OnMainModeReplySpeechStarted()
    {
        if (mainModeReplySpeechStarted)
        {
            return;
        }

        mainModeReplySpeechStarted = true;
        StopPendingWaitPhrasePlayback(stopAudioSource: false);

        if (!touchUiActive || !touchMainModeReplyAvailable)
        {
            return;
        }

        touchMainModeReplyShownOnce = true;
        RequestTouchMainModeTextView(TouchMainModeTextView.Reply, animated: true, direction: 1);
        if (currentState == UIState.MainMode)
        {
            UpdateHintBar(UIState.MainMode);
        }
    }

    private bool EnsureTouchMainModeTextLayoutCache()
    {
        if (!touchUiActive || mainModeTranscriptText == null || mainModeReplyText == null)
        {
            return false;
        }

        if (touchMainModeTextLayoutCached)
        {
            return true;
        }

        var transcriptRect = mainModeTranscriptText.rectTransform;
        var replyRect = mainModeReplyText.rectTransform;
        if (transcriptRect == null || replyRect == null)
        {
            return false;
        }

        touchMainModeTranscriptGroup = GetOrAddCanvasGroup(mainModeTranscriptText.gameObject);
        touchMainModeReplyGroup = GetOrAddCanvasGroup(mainModeReplyText.gameObject);
        touchMainModeTranscriptDefaultPos = transcriptRect.anchoredPosition;
        touchMainModeReplyDefaultPos = replyRect.anchoredPosition;
        touchMainModeTextLayoutCached = true;
        return true;
    }

    private void RequestTouchMainModeTextView(TouchMainModeTextView target, bool animated, int direction)
    {
        if (!EnsureTouchMainModeTextLayoutCache())
        {
            return;
        }

        if (!touchMainModeReplyAvailable && target == TouchMainModeTextView.Reply)
        {
            target = TouchMainModeTextView.Transcript;
        }

        if (touchMainModeTextSwitchRoutine != null)
        {
            StopCoroutine(touchMainModeTextSwitchRoutine);
            touchMainModeTextSwitchRoutine = null;
        }

        bool canShowText = !debugUiHidden && !mainModeChatNoteActive;
        if (!animated || !canShowText || target == touchMainModeTextView)
        {
            SetTouchMainModeTextViewImmediate(target);
            return;
        }

        touchMainModeTextSwitchRoutine = StartCoroutine(AnimateTouchMainModeTextViewSwitch(target, direction));
    }

    private IEnumerator AnimateTouchMainModeTextViewSwitch(TouchMainModeTextView target, int direction)
    {
        if (!EnsureTouchMainModeTextLayoutCache())
        {
            touchMainModeTextSwitchRoutine = null;
            yield break;
        }

        bool toReply = target == TouchMainModeTextView.Reply;
        var fromText = toReply ? mainModeTranscriptText : mainModeReplyText;
        var toText = toReply ? mainModeReplyText : mainModeTranscriptText;
        if (fromText == null || toText == null)
        {
            SetTouchMainModeTextViewImmediate(target);
            touchMainModeTextSwitchRoutine = null;
            yield break;
        }

        var fromRect = fromText.rectTransform;
        var toRect = toText.rectTransform;
        if (fromRect == null || toRect == null)
        {
            SetTouchMainModeTextViewImmediate(target);
            touchMainModeTextSwitchRoutine = null;
            yield break;
        }

        CanvasGroup fromGroup = toReply ? touchMainModeTranscriptGroup : touchMainModeReplyGroup;
        CanvasGroup toGroup = toReply ? touchMainModeReplyGroup : touchMainModeTranscriptGroup;
        Vector2 fromDefault = toReply ? touchMainModeTranscriptDefaultPos : touchMainModeReplyDefaultPos;
        Vector2 toDefault = toReply ? touchMainModeReplyDefaultPos : touchMainModeTranscriptDefaultPos;

        float sign = direction >= 0 ? 1f : -1f;
        float offset = Mathf.Max(0f, touchMainModeTextSwitchOffset) * sign;

        fromText.gameObject.SetActive(true);
        toText.gameObject.SetActive(true);
        fromRect.anchoredPosition = fromDefault;
        toRect.anchoredPosition = toDefault + new Vector2(offset, 0f);
        if (fromGroup != null)
        {
            fromGroup.alpha = 1f;
            fromGroup.interactable = false;
            fromGroup.blocksRaycasts = false;
        }
        if (toGroup != null)
        {
            toGroup.alpha = 0f;
            toGroup.interactable = false;
            toGroup.blocksRaycasts = false;
        }

        float duration = Mathf.Max(0f, touchMainModeTextSwitchDuration);
        if (duration <= 0f)
        {
            SetTouchMainModeTextViewImmediate(target);
            touchMainModeTextSwitchRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            fromRect.anchoredPosition = Vector2.LerpUnclamped(fromDefault, fromDefault + new Vector2(-offset, 0f), t);
            toRect.anchoredPosition = Vector2.LerpUnclamped(toDefault + new Vector2(offset, 0f), toDefault, t);
            if (fromGroup != null)
            {
                fromGroup.alpha = Mathf.Lerp(1f, 0f, t);
            }
            if (toGroup != null)
            {
                toGroup.alpha = Mathf.Lerp(0f, 1f, t);
            }

            yield return null;
        }

        SetTouchMainModeTextViewImmediate(target);
        touchMainModeTextSwitchRoutine = null;
    }

    private void SetTouchMainModeTextViewImmediate(TouchMainModeTextView target)
    {
        if (!EnsureTouchMainModeTextLayoutCache())
        {
            return;
        }

        bool canShowText = !debugUiHidden && !mainModeChatNoteActive;
        if (!touchMainModeReplyAvailable && target == TouchMainModeTextView.Reply)
        {
            target = TouchMainModeTextView.Transcript;
        }

        bool showTranscript = target == TouchMainModeTextView.Transcript;
        if (!canShowText)
        {
            if (mainModeTranscriptText != null)
            {
                mainModeTranscriptText.gameObject.SetActive(false);
            }
            if (mainModeReplyText != null)
            {
                mainModeReplyText.gameObject.SetActive(false);
            }
            return;
        }

        var transcriptRect = mainModeTranscriptText.rectTransform;
        var replyRect = mainModeReplyText.rectTransform;
        transcriptRect.anchoredPosition = touchMainModeTranscriptDefaultPos;
        replyRect.anchoredPosition = touchMainModeReplyDefaultPos;

        if (touchMainModeTranscriptGroup != null)
        {
            touchMainModeTranscriptGroup.alpha = showTranscript ? 1f : 0f;
            touchMainModeTranscriptGroup.interactable = false;
            touchMainModeTranscriptGroup.blocksRaycasts = false;
        }
        if (touchMainModeReplyGroup != null)
        {
            touchMainModeReplyGroup.alpha = showTranscript ? 0f : 1f;
            touchMainModeReplyGroup.interactable = false;
            touchMainModeReplyGroup.blocksRaycasts = false;
        }

        mainModeTranscriptText.gameObject.SetActive(showTranscript);
        mainModeReplyText.gameObject.SetActive(!showTranscript);
        touchMainModeTextView = showTranscript ? TouchMainModeTextView.Transcript : TouchMainModeTextView.Reply;
    }

    private void UpdateTouchAvatarDimming()
    {
        if (avatarManager == null)
        {
            return;
        }

        bool shouldDim = touchUiActive &&
                         currentState == UIState.MainMode &&
                         mainModeChatNoteActive &&
                         (IsInputFieldFocused(chatNoteInput) || TouchScreenKeyboard.visible);
        avatarManager.SetCurrentAvatarDimmed(shouldDim, touchChatAvatarDimMultiplier);
    }

    // Chat Note: mostra / nascondi / invia

    private CanvasGroup GetOrCreateChatNoteCanvasGroup()
    {
        if (chatNoteCanvasGroup != null) return chatNoteCanvasGroup;
        if (chatNoteInput == null) return null;
        chatNoteCanvasGroup = GetOrAddCanvasGroup(chatNoteInput.gameObject);
        return chatNoteCanvasGroup;
    }

    private void HideChatNoteImmediate()
    {
        if (chatNoteInput == null) return;
        var group = GetOrCreateChatNoteCanvasGroup();
        if (group != null)
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        chatNoteInput.gameObject.SetActive(false);
        chatNoteInput.text = string.Empty;
        mainModeChatNoteActive = false;
        UpdateTouchAvatarDimming();
        ResetTouchMainModeSwipeTracking();
        ApplyMainModeDebugVisibility();
        UpdateTouchMainModeActionButtonsVisibility();
    }

    private void ShowChatNote(char initialChar)
    {
        ShowChatNote(initialChar.ToString());
    }

    private void ShowChatNote(string initialText)
    {
        if (chatNoteInput == null) return;

        mainModeChatNoteActive = true;

        // Nascondiamo trascrizione e risposta.
        ApplyMainModeDebugVisibility();
        UpdateMainModeStatus("Digitando...");

        // Prepariamo la chat.
        chatNoteInput.onFocusSelectAll = false;
        chatNoteInput.text = initialText ?? string.Empty;
        chatNoteInput.gameObject.SetActive(true);
        FocusChatNoteWithoutSelection();

        // Dissolvenza in entrata.
        if (chatNoteTransitionRoutine != null) StopCoroutine(chatNoteTransitionRoutine);
        chatNoteTransitionRoutine = StartCoroutine(FadeChatNote(true, () =>
        {
            FocusChatNoteWithoutSelection();
            UpdateTouchAvatarDimming();
        }));

        UpdateTouchAvatarDimming();
        ResetTouchMainModeSwipeTracking();
        UpdateHintBar(UIState.MainMode);
        ConfigureNavigatorForState(UIState.MainMode, true);
        UpdateTouchMainModeActionButtonsVisibility();
    }

    private void DismissChatNote()
    {
        if (chatNoteInput == null) return;

        mainModeChatNoteActive = false;
        chatNoteJustDismissed = true;
        StartCoroutine(ClearChatNoteDismissFlag());

        // Dissolvenza in uscita.
        if (chatNoteTransitionRoutine != null) StopCoroutine(chatNoteTransitionRoutine);
        chatNoteTransitionRoutine = StartCoroutine(FadeChatNote(false, () =>
        {
            if (chatNoteInput != null)
            {
                chatNoteInput.text = string.Empty;
                chatNoteInput.gameObject.SetActive(false);
            }
            UpdateTouchAvatarDimming();
        }));

        // Ripristiniamo trascrizione e risposta.
        UpdateTouchAvatarDimming();
        ResetTouchMainModeSwipeTracking();
        ApplyMainModeDebugVisibility();
        UpdateMainModeStatus(touchUiActive ? "Tieni premuto PTT per parlare." : "Tieni premuto SPACE per parlare.");
        UpdateTouchMainModeActionButtonsVisibility();

        UpdateHintBar(UIState.MainMode);
        ConfigureNavigatorForState(UIState.MainMode, true);

        // Rimettiamo il focus su un pulsante.
        if (btnMainModeMemory != null) btnMainModeMemory.Select();
        else if (btnMainModeVoice != null) btnMainModeVoice.Select();
    }

    private void SubmitChatNote()
    {
        if (chatNoteInput == null) return;
        string text = chatNoteInput.text;
        if (string.IsNullOrWhiteSpace(text))
        {
            DismissChatNote();
            return;
        }

        // Nascondi il campo e processa come testo
        mainModeChatNoteActive = false;
        if (chatNoteTransitionRoutine != null) StopCoroutine(chatNoteTransitionRoutine);
        chatNoteTransitionRoutine = StartCoroutine(FadeChatNote(false, () =>
        {
            if (chatNoteInput != null)
            {
                chatNoteInput.text = string.Empty;
                chatNoteInput.gameObject.SetActive(false);
            }
            UpdateTouchAvatarDimming();
        }));

        // Ripristiniamo trascrizione e risposta.
        UpdateTouchAvatarDimming();
        ResetTouchMainModeSwipeTracking();
        ApplyMainModeDebugVisibility();
        UpdateTouchMainModeActionButtonsVisibility();

        UpdateHintBar(UIState.MainMode);
        ConfigureNavigatorForState(UIState.MainMode, true);

        // Avviamo la pipeline testo (salta Whisper e va diretta a RAG).
        if (mainModeRoutine != null) StopCoroutine(mainModeRoutine);
        mainModeRoutine = StartCoroutine(ProcessMainModeTextPipeline(text.Trim()));
    }

    private IEnumerator ClearChatNoteDismissFlag()
    {
        // Aspettiamo un frame per evitare che il backspace che svuota il campo
        // venga intercettato anche dal navigator come "GoBack".
        yield return null;
        yield return null;          // Ne usiamo due: ÃƒÂ¨ utile!
        chatNoteJustDismissed = false;
    }

    private IEnumerator FadeChatNote(bool show, System.Action onComplete = null)
    {
        var group = GetOrCreateChatNoteCanvasGroup();
        if (group == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        float from = show ? 0f : 1f;
        float to = show ? 1f : 0f;

        if (show)
        {
            group.alpha = 0f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }

        float duration = Mathf.Max(0f, chatNoteTransitionDuration);
        if (duration <= 0f)
        {
            group.alpha = to;
            group.interactable = show;
            group.blocksRaycasts = show;
            onComplete?.Invoke();
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            group.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        group.alpha = to;
        group.interactable = show;
        group.blocksRaycasts = show;
        onComplete?.Invoke();
    }

    private void FocusChatNoteWithoutSelection()
    {
        if (chatNoteInput == null)
        {
            return;
        }

        chatNoteInput.onFocusSelectAll = false;
        chatNoteInput.Select();
        chatNoteInput.ActivateInputField();
        chatNoteInput.MoveTextEnd(false);

        int end = chatNoteInput.text != null ? chatNoteInput.text.Length : 0;
        chatNoteInput.caretPosition = end;
        chatNoteInput.selectionAnchorPosition = end;
        chatNoteInput.selectionFocusPosition = end;
    }

    private IEnumerator ProcessMainModeTextPipeline(string userText)
    {
        mainModeProcessing = true;

        ApplyMainModeTranscript(userText);

        yield return StartCoroutine(RequestAndSpeakMainModeReply(userText, "keyboard"));
    }

    private IEnumerator SpeakMainModeReply(string reply)
    {
        ResetPendingMainModeTtsStreamState();
        UpdateMainModeStatus("Sto parlando...");
        mainModeSpeaking = true;
        mainModeTtsInterruptedByUser = false;
        StartMainModeReplyFlow(reply);
        Coroutine ttsReplyRoutine = StartCoroutine(PlayTtsReply(reply));
        yield return StartCoroutine(WaitForMainModeReplyAudioStart());

        yield return ttsReplyRoutine;
        ttsAcceptIncomingSamples = false;
        ttsActiveSessionId = -1;
        StopMainModeReplyFlow(finalizeText: !mainModeTtsInterruptedByUser);
        mainModeSpeaking = false;
    }

    private void ResetPendingMainModeTtsStreamState()
    {
        ttsStreamError = null;
        ttsStreamBytes = 0;
        ttsStreamSampleRate = 0;
        ttsStreamChannels = 0;
        ttsChunkPlayer?.ResetForPendingStream();
    }

    private IEnumerator WaitForMainModeReplyAudioStart()
    {
        while (mainModeSpeaking &&
               IsTtsSessionActive(ttsActiveSessionId) &&
               string.IsNullOrEmpty(ttsStreamError))
        {
            if (ttsStreamBytes > 0 && HasMainModeReplyAudioStarted())
            {
                OnMainModeReplySpeechStarted();
                yield break;
            }

            if (TryActivateMainModeReplyStaticFallback())
            {
                if (mainModeReplyText != null)
                {
                    SetTmpTextIfChanged(mainModeReplyText, EscapeForTmpRichText(mainModeReplyFullText));
                }

                LogMainModeReplyFirstReveal("static-fallback");
                OnMainModeReplySpeechStarted();
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator RequestAndSpeakMainModeReply(string userText, string inputMode)
    {
        if (servicesConfig == null)
        {
            FailMainModeProcessing("ServicesConfig mancante.");
            yield break;
        }

        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        string safeAvatarId = string.IsNullOrEmpty(avatarId) ? "default" : avatarId;

        if (!string.IsNullOrEmpty(mainModeConversationAvatarId) &&
            !string.Equals(mainModeConversationAvatarId, safeAvatarId, StringComparison.Ordinal))
        {
            Debug.Log($"[UIFlowController] Avatar mismatch before chat send oldAvatar={mainModeConversationAvatarId} newAvatar={safeAvatarId}");
            NotifyMainAvatarActivated(avatarId, stopActiveMainModeFlow: false);
        }

        yield return StartCoroutine(EnsureMainModeConversationSessionRoutine());

        UpdateMainModeStatus("Sto pensando...");
        TriggerMainModeWaitPhrase();
        string reply = null;
        string ragError = null;
        string sessionId = mainModeConversationSessionId;
        Debug.Log($"[UIFlowController] Sending MainMode chat avatar={safeAvatarId} session={sessionId ?? "<null>"} input={inputMode}");
        var payload = JsonUtility.ToJson(new RagChatPayload
        {
            avatar_id = safeAvatarId,
            user_text = userText,
            top_k = 8,
            session_id = sessionId,
            input_mode = inputMode,
            log_conversation = true,
            empirical_test_mode = empiricalTestModeEnabled
        });

        bool autoRemembered = false;
        yield return StartCoroutine(PostJsonWithRetry(
            BuildServiceUrlWithEmpiricalMode(servicesConfig.ragBaseUrl, "chat"),
            payload,
            "RAG",
            (RagChatResponse response) =>
            {
                reply = SanitizeMainModeReply(response != null ? response.text : null);
                autoRemembered = response != null && response.auto_remembered;
            },
            error => ragError = error,
            maxRetries: 3
        ));

        if (!string.IsNullOrEmpty(ragError))
        {
            FailMainModeProcessing($"Errore RAG: {ragError}");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(reply))
        {
            FailMainModeProcessing("Risposta vuota.");
            yield break;
        }

        PrepareMainModeReplyForSpeech(reply);
        OnMainModeReplyReady();

        if (autoRemembered)
        {
            Debug.Log("[UIFlowController] Auto-remember attivato: memoria salvata automaticamente.");
        }

        yield return StartCoroutine(SpeakMainModeReply(reply));
        UpdateMainModePostReplyStatus();
        mainModeProcessing = false;
    }

    private void UpdateMainModePostReplyStatus()
    {
        if (!string.IsNullOrEmpty(ttsStreamError))
        {
            return;
        }

        if (mainModeTtsInterruptedByUser)
        {
            if (!mainModeListening && !mainModeProcessing && !mainModeSpeaking && currentState == UIState.MainMode)
            {
                UpdateMainModeStatus("Risposta interrotta.");
            }
            return;
        }

        UpdateMainModeStatus(touchUiActive ? "Tieni premuto PTT per parlare." : "Tieni premuto SPACE per parlare.");
    }

    private void FailMainModeProcessing(string message)
    {
        StopPendingWaitPhrasePlayback(stopAudioSource: true);
        UpdateMainModeStatus(message);
        PlayErrorClip();
        mainModeProcessing = false;
    }

    private void ApplyMainModeTranscript(string transcript)
    {
        ClearMainModeReplyDisplay();
        SetTmpTextIfChanged(mainModeTranscriptText, transcript ?? string.Empty);

        OnMainModeTranscriptUpdated();
    }

    private void PrepareMainModeReplyForSpeech(string reply)
    {
        StopMainModeReplyFlow(finalizeText: false);
        mainModeReplyFullText = reply ?? string.Empty;

        if (mainModeReplyText == null)
        {
            return;
        }

        SetTmpTextIfChanged(mainModeReplyText, string.Empty);
    }

    private void StartMainModeReplyFlow(string reply)
    {
        StopMainModeReplyFlow(finalizeText: false);
        mainModeReplyFullText = reply ?? string.Empty;
        mainModeReplyTtsRequestId = null;
        mainModeReplyTimingState = MainModeReplyTimingResolutionState.Pending;
        mainModeReplyTimingComplete = false;
        mainModeReplyTimingRetryCount = 0;
        mainModeReplyLastResolvedWordIndex = -1;
        mainModeReplyUseStaticFallback = false;
        mainModeReplySpeechStarted = false;
        mainModeReplyLoggedFirstStreamByte = false;
        mainModeReplyLoggedFirstChunk = false;
        mainModeReplyLoggedStableClock = false;
        mainModeReplyLoggedFirstReveal = false;

        if (mainModeReplyText == null)
        {
            return;
        }

        if (!enableMainModeReplyWordFlow)
        {
            SetTmpTextIfChanged(mainModeReplyText, string.Empty);
            return;
        }

        if (IsWebGlReplyWordFlowDisabled())
        {
            mainModeReplyUseStaticFallback = true;
            mainModeReplyTimingState = MainModeReplyTimingResolutionState.Unavailable;
            mainModeReplyTimingComplete = true;
            SetTmpTextIfChanged(mainModeReplyText, EscapeForTmpRichText(mainModeReplyFullText));
            return;
        }

        if (!BuildMainModeReplyTimeline(mainModeReplyFullText, mainModeReplyTokens, mainModeReplyWords))
        {
            SetTmpTextIfChanged(mainModeReplyText, string.Empty);
            return;
        }

        if (empiricalTestModeEnabled && IsWebGlReplyWordFlowDisabled())
        {
            mainModeReplyUseStaticFallback = true;
            mainModeReplyTimingState = MainModeReplyTimingResolutionState.Unavailable;
            mainModeReplyTimingComplete = true;
            SetTmpTextIfChanged(mainModeReplyText, EscapeForTmpRichText(mainModeReplyFullText));
            return;
        }

        SetTmpTextIfChanged(mainModeReplyText, string.Empty);
        mainModeReplyFlowRoutine = StartCoroutine(MainModeReplyFlowRoutine());
    }

    private void StopMainModeReplyFlow(bool finalizeText)
    {
        if (mainModeReplyFlowRoutine != null)
        {
            StopCoroutine(mainModeReplyFlowRoutine);
            mainModeReplyFlowRoutine = null;
        }

        if (mainModeReplyTimingPollRoutine != null)
        {
            StopCoroutine(mainModeReplyTimingPollRoutine);
            mainModeReplyTimingPollRoutine = null;
        }

        if (mainModeReplyText != null)
        {
            if (finalizeText)
            {
                SetTmpTextIfChanged(mainModeReplyText, EscapeForTmpRichText(mainModeReplyFullText));
            }
            else
            {
                SetTmpTextIfChanged(mainModeReplyText, string.Empty);
            }
        }

        mainModeReplyTokens.Clear();
        mainModeReplyWords.Clear();
        mainModeReplyWordEndMs.Clear();
        mainModeReplySegmentEndMs.Clear();
        mainModeReplySegmentEndWordIndices.Clear();
        mainModeReplyTtsRequestId = null;
        mainModeReplyTimingState = MainModeReplyTimingResolutionState.Pending;
        mainModeReplyTimingComplete = false;
        mainModeReplyTimingRetryCount = 0;
        mainModeReplyLastResolvedWordIndex = -1;
        mainModeReplyUseStaticFallback = false;
        mainModeReplySpeechStarted = false;
        mainModeReplyLoggedFirstStreamByte = false;
        mainModeReplyLoggedFirstChunk = false;
        mainModeReplyLoggedStableClock = false;
        mainModeReplyLoggedFirstReveal = false;
    }

    private IEnumerator MainModeReplyFlowRoutine()
    {
        if (mainModeReplyWords.Count == 0 || mainModeReplyText == null)
        {
            mainModeReplyFlowRoutine = null;
            yield break;
        }

        int lastWordIndex = -1;
        bool lastCaretVisible = false;
        float nextUiUpdateAt = 0f;
        while (mainModeSpeaking && currentState == UIState.MainMode)
        {
            MaybeLogMainModeReplyStableClock();

            if (TryActivateMainModeReplyStaticFallback())
            {
                EnsureMainModeReplyStaticText("static-fallback");

                if (IsTtsStreamDrained() || !string.IsNullOrEmpty(ttsStreamError))
                {
                    break;
                }

                yield return null;
                continue;
            }

            if (!HasMainModeReplyAudioStarted())
            {
                ClearMainModeReplyProgressDisplay(ref lastWordIndex, ref lastCaretVisible);

                yield return null;
                continue;
            }

            if (mainModeReplyTimingState == MainModeReplyTimingResolutionState.Pending)
            {
                bool pendingRevealComplete = false;
                if (mainModeReplyWords.Count > 0)
                {
                    TryUpdateMainModeReplyWindow(
                        0,
                    pendingRevealComplete,
                        "word-flow-pending",
                        ref lastWordIndex,
                        ref lastCaretVisible,
                        forceRefresh: false);
                }

                if (IsTtsStreamDrained() && mainModeReplyTimingComplete)
                {
                    EnsureMainModeReplyStaticText("static-no-timing");
                    break;
                }

                yield return null;
                continue;
            }

            int currentWordIndex = EvaluateCurrentWordIndex();
            if (currentWordIndex < 0)
            {
                if (mainModeReplyTimingState == MainModeReplyTimingResolutionState.Unavailable)
                {
                    EnsureMainModeReplyStaticText("static-no-timing");
                    if (IsTtsStreamDrained() || !string.IsNullOrEmpty(ttsStreamError) || mainModeReplyTimingComplete)
                    {
                        break;
                    }

                    yield return null;
                    continue;
                }

                ClearMainModeReplyProgressDisplay(ref lastWordIndex, ref lastCaretVisible);

                yield return null;
                continue;
            }

            bool revealComplete = IsMainModeReplyRevealComplete(currentWordIndex);
            if (ShouldUpdateMainModeReplyUi(currentWordIndex, revealComplete, lastWordIndex, lastCaretVisible, ref nextUiUpdateAt))
            {
                TryUpdateMainModeReplyWindow(
                    currentWordIndex,
                    revealComplete,
                    "word-flow-timed",
                    ref lastWordIndex,
                    ref lastCaretVisible,
                    forceRefresh: true);
            }

            if (revealComplete)
            {
                break;
            }

            yield return null;
        }

        mainModeReplyFlowRoutine = null;
    }

    private void ClearMainModeReplyProgressDisplay(ref int lastWordIndex, ref bool lastCaretVisible)
    {
        if (lastWordIndex == -1 && !lastCaretVisible)
        {
            return;
        }

        SetTmpTextIfChanged(mainModeReplyText, string.Empty);
        lastWordIndex = -1;
        lastCaretVisible = false;
    }

    private void EnsureMainModeReplyStaticText(string revealMode)
    {
        string fullReply = EscapeForTmpRichText(mainModeReplyFullText);
        if (mainModeReplyText == null || string.Equals(mainModeReplyText.text, fullReply, StringComparison.Ordinal))
        {
            return;
        }

        SetTmpTextIfChanged(mainModeReplyText, fullReply);
        LogMainModeReplyFirstReveal(revealMode);
    }

    private bool IsMainModeReplyCaretVisible(bool revealComplete)
    {
        return replyShowTrailingCaret &&
               !revealComplete &&
               (((int)(Time.unscaledTime / Mathf.Max(0.1f, replyCaretBlinkSeconds))) % 2 == 0);
    }

    private bool TryUpdateMainModeReplyWindow(
        int currentWordIndex,
        bool revealComplete,
        string revealMode,
        ref int lastWordIndex,
        ref bool lastCaretVisible,
        bool forceRefresh)
    {
        bool caretVisible = IsMainModeReplyCaretVisible(revealComplete);
        bool changed = currentWordIndex != lastWordIndex || caretVisible != lastCaretVisible;
        if (!changed && !forceRefresh && !revealComplete)
        {
            return false;
        }

        SetTmpTextIfChanged(mainModeReplyText, BuildMainModeReplyWindowText(currentWordIndex, caretVisible));
        LogMainModeReplyFirstReveal(revealMode);
        lastWordIndex = currentWordIndex;
        lastCaretVisible = caretVisible;
        return true;
    }

    private bool HasMainModeReplyAudioStarted()
    {
        if (!IsTtsSessionActive(ttsActiveSessionId) || ttsStreamBytes <= 0)
        {
            return false;
        }

        return ttsChunkPlayer != null &&
               (ttsChunkPlayer.HasStableClockStarted || ttsChunkPlayer.HasPlaybackStarted);
    }

    private bool BuildMainModeReplyTimeline(
        string fullText,
        List<ReplyTokenInfo> outTokens,
        List<ReplyWordSpan> outWords)
    {
        outTokens.Clear();
        outWords.Clear();

        if (string.IsNullOrWhiteSpace(fullText))
        {
            return false;
        }

        string sourceText = fullText ?? string.Empty;
        if (sourceText.Length == 0)
        {
            return false;
        }

        int wordIndex = -1;
        int cursor = 0;
        while (cursor < sourceText.Length)
        {
            if (IsReplyWordLetter(sourceText[cursor]))
            {
                int start = cursor;
                cursor++;
                while (cursor < sourceText.Length)
                {
                    char c = sourceText[cursor];
                    if (IsReplyWordLetter(c))
                    {
                        cursor++;
                        continue;
                    }

                    if (IsReplyWordApostrophe(c) &&
                        cursor > start &&
                        cursor + 1 < sourceText.Length &&
                        IsReplyWordLetter(sourceText[cursor + 1]))
                    {
                        cursor++;
                        continue;
                    }

                    break;
                }

                string token = sourceText.Substring(start, cursor - start);
                string normalizedToken = NormalizeReplyWordToken(token);
                bool isWord = !string.IsNullOrEmpty(normalizedToken);
                int tokenIndex = outTokens.Count;
                if (isWord)
                {
                    wordIndex++;
                    outWords.Add(new ReplyWordSpan
                    {
                        tokenIndex = tokenIndex,
                        normalizedToken = normalizedToken
                    });
                }

                outTokens.Add(new ReplyTokenInfo
                {
                    text = token,
                    isWord = isWord,
                    wordIndex = isWord ? wordIndex : -1
                });

                continue;
            }

            int separatorStart = cursor;
            cursor++;
            while (cursor < sourceText.Length && !IsReplyWordLetter(sourceText[cursor]))
            {
                cursor++;
            }

            outTokens.Add(new ReplyTokenInfo
            {
                text = sourceText.Substring(separatorStart, cursor - separatorStart),
                isWord = false,
                wordIndex = -1
            });
        }

        return outWords.Count > 0;
    }

    private int EvaluateCurrentWordIndex()
    {
        if (mainModeReplyWords.Count == 0)
        {
            return -1;
        }

        if (TryEvaluateCurrentWordIndexFromServerTiming(out int timedWordIndex))
        {
            return timedWordIndex;
        }

        return -1;
    }

    private bool ShouldUpdateMainModeReplyUi(
        int currentWordIndex,
        bool revealComplete,
        int lastWordIndex,
        bool lastCaretVisible,
        ref float nextUiUpdateAt)
    {
        bool caretVisible = IsMainModeReplyCaretVisible(revealComplete);
        float now = Time.unscaledTime;
        bool firstVisibleFrame = lastWordIndex < 0 && currentWordIndex >= 0;
        bool timerElapsed = now >= nextUiUpdateAt;
        bool wordChanged = currentWordIndex != lastWordIndex;
        bool caretChanged = caretVisible != lastCaretVisible;

        if (!revealComplete && !firstVisibleFrame)
        {
            if (!wordChanged)
            {
                if (!caretChanged || !timerElapsed)
                {
                    return false;
                }
            }
            else if (!timerElapsed)
            {
                return false;
            }
        }

        float nextInterval = wordChanged
            ? Mathf.Max(0.02f, replyWordFlowUiUpdateIntervalSeconds)
            : Mathf.Max(Mathf.Max(0.02f, replyWordFlowUiUpdateIntervalSeconds), replyCaretBlinkSeconds * 0.5f);
        nextUiUpdateAt = now + nextInterval;
        return true;
    }

    private bool HasMainModeReplyServerTiming()
    {
        return (mainModeReplyTimingState == MainModeReplyTimingResolutionState.Available ||
                mainModeReplyTimingState == MainModeReplyTimingResolutionState.Partial) &&
               mainModeReplyWords.Count > 0 &&
               mainModeReplyWordEndMs.Count > 0 &&
               mainModeReplyWordEndMs.Count <= mainModeReplyWords.Count &&
               (mainModeReplySegmentEndMs.Count == 0 ||
                mainModeReplySegmentEndMs.Count == mainModeReplySegmentEndWordIndices.Count);
    }

    private bool TryEvaluateCurrentWordIndexFromServerTiming(out int wordIndex)
    {
        wordIndex = -1;

        int availableWordCount = Mathf.Min(mainModeReplyWordEndMs.Count, mainModeReplyWords.Count);
        if (availableWordCount > 0 && ttsChunkPlayer != null)
        {
            int playedWordMs = Mathf.Clamp(
                Mathf.RoundToInt(ttsChunkPlayer.PlayedAudioSeconds * 1000f),
                0,
                mainModeReplyWordEndMs[availableWordCount - 1]);
            int currentWordIndex = Mathf.Clamp(mainModeReplyLastResolvedWordIndex, 0, availableWordCount - 1);
            while (currentWordIndex < availableWordCount - 1 &&
                   playedWordMs >= mainModeReplyWordEndMs[currentWordIndex])
            {
                currentWordIndex++;
            }

            wordIndex = Mathf.Clamp(currentWordIndex, 0, availableWordCount - 1);
            mainModeReplyLastResolvedWordIndex = wordIndex;
            return true;
        }

        return false;
    }

    private bool IsMainModeReplyRevealComplete(int currentWordIndex)
    {
        if (mainModeReplyWords.Count == 0 ||
            currentWordIndex < 0 ||
            ttsChunkPlayer == null)
        {
            return false;
        }

        if (mainModeReplyTimingState != MainModeReplyTimingResolutionState.Available ||
            mainModeReplyWordEndMs.Count != mainModeReplyWords.Count ||
            mainModeReplyWordEndMs.Count == 0)
        {
            return false;
        }

        int lastWordEndMs = mainModeReplyWordEndMs[mainModeReplyWordEndMs.Count - 1];
        int playedMs = Mathf.Max(0, Mathf.RoundToInt(ttsChunkPlayer.PlayedAudioSeconds * 1000f));
        return currentWordIndex >= mainModeReplyWords.Count - 1 &&
               (playedMs >= lastWordEndMs || IsTtsStreamDrained());
    }

    private bool TryActivateMainModeReplyStaticFallback()
    {
        if (mainModeReplyUseStaticFallback || !enableMainModeReplyWordFlow || mainModeReplyWords.Count == 0)
        {
            return mainModeReplyUseStaticFallback;
        }

        if (ttsChunkPlayer == null || !IsTtsSessionActive(ttsActiveSessionId))
        {
            return false;
        }

        // La wait phrase usa lo stesso AudioSource del lip sync: finche' il reply
        // non ha ricevuto byte reali dal proprio stream, non dobbiamo armare il
        // fallback o considerare iniziato il parlato del reply.
        if (ttsStreamBytes <= 0)
        {
            return false;
        }

        if (IsWaitPhraseCurrentlyBlockingReply())
        {
            return false;
        }

        if (ttsChunkPlayer.HasStableClockStarted || ttsChunkPlayer.HasPlaybackStarted)
        {
            return false;
        }

        bool shouldFallbackToStatic = mainModeReplyTimingState == MainModeReplyTimingResolutionState.Unavailable &&
                                      (mainModeReplyTimingComplete ||
                                       mainModeReplyTimingRetryCount >= backendTimingPollMaxRetries ||
                                       (!string.IsNullOrEmpty(ttsStreamError) && IsTtsStreamDrained()));
        if (!shouldFallbackToStatic)
        {
            return false;
        }

        mainModeReplyUseStaticFallback = true;
        if (enableTtsWebGlStreamingLogs)
        {
            Debug.Log("[UIFlowController] Native TTS reply flow fallback -> static text (stream-drained).");
        }

        return true;
    }

    private void MaybeLogMainModeReplyStableClock()
    {
        if (!enableTtsWebGlStreamingLogs || mainModeReplyLoggedStableClock || ttsChunkPlayer == null || !ttsChunkPlayer.HasStableClockStarted)
        {
            return;
        }

        mainModeReplyLoggedStableClock = true;
        Debug.Log($"[UIFlowController] Native TTS first stable clock (played={ttsChunkPlayer.PlayedAudioSeconds:0.000}s).");
    }

    private void LogMainModeReplyFirstReveal(string mode)
    {
        if (!enableTtsWebGlStreamingLogs || mainModeReplyLoggedFirstReveal)
        {
            return;
        }

        mainModeReplyLoggedFirstReveal = true;
        Debug.Log($"[UIFlowController] Native TTS first reveal update ({mode}).");
    }

    private string BuildMainModeReplyWindowText(int currentWordIndex, bool caretVisible)
    {
        if (mainModeReplyWords.Count == 0 || mainModeReplyTokens.Count == 0)
        {
            return EscapeForTmpRichText(mainModeReplyFullText);
        }

        int totalWords = mainModeReplyWords.Count;
        int clampedWord = Mathf.Clamp(currentWordIndex, 0, totalWords - 1);
        int maxWindow = GetMainModeReplyWindowWords();
        int lookAhead = Mathf.Clamp(replyLookAheadWords, 0, Mathf.Max(0, maxWindow - 1));

        int endWord = Mathf.Min(totalWords - 1, clampedWord + lookAhead);
        int segmentEndWord = GetMainModeReplySegmentEndWordIndex(clampedWord);
        if (segmentEndWord >= 0)
        {
            endWord = Mathf.Min(endWord, segmentEndWord);
        }

        int startWord = Mathf.Max(0, endWord - maxWindow + 1);
        if (clampedWord < startWord)
        {
            startWord = clampedWord;
        }

        int startToken = mainModeReplyWords[startWord].tokenIndex;
        int endToken = mainModeReplyWords[endWord].tokenIndex;

        string pastColor = ColorUtility.ToHtmlStringRGBA(replyPastWordColor);
        string currentColor = ColorUtility.ToHtmlStringRGBA(replyCurrentWordColor);
        string futureColor = ColorUtility.ToHtmlStringRGBA(replyFutureWordColor);

        var sb = new StringBuilder(256);
        for (int tokenIndex = startToken; tokenIndex <= endToken && tokenIndex < mainModeReplyTokens.Count; tokenIndex++)
        {
            ReplyTokenInfo token = mainModeReplyTokens[tokenIndex];
            string escaped = EscapeForTmpRichText(token.text);
            if (!token.isWord)
            {
                string separatorColor = GetMainModeReplyTokenColorHex(tokenIndex, clampedWord, pastColor, currentColor, futureColor);
                if (!string.IsNullOrEmpty(separatorColor))
                {
                    sb.Append("<color=#").Append(separatorColor).Append(">").Append(escaped).Append("</color>");
                }
                else
                {
                    sb.Append(escaped);
                }
                continue;
            }

            if (token.wordIndex < clampedWord)
            {
                sb.Append("<color=#").Append(pastColor).Append(">").Append(escaped).Append("</color>");
            }
            else if (token.wordIndex == clampedWord)
            {
                sb.Append("<color=#").Append(currentColor).Append(">").Append(escaped).Append("</color>");
            }
            else
            {
                sb.Append("<color=#").Append(futureColor).Append(">").Append(escaped).Append("</color>");
            }
        }

        if (caretVisible)
        {
            sb.Append(" <color=#").Append(currentColor).Append(">|</color>");
        }

        return sb.ToString();
    }

    private string GetMainModeReplyTokenColorHex(
        int tokenIndex,
        int currentWordIndex,
        string pastColor,
        string currentColor,
        string futureColor)
    {
        if (tokenIndex < 0 || tokenIndex >= mainModeReplyTokens.Count)
        {
            return futureColor;
        }

        for (int i = tokenIndex - 1; i >= 0; i--)
        {
            ReplyTokenInfo token = mainModeReplyTokens[i];
            if (token.isWord)
            {
                return GetMainModeReplyWordColorHex(token.wordIndex, currentWordIndex, pastColor, currentColor, futureColor);
            }
        }

        for (int i = tokenIndex + 1; i < mainModeReplyTokens.Count; i++)
        {
            ReplyTokenInfo token = mainModeReplyTokens[i];
            if (token.isWord)
            {
                return GetMainModeReplyWordColorHex(token.wordIndex, currentWordIndex, pastColor, currentColor, futureColor);
            }
        }

        return futureColor;
    }

    private static string GetMainModeReplyWordColorHex(
        int wordIndex,
        int currentWordIndex,
        string pastColor,
        string currentColor,
        string futureColor)
    {
        if (wordIndex < currentWordIndex)
        {
            return pastColor;
        }

        if (wordIndex == currentWordIndex)
        {
            return currentColor;
        }

        return futureColor;
    }

    private int GetMainModeReplySegmentEndWordIndex(int currentWordIndex)
    {
        if (mainModeReplySegmentEndWordIndices.Count == 0)
        {
            return -1;
        }

        int clampedWordIndex = Mathf.Clamp(currentWordIndex, 0, Mathf.Max(0, mainModeReplyWords.Count - 1));
        for (int i = 0; i < mainModeReplySegmentEndWordIndices.Count; i++)
        {
            int segmentEndWordIndex = mainModeReplySegmentEndWordIndices[i];
            if (clampedWordIndex <= segmentEndWordIndex)
            {
                return segmentEndWordIndex;
            }
        }

        return mainModeReplyWords.Count - 1;
    }

    private int GetMainModeReplyWindowWords()
    {
        int baseWindow = touchUiActive ? replyTouchWindowWords : replyDesktopWindowWords;
        return Mathf.Max(4, baseWindow);
    }

    private static string EscapeForTmpRichText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private bool TryInterruptMainModeSpeechAndListen()
    {
        if (!mainModeSpeaking || mainModeChatNoteActive)
        {
            return false;
        }

        InterruptMainModeSpeech();
        StartMainModeListening();
        return true;
    }

    private void InterruptMainModeSpeech()
    {
        StopPendingWaitPhrasePlayback(stopAudioSource: true);

        mainModeTtsInterruptedByUser = true;

        if (mainModeRoutine != null)
        {
            StopCoroutine(mainModeRoutine);
            mainModeRoutine = null;
        }

        AbortMainModeRequests();
        StopMainModeTtsPlayback();
        StopMainModeReplyFlow(finalizeText: false);

        mainModeListening = false;
        mainModeSpeaking = false;
        mainModeProcessing = false;

        SetHintBarSpacePressed(false);

        var idleLook = avatarManager != null ? avatarManager.idleLook : null;
        if (idleLook != null)
        {
            idleLook.SetListening(false);
        }

        ResetMainModeTexts();
    }

    private void AbortMainModeRequests()
    {
        bool disposedMainRequest = false;
        if (mainModeRequests != null && mainModeRequests.Count > 0)
        {
            foreach (var request in mainModeRequests)
            {
                if (request == null)
                {
                    continue;
                }
                if (request == mainModeRequest)
                {
                    disposedMainRequest = true;
                }
                request.Abort();
                request.Dispose();
            }
            mainModeRequests.Clear();
        }

        if (mainModeRequest != null)
        {
            if (!disposedMainRequest)
            {
                mainModeRequest.Abort();
                mainModeRequest.Dispose();
            }
            mainModeRequest = null;
        }
    }

    private void StopMainModeTtsPlayback()
    {
        ttsAcceptIncomingSamples = false;
        ttsActiveSessionId = -1;
        ttsPlaybackSessionId++;

        ttsStreamError = null;
        ttsStreamBytes = 0;
        ttsStreamSampleRate = 0;
        ttsStreamChannels = 0;
        if (mainModeTtsInterruptedByUser && Application.platform != RuntimePlatform.WebGLPlayer)
        {
            mainModeTtsInterruptedByUser = false;
        }

        if (ttsAudioSource != null)
        {
            ttsAudioSource.Stop();
        }

        if (ttsChunkPlayer != null)
        {
            ttsChunkPlayer.StopStream();
        }
    }

    private bool IsTtsSessionActive(int sessionId)
    {
        return ttsAcceptIncomingSamples && sessionId >= 0 && sessionId == ttsActiveSessionId;
    }

    private void RequestMainModeHintRefresh()
    {
        if (!HasActiveHintBar() || currentState != UIState.MainMode)
        {
            return;
        }

        if (mainModeHintRefreshRoutine != null)
        {
            StopCoroutine(mainModeHintRefreshRoutine);
        }
        mainModeHintRefreshRoutine = StartCoroutine(RefreshMainModeHintRoutine());
    }

    private IEnumerator RefreshMainModeHintRoutine()
    {
        float delay = Mathf.Max(0f, mainModeHintRefreshDelay);
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }
        else
        {
            yield return null;
        }

        if (currentState == UIState.MainMode)
        {
            UpdateHintBar(UIState.MainMode);
        }
        mainModeHintRefreshRoutine = null;
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

        ResetMainModeTexts();
        mainModeListening = audioRecorder.StartRecording();
        if (!mainModeListening)
        {
            UpdateMainModeStatus("Consenti microfono per continuare.");
            PlayErrorClip();
            return;
        }

        UpdateMainModeStatus(touchUiActive
            ? "In ascolto... (rilascia PTT per terminare)"
            : "In ascolto... (rilascia SPACE per terminare)");
        SetHintBarSpacePressed(true);
        // Evitiamo un redesign UI nello stesso frame di avvio ascolto (riduce possibili "scatti" percepiti).
        RequestMainModeHintRefresh();
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
        SetHintBarSpacePressed(false);
        RequestMainModeHintRefresh();
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
            UpdateMainModeStatus("Registrazione fallita. Riprova.");
            PlayErrorClip();
            yield break;
        }

        if (IsWhisperInputTooShort(wavBytes))
        {
            UpdateMainModeStatus("Registrazione troppo breve. Riprova.");
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
            FailMainModeProcessing($"Errore Whisper: {whisperError}");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            FailMainModeProcessing("Non ho capito, riprova.");
            yield break;
        }

        if (LooksLikeWhisperSilenceHallucination(transcript))
        {
            FailMainModeProcessing("Trascrizione non valida. Riprova.");
            yield break;
        }

        ApplyMainModeTranscript(transcript);

        yield return StartCoroutine(RequestAndSpeakMainModeReply(transcript, "voice"));
    }

    private void TriggerMainModeWaitPhrase()
    {
        StopPendingWaitPhrasePlayback(stopAudioSource: false);
        waitPhrasePlaybackToken++;
        waitPhraseRoutine = StartCoroutine(PlayRandomWaitPhraseOnce(waitPhrasePlaybackToken));
    }

    private void StopPendingWaitPhrasePlayback(bool stopAudioSource)
    {
        waitPhrasePlaybackToken++;
        if (waitPhraseRoutine != null)
        {
            StopCoroutine(waitPhraseRoutine);
            waitPhraseRoutine = null;
        }

        if (stopAudioSource && ttsAudioSource != null)
        {
            ttsAudioSource.Stop();
        }

        if (stopAudioSource)
        {
            waitPhraseStarted = false;
            waitPhraseActiveClip = null;
        }
        else
        {
            if (waitPhraseStarted &&
                (ttsAudioSource == null ||
                 waitPhraseActiveClip == null ||
                 !ttsAudioSource.isPlaying ||
                 ttsAudioSource.clip != waitPhraseActiveClip))
            {
                waitPhraseStarted = false;
                waitPhraseActiveClip = null;
            }
        }
    }

    private bool IsWaitPhrasePlaybackStillValid(int playbackToken)
    {
        return playbackToken == waitPhrasePlaybackToken;
    }

    private bool IsWaitPhraseCurrentlyBlockingReply()
    {
        if (waitPhraseStarted &&
            (ttsAudioSource == null ||
             waitPhraseActiveClip == null ||
             !ttsAudioSource.isPlaying ||
             ttsAudioSource.clip != waitPhraseActiveClip))
        {
            waitPhraseStarted = false;
            waitPhraseActiveClip = null;
        }

        return waitPhraseStarted &&
               waitPhraseActiveClip != null &&
               ttsAudioSource != null &&
               ttsAudioSource.isPlaying &&
               ttsAudioSource.clip == waitPhraseActiveClip &&
               !mainModeReplySpeechStarted;
    }

    private IEnumerator PlayRandomWaitPhraseOnce(int playbackToken)
    {
        if (servicesConfig == null)
        {
            yield break;
        }

        float startedAt = Time.realtimeSinceStartup;

        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        if (string.IsNullOrEmpty(avatarId))
        {
            yield break;
        }

        if (!IsWaitPhrasePlaybackStillValid(playbackToken))
        {
            yield break;
        }

        string key = waitPhraseKeys[Random.Range(0, waitPhraseKeys.Length)];
        if (waitPhraseKeys.Length > 1
            && lastWaitPhraseByAvatar.TryGetValue(avatarId, out var lastKey)
            && key == lastKey)
        {
            var candidates = new List<string>(waitPhraseKeys.Length - 1);
            for (int i = 0; i < waitPhraseKeys.Length; i++)
            {
                string candidate = waitPhraseKeys[i];
                if (!string.Equals(candidate, lastKey, System.StringComparison.Ordinal))
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count > 0)
            {
                key = candidates[Random.Range(0, candidates.Count)];
            }
        }
        lastWaitPhraseByAvatar[avatarId] = key;
        string cacheKey = $"{(empiricalTestModeEnabled ? "empirical" : "default")}:{avatarId}:{key}";

        if (!waitPhraseCache.TryGetValue(cacheKey, out var clip) || clip == null)
        {
            string url = BuildServiceUrl(
                servicesConfig.coquiBaseUrl,
                AppendEmpiricalTestModeQuery($"wait_phrase?avatar_id={UnityWebRequest.EscapeURL(avatarId)}&name={UnityWebRequest.EscapeURL(key)}")
            );

            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                yield return request.SendWebRequest();

                if (!IsWaitPhrasePlaybackStillValid(playbackToken))
                {
                    yield break;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    UpdateDebugText($"Wait phrase error: {request.error}");
                    yield break;
                }
                else
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null)
                    {
                        waitPhraseCache[cacheKey] = clip;
                    }
                }
            }
        }

        if (clip == null)
        {
            yield break;
        }

        if (!IsWaitPhrasePlaybackStillValid(playbackToken))
        {
            yield break;
        }

        var source = GetOrCreateLipSyncAudioSource();
        if (source == null)
        {
            yield break;
        }

        float remainingDelay = Mathf.Max(0f, ttsWaitPhraseDelaySeconds - (Time.realtimeSinceStartup - startedAt));
        float delayDeadline = Time.realtimeSinceStartup + remainingDelay;
        while (Time.realtimeSinceStartup < delayDeadline)
        {
            if (!IsWaitPhrasePlaybackStillValid(playbackToken) ||
                (!mainModeProcessing && !mainModeSpeaking) ||
                mainModeReplySpeechStarted)
            {
                waitPhraseRoutine = null;
                yield break;
            }

            yield return null;
        }

        // In WebGL i clip da rete possono essere non pronti per qualche frame:
        // evitiamo Play() su clip non ancora loaded.
        yield return StartCoroutine(EnsureAudioClipLoaded(clip, 1.5f));
        if (clip.loadState != AudioDataLoadState.Loaded || !IsWaitPhrasePlaybackStillValid(playbackToken))
        {
            yield break;
        }

        if (!IsWaitPhrasePlaybackStillValid(playbackToken) ||
            (!mainModeProcessing && !mainModeSpeaking) ||
            mainModeReplySpeechStarted)
        {
            waitPhraseRoutine = null;
            yield break;
        }

        waitPhraseStarted = true;
        waitPhraseActiveClip = clip;
        source.Stop();
        source.loop = false;
        source.clip = clip;
        source.volume = 0.8f;
        source.Play();

        float playbackDeadline = Time.realtimeSinceStartup + Mathf.Max(0.15f, clip.length + 0.1f);
        while (IsWaitPhrasePlaybackStillValid(playbackToken) &&
               source != null &&
               source.clip == clip)
        {
            if (!source.isPlaying && Time.realtimeSinceStartup >= playbackDeadline)
            {
                break;
            }

            yield return null;
        }

        if (IsWaitPhrasePlaybackStillValid(playbackToken))
        {
            waitPhraseRoutine = null;
        }

        if (source == null || !source.isPlaying || source.clip != clip)
        {
            waitPhraseStarted = false;
            if (waitPhraseActiveClip == clip)
            {
                waitPhraseActiveClip = null;
            }
        }
    }

    private IEnumerator EnsureAudioClipLoaded(AudioClip clip, float timeoutSeconds)
    {
        if (clip == null)
        {
            yield break;
        }

        if (clip.loadState == AudioDataLoadState.Loaded)
        {
            yield break;
        }

        if (clip.loadState == AudioDataLoadState.Unloaded)
        {
            clip.LoadAudioData();
        }

        float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, timeoutSeconds);
        while (clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private void RequestWebGlMicrophonePermissionIfNeeded()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (audioRecorder != null)
        {
            audioRecorder.RequestMicrophonePermissionIfNeeded();
        }
#endif
    }

    private IEnumerator PlayTtsReply(string text)
    {
        mainModeTtsInterruptedByUser = false;
        ttsPlaybackSessionId++;
        ttsActiveSessionId = ttsPlaybackSessionId;
        ttsAcceptIncomingSamples = true;
        ttsStreamError = null;

        if (servicesConfig == null)
        {
            ttsStreamError = "ServicesConfig mancante.";
            UpdateMainModeStatus("ServicesConfig mancante.");
            PlayErrorClip();
            yield break;
        }

        yield return StartCoroutine(PlayTtsReplyNativeStream(text));
    }

    private IEnumerator PlayTtsReplyNativeStream(string text)
    {
        int sessionId = ttsActiveSessionId;
        string avatarId = avatarManager != null ? avatarManager.CurrentAvatarId : null;
        string url = BuildServiceUrl(servicesConfig.coquiBaseUrl, "tts_stream");
        string safeAvatarId = string.IsNullOrEmpty(avatarId) ? "default" : avatarId;
        string ttsRequestId = GenerateTtsRequestId();
        bool disableWebGlWordFlow = IsWebGlReplyWordFlowDisabled();
        bool enableTimingTransport = !disableWebGlWordFlow &&
                                     enableMainModeReplyWordFlow &&
                                     !mainModeReplyUseStaticFallback;

        ttsStreamError = null;
        ttsStreamBytes = 0;
        ttsStreamSampleRate = 0;
        ttsStreamChannels = 0;
        mainModeReplyTtsRequestId = enableTimingTransport ? ttsRequestId : null;
        mainModeReplyTimingState = enableTimingTransport
            ? MainModeReplyTimingResolutionState.Pending
            : MainModeReplyTimingResolutionState.Unavailable;
        mainModeReplyTimingComplete = !enableTimingTransport;
        mainModeReplyTimingRetryCount = 0;

        if (enableTtsWebGlStreamingLogs)
        {
            int len = string.IsNullOrEmpty(text) ? 0 : text.Length;
            Debug.Log($"[UIFlowController] Native TTS stream start (len={len}, avatar={safeAvatarId}).");
        }

        var form = new WWWForm();
        form.AddField("text", text ?? string.Empty);
        form.AddField("avatar_id", safeAvatarId);
        form.AddField("language", "it");
        form.AddField("reply_segment_max_chars", Mathf.Clamp(ttsReplySegmentMaxChars, 40, 400).ToString());
        form.AddField("client_platform", Application.platform == RuntimePlatform.WebGLPlayer ? "webgl" : "native");
        if (enableTimingTransport)
        {
            form.AddField("request_id", ttsRequestId);
        }
        AddEmpiricalTestModeField(form);

        using (var request = UnityWebRequest.Post(url, form))
        {
            int emitMinFrames = GetTtsPlaybackTuning().emitMinFrames;

            var handler = new PcmStreamDownloadHandler(
                (sampleRate, channels) =>
                {
                    if (!IsTtsSessionActive(sessionId))
                    {
                        return;
                    }
                    ttsStreamSampleRate = sampleRate;
                    ttsStreamChannels = channels;
                    EnsureTtsStreamPlayer(sampleRate, channels);
                },
                samples =>
                {
                    if (!IsTtsSessionActive(sessionId))
                    {
                        return;
                    }
                    bool hasSamples = samples != null && samples.Length > 0;
                    if (!mainModeReplyLoggedFirstChunk && hasSamples)
                    {
                        mainModeReplyLoggedFirstChunk = true;
                        if (enableTtsWebGlStreamingLogs)
                        {
                            Debug.Log($"[UIFlowController] Native TTS first chunk received ({samples.Length} samples).");
                        }
                    }
                    EnqueueTtsSamples(samples);
                },
                bytes =>
                {
                    if (!IsTtsSessionActive(sessionId))
                    {
                        return;
                    }
                    long previousBytes = ttsStreamBytes;
                    ttsStreamBytes += bytes;
                    if (enableTtsWebGlStreamingLogs && !mainModeReplyLoggedFirstStreamByte && bytes > 0)
                    {
                        mainModeReplyLoggedFirstStreamByte = true;
                        Debug.Log("[UIFlowController] Native TTS first byte received.");
                    }
                },
                error =>
                {
                    if (!IsTtsSessionActive(sessionId))
                    {
                        return;
                    }
                    ttsStreamError = error;
                },
                emitMinFrames);

            request.downloadHandler = handler;
            mainModeRequest = request;
            if (mainModeRequests != null)
            {
                mainModeRequests.Add(request);
            }
            request.timeout = GetRequestTimeoutSeconds(longOperation: true);
            var operation = request.SendWebRequest();

            if (mainModeReplyTimingPollRoutine != null)
            {
                StopCoroutine(mainModeReplyTimingPollRoutine);
            }
            if (enableTimingTransport)
            {
                mainModeReplyTimingPollRoutine = StartCoroutine(PollTtsIncrementalTiming(ttsRequestId, sessionId));
            }
            else
            {
                mainModeReplyTimingPollRoutine = null;
            }

            while (!operation.isDone)
            {
                yield return null;
            }
            if (mainModeRequests != null)
            {
                mainModeRequests.Remove(request);
            }
            if (mainModeRequest == request)
            {
                mainModeRequest = null;
            }

            UnityWebRequest.Result requestResult;
            try
            {
                requestResult = request.result;
            }
            catch (NullReferenceException)
            {
                if (!IsTtsSessionActive(sessionId))
                {
                    yield break;
                }

                ttsStreamError = "Richiesta TTS interrotta.";
                yield break;
            }

            if (requestResult != UnityWebRequest.Result.Success)
            {
                string detail = null;
                string error = "Network error";
                try
                {
                    detail = request.downloadHandler != null ? request.downloadHandler.text : null;
                    error = request.error ?? "Network error";
                }
                catch (NullReferenceException)
                {
                    if (!IsTtsSessionActive(sessionId))
                    {
                        yield break;
                    }

                    error = "Richiesta TTS interrotta.";
                }

                if (!string.IsNullOrWhiteSpace(detail))
                {
                    error = $"{error} - {detail}";
                }
                ReportServiceError("Coqui", error);
                if (IsTtsSessionActive(sessionId))
                {
                    ttsStreamError = error;
                }
            }
        }

        if (!IsTtsSessionActive(sessionId))
        {
            yield break;
        }

        EndTtsStreamPlayback();
        while (!IsTtsStreamDrained())
        {
            yield return null;
        }

        if (enableTtsWebGlStreamingLogs)
        {
            Debug.Log("[UIFlowController] Native TTS stream drained.");
        }

        if (!IsTtsSessionActive(sessionId))
        {
            yield break;
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

    private static bool IsWebGlReplyWordFlowDisabled()
    {
        return Application.platform == RuntimePlatform.WebGLPlayer;
    }


    private void CancelMainMode()
    {
        HideChatNoteImmediate();

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

        if (mainModeHintRefreshRoutine != null)
        {
            StopCoroutine(mainModeHintRefreshRoutine);
            mainModeHintRefreshRoutine = null;
        }

        ResetMainModeConversationSession();

        AbortMainModeRequests();
        StopMainModeTtsPlayback();
        StopMainModeReplyFlow(finalizeText: false);

        if (audioRecorder != null)
        {
            audioRecorder.StopRecording();
        }

        mainModeListening = false;
        mainModeProcessing = false;
        mainModeSpeaking = false;
        mainModeTtsInterruptedByUser = false;
        SetHintBarSpacePressed(false);
        ResetTouchMainModeSwipeTracking();
        UpdateTouchAvatarDimming();
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
            if (!IsKeyDown(KeyCode.Space))
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

        if (ttsChunkPlayer == null)
        {
            ttsChunkPlayer = gameObject.GetComponent<PcmChunkPlayer>();
            if (ttsChunkPlayer == null)
            {
                ttsChunkPlayer = gameObject.AddComponent<PcmChunkPlayer>();
            }
        }

        TtsPlaybackTuning tuning = GetTtsPlaybackTuning();

        ttsChunkPlayer.Configure(
            tuning.replyStartBufferFrames,
            tuning.minFramesPerClip,
            tuning.gatherBudgetSeconds,
            tuning.stallToleranceSeconds);

        ttsChunkPlayer.Begin(
            ttsAudioSource,
            sampleRate,
            channels,
            IsWaitPhraseCurrentlyBlockingReply);
    }

    private readonly struct TtsPlaybackTuning
    {
        public readonly int emitMinFrames;
        public readonly int replyStartBufferFrames;
        public readonly int minFramesPerClip;
        public readonly float gatherBudgetSeconds;
        public readonly float stallToleranceSeconds;

        public TtsPlaybackTuning(
            int emitMinFrames,
            int replyStartBufferFrames,
            int minFramesPerClip,
            float gatherBudgetSeconds,
            float stallToleranceSeconds)
        {
            this.emitMinFrames = emitMinFrames;
            this.replyStartBufferFrames = replyStartBufferFrames;
            this.minFramesPerClip = minFramesPerClip;
            this.gatherBudgetSeconds = gatherBudgetSeconds;
            this.stallToleranceSeconds = stallToleranceSeconds;
        }
    }

    private TtsPlaybackTuning GetTtsPlaybackTuning()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            return new TtsPlaybackTuning(4096, 4096, 4096, 0.12f, 0.30f);
        }

        switch (ttsWebGlStreamProfile)
        {
            case TtsWebGlStreamProfile.Smooth:
                return new TtsPlaybackTuning(24576, 32768, 49152, 0.50f, 1.30f);
            case TtsWebGlStreamProfile.MaxStability:
                return new TtsPlaybackTuning(32768, 49152, 65536, 0.60f, 1.60f);
            default:
                return new TtsPlaybackTuning(16384, 24576, 32768, 0.45f, 1.10f);
        }
    }

    private void EnqueueTtsSamples(float[] samples)
    {
        ttsChunkPlayer?.Enqueue(samples);
    }

    private void EndTtsStreamPlayback()
    {
        ttsChunkPlayer?.EndStream();
    }

    private bool IsTtsStreamDrained()
    {
        return ttsChunkPlayer == null || ttsChunkPlayer.IsDrained;
    }

    private bool TryBuildReplySegmentEndWordIndices(
        List<int> alignedWordEndMs,
        List<int> segmentEndMs,
        out List<int> segmentEndWordIndices)
    {
        segmentEndWordIndices = null;
        if (alignedWordEndMs == null ||
            segmentEndMs == null ||
            alignedWordEndMs.Count == 0 ||
            segmentEndMs.Count == 0)
        {
            return false;
        }

        var parsed = new List<int>(segmentEndMs.Count);
        int wordIndex = 0;
        for (int i = 0; i < segmentEndMs.Count; i++)
        {
            int segmentEndMsValue = segmentEndMs[i];
            while (wordIndex < alignedWordEndMs.Count - 1 &&
                   alignedWordEndMs[wordIndex] < segmentEndMsValue)
            {
                wordIndex++;
            }

            if (parsed.Count > 0 && wordIndex <= parsed[parsed.Count - 1])
            {
                return false;
            }

            parsed.Add(wordIndex);
        }

        if (parsed[parsed.Count - 1] != alignedWordEndMs.Count - 1)
        {
            return false;
        }

        segmentEndWordIndices = parsed;
        return true;
    }

    private bool TryMapAlignedWordsToReplyWords(List<string> alignedWordTokens)
    {
        if (alignedWordTokens == null ||
            alignedWordTokens.Count == 0 ||
            mainModeReplyWords.Count == 0)
        {
            return false;
        }

        if (alignedWordTokens.Count != mainModeReplyWords.Count)
        {
            return false;
        }

        for (int i = 0; i < mainModeReplyWords.Count; i++)
        {
            if (!string.Equals(mainModeReplyWords[i].normalizedToken, alignedWordTokens[i], StringComparison.Ordinal))
            {
                var canonicalReplyWords = ExtractCanonicalReplyAlignmentWords(mainModeReplyFullText);
                if (canonicalReplyWords.Count != alignedWordTokens.Count ||
                    canonicalReplyWords.Count != mainModeReplyWords.Count)
                {
                    return false;
                }

                for (int canonicalIndex = 0; canonicalIndex < canonicalReplyWords.Count; canonicalIndex++)
                {
                    if (!string.Equals(canonicalReplyWords[canonicalIndex], alignedWordTokens[canonicalIndex], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        return true;
    }

    private static bool IsReplyWordLetter(char c)
    {
        return char.IsLetter(c);
    }

    private static bool IsReplyWordApostrophe(char c)
    {
        return c == '\'' || c == '\u2019' || c == '\u2018';
    }

    private static string NormalizeReplyWordToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        string normalized = token
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(normalized.Length);
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if ((c >= 'a' && c <= 'z') || c == '\'')
            {
                sb.Append(c);
            }
        }

        string collapsed = Regex.Replace(sb.ToString(), @"'{2,}", "'");
        return collapsed.Trim('\'');
    }

    private static List<string> ExtractCanonicalReplyAlignmentWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        string normalized = text
            .Trim()
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace("-", " ")
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(normalized.Length);
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if ((c >= 'a' && c <= 'z') || c == '\'' || char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append(' ');
            }
        }

        MatchCollection matches = Regex.Matches(sb.ToString(), @"[a-z]+(?:'[a-z]+)*");
        var words = new List<string>(matches.Count);
        for (int i = 0; i < matches.Count; i++)
        {
            string normalizedToken = NormalizeReplyWordToken(matches[i].Value);
            if (!string.IsNullOrEmpty(normalizedToken))
            {
                words.Add(normalizedToken);
            }
        }

        return words;
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

        if (touchUiActive)
        {
            if (camTouchSetupVoiceAnchor == null)
            {
                Debug.Log("[UIFlowController] camTouchSetupVoiceAnchor non assegnato: uso fallback Cam_SetupVoice.");
            }

            if (camTouchSetupMemoryAnchor == null)
            {
                Debug.Log("[UIFlowController] camTouchSetupMemoryAnchor non assegnato: uso fallback Cam_SetupMemory.");
            }
        }
    }

    private static string GenerateTtsRequestId()
    {
        return $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
    }

    private float GetBackendTimingPollMinSeconds()
    {
        return Mathf.Max(0.05f, backendTimingPollMinSeconds);
    }

    private float GetBackendTimingPollMaxSeconds()
    {
        return Mathf.Max(GetBackendTimingPollMinSeconds(), backendTimingPollMaxSeconds);
    }

    private int GetMainModeReplyTimingLeadMs()
    {
        if (mainModeReplyWordEndMs.Count == 0 || ttsChunkPlayer == null)
        {
            return 0;
        }

        int playedMs = Mathf.Max(0, Mathf.RoundToInt(ttsChunkPlayer.PlayedAudioSeconds * 1000f));
        int lastTimedMs = mainModeReplyWordEndMs[mainModeReplyWordEndMs.Count - 1];
        return Mathf.Max(0, lastTimedMs - playedMs);
    }

    private float GetAdaptiveBackendTimingPollSeconds()
    {
        float minSeconds = GetBackendTimingPollMinSeconds();
        float maxSeconds = GetBackendTimingPollMaxSeconds();
        if (mainModeReplyWordEndMs.Count == 0)
        {
            return minSeconds;
        }

        int leadMs = GetMainModeReplyTimingLeadMs();
        float targetMs = Mathf.Max(100f, backendTimingTargetLeadMs);
        float lowMs = targetMs * 0.5f;
        float highMs = targetMs * 2f;
        if (leadMs <= lowMs)
        {
            return minSeconds;
        }

        if (leadMs >= highMs)
        {
            return maxSeconds;
        }

        float t = Mathf.InverseLerp(lowMs, highMs, leadMs);
        return Mathf.Lerp(minSeconds, maxSeconds, t);
    }

    private IEnumerator PollTtsIncrementalTiming(string requestId, int sessionId)
    {
        if (string.IsNullOrEmpty(requestId) || servicesConfig == null)
        {
            yield break;
        }

        string encodedRequestId = UnityWebRequest.EscapeURL(requestId);
        string url = BuildServiceUrlWithEmpiricalMode(servicesConfig.coquiBaseUrl, $"tts_timing?request_id={encodedRequestId}");
        float minPollSeconds = GetBackendTimingPollMinSeconds();
        float maxPollSeconds = GetBackendTimingPollMaxSeconds();
        float pollSeconds = minPollSeconds;
        float initialDeadline = Time.realtimeSinceStartup + Mathf.Max(0f, backendTimingInitialDelaySeconds);

        while (IsTtsSessionActive(sessionId) &&
               string.Equals(mainModeReplyTtsRequestId, requestId, StringComparison.Ordinal) &&
               !HasMainModeReplyAudioStarted() &&
               Time.realtimeSinceStartup < initialDeadline)
        {
            yield return null;
        }

        while (IsTtsSessionActive(sessionId) && string.Equals(mainModeReplyTtsRequestId, requestId, StringComparison.Ordinal))
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(2f, backendTimingPollTimeoutSeconds)));
                yield return request.SendWebRequest();

                if (!IsTtsSessionActive(sessionId) || !string.Equals(mainModeReplyTtsRequestId, requestId, StringComparison.Ordinal))
                {
                    yield break;
                }

                bool success = false;
                try
                {
                    success = request.result == UnityWebRequest.Result.Success;
                }
                catch (NullReferenceException)
                {
                    yield break;
                }

                if (success)
                {
                    TtsTimingSnapshotResponse payload = null;
                    try
                    {
                        payload = JsonUtility.FromJson<TtsTimingSnapshotResponse>(request.downloadHandler.text);
                    }
                    catch (Exception)
                    {
                        payload = null;
                    }

                    if (payload != null)
                    {
                        MainModeReplyTimingResolutionState stateAfterApply = ApplyIncrementalTtsTimingSnapshot(payload);
                        mainModeReplyTimingRetryCount = 0;
                        pollSeconds = Mathf.Clamp(GetAdaptiveBackendTimingPollSeconds(), minPollSeconds, maxPollSeconds);

                        if (!string.IsNullOrWhiteSpace(payload.error) && string.IsNullOrWhiteSpace(ttsStreamError))
                        {
                            ttsStreamError = payload.error;
                        }

                        if (payload.complete)
                        {
                            mainModeReplyTimingComplete = true;
                            if (stateAfterApply == MainModeReplyTimingResolutionState.Unavailable && mainModeReplyWordEndMs.Count == 0)
                            {
                                mainModeReplyUseStaticFallback = true;
                            }
                            yield break;
                        }
                    }
                }
                else
                {
                    mainModeReplyTimingRetryCount++;
                    pollSeconds = Mathf.Min(maxPollSeconds, Mathf.Max(minPollSeconds, pollSeconds * 1.35f));
                    if (mainModeReplyTimingRetryCount >= Mathf.Max(1, backendTimingPollMaxRetries))
                    {
                        mainModeReplyTimingState = MainModeReplyTimingResolutionState.Unavailable;
                        mainModeReplyTimingComplete = true;
                        if (mainModeReplyWordEndMs.Count == 0)
                        {
                            mainModeReplyUseStaticFallback = true;
                        }
                        yield break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(ttsStreamError) && IsTtsStreamDrained())
            {
                mainModeReplyTimingState = mainModeReplyWordEndMs.Count > 0
                    ? MainModeReplyTimingResolutionState.Partial
                    : MainModeReplyTimingResolutionState.Unavailable;
                mainModeReplyTimingComplete = true;
                yield break;
            }

            yield return new WaitForSecondsRealtime(pollSeconds);
        }
    }

    private MainModeReplyTimingResolutionState ApplyIncrementalTtsTimingSnapshot(TtsTimingSnapshotResponse payload)
    {
        if (payload == null || !payload.ok || mainModeReplyWords.Count == 0)
        {
            return mainModeReplyTimingState;
        }

        if (!string.IsNullOrEmpty(mainModeReplyTtsRequestId) &&
            !string.IsNullOrEmpty(payload.request_id) &&
            !string.Equals(mainModeReplyTtsRequestId, payload.request_id, StringComparison.Ordinal))
        {
            return mainModeReplyTimingState;
        }

        var parsedWordTokens = payload.words != null ? new List<string>(payload.words) : null;
        var parsedWordEndMs = payload.word_end_ms != null ? new List<int>(payload.word_end_ms) : null;
        if (parsedWordTokens == null || parsedWordEndMs == null || parsedWordTokens.Count == 0 || parsedWordTokens.Count != parsedWordEndMs.Count)
        {
            if (payload.complete)
            {
                mainModeReplyTimingState = MainModeReplyTimingResolutionState.Unavailable;
            }
            return mainModeReplyTimingState;
        }

        if (!TryMapAlignedWordsToReplyWords(parsedWordTokens) || parsedWordEndMs.Count < mainModeReplyWordEndMs.Count)
        {
            return mainModeReplyTimingState;
        }

        mainModeReplyWordEndMs.Clear();
        for (int i = 0; i < parsedWordEndMs.Count && i < mainModeReplyWords.Count; i++)
        {
            mainModeReplyWordEndMs.Add(parsedWordEndMs[i]);
        }

        mainModeReplySegmentEndMs.Clear();
        mainModeReplySegmentEndWordIndices.Clear();
        if (payload.segment_end_ms != null && payload.segment_end_ms.Length > 0)
        {
            var parsedSegmentEndMs = new List<int>(payload.segment_end_ms);
            if (TryBuildReplySegmentEndWordIndices(mainModeReplyWordEndMs, parsedSegmentEndMs, out var parsedSegmentEndWordIndices))
            {
                mainModeReplySegmentEndMs.AddRange(parsedSegmentEndMs);
                mainModeReplySegmentEndWordIndices.AddRange(parsedSegmentEndWordIndices);
            }
        }

        if (mainModeReplyWordEndMs.Count == mainModeReplyWords.Count && payload.complete)
        {
            mainModeReplyTimingState = MainModeReplyTimingResolutionState.Available;
        }
        else if (mainModeReplyWordEndMs.Count > 0)
        {
            mainModeReplyTimingState = MainModeReplyTimingResolutionState.Partial;
        }

        return mainModeReplyTimingState;
    }

    private AudioSource GetOrCreateLipSyncAudioSource()
    {
        if (ttsAudioSource != null)
        {
            ConfigureTtsAudioSource(ttsAudioSource);
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
            ConfigureTtsAudioSource(source);
            ttsAudioSource = source;
            return ttsAudioSource;
        }

        ttsAudioSource = GetComponent<AudioSource>();
        if (ttsAudioSource == null)
        {
            ttsAudioSource = gameObject.AddComponent<AudioSource>();
        }
        ConfigureTtsAudioSource(ttsAudioSource);
        return ttsAudioSource;
    }

    private static void ConfigureTtsAudioSource(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        source.enabled = true;
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.pitch = 1f;
        source.mute = false;
        if (source.volume <= 0.0001f)
        {
            source.volume = 1f;
        }
    }

    private void UpdateCameraAnchorForState(UIState state)
    {
        switch (state)
        {
            case UIState.SetupVoice:
                currentCameraAnchor = touchUiActive && camTouchSetupVoiceAnchor != null
                    ? camTouchSetupVoiceAnchor
                    : camSetupVoiceLeftAnchor;
                break;
            case UIState.SetupMemory:
                currentCameraAnchor = touchUiActive && camTouchSetupMemoryAnchor != null
                    ? camTouchSetupMemoryAnchor
                    : camSetupMemoryRightAnchor;
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
        private readonly int emitMinFrames;
        private readonly byte[] header = new byte[44];
        private int headerBytes;
        private bool headerReady;
        private byte[] leftover;
        private byte[] pendingPcmBytes;
        private int pendingPcmLength;
        private int channels = 1;

        public PcmStreamDownloadHandler(
            Action<int, int> onHeader,
            Action<float[]> onSamples,
            Action<int> onBytes,
            Action<string> onError,
            int emitMinFrames)
        {
            this.onHeader = onHeader;
            this.onSamples = onSamples;
            this.onBytes = onBytes;
            this.onError = onError;
            this.emitMinFrames = Mathf.Max(1, emitMinFrames);
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
                    channels = Mathf.Max(1, BitConverter.ToInt16(header, 22));
                    int sampleRate = BitConverter.ToInt32(header, 24);
                    headerReady = true;
                    onHeader?.Invoke(sampleRate, channels);
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
                leftover = CopySlice(payload, payloadOffset, remaining);
                return true;
            }

            if (aligned < remaining)
            {
                leftover = CopySlice(payload, payloadOffset + aligned, remaining - aligned);
            }

            AppendPendingPcm(payload, payloadOffset, aligned);
            FlushPendingPcm(flushAll: false);
            onBytes?.Invoke(aligned);
            return true;
        }

        protected override void CompleteContent()
        {
            if (!headerReady)
            {
                onError?.Invoke("Invalid WAV header");
                return;
            }

            FlushPendingPcm(flushAll: true);
        }

        private static byte[] CopySlice(byte[] source, int offset, int length)
        {
            if (source == null || length <= 0)
            {
                return Array.Empty<byte>();
            }

            var slice = new byte[length];
            Buffer.BlockCopy(source, offset, slice, 0, length);
            return slice;
        }

        private void AppendPendingPcm(byte[] payload, int payloadOffset, int length)
        {
            if (payload == null || length <= 0)
            {
                return;
            }

            int required = pendingPcmLength + length;
            if (pendingPcmBytes == null || pendingPcmBytes.Length < required)
            {
                int nextSize = pendingPcmBytes != null ? pendingPcmBytes.Length : 0;
                if (nextSize <= 0)
                {
                    nextSize = required;
                }
                while (nextSize < required)
                {
                    nextSize *= 2;
                }

                var resized = new byte[nextSize];
                if (pendingPcmLength > 0 && pendingPcmBytes != null)
                {
                    Buffer.BlockCopy(pendingPcmBytes, 0, resized, 0, pendingPcmLength);
                }

                pendingPcmBytes = resized;
            }

            Buffer.BlockCopy(payload, payloadOffset, pendingPcmBytes, pendingPcmLength, length);
            pendingPcmLength += length;
        }

        private void FlushPendingPcm(bool flushAll)
        {
            if (!headerReady || pendingPcmLength <= 0)
            {
                return;
            }

            int frameBytes = Mathf.Max(1, channels) * 2;
            int minBytes = Mathf.Max(frameBytes, emitMinFrames * frameBytes);
            int bytesToEmit = flushAll ? pendingPcmLength : pendingPcmLength - (pendingPcmLength % frameBytes);
            if (!flushAll && bytesToEmit < minBytes)
            {
                return;
            }

            bytesToEmit -= bytesToEmit % frameBytes;
            if (bytesToEmit <= 0)
            {
                return;
            }

            int sampleCount = bytesToEmit / 2;
            var samples = new float[sampleCount];
            int byteIndex = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(pendingPcmBytes, byteIndex);
                samples[i] = sample / 32768f;
                byteIndex += 2;
            }

            onSamples?.Invoke(samples);

            int remaining = pendingPcmLength - bytesToEmit;
            if (remaining > 0)
            {
                Buffer.BlockCopy(pendingPcmBytes, bytesToEmit, pendingPcmBytes, 0, remaining);
            }
            pendingPcmLength = remaining;
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

    private sealed class PcmChunkPlayer : MonoBehaviour
    {
        private const int DefaultWebGlMinFramesPerClip = 4096;
        private const float DefaultWebGlGatherBudgetSeconds = 0.10f;
        private const float DefaultStallToleranceSeconds = 0.25f;
        private readonly Queue<float[]> queue = new Queue<float[]>();
        private AudioSource source;
        private int channels = 1;
        private int sampleRate = 24000;
        private bool streamEnded;
        private Coroutine playRoutine;
        private readonly object locker = new object();
        private int queuedSampleCount;
        private int pendingClipSampleCount;
        private int replyStartBufferFrames = 6144;
        private int webGlMinFramesPerClip = DefaultWebGlMinFramesPerClip;
        private float webGlGatherBudgetSeconds = DefaultWebGlGatherBudgetSeconds;
        private float stallToleranceSeconds = DefaultStallToleranceSeconds;
        public int PlayedChunksCount { get; private set; }
        public float PlayedAudioSeconds { get; private set; }
        public int CurrentChunkIndex { get; private set; } = -1;
        public float CurrentChunkProgress01 { get; private set; }
        public bool HasPlaybackStarted { get; private set; }
        public bool HasStableClockStarted { get; private set; }
        public bool CurrentChunkClockStable { get; private set; }
        public float FirstPlaybackObservedRealtime { get; private set; } = -1f;

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

        public int BufferedFrames
        {
            get
            {
                lock (locker)
                {
                    return channels > 0 ? (queuedSampleCount + pendingClipSampleCount) / channels : 0;
                }
            }
        }

        private Func<bool> deferPlaybackPredicate;

        public void Configure(
            int replyStartBufferFrames,
            int webGlMinFramesPerClip,
            float webGlGatherBudgetSeconds,
            float stallToleranceSeconds)
        {
            this.replyStartBufferFrames = Mathf.Max(0, replyStartBufferFrames);
            this.webGlMinFramesPerClip = Mathf.Max(1024, webGlMinFramesPerClip);
            this.webGlGatherBudgetSeconds = Mathf.Max(0.01f, webGlGatherBudgetSeconds);
            this.stallToleranceSeconds = Mathf.Max(0.05f, stallToleranceSeconds);
        }

        public void ResetForPendingStream()
        {
            ResetPlaybackState(stopSourcePlayback: false, clearIdleSourceClip: true);
            streamEnded = false;
        }

        public void Begin(AudioSource audioSource, int sampleRate, int channels, Func<bool> deferPlaybackPredicate = null)
        {
            if (audioSource == null)
            {
                return;
            }

            bool shouldStopCurrentPlayback = playRoutine != null;
            source = audioSource;
            this.sampleRate = Mathf.Max(8000, sampleRate);
            this.channels = Mathf.Max(1, channels);
            ResetPlaybackState(stopSourcePlayback: shouldStopCurrentPlayback, clearIdleSourceClip: true);
            this.deferPlaybackPredicate = deferPlaybackPredicate;
            streamEnded = false;
            if (shouldStopCurrentPlayback)
            {
                source.Stop();
            }
            source.loop = false;
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
                queuedSampleCount += samples.Length;
            }
        }

        public void EndStream()
        {
            streamEnded = true;
        }

        public void StopStream()
        {
            ResetPlaybackState(stopSourcePlayback: true, clearIdleSourceClip: true);
        }

        private void GetChunkPlaybackTuning(out int targetFrames, out float gatherBudget)
        {
            targetFrames = HasPlaybackStarted
                ? Mathf.Max(1024, webGlMinFramesPerClip / 2)
                : webGlMinFramesPerClip;
            gatherBudget = HasPlaybackStarted
                ? Mathf.Min(0.12f, webGlGatherBudgetSeconds)
                : Mathf.Min(0.18f, webGlGatherBudgetSeconds);
        }

        private void ResetPlaybackState(bool stopSourcePlayback, bool clearIdleSourceClip)
        {
            streamEnded = true;
            PlayedChunksCount = 0;
            PlayedAudioSeconds = 0f;
            CurrentChunkIndex = -1;
            CurrentChunkProgress01 = 0f;
            HasPlaybackStarted = false;
            HasStableClockStarted = false;
            CurrentChunkClockStable = false;
            FirstPlaybackObservedRealtime = -1f;
            deferPlaybackPredicate = null;

            lock (locker)
            {
                queue.Clear();
                queuedSampleCount = 0;
                pendingClipSampleCount = 0;
            }

            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            if (source == null)
            {
                return;
            }

            if (stopSourcePlayback)
            {
                source.Stop();
            }

            if (stopSourcePlayback || (!source.isPlaying && clearIdleSourceClip))
            {
                source.clip = null;
            }
        }

        private IEnumerator PlayQueue()
        {
            float completedSeconds = 0f;
            AudioClip activeClip = null;
            try
            {
                while (true)
                {
                    if (!HasPlaybackStarted &&
                        !streamEnded &&
                        BufferedFrames < Mathf.Max(0, replyStartBufferFrames))
                    {
                        yield return null;
                        continue;
                    }

                    float[] chunk = DequeueChunk();

                    if (chunk == null)
                    {
                        if (streamEnded)
                        {
                            yield break;
                        }
                        yield return null;
                        continue;
                    }

                    GetChunkPlaybackTuning(out int targetFrames, out float gatherBudget);

                    int minSamples = Mathf.Max(1, targetFrames * Mathf.Max(1, channels));
                    if (chunk.Length < minSamples)
                    {
                        float gatherDeadline = Time.realtimeSinceStartup + gatherBudget;
                        while (chunk.Length < minSamples)
                        {
                            float[] next = DequeueChunk();

                            if (next != null && next.Length > 0)
                            {
                                chunk = MergeChunks(chunk, next);
                                continue;
                            }

                            if (streamEnded || Time.realtimeSinceStartup >= gatherDeadline)
                            {
                                break;
                            }

                            yield return null;
                        }
                    }

                    int frames = Mathf.Max(1, chunk.Length / channels);
                    lock (locker)
                    {
                        pendingClipSampleCount = chunk.Length;
                    }
                    var clip = AudioClip.Create("tts_stream_chunk", frames, channels, sampleRate, false);
                    clip.SetData(chunk, 0);

                    if (clip.loadState != AudioDataLoadState.Loaded)
                    {
                        clip.LoadAudioData();
                        float deadline = Time.realtimeSinceStartup + 0.25f;
                        while (clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < deadline)
                        {
                            yield return null;
                        }
                    }

                    while (deferPlaybackPredicate != null && deferPlaybackPredicate())
                    {
                        yield return null;
                    }

                    if (activeClip != null)
                    {
                        var staleClip = activeClip;
                        activeClip = null;
                        if (source != null && source.clip == staleClip)
                        {
                            source.clip = null;
                        }
                        Destroy(staleClip);
                    }

                    source.Stop();
                    source.loop = false;
                    source.clip = clip;
                    activeClip = clip;
                    source.Play();
                    CurrentChunkIndex = PlayedChunksCount;
                    CurrentChunkProgress01 = 0f;
                    PlayedChunksCount++;
                    float expectedDuration = Mathf.Max(0.02f, (float)frames / Mathf.Max(1, sampleRate));
                    float chunkStartedAt = Time.realtimeSinceStartup;
                    float chunkElapsed = 0f;

                    while (true)
                    {
                        int rawTimeSamples = Mathf.Max(0, source.timeSamples);
                        if (!HasPlaybackStarted && (source.isPlaying || rawTimeSamples > 0))
                        {
                            HasPlaybackStarted = true;
                            FirstPlaybackObservedRealtime = Time.realtimeSinceStartup;
                        }

                        if (Application.platform == RuntimePlatform.WebGLPlayer)
                        {
                            if (source.isPlaying || rawTimeSamples > 0)
                            {
                                HasStableClockStarted = true;
                                CurrentChunkClockStable = true;
                            }

                            float elapsedFromSamples = Mathf.Clamp(rawTimeSamples / Mathf.Max(1f, sampleRate), 0f, expectedDuration);
                            float elapsedFromRealtime = Mathf.Clamp(Time.realtimeSinceStartup - chunkStartedAt, 0f, expectedDuration);
                            if (elapsedFromSamples > elapsedFromRealtime + 0.08f)
                            {
                                elapsedFromSamples = elapsedFromRealtime;
                            }

                            chunkElapsed = Mathf.Max(chunkElapsed, elapsedFromSamples);
                        }
                        else
                        {
                            if (source.isPlaying || rawTimeSamples > 0)
                            {
                                HasStableClockStarted = true;
                                CurrentChunkClockStable = true;
                            }

                            float elapsedFromSamples = Mathf.Clamp(rawTimeSamples / Mathf.Max(1f, sampleRate), 0f, expectedDuration);
                            chunkElapsed = Mathf.Max(chunkElapsed, elapsedFromSamples);
                        }

                        CurrentChunkProgress01 = Mathf.Clamp01(chunkElapsed / Mathf.Max(0.0001f, expectedDuration));
                        PlayedAudioSeconds = completedSeconds + chunkElapsed;

                        bool doneBySamples = chunkElapsed >= (expectedDuration - 0.005f);
                        bool stalePlayback = !source.isPlaying && (Time.realtimeSinceStartup - chunkStartedAt) >= (expectedDuration + stallToleranceSeconds);
                        if (doneBySamples || stalePlayback)
                        {
                            break;
                        }

                        yield return null;
                    }

                    completedSeconds += chunkElapsed;
                    CurrentChunkProgress01 = 1f;
                    PlayedAudioSeconds = completedSeconds;
                    lock (locker)
                    {
                        pendingClipSampleCount = 0;
                    }

                    if (activeClip != null)
                    {
                        if (source != null && source.clip == activeClip)
                        {
                            source.clip = null;
                        }
                        Destroy(activeClip);
                        activeClip = null;
                    }
                }
            }
            finally
            {
                lock (locker)
                {
                    pendingClipSampleCount = 0;
                }
                if (activeClip != null)
                {
                    if (source != null && source.clip == activeClip)
                    {
                        source.clip = null;
                    }
                    Destroy(activeClip);
                    activeClip = null;
                }
                playRoutine = null;
            }
        }

        private float[] DequeueChunk()
        {
            lock (locker)
            {
                if (queue.Count <= 0)
                {
                    return null;
                }

                float[] chunk = queue.Dequeue();
                if (chunk != null)
                {
                    queuedSampleCount = Mathf.Max(0, queuedSampleCount - chunk.Length);
                }

                return chunk;
            }
        }

        private static float[] MergeChunks(float[] a, float[] b)
        {
            int aLen = a.Length;
            int bLen = b.Length;
            var merged = new float[aLen + bLen];
            Buffer.BlockCopy(a, 0, merged, 0, aLen * sizeof(float));
            Buffer.BlockCopy(b, 0, merged, aLen * sizeof(float), bLen * sizeof(float));
            return merged;
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

        if (IsWhisperInputTooShort(wavBytes))
        {
            UpdateSetupVoiceStatus("Registrazione troppo breve. Riprova.");
            UpdateDebugText("Setup Voice: registrazione troppo breve per Whisper.");
            PlayErrorClip();
            yield break;
        }

        if (devMode)
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "setup_voice.wav");
            System.IO.File.WriteAllBytes(path, wavBytes);
            UpdateDebugText($"WAV salvato: {path}");
        }

        yield return StartCoroutine(ShowRingsForVoiceOperation());
        float ringsShownAt = Time.unscaledTime;

        UpdateSetupVoiceStatus("Trascrizione...");
        UpdateDebugText($"Setup Voice: trascrizione {wavBytes.Length} bytes...");
        string transcript = null;
        string whisperError = null;
        yield return StartCoroutine(PostWavToWhisper(wavBytes, text => transcript = text, error => whisperError = error));

        if (setupVoiceCancelling)
        {
            UpdateDebugText("Setup Voice: trascrizione annullata.");
            yield return StartCoroutine(HideRingsAfterVoiceOperationWithMinimum(ringsShownAt));
            yield break;
        }

        if (!string.IsNullOrEmpty(whisperError))
        {
            UpdateSetupVoiceStatus($"Errore Whisper: {whisperError}");
            UpdateDebugText($"Setup Voice: errore Whisper - {whisperError}");
            PlayErrorClip();
            yield return StartCoroutine(HideRingsAfterVoiceOperationWithMinimum(ringsShownAt));
            yield break;
        }

        if (string.IsNullOrWhiteSpace(transcript) || LooksLikeWhisperSilenceHallucination(transcript))
        {
            UpdateSetupVoiceStatus("Trascrizione non valida. Riprova.");
            UpdateDebugText($"Setup Voice: trascrizione scartata ('{transcript ?? string.Empty}').");
            PlayErrorClip();
            yield return StartCoroutine(HideRingsAfterVoiceOperationWithMinimum(ringsShownAt));
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
            yield return StartCoroutine(HideRingsAfterVoiceOperationWithMinimum(ringsShownAt));
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
            yield return StartCoroutine(HideRingsAfterVoiceOperationWithMinimum(ringsShownAt));
            yield break;
        }

        if (!coquiOk)
        {
            UpdateSetupVoiceStatus($"Errore Coqui: {coquiError}");
            UpdateDebugText($"Setup Voice: errore Coqui - {coquiError}");
            PlayErrorClip();
            yield return StartCoroutine(HideRingsAfterVoiceOperationWithMinimum(ringsShownAt));
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

        yield return StartCoroutine(HideRingsAfterVoiceOperationWithMinimum(ringsShownAt));

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

        bool requestOk = false;
        string requestError = null;
        bool reportError = false;

        var form = new WWWForm();
        form.AddField("avatar_id", avatarId);
        form.AddField("language", "it");
        AddEmpiricalTestModeField(form);

        using (var request = UnityWebRequest.Post(
            BuildServiceUrlWithEmpiricalMode(servicesConfig.coquiBaseUrl, "generate_wait_phrases"),
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
        AddEmpiricalTestModeField(form);

        using (var request = UnityWebRequest.Post(BuildServiceUrlWithEmpiricalMode(servicesConfig.coquiBaseUrl, "set_avatar_voice"), form))
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

    private IEnumerator RunSnapshotOperation(
        string baseUrl,
        string path,
        string avatarId,
        string serviceName,
        System.Action<SnapshotOperationResponse> onSuccess,
        System.Action<string> onFailure)
    {
        if (servicesConfig == null)
        {
            onFailure?.Invoke("ServicesConfig mancante");
            yield break;
        }

        var form = new WWWForm();
        form.AddField("avatar_id", avatarId);
        AddEmpiricalTestModeField(form);

        using (var request = UnityWebRequest.Post(BuildServiceUrl(baseUrl, path), form))
        {
            request.timeout = GetRequestTimeoutSeconds(longOperation: true);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = BuildHttpError(request, $"{serviceName} network error");
                ReportServiceError(serviceName, error);
                onFailure?.Invoke(error);
                yield break;
            }

            try
            {
                var payload = JsonUtility.FromJson<SnapshotOperationResponse>(request.downloadHandler.text);
                if (payload == null)
                {
                    string error = $"{serviceName} invalid snapshot response";
                    ReportServiceError(serviceName, error);
                    onFailure?.Invoke(error);
                    yield break;
                }
                onSuccess?.Invoke(payload);
            }
            catch (System.Exception ex)
            {
                string error = $"{serviceName} snapshot parse error: {ex.Message}";
                ReportServiceError(serviceName, error);
                onFailure?.Invoke(error);
            }
        }
    }

    private IEnumerator BackupAvatarVoiceSnapshot(
        string avatarId,
        System.Action<bool> onSuccess,
        System.Action<string> onFailure)
    {
        SnapshotOperationResponse response = null;
        string error = null;
        yield return StartCoroutine(RunSnapshotOperation(
            servicesConfig.coquiBaseUrl,
            "avatar_voice_backup",
            avatarId,
            "Coqui",
            payload => response = payload,
            requestError => error = requestError));

        if (!string.IsNullOrEmpty(error))
        {
            onFailure?.Invoke(error);
            yield break;
        }

        onSuccess?.Invoke(response != null && response.ok && response.backed_up);
    }

    private IEnumerator RestoreAvatarVoiceSnapshot(
        string avatarId,
        System.Action<bool> onSuccess,
        System.Action<string> onFailure)
    {
        SnapshotOperationResponse response = null;
        string error = null;
        yield return StartCoroutine(RunSnapshotOperation(
            servicesConfig.coquiBaseUrl,
            "avatar_voice_restore",
            avatarId,
            "Coqui",
            payload => response = payload,
            requestError => error = requestError));

        if (!string.IsNullOrEmpty(error))
        {
            onFailure?.Invoke(error);
            yield break;
        }

        bool restored = response != null && response.ok && response.restored;
        if (restored)
        {
            ClearCachedWaitPhrasesForAvatar(avatarId);
        }
        onSuccess?.Invoke(restored);
    }

    private IEnumerator BackupAvatarMemorySnapshot(
        string avatarId,
        System.Action<bool> onSuccess,
        System.Action<string> onFailure)
    {
        SnapshotOperationResponse response = null;
        string error = null;
        yield return StartCoroutine(RunSnapshotOperation(
            servicesConfig.ragBaseUrl,
            "avatar_memory_backup",
            avatarId,
            "RAG",
            payload => response = payload,
            requestError => error = requestError));

        if (!string.IsNullOrEmpty(error))
        {
            onFailure?.Invoke(error);
            yield break;
        }

        onSuccess?.Invoke(response != null && response.ok && response.backed_up);
    }

    private IEnumerator RestoreAvatarMemorySnapshot(
        string avatarId,
        System.Action<bool> onSuccess,
        System.Action<string> onFailure)
    {
        SnapshotOperationResponse response = null;
        string error = null;
        yield return StartCoroutine(RunSnapshotOperation(
            servicesConfig.ragBaseUrl,
            "avatar_memory_restore",
            avatarId,
            "RAG",
            payload => response = payload,
            requestError => error = requestError));

        if (!string.IsNullOrEmpty(error))
        {
            onFailure?.Invoke(error);
            yield break;
        }

        onSuccess?.Invoke(response != null && response.ok && response.restored);
    }

    private void ClearCachedWaitPhrasesForAvatar(string avatarId)
    {
        if (string.IsNullOrEmpty(avatarId))
        {
            return;
        }

        var keysToRemove = new List<string>();
        foreach (var key in waitPhraseCache.Keys)
        {
            if (key.IndexOf($":{avatarId}:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                keysToRemove.Add(key);
            }
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            string key = keysToRemove[i];
            if (waitPhraseCache.TryGetValue(key, out var clip) && clip != null)
            {
                Destroy(clip);
            }
            waitPhraseCache.Remove(key);
        }

        lastWaitPhraseByAvatar.Remove(avatarId);
    }

    private void ClearAllCachedWaitPhraseClips()
    {
        if (waitPhraseCache.Count == 0)
        {
            return;
        }

        var keys = new List<string>(waitPhraseCache.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i];
            if (waitPhraseCache.TryGetValue(key, out var clip) && clip != null)
            {
                Destroy(clip);
            }
        }

        waitPhraseCache.Clear();
        lastWaitPhraseByAvatar.Clear();
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
            float timeoutSeconds = servicesConfig != null ? servicesConfig.requestTimeoutSeconds : 10f;
            if (!string.IsNullOrEmpty(serviceName) && string.Equals(serviceName, "RAG", StringComparison.OrdinalIgnoreCase))
            {
                // RAG puo' richiedere piu' tempo quando il modello va in cold start.
                timeoutSeconds = Mathf.Max(timeoutSeconds, 120f);
            }
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));

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
                string error = BuildHttpError(request, $"{serviceName} network error");
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

    private IEnumerator PostJsonWithRetry<T>(
        string url,
        string jsonPayload,
        string serviceName,
        System.Action<T> onSuccess,
        System.Action<string> onFailure,
        int maxRetries = 3
    )
    {
        int[] retryDelaysMs = { 2000, 4000, 8000 };
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            bool success = false;
            T result = default(T);
            string error = null;
            
            yield return PostJson(
                url,
                jsonPayload,
                serviceName,
                (T resp) => {
                    success = true;
                    result = resp;
                },
                (string err) => {
                    error = err;
                    if (!err.Contains("429"))
                    {
                        success = true;
                    }
                }
            );
            
            if (success && error == null)
            {
                onSuccess?.Invoke(result);
                yield break;
            }
            
            if (error != null && error.Contains("429"))
            {
                if (attempt < maxRetries - 1)
                {
                    int delayMs = retryDelaysMs[attempt];
                    Debug.Log($"[TTS Retry] Attempt {attempt + 1} failed (429). Retrying in {delayMs}ms...");
                    yield return new WaitForSeconds(delayMs / 1000f);
                    continue;
                }
            }
            else
            {
                onFailure?.Invoke(error ?? "Unknown error");
                yield break;
            }
        }
        
        onFailure?.Invoke($"{serviceName} unavailable after {maxRetries} retries");
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

    private static string SanitizeMainModeReply(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        string outText = text.Trim();
        outText = Regex.Replace(outText, @"\*[^*]{1,120}\*", " ");
        outText = Regex.Replace(outText, @"\(([^()]{1,80})\)", match =>
        {
            string inner = match.Groups[1].Value;
            string norm = Regex.Replace(inner, @"[^\p{L}\p{N}\s']", " ").Trim();
            if (string.IsNullOrWhiteSpace(norm))
            {
                return " ";
            }
            int words = norm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return words <= 8 ? " " : match.Value;
        });
        outText = Regex.Replace(outText, @"\[([^\[\]]{1,80})\]", match =>
        {
            string inner = match.Groups[1].Value;
            string norm = Regex.Replace(inner, @"[^\p{L}\p{N}\s']", " ").Trim();
            if (string.IsNullOrWhiteSpace(norm))
            {
                return " ";
            }
            int words = norm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return words <= 8 ? " " : match.Value;
        });
        outText = Regex.Replace(outText, @"^\s*(?:ahem+|ehm+|mmm+|uhm+|hmm+)\b[\s,.;:!?-]*", "", RegexOptions.IgnoreCase);
        outText = Regex.Replace(outText, @"[*_~`]+", "");
        outText = Regex.Replace(outText, @"\s{2,}", " ");
        outText = outText.Trim();
        return outText;
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

    private bool IsValidSetupVoicePhrase(string phrase, int minChars, int minWords)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        if (phrase.Length < Mathf.Max(1, minChars))
        {
            return false;
        }

        if (CountWords(phrase) < Mathf.Max(1, minWords))
        {
            return false;
        }

        if (LooksLikeModelRefusal(phrase))
        {
            return false;
        }

        return true;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Regex.Matches(text, @"\b[\p{L}\p{N}']+\b").Count;
    }

    private static bool IsWhisperInputTooShort(byte[] wavBytes)
    {
        return TryGetWavDurationSeconds(wavBytes, out float durationSeconds)
            && durationSeconds < MinWhisperInputDurationSeconds;
    }

    private static bool TryGetWavDurationSeconds(byte[] wavBytes, out float durationSeconds)
    {
        durationSeconds = 0f;

        if (wavBytes == null || wavBytes.Length < 44)
        {
            return false;
        }

        if (wavBytes[0] != 'R' || wavBytes[1] != 'I' || wavBytes[2] != 'F' || wavBytes[3] != 'F'
            || wavBytes[8] != 'W' || wavBytes[9] != 'A' || wavBytes[10] != 'V' || wavBytes[11] != 'E')
        {
            return false;
        }

        int channels = BitConverter.ToInt16(wavBytes, 22);
        int sampleRate = BitConverter.ToInt32(wavBytes, 24);
        int bitsPerSample = BitConverter.ToInt16(wavBytes, 34);
        int dataLength = BitConverter.ToInt32(wavBytes, 40);
        int bytesPerSample = bitsPerSample / 8;

        if (channels <= 0 || sampleRate <= 0 || bytesPerSample <= 0 || dataLength <= 0)
        {
            return false;
        }

        double bytesPerSecond = (double)sampleRate * channels * bytesPerSample;
        if (bytesPerSecond <= 0d)
        {
            return false;
        }

        durationSeconds = (float)(dataLength / bytesPerSecond);
        return durationSeconds > 0f;
    }

    private static bool LooksLikeWhisperSilenceHallucination(string text)
    {
        string normalized = NormalizeForCompare(text);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        if (normalized.Contains("revisione a cura di") && normalized.Contains("qtss"))
        {
            return true;
        }

        if (normalized.Contains("sottotitoli") && normalized.Contains("amara org"))
        {
            return true;
        }

        return normalized == "thank you for watching";
    }

    private static bool LooksLikeModelRefusal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string value = text.Trim().ToLowerInvariant();
        string[] refusalMarkers =
        {
            "mi dispiace",
            "non posso",
            "non riesco",
            "non sono in grado",
            "non posso generare",
            "impossibile",
            "i cannot",
            "sorry"
        };

        for (int i = 0; i < refusalMarkers.Length; i++)
        {
            if (value.Contains(refusalMarkers[i]))
            {
                return true;
            }
        }

        return false;
    }

    private string PickFallbackPhrase()
    {
        string[] phrases =
        {
            "Questa mattina ho attraversato il mercato coperto della citta' antica, ho ascoltato i venditori raccontare la provenienza delle spezie, poi mi sono fermato davanti a una bottega di legno profumato, dove un artigiano paziente lucidava strumenti delicati mentre la pioggia leggera tamburellava sul tetto e l'aria portava odore di agrumi maturi dalla piazza vicina.",
            "Nel pomeriggio sono salito lungo un sentiero di pietra tra castagni e muretti, ho incontrato un pastore gentile che indicava le nuvole in arrivo e, quando il vento ha cambiato direzione, abbiamo cercato riparo in una vecchia stalla, parlando lentamente di stagioni, raccolti e memoria familiare, prima di rientrare con passo calmo verso il villaggio illuminato.",
            "Durante il viaggio in treno verso la costa ho letto ad alta voce alcune pagine annotate, osservando dal finestrino campi dorati, fiumi tranquilli e piccole stazioni dimenticate, finche' al tramonto una luce arancione ha riempito il vagone e tutti sono rimasti in silenzio, come davanti a una scena sospesa, mentre un bambino salutava dal corridoio con un sorriso curioso.",
            "Ieri sera, nella biblioteca del quartiere, una restauratrice mi ha mostrato manoscritti fragili, mappe sbiadite e fotografie d'epoca, spiegando con precisione come proteggere carta e inchiostro dall'umidita', mentre fuori le biciclette passavano veloci e una campana lontana scandiva il tempo con ritmo sorprendentemente calmo, e ogni pagina restituiva una voce antica ma presente.",
            "All'alba ho preparato il caffe' in una cucina stretta ma luminosa, ho aperto le finestre verso il cortile interno e ho sentito i primi rumori del panificio, poi ho scritto appunti ordinati su un quaderno blu, cercando parole nitide e semplici per descrivere una giornata intera senza fretta, concentrandomi sulla pronuncia chiara di ogni termine importante.",
            "Nel laboratorio sonoro abbiamo registrato passi, fruscii di tessuto, colpi metallici e respiri controllati, regolando con cura la distanza dai microfoni e la dinamica della voce, finche' una traccia pulita ha unito tutti i dettagli in modo coerente, restituendo un paesaggio acustico credibile, ricco e leggibile, con consonanti morbide, pause naturali e finali sonore.",
            "Quando la nebbia e' scesa sulla valle, il guardiano del faro ha acceso una lampada supplementare e ci ha invitati a osservare il mare dalla terrazza piu' alta, raccontando episodi di navigazione prudente, segnali di emergenza e rotte sicure, con una calma pratica che rendeva ogni istruzione chiara e affidabile, senza fretta e con tono costante.",
            "Stamattina, dopo una lunga riunione tecnica, abbiamo verificato cablaggi, alimentazione, sensori e connessioni di rete uno per uno, annotando anomalie minime e tempi di risposta, poi abbiamo ripetuto tutti i test in sequenza fino a ottenere risultati stabili, ripetibili e coerenti con le specifiche concordate, con attenzione alla dizione, al ritmo e alla respirazione costante."
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

    private static string DecodeJsonStringToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        string decoded = value
            .Replace("\\\"", "\"")
            .Replace("\\n", " ")
            .Replace("\\r", " ")
            .Replace("\\t", " ");
        try
        {
            decoded = Regex.Unescape(decoded);
        }
        catch
        {
            // Manteniamo la versione best-effort anche se l'unescape fallisce.
        }

        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string TryExtractJsonStringField(string json, string field)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        Match match = Regex.Match(
            json,
            $"\"{Regex.Escape(field)}\"\\s*:\\s*\"(?<v>(?:\\\\.|[^\"])*)\"",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return DecodeJsonStringToken(match.Groups["v"].Value);
    }

    private static int? TryExtractJsonIntField(string json, string field)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        Match match = Regex.Match(
            json,
            $"\"{Regex.Escape(field)}\"\\s*:\\s*(?<v>-?\\d+)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        if (int.TryParse(match.Groups["v"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }
        return null;
    }

    private static string CompactErrorBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        string compact = Regex.Replace(body, @"\s+", " ").Trim();
        const int maxLen = 260;
        if (compact.Length > maxLen)
        {
            compact = compact.Substring(0, maxLen) + "...";
        }
        return compact;
    }

    private static string ParseBackendErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        string message = TryExtractJsonStringField(body, "message");
        string code = TryExtractJsonStringField(body, "code");
        int? minRequired = TryExtractJsonIntField(body, "min_length_required");
        int? received = TryExtractJsonIntField(body, "received_length");
        int? wordsFound = TryExtractJsonIntField(body, "alpha_words_found");
        int? wordsMin = TryExtractJsonIntField(body, "alpha_words_min_required");

        if (!string.IsNullOrWhiteSpace(message))
        {
            if (received.HasValue && minRequired.HasValue)
            {
                message = $"{message} ({received.Value}/{minRequired.Value} caratteri)";
            }
            else if (wordsFound.HasValue && wordsMin.HasValue)
            {
                message = $"{message} ({wordsFound.Value}/{wordsMin.Value} parole)";
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                message = $"{message} [{code}]";
            }
            return message;
        }

        string detailAsString = TryExtractJsonStringField(body, "detail");
        if (!string.IsNullOrWhiteSpace(detailAsString))
        {
            return detailAsString;
        }

        return CompactErrorBody(body);
    }

    private static string BuildHttpError(UnityWebRequest request, string fallbackError)
    {
        string status = request != null ? request.error : null;
        long code = request != null ? request.responseCode : 0;
        if (string.IsNullOrWhiteSpace(status))
        {
            status = code > 0 ? $"HTTP {code}" : (string.IsNullOrWhiteSpace(fallbackError) ? "Network error" : fallbackError);
        }

        string body = request != null && request.downloadHandler != null ? request.downloadHandler.text : null;
        string detail = ParseBackendErrorDetail(body);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            return $"{status}: {detail}";
        }
        return status;
    }

    private static string BuildRememberUiErrorMessage(string rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "Errore salvataggio memoria. Riprova.";
        }

        string lower = rawError.ToLowerInvariant();
        if (lower.Contains("remember_text_too_short") || lower.Contains("min_length_required"))
        {
            int? minRequired = TryExtractJsonIntField(rawError, "min_length_required");
            int? received = TryExtractJsonIntField(rawError, "received_length");
            if (minRequired.HasValue && received.HasValue)
            {
                return $"Testo troppo corto: {received.Value}/{minRequired.Value} caratteri minimi.";
            }
            return "Testo troppo corto per il salvataggio memoria.";
        }

        if (lower.Contains("remember_not_enough_words"))
        {
            return "Servono almeno 2 parole per salvare un ricordo.";
        }

        if (lower.Contains("remember_text_not_supported"))
        {
            return "Testo non valido per il salvataggio memoria.";
        }

        return $"Errore salvataggio memoria: {rawError}";
    }

    private void ReportServiceError(string serviceName, string error)
    {
        string raw = string.IsNullOrWhiteSpace(error) ? "errore sconosciuto" : error;
        string lower = raw.ToLowerInvariant();
        string message;
        if (lower.Contains("502") || lower.Contains("bad gateway") || lower.Contains("connection refused"))
        {
            message = $"{serviceName} non raggiungibile: servizio non avviato o in crash.";
        }
        else if (lower.Contains("remember_text_too_short") || lower.Contains("min_length_required"))
        {
            message = "Memoria: testo troppo corto.";
        }
        else if (lower.Contains("400") || lower.Contains("422"))
        {
            message = $"{serviceName}: richiesta non valida.";
        }
        else if (lower.Contains("403") || lower.Contains("cors"))
        {
            message = $"CORS: controlla allow_origins sul servizio {serviceName}.";
        }
        else
        {
            message = $"Network: controlla stato servizio {serviceName} e reverse proxy.";
        }
        UpdateDebugText($"{message} ({raw})");
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
        Require(pnlLoading, "pnlLoading (Pnl_Loading)");

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
        Require(saveMemoryTitleText, "saveMemoryTitleText (Txt_SaveMemoryTitle)");
        Require(setupMemoryNoteInput, "setupMemoryNoteInput (Inp_Note)");
        Require(btnSetupMemorySave, "btnSetupMemorySave (Btn_SaveMemory)");
        Require(btnSetupMemoryIngest, "btnSetupMemoryIngest (Btn_Ingest)");
        Require(btnSetupMemoryDescribe, "btnSetupMemoryDescribe (Btn_Describe)");

        Require(mainModeStatusText, "mainModeStatusText (Txt_MainModeStatus)");
        Require(mainModeTranscriptText, "mainModeTranscriptText (Txt_MainModeTranscript)");
        Require(mainModeReplyText, "mainModeReplyText (Txt_MainModeReply)");

        if (touchUiActive)
        {
            ResolveTouchHintBarReferences();
            Require(touchHintBarObject, "touchHintBarObject (UI_HintBar_Touch)");
            Require(touchHintBarComponent, "UIHintBar component on touchHintBarObject");
        }
        else
        {
            Require(hintBar, "hintBar (UI_HintBar)");
        }
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
                SetTmpTextIfChanged(debugText, "Missing references:\n" + string.Join("\n", missing));
            }
        }
    }

    private void UpdateHintBar(UIState state)
    {
        if (touchUiActive)
        {
            UpdateTouchHintBar(state);
            UpdateTouchAvatarDeleteButtonVisual();
            SetTouchPttVisualState();
            return;
        }

        if (hintBar == null)
            return;

        // Frecce orizzontali in AvatarLibrary/MainMode.
        bool useHorizontal = state == UIState.AvatarLibrary || state == UIState.MainMode;
        hintBar.SetArrowsHorizontal(useHorizontal);
        bool showSpacePressed = (state == UIState.MainMode && mainModeListening) ||
                                (state == UIState.SetupVoice && setupVoiceRecording);
        hintBar.SetSpacePressed(showSpacePressed);

        // Se usiamo hintEntries nell'Inspector, teniamo il comportamento testuale di prima (tranne setup voce/main mode)
        if (hintEntries != null && hintEntries.Count > 0 && hintMap.TryGetValue(state, out var hints) &&
            state != UIState.SetupVoice && state != UIState.SetupMemory && state != UIState.MainMode && state != UIState.AvatarLibrary)
        {
            hintBar.SetHints($"{hints}   [INS] {GetDebugToggleLabel()}");
            return;
        }

        // Di base mostriamo i prompt da tastiera PC.
        var arrows = new UIHintBar.HintItem(UIHintBar.HintIcon.Arrows, "Seleziona");
        var enter = new UIHintBar.HintItem(UIHintBar.HintIcon.Enter, "Conferma");
        var debugToggle = new UIHintBar.HintItem(UIHintBar.HintIcon.Ins, GetDebugToggleLabel());

        if (state == UIState.Boot)
        {
            var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Skip");
            hintBar.SetHints(back, debugToggle);
        }
        else if (state == UIState.SetupVoice)
        {
            string label = setupVoiceRecording ? "Lascia per terminare" : "Tieni premuto per parlare";
            var recordSpace = new UIHintBar.HintItem(UIHintBar.HintIcon.Space, label);
            var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
            hintBar.SetHints(recordSpace, back, debugToggle);
        }
        else if (state == UIState.SetupMemory)
        {
            if (setupMemoryInputFocused)
            {
                string enterLabel = "Salva nota";
                if (IsInitialEmpiricalMandatoryMemoryFlowActive())
                {
                    if (IsCurrentEmpiricalStepReady())
                    {
                        enterLabel = empiricalSetupMemoryStepIndex >= empiricalMemorySteps.Length - 1
                            ? "Conferma"
                            : "Avanti";
                    }
                    else
                    {
                        enterLabel = "Completa testo";
                    }
                }

                var enterSave = new UIHintBar.HintItem(UIHintBar.HintIcon.Enter, enterLabel);
                var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
                hintBar.SetHints(enterSave, back, debugToggle);
            }
            else
            {
                var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
                if (CanOfferSetupMemoryDelete())
                {
                    var deleteMemory = new UIHintBar.HintItem(UIHintBar.HintIcon.Delete, "Cancella memoria");
                    hintBar.SetHints(arrows, enter, deleteMemory, back, debugToggle);
                }
                else
                {
                    hintBar.SetHints(arrows, enter, back, debugToggle);
                }
            }
        }
        else if (state == UIState.MainMenu)
        {
            var esc = new UIHintBar.HintItem(UIHintBar.HintIcon.Esc, "Chiudi programma");
            if (empiricalTestModeEnabled)
            {
                var testMode = new UIHintBar.HintItem(UIHintBar.HintIcon.T, GetEmpiricalTestModeHintLabel());
                hintBar.SetHints(arrows, enter, testMode, esc, debugToggle);
            }
            else
            {
                hintBar.SetHints(arrows, enter, esc, debugToggle);
            }
        }
        else if (state == UIState.AvatarLibrary)
        {
            var deleteHint = new UIHintBar.HintItem(UIHintBar.HintIcon.Delete, GetAvatarLibraryDeleteLabel());
            var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
            if (empiricalTestModeEnabled)
            {
                var space = new UIHintBar.HintItem(UIHintBar.HintIcon.Space, GetEmpiricalAvatarCarouselStatusLabel());
                hintBar.SetHints(arrows, enter, space, deleteHint, back, debugToggle);
            }
            else
            {
                hintBar.SetHints(arrows, enter, deleteHint, back, debugToggle);
            }
        }
        else if (state == UIState.MainMode)
        {
            if (mainModeChatNoteActive)
            {
                var enterConfirm = new UIHintBar.HintItem(UIHintBar.HintIcon.Enter, "Conferma testo");
                var deleteCancel = new UIHintBar.HintItem(UIHintBar.HintIcon.Delete, "Annulla l'inserimento");
                hintBar.SetHints(enterConfirm, deleteCancel, debugToggle);
            }
            else
            {
                string label = mainModeListening ? "Lascia per terminare" : "Tieni premuto per parlare";
                var talk = new UIHintBar.HintItem(UIHintBar.HintIcon.Space, label);
                var any = new UIHintBar.HintItem(UIHintBar.HintIcon.Any, "Digita per scrivere");
                string backLabel = mainModeExitConfirmPending ? "Confermi uscita?" : "Indietro";
                var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, backLabel);
                if (AreMainModeSetupButtonsAvailable())
                {
                    hintBar.SetHints(arrows, talk, any, back, debugToggle);
                }
                else
                {
                    hintBar.SetHints(talk, any, back, debugToggle);
                }
            }
        }
        else
        {
            var back = new UIHintBar.HintItem(UIHintBar.HintIcon.Backspace, "Indietro");
            hintBar.SetHints(arrows, enter, back, debugToggle);
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
            avatarManager.SetCurrentAvatarVisible(ShouldShowAvatarInState(state));
        }
        if (postProcessingBootstrap != null)
        {
            postProcessingBootstrap.SetAvatarForegroundScatterActive(ShouldShowAvatarInState(state));
        }

        UpdateRingsForState(state);
        if (ringsController != null)
        {
            if (state == UIState.Boot)
            {
                if (coquiInitializationRingsVisualActive)
                {
                    ringsController.SetOrbitSpeedMultiplier(GetCoquiBootRingsSlowMultiplier());
                }
                else
                {
                    ringsController.SetOrbitSpeedMultiplier(bootRingsSpeedMultiplier);
                }
            }
            else if (!downloadStateActive)
            {
                ringsController.SetOrbitSpeedMultiplier(1f);
                coquiInitializationRingsVisualActive = false;
                if (postProcessingBootstrap != null)
                {
                    postProcessingBootstrap.SetInitializationScatterPulseActive(false);
                }
            }
        }
    }

    private static bool ShouldShowAvatarInState(UIState state)
    {
        return state == UIState.SetupVoice ||
               state == UIState.SetupMemory ||
               state == UIState.MainMode;
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
                if (btnNewAvatar != null) selectables.Add(btnNewAvatar);
                if (btnShowList != null) selectables.Add(btnShowList);
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
                        if (touchUiActive)
                        {
                            if (btnTouchConfirmSetMemory != null) selectables.Add(btnTouchConfirmSetMemory);
                            if (btnTouchCancelSetMemory != null) selectables.Add(btnTouchCancelSetMemory);
                        }
                        else if (btnSetMemory != null)
                        {
                            selectables.Add(btnSetMemory);
                        }
                    }
                }
                break;
            case UIState.MainMode:
                axisMode = UINavigator.AxisMode.Horizontal;
                if (!mainModeChatNoteActive)
                {
                    if (AreMainModeSetupButtonsAvailable())
                    {
                        if (btnMainModeMemory != null) selectables.Add(btnMainModeMemory);
                        if (btnMainModeVoice != null) selectables.Add(btnMainModeVoice);
                    }
                }
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
            // Come richiesto, qui nascondiamo tutto.
            mainMenuCanvasGroup.alpha = 0f; 
            mainMenuCanvasGroup.interactable = false;
            mainMenuCanvasGroup.blocksRaycasts = false;
        }
        
        // Qui nascondiamo Title e HintBar in modo esplicito.
        if (_titleCanvasGroup != null) _titleCanvasGroup.alpha = 0f;
        SetHintBarVisible(false);

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
        
        // Qui ripristiniamo Title e HintBar.
        if (!carouselDownloading && !_previewModeActive)
        {
            SetTitleVisible(true);
            SetHintBarVisible(true);
        }

        SetMainMenuButtonsVisible(true, immediate: true);

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
        public string system;
        public string session_id;
        public string input_mode;
        public bool log_conversation;
        public bool empirical_test_mode;
    }

    [System.Serializable]
    private class RagChatResponse
    {
        public string text;
        public bool auto_remembered;
    }

    [System.Serializable]
    private class ChatSessionStartPayload
    {
        public string avatar_id;
        public bool empirical_test_mode;
    }

    [System.Serializable]
    private class ChatSessionStartResponse
    {
        public bool ok;
        public string avatar_id;
        public string session_id;
        public string log_file;
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
        public bool empirical_test_mode;
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
    private class SnapshotOperationResponse
    {
        public bool ok;
        public string avatar_id;
        public bool backed_up;
        public bool restored;
        public bool empirical_test_mode;
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

    [System.Serializable]
    private class TtsTimingSnapshotResponse
    {
        public bool ok;
        public string request_id;
        public string[] words;
        public int[] word_end_ms;
        public int[] segment_end_ms;
        public bool complete;
        public string error;
    }
}
