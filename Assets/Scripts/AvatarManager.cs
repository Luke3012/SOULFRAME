using System;
using Avaturn.Core.Runtime.Scripts.Avatar;
using Avaturn.Core.Runtime.Scripts.Avatar.Data;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_WEBGL || UNITY_EDITOR
using GLTFast;
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

public class AvatarManager : MonoBehaviour
{
    [System.Serializable]
    public class SavedAvatarData
    {
        public List<AvatarData> avatars = new List<AvatarData>();
    }

    [System.Serializable]
    public class AvatarData
    {
        public string avatarId;
        public string url;          // <-- SALVIAMO L’URL VERO (httpURL)
        public string urlType;
        public string bodyId;
        public string gender;
        public string source;
        public string localFile;
        public string displayName;
        public string sourceUrl;

        // opzionale (solo non-webgl)
        public string fileName;
        public bool isWebGL;

        public AvatarData()
        {
        }

        public AvatarData(AvatarInfo info, bool isWebGL)
        {
            avatarId = info.AvatarId;
            url = info.Url;
            urlType = info.UrlType;
            bodyId = info.BodyId;
            gender = info.Gender;
            this.isWebGL = isWebGL;
            source = "avaturn";
            sourceUrl = info.Url;

            fileName = $"avatar_{avatarId}.glb";
        }

        public AvatarInfo ToAvatarInfo()
        {
            // Se abbiamo già un URL valido (http/file) o è un modello locale, usa quello e basta
            if (!string.IsNullOrEmpty(url) &&
                (isWebGL || url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://") ||
                 AvatarManager.IsLocalAvatarId(bodyId, avatarId)))
            {
                return new AvatarInfo(url, urlType, bodyId, gender, avatarId);
            }

            // Ripiego 1: se esiste un salvataggio, usiamo quello.
            var path = Path.Combine(Application.persistentDataPath, fileName);
            if (File.Exists(path))
                return new AvatarInfo($"file://{path}", urlType, bodyId, gender, avatarId);

            // Ultimo fallback: proviamo comunque l'URL salvato.
            return new AvatarInfo(url, urlType, bodyId, gender, avatarId);
        }
    }

    [Header("Riferimenti Core (Legacy non-WebGL)")]
    public DownloadAvatar downloadAvatar;
    public Transform spawnPoint;

    [Header("UI Reference")]
    public UIFlowController uiFlowController;

    [Header("Avatar Anchor")]
    public Transform avatarAnchor;
    public Transform baseModelToReplace; // opzionale: placeholder legacy
    public bool hideBaseModel = true;
    public bool destroyBaseModel = true;


    [Header("Local test models (StreamingAssets)")]
    [SerializeField] private bool addLocalTestModels = true;
    [SerializeField] private string localModel1FileName = "model1.glb";
    [SerializeField] private string localModel1AvatarId = "LOCAL_model1";
    [SerializeField] private string localModel1Gender = "male";
    [SerializeField] private string localModel2FileName = "model2.glb";
    [SerializeField] private string localModel2AvatarId = "LOCAL_model2";
    [SerializeField] private string localModel2Gender = "male";

    [Header("Animation")]
    public AvaturnIdleLookAndBlink idleLook;

    [Header("Download Watchdog")]
    [SerializeField] private float previewTimeoutSeconds = 15f;
    [SerializeField] private float mainTimeoutSeconds = 45f;

    [Header("Backend (WebGL)")]
    [SerializeField] private string backendBaseUrl = "http://127.0.0.1:8003";
    [SerializeField] private int webglRequestTimeoutSeconds = 60;
    [SerializeField] private int minGlbBytes = 100 * 1024;

    public SavedAvatarData SavedData => savedData;
    public bool IsDownloading => _isMainDownloading;
    public bool IsPreviewDownloading => _isPreviewDownloading;
    // Proprieta' legacy di compatibilita', ormai quasi inutilizzata.
    public bool IsPreviewDownloadActive => _isPreviewDownloading;
    public string CurrentAvatarId => currentDownloadAvatarId;
    public string CurrentAvatarGender
    {
        get
        {
            if (string.IsNullOrEmpty(currentDownloadAvatarId))
                return "unknown";

            var avatarData = savedData?.avatars?.Find(a => a.avatarId == currentDownloadAvatarId);
            return avatarData != null ? avatarData.gender : "unknown";
        }
    }

    private SavedAvatarData savedData;
    private string savePath;
    private GameObject currentAvatar;
    private readonly List<AvatarTintTarget> currentAvatarTintTargets = new List<AvatarTintTarget>();
    private bool currentAvatarTintCacheValid;
    private bool currentAvatarDimmed;
    private float currentAvatarDimMultiplier = 1f;
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    
    // Stati download separati.
    private bool _isMainDownloading;
    private bool _isPreviewDownloading;
    private int _previewSessionId;
    private DownloadAvatar _previewDownloader;

    private bool fileSystemReady;
    private int _watchId;
    private string currentDownloadAvatarId;
    private bool cancelPendingDownloads;
    private int _webglMainSessionId;
    private int _webglPreviewSessionId;

    private Transform _anchorParent;
    private Vector3 _anchorLocalPos;
    private Quaternion _anchorLocalRot;
    private Vector3 _anchorLocalScale;
    private bool _anchorPoseCached;

    public AvaturnULipSyncBinder lipSyncBinder;
    public ULipSyncProfileRouter profileRouter;

    private struct AvatarTintTarget
    {
        public Renderer renderer;
        public int materialIndex;
        public int colorPropertyId;
        public Color baseColor;
    }


    void Awake()
    {
        savePath = Path.Combine(Application.persistentDataPath, "Avatars.json");
        Debug.Log($"[AvatarManager] persistentDataPath={Application.persistentDataPath}");

#if UNITY_WEBGL && !UNITY_EDITOR
        backendBaseUrl = NormalizeAvatarBackendBaseUrl(backendBaseUrl);
        Debug.Log($"[AvatarManager] WebGL backendBaseUrl={backendBaseUrl}");
        StartCoroutine(WebGLPopulateAndLoad());
        if (downloadAvatar != null)
        {
            Debug.Log("[AvatarManager] WebGL path active: DownloadAvatar is ignored.");
        }
#else
        LoadData();
        if (addLocalTestModels)
            EnsureLocalModelInList();

    // Parametri WebGL: restano serializzati/modificabili, ma li usiamo solo nelle build WebGL.
    // Li tocchiamo qui per evitare avvisi CS0414 in compilazione Editor/Standalone.
    _ = backendBaseUrl;
    _ = webglRequestTimeoutSeconds;
    _ = minGlbBytes;
#endif

        if (downloadAvatar != null)
            downloadAvatar.SetOnDownloaded(OnMainAvatarDownloaded);

        // Qui memorizziamo in cache la posa relativa al parent reale, cosi' evitiamo teletrasporti.
        if (avatarAnchor != null)
            _anchorParent = avatarAnchor;
        else if (baseModelToReplace != null)
            _anchorParent = baseModelToReplace.parent;

        if (_anchorParent != null)
        {
            if (baseModelToReplace != null)
            {
                // posa del baseModel nello spazio locale di _anchorParent
                _anchorLocalPos = _anchorParent.InverseTransformPoint(baseModelToReplace.position);
                _anchorLocalRot = Quaternion.Inverse(_anchorParent.rotation) * baseModelToReplace.rotation;

                // scala relativa (robusta anche se i parent differiscono)
                Vector3 p = _anchorParent.lossyScale;
                Vector3 b = baseModelToReplace.lossyScale;
                _anchorLocalScale = new Vector3(
                    p.x != 0f ? b.x / p.x : 1f,
                    p.y != 0f ? b.y / p.y : 1f,
                    p.z != 0f ? b.z / p.z : 1f
                );
            }
            else
            {
                _anchorLocalPos = Vector3.zero;
                _anchorLocalRot = Quaternion.identity;
                _anchorLocalScale = Vector3.one;
            }

            _anchorPoseCached = true;
            Debug.Log($"[AvatarManager] Anchor pose cached: parent={_anchorParent.name} pos={_anchorLocalPos}");
        }
        // Qui chiudiamo la gestione della cache posa.
    }

    private static string NormalizeAvatarBackendBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/api/avatar";
        }

        string trimmed = value.Trim().TrimEnd('/');
        if (trimmed.StartsWith("/"))
        {
            return trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri))
        {
            return trimmed;
        }

        string host = uri.Host.ToLowerInvariant();
        bool isLoopback = host == "127.0.0.1" || host == "localhost" || host == "::1";
        if (isLoopback && uri.Port == 8003)
        {
            return IsCurrentWebPageLoopbackHost() ? trimmed : "/api/avatar";
        }

        return trimmed;
    }

    private static bool IsCurrentWebPageLoopbackHost()
    {
        string currentUrl = Application.absoluteURL;
        if (string.IsNullOrWhiteSpace(currentUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out Uri uri))
        {
            return false;
        }

        string host = uri.Host.ToLowerInvariant();
        return host == "127.0.0.1" || host == "localhost" || host == "::1";
    }

    private static bool IsLocalAvatarId(string bodyId, string avatarId)
    {
        return bodyId == "local" || (!string.IsNullOrEmpty(avatarId) && avatarId.StartsWith("LOCAL_"));
    }

    private string BuildAvatarServiceUrl(string pathAndQuery)
    {
        string baseUrl = string.IsNullOrWhiteSpace(backendBaseUrl) ? "/api/avatar" : backendBaseUrl.Trim().TrimEnd('/');
        if (!pathAndQuery.StartsWith("/"))
        {
            pathAndQuery = "/" + pathAndQuery;
        }
        return baseUrl + pathAndQuery;
    }

    private static bool IsLegacyAvatarPath(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               path.StartsWith("/avatars/", StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeCachedAvatarUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return rawUrl;
        }

        string trimmed = rawUrl.Trim();
        if (trimmed.StartsWith("avatars/", StringComparison.OrdinalIgnoreCase))
        {
            return BuildAvatarServiceUrl(trimmed);
        }

        if (IsLegacyAvatarPath(trimmed))
        {
            return BuildAvatarServiceUrl(trimmed.TrimStart('/'));
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri absoluteUri))
        {
            string host = absoluteUri.Host.ToLowerInvariant();
            bool isLoopback = host == "127.0.0.1" || host == "localhost" || host == "::1";
            bool legacyLoopback = isLoopback && absoluteUri.Port == 8003;
            bool legacyPath = IsLegacyAvatarPath(absoluteUri.AbsolutePath);
            if (legacyLoopback || legacyPath)
            {
                return BuildAvatarServiceUrl(absoluteUri.PathAndQuery.TrimStart('/'));
            }
        }

        return trimmed;
    }

    void Start()
    {
        // DownloadAvatar puo' reimpostare i gestori in Start().
        // Li ri-agganciamo qui per essere sicuri che OnMainAvatarDownloaded venga chiamato.
        if (downloadAvatar != null)
        {
            downloadAvatar.SetOnDownloaded(OnMainAvatarDownloaded);
            Debug.Log("[AvatarManager] Callback SetOnDownloaded ri-agganciato in Start()");
        }
    }


    public void OnAvatarReceived(AvatarInfo avatarInfo)
    {
        Debug.Log($"Avatar ricevuto: {avatarInfo.AvatarId}");

        uiFlowController?.UpdateDebugText($"Avatar ricevuto ({avatarInfo.Gender}) - elaborazione...");

#if UNITY_WEBGL || UNITY_EDITOR
        var avatarData = new AvatarData
        {
            avatarId = avatarInfo.AvatarId,
            url = avatarInfo.Url,
            sourceUrl = avatarInfo.Url,
            urlType = avatarInfo.UrlType,
            bodyId = avatarInfo.BodyId,
            gender = avatarInfo.Gender,
            source = "avaturn",
            isWebGL = true
        };

        StartImportAndLoadMainWebGL(avatarData);
#else
        // Altre piattaforme: puoi continuare come preferisci (http o file system).
        var meta = new AvatarData(avatarInfo, false);
        if (!AvatarExists(meta.avatarId))
        {
            savedData.avatars.Add(meta);
            SaveData();
        }
        LoadAvatarImmediately(avatarInfo);
#endif

        profileRouter?.ApplyGender(avatarInfo.Gender); // Applica profilo LipSync in base al genere
    }

#if UNITY_WEBGL || UNITY_EDITOR
    private void StartImportAndLoadMainWebGL(AvatarData avatarData)
    {
        cancelPendingDownloads = false;
        _webglMainSessionId++;
        int sessionId = _webglMainSessionId;
        StartCoroutine(WebGLImportAndLoadMain(avatarData, sessionId));
    }

    private void StartLoadMainFromUrlWebGL(AvatarData avatarData)
    {
        cancelPendingDownloads = false;
        _webglMainSessionId++;
        int sessionId = _webglMainSessionId;
        StartCoroutine(WebGLLoadFromCachedUrl(avatarData, sessionId));
    }

    private IEnumerator WebGLImportAndLoadMain(AvatarData avatarData, int sessionId)
    {
        if (avatarData == null)
        {
            yield break;
        }

        var avatarInfo = new AvatarInfo(
            avatarData.url ?? string.Empty,
            string.IsNullOrEmpty(avatarData.urlType) ? "glb" : avatarData.urlType,
            string.IsNullOrEmpty(avatarData.bodyId) ? "default" : avatarData.bodyId,
            string.IsNullOrEmpty(avatarData.gender) ? "unknown" : avatarData.gender,
            string.IsNullOrEmpty(avatarData.avatarId) ? Guid.NewGuid().ToString() : avatarData.avatarId
        );

        uiFlowController?.OnAvatarDownloadStarted(avatarInfo);
        _isMainDownloading = true;
        currentDownloadAvatarId = avatarInfo.AvatarId;
        StartWatchdog(GetMainWatchdogSeconds());

        string cachedUrl = null;
        if (avatarData.source == "avaturn" && !string.IsNullOrEmpty(avatarData.sourceUrl))
        {
            yield return StartCoroutine(WebGLImportAvatar(avatarData, sessionId, url => cachedUrl = url));
            if (sessionId != _webglMainSessionId)
            {
                yield break;
            }
        }

        if (string.IsNullOrEmpty(cachedUrl))
        {
            cachedUrl = avatarData.url;
        }

        if (string.IsNullOrEmpty(cachedUrl))
        {
            Debug.LogError("[AvatarManager] WebGL import failed: cached URL missing.");
            _isMainDownloading = false;
            yield break;
        }

        yield return StartCoroutine(WebGLDownloadAndInstantiate(cachedUrl, transform, sessionId, HandleMainAvatarLoadedRuntime));
    }

    private IEnumerator WebGLLoadFromCachedUrl(AvatarData avatarData, int sessionId)
    {
        if (avatarData == null)
        {
            yield break;
        }

        var avatarInfo = new AvatarInfo(
            avatarData.url ?? string.Empty,
            string.IsNullOrEmpty(avatarData.urlType) ? "glb" : avatarData.urlType,
            string.IsNullOrEmpty(avatarData.bodyId) ? "default" : avatarData.bodyId,
            string.IsNullOrEmpty(avatarData.gender) ? "unknown" : avatarData.gender,
            string.IsNullOrEmpty(avatarData.avatarId) ? Guid.NewGuid().ToString() : avatarData.avatarId
        );

        uiFlowController?.OnAvatarDownloadStarted(avatarInfo);
        _isMainDownloading = true;
        currentDownloadAvatarId = avatarInfo.AvatarId;
        StartWatchdog(GetMainWatchdogSeconds());

        string cachedUrl = avatarData.url;
        if (avatarData.source == "local")
        {
            cachedUrl = BuildLocalUrl(avatarData.localFile);
        }

        if (string.IsNullOrEmpty(cachedUrl))
        {
            Debug.LogError("[AvatarManager] WebGL cached URL missing.");
            _isMainDownloading = false;
            yield break;
        }

        yield return StartCoroutine(WebGLDownloadAndInstantiate(cachedUrl, transform, sessionId, HandleMainAvatarLoadedRuntime));
    }
