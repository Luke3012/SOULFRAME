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

    public string[] jawOpenFallbackNames = { "jawOpen", "JawOpen", "MouthOpen", "mouthOpen" };

    [Header("Tuning")]
    [Range(0f, 200f)] public float maxWeight = 1f;
    [Range(0f, 4f)] public float vowelGain = 1.2f;
    [Range(0f, 2f)] public float jawFallbackGain = 1.0f;
    [Range(0f, 20f)] public float smooth = 12f;

    [Header("Debug")]
    public bool logSetupSummary = true;
    public bool logBlendshapeNames = false;

    private Dictionary<SkinnedMeshRenderer, Indices> _indices = new Dictionary<SkinnedMeshRenderer, Indices>();

    private float _a, _i, _u, _e, _o, _n, _jaw;
    private float _volume;

    [Serializable]
    private struct Indices
    {
        public int a, i, u, e, o, n, jaw;
        public bool HasAny => a >= 0 || i >= 0 || u >= 0 || e >= 0 || o >= 0 || n >= 0 || jaw >= 0;
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
                jaw = FindBlendShapeIndex(r, jawOpenFallbackNames),
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

        _a = Mathf.Lerp(_a, a, Time.deltaTime * smooth);
        _i = Mathf.Lerp(_i, i, Time.deltaTime * smooth);
        _u = Mathf.Lerp(_u, u, Time.deltaTime * smooth);
        _e = Mathf.Lerp(_e, e, Time.deltaTime * smooth);
        _o = Mathf.Lerp(_o, o, Time.deltaTime * smooth);
        _n = Mathf.Lerp(_n, n, Time.deltaTime * smooth);

        var speechAmount = Mathf.Clamp01((_a + _i + _u + _e + _o + _n) * 0.5f);
        _jaw = Mathf.Lerp(_jaw, speechAmount, Time.deltaTime * smooth);
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

            ZeroAll(r, idx);

            Apply(r, idx.a, _a * _volume * vowelGain);
            Apply(r, idx.i, _i * _volume * vowelGain);
            Apply(r, idx.u, _u * _volume * vowelGain);
            Apply(r, idx.e, _e * _volume * vowelGain);
            Apply(r, idx.o, _o * _volume * vowelGain);
            Apply(r, idx.n, _n * _volume * vowelGain);

            bool hasVisemes = idx.a >= 0 || idx.i >= 0 || idx.u >= 0 || idx.e >= 0 || idx.o >= 0 || idx.n >= 0;
            if (!hasVisemes)
                Apply(r, idx.jaw, _jaw * _volume * jawFallbackGain);
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
        if (idx.jaw >= 0) r.SetBlendShapeWeight(idx.jaw, 0);
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

        _a = _i = _u = _e = _o = _n = _jaw = 0f;
        _volume = 0f;
    }


    private readonly List<SkinnedMeshRenderer> _toRemove = new List<SkinnedMeshRenderer>(16);

}
