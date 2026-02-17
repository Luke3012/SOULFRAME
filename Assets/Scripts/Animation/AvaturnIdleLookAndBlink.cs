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
    [Range(0f, 1f)] public float eyeFollowHeadExternal = 0.35f;
    public float eyeLerpSpeed = 14f;
    public float eyeSaccadeYawDeg = 2.0f;
    public float eyeSaccadePitchDeg = 1.2f;
    public float eyeSaccadeSpeed = 1.6f;

    [Header("Speaking Micro Motion")]
    public float speakingHeadYawDeg = 3f;
    public float speakingHeadPitchDeg = 2f;
    public float speakingHeadLerp = 10f;
    public float speakingHeadReturnLerp = 5f;
    public float speakingEyeSaccadeYawDeg = 1.2f;
    public float speakingEyeSaccadePitchDeg = 0.8f;
    public float speakingEyeSaccadeSpeed = 2.2f;

    [Header("Speaking detect (da uLipSync)")]
    public float speakingVolumeThreshold = 0.015f;
    public float speakingHoldSeconds = 0.6f;

    [Header("Main Mode Look")]
    public float externalLookSpeed = 10f;
    public float externalYawClampDeg = 50f;
    public float externalPitchClampDeg = 30f;
    public float listeningFocusSpeed = 12f;
    public bool logStateChanges = false;

    [Header("Listening Ear Lean")]
    [Tooltip("Yaw leggero: avvicina un orecchio alla camera durante l'ascolto.")]
    public float listeningEarYawDeg = 6f;
    [Tooltip("Roll leggero: effetto \"orecchio verso la camera\".")]
    public float listeningEarRollDeg = 5f;
    [Range(0f, 1f)] public float listeningPitchFollow = 0.2f;
    public float listeningPitchClampDeg = 3f;
    public float listeningEarDeadZoneDeg = 4f;
    public float listeningSwayDeg = 0.6f;
    public float listeningSwaySpeed = 1.2f;
    [Tooltip("Velocita' ingresso posa ascolto (evita scatto iniziale).")]
    public float listeningEnterBlendSpeed = 7f;
    [Tooltip("Velocita' uscita posa ascolto.")]
    public float listeningExitBlendSpeed = 10f;

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
    bool _mainModeEnabled;
    bool _isListening;
    Vector3? _externalLookTarget;
    bool _wasSpeaking;
    float _speechYawTarget;
    float _speechPitchTarget;
    float _speechYaw;
    float _speechPitch;
    float _listeningEarSide = 1f;
    float _listeningBlend;

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

        // Testa
        _head = allT.FirstOrDefault(t => t.name == "Head" || t.name.ToLower().Contains("head"));
        if (_head != null)
            _headBaseLocalRot = _head.localRotation;

        // Braccia (LeftArm/RightArm, fallback su LeftUpperArm/RightUpperArm)
        _leftArm = allT.FirstOrDefault(t => t.name == "LeftArm") ?? allT.FirstOrDefault(t => t.name == "LeftUpperArm");
        _rightArm = allT.FirstOrDefault(t => t.name == "RightArm") ?? allT.FirstOrDefault(t => t.name == "RightUpperArm");

        if (applyArmsRestPoseOnSetup)
            ApplyArmsRestPose();

        // Occhi
        if (enableEyes)
        {
            _eyeL = allT.FirstOrDefault(t => IsLeftEyeName(t.name));
            _eyeR = allT.FirstOrDefault(t => IsRightEyeName(t.name));

            if (_eyeL != null) _eyeLBaseLocalRot = _eyeL.localRotation;
            if (_eyeR != null) _eyeRBaseLocalRot = _eyeR.localRotation;
        }

        // Blendshape di ammiccamento
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

    public void SetMainModeEnabled(bool enabled)
    {
        if (_mainModeEnabled != enabled && logStateChanges)
        {
            Debug.Log($"[IdleLook] MainMode={(enabled ? "ON" : "OFF")}");
        }
        _mainModeEnabled = enabled;
    }

    public void SetExternalLookTarget(Vector3? worldTarget)
    {
        _externalLookTarget = worldTarget;
    }

    public void SetListening(bool listening)
    {
        if (_isListening != listening && logStateChanges)
        {
            Debug.Log($"[IdleLook] Listening={(listening ? "ON" : "OFF")}");
        }

        if (listening && !_isListening)
        {
            _listeningEarSide = DetermineListeningEarSide();
            // Se entriamo in ascolto, evitiamo che il ramo "speaking hold" prenda priorita' per qualche frame.
            _speakUntil = 0f;
        }

        _isListening = listening;
    }

    private float DetermineListeningEarSide()
    {
        // Evita "scatti" casuali: riusa il lato coerente con la posa corrente della testa.
        if (TryGetCurrentHeadLocalEuler(out _, out float currentYaw, out _))
        {
            if (Mathf.Abs(currentYaw) > 0.5f)
            {
                return Mathf.Sign(currentYaw);
            }
        }

        // Ripiego: lato deciso in base alla posizione camera.
        if (_head != null && _head.parent != null && focusTarget != null)
        {
            GetYawPitchToTarget(_head.parent, _head.position, focusTarget.position, out float camYaw, out _);
            if (Mathf.Abs(camYaw) > 0.5f)
            {
                return Mathf.Sign(camYaw);
            }
        }

        return _listeningEarSide == 0f ? 1f : Mathf.Sign(_listeningEarSide);
    }

    private bool TryGetCurrentHeadLocalEuler(out float pitchDeg, out float yawDeg, out float rollDeg)
    {
        pitchDeg = 0f;
        yawDeg = 0f;
        rollDeg = 0f;

        if (_head == null)
        {
            return false;
        }

        Quaternion delta = Quaternion.Inverse(_headBaseLocalRot) * _head.localRotation;
        pitchDeg = NormalizeSignedAngle(delta.eulerAngles.x);
        yawDeg = NormalizeSignedAngle(delta.eulerAngles.y);
        rollDeg = NormalizeSignedAngle(delta.eulerAngles.z);
        return true;
    }

    public bool TryGetHeadWorldPosition(out Vector3 worldPos)
    {
        if (_head != null)
        {
            worldPos = _head.position;
            return true;
        }

        worldPos = Vector3.zero;
        return false;
    }

    public bool TryGetHeadTransform(out Transform head)
    {
        head = _head;
        return _head != null;
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
        _mainModeEnabled = false;
        _isListening = false;
        _externalLookTarget = null;
        _listeningEarSide = 1f;
        _listeningBlend = 0f;
    }

    void OnDisable() => Clear();
    void OnDestroy() => Clear();

    public void OnLipSyncUpdate(LipSyncInfo info)
    {
        if (info.volume > speakingVolumeThreshold)
            _speakUntil = Time.time + speakingHoldSeconds;

        float a = 0f, i = 0f, u = 0f, e = 0f, o = 0f, n = 0f;
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

        float volumeWeight = Mathf.Clamp01(info.volume / Mathf.Max(0.0001f, speakingVolumeThreshold));
        float speechWeight = Mathf.Clamp01((a + i + u + e + o + n) * 0.5f) * volumeWeight;
        float lateral = Mathf.Clamp((i + e) - (o + u + a) * 0.5f, -1f, 1f);
        float vertical = Mathf.Clamp((a + o) - (i + e) * 0.5f, -1f, 1f);

        _speechYawTarget = lateral * speakingHeadYawDeg * speechWeight;
        _speechPitchTarget = vertical * speakingHeadPitchDeg * speechWeight;
    }

    void LateUpdate()
    {
        if (_head == null) return;

        bool speaking = Time.time < _speakUntil;
        if (speaking != _wasSpeaking && logStateChanges)
        {
            Debug.Log($"[IdleLook] Speaking={(speaking ? "ON" : "OFF")}");
        }
        _wasSpeaking = speaking;

        float listeningSpeed = _isListening ? listeningEnterBlendSpeed : listeningExitBlendSpeed;
        _listeningBlend = Mathf.MoveTowards(_listeningBlend, _isListening ? 1f : 0f, Time.deltaTime * Mathf.Max(0.01f, listeningSpeed));

        float headYaw, headPitch;
        float headRoll = 0f;

        if (speaking && focusTarget != null && _head.parent != null)
        {
            GetYawPitchToTarget(_head.parent, _head.position, focusTarget.position, out headYaw, out headPitch);
            headYaw = Mathf.Clamp(headYaw, -focusYawClampDeg, focusYawClampDeg);
            headPitch = Mathf.Clamp(headPitch, -focusPitchClampDeg, focusPitchClampDeg);

            _speechYaw = Mathf.Lerp(_speechYaw, _speechYawTarget, Time.deltaTime * speakingHeadLerp);
            _speechPitch = Mathf.Lerp(_speechPitch, _speechPitchTarget, Time.deltaTime * speakingHeadLerp);
            headYaw += _speechYaw;
            headPitch += _speechPitch;

            ApplyHeadRotation(headPitch, headYaw, headRoll, focusSpeed);
        }
        else if (_isListening && _head.parent != null)
        {
            Vector3 cameraTarget = focusTarget != null ? focusTarget.position : (_head.position + _head.forward);
            GetYawPitchToTarget(_head.parent, _head.position, cameraTarget, out float camYaw, out float camPitch);

            if (Mathf.Abs(camYaw) > listeningEarDeadZoneDeg)
            {
                _listeningEarSide = Mathf.Sign(camYaw);
            }

            float swayYaw = SignedPerlin(503f, listeningSwaySpeed, listeningSwayDeg);
            float swayRoll = SignedPerlin(607f, listeningSwaySpeed, listeningSwayDeg);

            float targetYaw = _listeningEarSide * listeningEarYawDeg + swayYaw;
            float targetPitch = Mathf.Clamp(camPitch * listeningPitchFollow, -listeningPitchClampDeg, listeningPitchClampDeg);
            float targetRoll = -_listeningEarSide * listeningEarRollDeg + swayRoll;

            // Transizione progressiva verso la posa di ascolto, cosi' evitiamo scatti netti da mouse-look/parlato.
            TryGetCurrentHeadLocalEuler(out float currentPitch, out float currentYaw, out float currentRoll);

            headYaw = Mathf.Lerp(currentYaw, targetYaw, _listeningBlend);
            headPitch = Mathf.Lerp(currentPitch, targetPitch, _listeningBlend);
            headRoll = Mathf.Lerp(currentRoll, targetRoll, _listeningBlend);

            ApplyHeadRotation(headPitch, headYaw, headRoll, listeningFocusSpeed);
        }
        else if (_mainModeEnabled && _externalLookTarget.HasValue && _head.parent != null)
        {
            GetYawPitchToTarget(_head.parent, _head.position, _externalLookTarget.Value, out headYaw, out headPitch);
            headYaw = Mathf.Clamp(headYaw, -externalYawClampDeg, externalYawClampDeg);
            headPitch = Mathf.Clamp(headPitch, -externalPitchClampDeg, externalPitchClampDeg);

            ApplyHeadRotation(headPitch, headYaw, headRoll, externalLookSpeed);
        }
        else
        {
            _speechYawTarget = Mathf.Lerp(_speechYawTarget, 0f, Time.deltaTime * speakingHeadReturnLerp);
            _speechPitchTarget = Mathf.Lerp(_speechPitchTarget, 0f, Time.deltaTime * speakingHeadReturnLerp);
            _speechYaw = Mathf.Lerp(_speechYaw, _speechYawTarget, Time.deltaTime * speakingHeadReturnLerp);
            _speechPitch = Mathf.Lerp(_speechPitch, _speechPitchTarget, Time.deltaTime * speakingHeadReturnLerp);

            headYaw = SignedPerlin(0f, idleSpeed, idleYawDeg);
            headPitch = SignedPerlin(17f, idleSpeed, idlePitchDeg);

            ApplyHeadRotation(headPitch, headYaw, headRoll, 2f);
        }

        if (!enableEyes) return;
        if (_eyeL == null && _eyeR == null) return;

        float eyeYaw, eyePitch;

        if (speaking && focusTarget != null && _head.parent != null)
        {
            GetYawPitchToTarget(_head.parent, _head.position, focusTarget.position, out eyeYaw, out eyePitch);
            eyeYaw *= eyeFocusMultiplier;
            eyePitch *= eyeFocusMultiplier;

            float sYaw = SignedPerlin(303f, speakingEyeSaccadeSpeed, speakingEyeSaccadeYawDeg);
            float sPitch = SignedPerlin(404f, speakingEyeSaccadeSpeed, speakingEyeSaccadePitchDeg);
            eyeYaw += sYaw;
            eyePitch += sPitch;

            eyeYaw = Mathf.Clamp(eyeYaw, -eyeYawClampDeg, eyeYawClampDeg);
            eyePitch = Mathf.Clamp(eyePitch, -eyePitchClampDeg, eyePitchClampDeg);
        }
        else if (_isListening)
        {
            float sYaw = SignedPerlin(701f, eyeSaccadeSpeed, eyeSaccadeYawDeg);
            float sPitch = SignedPerlin(809f, eyeSaccadeSpeed, eyeSaccadePitchDeg);

            eyeYaw = headYaw * eyeFollowHeadIdle + sYaw;
            eyePitch = headPitch * eyeFollowHeadIdle + sPitch;

            eyeYaw = Mathf.Clamp(eyeYaw, -eyeYawClampDeg, eyeYawClampDeg);
            eyePitch = Mathf.Clamp(eyePitch, -eyePitchClampDeg, eyePitchClampDeg);
        }
        else if (_mainModeEnabled && _externalLookTarget.HasValue && _head.parent != null)
        {
            GetYawPitchToTarget(_head.parent, _head.position, _externalLookTarget.Value, out eyeYaw, out eyePitch);

            eyeYaw = Mathf.Lerp(eyeYaw, headYaw, eyeFollowHeadExternal);
            eyePitch = Mathf.Lerp(eyePitch, headPitch, eyeFollowHeadExternal);

            eyeYaw = Mathf.Clamp(eyeYaw, -eyeYawClampDeg, eyeYawClampDeg);
            eyePitch = Mathf.Clamp(eyePitch, -eyePitchClampDeg, eyePitchClampDeg);
        }
        else
        {
            float sYaw = SignedPerlin(101f, eyeSaccadeSpeed, eyeSaccadeYawDeg);
            float sPitch = SignedPerlin(202f, eyeSaccadeSpeed, eyeSaccadePitchDeg);

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

    private float SignedPerlin(float seedOffset, float speed, float amplitude)
    {
        return (Mathf.PerlinNoise(_seed + seedOffset, Time.time * speed) - 0.5f) * 2f * amplitude;
    }

    private void ApplyHeadRotation(float pitch, float yaw, float roll, float speed)
    {
        var targetRot = _headBaseLocalRot * Quaternion.Euler(pitch, yaw, roll);
        _head.localRotation = Quaternion.Slerp(_head.localRotation, targetRot, Time.deltaTime * speed);
    }

    static float NormalizeSignedAngle(float angleDeg)
    {
        angleDeg %= 360f;
        if (angleDeg > 180f) angleDeg -= 360f;
        if (angleDeg < -180f) angleDeg += 360f;
        return angleDeg;
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