#endif

    void LoadAvatarImmediately(AvatarInfo avatarInfo)
    {
        if (downloadAvatar != null)
        {
            cancelPendingDownloads = false;
            
            // Se c'e' un download anteprima in corso, NON lo blocchiamo, ma possiamo gestire la priorita'.
            // In questa logica, download principale e anteprima sono indipendenti.
            // Se il principale e' gia' in corso, evitiamo di avviarne un altro sopra.
            if (_isMainDownloading)
            {
                Debug.LogWarning("[AvatarManager] Main download già in corso, ignoro richiesta duplicata/ravvicinata.");
                return;
            }

            downloadAvatar.SetOnDownloaded(OnMainAvatarDownloaded);
            
            _isMainDownloading = true;
            currentDownloadAvatarId = avatarInfo.AvatarId;
            
            Debug.Log($"[AvatarManager] [MAIN] Download start avatarId={currentDownloadAvatarId}");
            StartWatchdog(GetMainWatchdogSeconds());
            downloadAvatar.Download(avatarInfo);
        }
        else
            Debug.LogError("DownloadAvatar reference mancante in AvatarManager");

        uiFlowController?.OnAvatarDownloadStarted(avatarInfo);
    }

    public void LoadSavedAvatar(AvatarData avatarData)
    {
        Debug.Log($"Caricamento avatar salvato: {avatarData.avatarId}");
        uiFlowController?.UpdateDebugText($"Caricamento avatar {avatarData.avatarId}...");

        profileRouter?.ApplyGender(avatarData.gender);

#if UNITY_WEBGL || UNITY_EDITOR
        StartLoadMainFromUrlWebGL(avatarData);
#else
        var info = avatarData.ToAvatarInfo();
        LoadAvatarImmediately(info);
#endif
    }

    public void OnMainAvatarDownloaded(Transform loaderTransform)
    {
        // Invalida watchdog se completa normalmente
        _watchId++;

        _isMainDownloading = false;

        if (loaderTransform == null)
        {
            Debug.LogError("[AvatarManager] Loader transform is null in OnMainAvatarDownloaded!");
            return;
        }

        if (cancelPendingDownloads)
        {
            // Se loaderTransform è il GameObject che contiene DownloadAvatar, NON distruggerlo!
            // Distruggi solo i figli (il modello caricato).
            if (downloadAvatar != null && loaderTransform == downloadAvatar.transform)
            {
                 Debug.LogWarning("[AvatarManager] Cancel pending called, clearing children of persistent downloader.");
                 foreach (Transform child in loaderTransform)
                 {
                     Destroy(child.gameObject);
                 }
            }
            else
            {
                Destroy(loaderTransform.gameObject);
            }
            return;
        }

        Debug.Log($"[AvatarManager] [MAIN] OnMainAvatarDownloaded avatarId={currentDownloadAvatarId} loader={loaderTransform.name} children={loaderTransform.childCount}");

        // Rimuoviamo luci/camere dall'avatar principale per evitare abbagliamento o camere extra.
        CleanupConflictingComponents(loaderTransform.gameObject, keepAnimator: true);

        if (loaderTransform.childCount == 0)
        {
            Debug.LogError("Load OK ma nessun child instanziato sotto il Loader.");
            return;
        }

        Transform parent = _anchorPoseCached ? _anchorParent : spawnPoint;
        if (parent == null)
            parent = transform;

        var containerGO = new GameObject("CurrentAvatar");
        var container = containerGO.transform;
        container.SetParent(parent, false);

        if (_anchorPoseCached)
        {
            container.localPosition = _anchorLocalPos;
            container.localRotation = _anchorLocalRot;
            container.localScale = _anchorLocalScale;
        }
        else
        {
            container.localPosition = Vector3.zero;
            container.localRotation = Quaternion.identity;
            container.localScale = Vector3.one;
        }

        if (currentAvatar != null) Destroy(currentAvatar);

        // Qui usiamo la versione rivista del reparenting.
        // Se loaderTransform è il DownloadAvatar stesso, NON reparentarlo dentro CurrentAvatar,
        // altrimenti verrà distrutto quando distruggeremo CurrentAvatar!
        // Invece, spostiamo i figli (il modello) dentro un nuovo oggetto "Model" dentro container.

        GameObject modelRootGO;

        if (downloadAvatar != null && loaderTransform == downloadAvatar.transform)
        {
            // Il downloader è persistente. Estraiamo i figli.
            Debug.Log("[AvatarManager] Loader is persistent DownloadAvatar. Extracting children.");
            
            modelRootGO = new GameObject("Model");
            modelRootGO.transform.SetParent(container, false);
            modelRootGO.transform.localPosition = Vector3.zero;
            modelRootGO.transform.localRotation = Quaternion.identity;
            modelRootGO.transform.localScale = Vector3.one;

            // Sposta tutti i figli
            // Iteriamo al contrario o usiamo while per sicurezza mentre reparentiamo
            while (loaderTransform.childCount > 0)
            {
                Transform child = loaderTransform.GetChild(0);
                child.SetParent(modelRootGO.transform, false);
            }
        }
        else
        {
            // Comportamento standard (se il loader è un oggetto usa-e-getta generato da Avaturn)
            loaderTransform.name = "Model";
            loaderTransform.SetParent(container, false);
            loaderTransform.localPosition = Vector3.zero;
            loaderTransform.localRotation = Quaternion.identity;
            loaderTransform.localScale = Vector3.one;
            modelRootGO = loaderTransform.gameObject;
        }

        modelRootGO.SetActive(true);

        foreach (var r in modelRootGO.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;

        currentAvatar = container.gameObject;
        currentAvatarDimmed = false;
        currentAvatarDimMultiplier = 1f;
        InvalidateCurrentAvatarTintCache();
        // currentDownloadMode = DownloadMode.Main; // Riga rimossa per risolvere un errore di compilazione

        if (lipSyncBinder != null)
            StartCoroutine(SetupLipSyncNextFrame(container));
        else
            Debug.LogWarning("[LipSync] lipSyncBinder non assegnato in AvatarManager (Inspector).");

        if (idleLook != null)
            StartCoroutine(SetupIdleLookNextFrame(container));

        // Nascondi il baseModel se necessario
        if (baseModelToReplace != null && hideBaseModel)
        {
            baseModelToReplace.gameObject.SetActive(false);
        }

        // Debug utile: ora deve essere vicino allo spawnpoint.
        Debug.Log($"[Avatar] container world={container.position} local={container.localPosition} children={container.childCount}");

        uiFlowController?.OnAvatarDownloaded(container);
    }

    private IEnumerator SetupLipSyncNextFrame(Transform avatarRoot)
    {
        // Aspettiamo 1 frame: su WebGL/GLTFast evitiamo casi limite mentre Unity finalizza renderer/mesh.
        yield return null;
        lipSyncBinder.Setup(avatarRoot);
    }

    private IEnumerator SetupIdleLookNextFrame(Transform avatarRoot)
    {
        yield return null;
        idleLook.Setup(avatarRoot);
    }



    public void RemoveCurrentAvatar()
    {
        // Fermiamo le logiche legate all'avatar runtime.
        if (idleLook != null) idleLook.Clear();

        if (lipSyncBinder != null)
            lipSyncBinder.ClearTargets();

        if (currentAvatar != null)
        {
            Destroy(currentAvatar);
            currentAvatar = null;
        }
        currentAvatarDimmed = false;
        currentAvatarDimMultiplier = 1f;
        InvalidateCurrentAvatarTintCache();

        // Riattiva il placeholder
        if (baseModelToReplace != null && !hideBaseModel)
        {
            baseModelToReplace.gameObject.SetActive(true);

            if (idleLook != null)
                StartCoroutine(SetupIdleLookOnBaseNextFrame());
        }
    }

    public bool RemoveAvatarFromSavedData(string avatarId)
    {
        if (string.IsNullOrEmpty(avatarId) || savedData == null)
        {
            return false;
        }

        int removed = savedData.avatars.RemoveAll(item => item.avatarId == avatarId);
        if (removed > 0)
        {
            SaveData();
            return true;
        }

        return false;
    }

    private IEnumerator SetupIdleLookOnBaseNextFrame()
    {
        yield return null;
        if (idleLook != null && baseModelToReplace != null)
            idleLook.Setup(baseModelToReplace);
    }



#if UNITY_WEBGL && !UNITY_EDITOR
[DllImport("__Internal")] private static extern int EnsureDynCallV();
[DllImport("__Internal")] private static extern void JS_FileSystem_Sync();
[DllImport("__Internal")] private static extern void JS_FileSystem_SyncFromDiskAndNotify(string goName, string callbackMethod);
#endif

    private bool pendingSave;
    
    void SaveData()
    {
        try
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // In WebGL la lista avatar arriva dal backend, quindi saltiamo la cache file persistente.
            return;
#else
            string json = JsonUtility.ToJson(savedData, true);
            File.WriteAllText(savePath, json);
            Debug.Log("Dati salvati: " + savedData.avatars.Count + " avatar(s)");
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"Errore nel salvataggio dati: {e.Message}");
        }
    }

    void LoadData()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        savedData = new SavedAvatarData();
        return;
