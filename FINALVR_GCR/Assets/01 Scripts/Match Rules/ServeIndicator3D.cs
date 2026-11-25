using UnityEngine;

/// <summary>
/// Indicador visual 3D sobre la mesa que muestra de forma inmediata qué lado tiene el saque.
/// Se desplaza suavemente entre el lado del jugador y el lado del rival, pulsa durante
/// WaitingForServe y se atenúa durante el rally activo para no estorbar la vista en VR.
/// </summary>
public class ServeIndicator3D : MonoBehaviour
{
    [Header("Referencias")]
    public MatchReferee referee;
    public Transform playerTableTarget;
    public Transform enemyTableTarget;

    [Header("Posicionamiento 3D")]
    [Tooltip("Altura sobre la superficie de la mesa donde flota el indicador.")]
    public float heightAboveTable = 0.62f;
    [Tooltip("Desplazamiento hacia el fondo de la mitad correspondiente (0 = centro de la mitad).")]
    public float depthOffsetTowardEdge = 0.18f;
    public float moveSmoothSpeed = 9f;
    public float bobAmplitude = 0.045f;
    public float bobFrequency = 3.2f;
    public float spinSpeedDeg = 75f;

    [Header("Colores")]
    public Color playerServeColor = new Color(0.18f, 0.85f, 1f, 1f);
    public Color aiServeColor = new Color(1f, 0.52f, 0.18f, 1f);
    public Color letHighlightColor = new Color(1f, 0.90f, 0.25f, 1f);

    [Header("Jerarquía Visual (Auto-generada)")]
    [SerializeField] private Transform indicatorRoot;
    [SerializeField] private Transform chevronPivot;
    [SerializeField] private Transform ringPivot;

