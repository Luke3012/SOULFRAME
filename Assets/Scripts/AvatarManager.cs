using System;
using Avaturn.Core.Runtime.Scripts.Avatar;
using Avaturn.Core.Runtime.Scripts.Avatar.Data;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_WEBGL && !UNITY_EDITOR
using GLTFast;
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
                 bodyId == "local" || (avatarId != null && avatarId.StartsWith("LOCAL_"))))
            {
                return new AvatarInfo(url, urlType, bodyId, gender, avatarId);
            }

            // Fallback 1, se esiste salvataggio
            var path = Path.Combine(Application.persistentDataPath, fileName);
            if (File.Exists(path))
                return new AvatarInfo($"file://{path}", urlType, bodyId, gender, avatarId);

            // Ultimo fallback: prova comunque l'url salvato
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
    public RuntimeAnimatorController idleController;
    public bool animatorApplyRootMotion = false;
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
    // Legacy support property, though mostly unused now
    public bool IsPreviewDownloadActive => _isPreviewDownloading;

    private SavedAvatarData savedData;
    private string savePath;
    private GameObject currentAvatar;
    
    // Split downloading states
    private bool _isMainDownloading;
    private bool _isPreviewDownloading;
    private int _previewSessionId;
    private DownloadAvatar _previewDownloader;

    private bool fileSystemReady;
    private int _watchId;
    private bool hasPendingMainDownload;
    private AvatarInfo pendingMainAvatarInfo;
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


    void Awake()
    {
        savePath = Path.Combine(Application.persistentDataPath, "Avatars.json");
        Debug.Log($"[AvatarManager] persistentDataPath={Application.persistentDataPath}");

#if UNITY_WEBGL && !UNITY_EDITOR
        StartCoroutine(WebGLPopulateAndLoad());
        if (downloadAvatar != null)
        {
            Debug.Log("[AvatarManager] WebGL path active: DownloadAvatar is ignored.");
        }
#else
        LoadData();
        if (addLocalTestModels)
            EnsureLocalModelInList();

    // WebGL-only tunables: keep serialized & editable, but they are only used in WebGL builds.
    // Touch them here to avoid CS0414 warnings in Editor/Standalone compiles.
    _ = backendBaseUrl;
    _ = webglRequestTimeoutSeconds;
    _ = minGlbBytes;
#endif

        if (downloadAvatar != null)
            downloadAvatar.SetOnDownloaded(OnMainAvatarDownloaded);

        // --- FIX 1: cache pose relativa al parent reale (evita teleport) ---
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
        // --- fine FIX 1 ---
    }

    void Start()
    {
        // IMPORTANT: DownloadAvatar può reimpostare i callback in Start().
        // Ri-agganciamo qui per essere sicuri che OnMainAvatarDownloaded venga chiamato.
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

#if UNITY_WEBGL && !UNITY_EDITOR
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

#if UNITY_WEBGL && !UNITY_EDITOR
    private void StartImportAndLoadMainWebGL(AvatarData avatarData)
    {
        _webglMainSessionId++;
        int sessionId = _webglMainSessionId;
        StartCoroutine(WebGLImportAndLoadMain(avatarData, sessionId));
    }

    private void StartLoadMainFromUrlWebGL(AvatarData avatarData)
    {
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
        StartWatchdog(mainTimeoutSeconds);

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
        StartWatchdog(mainTimeoutSeconds);

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
            
            // Se c'è un download preview in corso, NON lo blocchiamo, ma potremmo voler gestire la priorità.
            // In questo design, Main e Preview sono indipendenti. 
            // Tuttavia, se Main è già in corso, evitiamo di farne partire un altro sopra.
            if (_isMainDownloading)
            {
                Debug.LogWarning("[AvatarManager] Main download già in corso, ignoro richiesta duplicata/ravvicinata.");
                return;
            }

            downloadAvatar.SetOnDownloaded(OnMainAvatarDownloaded);
            
            _isMainDownloading = true;
            currentDownloadAvatarId = avatarInfo.AvatarId;
            
            Debug.Log($"[AvatarManager] [MAIN] Download start avatarId={currentDownloadAvatarId}");
            StartWatchdog(mainTimeoutSeconds); 
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

#if UNITY_WEBGL && !UNITY_EDITOR
        StartLoadMainFromUrlWebGL(avatarData);
#else
        var info = avatarData.ToAvatarInfo();
        LoadAvatarImmediately(info);
#endif
    }

    public void OnMainAvatarDownloaded(Transform loaderTransform)
    {
        // FIX 2: Invalida watchdog se completa normalmente
        _watchId++;

        _isMainDownloading = false;

        if (loaderTransform == null)
        {
            Debug.LogError("[AvatarManager] Loader transform is null in OnMainAvatarDownloaded!");
            return;
        }

        if (cancelPendingDownloads)
        {
            // CRITICAL FIX: Se loaderTransform è il GameObject che contiene DownloadAvatar, NON distruggerlo!
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

        // FIX: Strip Lights/Cameras from Main Avatar to prevent blinding light or extra cameras
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

        // FIX 3 REVISED: 
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
            // Nota: Iteriamo al contrario o usiamo while per sicurezza mentre reparentiamo
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
        // currentDownloadMode = DownloadMode.Main; // Removed to fix compilation error

        StartCoroutine(SetupAnimatorNextFrame(currentAvatar));

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

        // Debug utilissimo: ora deve essere vicino allo spawnpoint
        Debug.Log($"[Avatar] container world={container.position} local={container.localPosition} children={container.childCount}");

        uiFlowController?.OnAvatarDownloaded(container);
    }

    private IEnumerator SetupLipSyncNextFrame(Transform avatarRoot)
    {
        // aspetta 1 frame: su WebGL/GLTFast evita edge-case mentre Unity finalizza renderer/mesh
        yield return null;
        lipSyncBinder.Setup(avatarRoot);
    }

    private IEnumerator SetupAnimatorNextFrame(GameObject avatarGO)
    {
        yield return null;

        if (idleController == null)
        {
            Debug.LogWarning("[Anim] idleController non assegnato (AvatarManager). Rimarra' in T-pose.");
            yield break;
        }

        var animator = avatarGO.GetComponent<Animator>();
        if (animator == null) animator = avatarGO.AddComponent<Animator>();

        animator.applyRootMotion = animatorApplyRootMotion;
        animator.runtimeAnimatorController = idleController;

        // chiave: costruisce l’Avatar humanoid a runtime
        animator.avatar = HumanoidAvatarBuilder.Build(avatarGO);

        // utile in WebGL / camera lontane
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Debug.Log("[Anim] Animator pronto: Idle in play.");
    }

    private IEnumerator SetupIdleLookNextFrame(Transform avatarRoot)
    {
        yield return null;
        idleLook.Setup(avatarRoot);
    }



    public void RemoveCurrentAvatar()
    {
        // Ferma logiche legate all’avatar runtime
        if (idleLook != null) idleLook.Clear();

        if (lipSyncBinder != null)
            lipSyncBinder.ClearTargets();

        if (currentAvatar != null)
        {
            Destroy(currentAvatar);
            currentAvatar = null;
        }

        // Riattiva il placeholder
        if (baseModelToReplace != null && !hideBaseModel)
        {
            baseModelToReplace.gameObject.SetActive(true);

            if (idleLook != null)
                StartCoroutine(SetupIdleLookOnBaseNextFrame());
        }
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
            // WebGL: avatar list is now backend-driven, so persistent file caching is skipped.
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
            
        // Se c'era un save in sospeso, eseguilo ora
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
        // In WebGL StreamingAssets è già un URL (stessa origin del build)
        url = $"{Application.streamingAssetsPath}/{fileName}";
#else
        // In editor/standalone è un path su disco: serve file:// con slash corretti
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

#if UNITY_WEBGL && !UNITY_EDITOR
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
        // By default we used to always destroy/recreate the downloader.
        // For carousel previews we may want to KEEP the downloader alive, because some SDKs
        // cleanup runtime textures/materials in OnDestroy, which would invalidate already reparented models.
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
            // Case 1: downloadAvatar is a separate object/prefab (Standard case)
            if (downloadAvatar.gameObject != this.gameObject)
            {
                var go = Instantiate(downloadAvatar.gameObject, transform);
                go.name = "PreviewDownloader_Temp";
                _previewDownloader = go.GetComponent<DownloadAvatar>();
                go.SetActive(true);
            }
            // Case 2: downloadAvatar is attached to THIS AvatarManager (Edge case)
            else
            {
                var go = new GameObject("PreviewDownloader_Temp");
                go.transform.SetParent(transform, false);
                
                var newComp = go.AddComponent<DownloadAvatar>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(downloadAvatar), newComp);
                
                _previewDownloader = newComp;
                go.SetActive(true);
            }

            // Strip everything from the container itself
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

        // Detach first so the hierarchy is clean immediately, then destroy.
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

        // GLTFast/DownloadAvatar needs a clean slate. 
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
#if UNITY_WEBGL && !UNITY_EDITOR
        return DownloadPreviewToTransform(avatarData, transform, onLoaded, destroyDownloaderAfter);
#else
        // Se c'è già un download in corso, lo cancelliamo per far posto al nuovo (comportamento "Last One Wins" per preview rapida)
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

        // Ensure a clean slate for the new import
        DetachAndDestroyChildren(_previewDownloader.transform);

        // Increment session ID for new session
        _previewSessionId++;
        int currentSession = _previewSessionId;
        _isPreviewDownloading = true;

        // UI Feedback: Nascondi HintBar/Title
        uiFlowController?.SetPreviewModeUI(true);

        Debug.Log($"[AvatarManager] [PREVIEW gen={currentSession}] start avatarId={avatarData.avatarId}");

        // Capture specific instance for this session
        var currentDownloader = _previewDownloader;

        // Imposta callback
        currentDownloader.SetOnDownloaded(loader => OnPreviewDownloaded(loader, currentSession, onLoaded, currentDownloader, destroyDownloaderAfter));

        // Start
        var info = avatarData.ToAvatarInfo();
        currentDownloader.Download(info);
        
        // Watchdog per preview
        StartWatchdog(previewTimeoutSeconds);
        return true;
#endif
    }

    public void RequestAvatarListFromBackend(Action<List<AvatarData>> cb)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        StartCoroutine(WebGLRequestAvatarList(cb));
#else
        if (savedData != null)
        {
            cb?.Invoke(new List<AvatarData>(savedData.avatars));
        }
        else
        {
            cb?.Invoke(new List<AvatarData>());
        }
#endif
    }

    public bool DownloadPreviewToTransform(AvatarData data, Transform parent, Action<Transform> onLoaded)
    {
        return DownloadPreviewToTransform(data, parent, onLoaded, destroyDownloaderAfter: true);
    }

    public bool DownloadPreviewToTransform(AvatarData data, Transform parent, Action<Transform> onLoaded, bool destroyDownloaderAfter)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (_isPreviewDownloading)
        {
            CancelPreviewDownloads();
        }

        _webglPreviewSessionId++;
        int sessionId = _webglPreviewSessionId;
        _isPreviewDownloading = true;

        uiFlowController?.SetPreviewModeUI(true);
        StartWatchdog(previewTimeoutSeconds);

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
             // UI reset for safety
             uiFlowController?.SetPreviewModeUI(false);
             _isPreviewDownloading = false;
             return;
        }

        // 3. Cleanup stato
        _isPreviewDownloading = false;
        uiFlowController?.SetPreviewModeUI(false); // Restore UI

        // 4. Elaborazione modello
        if (loader != null)
        {
            // Strip components safely
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

        // 5. Cleanup finale del downloader temporaneo
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
        // HARD CANCEL
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

#if UNITY_WEBGL && !UNITY_EDITOR
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
            onCachedUrl?.Invoke(response != null ? response.cached_glb_url : null);
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
        // The second parameter is an optional base URI for resolving external resources; null is correct here
        // because this avatar GLB is fully self-contained and loaded from in-memory bytes only.
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

        StartCoroutine(SetupAnimatorNextFrame(currentAvatar));

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

    private List<AvatarData> BuildLocalAvatarList()
    {
        var list = new List<AvatarData>();
        list.Add(CreateLocalAvatar(localModel1AvatarId, localModel1FileName, localModel1Gender));
        list.Add(CreateLocalAvatar(localModel2AvatarId, localModel2FileName, localModel2Gender));
        return list;
    }

    private AvatarData CreateLocalAvatar(string avatarId, string fileName, string gender)
    {
        return new AvatarData
        {
            avatarId = avatarId,
            urlType = "glb",
            bodyId = "local",
            gender = gender,
            source = "local",
            localFile = fileName,
            displayName = avatarId,
            isWebGL = true
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

        return $"{Application.streamingAssetsPath}/{localFile}";
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
            url = item.cached_glb_url,
            sourceUrl = item.url_originale,
            urlType = string.IsNullOrEmpty(item.urlType) ? "glb" : item.urlType,
            bodyId = item.bodyId,
            gender = item.gender,
            source = item.source,
            localFile = item.local_file,
            displayName = item.display_name,
            isWebGL = true
        };
    }
#endif

    public bool IsAvatarCached(AvatarData avatarData)
    {
        if (avatarData == null)
        {
            return false;
        }

        if (avatarData.bodyId == "local" || (avatarData.avatarId != null && avatarData.avatarId.StartsWith("LOCAL_")))
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

    // --- FIX 2: Watchdog per timeout di download ---
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
            
            // Log dettagliato per debugging
            if (_isMainDownloading)
                Debug.LogWarning("[AvatarManager] Main download in timeout.");
            if (_isPreviewDownloading)
                Debug.LogWarning("[AvatarManager] Preview download in timeout.");
                
            _isMainDownloading = false;
            
            // Per preview, il timeout agisce come un cancel
            if (_isPreviewDownloading)
            {
                 CancelPreviewDownloads();
            }
        }
    }
    // --- fine FIX 2 ---


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

#if UNITY_WEBGL && !UNITY_EDITOR
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
#endif
}