#else
        if (File.Exists(savePath))
        {
            try
            {
                Debug.Log("[AvatarManager] LoadData start");
                var json = File.ReadAllText(savePath);
                savedData = JsonUtility.FromJson<SavedAvatarData>(json) ?? new SavedAvatarData();
                Debug.Log($"Dati caricati: {savedData.avatars.Count} avatar(s)");
            }
            catch (Exception e)
            {
                Debug.LogError($"Errore nel caricamento dati: {e.Message}");
                savedData = new SavedAvatarData();
            }
        }
        else
        {
            savedData = new SavedAvatarData();
            Debug.Log("Nessun dato salvato trovato");
        }
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private IEnumerator WebGLPopulateAndLoad()
    {
        fileSystemReady = false;
        
        try
        {
            int ok = EnsureDynCallV();
            Debug.Log("[WebGL] EnsureDynCallV (AvatarManager) => " + ok);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WebGL] EnsureDynCallV (AvatarManager) failed: " + e.Message);
        }

        Debug.Log("[AvatarManager] WebGL FS populate start");
        
        try
        {
            JS_FileSystem_SyncFromDiskAndNotify(gameObject.name, nameof(OnFileSystemReady));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AvatarManager] JS_FileSystem_SyncFromDiskAndNotify error: {e.Message}");
        }

        const float timeout = 6f;
        float elapsed = 0f;
        while (!fileSystemReady && elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!fileSystemReady)
        {
            Debug.LogWarning("[AvatarManager] WebGL FS populate timeout, proceeding with LoadData");
            fileSystemReady = true;
        }
        
        // Assicura che la directory persistentDataPath esista
        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);
        }
        catch (Exception dirEx)
        {
            Debug.LogWarning($"[AvatarManager] Could not create persistentDataPath: {dirEx.Message}");
        }

        LoadData();
        if (addLocalTestModels)
            EnsureLocalModelInList();
            
        // Se c'era un salvataggio in sospeso, lo eseguiamo ora.
        if (pendingSave)
        {
            pendingSave = false;
            SaveData();
        }
    }

    public void OnFileSystemReady(string result)
    {
        fileSystemReady = true;
        Debug.Log($"[AvatarManager] WebGL FS populate complete: {result}");
    }
