using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.EventSystems;
using Avaturn.Core.Runtime.Scripts.Avatar;

public class AvatarLibraryCarousel : MonoBehaviour
{
    [Serializable]
    private class CarouselItem
    {
        public AvatarManager.AvatarData data;
        public Transform root;
        public Transform modelRoot;
        public bool ready;
        public bool hasError;
        public int retryCount;
        public List<RendererInfo> renderers = new List<RendererInfo>();
        public Quaternion initialRotation;
        public Coroutine resetRotationRoutine;
    }

    private class RendererInfo
    {
        public Renderer renderer;
        public Color baseColor;
        public bool hasBaseColor;
        public bool hasEmission;
    }

    [Header("References")]
    [SerializeField] private AvatarManager avatarManager;
    [SerializeField] private UIFlowController uiFlowController;
    [SerializeField] private Transform trackRoot;
    [SerializeField] private Transform placeholderSource;

    [Header("Layout")]
    [SerializeField] private float spacing = 1.6f;
    [SerializeField] private Vector3 itemScale = Vector3.one;
    [SerializeField] private float scrollLerp = 8f;
    [SerializeField] private float trackOffsetX = 0.8f;
    [SerializeField, Min(0.1f)] private float scrollStepMultiplier = 1f;
    [SerializeField] private bool centerCarouselInTouchMode = true;
    [SerializeField] private float touchTrackOffsetX = 0f;
    [SerializeField, Min(0.1f)] private float touchScrollStepMultiplier = 1f;

    [Header("Selection FX")]
    [SerializeField] private float rotationSpeed = 25f;
    [SerializeField] private float initialYaw = 180f;
    [SerializeField] private float resetRotationDuration = 0.35f;
    [SerializeField] private Color highlightColor = new Color(0.25f, 0.9f, 1f, 1f);
    [SerializeField, Range(0.2f, 1f)] private float dimFactor = 0.55f;
    [SerializeField] private GameObject selectionLightPrefab; // Optional light to highlight selected item

    [Header("Delete/Reset FX")]
    [SerializeField] private float fxRotationSpeed = 720f;
    [SerializeField] private float fxDownSpeed = 0.6f;

    [Header("Fade In FX")]
    [SerializeField] private float fadeInDuration = 0.3f;
    [SerializeField] private float fadeInStagger = 0.07f;

    [Header("Input")]
    [SerializeField] private float enterLibraryInputLockSeconds = 0.25f;
    [SerializeField] private bool enableTouchInput = true;
    [SerializeField, Min(12f)] private float touchSwipeThresholdPixels = 70f;
    [SerializeField, Min(0.05f)] private float touchTapMaxDurationSeconds = 0.25f;
    [SerializeField, Min(2f)] private float touchTapMaxMovePixels = 20f;
    private float inputLockedUntil = 0f;

    [Header("Preview Download")]
    [SerializeField] private float previewRetryDelaySeconds = 0.4f;
    [SerializeField] private int maxPreviewRetries = 1;

    private readonly List<CarouselItem> items = new List<CarouselItem>();
    private readonly Queue<int> downloadQueue = new Queue<int>();
    private int selectedIndex;
    private bool active;
    private int previewGeneration;
    private Vector3 trackBaseLocalPosition;
    private Light carouselFillLight;
    private bool listRequestInFlight;
    private bool carouselDownloading;
    private const string LastSelectedAvatarKey = "Carousel_LastSelectedAvatarId";
    private const float ResetEffectDuration = 0.6f;
    private const float ResetJumpHeight = 0.12f;
    private const float DeleteEffectDuration = 3f;
    private AmbientMode previousAmbientMode;
    private Color previousAmbientColor;
    private float previousAmbientIntensity;
    private bool ambientOverrideActive;
    private bool fxActive;
    private Coroutine fxRoutine;
    private Transform _stagingRoot;
    private int trackedTouchFingerId = -1;
    private Vector2 trackedTouchStartPosition;
    private float trackedTouchStartTime;
    private bool trackedTouchStartedOverUi;
    private bool trackedTouchConsumedBySwipe;

    public void Initialize(AvatarManager manager, UIFlowController flowController)
    {
        avatarManager = manager;
        uiFlowController = flowController;
    }

    private void Awake()
    {
        if (trackRoot != null)
        {
            trackBaseLocalPosition = trackRoot.localPosition;
        }
        EnsureStagingRoot();
    }

