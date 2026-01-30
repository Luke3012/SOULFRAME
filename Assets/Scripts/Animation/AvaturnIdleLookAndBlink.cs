using System.Collections;
using System.Linq;
using UnityEngine;
using uLipSync;

public class AvaturnIdleLookAndBlink : MonoBehaviour
{
    [Header("Optional: target da guardare quando parla")]
    public Transform focusTarget;

    [Header("Head Gaze")]
    public float idleYawDeg = 15f;
    public float idlePitchDeg = 6f;
    public float idleSpeed = 0.6f;
    public float focusSpeed = 8f;

    [Tooltip("Clamp massimo durante focus (parlato).")]
    public float focusYawClampDeg = 45f;
    public float focusPitchClampDeg = 25f;

    [Header("Eyes")]
    public bool enableEyes = true;
    public float eyeYawClampDeg = 10f;
    public float eyePitchClampDeg = 6f;
    public float eyeFocusMultiplier = 1.25f;
    [Range(0f, 1f)] public float eyeFollowHeadIdle = 0.75f;
    public float eyeLerpSpeed = 14f;
    public float eyeSaccadeYawDeg = 2.0f;
    public float eyeSaccadePitchDeg = 1.2f;
    public float eyeSaccadeSpeed = 1.6f;

    [Header("Speaking detect (da uLipSync)")]
    public float speakingVolumeThreshold = 0.015f;
    public float speakingHoldSeconds = 0.6f;

    [Header("Blink")]
    public Vector2 blinkInterval = new Vector2(2.0f, 5.0f);
    public float blinkCloseTime = 0.05f;
    public float blinkOpenTime = 0.08f;
    public float blinkWeight = 1f;

    [Header("Arms Rest Pose")]
    public bool applyArmsRestPoseOnSetup = true;
    [Tooltip("Imposta localRotation.x (in gradi) di LeftArm/RightArm (o fallback UpperArm)")]
    public float armsLocalRotX = 72f;

    Transform _avatarRoot;
    Transform _head;
    Quaternion _headBaseLocalRot;

    Transform _eyeL, _eyeR;
    Quaternion _eyeLBaseLocalRot, _eyeRBaseLocalRot;

    Transform _leftArm, _rightArm;

    struct BlinkTarget { public SkinnedMeshRenderer r; public int l; public int rr; }
    BlinkTarget[] _blinkTargets = new BlinkTarget[0];

    float _speakUntil;
    float _seed;
    Coroutine _blinkCo;

    static readonly string[] BlinkLeftNames = { "eyeBlinkLeft", "EyeBlinkLeft", "blinkLeft", "BlinkLeft" };
    static readonly string[] BlinkRightNames = { "eyeBlinkRight", "EyeBlinkRight", "blinkRight", "BlinkRight" };

    public void Setup(Transform avatarRoot)
    {
        Clear();

        _avatarRoot = avatarRoot;
        if (_avatarRoot == null) return;

        if (focusTarget == null && Camera.main != null)
            focusTarget = Camera.main.transform;

        var allT = _avatarRoot.GetComponentsInChildren<Transform>(true);

        // Head
        _head = allT.FirstOrDefault(t => t.name == "Head" || t.name.ToLower().Contains("head"));
        if (_head != null)
            _headBaseLocalRot = _head.localRotation;

        // Arms (LeftArm/RightArm, fallback a LeftUpperArm/RightUpperArm)
        _leftArm = allT.FirstOrDefault(t => t.name == "LeftArm") ?? allT.FirstOrDefault(t => t.name == "LeftUpperArm");
        _rightArm = allT.FirstOrDefault(t => t.name == "RightArm") ?? allT.FirstOrDefault(t => t.name == "RightUpperArm");

        if (applyArmsRestPoseOnSetup)
            ApplyArmsRestPose();

        // Eyes
        if (enableEyes)
        {
            _eyeL = allT.FirstOrDefault(t => IsLeftEyeName(t.name));
            _eyeR = allT.FirstOrDefault(t => IsRightEyeName(t.name));

            if (_eyeL != null) _eyeLBaseLocalRot = _eyeL.localRotation;
            if (_eyeR != null) _eyeRBaseLocalRot = _eyeR.localRotation;
        }

        // Blink blendshapes
        var rends = _avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => r && r.sharedMesh && r.sharedMesh.blendShapeCount > 0);

