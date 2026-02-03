using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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

    [Header("Selection FX")]
    [SerializeField] private float rotationSpeed = 25f;
    [SerializeField] private float initialYaw = 180f;
    [SerializeField] private float resetRotationDuration = 0.35f;
    [SerializeField] private Color highlightColor = new Color(0.25f, 0.9f, 1f, 1f);
    [SerializeField, Range(0.2f, 1f)] private float dimFactor = 0.55f;
    [SerializeField] private GameObject selectionLightPrefab; // Optional light to highlight selected item

    [Header("Input")]
    [SerializeField] private float enterLibraryInputLockSeconds = 0.25f;
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
    private AmbientMode previousAmbientMode;
    private Color previousAmbientColor;
    private float previousAmbientIntensity;
    private bool ambientOverrideActive;

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
    }

    public void ShowLibrary(bool enabled)
    {
        if (enabled && listRequestInFlight)
        {
            Debug.Log("[AvatarLibraryCarousel] Avatar list request already in flight.");
            return;
        }

        active = enabled;
        
        // Patch 2: Blocca input di conferma/back per un tempo breve all'ingresso
        inputLockedUntil = enabled ? (Time.unscaledTime + enterLibraryInputLockSeconds) : 0f;

        if (!enabled)
        {
            // Disable fill light on exit
            if (carouselFillLight != null)
            {
                carouselFillLight.enabled = false;
            }

            RestoreAmbientLighting();

            // HARD CANCEL: Incrementa generation per invalidare TUTTI i callback preview pendenti
            previewGeneration++;
            
            // Cancella download in corso
            avatarManager?.CancelPreviewDownloads();
            SetCarouselDownloading(false);
            listRequestInFlight = false;
            
            // Reset stato carosello
            ResetCarouselState();
            
            // Pulizia completa
            ClearCarousel();
            return;
        }

        Debug.Log("[AvatarLibraryCarousel] Enter library");

        // Enable fill light on enter
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
            selectedIndex = RestoreSelectedIndex();
            ApplySelectionVisuals();
            StartNextDownload();
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

        // Try to match the layer of the trackRoot to ensure we hit the models
        if (trackRoot != null)
        {
            lightObj.layer = trackRoot.gameObject.layer;
            // Note: If Culling Mask is used by camera, this light affects everything in that layer.
            // Since this is a library view, likely isolated or we want visibility.
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

        // Patch 3: Determina se confirm/back sono bloccati all'ingresso
        bool lockConfirmAndBack = Time.unscaledTime < inputLockedUntil;

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            SetSelectedIndex(selectedIndex - 1);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            SetSelectedIndex(selectedIndex + 1);
        }

        // Solo se non bloccati
        if (!lockConfirmAndBack)
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                ConfirmSelection();
            }
            else if (Input.GetKeyDown(KeyCode.Backspace))
            {
                uiFlowController?.GoToMainMenu();
                return;
            }
        }
    }

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
            // Fix 1: Notifica a UIFlowController che l'utente ha richiesto un main load
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

        // Track offset shifts the entire carousel so items appear centered
        // Formula: trackOffsetX provides base shift, selectedIndex * spacing moves to selected item
        float targetOffset = trackOffsetX - selectedIndex * spacing * scrollStepMultiplier;
        Vector3 target = trackBaseLocalPosition + new Vector3(targetOffset, 0f, 0f);
        trackRoot.localPosition = Vector3.Lerp(trackRoot.localPosition, target, scrollLerp * Time.unscaledDeltaTime);
    }

    private void RotateSelected()
    {
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

    private void BuildCarousel(List<AvatarManager.AvatarData> avatarList)
    {
        ClearCarousel();
        
        // Increment preview generation to invalidate any pending preview callbacks
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
            // Always mark as not ready - carousel downloads fresh previews for visual consistency
            // even if the avatar is cached, to ensure all items look uniform
            item.ready = false;
            item.hasError = false;
            item.retryCount = 0;
            item.modelRoot = CreatePlaceholder(item.root);
            CacheRenderers(item);

            items.Add(item);
            downloadQueue.Enqueue(index);
            index++;
        }

        Debug.Log($"[AvatarLibraryCarousel] List loaded: {items.Count} items");
    }

    private Transform CreateItemRoot(int index, string avatarId)
    {
        var root = new GameObject($"AvatarItem_{index}_{avatarId}").transform;
        root.SetParent(trackRoot, false);

        // IMPORTANT: allinea il layer degli item al layer del trackRoot.
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
        
        // Fix 4: Forza il placeholder ad essere attivo e visibile
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
        // Handle Material Property Blocks
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
                        // Fallback programmativo se manca il prefab
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
                        lightComp.intensity = 1.2f; // Leggermente più forte per evidenziare
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

    // Patch 3: Helper methods per normalizzazione dei modelli caricati
    private bool TryGetWorldBounds(Transform root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            bounds = new Bounds(root.position, Vector3.one);
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return true;
    }

    private float GetPlaceholderHeightFallback()
    {
        if (placeholderSource == null) return 1.7f;

        if (TryGetWorldBounds(placeholderSource, out var b))
        {
            if (b.size.y > 0.01f) return b.size.y;
        }

        return 1.7f;
    }

    private void StartNextDownload()
    {
        // Controlla specificamente se il preview downloader è occupato
        if (avatarManager == null || avatarManager.IsPreviewDownloading)
        {
            return;
        }

        if (downloadQueue.Count == 0)
        {
            SetCarouselDownloading(false);
            return;
        }

        int index = downloadQueue.Dequeue();
        if (index < 0 || index >= items.Count)
        {
            StartNextDownload();
            return;
        }

        var item = items[index];
        if (item.hasError)
        {
            StartNextDownload();
            return;
        }

        int generation = previewGeneration;
        SetCarouselDownloading(true);
        Debug.Log($"[AvatarLibraryCarousel] Preview start: {item.data.avatarId}");
        bool started = avatarManager.DownloadPreviewToTransform(item.data, transform, loader =>
        {
            if (!active || generation != previewGeneration)
            {
                ClearLoaderChildren(loader);
                Debug.Log("[AvatarLibraryCarousel] Preview canceled (generation change)");
                return;
            }

            bool replaced = ReplaceWithLoadedModel(item, loader);
            if (!replaced)
            {
                HandlePreviewFailure(item, "renderer missing or load failed");
            }
            else
            {
                Debug.Log($"[AvatarLibraryCarousel] Preview done: {item.data.avatarId}");
            }
            if (active)
            {
                StartNextDownload();
            }
        }, destroyDownloaderAfter: false);

        if (!started)
        {
            // Avoid infinite loop if data is invalid
            if (item.data != null)
            {
                 downloadQueue.Enqueue(index);
            }
        }
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

        // IMPORTANT: Non clonare child-per-child.
        // Molti avatar GLTF hanno armature e skinned mesh come siblings; clonare separatamente
        // può lasciare bones/rootBone puntati all'originale e rompersi quando il loader viene distrutto.
        // Spostiamo (reparent) l'intera gerarchia preservando i transform.
        bool isLocal = item.data.bodyId == "local" || (item.data.avatarId != null && item.data.avatarId.StartsWith("LOCAL_"));
        while (loaderTransform.childCount > 0)
        {
            var child = loaderTransform.GetChild(0);
            child.SetParent(container, false);

            // Non azzerare localPosition/localRotation/localScale: può rompere offset/armature.
            if (isLocal)
            {
                // Forza l'aggiornamento dei materiali sui modelli locali (se necessario)
                FixLocalModelRendering(child);
            }
        }
        
        // Assicura che i layer siano corretti
        SetLayerRecursively(container.gameObject, item.root.gameObject.layer);

        // Svuota il loader (a questo punto dovrebbe essere già vuoto)
        ClearLoaderChildren(loaderTransform);

        // Attiva i GameObject dei renderer e forza enabled.
        foreach (var r in container.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;

            r.gameObject.SetActive(true);
            r.enabled = true;

            // Pulisce eventuali override precedenti sui materiali (può evitare tint/emission strani)
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
        
        // Forza un refresh delle mesh
        var meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                mf.sharedMesh = Instantiate(mf.sharedMesh);
            }
        }
        
        var skinnedMeshRenderers = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in skinnedMeshRenderers)
        {
            if (smr.sharedMesh != null)
            {
                smr.sharedMesh = Instantiate(smr.sharedMesh);
            }
        }
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
        // HARD CANCEL: Invalida tutte le sessioni precedenti
        previewGeneration++;
        selectedIndex = 0;
        inputLockedUntil = 0f;

        // Stoppa tutte le coroutine di rotazione in corso
        StopAllCoroutines();
        
        // Reset download queue
        downloadQueue.Clear();

        if (trackRoot != null)
        {
            trackRoot.localPosition = trackBaseLocalPosition + new Vector3(trackOffsetX, 0f, 0f);
        }

        ResetAllItemRotationsImmediate();
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

    // Patch 4: Helper per svuotare il loader dopo il cloning
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

    private int RestoreSelectedIndex()
    {
        string lastId = PlayerPrefs.GetString(LastSelectedAvatarKey, string.Empty);
        if (string.IsNullOrEmpty(lastId))
        {
            return 0;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].data != null && items[i].data.avatarId == lastId)
            {
                return i;
            }
        }

        return 0;
    }


    private void ClearCarousel()
    {
        foreach (var item in items)
        {
            // Stop any running rotation reset coroutines before destroying
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
