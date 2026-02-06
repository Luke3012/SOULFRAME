using UnityEngine;

[DisallowMultipleComponent]
public class PS2BackgroundRings : MonoBehaviour
{
    [Header("Ring Material")]
    [SerializeField] private Material ringMaterial;     // PS2_RingAdditive
    [SerializeField] private Texture2D ringTexture;     // opzionale

    [Header("Particle Color / Glow")]
    [SerializeField, ColorUsage(true, true)]
    private Color ringTint = new Color(0.4f, 0.75f, 1f, 0.9f);
    [SerializeField] private float particleHDRIntensity = 5.0f; // aumenta per più bloom
    [SerializeField] private bool overrideMaterialColor = false; // se TRUE, sovrascrive _Color del material

    [Header("Renderer Sorting (utile se in UI)")]
    [SerializeField] private string sortingLayerName = "UI";
    [SerializeField] private int sortingOrder = 100;

    [Header("Orbs (unità del RectTransform / UI)")]
    [SerializeField, Range(3, 12)] private int orbCount = 7;
    [SerializeField] private float orbSize = 24f;                 // “pixel-ish”
    [SerializeField] private float orbitRadius = 140f;            // “pixel-ish”
    [SerializeField] private float radiusJitter = 10f;
    [SerializeField] private float spacingJitterRadians = 0.05f;  // piccola irregolarità nella spaziatura

    [Header("Orbit (PS2 style)")]
    [SerializeField] private float baseOrbitSpeed = 1.15f;        // rad/sec
    [SerializeField] private float microJitter = 1.8f;            // sempre presente (piccolo)
    [SerializeField] private float microJitterFrequency = 0.45f;

    [Header("Occasional Mix (si mischiano ogni tanto)")]
    [SerializeField] private Vector2 mixIntervalRange = new Vector2(3.5f, 8.5f);
    [SerializeField] private float mixDuration = 1.35f;
    [SerializeField] private float mixPhaseStrength = 0.65f;      // rad: quanto “sorpassano”
    [SerializeField] private float mixChaosBoost = 18f;           // “pixel-ish” extra jitter SOLO durante il mix
    [SerializeField, Range(1, 12)] private int mixAffectCount = 3; // quante particelle coinvolge ogni mix

    [Header("Trail (unità del RectTransform / UI)")]
    private float baseOrbitSpeedDefault;

    public void SetOrbitSpeedMultiplier(float multiplier)
    {
        baseOrbitSpeed = baseOrbitSpeedDefault * Mathf.Max(0f, multiplier);
    }

    [SerializeField] private float trailSpacing = 34f;         // “pixel-ish”
    [SerializeField] private float trailInertia = 10f;         // smoothing verso target
    [SerializeField] private float movementThreshold = 6f;     // “pixel-ish” al secondo
    [SerializeField] private float breakToTrailSpeed = 14f;    // quanto “subito” rompi in scia
    [SerializeField] private float returnToOrbitSpeed = 3f;    // quanto “morbido” ri-agganci
    [SerializeField] private bool useUnscaledTime = true;

    private ParticleSystem ps;
    private ParticleSystemRenderer psr;

    private ParticleSystem.Particle[] particles;

    private Texture2D generatedTexture;
    private Material runtimeMaterial;

    private RectTransform rectTransform;

    // per-orb data
    private float[] radii;
    private float[] spacingJitter;
    private Vector2[] noiseSeeds;
    private Vector3[] simPositions;

    // orbit core
    private float baseAngle = 0f;

    // mixing state
    private bool mixing = false;
    private float mixStartTime = 0f;
    private float nextMixTime = 0f;
    private float[] mixTargets; // rad offset per orb durante il mix

    // movement tracking
    private Vector3 prevCenterWorld;
    private Vector3 lastMoveDir = Vector3.right;
    private float chaseWeight = 0f;

    private int lastOrbCount = -1;