#endif

    private void EnsureLocalModelInList()
    {
        AddLocalModelIfMissing(localModel1AvatarId, localModel1FileName, localModel1Gender);
        AddLocalModelIfMissing(localModel2AvatarId, localModel2FileName, localModel2Gender);
    }

    private void AddLocalModelIfMissing(string avatarId, string fileName, string gender)
    {
        if (AvatarExists(avatarId))
            return;

        string url;

#if UNITY_WEBGL && !UNITY_EDITOR
        // In WebGL StreamingAssets e' gia' un URL (stessa origine della build).
        url = $"{Application.streamingAssetsPath}/{fileName}";
#else
        // In editor/standalone e' un percorso su disco: serve file:// con slash corretti.
        var filePath = Path.Combine(Application.streamingAssetsPath, fileName);
        url = new Uri(filePath).AbsoluteUri;
#endif

        var info = new Avaturn.Core.Runtime.Scripts.Avatar.Data.AvatarInfo(
            url,
            "glb",
            "local",
            gender,
            avatarId
        );

        bool isWebGL =
#if UNITY_WEBGL && !UNITY_EDITOR
        true;
#else
            false;
#endif

        var meta = new AvatarData(info, isWebGL);

        savedData.avatars.Insert(0, meta);
        SaveData();

        Debug.Log($"[AvatarManager] Aggiunto avatar locale: {avatarId} -> {url}");
    }


    bool AvatarExists(string avatarId)
    {
        foreach (var data in savedData.avatars)
            if (data.avatarId == avatarId) return true;
        return false;
    }

    private string _lastAvatarId;
    private float _lastAvatarTs;

    // chiamato da UIFlowController / bridge
    public void OnAvatarJsonReceived(string json)
    {
        Debug.Log($"JSON ricevuto: {json}");

        try
        {
            var jsonData = JsonUtility.FromJson<AvatarJsonData>(json);

            if (jsonData == null)
                return;

            if (jsonData.status == "closed")
            {
                uiFlowController?.UpdateDebugText("Avaturn chiuso");
                return;
            }

            if (jsonData.status == "error")
            {
                uiFlowController?.UpdateDebugText("Errore Avaturn");
                return;
            }

            if (string.IsNullOrEmpty(jsonData.url))
            {
                Debug.LogWarning("[AvatarManager] JSON senza url (ignorato). status=" + jsonData.status);
                return;
            } else {
                if (_lastAvatarId == jsonData.avatarId && Time.realtimeSinceStartup - _lastAvatarTs < 2f)
                    return;

                _lastAvatarId = jsonData.avatarId;
                _lastAvatarTs = Time.realtimeSinceStartup;
            }

            uiFlowController?.NotifyMainAvatarLoadRequested();

#if UNITY_WEBGL || UNITY_EDITOR
            var avatarData = new AvatarData
            {
                avatarId = string.IsNullOrEmpty(jsonData.avatarId) ? Guid.NewGuid().ToString() : jsonData.avatarId,
                url = jsonData.url,
                sourceUrl = jsonData.url,
                urlType = string.IsNullOrEmpty(jsonData.urlType) ? "glb" : jsonData.urlType,
                bodyId = string.IsNullOrEmpty(jsonData.bodyId) ? "default" : jsonData.bodyId,
                gender = string.IsNullOrEmpty(jsonData.gender) ? "unknown" : jsonData.gender,
                source = "avaturn",
                isWebGL = true
            };

            StartImportAndLoadMainWebGL(avatarData);
#else
            var avatarInfo = new AvatarInfo(
                jsonData.url,
                string.IsNullOrEmpty(jsonData.urlType) ? "glb" : jsonData.urlType,
                string.IsNullOrEmpty(jsonData.bodyId) ? "default" : jsonData.bodyId,
                string.IsNullOrEmpty(jsonData.gender) ? "unknown" : jsonData.gender,
                string.IsNullOrEmpty(jsonData.avatarId) ? Guid.NewGuid().ToString() : jsonData.avatarId
            );

            OnAvatarReceived(avatarInfo);
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"Errore nel parsing JSON: {e.Message}");
            uiFlowController?.UpdateDebugText("Errore JSON avatar");
        }
    }

    private void CreatePreviewDownloader(bool forceFresh)
    {
        // Di base distruggiamo e ricreiamo sempre il downloader.
        // Per le anteprime del carosello conviene mantenerlo vivo, perche' alcuni SDK
        // puliscono texture/materiali runtime in OnDestroy e invalidano modelli gia' riparentati.
        if (forceFresh && _previewDownloader != null)
        {
            if (_previewDownloader.gameObject != null) Destroy(_previewDownloader.gameObject);
            _previewDownloader = null;
        }

        if (!forceFresh && _previewDownloader != null)
        {
            return;
        }

        if (downloadAvatar == null)
        {
            Debug.LogError("[AvatarManager] Cannot create PreviewDownloader: 'downloadAvatar' field is null!");
            return;
        }

        try
        {
            // Caso 1: downloadAvatar e' un oggetto/prefab separato (caso standard).
            if (downloadAvatar.gameObject != this.gameObject)
            {
                var go = Instantiate(downloadAvatar.gameObject, transform);
                go.name = "PreviewDownloader_Temp";
                _previewDownloader = go.GetComponent<DownloadAvatar>();
                go.SetActive(true);
            }
            // Caso 2: downloadAvatar e' agganciato a QUESTO AvatarManager (caso limite).
            else
            {
                var go = new GameObject("PreviewDownloader_Temp");
                go.transform.SetParent(transform, false);
                
                var newComp = go.AddComponent<DownloadAvatar>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(downloadAvatar), newComp);
                
                _previewDownloader = newComp;
                go.SetActive(true);
            }

            // Rimuoviamo tutto dal contenitore stesso.
            CleanupConflictingComponents(_previewDownloader.gameObject, keepAnimator: false);
        }
        catch (Exception e)
        {
            Debug.LogError($"[AvatarManager] Error creating PreviewDownloader: {e.Message}");
        }
    }

    private static void DetachAndDestroyChildren(Transform root)
    {
        if (root == null) return;

        // Stacchiamo prima, cosi' la gerarchia resta subito pulita, poi distruggiamo.
        while (root.childCount > 0)
        {
            var child = root.GetChild(0);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }

    private void CleanupConflictingComponents(GameObject go, bool keepAnimator)
    {
        if (go == null) return;

        // Rimuovi tutte le luci - questa è la causa dell'abbagliamento
        var lights = go.GetComponentsInChildren<Light>(true);
        foreach (var l in lights) 
        {
            Debug.Log($"[Cleanup] Rimossa luce: {l.name} dal gameobject {go.name}");
            DestroyImmediate(l);
        }

        // Rimuovi anche eventuali componenti di luce aggiuntivi
        var reflectionProbes = go.GetComponentsInChildren<ReflectionProbe>(true);
        foreach (var rp in reflectionProbes) DestroyImmediate(rp);

        // GLTFast/DownloadAvatar richiede uno stato pulito.
        if (!keepAnimator)
        {
            var animators = go.GetComponentsInChildren<Animator>(true);
            foreach (var anim in animators) DestroyImmediate(anim);

            var animations = go.GetComponentsInChildren<Animation>(true);
            foreach (var anim in animations) DestroyImmediate(anim);
        }

        var cameras = go.GetComponentsInChildren<Camera>(true);
        foreach (var cam in cameras) DestroyImmediate(cam);

        var listeners = go.GetComponentsInChildren<AudioListener>(true);
        foreach (var list in listeners) DestroyImmediate(list);
        
        var sources = go.GetComponentsInChildren<AudioSource>(true);
        foreach (var src in sources) DestroyImmediate(src);
    }

    public bool DownloadPreview(AvatarData avatarData, Action<Transform> onLoaded)
    {
        return DownloadPreview(avatarData, onLoaded, destroyDownloaderAfter: true);
    }

    public bool DownloadPreview(AvatarData avatarData, Action<Transform> onLoaded, bool destroyDownloaderAfter)
    {
#if UNITY_WEBGL || UNITY_EDITOR
        return DownloadPreviewToTransform(avatarData, transform, onLoaded, destroyDownloaderAfter);
#else
        // Se c'e' gia' un download in corso, lo cancelliamo: nella preview rapida vince l'ultima richiesta.
        if (_isPreviewDownloading)
        {
            CancelPreviewDownloads();
        }

        CreatePreviewDownloader(forceFresh: destroyDownloaderAfter);
        if (_previewDownloader == null)
        {
            Debug.LogError("[AvatarManager] Impossibile creare PreviewDownloader");
            return false;
        }

        // Garantiamo uno stato pulito per il nuovo import.
        DetachAndDestroyChildren(_previewDownloader.transform);

        // Incrementiamo l'ID sessione per la nuova sessione.
        _previewSessionId++;
        int currentSession = _previewSessionId;
        _isPreviewDownloading = true;

        // Feedback UI: nascondiamo HintBar/Title.
        uiFlowController?.SetPreviewModeUI(true);

        Debug.Log($"[AvatarManager] [PREVIEW gen={currentSession}] start avatarId={avatarData.avatarId}");

        // Catturiamo l'istanza specifica per questa sessione.
        var currentDownloader = _previewDownloader;

        // Impostiamo il callback.
        currentDownloader.SetOnDownloaded(loader => OnPreviewDownloaded(loader, currentSession, onLoaded, currentDownloader, destroyDownloaderAfter));

        // Avvio.
        var info = avatarData.ToAvatarInfo();
        currentDownloader.Download(info);
        
        // Controllo timeout per anteprima.
        StartWatchdog(GetPreviewWatchdogSeconds());
        return true;
#endif
    }

    public void RequestAvatarListFromBackend(Action<List<AvatarData>> cb)
    {
        // Usiamo la lista backend in tutte le build per allineare il comportamento WebGL.
        StartCoroutine(WebGLRequestAvatarList(cb));
    }

    public bool DownloadPreviewToTransform(AvatarData data, Transform parent, Action<Transform> onLoaded)
    {
        return DownloadPreviewToTransform(data, parent, onLoaded, destroyDownloaderAfter: true);
    }

    public bool DownloadPreviewToTransform(AvatarData data, Transform parent, Action<Transform> onLoaded, bool destroyDownloaderAfter)
    {
#if UNITY_WEBGL || UNITY_EDITOR
        if (_isPreviewDownloading)
        {
            CancelPreviewDownloads();
        }

        _webglPreviewSessionId++;
        int sessionId = _webglPreviewSessionId;
        _isPreviewDownloading = true;

        uiFlowController?.SetPreviewModeUI(true);
        StartWatchdog(GetPreviewWatchdogSeconds());

        StartCoroutine(WebGLPreviewDownload(data, parent, sessionId, onLoaded));
        return true;
#else
        return DownloadPreview(data, onLoaded, destroyDownloaderAfter);
#endif
    }

        private void OnPreviewDownloaded(Transform loader, int sessionId, Action<Transform> callback, DownloadAvatar usedDownloader, bool destroyDownloaderAfter)
    {
        // Controllo aggiuntivo per oggetti null
        if (this == null || gameObject == null || !gameObject.activeInHierarchy)
        {
            Debug.LogWarning("[AvatarManager] Manager distrutto, ignoro callback.");
            return;
        }

        // 1. Validazione sessione
        if (sessionId != _previewSessionId)
        {
            Debug.Log($"[AvatarManager] [PREVIEW] Session Mismatch (req={sessionId} curr={_previewSessionId}). Discarding.");
            if (destroyDownloaderAfter && usedDownloader != null) Destroy(usedDownloader.gameObject);
            return;
        }

        // 2. Controllo validità downloader
        if (usedDownloader == null)
        {
             Debug.LogWarning($"[AvatarManager] [PREVIEW gen={sessionId}] usedDownloader è null.");
             // Ripristino UI di sicurezza.
             uiFlowController?.SetPreviewModeUI(false);
             _isPreviewDownloading = false;
             return;
        }

        // 3. Pulizia stato
        _isPreviewDownloading = false;
        uiFlowController?.SetPreviewModeUI(false); // Restore UI

        // 4. Elaborazione modello
        if (loader != null)
        {
            // Rimuoviamo i componenti in sicurezza.
            CleanupConflictingComponents(loader.gameObject, keepAnimator: false);
            
            Debug.Log($"[AvatarManager] [PREVIEW gen={sessionId}] Complete. Children: {loader.childCount}");
            
            try
            {
                callback?.Invoke(loader);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarManager] Error in preview callback: {e.Message}");
            }
        }
        else
        {
             Debug.LogWarning($"[AvatarManager] [PREVIEW gen={sessionId}] Loader is null?");
        }

        // 5. Pulizia finale del downloader temporaneo
        if (destroyDownloaderAfter)
        {
            if (usedDownloader != null)
            {
                Destroy(usedDownloader.gameObject);
            }

            // Se questo era il downloader attivo globale, puliamo il riferimento
            if (_previewDownloader == usedDownloader)
            {
                _previewDownloader = null;
            }
        }
    }

    public void CancelPreviewDownloads()
    {
        // Annullamento forzato
        _previewSessionId++; // Invalida i callback pendenti
        _isPreviewDownloading = false;

#if UNITY_WEBGL && !UNITY_EDITOR
        _webglPreviewSessionId++;
#endif
        
        if (_previewDownloader != null)
        {
             if (_previewDownloader.gameObject != null) Destroy(_previewDownloader.gameObject);
             _previewDownloader = null;
        }
        
        // Assicura ripristino UI
        uiFlowController?.SetPreviewModeUI(false);
        
        Debug.Log($"[AvatarManager] [PREVIEW] Hard Cancel (new gen: {_previewSessionId})");
    }

    public void CancelAllDownloads()
    {
        cancelPendingDownloads = true;
        currentDownloadAvatarId = null;
        
        _isMainDownloading = false;
        
        CancelPreviewDownloads(); // Invalida anche preview

#if UNITY_WEBGL && !UNITY_EDITOR
        _webglMainSessionId++;
#endif
        
        _watchId++;
        
        Debug.Log("[AvatarManager] ALL downloads HARD CANCEL completato.");
    }

    public void ResetPreviewStateForLibrary()
    {
        CancelPreviewDownloads(); // Assicura stato pulito
        Debug.Log("[AvatarManager] Preview state HARD RESET per nuova sessione libreria.");
    }

    private IEnumerator WebGLRequestAvatarList(Action<List<AvatarData>> cb)
    {
        string url = $"{backendBaseUrl.TrimEnd('/')}/avatars/list";
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = webglRequestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AvatarManager] Backend list failed: {request.error}. Fallback to local models.");
                cb?.Invoke(BuildLocalAvatarList());
                yield break;
            }

            try
            {
                var response = JsonUtility.FromJson<AvatarListResponse>(request.downloadHandler.text);
                var results = new List<AvatarData>();
                if (response != null && response.avatars != null)
                {
                    foreach (var item in response.avatars)
                    {
                        var converted = ConvertListItem(item);
                        if (converted != null)
                        {
                            results.Add(converted);
                        }
                    }
                }

                EnsureLocalEntries(results);
                cb?.Invoke(results);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarManager] Backend list parse failed: {e.Message}. Fallback to local models.");
                cb?.Invoke(BuildLocalAvatarList());
            }
        }
    }

