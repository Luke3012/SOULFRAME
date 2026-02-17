using System;
using System.Collections.Generic;
using uLipSync;
using UnityEngine;

public class AvaturnULipSyncBinder : MonoBehaviour
{
    [Header("Targets (riempiti a runtime con Setup)")]
    [SerializeField] private SkinnedMeshRenderer[] targetRenderers;

    [Header("Blendshape name candidates")]
    public string[] aNames = { "viseme_aa", "A", "v_A", "Mouth_A", "mouthA", "MTH_A" };
    public string[] iNames = { "viseme_ih", "viseme_I", "I", "v_I", "Mouth_I", "mouthI", "MTH_I" };
    public string[] uNames = { "viseme_ou", "viseme_U", "U", "v_U", "Mouth_U", "mouthU", "MTH_U" };
    public string[] eNames = { "viseme_ee", "viseme_E", "E", "v_E", "Mouth_E", "mouthE", "MTH_E" };
    public string[] oNames = { "viseme_oh", "viseme_O", "O", "v_O", "Mouth_O", "mouthO", "MTH_O" };
    public string[] nNames = { "viseme_nn", "N", "v_N", "Mouth_N", "mouthN", "MTH_N" };

    [Header("Tuning")]
    [Range(0f, 200f)] public float maxWeight = 1f;
    [Range(0f, 4f)] public float vowelGain = 1.2f;
    [Range(0f, 20f)] public float smooth = 12f;
    
    [Header("WebGL Assist")]
    [SerializeField] private bool enableWebGlAssist = true;
    [SerializeField, Range(1f, 6f)] private float webGlWeightBoost = 1.8f;
    [SerializeField, Range(0.05f, 1f)] private float webGlCloseSpeedMultiplier = 0.35f;
    [SerializeField, Range(0f, 1f)] private float webGlVolumeFloor = 0.05f;

    [Header("Debug")]
    public bool logSetupSummary = true;
    public bool logBlendshapeNames = false;

    private Dictionary<SkinnedMeshRenderer, Indices> _indices = new Dictionary<SkinnedMeshRenderer, Indices>();

    private float _a, _i, _u, _e, _o, _n;
    private float _volume;

    [Serializable]
    private struct Indices
    {
        public int a, i, u, e, o, n;
        public bool HasAny => a >= 0 || i >= 0 || u >= 0 || e >= 0 || o >= 0 || n >= 0;
    }

    public void Setup(Transform avatarRoot)
    {
        if (avatarRoot == null)
        {
            Debug.LogError("[LipSync] Setup: avatarRoot null");
            return;
        }

        // Ricrea (non Clear) -> evita crash WebGL su Array.Clear(null)
        _indices = new Dictionary<SkinnedMeshRenderer, Indices>(32);

        var all = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var list = new List<SkinnedMeshRenderer>(all.Length);
        for (int k = 0; k < all.Length; k++)
        {
            var r = all[k];
            if (!r) continue;
            if (!r.sharedMesh) continue;
            if (r.sharedMesh.blendShapeCount <= 0) continue;
            list.Add(r);
        }
        targetRenderers = list.ToArray();

        int hooked = 0;

        foreach (var r in targetRenderers)
        {
            var idx = new Indices
            {
                a = FindBlendShapeIndex(r, aNames),
                i = FindBlendShapeIndex(r, iNames),
                u = FindBlendShapeIndex(r, uNames),
                e = FindBlendShapeIndex(r, eNames),
                o = FindBlendShapeIndex(r, oNames),
                n = FindBlendShapeIndex(r, nNames),
            };

            if (idx.HasAny)
            {
                _indices[r] = idx;
                hooked++;
            }

            if (logBlendshapeNames)
            {
                Debug.Log($"[LipSync] Renderer={r.name} mesh={r.sharedMesh.name} blendShapes={r.sharedMesh.blendShapeCount}");
                for (int i = 0; i < r.sharedMesh.blendShapeCount; i++)
                    Debug.Log($"  - {r.sharedMesh.GetBlendShapeName(i)}");
            }
        }

        if (logSetupSummary)
        {
            if (hooked == 0)
                Debug.LogWarning("[LipSync] Setup: nessun blendshape trovato (avatar T1 o nomi diversi).");
            else
                Debug.Log($"[LipSync] Setup OK. Renderers agganciati: {hooked}");
        }
    }

