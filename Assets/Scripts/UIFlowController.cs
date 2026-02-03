using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

public class UIFlowController : MonoBehaviour
{
    public enum UIState
    {
        MainMenu,
        AvatarLibrary,
        AvatarReady,
        SetupVoice,
        SetupMemory,
        MainMode
    }

    [Header("Panels")]
    public GameObject pnlMainMenu;
    public GameObject pnlAvatarLibrary;
    public GameObject pnlAvatarReady;
    public GameObject pnlSetupVoice;
    public GameObject pnlSetupMemory;
    public GameObject pnlMainMode;

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

    [Header("List Panel")]
    public Transform listContent;
    public GameObject listItemPrefab;

    [Header("Avatar Ready Panel")]
    public TextMeshProUGUI avatarInfoText;

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
    [SerializeField] private Vector3 ringsHiddenOffset = new Vector3(0f, 0f, 2f);
    [SerializeField] private float ringsTransitionDuration = 0.6f;
    [SerializeField] private PS2BackgroundRings ringsController;

    [Header("Download State")]
    [SerializeField] private float downloadRingsSpeedMultiplier = 2f;


    [Header("Transitions")]
    [SerializeField] private float transitionDuration = 0.25f;
    [SerializeField] private float slideOffset = 40f;

    [Header("References")]
    public AvaturnSystem avaturnSystem;
    public AvatarManager avatarManager;
    public Transform avatarSpawnPoint;

    private AvaturnWebController webController;
    private readonly List<GameObject> avatarListItems = new List<GameObject>();
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

    // Fix 1: Flag per controllare se la transizione a AvatarReady è stata richiesta dall'utente
    private bool _pendingAvatarReadyTransition = false;
    private bool _previewModeActive = false;

    public bool IsWebOverlayOpen => webOverlayOpen;

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

        if (avatarManager == null)
        {
            avatarManager = FindFirstObjectByType<AvatarManager>();
            Debug.Log($"AvatarManager trovato: {avatarManager != null}");
        }

        if (avaturnSystem == null)
        {
            avaturnSystem = FindFirstObjectByType<AvaturnSystem>();
            Debug.Log($"AvaturnSystem trovato: {avaturnSystem != null}");
        }

        if (hintBar == null)
        {
            hintBar = FindFirstObjectByType<UIHintBar>();
        }

        if (avatarLibraryCarousel == null)
        {
            avatarLibraryCarousel = FindFirstObjectByType<AvatarLibraryCarousel>();
        }

        if (avatarLibraryCarousel != null)
        {
            avatarLibraryCarousel.Initialize(avatarManager, this);
        }

        if (ringsTransform == null)
        {
            var ringsObject = GameObject.Find("VFX_BackgroundRings");
            if (ringsObject != null)
            {
                ringsTransform = ringsObject.transform;
            }
        }

        if (ringsTransform != null)
        {
            ringsDefaultPosition = ringsTransform.position;
        }

        if (ringsController == null && ringsTransform != null)
        {
            ringsController = ringsTransform.GetComponent<PS2BackgroundRings>();
        }
        
        // Fix 1: Risolvi il CanvasGroup del titolo
        ResolveTitleGroup();

        if (btnNewAvatar != null) btnNewAvatar.onClick.AddListener(OnNewAvatar);
        if (btnShowList != null) btnShowList.onClick.AddListener(GoToAvatarLibrary);
        
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

