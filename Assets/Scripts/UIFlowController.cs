using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
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

    [Header("List Panel")]
    public Transform listContent;
    public GameObject listItemPrefab;
    public Button btnBackFromList;

    [Header("Avatar Ready Panel")]
    public Button btnCloseAvatar;
    public Button btnBackToHome;
    public TextMeshProUGUI avatarInfoText;

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

    private UIState currentState;
    private Coroutine transitionRoutine;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int EnsureDynCallV();
#endif

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

        btnNewAvatar.onClick.AddListener(OnNewAvatar);
        btnShowList.onClick.AddListener(GoToAvatarLibrary);
        btnBackFromList.onClick.AddListener(GoBack);
        btnCloseAvatar.onClick.AddListener(OnCloseAvatarClick);
        btnBackToHome.onClick.AddListener(GoToMainMenu);

        btnCloseAvatar.gameObject.SetActive(false);

        BuildPanelMap();
        CachePanelDefaults();

        SetStateImmediate(UIState.MainMenu);

        UpdateDebugText("Sistema pronto. Clicca 'Nuovo Avatar' per iniziare.");

        if (avaturnSystem != null)
        {
            avaturnSystem.SetupAvatarCallbacks(OnAvatarReceived, null);
        }

        webController = FindFirstObjectByType<AvaturnWebController>();
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
#if UNITY_EDITOR
        string url = avaturnSystem != null ? avaturnSystem.GetAvaturnUrl() : "https://demo.avaturn.dev";
        Application.OpenURL(url);
        UpdateDebugText("Editor: Apri URL nel browser esterno");
#elif UNITY_WEBGL && !UNITY_EDITOR
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
        backStack.Clear();
        backStack.Clear();
        GoToState(UIState.MainMenu);
    }

    public void GoToAvatarLibrary()
    {
        GoToState(UIState.AvatarLibrary);
        PopulateAvatarList();
    }

    public void GoToAvatarReady()
    {
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

    void OnCloseAvatarClick()
    {
        if (avatarManager != null)
        {
            avatarManager.RemoveCurrentAvatar();
            btnCloseAvatar.gameObject.SetActive(false);
            UpdateDebugText("Avatar rimosso dalla scena");
        }
    }

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
            emptyItem.GetComponent<Button>().interactable = false;
            avatarListItems.Add(emptyItem);
        }
    }

    void LoadAvatarFromList(AvatarManager.AvatarData avatarData)
    {
        if (avatarManager != null)
        {
            avatarManager.LoadSavedAvatar(avatarData);
            GoToAvatarReady();
        }
    }

    public void OnAvatarReceived(Avaturn.Core.Runtime.Scripts.Avatar.Data.AvatarInfo avatarInfo)
    {
        UpdateDebugText($"Avatar ricevuto: {avatarInfo.AvatarId}");

        if (avatarManager != null)
        {
            avatarManager.OnAvatarReceived(avatarInfo);
        }
    }

    public void OnAvatarDownloadStarted(Avaturn.Core.Runtime.Scripts.Avatar.Data.AvatarInfo avatarInfo)
    {
        UpdateDebugText("Download avatar in corso...");
    }

    public void OnAvatarDownloaded(Transform avatarTransform)
    {
        UpdateDebugText("Avatar caricato nella scena!");

        btnCloseAvatar.gameObject.SetActive(true);

        GoToAvatarReady();

        if (avatarInfoText != null && avatarTransform != null)
        {
            avatarInfoText.text = $"Avatar: {avatarTransform.name}\nPosizione: {avatarSpawnPoint.position}";
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

    public void OnAvatarJsonReceived(string json)
    {
        Debug.Log("JSON ricevuto dal bridge: " + json);

        try
        {
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