    private Collider playerTableCollider;
    private Collider enemyTableCollider;
    private Renderer[] cachedRenderers;
    private Material runtimeMaterial;
    private MaterialPropertyBlock propBlock;
    private bool initializedPosition;
    private float currentAlpha = 1f;
    private Color currentColor;
    private float letFlashTimer;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        ResolveReferences();
        BuildVisualIfNeeded();
        CacheRenderers();
        propBlock = new MaterialPropertyBlock();
        currentColor = playerServeColor;
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (referee != null)
        {
            referee.OnServeTurnChanged += HandleServeTurnChanged;
            referee.OnLetCalled += HandleLetCalled;
        }
    }

    private void OnDisable()
    {
        if (referee != null)
        {
            referee.OnServeTurnChanged -= HandleServeTurnChanged;
            referee.OnLetCalled -= HandleLetCalled;
        }
    }

    private void Start()
    {
        ResolveReferences();
        if (referee != null)
        {
            referee.OnServeTurnChanged -= HandleServeTurnChanged;
            referee.OnServeTurnChanged += HandleServeTurnChanged;
            referee.OnLetCalled -= HandleLetCalled;
            referee.OnLetCalled += HandleLetCalled;
        }

        SnapToCurrentServer();
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    private void Update()
    {
        if (indicatorRoot == null) return;

        bool isPlayerServe = referee == null || referee.GetIsPlayerTurnToServe();
        MatchReferee.PointPhase phase = referee != null ? referee.CurrentPhase : MatchReferee.PointPhase.WaitingForServe;
        bool isMatchOver = referee != null && referee.IsMatchOver;

        bool showFull = !isMatchOver && phase == MatchReferee.PointPhase.WaitingForServe;
        bool showSubtle = !isMatchOver && (phase == MatchReferee.PointPhase.BetweenPoints || phase == MatchReferee.PointPhase.BetweenSets);
        bool visible = showFull || showSubtle;

        float dt = Time.unscaledDeltaTime;
        if (letFlashTimer > 0f)
        {
            letFlashTimer = Mathf.Max(0f, letFlashTimer - dt);
        }

        Vector3 targetBasePos = ComputeAnchorPosition(isPlayerServe);
        if (!initializedPosition)
        {
            indicatorRoot.position = targetBasePos;
            initializedPosition = true;
        }
        else
        {
            float bob = showFull ? Mathf.Sin(Time.unscaledTime * bobFrequency) * bobAmplitude : 0f;
            Vector3 desired = targetBasePos + Vector3.up * bob;
            indicatorRoot.position = Vector3.Lerp(indicatorRoot.position, desired, Mathf.Clamp01(dt * moveSmoothSpeed));
        }

        if (ringPivot != null)
        {
            ringPivot.Rotate(Vector3.up, spinSpeedDeg * dt, Space.Self);
        }

        if (chevronPivot != null)
        {
            float pulseScale = showFull ? 1f + Mathf.Sin(Time.unscaledTime * (bobFrequency * 1.4f)) * 0.08f : 0.78f;
            chevronPivot.localScale = Vector3.one * pulseScale;
        }

        float targetAlpha = isMatchOver ? 0f : (showFull ? 1f : (showSubtle ? 0.35f : 0f));
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, dt * 4.5f);

        bool shouldBeActive = visible || currentAlpha > 0.02f;
        if (indicatorRoot.gameObject.activeSelf != shouldBeActive)
        {
            indicatorRoot.gameObject.SetActive(shouldBeActive);
        }

        if (!shouldBeActive) return;

        Color targetColor = isPlayerServe ? playerServeColor : ResolveAIServeColor();
        if (letFlashTimer > 0f)
        {
            targetColor = Color.Lerp(targetColor, letHighlightColor, 0.75f);
        }
        currentColor = Color.Lerp(currentColor, targetColor, Mathf.Clamp01(dt * 8f));
        ApplyColorToRenderers(currentColor, currentAlpha);
    }

    private void HandleServeTurnChanged(bool isPlayerServing, MatchReferee.PointPhase phase)
    {
        if (phase == MatchReferee.PointPhase.WaitingForServe && !initializedPosition)
        {
            SnapToCurrentServer();
        }
    }

    private void HandleLetCalled(string _)
    {
        letFlashTimer = 1.0f;
    }

    /// <summary>
    /// Construye la geometría 3D estilizada del indicador de saque (flecha vertical + aro) de forma idempotente y URP-safe.
    /// </summary>
    [ContextMenu("Build Serve Indicator Visual")]
    public void BuildVisualIfNeeded()
    {
        if (indicatorRoot == null)
        {
            Transform existing = transform.Find("ServeIndicatorRoot");
            if (existing != null)
            {
                indicatorRoot = existing;
            }
            else
            {
                GameObject rootGo = new GameObject("ServeIndicatorRoot");
                rootGo.transform.SetParent(transform, false);
                indicatorRoot = rootGo.transform;
            }
        }

        Material baseMat = CreateSafeMaterial(playerServeColor);

        // 1. Pivote de flecha/chevrón apuntando hacia la mesa
        chevronPivot = FindOrCreateChild(indicatorRoot, "ChevronPivot");
        chevronPivot.localPosition = Vector3.zero;
        chevronPivot.localRotation = Quaternion.identity;

        Transform shaft = EnsurePrimitive(chevronPivot, "ArrowShaft", PrimitiveType.Cylinder, baseMat);
        shaft.localPosition = new Vector3(0f, 0.10f, 0f);
        shaft.localRotation = Quaternion.identity;
        shaft.localScale = new Vector3(0.036f, 0.085f, 0.036f);

        Transform leftWing = EnsurePrimitive(chevronPivot, "ArrowWingLeft", PrimitiveType.Cube, baseMat);
        leftWing.localPosition = new Vector3(-0.048f, 0.015f, 0f);
        leftWing.localRotation = Quaternion.Euler(0f, 0f, 42f);
        leftWing.localScale = new Vector3(0.032f, 0.135f, 0.032f);

        Transform rightWing = EnsurePrimitive(chevronPivot, "ArrowWingRight", PrimitiveType.Cube, baseMat);
        rightWing.localPosition = new Vector3(0.048f, 0.015f, 0f);
        rightWing.localRotation = Quaternion.Euler(0f, 0f, -42f);
        rightWing.localScale = new Vector3(0.032f, 0.135f, 0.032f);

        // 2. Aro orbital alrededor de la flecha
        ringPivot = FindOrCreateChild(indicatorRoot, "RingPivot");
        ringPivot.localPosition = new Vector3(0f, -0.04f, 0f);
        ringPivot.localRotation = Quaternion.identity;

        const int ringSegments = 8;
        const float ringRadius = 0.135f;
        for (int i = 0; i < ringSegments; i++)
        {
            float angleRad = (i / (float)ringSegments) * Mathf.PI * 2f;
            Transform seg = EnsurePrimitive(ringPivot, $"RingSeg_{i}", PrimitiveType.Cube, baseMat);
            seg.localPosition = new Vector3(Mathf.Cos(angleRad) * ringRadius, 0f, Mathf.Sin(angleRad) * ringRadius);
            seg.localRotation = Quaternion.Euler(0f, -angleRad * Mathf.Rad2Deg, 0f);
            seg.localScale = new Vector3(0.020f, 0.014f, 0.075f);
        }

        CacheRenderers();
        SnapToCurrentServer();
    }

    public void SnapToCurrentServer()
    {
        ResolveReferences();
        if (indicatorRoot == null) return;

        bool isPlayerServe = referee == null || referee.GetIsPlayerTurnToServe();
        indicatorRoot.position = ComputeAnchorPosition(isPlayerServe);
        initializedPosition = true;
    }

    private Vector3 ComputeAnchorPosition(bool isPlayerSide)
    {
        Vector3 playerCenter = GetTableSurfaceCenter(playerTableTarget, ref playerTableCollider);
        Vector3 enemyCenter = GetTableSurfaceCenter(enemyTableTarget, ref enemyTableCollider);

        bool hasPlayer = playerTableTarget != null;
        bool hasEnemy = enemyTableTarget != null;

        if (!hasPlayer && !hasEnemy)
        {
            return transform.position + Vector3.up * heightAboveTable;
        }

        Vector3 chosen = isPlayerSide
            ? (hasPlayer ? playerCenter : enemyCenter)
            : (hasEnemy ? enemyCenter : playerCenter);

        if (hasPlayer && hasEnemy)
        {
            Vector3 awayFromNet = chosen - (0.5f * (playerCenter + enemyCenter));
            awayFromNet.y = 0f;
            if (awayFromNet.sqrMagnitude > 0.0001f)
            {
                chosen += awayFromNet.normalized * depthOffsetTowardEdge;
            }
        }

        chosen.y += heightAboveTable;
        return chosen;
    }

    private Color ResolveAIServeColor()
    {
        if (referee != null && referee.rivalAI != null && referee.rivalAI.ActiveDifficultyProfile != null)
        {
            return referee.rivalAI.ActiveDifficultyProfile.accentColor;
        }
        return aiServeColor;
    }

    private void ResolveReferences()
    {
        if (referee == null)
        {
            referee = FindFirstObjectByType<MatchReferee>();
        }

        if (playerTableTarget == null)
        {
            playerTableTarget = FindTransformSafe("TablePlayer");
        }

        if (enemyTableTarget == null)
        {
            enemyTableTarget = FindTransformSafe("TableEnemy");
        }
    }

    private static Transform FindTransformSafe(string tagOrName)
    {
        try
        {
            GameObject tagged = GameObject.FindGameObjectWithTag(tagOrName);
            if (tagged != null) return tagged.transform;
        }
        catch (UnityException)
        {
            // Ignorar si el tag no existe
        }

        GameObject named = GameObject.Find(tagOrName);
        return named != null ? named.transform : null;
    }

    private static Vector3 GetTableSurfaceCenter(Transform target, ref Collider cachedCol)
    {
        if (target == null) return Vector3.zero;
        if (cachedCol == null) cachedCol = target.GetComponentInChildren<Collider>();
        if (cachedCol != null)
        {
            Bounds b = cachedCol.bounds;
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }
        return target.position;
    }

    private void CacheRenderers()
    {
        cachedRenderers = indicatorRoot != null
            ? indicatorRoot.GetComponentsInChildren<Renderer>(true)
            : new Renderer[0];
    }

    private void ApplyColorToRenderers(Color color, float alpha)
    {
        if (cachedRenderers == null || propBlock == null) return;

        Color scaled = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        Color emission = color * Mathf.Lerp(0.15f, 1.45f, Mathf.Clamp01(alpha));

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            Renderer r = cachedRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(propBlock);
            propBlock.SetColor(BaseColorId, scaled);
            propBlock.SetColor(ColorId, scaled);
            propBlock.SetColor(EmissionColorId, emission);
            r.SetPropertyBlock(propBlock);
        }
    }

    private Material CreateSafeMaterial(Color initialColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader) { name = "ServeIndicator3D_Mat" };
        if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, initialColor);
        if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, initialColor);
        if (mat.HasProperty(EmissionColorId))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor(EmissionColorId, initialColor * 1.1f);
        }

        if (Application.isPlaying)
        {
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
            runtimeMaterial = mat;
        }
        return mat;
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing;

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static Transform EnsurePrimitive(Transform parent, string name, PrimitiveType primitiveType, Material material)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            go.transform.SetParent(parent, false);
        }

        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null && material != null)
        {
            rend.sharedMaterial = material;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        return go.transform;
    }
}