        if (navigator == null)
        {
            navigator = FindFirstObjectByType<UINavigator>();
            if (navigator == null)
            {
                navigator = gameObject.AddComponent<UINavigator>();
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

        SetStateImmediate(UIState.MainMenu);

        UpdateHintBar(UIState.MainMenu);

        UpdateDebugText("Sistema pronto. Clicca 'Nuovo Avatar' per iniziare.");

        if (avaturnSystem != null)
        {
            avaturnSystem.SetupAvatarCallbacks(OnAvatarReceived, null);
        }

        webController = FindFirstObjectByType<AvaturnWebController>();
        
        // Fix 1: Avvia intro se abilitata
        if (enableMainMenuIntro)
        {
            StartCoroutine(MainMenuButtonsIntroRoutine());
        }
    }

    private void BuildPanelMap()
    {
        panelMap.Clear();
        panelMap[UIState.MainMenu] = pnlMainMenu;
        panelMap[UIState.AvatarLibrary] = pnlAvatarLibrary;
        panelMap[UIState.AvatarReady] = pnlAvatarReady;
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
            var titleObj = GameObject.Find("Title_SOULFRAME");
            if (titleObj != null)
            {
                _titleCanvasGroup = titleObj.GetComponent<CanvasGroup>();
                if (_titleCanvasGroup == null)
                {
                    Debug.LogWarning("[UIFlowController] Title_SOULFRAME trovato ma senza CanvasGroup");
                }
            }
        }
        
        // Crea CanvasGroup per i bottoni se non esistono
        if (btnNewAvatar != null)
        {
            _btnNewAvatarGroup = btnNewAvatar.GetComponent<CanvasGroup>();
            if (_btnNewAvatarGroup == null)
            {
                _btnNewAvatarGroup = btnNewAvatar.gameObject.AddComponent<CanvasGroup>();
            }
        }
        
        if (btnShowList != null)
        {
            _btnShowListGroup = btnShowList.GetComponent<CanvasGroup>();
            if (_btnShowListGroup == null)
            {
                _btnShowListGroup = btnShowList.gameObject.AddComponent<CanvasGroup>();
            }
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
            hintMap[UIState.MainMenu] = "[X] Enter   [Tri] Close";
            hintMap[UIState.AvatarLibrary] = "[X] Select   [O] Back";
            hintMap[UIState.AvatarReady] = "[X] Play   [O] Close";
            hintMap[UIState.SetupVoice] = "[X] Record   [O] Back";
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
        _pendingAvatarReadyTransition = false;
        pendingNewAvatarDownload = false;
        ExitDownloadState();
        avatarManager?.CancelAllDownloads();
        backStack.Clear();
        GoToState(UIState.MainMenu);
    }

    // Fix 1: Metodo pubblico per notificare che l'utente ha richiesto un main avatar load
    public void NotifyMainAvatarLoadRequested()
    {
        _pendingAvatarReadyTransition = true;
        Debug.Log("[UIFlowController] Main avatar load requested by user.");
    }

    public void GoToAvatarLibrary()
    {
        GoToState(UIState.AvatarLibrary);
        if (avatarLibraryCarousel == null)
        {
            PopulateAvatarList();
        }
    }

    public void GoToAvatarReady()
    {
        avatarManager?.CancelAllDownloads();
        GoToState(UIState.AvatarReady);
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

    public void GoBack()
    {
        // Al menu principale Backspace non deve innescare alcuna transizione.
        if (currentState == UIState.MainMenu)
            return;

        // FIX BACKSPACE: In AvatarReady, rimuovi l'avatar corrente prima di tornare indietro
        if (currentState == UIState.AvatarReady)
        {
            if (avatarManager != null)
            {
                avatarManager.RemoveCurrentAvatar();
                Debug.Log("[UIFlowController] Avatar rimosso tramite Backspace da AvatarReady");
            }
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

    private void SetState(UIState targetState, bool pushCurrent)
    {
        if (currentState.Equals(targetState))
        {
            return;
        }

        if (pushCurrent)
        {
            backStack.Push(currentState);
        }

        GameObject fromPanel = GetPanel(currentState);
        GameObject toPanel = GetPanel(targetState);

        currentState = targetState;

        UpdateHintBar(targetState);
        ConfigureNavigatorForState(targetState, true);
        UpdateStateEffects(targetState);

        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
        }

        transitionRoutine = StartCoroutine(TransitionPanels(fromPanel, toPanel));
    }

    private void SetStateImmediate(UIState targetState)
    {
        currentState = targetState;

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

    // OnCloseAvatarClick() rimosso - ora la pulizia avatar avviene via GoBack() da AvatarReady

    void PopulateAvatarList()
    {
        foreach (var item in avatarListItems)
        {
            Destroy(item);
        }
        avatarListItems.Clear();

        if (avatarManager != null && avatarManager.SavedData != null)
        {
            foreach (var avatarData in avatarManager.SavedData.avatars)
            {
                GameObject listItem = Instantiate(listItemPrefab, listContent);
                avatarListItems.Add(listItem);

                TextMeshProUGUI text = listItem.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null)
                {
                    text.text = $"Avatar: {avatarData.avatarId} ({avatarData.gender})";
                }

                Button itemButton = listItem.GetComponent<Button>();
                if (itemButton != null)
                {
                    var data = avatarData;
                    itemButton.onClick.AddListener(() => LoadAvatarFromList(data));
                }
            }
        }

        if (avatarListItems.Count == 0)
        {
            GameObject emptyItem = Instantiate(listItemPrefab, listContent);
            TextMeshProUGUI text = emptyItem.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = "Nessun avatar salvato";
            }
            var emptyButton = emptyItem.GetComponent<Button>();
            if (emptyButton != null)
            {
                emptyButton.interactable = false;
            }
            avatarListItems.Add(emptyItem);
        }

        ConfigureNavigatorForState(currentState, true);
    }

    void LoadAvatarFromList(AvatarManager.AvatarData avatarData)
    {
        if (avatarManager != null)
        {
            NotifyMainAvatarLoadRequested();
            avatarManager.LoadSavedAvatar(avatarData);
            GoToAvatarReady();
        }
    }

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
        if (_pendingAvatarReadyTransition)
        {
            _pendingAvatarReadyTransition = false;
            GoToAvatarReady();
        }
        else
        {
            // preview completata: NON cambiare pannello
            Debug.Log("[UIFlowController] Download completato senza transizione (preview).");
        }

        if (avatarInfoText != null && avatarTransform != null)
        {
            avatarInfoText.text = $"Avatar: {avatarTransform.name}\nPosizione: {avatarSpawnPoint.position}";
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

    private void UpdateHintBar(UIState state)
    {
        if (hintBar == null)
            return;

        // Se usi hintEntries nell'Inspector, mantieni il vecchio comportamento testuale
        if (hintEntries != null && hintEntries.Count > 0 && hintMap.TryGetValue(state, out var hints))
        {
            hintBar.SetHints(hints);
            return;
        }

        // Horizontal arrows in AvatarReady/AvatarLibrary
        bool useHorizontal = state == UIState.AvatarReady || state == UIState.AvatarLibrary;
        hintBar.SetArrowsHorizontal(useHorizontal);

        // Default: PC keyboard prompts
        var arrows = new UIHintBar.HintItem(UIHintBar.HintIcon.Arrows, "Seleziona");
        var enter = new UIHintBar.HintItem(UIHintBar.HintIcon.Enter, state == UIState.AvatarLibrary ? "Conferma" : "Seleziona");

        if (state == UIState.MainMenu)
        {
            var esc = new UIHintBar.HintItem(UIHintBar.HintIcon.Esc, "Chiudi programma");
            hintBar.SetHints(arrows, enter, esc);
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
                // Lista classica: bottoni avatar come selectables
                if (avatarLibraryCarousel == null)
                {
                    foreach (var item in avatarListItems)
                    {
                        if (item == null) continue;
                        var button = item.GetComponent<Button>();
                        if (button != null) selectables.Add(button);
                    }
                    axisMode = UINavigator.AxisMode.Vertical;
                }
                else
                {
                    axisMode = UINavigator.AxisMode.Horizontal;
                }
                break;
            case UIState.AvatarReady:
                axisMode = UINavigator.AxisMode.Horizontal;
                break;
            case UIState.SetupVoice:
            case UIState.SetupMemory:
            case UIState.MainMode:
                axisMode = UINavigator.AxisMode.Vertical;
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

        bool hideRings = state != UIState.MainMenu;
        Vector3 offset = ringsHiddenOffset;
        if (Camera.main != null)
        {
            offset = Camera.main.transform.right * ringsHiddenOffset.x +
                     Camera.main.transform.up * ringsHiddenOffset.y +
                     Camera.main.transform.forward * ringsHiddenOffset.z;
        }

        Vector3 target = hideRings ? ringsDefaultPosition + offset : ringsDefaultPosition;

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
}