#if UNITY_WEBGL || UNITY_EDITOR
    private IEnumerator WebGLPreviewDownload(AvatarData data, Transform parent, int sessionId, Action<Transform> onLoaded)
    {
        if (data == null)
        {
            _isPreviewDownloading = false;
            uiFlowController?.SetPreviewModeUI(false);
            yield break;
        }

        string cachedUrl = data.url;
        if (data.source == "local")
        {
            cachedUrl = BuildLocalUrl(data.localFile);
        }
        else if (!string.IsNullOrEmpty(data.sourceUrl))
        {
            yield return StartCoroutine(WebGLImportAvatar(data, sessionId, url => cachedUrl = url));
            if (sessionId != _webglPreviewSessionId)
            {
                yield break;
            }
        }

        if (string.IsNullOrEmpty(cachedUrl))
        {
            Debug.LogWarning("[AvatarManager] Preview cached url missing.");
            _isPreviewDownloading = false;
            uiFlowController?.SetPreviewModeUI(false);
            yield break;
        }

        yield return StartCoroutine(WebGLDownloadAndInstantiate(cachedUrl, parent, sessionId, loader =>
        {
            _isPreviewDownloading = false;
            uiFlowController?.SetPreviewModeUI(false);
            if (loader != null)
            {
                CleanupConflictingComponents(loader.gameObject, keepAnimator: false);
            }
            onLoaded?.Invoke(loader);
        }));
    }

    private IEnumerator WebGLImportAvatar(AvatarData data, int sessionId, Action<string> onCachedUrl)
    {
        if (data == null || string.IsNullOrEmpty(data.sourceUrl))
        {
            onCachedUrl?.Invoke(null);
            yield break;
        }

        string url = $"{backendBaseUrl.TrimEnd('/')}/avatars/import";
        var payload = new ImportRequest
        {
            avatar_id = data.avatarId,
            url = data.sourceUrl,
            gender = data.gender,
            bodyId = data.bodyId,
            urlType = data.urlType
        };

        string json = JsonUtility.ToJson(payload);
        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.timeout = webglRequestTimeoutSeconds;
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (sessionId != _webglMainSessionId && sessionId != _webglPreviewSessionId)
            {
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AvatarManager] Backend import failed: {request.error}");
                uiFlowController?.UpdateDebugText("Backend avatar import failed.");
                onCachedUrl?.Invoke(null);
                yield break;
            }

            var response = JsonUtility.FromJson<ImportResponse>(request.downloadHandler.text);
            onCachedUrl?.Invoke(response != null ? NormalizeCachedAvatarUrl(response.cached_glb_url) : null);
        }
    }

    private IEnumerator WebGLDownloadAndInstantiate(
        string cachedUrl,
        Transform parent,
        int sessionId,
        Action<Transform> onLoaded)
    {
        using (var request = UnityWebRequest.Get(cachedUrl))
        {
            request.timeout = webglRequestTimeoutSeconds;
            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();

            if (sessionId != _webglMainSessionId && sessionId != _webglPreviewSessionId)
            {
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AvatarManager] Download glb failed: {request.error}");
                _isMainDownloading = false;
                uiFlowController?.UpdateDebugText("Download glb failed.");
                onLoaded?.Invoke(null);
                yield break;
            }

            byte[] bytes = request.downloadHandler.data;
            if (bytes == null || bytes.Length < minGlbBytes)
            {
                Debug.LogWarning($"[AvatarManager] Downloaded glb too small: {bytes?.Length ?? 0} bytes.");
                _isMainDownloading = false;
                uiFlowController?.UpdateDebugText("Downloaded glb too small.");
                onLoaded?.Invoke(null);
                yield break;
            }

            var loadTask = LoadGltfBytesAsync(bytes, parent, "Model");

            Transform loadedAvatar = null;
            Exception loadException = null;
            bool loadCanceled = false;
            bool loadCompleted = false;

            loadTask.ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    loadCanceled = true;
                }
                else if (t.IsFaulted)
                {
                    loadException = t.Exception;
                }
                else
                {
                    loadedAvatar = t.Result;
                }

                loadCompleted = true;
            }, TaskScheduler.FromCurrentSynchronizationContext());

            yield return new WaitUntil(() => loadCompleted);

            if (sessionId != _webglMainSessionId && sessionId != _webglPreviewSessionId)
            {
                if (loadedAvatar != null)
                {
                    Destroy(loadedAvatar.gameObject);
                }
                yield break;
            }

            if (loadException != null || loadCanceled)
            {
                Debug.LogWarning("[AvatarManager] Runtime glb load failed.");
                _isMainDownloading = false;
                onLoaded?.Invoke(null);
                yield break;
            }

            onLoaded?.Invoke(loadedAvatar);
        }
    }

    private async Task<Transform> LoadGltfBytesAsync(byte[] bytes, Transform parent, string rootName)
    {
        var gltf = new GltfImport();
        // Il secondo parametro e' un URI base opzionale per risolvere risorse esterne; qui null e' corretto
        // perche' questo GLB avatar e' autosufficiente e viene caricato solo da byte in memoria.
        bool success = await gltf.Load(bytes, null);
        if (!success)
        {
            return null;
        }

        var root = new GameObject(rootName);
        root.transform.SetParent(parent, false);

        success = await gltf.InstantiateMainSceneAsync(root.transform);
        if (!success)
        {
            Destroy(root);
            return null;
        }

        return root.transform;
    }

    private void HandleMainAvatarLoadedRuntime(Transform modelRoot)
    {
        _watchId++;
        _isMainDownloading = false;

        if (modelRoot == null)
        {
            Debug.LogWarning("[AvatarManager] WebGL main load returned null.");
            return;
        }

        if (cancelPendingDownloads)
        {
            Destroy(modelRoot.gameObject);
            return;
        }

        CleanupConflictingComponents(modelRoot.gameObject, keepAnimator: true);

        Transform parent = _anchorPoseCached ? _anchorParent : spawnPoint;
        if (parent == null)
        {
            parent = transform;
        }

        var containerGO = new GameObject("CurrentAvatar");
        var container = containerGO.transform;
        container.SetParent(parent, false);

        if (_anchorPoseCached)
        {
            container.localPosition = _anchorLocalPos;
            container.localRotation = _anchorLocalRot;
            container.localScale = _anchorLocalScale;
        }
        else
        {
            container.localPosition = Vector3.zero;
            container.localRotation = Quaternion.identity;
            container.localScale = Vector3.one;
        }

        if (currentAvatar != null)
        {
            Destroy(currentAvatar);
        }

        modelRoot.name = "Model";
        modelRoot.SetParent(container, false);

        foreach (var r in modelRoot.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
        }

        currentAvatar = container.gameObject;
        currentAvatarDimmed = false;
        currentAvatarDimMultiplier = 1f;
        InvalidateCurrentAvatarTintCache();

        if (lipSyncBinder != null)
        {
            StartCoroutine(SetupLipSyncNextFrame(container));
        }
        else
        {
            Debug.LogWarning("[LipSync] lipSyncBinder non assegnato in AvatarManager (Inspector).");
        }

        if (idleLook != null)
        {
            StartCoroutine(SetupIdleLookNextFrame(container));
        }

        if (baseModelToReplace != null && hideBaseModel)
        {
            baseModelToReplace.gameObject.SetActive(false);
        }

        uiFlowController?.OnAvatarDownloaded(container);
    }

    [System.Serializable]
    private class ImportRequest
    {
        public string avatar_id;
        public string url;
        public string gender;
        public string bodyId;
        public string urlType;
    }

    [System.Serializable]
    private class ImportResponse
    {
        public bool ok;
        public string avatar_id;
        public string cached_glb_url;
        public long bytes;
    }
