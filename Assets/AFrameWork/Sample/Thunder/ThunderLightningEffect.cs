using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if HAS_UNITY_URP
using UnityEngine.Rendering.Universal;
#endif

[ExecuteAlways]
public class ThunderLightningEffect : MonoBehaviour
{
    #region ━━━ 空中闪电 ━━━

    [Header("空中闪电 — 主柱")]
    [SerializeField] private float strikeHeight = 30f;
    [SerializeField] private float groundY = 0f;
    [SerializeField] private int mainBoltSegments = 40;
    [SerializeField] private float jitterAmount = 3.5f;
    [SerializeField] private float boltCoreWidth = 0.35f;
    [SerializeField] private float boltGlowWidth = 2.5f;

    [Header("空中闪电 — 分支")]
    [SerializeField] private int branchCount = 15;
    [SerializeField] private int branchSegments = 16;
    [SerializeField] private float branchJitter = 2f;
    [SerializeField] private float branchLength = 12f;
    [SerializeField] private float branchCoreWidth = 0.15f;
    [SerializeField] private float branchGlowWidth = 1f;

    [Header("空中闪电 — 颜色 (HDR)")]
    [SerializeField] private Color coreColor = new Color(1.2f, 1.3f, 1.5f, 1f);
    [SerializeField] private Color glowColor = new Color(0.5f, 0.8f, 1.5f, 0.4f);

    [Header("空中闪电 — HDR 强度")]
    [SerializeField] private float coreIntensity = 3f;
    [SerializeField] private float glowIntensity = 6f;

    #endregion

    #region ━━━ 地面闪电 ━━━

    [Header("地面闪电 — 形状")]
    [SerializeField] private int crackCount = 15;
    [SerializeField] private int crackSegments = 18;
    [SerializeField] private float crackMaxRadius = 0.3f;
    [SerializeField] private float crackJitter = 0.8f;
    [SerializeField] private float crackCoreWidth = 0.15f;
    [SerializeField] private float crackGlowWidth = 0.6f;
    [SerializeField] private int crackWidthKeys = 12;

    [Header("地面闪电 — 颜色 (HDR)")]
    [SerializeField] private Color crackColor = new Color(1.0f, 1.1f, 1.3f, 0.9f);
    [SerializeField] private Color crackGlowColor = new Color(0.4f, 0.6f, 1.5f, 0.35f);

    [Header("地面闪电 — HDR 强度")]
    [SerializeField] private float crackIntensity = 3f;
    [SerializeField] private float crackGlowIntensity = 6f;

    #endregion

    #region ━━━ Bloom 后处理 ━━━

    [Header("Bloom 后处理")]
    [SerializeField] private bool enableBloom = true;
    [SerializeField] private float bloomIntensity = 4f;
    [SerializeField] private float bloomThreshold = 0.4f;
    [SerializeField] private float bloomScatter = 0.7f;

    #endregion

    #region ━━━ 时序与闪光灯 ━━━

    [Header("时序")]
    [SerializeField] private bool autoRepeat = true;
    [SerializeField] private float repeatInterval = 3f;
    [SerializeField] private float boltVisibleDuration = 0.5f;
    [SerializeField] private float crackExpandDuration = 0.4f;
    [SerializeField] private float crackFadeDuration = 1f;
    [SerializeField] private float flickerInterval = 0.04f;

    [Header("闪光灯")]
    [SerializeField] private float flashIntensity = 15f;
    [SerializeField] private float flashRange = 60f;
    [SerializeField] private Color flashColor = new Color(0.6f, 0.8f, 1f, 1f);

    #endregion

    // Runtime
    private LineRenderer mainBoltCore, mainBoltGlow;
    private List<LineRenderer> branchCores = new List<LineRenderer>();
    private List<LineRenderer> branchGlows = new List<LineRenderer>();
    private List<LineRenderer> crackCores = new List<LineRenderer>();
    private List<LineRenderer> crackGlows = new List<LineRenderer>();
    private Light flashLight;
    private ParticleSystem electricArcs, sparks, impactBurst, groundGlow;
    private Material matBolt, matGlow, matCrack, matCrackGlow, matParticle;
    private Texture2D circleTex;
    private GameObject bloomVolumeGo;
    private Volume bloomVolume;
    private VolumeProfile bloomProfile;

    private List<Vector3> mainBoltPath = new List<Vector3>();
    private List<List<Vector3>> branchPaths = new List<List<Vector3>>();
    private List<List<Vector3>> crackPaths = new List<List<Vector3>>();