        _blinkTargets = rends.Select(r =>
        {
            int l = FindBlendshape(r, BlinkLeftNames);
            int rr = FindBlendshape(r, BlinkRightNames);
            return new BlinkTarget { r = r, l = l, rr = rr };
        })
        .Where(t => t.l >= 0 || t.rr >= 0)
        .ToArray();

        _seed = Random.value * 999f;

        _blinkCo = StartCoroutine(BlinkLoop());

        Debug.Log($"[IdleLook] Setup OK. head={(_head ? _head.name : "NULL")} arms=({_leftArm?.name ?? "NULL"},{_rightArm?.name ?? "NULL"}) eyes=({_eyeL?.name ?? "NULL"},{_eyeR?.name ?? "NULL"}) blinkTargets={_blinkTargets.Length}");
    }

    void ApplyArmsRestPose()
    {
        if (_leftArm != null)
        {
            var e = _leftArm.localEulerAngles;
            _leftArm.localEulerAngles = new Vector3(armsLocalRotX, e.y, e.z);
        }

        if (_rightArm != null)
        {
            var e = _rightArm.localEulerAngles;
            _rightArm.localEulerAngles = new Vector3(armsLocalRotX, e.y, e.z);
        }
    }

    public void Clear()
    {
        if (_blinkCo != null) StopCoroutine(_blinkCo);
        _blinkCo = null;

        _blinkTargets = new BlinkTarget[0];
        _avatarRoot = null;
        _head = null;
        _eyeL = null;
        _eyeR = null;
        _leftArm = null;
        _rightArm = null;
        _speakUntil = 0;
    }

    void OnDisable() => Clear();
    void OnDestroy() => Clear();

    public void OnLipSyncUpdate(LipSyncInfo info)
    {
        if (info.volume > speakingVolumeThreshold)
            _speakUntil = Time.time + speakingHoldSeconds;
    }

    void LateUpdate()
    {
        if (_head == null) return;

        bool speaking = Time.time < _speakUntil;

        float headYaw, headPitch;

        if (speaking && focusTarget != null && _head.parent != null)
        {
            GetYawPitchToTarget(_head.parent, _head.position, focusTarget.position, out headYaw, out headPitch);
            headYaw = Mathf.Clamp(headYaw, -focusYawClampDeg, focusYawClampDeg);
            headPitch = Mathf.Clamp(headPitch, -focusPitchClampDeg, focusPitchClampDeg);

            var targetRot = _headBaseLocalRot * Quaternion.Euler(headPitch, headYaw, 0f);
            _head.localRotation = Quaternion.Slerp(_head.localRotation, targetRot, Time.deltaTime * focusSpeed);
        }
        else
        {
            headYaw = (Mathf.PerlinNoise(_seed, Time.time * idleSpeed) - 0.5f) * 2f * idleYawDeg;
            headPitch = (Mathf.PerlinNoise(_seed + 17f, Time.time * idleSpeed) - 0.5f) * 2f * idlePitchDeg;

            var idleRot = _headBaseLocalRot * Quaternion.Euler(headPitch, headYaw, 0f);
            _head.localRotation = Quaternion.Slerp(_head.localRotation, idleRot, Time.deltaTime * 2f);
        }

        if (!enableEyes) return;
        if (_eyeL == null && _eyeR == null) return;

        float eyeYaw, eyePitch;

        if (speaking && focusTarget != null && _head.parent != null)
        {
            GetYawPitchToTarget(_head.parent, _head.position, focusTarget.position, out eyeYaw, out eyePitch);
            eyeYaw *= eyeFocusMultiplier;
            eyePitch *= eyeFocusMultiplier;

            eyeYaw = Mathf.Clamp(eyeYaw, -eyeYawClampDeg, eyeYawClampDeg);
            eyePitch = Mathf.Clamp(eyePitch, -eyePitchClampDeg, eyePitchClampDeg);
        }
        else
        {
            float sYaw = (Mathf.PerlinNoise(_seed + 101f, Time.time * eyeSaccadeSpeed) - 0.5f) * 2f * eyeSaccadeYawDeg;
            float sPitch = (Mathf.PerlinNoise(_seed + 202f, Time.time * eyeSaccadeSpeed) - 0.5f) * 2f * eyeSaccadePitchDeg;

            eyeYaw = headYaw * eyeFollowHeadIdle + sYaw;
            eyePitch = headPitch * eyeFollowHeadIdle + sPitch;

            eyeYaw = Mathf.Clamp(eyeYaw, -eyeYawClampDeg, eyeYawClampDeg);
            eyePitch = Mathf.Clamp(eyePitch, -eyePitchClampDeg, eyePitchClampDeg);
        }

        if (_eyeL != null)
        {
            var target = _eyeLBaseLocalRot * Quaternion.Euler(eyePitch, eyeYaw, 0f);
            _eyeL.localRotation = Quaternion.Slerp(_eyeL.localRotation, target, Time.deltaTime * eyeLerpSpeed);
        }

        if (_eyeR != null)
        {
            var target = _eyeRBaseLocalRot * Quaternion.Euler(eyePitch, eyeYaw, 0f);
            _eyeR.localRotation = Quaternion.Slerp(_eyeR.localRotation, target, Time.deltaTime * eyeLerpSpeed);
        }
    }

    static void GetYawPitchToTarget(Transform referenceSpace, Vector3 fromWorld, Vector3 toWorld, out float yawDeg, out float pitchDeg)
    {
        var dirWorld = (toWorld - fromWorld);
        if (dirWorld.sqrMagnitude < 0.000001f) { yawDeg = 0; pitchDeg = 0; return; }

        var dirLocal = Quaternion.Inverse(referenceSpace.rotation) * dirWorld.normalized;
        yawDeg = Mathf.Atan2(dirLocal.x, dirLocal.z) * Mathf.Rad2Deg;
        pitchDeg = -Mathf.Asin(Mathf.Clamp(dirLocal.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    IEnumerator BlinkLoop()
    {
        while (true)
        {
            float wait = Random.Range(blinkInterval.x, blinkInterval.y);
            yield return new WaitForSeconds(wait);
            yield return BlinkOnce();
        }
    }

    IEnumerator BlinkOnce()
    {
        for (float t = 0; t < blinkCloseTime; t += Time.deltaTime)
        {
            SetBlink(Mathf.Lerp(0, blinkWeight, t / blinkCloseTime));
            yield return null;
        }
        SetBlink(blinkWeight);

        for (float t = 0; t < blinkOpenTime; t += Time.deltaTime)
        {
            SetBlink(Mathf.Lerp(blinkWeight, 0, t / blinkOpenTime));
            yield return null;
        }
        SetBlink(0);
    }

    void SetBlink(float w)
    {
        for (int i = 0; i < _blinkTargets.Length; i++)
        {
            var t = _blinkTargets[i];
            if (t.r == null || t.r.sharedMesh == null) continue;
            if (t.l >= 0) t.r.SetBlendShapeWeight(t.l, w);
            if (t.rr >= 0) t.r.SetBlendShapeWeight(t.rr, w);
        }
    }

    static int FindBlendshape(SkinnedMeshRenderer r, string[] names)
    {
        var m = r.sharedMesh;
        for (int i = 0; i < names.Length; i++)
        {
            int idx = m.GetBlendShapeIndex(names[i]);
            if (idx >= 0) return idx;
        }
        return -1;
    }

    static bool IsLeftEyeName(string n)
    {
        var s = n.ToLowerInvariant();
        if (!s.Contains("eye")) return false;
        if (s.Contains("lid")) return false;
        return s.Contains("left") || s.EndsWith("_l") || s.Contains("eye_l") || s.Contains("l_eye") || s == "lefteye";
    }

    static bool IsRightEyeName(string n)
    {
        var s = n.ToLowerInvariant();
        if (!s.Contains("eye")) return false;
        if (s.Contains("lid")) return false;
        return s.Contains("right") || s.EndsWith("_r") || s.Contains("eye_r") || s.Contains("r_eye") || s == "righteye";
    }
}