    private void EnsureStagingRoot()
    {
        if (_stagingRoot != null) return;
        var go = new GameObject("_CarouselStaging");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, -1000f, 0f);
        _stagingRoot = go.transform;
    }

    public void ShowLibrary(bool enabled)
    {
        if (enabled && listRequestInFlight)
        {
            return;
        }

        active = enabled;
        
        // All'ingresso blocchiamo conferma/back per un intervallo breve.
        inputLockedUntil = enabled ? (Time.unscaledTime + enterLibraryInputLockSeconds) : 0f;

        if (!enabled)
        {
            // Disattiviamo la fill light in uscita.
            if (carouselFillLight != null)
            {
                carouselFillLight.enabled = false;
            }

            RestoreAmbientLighting();

            // Incrementiamo la generazione per invalidare tutti i callback preview pendenti.
            previewGeneration++;
            
            // Cancelliamo i download in corso.
            avatarManager?.CancelPreviewDownloads();
            SetCarouselDownloading(false);
            listRequestInFlight = false;
            
            // Reimpostiamo lo stato del carosello.
            ResetCarouselState();
            
            // Pulizia completa.
            ClearCarousel();
            return;
        }

        // Attiviamo la fill light in ingresso.
        EnsureCarouselLighting();
        if (carouselFillLight != null)
        {
            carouselFillLight.enabled = true;
        }
        ApplyAmbientLighting();

        avatarManager?.ResetPreviewStateForLibrary();
        listRequestInFlight = true;
        SetCarouselDownloading(false);
        previewGeneration++;
        ClearCarousel();

        if (avatarManager == null)
        {
            Debug.LogWarning("[AvatarLibraryCarousel] AvatarManager reference missing.");
            listRequestInFlight = false;
            return;
        }

        avatarManager.RequestAvatarListFromBackend(result =>
        {
            if (!active)
            {
                return;
            }

            listRequestInFlight = false;
            BuildCarousel(result);
            selectedIndex = 0;
            ApplySelectionVisuals();
            uiFlowController?.OnAvatarLibrarySelectionChanged();
            StartNextDownload();
            StartCoroutine(StaggeredFadeIn());
        });
    }

    private void EnsureCarouselLighting()
    {
        if (carouselFillLight != null) return;

        var lightObj = new GameObject("CarouselFillLight");
        lightObj.transform.SetParent(transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 3f, -2f);
        lightObj.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);

        carouselFillLight = lightObj.AddComponent<Light>();
        carouselFillLight.type = LightType.Directional;
        carouselFillLight.intensity = 1.0f;
        carouselFillLight.color = Color.white;
        carouselFillLight.shadows = LightShadows.None;

        // Proviamo ad allineare il layer di trackRoot per colpire i modelli.
        if (trackRoot != null)
        {
            lightObj.layer = trackRoot.gameObject.layer;
            // Se la camera usa Culling Mask, questa luce influenza tutto quel layer.
            // Questa vista libreria e' isolata, quindi privilegiamo la visibilita'.
            carouselFillLight.cullingMask = 1 << trackRoot.gameObject.layer;
        }
    }

    private void Update()
    {
        if (!active)
        {
            return;
        }

        if (uiFlowController != null && uiFlowController.IsWebOverlayOpen)
        {
            return;
        }

        HandleInput();
        UpdateCarouselMotion();
        RotateSelected();
        if (avatarManager != null && !avatarManager.IsPreviewDownloading)
        {
            StartNextDownload();
        }
    }

    private void HandleInput()
    {
        if (!active)
            return;

        // Qui verifichiamo se conferma/indietro sono ancora bloccati all'ingresso.
        bool lockConfirmAndBack = Time.unscaledTime < inputLockedUntil;

        bool moveLeftPressed = Input.GetKeyDown(KeyCode.LeftArrow);
#if ENABLE_INPUT_SYSTEM
        if (!moveLeftPressed)
        {
            moveLeftPressed = IsInputSystemKeyPressed(UnityEngine.InputSystem.Key.LeftArrow);
        }
#endif
        if (moveLeftPressed)
        {
            SetSelectedIndex(selectedIndex - 1);
        }
        else
        {
            bool moveRightPressed = Input.GetKeyDown(KeyCode.RightArrow);
#if ENABLE_INPUT_SYSTEM
            if (!moveRightPressed)
            {
                moveRightPressed = IsInputSystemKeyPressed(UnityEngine.InputSystem.Key.RightArrow);
            }
#endif
            if (moveRightPressed)
            {
                SetSelectedIndex(selectedIndex + 1);
            }
        }

        // Solo se non bloccati
        if (!lockConfirmAndBack)
        {
            bool submitPressed = Input.GetKeyDown(KeyCode.Return);
#if ENABLE_INPUT_SYSTEM
            if (!submitPressed)
            {
                submitPressed = IsInputSystemKeyPressed(UnityEngine.InputSystem.Key.Enter) ||
                                IsInputSystemKeyPressed(UnityEngine.InputSystem.Key.NumpadEnter);
            }
#endif
            if (submitPressed)
            {
                ConfirmSelection();
            }
            else
            {
                bool deletePressed = Input.GetKeyDown(KeyCode.Delete);
#if ENABLE_INPUT_SYSTEM
                if (!deletePressed)
                {
                    deletePressed = IsInputSystemKeyPressed(UnityEngine.InputSystem.Key.Delete);
                }
#endif
                if (deletePressed)
                {
                    uiFlowController?.RequestDeleteSelectedAvatar();
                    return;
                }

                bool backPressed = Input.GetKeyDown(KeyCode.Backspace);
#if ENABLE_INPUT_SYSTEM
                if (!backPressed)
                {
                    backPressed = IsInputSystemKeyPressed(UnityEngine.InputSystem.Key.Backspace);
                }
#endif
                if (backPressed)
                {
                    uiFlowController?.GoBack();
                    return;
                }
            }
        }

        HandleTouchInput(lockConfirmAndBack);
    }

    private void HandleTouchInput(bool lockConfirmAndBack)
    {
        if (!enableTouchInput || !Input.touchSupported)
        {
            return;
        }

        int touchCount = Input.touchCount;
        if (touchCount <= 0)
        {
            return;
        }

        for (int i = 0; i < touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    if (trackedTouchFingerId != -1)
                    {
                        break;
                    }

                    trackedTouchFingerId = touch.fingerId;
                    trackedTouchStartPosition = touch.position;
                    trackedTouchStartTime = Time.unscaledTime;
                    var eventSystem = EventSystem.current;
                    trackedTouchStartedOverUi = eventSystem != null && eventSystem.IsPointerOverGameObject(touch.fingerId);
                    trackedTouchConsumedBySwipe = false;
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (touch.fingerId != trackedTouchFingerId || trackedTouchStartedOverUi || trackedTouchConsumedBySwipe)
                    {
                        break;
                    }

                    Vector2 delta = touch.position - trackedTouchStartPosition;
                    float absX = Mathf.Abs(delta.x);
                    float absY = Mathf.Abs(delta.y);
                    if (absX >= touchSwipeThresholdPixels && absX > absY)
                    {
                        int direction = delta.x < 0f ? 1 : -1;
                        SetSelectedIndex(selectedIndex + direction);
                        trackedTouchConsumedBySwipe = true;
                    }
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (touch.fingerId != trackedTouchFingerId)
                    {
                        break;
                    }

                    if (!trackedTouchStartedOverUi && !trackedTouchConsumedBySwipe && !lockConfirmAndBack)
                    {
                        Vector2 releaseDelta = touch.position - trackedTouchStartPosition;
                        float duration = Time.unscaledTime - trackedTouchStartTime;
                        float maxMoveSqr = touchTapMaxMovePixels * touchTapMaxMovePixels;
                        if (releaseDelta.sqrMagnitude <= maxMoveSqr && duration <= touchTapMaxDurationSeconds)
                        {
                            ConfirmSelection();
                        }
                    }

                    trackedTouchFingerId = -1;
                    trackedTouchStartedOverUi = false;
                    trackedTouchConsumedBySwipe = false;
                    break;
            }
        }
    }