    private void Awake()
    {
        // Initialize base orbit speed for multiplier calculations
        baseOrbitSpeedDefault = baseOrbitSpeed;
        
        rectTransform = GetComponent<RectTransform>();

        EnsureParticleSystem();
        RebuildIfNeeded(force: true);

        prevCenterWorld = GetCenterWorld();
        ScheduleNextMix(Now());
    }

    private void OnEnable()
    {
        if (ps != null) ps.Play(true);
        prevCenterWorld = GetCenterWorld();
        ScheduleNextMix(Now());
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
        if (generatedTexture != null) Destroy(generatedTexture);
    }

    private void LateUpdate()
    {
        FaceCameraIfNeeded();

        RebuildIfNeeded(force: false);

        if (ps == null || particles == null) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;

        float now = Now();

        Vector3 center = GetCenterWorld();

        // local->world scale (così orbitRadius/orbSize sono in “unità UI”)
        float unitToWorld = GetUnitToWorld();
        float thresholdWorld = movementThreshold * unitToWorld;

        Vector3 vel = (center - prevCenterWorld) / dt;
        float speed = vel.magnitude;
        bool moving = speed > thresholdWorld;

        if (moving && speed > 0.0001f)
            lastMoveDir = vel.normalized;

        prevCenterWorld = center;

        float chaseRate = moving ? breakToTrailSpeed : returnToOrbitSpeed;
        chaseWeight = Mathf.MoveTowards(chaseWeight, moving ? 1f : 0f, dt * chaseRate);

        // mixing: aggiorna stato
        UpdateMixState(now);

        // assicura numero particelle
        EnsureExactParticleCount();

        // orbit: una baseAngle comune, così seguono la stessa traiettoria
        baseAngle += baseOrbitSpeed * dt;

        float sizeWorld = orbSize * unitToWorld;
        float spacingWorld = trailSpacing * unitToWorld;

        Vector3 trailDir = -lastMoveDir;

        float spacing = Mathf.PI * 2f / Mathf.Max(orbCount, 1);

        float mixPulse = 0f;
        if (mixing)
        {
            float u = Mathf.Clamp01((now - mixStartTime) / Mathf.Max(0.0001f, mixDuration));
            mixPulse = Mathf.Sin(Mathf.PI * u); // 0->1->0
        }

        float chaosPixels = microJitter + mixChaosBoost * mixPulse;

        // colore particella (HDR per far lavorare il Bloom)
        // Se ha già PS2_RingAdditive con tinta blu, di default NON raddoppia la tinta:
        Color baseColor = (ringMaterial != null && !overrideMaterialColor) ? Color.white : ringTint;
        Color particleColor = new Color(
            baseColor.r * particleHDRIntensity,
            baseColor.g * particleHDRIntensity,
            baseColor.b * particleHDRIntensity,
            ringTint.a
        );

        // aggiorna e imposta posizioni
        for (int i = 0; i < orbCount; i++)
        {
            float phase = i * spacing + spacingJitter[i];

            // durante il mix, alcuni orbi ricevono un offset di fase (sorpassi / incroci)
            phase += mixTargets[i] * mixPulse;

            float ang = baseAngle + phase;

            Vector3 orbitTarget = center + GetOrbitOffsetWorld(i, ang, chaosPixels);
            Vector3 trailTarget = ((i == 0) ? center : simPositions[i - 1]) + trailDir * spacingWorld;

            Vector3 target = Vector3.Lerp(orbitTarget, trailTarget, chaseWeight);

            // smoothing esponenziale (stabile e “inerziale”)
            float k = 1f - Mathf.Exp(-trailInertia * dt);
            simPositions[i] = Vector3.Lerp(simPositions[i], target, k);

            particles[i].position = simPositions[i];
            particles[i].startSize = sizeWorld;
            particles[i].startColor = particleColor;

            // vita infinita “pratica”
            particles[i].remainingLifetime = 999999f;
            particles[i].startLifetime = 999999f;
        }

        ps.SetParticles(particles, orbCount);
    }

