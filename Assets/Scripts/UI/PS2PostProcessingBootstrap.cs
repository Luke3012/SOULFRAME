using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PS2PostProcessingBootstrap : MonoBehaviour
{
    [Header("Apply")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool ensurePostOnAllCameras = true;
    [SerializeField] private bool enableHDRonCameras = true;

    [Header("Bloom Settings (PS2-ish)")]
    [SerializeField] private float bloomIntensity = 2.4f;
    [SerializeField] private float bloomThreshold = 0.45f;
    [SerializeField, Range(0f, 1f)] private float bloomScatter = 0.85f;
    [SerializeField] private Color bloomTint = new Color(0.7f, 0.9f, 1f, 1f);
    [SerializeField] private bool highQualityFiltering = true;

    [Header("WebGL Overrides")]
    [SerializeField] private bool webglDisableHDR = true;
    [SerializeField, Range(0f, 2f)] private float webglBloomIntensityMultiplier = 0.6f;
    [SerializeField, Range(-0.2f, 0.6f)] private float webglBloomThresholdOffset = 0.1f;

    [Header("Initialization Pulse")]
    [SerializeField, Min(0f)] private float initializationScatterPulseSpeed = 0.8f;
    [SerializeField, Range(0f, 1f)] private float avatarForegroundScatter = 0.4f;

    private Bloom runtimeBloom;
    private bool initializationScatterPulseActive;
    private bool avatarForegroundScatterActive;
    private float baseScatter;

    private void Awake()
    {
        baseScatter = Mathf.Clamp01(bloomScatter);
    }

    private void Start()
    {
        if (!applyOnStart) return;

        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
        {
            bool isWebGL = Application.platform == RuntimePlatform.WebGLPlayer;
            EnsureGlobalBloom(isWebGL);

            if (ensurePostOnAllCameras)
                EnsureCamerasHavePostProcessing(isWebGL);
        }
    }

    private void Update()
    {
        if (!initializationScatterPulseActive)
        {
            return;
        }

        if (!TryResolveRuntimeBloom())
        {
            return;
        }

        if (initializationScatterPulseSpeed <= 0f)
        {
            runtimeBloom.scatter.Override(1f);
            return;
        }

        float t = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * initializationScatterPulseSpeed));
        float scatter = Mathf.Lerp(baseScatter, 1f, t);
        runtimeBloom.scatter.Override(scatter);
    }

    public void SetInitializationScatterPulseActive(bool active)
    {
        initializationScatterPulseActive = active;
        if (initializationScatterPulseActive)
        {
            return;
        }

        ApplyCurrentStaticScatter();
    }

    public void SetAvatarForegroundScatterActive(bool active)
    {
        avatarForegroundScatterActive = active;
        if (initializationScatterPulseActive)
        {
            return;
        }

        ApplyCurrentStaticScatter();
    }

    private void EnsureCamerasHavePostProcessing(bool isWebGL)
    {
        var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var cam in cams)
        {
            cam.allowHDR = enableHDRonCameras && !(isWebGL && webglDisableHDR);

            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
                data.renderPostProcessing = true;
        }
    }

    private void EnsureGlobalBloom(bool isWebGL)
    {
        var volume = GetComponent<Volume>();
        if (volume == null) volume = gameObject.AddComponent<Volume>();

        volume.isGlobal = true;
        volume.priority = 10f;
        volume.weight = 1f;

        if (volume.profile == null)
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();

        if (!volume.profile.TryGet(out Bloom bloom))
            bloom = volume.profile.Add<Bloom>(true);

        bloom.active = true;
        float intensity = bloomIntensity;
        float threshold = bloomThreshold;
        if (isWebGL)
        {
            intensity *= webglBloomIntensityMultiplier;
            threshold = Mathf.Clamp01(bloomThreshold + webglBloomThresholdOffset);
        }

        bloom.intensity.Override(intensity);
        bloom.threshold.Override(threshold);
        bloom.scatter.Override(bloomScatter);
        bloom.tint.Override(bloomTint);
           
        bloom.highQualityFiltering.Override(highQualityFiltering);

        runtimeBloom = bloom;
        baseScatter = Mathf.Clamp01(bloomScatter);
    }

    private bool TryResolveRuntimeBloom()
    {
        if (runtimeBloom != null)
        {
            return true;
        }

        var volume = GetComponent<Volume>();
        if (volume == null || volume.profile == null)
        {
            return false;
        }

        if (!volume.profile.TryGet(out Bloom bloom))
        {
            return false;
        }

        runtimeBloom = bloom;
        baseScatter = runtimeBloom.scatter.overrideState ? runtimeBloom.scatter.value : bloomScatter;
        baseScatter = Mathf.Clamp01(baseScatter);
        return true;
    }

    private void ApplyCurrentStaticScatter()
    {
        if (!TryResolveRuntimeBloom())
        {
            return;
        }

        float targetScatter = avatarForegroundScatterActive ? avatarForegroundScatter : baseScatter;
        runtimeBloom.scatter.Override(Mathf.Clamp01(targetScatter));
    }
}