#if ENABLE_INPUT_SYSTEM
    private static bool IsInputSystemKeyPressed(UnityEngine.InputSystem.Key key)
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }
#endif

    private void SetSelectedIndex(int index)
    {
        if (items.Count == 0)
        {
            return;
        }

        int clamped = Mathf.Clamp(index, 0, items.Count - 1);
        if (clamped == selectedIndex)
        {
            return;
        }

        int previousIndex = selectedIndex;
        selectedIndex = clamped;
        if (previousIndex >= 0 && previousIndex < items.Count)
        {
            BeginResetRotation(items[previousIndex]);
        }
        if (selectedIndex >= 0 && selectedIndex < items.Count && items[selectedIndex].root != null)
        {
            items[selectedIndex].root.localRotation = items[selectedIndex].initialRotation;
        }
        ApplySelectionVisuals();
        uiFlowController?.OnAvatarLibrarySelectionChanged();
    }

    public bool TryGetSelectedAvatarData(out AvatarManager.AvatarData data)
    {
        data = null;
        if (items.Count == 0 || selectedIndex < 0 || selectedIndex >= items.Count)
        {
            return false;
        }

        data = items[selectedIndex].data;
        return data != null;
    }

    private void ConfirmSelection()
    {
        if (items.Count == 0)
        {
            return;
        }

        var item = items[selectedIndex];
        if (item.ready)
        {
            // Qui notifichiamo a UIFlowController che l'utente ha richiesto un caricamento principale.
            uiFlowController?.NotifyMainAvatarLoadRequested();
            avatarManager?.LoadSavedAvatar(item.data);
            PlayerPrefs.SetString(LastSelectedAvatarKey, item.data.avatarId);
        }
        else
        {
            uiFlowController?.UpdateDebugText("Downloading...");
        }
    }

    private void UpdateCarouselMotion()
    {
        if (trackRoot == null)
        {
            return;
        }

        float offsetX = GetActiveTrackOffsetX();
        float stepMultiplier = GetActiveScrollStepMultiplier();

        // Il track offset sposta tutto il carosello per centrare gli elementi.
        // Regola: `trackOffsetX` da' lo shift base, `selectedIndex * spacing` porta all'elemento selezionato.
        float targetOffset = offsetX - selectedIndex * spacing * stepMultiplier;
        Vector3 target = trackBaseLocalPosition + new Vector3(targetOffset, 0f, 0f);
        trackRoot.localPosition = Vector3.Lerp(trackRoot.localPosition, target, scrollLerp * Time.unscaledDeltaTime);
    }

    private void RotateSelected()
    {
        if (fxActive)
        {
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= items.Count)
        {
            return;
        }

        var selected = items[selectedIndex];
        if (selected.root == null)
        {
            return;
        }

        selected.root.Rotate(Vector3.up, rotationSpeed * Time.unscaledDeltaTime, Space.Self);
    }

    public IEnumerator PlayResetEffect()
    {
        if (!TryGetSelectedItem(out var item) || item.root == null)
        {
            yield break;
        }

        if (fxRoutine != null)
        {
            StopCoroutine(fxRoutine);
        }

        fxRoutine = StartCoroutine(ResetEffectRoutine(item));
        yield return fxRoutine;
        fxRoutine = null;
    }

    public IEnumerator PlayDeleteEffect()
    {
        if (!TryGetSelectedItem(out var item) || item.root == null)
        {
            yield break;
        }

        if (fxRoutine != null)
        {
            StopCoroutine(fxRoutine);
        }

        fxRoutine = StartCoroutine(DeleteEffectRoutine(item));
        yield return fxRoutine;
        fxRoutine = null;
    }

    private IEnumerator ResetEffectRoutine(CarouselItem item)
    {
        fxActive = true;
        Vector3 startPos = item.root.localPosition;
        Quaternion startRot = item.root.localRotation;
        float elapsed = 0f;
        float spinTarget = Mathf.Max(1, Mathf.RoundToInt((fxRotationSpeed * ResetEffectDuration) / 360f)) * 360f;

        while (elapsed < ResetEffectDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / ResetEffectDuration);
            float jump = Mathf.Sin(t * Mathf.PI) * ResetJumpHeight;
            item.root.localPosition = startPos + Vector3.up * jump;
            float angle = Mathf.Lerp(0f, spinTarget, t);
            item.root.localRotation = startRot * Quaternion.Euler(0f, angle, 0f);
            yield return null;
        }

        item.root.localPosition = startPos;
        item.root.localRotation = startRot;
        fxActive = false;
    }

    private IEnumerator DeleteEffectRoutine(CarouselItem item)
    {
        fxActive = true;
        Vector3 startPos = item.root.localPosition;
        float elapsed = 0f;

        while (elapsed < DeleteEffectDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float fall = fxDownSpeed * elapsed;
            item.root.localPosition = startPos + Vector3.down * fall;
            item.root.Rotate(Vector3.up, fxRotationSpeed * Time.unscaledDeltaTime, Space.Self);
            yield return null;
        }

        fxActive = false;
    }

    private bool TryGetSelectedItem(out CarouselItem item)
    {
        item = null;
        if (selectedIndex < 0 || selectedIndex >= items.Count)
        {
            return false;
        }

        item = items[selectedIndex];
        return item != null && item.root != null;
    }

    private void BuildCarousel(List<AvatarManager.AvatarData> avatarList)
    {
        ClearCarousel();
        
        // Incrementiamo la generazione anteprime per invalidare i callback pendenti.
        previewGeneration++;

        if (avatarList == null || avatarList.Count == 0)
        {
            return;
        }

        int index = 0;
        foreach (var avatarData in avatarList)
        {
            var item = new CarouselItem
            {
                data = avatarData,
                root = CreateItemRoot(index, avatarData.avatarId)
            };

            item.initialRotation = Quaternion.Euler(0f, initialYaw, 0f);
            // Segniamo sempre come non pronto: il carosello scarica anteprime nuove per coerenza visiva,
            // anche se l'avatar e' in cache, cosi' tutti gli elementi restano uniformi.
            item.ready = false;
            item.hasError = false;
            item.retryCount = 0;
            item.modelRoot = CreatePlaceholder(item.root);
            CacheRenderers(item);

            items.Add(item);
            downloadQueue.Enqueue(index);

            // Partiamo invisibili per un fade-in sfalsato.
            if (item.root != null)
                item.root.localScale = Vector3.zero;

            index++;
        }

        Debug.Log($"[AvatarLibraryCarousel] List loaded: {items.Count} items");
    }

    private Transform CreateItemRoot(int index, string avatarId)
    {
        var root = new GameObject($"AvatarItem_{index}_{avatarId}").transform;
        root.SetParent(trackRoot, false);

        // Allineiamo il layer degli item a quello di trackRoot.
        // La fill light usa una cullingMask basata sul layer di trackRoot, quindi se gli item
        // restano su Default (0) possono risultare neri/non illuminati.
        int layer = trackRoot != null ? trackRoot.gameObject.layer : 0;
        root.gameObject.layer = layer;
        root.localPosition = new Vector3(index * spacing, 0f, 0f);
        root.localRotation = Quaternion.Euler(0f, initialYaw, 0f);
        root.localScale = itemScale;
        return root;
    }

    private Transform CreatePlaceholder(Transform parent)
    {
        if (placeholderSource == null)
        {
            return null;
        }

        var placeholder = Instantiate(placeholderSource.gameObject, parent);
        placeholder.name = "Placeholder";
        placeholder.transform.localPosition = Vector3.zero;
        placeholder.transform.localRotation = Quaternion.identity;
        placeholder.transform.localScale = Vector3.one;

        // Mantieni placeholder e figli sul layer del parent (per luci/camera culling coerenti)
        SetLayerRecursively(placeholder, parent.gameObject.layer);
        
        // Forziamo il placeholder ad essere attivo e visibile.
        placeholder.SetActive(true);
        
        // Assicura che tutti i renderer siano attivi
        foreach (var renderer in placeholder.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
        }
        
        return placeholder.transform;
    }

    private void CacheRenderers(CarouselItem item)
    {
        item.renderers.Clear();
        if (item.root == null)
        {
            return;
        }

        foreach (var renderer in item.root.GetComponentsInChildren<Renderer>(true))
        {
            var info = new RendererInfo { renderer = renderer };
            if (renderer.sharedMaterial != null)
            {
                if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                {
                    info.baseColor = renderer.sharedMaterial.GetColor("_BaseColor");
                    info.hasBaseColor = true;
                }
                else if (renderer.sharedMaterial.HasProperty("_Color"))
                {
                    info.baseColor = renderer.sharedMaterial.GetColor("_Color");
                    info.hasBaseColor = true;
                }

                info.hasEmission = renderer.sharedMaterial.HasProperty("_EmissionColor");
            }

            item.renderers.Add(info);
        }
    }

    private void ApplySelectionVisuals()
    {
        for (int i = 0; i < items.Count; i++)
        {
            bool selected = i == selectedIndex;
            ApplySelection(items[i], selected);
        }
    }

    private void ApplySelection(CarouselItem item, bool selected)
    {
        // Gestiamo i Material Property Block.
        var block = new MaterialPropertyBlock();
        foreach (var rendererInfo in item.renderers)
        {
            if (rendererInfo.renderer == null)
            {
                continue;
            }

            rendererInfo.renderer.GetPropertyBlock(block);
            if (rendererInfo.hasBaseColor)
            {
                var baseColor = rendererInfo.baseColor;
                var target = selected ? highlightColor : baseColor * dimFactor;
                if (rendererInfo.renderer.sharedMaterial.HasProperty("_BaseColor"))
                {
                    block.SetColor("_BaseColor", target);
                }
                else if (rendererInfo.renderer.sharedMaterial.HasProperty("_Color"))
                {
                    block.SetColor("_Color", target);
                }
            }

            if (rendererInfo.hasEmission)
            {
                var emission = selected ? highlightColor * 2f : Color.black;
                block.SetColor("_EmissionColor", emission);
            }

            rendererInfo.renderer.SetPropertyBlock(block);
        }

        // Gestione della luce di selezione
        if (item.root != null)
        {
            Transform existingLight = item.root.Find("SelectionLight");
            
            if (selected)
            {
                if (existingLight == null)
                {
                    GameObject lightObj;
                    if (selectionLightPrefab != null)
                    {
                        lightObj = Instantiate(selectionLightPrefab, item.root);
                    }
                    else
                    {
                        // Ripiego programmativo se manca il prefab.
                        lightObj = new GameObject("SelectionLight_Fallback");
                        lightObj.transform.SetParent(item.root, false);
                        var l = lightObj.AddComponent<Light>();
                        l.type = LightType.Spot;
                        l.spotAngle = 60f;
                    }

                    lightObj.name = "SelectionLight";
                    lightObj.transform.localPosition = new Vector3(0, 2f, 2f);
                    lightObj.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
                    
                    // Configura la luce per essere morbida e del colore giusto
                    Light lightComp = lightObj.GetComponent<Light>();
                    if (lightComp != null)
                    {
                        lightComp.intensity = 1.2f; // Leggermente piÃƒÂ¹ forte per evidenziare
                        lightComp.range = 6f;
                        lightComp.color = highlightColor;
#if UNITY_WEBGL && !UNITY_EDITOR
                        lightComp.shadows = LightShadows.None;
#else
                        lightComp.shadows = LightShadows.Soft;
#endif
                    }
                }
                else
                {
                    existingLight.gameObject.SetActive(true);
                }
            }
            else
            {
                if (existingLight != null)
                {
                    existingLight.gameObject.SetActive(false);
                }
            }
        }
    }
    private void StartNextDownload()
    {
        // Controlliamo in modo esplicito se il downloader delle anteprime e' occupato.
        if (avatarManager == null || avatarManager.IsPreviewDownloading)
        {
            return;
        }

        if (!TryDequeueNextDownload(out int index, out CarouselItem item))
        {
            SetCarouselDownloading(false);
            return;
        }

        int generation = previewGeneration;
        SetCarouselDownloading(true);
        EnsureStagingRoot();
        bool started = avatarManager.DownloadPreviewToTransform(item.data, _stagingRoot, loader =>
        {
            if (!active || generation != previewGeneration)
            {
                ClearLoaderChildren(loader);
                return;
            }

            bool replaced = ReplaceWithLoadedModel(item, loader);
            if (!replaced)
            {
                HandlePreviewFailure(item, "renderer missing or load failed");
            }
            if (active)
            {
                StartNextDownload();
            }
        }, destroyDownloaderAfter: false);

        if (!started)
        {
            // Evitiamo un loop infinito se i dati non sono validi.
            if (item.data != null)
            {
                 downloadQueue.Enqueue(index);
            }
        }
    }

    private bool TryDequeueNextDownload(out int index, out CarouselItem item)
    {
        while (downloadQueue.Count > 0)
        {
            int candidateIndex = downloadQueue.Dequeue();
            if (candidateIndex < 0 || candidateIndex >= items.Count)
            {
                continue;
            }

            var candidateItem = items[candidateIndex];
            if (candidateItem == null || candidateItem.hasError)
            {
                continue;
            }

            index = candidateIndex;
            item = candidateItem;
            return true;
        }

        index = -1;
        item = null;
        return false;
    }
    private bool ReplaceWithLoadedModel(CarouselItem item, Transform loaderTransform)
    {
        if (loaderTransform == null || item == null)
            return false;

        if (!active)
        {
            ClearLoaderChildren(loaderTransform);
            return false;
        }

        // Rimuovi placeholder/vecchio modello
        if (item.modelRoot != null)
            Destroy(item.modelRoot.gameObject);

        // Crea contenitore per il modello
        var containerGO = new GameObject("Model");
        var container = containerGO.transform;
        container.SetParent(item.root, false);
        container.localPosition = Vector3.zero;
        container.localRotation = Quaternion.identity;
        container.localScale = Vector3.one;

        // Non cloniamo figlio-per-figlio.
        // Molti avatar GLTF hanno armature e skinned mesh come siblings; clonare separatamente
        // puÃƒÂ² lasciare bones/rootBone puntati all'originale e rompersi quando il loader viene distrutto.
        // Spostiamo (reparent) l'intera gerarchia preservando le trasformazioni.
        var avatarData = item.data;
        bool isLocal = avatarData != null &&
                       (avatarData.bodyId == "local" || (!string.IsNullOrEmpty(avatarData.avatarId) && avatarData.avatarId.StartsWith("LOCAL_")));
        while (loaderTransform.childCount > 0)
        {
            var child = loaderTransform.GetChild(0);
            child.SetParent(container, false);

            // Non azzerare localPosition/localRotation/localScale: puo' rompere scostamenti/armature.
            if (isLocal)
            {
                // Forza l'aggiornamento dei materiali sui modelli locali (se necessario)
                FixLocalModelRendering(child);
            }
        }
        
        // Assicura che i layer siano corretti
        SetLayerRecursively(container.gameObject, item.root.gameObject.layer);

        // Svuotiamo il loader (a questo punto dovrebbe essere gia' vuoto).
        ClearLoaderChildren(loaderTransform);

        // Attiva i GameObject dei renderer e forza enabled.
        foreach (var r in container.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;

            r.gameObject.SetActive(true);
            r.enabled = true;

            // Pulisce eventuali override precedenti sui materiali (puÃƒÂ² evitare tint/emission strani)
            r.SetPropertyBlock(null);
        }

        if (!HasValidRenderer(container))
        {
            Destroy(containerGO);
            return false;
        }

        item.modelRoot = container;
        item.ready = true;

        CacheRenderers(item);
        ApplySelection(item, item == items[selectedIndex]);
        return true;
    }

    private void FixLocalModelRendering(Transform modelRoot)
    {
        if (modelRoot == null) return;
        
        // Forza un refresh di tutti i renderer
        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            // Temporaneamente disabilita e riabilita per forzare l'update
            renderer.enabled = false;
            renderer.enabled = true;
            
            // Forza l'update dei materiali
            if (renderer.sharedMaterial != null)
            {
                var materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null)
                    {
                        // Ricrea l'istanza del materiale
                        materials[i] = new Material(materials[i]);
                    }
                }
                renderer.materials = materials;
            }
        }
        
        // Non clonare le mesh runtime: su WebGL alcune SkinnedMesh importate possono
        // emettere warning sui vertex streams dopo Instantiate(sharedMesh).
        // Qui non modifichiamo i vertici, quindi il refresh renderer/materiali sopra e' sufficiente.
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach(Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void ResetCarouselState()
    {
        // Invalidiamo tutte le sessioni precedenti.
        previewGeneration++;
        selectedIndex = 0;
        inputLockedUntil = 0f;

        // Stoppa tutte le coroutine di rotazione in corso
        StopAllCoroutines();
        
        // Azzera la coda download.
        downloadQueue.Clear();

        if (trackRoot != null)
        {
            trackRoot.localPosition = trackBaseLocalPosition + new Vector3(GetActiveTrackOffsetX(), 0f, 0f);
        }

        ResetAllItemRotationsImmediate();
    }

    private float GetActiveTrackOffsetX()
    {
        if (centerCarouselInTouchMode && uiFlowController != null && uiFlowController.IsTouchUiActive)
        {
            return touchTrackOffsetX;
        }

        return trackOffsetX;
    }

    private float GetActiveScrollStepMultiplier()
    {
        if (centerCarouselInTouchMode && uiFlowController != null && uiFlowController.IsTouchUiActive)
        {
            return touchScrollStepMultiplier;
        }

        return scrollStepMultiplier;
    }

    private void ResetAllItemRotationsImmediate()
    {
        foreach (var item in items)
        {
            StopResetRotation(item);
            if (item.root != null)
            {
                item.root.localRotation = item.initialRotation;
            }
        }
    }

    private void BeginResetRotation(CarouselItem item)
    {
        if (item == null || item.root == null)
        {
            return;
        }

        StopResetRotation(item);
        item.resetRotationRoutine = StartCoroutine(ResetRotationRoutine(item));
    }

    private void StopResetRotation(CarouselItem item)
    {
        if (item == null || item.resetRotationRoutine == null)
        {
            return;
        }

        StopCoroutine(item.resetRotationRoutine);
        item.resetRotationRoutine = null;
    }

    private System.Collections.IEnumerator ResetRotationRoutine(CarouselItem item)
    {
        if (item == null || item.root == null)
        {
            yield break;
        }

        Quaternion start = item.root.localRotation;
        Quaternion target = item.initialRotation;
        float duration = Mathf.Max(0.01f, resetRotationDuration);
        float elapsed = 0f;

        while (elapsed < duration && item.root != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            item.root.localRotation = Quaternion.Slerp(start, target, t);
            yield return null;
        }

        if (item.root != null)
        {
            item.root.localRotation = target;
        }

        item.resetRotationRoutine = null;
    }

    // Supporto: qui svuotiamo il loader dopo la clonazione.
    private void ClearLoaderChildren(Transform loaderTransform)
    {
        if (loaderTransform == null) return;

        // Stacca i figli subito dal loader (effetto immediato), poi distruggili
        while (loaderTransform.childCount > 0)
        {
            var child = loaderTransform.GetChild(0);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }

    private void SetCarouselDownloading(bool downloading)
    {
        if (carouselDownloading == downloading)
        {
            return;
        }

        carouselDownloading = downloading;
        uiFlowController?.SetCarouselDownloading(downloading);
    }

    private void ApplyAmbientLighting()
    {
        if (ambientOverrideActive)
        {
            return;
        }

        previousAmbientMode = RenderSettings.ambientMode;
        previousAmbientColor = RenderSettings.ambientLight;
        previousAmbientIntensity = RenderSettings.ambientIntensity;
        ambientOverrideActive = true;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.8f, 0.8f, 0.8f, 1f);
        RenderSettings.ambientIntensity = 1.2f;
    }

    private void RestoreAmbientLighting()
    {
        if (!ambientOverrideActive)
        {
            return;
        }

        RenderSettings.ambientMode = previousAmbientMode;
        RenderSettings.ambientLight = previousAmbientColor;
        RenderSettings.ambientIntensity = previousAmbientIntensity;
        ambientOverrideActive = false;
    }

    private bool HasValidRenderer(Transform root)
    {
        if (root == null)
        {
            return false;
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null && renderer.enabled)
            {
                return true;
            }
        }

        return false;
    }

    private void HandlePreviewFailure(CarouselItem item, string reason)
    {
        if (item == null)
        {
            return;
        }

        item.ready = false;

        if (item.retryCount < maxPreviewRetries)
        {
            item.retryCount++;
            Debug.LogWarning($"[AvatarLibraryCarousel] Preview failed ({reason}) retry {item.retryCount}/{maxPreviewRetries}.");
            StartCoroutine(EnqueueRetryAfterDelay(item));
        }
        else
        {
            item.hasError = true;
            Debug.LogWarning($"[AvatarLibraryCarousel] Preview failed ({reason}) - giving up.");
        }
    }

    private System.Collections.IEnumerator EnqueueRetryAfterDelay(CarouselItem item)
    {
        yield return new WaitForSecondsRealtime(previewRetryDelaySeconds);

        if (!active || item == null)
        {
            yield break;
        }

        int index = items.IndexOf(item);
        if (index >= 0)
        {
            downloadQueue.Enqueue(index);
        }
    }
    private IEnumerator StaggeredFadeIn()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (!active) yield break;
            var item = items[i];
            if (item.root != null)
                StartCoroutine(ScaleInTransform(item.root, itemScale, fadeInDuration));
            if (i < items.Count - 1)
                yield return new WaitForSecondsRealtime(fadeInStagger);
        }
    }

    private IEnumerator ScaleInTransform(Transform target, Vector3 targetScale, float duration)
    {
        if (target == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (target == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Curva quadratica in uscita per una decelerazione morbida.
            float eased = 1f - (1f - t) * (1f - t);
            target.localScale = targetScale * eased;
            yield return null;
        }

        if (target != null)
            target.localScale = targetScale;
    }

    private void ClearCarousel()
    {
        foreach (var item in items)
        {
            // Fermiamo le coroutine di reset rotazione ancora attive prima di distruggere.
            StopResetRotation(item);
            
            if (item.root != null)
            {
                Destroy(item.root.gameObject);
            }
        }
        items.Clear();
        downloadQueue.Clear();
    }
}