    private void FaceCameraIfNeeded()
    {
        if (rectTransform != null)
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 toCamera = cam.transform.position - transform.position;
        if (toCamera.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(toCamera.normalized, cam.transform.up);
    }

    private float Now() => useUnscaledTime ? Time.unscaledTime : Time.time;

    private void UpdateMixState(float now)
    {
        if (!mixing && now >= nextMixTime)
        {
            StartMix(now);
            return;
        }

        if (mixing && now >= mixStartTime + mixDuration)
        {
            mixing = false;
            // reset target (non necessario, ma pulito)
            for (int i = 0; i < mixTargets.Length; i++) mixTargets[i] = 0f;
            ScheduleNextMix(now);
        }
    }

    private void StartMix(float now)
    {
        mixing = true;
        mixStartTime = now;

        // azzera
        for (int i = 0; i < mixTargets.Length; i++)
            mixTargets[i] = 0f;

        // sceglie un sottoinsieme di particelle da “mischiare”
        int count = Mathf.Clamp(mixAffectCount, 1, orbCount);
        for (int k = 0; k < count; k++)
        {
            int idx = Random.Range(0, orbCount);
            mixTargets[idx] = Random.Range(-mixPhaseStrength, mixPhaseStrength);
        }
    }

    private void ScheduleNextMix(float now)
    {
        float inSec = Random.Range(mixIntervalRange.x, mixIntervalRange.y);
        nextMixTime = now + Mathf.Max(0.2f, inSec);
    }

    private void RebuildIfNeeded(bool force)
    {
        if (!force && lastOrbCount == orbCount && particles != null && particles.Length == orbCount) return;

        lastOrbCount = orbCount;

        particles = new ParticleSystem.Particle[orbCount];
        radii = new float[orbCount];
        spacingJitter = new float[orbCount];
        noiseSeeds = new Vector2[orbCount];
        simPositions = new Vector3[orbCount];
        mixTargets = new float[orbCount];

        if (ps != null)
        {
            var main = ps.main;
            main.maxParticles = orbCount;
        }

        ForceRebuildParticles();
        ScheduleNextMix(Now());
    }

    private void ForceRebuildParticles()
    {
        if (ps == null) return;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Clear(true);

        Vector3 center = GetCenterWorld();
        float unitToWorld = GetUnitToWorld();

        float spacing = Mathf.PI * 2f / Mathf.Max(orbCount, 1);

        for (int i = 0; i < orbCount; i++)
        {
            radii[i] = orbitRadius + Random.Range(-radiusJitter, radiusJitter);
            spacingJitter[i] = Random.Range(-spacingJitterRadians, spacingJitterRadians);
            noiseSeeds[i] = new Vector2(Random.Range(1f, 999f), Random.Range(1f, 999f));
            mixTargets[i] = 0f;

            float ang = baseAngle + (i * spacing) + spacingJitter[i];

            Vector3 initial = center + GetOrbitOffsetWorld(i, ang, microJitter);
            simPositions[i] = initial;

            particles[i].position = initial;
            particles[i].startSize = orbSize * unitToWorld;
            particles[i].startColor = Color.white;
            particles[i].remainingLifetime = 999999f;
            particles[i].startLifetime = 999999f;
        }

        ps.Play(true);
        ps.SetParticles(particles, orbCount);
    }

    private void EnsureExactParticleCount()
    {
        if (ps == null) return;

        int count = ps.GetParticles(particles);
        if (count == orbCount) return;

        if (count < orbCount)
        {
            ForceRebuildParticles();
        }
        else
        {
            for (int i = orbCount; i < count && i < particles.Length; i++)
                particles[i].remainingLifetime = 0.001f;

            ps.SetParticles(particles, count);
        }
    }

    private Vector3 GetOrbitOffsetWorld(int i, float angle, float chaosPixels)
    {
        float t = Now() * microJitterFrequency;

        float nx = Mathf.PerlinNoise(noiseSeeds[i].x, t) - 0.5f;
        float ny = Mathf.PerlinNoise(noiseSeeds[i].y, t + 10f) - 0.5f;

        Vector2 chaotic = new Vector2(nx, ny) * chaosPixels;

        Vector2 local = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radii[i] + chaotic;

        if (rectTransform != null)
            return rectTransform.TransformVector(new Vector3(local.x, local.y, 0f));

        return transform.TransformVector(new Vector3(local.x, local.y, 0f));
    }

    private Vector3 GetCenterWorld()
    {
        if (rectTransform != null)
            return rectTransform.TransformPoint(rectTransform.rect.center);

        return transform.position;
    }

    private float GetUnitToWorld()
    {
        if (rectTransform == null) return 1f;

        Vector3 w = rectTransform.TransformVector(Vector3.right);
        float m = w.magnitude;
        return (m <= 0.000001f) ? 1f : m;
    }

    private void EnsureParticleSystem()
    {
        if (ps != null) return;

        ps = GetComponentInChildren<ParticleSystem>(true);

        GameObject psGo;
        if (ps == null)
        {
            psGo = new GameObject("PS2OrbitingLights");
            psGo.transform.SetParent(transform, false);
            psGo.transform.localPosition = Vector3.zero;
            ps = psGo.AddComponent<ParticleSystem>();
        }
        else
        {
            psGo = ps.gameObject;
            psGo.transform.localPosition = Vector3.zero;
        }

        psr = psGo.GetComponent<ParticleSystemRenderer>();
        if (psr == null) psr = psGo.AddComponent<ParticleSystemRenderer>();

        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sortingOrder = sortingOrder;
        if (!string.IsNullOrEmpty(sortingLayerName))
            psr.sortingLayerName = sortingLayerName;

        runtimeMaterial = BuildRuntimeMaterial();
        if (runtimeMaterial != null)
        {
            if (overrideMaterialColor)
                runtimeMaterial.color = ringTint;

            psr.sharedMaterial = runtimeMaterial;
        }

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.maxParticles = orbCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = 999999f;
        main.startSize = 1f;
        main.startColor = Color.white;

        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var vel = ps.velocityOverLifetime; vel.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = false;
        var sol = ps.sizeOverLifetime; sol.enabled = false;
        var rol = ps.rotationOverLifetime; rol.enabled = false;
        var nol = ps.noise; nol.enabled = false;
        var trails = ps.trails; trails.enabled = false;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Clear(true);
        ps.Play(true);
    }

    private Material BuildRuntimeMaterial()
    {
        if (ringMaterial != null)
        {
            var mat = new Material(ringMaterial);
            // se vuoi forzare la texture da qui, puoi farlo:
            if (ringTexture != null) mat.mainTexture = ringTexture;
            return mat;
        }

        Shader shader =
            Shader.Find("SOULFRAME/PS2AdditiveParticle") ??
            Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
            Shader.Find("Particles/Unlit") ??
            Shader.Find("Legacy Shaders/Particles/Additive");

        if (shader == null)
        {
            Debug.LogError("PS2BackgroundRings: nessuno shader particellare trovato.", this);
            return null;
        }

        var fallback = new Material(shader);

        if (ringTexture != null)
            fallback.mainTexture = ringTexture;
        else
            generatedTexture = CreateSphereTexture(128, Color.white);

        if (generatedTexture != null && fallback.mainTexture == null)
            fallback.mainTexture = generatedTexture;

        fallback.color = Color.white;

        return fallback;
    }

    private Texture2D CreateSphereTexture(int size, Color sphereColor)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        float center = (size - 1) * 0.5f;
        float radius = size * 0.45f;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float a = 1f - Mathf.Clamp01(dist / radius);
                a = Mathf.Pow(a, 0.6f);

                texture.SetPixel(x, y, new Color(sphereColor.r, sphereColor.g, sphereColor.b, a));
            }

        texture.Apply();
        return texture;
    }
}
