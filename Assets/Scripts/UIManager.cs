using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif


public class UIManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject homePanel;
    public GameObject listPanel;
    public GameObject gamePanel;

    [Header("Home Panel")]
    public Button btnNewAvatar;
    public Button btnShowList;
    public TextMeshProUGUI debugText;

    [Header("List Panel")]
    public Transform listContent;
    public GameObject listItemPrefab;
    public Button btnBackFromList;

    [Header("Game Panel")]
    public Button btnCloseAvatar;
    public Button btnBackToHome;
    public TextMeshProUGUI avatarInfoText;

    [Header("References")]
    public AvaturnSystem avaturnSystem;
    public AvatarManager avatarManager;
    public Transform avatarSpawnPoint;

    // Aggiungiamo un riferimento privato a AvaturnWebController
    private AvaturnWebController webController;

    private List<GameObject> avatarListItems = new List<GameObject>();

#if UNITY_WEBGL && !UNITY_EDITOR
[DllImport("__Internal")] private static extern int EnsureDynCallV();
#endif


    void Start()
    {
        Debug.Log("UIManager Start chiamato");

#if UNITY_WEBGL && !UNITY_EDITOR
try {
    int ok = EnsureDynCallV();
    Debug.Log("[WebGL] EnsureDynCallV => " + ok);
} catch (System.Exception e) {
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

        // Setup button listeners
        btnNewAvatar.onClick.AddListener(OnNewAvatar);
        btnShowList.onClick.AddListener(OnShowList);
        btnBackFromList.onClick.AddListener(OnBackFromList);
        btnCloseAvatar.onClick.AddListener(OnCloseAvatarClick);
        btnBackToHome.onClick.AddListener(OnBackToHome);

        // Initially hide close avatar button
        btnCloseAvatar.gameObject.SetActive(false);

        // Show home panel
        ShowPanel("HOME");

        // Setup debug text
        UpdateDebugText("Sistema pronto. Clicca 'Nuovo Avatar' per iniziare.");

        // Setup Avaturn callbacks
        if (avaturnSystem != null)
        {
            avaturnSystem.SetupAvatarCallbacks(OnAvatarReceived, null);
        }

        // Trova AvaturnWebController se non assegnato
        webController = FindFirstObjectByType<AvaturnWebController>();
    }

    void OnNewAvatar()
    {
#if UNITY_EDITOR
        // Usa l'URL da AvaturnSystem
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
            // Fallback su AvaturnSystem
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

    void OnShowList()
    {
        ShowPanel("LIST");
        PopulateAvatarList();
    }

    void OnBackFromList()
    {
        ShowPanel("HOME");
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

    void OnBackToHome()
    {
        ShowPanel("HOME");
    }

    public void ShowPanel(string panelName)
    {
        homePanel.SetActive(panelName == "HOME");
        listPanel.SetActive(panelName == "LIST");
        gamePanel.SetActive(panelName == "GAME");
    }

    void PopulateAvatarList()
    {
        // Clear existing items
        foreach (var item in avatarListItems)
        {
            Destroy(item);
        }
        avatarListItems.Clear();

        // Load saved avatars from AvatarManager
        if (avatarManager != null && avatarManager.SavedData != null)
        {
            foreach (var avatarData in avatarManager.SavedData.avatars)
            {
                GameObject listItem = Instantiate(listItemPrefab, listContent);
                avatarListItems.Add(listItem);

                // Setup list item
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

    // Aggiorna LoadAvatarFromList:
    void LoadAvatarFromList(AvatarManager.AvatarData avatarData)
    {
        if (avatarManager != null)
        {
            avatarManager.LoadSavedAvatar(avatarData);
            ShowPanel("GAME");
        }
    }

    public void OnAvatarReceived(Avaturn.Core.Runtime.Scripts.Avatar.Data.AvatarInfo avatarInfo)
    {
        UpdateDebugText($"Avatar ricevuto: {avatarInfo.AvatarId}");

        // Process through AvatarManager
        if (avatarManager != null)
        {
            avatarManager.OnAvatarReceived(avatarInfo);
        }
    }

    // Chiamato da AvatarManager quando inizia il download
    public void OnAvatarDownloadStarted(Avaturn.Core.Runtime.Scripts.Avatar.Data.AvatarInfo avatarInfo)
    {
        UpdateDebugText("Download avatar in corso...");
    }

    // Chiamato da AvatarManager quando il download è completato
    public void OnAvatarDownloaded(Transform avatarTransform)
    {
        UpdateDebugText("Avatar caricato nella scena!");

        // Show close button
        btnCloseAvatar.gameObject.SetActive(true);

        // Show game panel
        ShowPanel("GAME");

        // Update avatar info text
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
        Debug.Log("[UIManager] " + message);
    }

    // Method called from JavaScript bridge
    public void OnAvatarJsonReceived(string json)
    {
        Debug.Log("JSON ricevuto dal bridge: " + json);

        try
        {
            // Passa direttamente ad AvatarManager per l'elaborazione
            if (avatarManager != null)
            {
                // Usa reflection per chiamare il nuovo metodo
                var method = avatarManager.GetType().GetMethod("OnAvatarJsonReceived");
                if (method != null)
                {
                    method.Invoke(avatarManager, new object[] { json });
                }
                else
                {
                    // Fallback al metodo precedente
                    var jsonData = JsonUtility.FromJson<AvatarJsonData>(json);

                    // Controlla se è un messaggio di chiusura
                    if (jsonData.status == "closed")
                    {
                        UpdateDebugText("Avaturn chiuso");
                        return;
                    }

                    // Convert to AvatarInfo
                    var avatarInfo = new Avaturn.Core.Runtime.Scripts.Avatar.Data.AvatarInfo(
                        jsonData.url,
                        jsonData.urlType,
                        jsonData.bodyId,
                        jsonData.gender,
                        jsonData.avatarId
                    );

                    OnAvatarReceived(avatarInfo);
                }
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
        public string status; // <--- AGGIUNGI QUESTA RIGA
    }
}