    private enum EffectState { Idle, Striking, CrackExpand, CrackFade }
    private EffectState state = EffectState.Idle;
    private float stateTimer, idleTimer, flickerTimer;
    private float lastRealTime;
    private bool initialized;

    #region Lifecycle

    private void OnEnable()
    {
        lastRealTime = Time.realtimeSinceStartup;
        Initialize();
    }

    private void OnValidate()
    {
        if (!initialized) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            Initialize();
        };
#endif
    }

    private void OnDisable()
    {
        state = EffectState.Idle;
        SetBoltsVisible(false);
        SetCracksVisible(false, 0f);
        if (flashLight != null) flashLight.intensity = 0f;
    }

    private void OnDestroy()
    {
        DestroyMat(matBolt); DestroyMat(matGlow); DestroyMat(matCrack);
        DestroyMat(matCrackGlow); DestroyMat(matParticle);
        if (circleTex != null)
        {
            if (Application.isPlaying) Destroy(circleTex);
            else DestroyImmediate(circleTex);
        }
        DestroyBloomVolume();
    }

    private void DestroyMat(Material m)
    {
        if (m == null) return;
        if (Application.isPlaying) Destroy(m);
        else DestroyImmediate(m);
    }

    #endregion

    #region Initialization

    private void Initialize()
    {
        CreateCircleTexture();
        CreateMaterials();
        CreateMainBolt();
        CreateBranches();
        CreateParticleSystems();
        CreateFlashLight();
        CreateGroundCracks();
        CreateBloomVolume();
        GenerateLightning();
        GenerateCracks();
        SetBoltsVisible(false);
        SetCracksVisible(false, 0f);
        initialized = true;
    }

    private void CreateCircleTexture()
    {
        if (circleTex != null) return;
        int s = 128;
        circleTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        circleTex.hideFlags = HideFlags.HideAndDontSave;
        float c = s / 2f, r = s / 2f;
        for (int x = 0; x < s; x++)
            for (int y = 0; y < s; y++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = Mathf.Pow(Mathf.Clamp01(1f - d / r), 2f);
                circleTex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        circleTex.Apply();
    }

    private void CreateMaterials()
    {
        Shader sh = Shader.Find("Thunder/Lightning");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Standard");

        // Aerial lightning materials
        matBolt = NewMat(sh, coreColor, coreIntensity);
        matGlow = NewMat(sh, glowColor, glowIntensity);

        // Ground lightning materials (separate HDR intensity & color)
        matCrack = NewMat(sh, crackColor, crackIntensity);
        matCrackGlow = NewMat(sh, crackGlowColor, crackGlowIntensity);

        matParticle = NewMat(sh, Color.white, glowIntensity, useCircleTex: true);
    }

    private Material NewMat(Shader sh, Color c, float intensity, bool useCircleTex = false)
    {
        Material m = new Material(sh);
        m.hideFlags = HideFlags.HideAndDontSave;
        m.SetColor("_Color", c);
        m.SetFloat("_Intensity", intensity);
        if (useCircleTex && circleTex != null) m.SetTexture("_MainTex", circleTex);
        return m;
    }

    private GameObject GetChild(string name)
    {
        Transform t = transform.Find(name);
        if (t != null) return t.gameObject;
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        return go;
    }

    private LineRenderer CreateLR(string name, Material mat, float width, int sortOrder)
    {
        GameObject go = GetChild(name);
        LineRenderer lr = go.GetComponent<LineRenderer>();
        if (lr == null) lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.material = mat;
        lr.widthMultiplier = width;
        lr.positionCount = 0;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        lr.sortingOrder = sortOrder;
        return lr;
    }

    private void SetJaggedWidth(LineRenderer lr, float baseWidth, int keyCount)
    {
        AnimationCurve curve = new AnimationCurve();
        for (int i = 0; i < keyCount; i++)
        {
            float t = (float)i / (keyCount - 1);
            float v;
            if (i == 0)
                v = 0.2f;
            else if (i == keyCount - 1)
                v = 0.5f;
            else
                v = Random.Range(0.3f, 1.4f);
            Keyframe key = new Keyframe(t, v);
            key.inTangent = 0f;
            key.outTangent = 0f;
            curve.AddKey(key);
        }
        lr.widthCurve = curve;
        lr.widthMultiplier = baseWidth;
    }

    private void SetBoltGradient(LineRenderer lr, Color baseCol)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(baseCol * 0.7f, 0f),
                new GradientColorKey(baseCol, 0.5f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(baseCol.a * 0.8f, 0f),
                new GradientAlphaKey(baseCol.a, 0.5f),
                new GradientAlphaKey(baseCol.a, 1f)
            }
        );
        lr.colorGradient = g;
    }

    private void CreateMainBolt()
    {
        mainBoltCore = CreateLR("_MainBoltCore", matBolt, boltCoreWidth, 20);
        SetJaggedWidth(mainBoltCore, boltCoreWidth, 15);
        SetBoltGradient(mainBoltCore, coreColor);

        mainBoltGlow = CreateLR("_MainBoltGlow", matGlow, boltGlowWidth, 15);
        SetJaggedWidth(mainBoltGlow, boltGlowWidth, 15);
        SetBoltGradient(mainBoltGlow, glowColor);
    }

    private void CleanupChildren(string prefix)
    {
        var list = new List<GameObject>();
        foreach (Transform child in transform)
            if (child.name.StartsWith(prefix)) list.Add(child.gameObject);
        foreach (var go in list) DestroyImmediate(go);
    }

    private void CreateBranches()
    {
        CleanupChildren("_Branch_");
        branchCores.Clear();
        branchGlows.Clear();
        for (int i = 0; i < branchCount; i++)
        {
            var core = CreateLR($"_Branch_{i}_Core", matBolt, branchCoreWidth, 18);
            SetJaggedWidth(core, branchCoreWidth, 12);
            SetBoltGradient(core, coreColor * 0.8f);
            branchCores.Add(core);

            var glow = CreateLR($"_Branch_{i}_Glow", matGlow, branchGlowWidth, 13);
            SetJaggedWidth(glow, branchGlowWidth, 12);
            SetBoltGradient(glow, glowColor * 0.7f);
            branchGlows.Add(glow);
        }
    }

    private void CreateParticleSystems()
    {
        electricArcs = GetOrCreatePS("_ElectricArcs");
        SetupElectricArcs(electricArcs);

        sparks = GetOrCreatePS("_Sparks");
        SetupSparks(sparks);

        impactBurst = GetOrCreatePS("_ImpactBurst");
        SetupImpactBurst(impactBurst);

        groundGlow = GetOrCreatePS("_GroundGlow");
        SetupGroundGlow(groundGlow);
    }

    private ParticleSystem GetOrCreatePS(string name)
    {
        GameObject go = GetChild(name);
        ParticleSystem ps = go.GetComponent<ParticleSystem>();
        if (ps == null) ps = go.AddComponent<ParticleSystem>();
        return ps;
    }

    private void SetPSRenderer(ParticleSystem ps, int sortOrder)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.material = matParticle;
        r.sortingOrder = sortOrder;
        r.renderMode = ParticleSystemRenderMode.Billboard;
    }

    private void SetupElectricArcs(ParticleSystem ps)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 5f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = 0.3f; main.startSpeed = 3f;
        main.startSize = 0.2f;
        main.startColor = new Color(0.7f, 0.85f, 1f, 1f);
        main.maxParticles = 800;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 0; em.burstCount = 1;
        em.SetBurst(0, new ParticleSystem.Burst(0f, 300));

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = new Vector3(0, strikeHeight * 0.5f, 0);
        shape.scale = new Vector3(4f, strikeHeight, 4f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sc = new AnimationCurve();
        sc.AddKey(0f, 1f); sc.AddKey(0.5f, 0.6f); sc.AddKey(1f, 0f);
        sol.size = new ParticleSystem.MinMaxCurve(1f, sc);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(0.8f, 0.9f, 1f), 0f), new GradientColorKey(new Color(0.3f, 0.5f, 1f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
        vol.y = new ParticleSystem.MinMaxCurve(-2f, 2f);
        vol.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

        SetPSRenderer(ps, 8);
    }

    private void SetupSparks(ParticleSystem ps)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 5f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = 1f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 15f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startColor = new Color(1f, 0.95f, 0.8f, 1f);
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.5f;

        var em = ps.emission;
        em.rateOverTime = 0; em.burstCount = 1;
        em.SetBurst(0, new ParticleSystem.Burst(0f, 150));

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(1f, 1f, 0.9f), 0f), new GradientColorKey(new Color(0.5f, 0.7f, 1f), 0.5f), new GradientColorKey(new Color(0.2f, 0.3f, 0.6f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        var trails = ps.trails;
        trails.enabled = true;
        trails.ratio = 0.5f;
        trails.lifetime = 0.2f;
        var tg = new Gradient();
        tg.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.3f, 0.5f, 1f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(tg);

        SetPSRenderer(ps, 6);
        ps.GetComponent<ParticleSystemRenderer>().trailMaterial = matParticle;
    }

    private void SetupImpactBurst(ParticleSystem ps)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 5f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = 0.5f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startColor = new Color(0.6f, 0.8f, 1f, 1f);
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.5f;

        var em = ps.emission;
        em.rateOverTime = 0; em.burstCount = 1;
        em.SetBurst(0, new ParticleSystem.Burst(0f, 200));

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sc = new AnimationCurve();
        sc.AddKey(0f, 1f); sc.AddKey(1f, 0f);
        sol.size = new ParticleSystem.MinMaxCurve(1f, sc);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.4f, 0.6f, 1f), 0.5f), new GradientColorKey(new Color(0.1f, 0.2f, 0.5f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        SetPSRenderer(ps, 7);
    }

    private void SetupGroundGlow(ParticleSystem ps)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 5f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = 0.8f; main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startColor = new Color(0.4f, 0.6f, 1f, 0.5f);
        main.maxParticles = 50;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 0; em.burstCount = 1;
        em.SetBurst(0, new ParticleSystem.Burst(0f, 20));

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sc = new AnimationCurve();
        sc.AddKey(0f, 0.2f); sc.AddKey(0.3f, 1f); sc.AddKey(1f, 0f);
        sol.size = new ParticleSystem.MinMaxCurve(1f, sc);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.3f, 0.5f, 1f), 0.5f), new GradientColorKey(new Color(0.1f, 0.2f, 0.5f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.3f, 0.5f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        SetPSRenderer(ps, 3);
    }

    private void CreateFlashLight()
    {
        GameObject go = GetChild("_FlashLight");
        flashLight = go.GetComponent<Light>();
        if (flashLight == null) flashLight = go.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = flashColor;
        flashLight.intensity = 0f;
        flashLight.range = flashRange;
        flashLight.shadows = LightShadows.None;
        flashLight.transform.position = new Vector3(
            transform.position.x, groundY + strikeHeight * 0.3f, transform.position.z);
    }

    private void CreateGroundCracks()
    {
        CleanupChildren("_Crack_");
        crackCores.Clear();
        crackGlows.Clear();
        for (int i = 0; i < crackCount; i++)
        {
            var core = CreateLR($"_Crack_{i}_Core", matCrack, crackCoreWidth, 12);
            SetJaggedWidth(core, crackCoreWidth, crackWidthKeys);
            SetBoltGradient(core, crackColor);
            crackCores.Add(core);

            var glow = CreateLR($"_Crack_{i}_Glow", matCrackGlow, crackGlowWidth, 11);
            SetJaggedWidth(glow, crackGlowWidth, crackWidthKeys);
            SetBoltGradient(glow, crackGlowColor);
            crackGlows.Add(glow);
        }
    }

    private void CreateBloomVolume()
    {
        if (!enableBloom) return;
#if HAS_UNITY_URP
        DestroyBloomVolume();

        bloomVolumeGo = new GameObject("_ThunderBloomVolume");
        bloomVolumeGo.transform.SetParent(transform, false);
        bloomVolume = bloomVolumeGo.AddComponent<Volume>();
        bloomVolume.isGlobal = true;
        bloomVolume.priority = 100;

        bloomProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        bloomProfile.hideFlags = HideFlags.HideAndDontSave;

        var bloom = bloomProfile.Add<Bloom>(true);
        bloom.intensity.Override(bloomIntensity);
        bloom.threshold.Override(bloomThreshold);
        bloom.scatter.Override(bloomScatter);
        bloom.tint.Override(new Color(0.6f, 0.8f, 1f));
        bloom.highQualityFiltering.Override(true);

        bloomVolume.sharedProfile = bloomProfile;
#else
        Debug.LogWarning("[ThunderLightningEffect] URP not available, Bloom skipped.");
#endif
    }

    private void DestroyBloomVolume()
    {
#if HAS_UNITY_URP
        if (bloomProfile != null)
        {
            if (Application.isPlaying) Destroy(bloomProfile);
            else DestroyImmediate(bloomProfile);
            bloomProfile = null;
        }
        if (bloomVolumeGo != null)
        {
            if (Application.isPlaying) Destroy(bloomVolumeGo);
            else DestroyImmediate(bloomVolumeGo);
            bloomVolumeGo = null;
            bloomVolume = null;
        }
#endif
    }

    #endregion

    #region Generation

    private void GenerateLightning()
    {
        Vector3 start = new Vector3(transform.position.x, transform.position.y + strikeHeight, transform.position.z);
        Vector3 end = new Vector3(transform.position.x, groundY, transform.position.z);

        mainBoltPath = GenerateBoltPath(start, end, mainBoltSegments, jitterAmount);
        ApplyPath(mainBoltCore, mainBoltPath);
        ApplyPath(mainBoltGlow, mainBoltPath);

        branchPaths.Clear();
        Vector3 mainDir = (end - start).normalized;
        for (int i = 0; i < branchCount; i++)
        {
            int idx = Random.Range(
                Mathf.FloorToInt(mainBoltSegments * 0.2f),
                Mathf.FloorToInt(mainBoltSegments * 0.85f));
            Vector3 origin = mainBoltPath[idx];
            Vector3 perp = Vector3.ProjectOnPlane(Random.onUnitSphere, mainDir).normalized;
            if (perp == Vector3.zero) perp = Vector3.right;
            float angle = Random.Range(20f, 60f);
            Vector3 bDir = Quaternion.AngleAxis(angle, Vector3.Cross(mainDir, perp)) * perp;
            bDir = (bDir - mainDir * 0.2f).normalized;
            Vector3 bEnd = origin + bDir * branchLength * Random.Range(0.5f, 1f);
            var path = GenerateBoltPath(origin, bEnd, branchSegments, branchJitter);
            branchPaths.Add(path);
            if (i < branchCores.Count) ApplyPath(branchCores[i], path);
            if (i < branchGlows.Count) ApplyPath(branchGlows[i], path);
        }
    }

    /// <summary>
    /// Jagged, angular lightning path with sharp zigzag displacements.
    /// </summary>
    private List<Vector3> GenerateBoltPath(Vector3 start, Vector3 end, int segments, float jitter)
    {
        var path = new List<Vector3>(segments + 1);
        Vector3 dir = (end - start).normalized;
        Vector3 p1 = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
        Vector3 p2 = Vector3.Cross(dir, p1).normalized;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 pt = Vector3.Lerp(start, end, t);
            float env = Mathf.Sin(t * Mathf.PI);
            float j = jitter * env;
            pt += p1 * (Random.value - 0.5f) * 2f * j;
            pt += p2 * (Random.value - 0.5f) * 2f * j;
            path.Add(pt);
        }

        path[0] = start;
        path[path.Count - 1] = end;
        return path;
    }

    private void GenerateCracks()
    {
        crackPaths.Clear();
        Vector3 center = new Vector3(transform.position.x, groundY + 0.05f, transform.position.z);
        for (int i = 0; i < crackCount; i++)
        {
            float angle = (float)i / crackCount * 360f + Random.Range(-15f, 15f);
            crackPaths.Add(GenerateCrackPath(center, angle));
        }
    }

    /// <summary>
    /// Generates a jagged, branch-like ground crack path with sharp angular turns.
    /// Uses the same midpoint-displacement algorithm as aerial bolts, but confined
    /// to the ground plane (y = 0) with per-segment direction jitter.
    /// </summary>
    private List<Vector3> GenerateCrackPath(Vector3 center, float startAngle)
    {
        float radius = crackMaxRadius * Random.Range(0.15f, 1f);
        Vector3 start = center;
        Vector3 end = center + Quaternion.Euler(0, startAngle, 0) * Vector3.forward * radius;

        // Generate the main crack path using the same jagged algorithm as bolts
        var path = GenerateBoltPath(start, end, crackSegments, crackJitter);

        // Flatten to ground plane — project every point onto y = groundY + 0.05
        for (int i = 0; i < path.Count; i++)
            path[i] = new Vector3(path[i].x, center.y, path[i].z);

        return path;
    }

    private void ApplyPath(LineRenderer lr, List<Vector3> path)
    {
        if (lr == null || path == null || path.Count == 0) return;
        lr.positionCount = path.Count;
        lr.SetPositions(path.ToArray());
    }

    #endregion

    #region State Machine

    private void Update()
    {
        if (!initialized) return;
        float dt;
        if (Application.isPlaying)
        {
            dt = Time.deltaTime;
        }
        else
        {
            dt = Time.realtimeSinceStartup - lastRealTime;
            if (dt > 0.1f) dt = 0.1f;
            lastRealTime = Time.realtimeSinceStartup;
        }

        switch (state)
        {
            case EffectState.Idle: UpdateIdle(dt); break;
            case EffectState.Striking: UpdateStriking(dt); break;
            case EffectState.CrackExpand: UpdateCrackExpand(dt); break;
            case EffectState.CrackFade: UpdateCrackFade(dt); break;
        }
    }

    private void UpdateIdle(float dt)
    {
        if (!autoRepeat) return;
        idleTimer += dt;
        if (idleTimer >= repeatInterval) Strike();
    }

    private void UpdateStriking(float dt)
    {
        stateTimer += dt;
        flickerTimer += dt;
        if (flickerTimer >= flickerInterval)
        {
            flickerTimer = 0f;
            GenerateLightning();
        }
        float phase = stateTimer / boltVisibleDuration;
        flashLight.intensity = flashIntensity * (1f - phase) * Random.Range(0.7f, 1f);

        if (stateTimer >= boltVisibleDuration)
        {
            SetBoltsVisible(false);
            flashLight.intensity = 0f;
            state = EffectState.CrackFade;
            stateTimer = 0f;

            Vector3 impact = new Vector3(transform.position.x, groundY + 0.1f, transform.position.z);
            sparks.transform.position = impact;
            impactBurst.transform.position = impact;
            groundGlow.transform.position = impact;
            electricArcs.transform.position = transform.position;

            sparks.Play();
            impactBurst.Play();
            groundGlow.Play();
            electricArcs.Play();
        }
    }

    private void UpdateCrackExpand(float dt)
    {
        stateTimer += dt;
        float p = Mathf.Clamp01(stateTimer / crackExpandDuration);
        SetCracksVisible(true, p);
        if (stateTimer >= crackExpandDuration)
        {
            state = EffectState.CrackFade;
            stateTimer = 0f;
        }
    }

    private void UpdateCrackFade(float dt)
    {
        stateTimer += dt;
        float p = Mathf.Clamp01(stateTimer / crackFadeDuration);
        float alpha = 1f - p;
        for (int i = 0; i < crackCores.Count; i++)
        {
            if (crackCores[i] == null) continue;
            crackCores[i].widthMultiplier = crackCoreWidth * alpha;
            crackGlows[i].widthMultiplier = crackGlowWidth * alpha;
        }

        if (stateTimer >= crackFadeDuration)
        {
            SetCracksVisible(false, 0f);
            state = EffectState.Idle;
            stateTimer = 0f;
            idleTimer = 0f;
        }
    }

    #endregion

    #region Visibility

    private void SetBoltsVisible(bool v)
    {
        if (mainBoltCore != null) mainBoltCore.enabled = v;
        if (mainBoltGlow != null) mainBoltGlow.enabled = v;
        foreach (var lr in branchCores) if (lr != null) lr.enabled = v;
        foreach (var lr in branchGlows) if (lr != null) lr.enabled = v;
    }

    private void SetCracksVisible(bool v, float progress)
    {
        for (int i = 0; i < crackCores.Count; i++)
        {
            if (crackCores[i] == null) continue;
            crackCores[i].enabled = v;
            crackGlows[i].enabled = v;
            if (v && i < crackPaths.Count)
            {
                var path = crackPaths[i];
                int n = Mathf.Clamp(Mathf.CeilToInt(path.Count * progress), 0, path.Count);
                crackCores[i].positionCount = n;
                crackGlows[i].positionCount = n;
                if (n > 0)
                {
                    var sub = path.GetRange(0, n);
                    crackCores[i].SetPositions(sub.ToArray());
                    crackGlows[i].SetPositions(sub.ToArray());
                }
            }
        }
    }

    #endregion

    #region Public API

    [ContextMenu("Strike Now")]
    public void Strike()
    {
        if (state != EffectState.Idle) return;
        GenerateLightning();
        GenerateCracks();
        SetBoltsVisible(true);
        SetCracksVisible(true, 1f);
        for (int i = 0; i < crackCores.Count; i++)
        {
            if (crackCores[i] != null)
            {
                crackCores[i].widthMultiplier = crackCoreWidth;
                crackGlows[i].widthMultiplier = crackGlowWidth;
            }
        }
        flashLight.intensity = flashIntensity;
        state = EffectState.Striking;
        stateTimer = 0f;
        flickerTimer = 0f;
    }

    #endregion
}
