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
    }
}