#endif

    private List<AvatarData> BuildLocalAvatarList()
    {
        var list = new List<AvatarData>();
        list.Add(CreateLocalAvatar(localModel1AvatarId, localModel1FileName, localModel1Gender));
        list.Add(CreateLocalAvatar(localModel2AvatarId, localModel2FileName, localModel2Gender));
        return list;
    }

    private AvatarData CreateLocalAvatar(string avatarId, string fileName, string gender)
    {
        var url = BuildLocalUrl(fileName);
        return new AvatarData
        {
            avatarId = avatarId,
            url = url,
            urlType = "glb",
            bodyId = "local",
            gender = gender,
            source = "local",
            localFile = fileName,
            displayName = avatarId,
            fileName = fileName,
#if UNITY_WEBGL && !UNITY_EDITOR
            isWebGL = true
#else
            isWebGL = false
#endif
        };
    }

    private void EnsureLocalEntries(List<AvatarData> list)
    {
        if (list == null)
        {
            return;
        }

        bool hasModel1 = false;
        bool hasModel2 = false;

        foreach (var item in list)
        {
            if (item.avatarId == localModel1AvatarId)
            {
                hasModel1 = true;
            }
            else if (item.avatarId == localModel2AvatarId)
            {
                hasModel2 = true;
            }
        }

        if (!hasModel1)
        {
            list.Insert(0, CreateLocalAvatar(localModel1AvatarId, localModel1FileName, localModel1Gender));
        }
        if (!hasModel2)
        {
            list.Insert(1, CreateLocalAvatar(localModel2AvatarId, localModel2FileName, localModel2Gender));
        }
    }

    private string BuildLocalUrl(string localFile)
    {
        if (string.IsNullOrEmpty(localFile))
        {
            return null;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        return $"{Application.streamingAssetsPath}/{localFile}";
#else
        var filePath = Path.Combine(Application.streamingAssetsPath, localFile);
        return new Uri(filePath).AbsoluteUri;
#endif
    }

    private AvatarData ConvertListItem(AvatarListItem item)
    {
        if (item == null)
        {
            return null;
        }

        return new AvatarData
        {
            avatarId = item.avatar_id,
            url = NormalizeCachedAvatarUrl(item.cached_glb_url),
            sourceUrl = item.url_originale,
            urlType = string.IsNullOrEmpty(item.urlType) ? "glb" : item.urlType,
            bodyId = item.bodyId,
            gender = item.gender,
            source = item.source,
            localFile = item.local_file,
            displayName = item.display_name,
#if UNITY_WEBGL && !UNITY_EDITOR
            isWebGL = true
#else
            isWebGL = false
#endif
        };
    }

    public bool IsAvatarCached(AvatarData avatarData)
    {
        if (avatarData == null)
        {
            return false;
        }

        if (IsLocalAvatarId(avatarData.bodyId, avatarData.avatarId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(avatarData.fileName))
        {
            var path = Path.Combine(Application.persistentDataPath, avatarData.fileName);
            return File.Exists(path);
        }

        return false;
    }

    public void SetCurrentAvatarVisible(bool visible)
    {
        if (currentAvatar != null)
        {
            currentAvatar.SetActive(visible);
        }
    }

    public void SetCurrentAvatarDimmed(bool dimmed, float dimMultiplier = 0.55f)
    {
        float targetMultiplier = dimmed ? Mathf.Clamp(dimMultiplier, 0.05f, 1f) : 1f;
        if (currentAvatarDimmed == dimmed && Mathf.Approximately(currentAvatarDimMultiplier, targetMultiplier))
        {
            return;
        }

        currentAvatarDimmed = dimmed;
        currentAvatarDimMultiplier = targetMultiplier;
        ApplyCurrentAvatarDimming();
    }

    private void InvalidateCurrentAvatarTintCache()
    {
        currentAvatarTintTargets.Clear();
        currentAvatarTintCacheValid = false;
    }

    private void EnsureCurrentAvatarTintCache()
    {
        if (currentAvatarTintCacheValid)
        {
            return;
        }

        currentAvatarTintTargets.Clear();
        if (currentAvatar == null)
        {
            currentAvatarTintCacheValid = true;
            return;
        }

        Renderer[] renderers = currentAvatar.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            Material[] sharedMaterials = renderer.sharedMaterials;
            if (sharedMaterials == null)
            {
                continue;
            }

            for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
            {
                Material sharedMaterial = sharedMaterials[materialIndex];
                if (sharedMaterial == null)
                {
                    continue;
                }

                int colorPropertyId = 0;
                Color baseColor = Color.white;
                if (sharedMaterial.HasProperty(BaseColorPropertyId))
                {
                    colorPropertyId = BaseColorPropertyId;
                    baseColor = sharedMaterial.GetColor(BaseColorPropertyId);
                }
                else if (sharedMaterial.HasProperty(ColorPropertyId))
                {
                    colorPropertyId = ColorPropertyId;
                    baseColor = sharedMaterial.GetColor(ColorPropertyId);
                }

                if (colorPropertyId == 0)
                {
                    continue;
                }

                currentAvatarTintTargets.Add(new AvatarTintTarget
                {
                    renderer = renderer,
                    materialIndex = materialIndex,
                    colorPropertyId = colorPropertyId,
                    baseColor = baseColor
                });
            }
        }

        currentAvatarTintCacheValid = true;
    }

    private void ApplyCurrentAvatarDimming()
    {
        if (currentAvatar == null)
        {
            return;
        }

        EnsureCurrentAvatarTintCache();
        if (currentAvatarTintTargets.Count == 0)
        {
            return;
        }

        float multiplier = currentAvatarDimmed ? currentAvatarDimMultiplier : 1f;
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < currentAvatarTintTargets.Count; i++)
        {
            AvatarTintTarget target = currentAvatarTintTargets[i];
            if (target.renderer == null)
            {
                continue;
            }

            target.renderer.GetPropertyBlock(block, target.materialIndex);
            Color tinted = target.baseColor * multiplier;
            tinted.a = target.baseColor.a;
            block.SetColor(target.colorPropertyId, tinted);
            target.renderer.SetPropertyBlock(block, target.materialIndex);
            block.Clear();
        }
    }

    // Qui gestiamo il watchdog timeout dei download.
    private float GetPreviewWatchdogSeconds()
    {
        float configured = Mathf.Max(5f, previewTimeoutSeconds);
#if UNITY_WEBGL || UNITY_EDITOR
        // In WebGL il flusso preview puo' includere:
        // 1) import backend, 2) download GLB, 3) parse/instantiate runtime.
        // Un timeout troppo basso sblocca in anticipo mentre la rete sta ancora lavorando.
        float webglBudget = Mathf.Max(20f, (webglRequestTimeoutSeconds * 2f) + 20f);
        configured = Mathf.Max(configured, webglBudget);
#endif
        return configured;
    }

    private float GetMainWatchdogSeconds()
    {
        float configured = Mathf.Max(10f, mainTimeoutSeconds);
#if UNITY_WEBGL || UNITY_EDITOR
        // Anche il main mode puo' fare import + download in sequenza.
        float webglBudget = Mathf.Max(30f, (webglRequestTimeoutSeconds * 2f) + 30f);
        configured = Mathf.Max(configured, webglBudget);
#endif
        return configured;
    }

    private void StartWatchdog(float timeoutSeconds)
    {
        int id = ++_watchId;
        StartCoroutine(WatchdogCoroutine(id, timeoutSeconds));
    }

    private IEnumerator WatchdogCoroutine(int id, float timeoutSeconds)
    {
        yield return new WaitForSecondsRealtime(timeoutSeconds);

        // Controlla se siamo ancora nello stesso "stato" di attesa
        if (id != _watchId)
            yield break;

        if (_isMainDownloading || _isPreviewDownloading)
        {
            Debug.LogWarning($"[AvatarManager] Download timeout dopo {timeoutSeconds} secondi. Forcing unlock.");
            
            // Log dettagliato per debug.
            if (_isMainDownloading)
                Debug.LogWarning("[AvatarManager] Main download in timeout.");
            if (_isPreviewDownloading)
                Debug.LogWarning("[AvatarManager] Preview download in timeout.");
                
            _isMainDownloading = false;
            
            // Per l'anteprima, il timeout agisce come annullamento.
            if (_isPreviewDownloading)
            {
                 CancelPreviewDownloads();
            }
        }
    }
    // Fine gestione watchdog timeout.


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
    private class AvatarListResponse
    {
        public AvatarListItem[] avatars;
    }

    [System.Serializable]
    private class AvatarListItem
    {
        public string avatar_id;
        public string source;
        public string local_file;
        public string cached_glb_url;
        public string url_originale;
        public string urlType;
        public string gender;
        public string bodyId;
        public string display_name;
    }
}