    // Collega questo metodo a: uLipSync -> Parameters -> On Lip Sync Updated (LipSyncInfo)
    public void OnLipSyncUpdate(LipSyncInfo info)
    {
        _volume = info.volume;

        float a = 0, i = 0, u = 0, e = 0, o = 0, n = 0;

        if (info.phonemeRatios != null)
        {
            foreach (var kv in info.phonemeRatios)
            {
                var key = kv.Key.ToUpperInvariant();
                switch (key)
                {
                    case "A": a = kv.Value; break;
                    case "I": i = kv.Value; break;
                    case "U": u = kv.Value; break;
                    case "E": e = kv.Value; break;
                    case "O": o = kv.Value; break;
                    case "N": n = kv.Value; break;
                }
            }
        }

        // Smussatura indipendente dal frame-rate: in WebGL evita una bocca "scattosa" con FPS variabile.
        float dt = Mathf.Max(0f, Time.unscaledDeltaTime);
        float blend = 1f - Mathf.Exp(-smooth * dt);

        bool webGlAssistActive = enableWebGlAssist && Application.platform == RuntimePlatform.WebGLPlayer;
        float closeBlendMultiplier = webGlAssistActive ? Mathf.Clamp01(webGlCloseSpeedMultiplier) : 1f;

        _a = SmoothPhoneme(_a, a, blend, closeBlendMultiplier);
        _i = SmoothPhoneme(_i, i, blend, closeBlendMultiplier);
        _u = SmoothPhoneme(_u, u, blend, closeBlendMultiplier);
        _e = SmoothPhoneme(_e, e, blend, closeBlendMultiplier);
        _o = SmoothPhoneme(_o, o, blend, closeBlendMultiplier);
        _n = SmoothPhoneme(_n, n, blend, closeBlendMultiplier);

    }

    private void LateUpdate()
    {
        if (_indices == null || _indices.Count == 0) return;

        // rimuovi renderer distrutti
        List<SkinnedMeshRenderer> dead = null;

        foreach (var kv in _indices)
        {
            var r = kv.Key;
            if (!r)
            {
                dead ??= new List<SkinnedMeshRenderer>();
                dead.Add(r);
                continue;
            }

            var idx = kv.Value;
            bool webGlAssistActive = enableWebGlAssist && Application.platform == RuntimePlatform.WebGLPlayer;
            float volume = webGlAssistActive ? Mathf.Max(_volume, webGlVolumeFloor) : _volume;
            float gain = webGlAssistActive ? (vowelGain * webGlWeightBoost) : vowelGain;

            ZeroAll(r, idx);

            Apply(r, idx.a, _a * volume * gain);
            Apply(r, idx.i, _i * volume * gain);
            Apply(r, idx.u, _u * volume * gain);
            Apply(r, idx.e, _e * volume * gain);
            Apply(r, idx.o, _o * volume * gain);
            Apply(r, idx.n, _n * volume * gain);
        }

        if (dead != null)
            for (int i = 0; i < dead.Count; i++)
                _indices.Remove(dead[i]);
    }


    private void Apply(SkinnedMeshRenderer r, int index, float normalized01)
    {
        if (index < 0) return;
        r.SetBlendShapeWeight(index, Mathf.Clamp01(normalized01) * maxWeight);
    }

    private void ZeroAll(SkinnedMeshRenderer r, Indices idx)
    {
        if (idx.a >= 0) r.SetBlendShapeWeight(idx.a, 0);
        if (idx.i >= 0) r.SetBlendShapeWeight(idx.i, 0);
        if (idx.u >= 0) r.SetBlendShapeWeight(idx.u, 0);
        if (idx.e >= 0) r.SetBlendShapeWeight(idx.e, 0);
        if (idx.o >= 0) r.SetBlendShapeWeight(idx.o, 0);
        if (idx.n >= 0) r.SetBlendShapeWeight(idx.n, 0);
    }

    private static int FindBlendShapeIndex(SkinnedMeshRenderer r, string[] candidates)
    {
        if (!r || !r.sharedMesh) return -1;

        int count = r.sharedMesh.blendShapeCount;
        for (int c = 0; c < candidates.Length; c++)
        {
            var target = candidates[c];
            for (int i = 0; i < count; i++)
            {
                var name = r.sharedMesh.GetBlendShapeName(i);
                if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    private static float SmoothPhoneme(float current, float target, float blend, float closeBlendMultiplier)
    {
        float t = target >= current ? blend : (blend * closeBlendMultiplier);
        return Mathf.Lerp(current, target, Mathf.Clamp01(t));
    }

    public void ClearTargets()
    {
        // azzera pesi e svuota tutto
        foreach (var kv in _indices)
        {
            var r = kv.Key;
            if (!r) continue;
            var idx = kv.Value;
            ZeroAll(r, idx);
        }

        _indices.Clear();
        targetRenderers = Array.Empty<SkinnedMeshRenderer>();

        _a = _i = _u = _e = _o = _n = 0f;
        _volume = 0f;
    }

}
