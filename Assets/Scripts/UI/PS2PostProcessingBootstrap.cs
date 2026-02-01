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

    private void Start()
    {
        if (!applyOnStart) return;

        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
        {
            EnsureGlobalBloom();

            if (ensurePostOnAllCameras)
                EnsureCamerasHavePostProcessing();
        }
    }

    private void EnsureCamerasHavePostProcessing()
    {
        var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var cam in cams)
        {
            if (enableHDRonCameras)
                cam.allowHDR = true;

            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
                data.renderPostProcessing = true;
        }
    }

    private void EnsureGlobalBloom()
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
        bloom.intensity.Override(bloomIntensity);
        bloom.threshold.Override(bloomThreshold);
        bloom.scatter.Override(bloomScatter);
        bloom.tint.Override(bloomTint);
           
        bloom.highQualityFiltering.Override(highQualityFiltering);
    }
}
