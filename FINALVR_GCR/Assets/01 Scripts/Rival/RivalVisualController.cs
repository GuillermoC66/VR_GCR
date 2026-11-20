using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controlador visual, avatar VR estilizado (primitivas URP-safe), animaciones procedurales,
/// telegrafiado visual/sonoro de tiros especiales y hooks plug-and-play para Animator 3D del Rival.
/// </summary>
public class RivalVisualController : MonoBehaviour
{
    private const string AvatarRootName = "RivalAvatarRoot";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");

    private enum ProceduralPoseState
    {
        Idle,
        ServeWindup,
        TelegraphBackswing,
        StrikeFollowThrough,
        MissWobble,
        MatchWin,
        MatchLose
    }

    [Header("Referencias del Rival")]
    [Tooltip("Controlador de IA del rival. Si se deja vacío se busca automáticamente en este GameObject.")]
    public RivalAIController rivalAI;

    [Tooltip("Opcional: pivote visual de la pala si está separado del Transform aiPaddle.")]
    public Transform paddleVisualPivot;

    [Tooltip("Oculta el MeshRenderer primitivo del GameObject contenedor (ej. Capsule legado) al construir el avatar.")]
    public bool hideHostPlaceholderMesh = true;

    [Header("Posicionamiento del Avatar VR")]
    [Tooltip("Distancia detrás de baselineX donde se coloca el cuerpo del rival.")]
    public float avatarBackOffset = 0.68f;

    [Tooltip("Altura Y del suelo sobre el que se apoya el avatar (se autoajusta si es negativo).")]
    public float avatarFloorY = 0f;

    [Tooltip("Ancho máximo lateral (Z) al que el cuerpo del rival sigue la pala.")]
    public float maxLateralFollowOffset = 0.85f;

    [Tooltip("Suavizado del seguimiento lateral del cuerpo.")]
    public float bodyFollowSpeed = 9f;

    [Tooltip("Si true, utiliza la mano derecha del rival para sujetar la pala; si false, la izquierda.")]
    public bool paddleOnRightHand = true;

    [Header("Animación Procedural")]
    [Tooltip("Inclinación máxima del torso (grados) al desplazarse lateralmente.")]
    public float maxMovementLeanAngle = 14f;

    [Tooltip("Amplitud vertical (m) de la respiración en reposo.")]
    public float idleBreathAmplitude = 0.018f;

    [Tooltip("Velocidad de respiración en reposo.")]
    public float idleBreathSpeed = 2.4f;

    [Tooltip("Ángulo máximo de giro horizontal de la cabeza siguiendo la pelota.")]
    public float headMaxYaw = 50f;

    [Tooltip("Ángulo máximo de inclinación vertical de la cabeza siguiendo la pelota.")]
    public float headMaxPitch = 28f;

    [Tooltip("Velocidad de rotación suave de la cabeza al rastrear la pelota.")]
    public float headTrackSpeed = 10f;

    [Tooltip("Duración de la animación de golpe y seguimiento (follow-through).")]
    public float strikeFollowThroughDuration = 0.34f;

    [Tooltip("Duración del tropiezo visual cuando el rival falla un tiro.")]
    public float missWobbleDuration = 0.55f;

    [Header("Colores de Telegrafiado y Energía")]
    public Color smashTelegraphColor = new Color(1f, 0.32f, 0.08f, 1f);
    public Color sliceTelegraphColor = new Color(0.12f, 0.88f, 1f, 1f);
    public Color specialTelegraphColor = new Color(1f, 0.84f, 0.12f, 1f);
    public Color driveFlashColor = new Color(0.85f, 0.92f, 1f, 1f);

    [Tooltip("Intensidad HDR de emisión durante el telegrafiado.")]
    public float telegraphEmissionIntensity = 2.8f;

    [Tooltip("Intensidad HDR de emisión del aura cuando el poder especial está cargado.")]
    public float powerReadyEmissionIntensity = 2.1f;

    [Header("Audio de Telegrafiado (Opcional)")]
    public AudioSource audioSource;
    public AudioClip smashTelegraphClip;
    public AudioClip sliceTelegraphClip;
    public AudioClip specialTelegraphClip;
    public AudioClip powerReadyClip;
    public AudioClip strikeClip;
    public AudioClip specialStrikeClip;
    [Range(0f, 1.5f)]
    public float sfxGain = 0.85f;

    [Header("Hooks para Personaje 3D con Animator (Plug-and-Play)")]
    [Tooltip("Asigna aquí el Animator de un personaje FBX 3D futuro para disparar animaciones automáticamente.")]
    public Animator characterAnimator;
    public string animParamMoveX = "MoveX";
    public string animParamSwing = "Swing";
    public string animParamSmash = "Smash";
    public string animParamSlice = "Slice";
    public string animParamSpecial = "Special";
    public string animParamServe = "Serve";
    public string animParamWin = "Win";
    public string animParamLose = "Lose";
    public string animParamPowerReady = "PowerReady";

    [Header("Referencias del Avatar Procedural (Auto-generadas)")]
    [SerializeField] private Transform avatarRoot;
    [SerializeField] private Transform bodyPivot;
    [SerializeField] private Transform torsoTransform;
    [SerializeField] private Transform headPivot;
    [SerializeField] private Transform rightShoulderPivot;
    [SerializeField] private Transform upperArmTransform;
    [SerializeField] private Transform elbowJointTransform;
    [SerializeField] private Transform forearmTransform;
    [SerializeField] private Transform leftShoulderPivot;
    [SerializeField] private Transform leftArmTransform;
    [SerializeField] private Transform difficultyAuraRoot;
    [SerializeField] private Transform telegraphHaloRoot;

    // Renderers cacheados del avatar (se enlazan en BuildPlaceholderAvatarIfNeeded)
    private readonly List<Renderer> primaryRenderers = new List<Renderer>();
    private readonly List<Renderer> secondaryRenderers = new List<Renderer>();
    private readonly List<Renderer> darkJointRenderers = new List<Renderer>();
    private readonly List<Renderer> visorEyeRenderers = new List<Renderer>();
    private readonly List<Renderer> difficultyAuraRenderers = new List<Renderer>();
    private readonly List<Renderer> telegraphHaloRenderers = new List<Renderer>();
    private Renderer chestCoreRenderer;
    private Renderer visorGlassRenderer;

    // Materiales creados en runtime (destruidos limpiamente en OnDestroy)
    private readonly List<Material> runtimeCreatedMaterials = new List<Material>();
    private MaterialPropertyBlock sharedPropBlock;

    // Emisión temporal sobre la pala del rival (restaurada en OnDestroy)
    private Renderer cachedPaddleRenderer;
    private Material originalPaddleSharedMaterial;
    private Material runtimePaddleMaterial;

    // Estado de colores temáticos actuales
    private Color currentPrimaryColor = new Color(0.18f, 0.45f, 0.85f, 1f);
    private Color currentSecondaryColor = new Color(0.95f, 0.85f, 0.25f, 1f);
    private Color currentDifficultyAccent = new Color(1f, 0.68f, 0.15f, 1f);

    // Estado de animación procedural
    private bool isSubscribed;
    private bool hasCapturedPaddleBaseRotation;
    private Quaternion paddleBaseWorldRotation = Quaternion.identity;
    private Vector3 avatarBaseAnchorWorld;
    private float currentAvatarZ;
    private float currentBodyLeanZ;
    private float currentBodyPitchX;
    private float currentTorsoTwistY;
    private float currentStepInX;
    private Quaternion currentHeadLocalRot = Quaternion.identity;

    private ProceduralPoseState poseState = ProceduralPoseState.Idle;
    private float poseTimer;
    private float poseDuration = 0.25f;
    private ArcadeShotType activeShotType = ArcadeShotType.Drive;
    private SpecialPowerType activeSpecialPower = SpecialPowerType.FastballSmash;
    private bool activeIsServe;

    // Estado de telegrafiado y poder
    private bool isPowerReady;
    private float powerCharge01;
    private float telegraphFlashTimer;
    private float telegraphFlashDuration = 0.35f;
    private Color activeTelegraphColor = new Color(1f, 0.84f, 0.12f, 1f);
    private float currentPaddleEmissionStrength;
    private Color currentPaddleEmissionColor = Color.black;

    // Caché de parámetros válidos en characterAnimator para evitar warnings
    private Animator cachedValidatedAnimator;
    private readonly HashSet<int> validAnimatorParamHashes = new HashSet<int>();

    // Constantes anatómicas del avatar estilizado (metros)
    private const float TorsoCenterY = 1.04f;
    private const float HeadNeckY = 1.44f;
    private const float ShoulderHeightY = 1.29f;
    private const float ShoulderHalfWidth = 0.24f;
    private const float UpperArmLength = 0.32f;
    private const float ForearmLength = 0.34f;
    private const float HaloBaseHeightY = 1.32f;
    private const float FloorRingY = 0.025f;

    public Transform AvatarRoot => avatarRoot;

    private void Awake()
    {
        sharedPropBlock = new MaterialPropertyBlock();

        if (rivalAI == null)
        {
            rivalAI = GetComponent<RivalAIController>();
            if (rivalAI == null) rivalAI = GetComponentInParent<RivalAIController>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        BuildPlaceholderAvatarIfNeeded();
        CacheAnimatorParameters();
    }

    private void OnEnable()
    {
        SubscribeToRivalEvents();
    }

    private void Start()
    {
        SubscribeToRivalEvents();
        CapturePaddleBaseTransformAndMaterial();
        SyncInitialStateFromRival();
    }

    private void OnDisable()
    {
        UnsubscribeFromRivalEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromRivalEvents();

        if (cachedPaddleRenderer != null && originalPaddleSharedMaterial != null)
        {
            cachedPaddleRenderer.sharedMaterial = originalPaddleSharedMaterial;
        }

        if (runtimePaddleMaterial != null)
        {
            DestroyMaterialSafe(runtimePaddleMaterial);
            runtimePaddleMaterial = null;
        }

        for (int i = 0; i < runtimeCreatedMaterials.Count; i++)
        {
            if (runtimeCreatedMaterials[i] != null)
            {
                DestroyMaterialSafe(runtimeCreatedMaterials[i]);
            }
        }
        runtimeCreatedMaterials.Clear();
    }

    /// <summary>
    /// Construye (de forma idempotente) el avatar estilizado VR con primitivas URP-safe.
    /// Seguro de invocar tanto en Edit Mode (RivalSetupEditor) como en Play Mode (Awake/Start).
    /// </summary>
    [ContextMenu("Build Placeholder Avatar If Needed")]
    public void BuildPlaceholderAvatarIfNeeded()
    {
        if (rivalAI == null)
        {
            rivalAI = GetComponent<RivalAIController>();
        }

        if (sharedPropBlock == null)
        {
            sharedPropBlock = new MaterialPropertyBlock();
        }

        Transform existingRoot = avatarRoot != null ? avatarRoot : transform.Find(AvatarRootName);
        if (existingRoot != null)
        {
            avatarRoot = existingRoot;
            RebindAvatarHierarchyReferences();
            PositionAvatarRootAtBaseline(immediate: true);
            UpdateArmRigToPaddle();
            HideHostPrimitiveMeshIfRequested();
            ApplyThemeFromRivalSafe();
            return;
        }

        GameObject rootGo = new GameObject(AvatarRootName);
        avatarRoot = rootGo.transform;
        avatarRoot.SetParent(transform, false);

        // Compensar escala del GameObject contenedor (ej. Capsule 0.86 en SampleScene) para mantener escala humana 1:1
        avatarRoot.localScale = ComputeParentInverseScale();

        Material primaryMat = CreateAvatarMaterial("Rival_Primary_Mat", currentPrimaryColor, 0.2f, 0.55f, Color.black);
        Material secondaryMat = CreateAvatarMaterial("Rival_Secondary_Mat", currentSecondaryColor, 0.25f, 0.65f, Color.black);
        Material darkJointMat = CreateAvatarMaterial("Rival_Joints_Mat", new Color(0.12f, 0.14f, 0.18f, 1f), 0.35f, 0.45f, Color.black);
        Material visorGlassMat = CreateAvatarMaterial("Rival_Visor_Mat", new Color(0.05f, 0.07f, 0.11f, 1f), 0.7f, 0.9f, currentDifficultyAccent * 0.25f);
        Material visorEyeMat = CreateAvatarMaterial("Rival_VisorEyes_Mat", currentDifficultyAccent, 0.1f, 0.8f, currentDifficultyAccent * 1.4f);
        Material chestCoreMat = CreateAvatarMaterial("Rival_ChestCore_Mat", currentDifficultyAccent, 0.15f, 0.75f, currentDifficultyAccent * 1.15f);
        Material auraMat = CreateAvatarMaterial("Rival_DifficultyAura_Mat", currentDifficultyAccent, 0.1f, 0.7f, currentDifficultyAccent * 1.25f);
        Material haloMat = CreateAvatarMaterial("Rival_TelegraphHalo_Mat", specialTelegraphColor, 0.1f, 0.8f, specialTelegraphColor * 1.5f);

        // 1. Pivote de cuerpo (respiración, inclinación, giros de golpeo)
        bodyPivot = CreateEmptyChild("BodyPivot", avatarRoot, Vector3.zero);

        // 2. Base / Pedestal estilizado deportivo (piernas/soporte sutil para dar anclaje visual en VR)
        Transform lowerStand = CreatePrimitivePart(
            "LowerBody", PrimitiveType.Capsule, bodyPivot,
            new Vector3(0f, 0.45f, 0f), Quaternion.identity, new Vector3(0.28f, 0.43f, 0.24f),
            darkJointMat);
        darkJointRenderers.Add(lowerStand.GetComponent<Renderer>());

        // 3. Torso estilizado + banda pectoral + hombrera + núcleo de energía
        torsoTransform = CreatePrimitivePart(
            "Torso", PrimitiveType.Capsule, bodyPivot,
            new Vector3(0f, TorsoCenterY, 0f), Quaternion.identity, new Vector3(0.42f, 0.34f, 0.28f),
            primaryMat);
        primaryRenderers.Add(torsoTransform.GetComponent<Renderer>());

        Transform chestBand = CreatePrimitivePart(
            "ChestAccentBand", PrimitiveType.Cube, torsoTransform,
            new Vector3(0f, 0.18f, 0.12f), Quaternion.identity, new Vector3(0.92f, 0.22f, 0.86f),
            secondaryMat);
        secondaryRenderers.Add(chestBand.GetComponent<Renderer>());

        Transform shoulderBar = CreatePrimitivePart(
            "ShoulderYoke", PrimitiveType.Capsule, torsoTransform,
            new Vector3(0f, 0.62f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.32f, 0.68f, 0.45f),
            darkJointMat);
        darkJointRenderers.Add(shoulderBar.GetComponent<Renderer>());

        Transform chestCore = CreatePrimitivePart(
            "ChestCoreEmblem", PrimitiveType.Sphere, torsoTransform,
            new Vector3(0f, 0.18f, 0.54f), Quaternion.identity, new Vector3(0.22f, 0.22f, 0.14f),
            chestCoreMat);
        chestCoreRenderer = chestCore.GetComponent<Renderer>();

        // 4. Cabeza + Visor VR elegante + Ojos emisivos (siguen la pelota suavemente)
        headPivot = CreateEmptyChild("HeadPivot", bodyPivot, new Vector3(0f, HeadNeckY, 0f));

        Transform neck = CreatePrimitivePart(
            "Neck", PrimitiveType.Cylinder, headPivot,
            new Vector3(0f, -0.04f, 0f), Quaternion.identity, new Vector3(0.11f, 0.06f, 0.11f),
            darkJointMat);
        darkJointRenderers.Add(neck.GetComponent<Renderer>());

        Transform headMesh = CreatePrimitivePart(
            "Head", PrimitiveType.Sphere, headPivot,
            new Vector3(0f, 0.11f, 0f), Quaternion.identity, new Vector3(0.26f, 0.28f, 0.27f),
            primaryMat);
        primaryRenderers.Add(headMesh.GetComponent<Renderer>());

        Transform headCrest = CreatePrimitivePart(
            "HeadCrest", PrimitiveType.Cube, headPivot,
            new Vector3(0f, 0.23f, -0.01f), Quaternion.identity, new Vector3(0.055f, 0.06f, 0.22f),
            secondaryMat);
        secondaryRenderers.Add(headCrest.GetComponent<Renderer>());

        Transform visor = CreatePrimitivePart(
            "VRVisor", PrimitiveType.Cube, headPivot,
            new Vector3(0f, 0.12f, 0.105f), Quaternion.identity, new Vector3(0.24f, 0.095f, 0.10f),
            visorGlassMat);
        visorGlassRenderer = visor.GetComponent<Renderer>();

        Transform eyeLeft = CreatePrimitivePart(
            "VisorEyeLeft", PrimitiveType.Cube, visor,
            new Vector3(-0.24f, 0.04f, 0.50f), Quaternion.identity, new Vector3(0.30f, 0.34f, 0.08f),
            visorEyeMat);
        visorEyeRenderers.Add(eyeLeft.GetComponent<Renderer>());

        Transform eyeRight = CreatePrimitivePart(
            "VisorEyeRight", PrimitiveType.Cube, visor,
            new Vector3(0.24f, 0.04f, 0.50f), Quaternion.identity, new Vector3(0.30f, 0.34f, 0.08f),
            visorEyeMat);
        visorEyeRenderers.Add(eyeRight.GetComponent<Renderer>());

        // 5. Rig de Hombro / Brazo conectado a aiPaddle (2 segmentos + articulación de codo)
        float paddleSideSign = paddleOnRightHand ? 1f : -1f;
        rightShoulderPivot = CreateEmptyChild(
            "PaddleShoulderPivot", bodyPivot,
            new Vector3(ShoulderHalfWidth * paddleSideSign, ShoulderHeightY, 0.02f));

        Transform rightShoulderCap = CreatePrimitivePart(
            "PaddleShoulderCap", PrimitiveType.Sphere, rightShoulderPivot,
            Vector3.zero, Quaternion.identity, new Vector3(0.13f, 0.13f, 0.13f),
            secondaryMat);
        secondaryRenderers.Add(rightShoulderCap.GetComponent<Renderer>());

        upperArmTransform = CreatePrimitivePart(
            "UpperArm", PrimitiveType.Capsule, rightShoulderPivot,
            new Vector3(0f, -UpperArmLength * 0.5f, 0f), Quaternion.identity, new Vector3(0.095f, UpperArmLength * 0.5f, 0.095f),
            primaryMat);
        primaryRenderers.Add(upperArmTransform.GetComponent<Renderer>());

        elbowJointTransform = CreatePrimitivePart(
            "ElbowJoint", PrimitiveType.Sphere, rightShoulderPivot,
            new Vector3(0f, -UpperArmLength, 0f), Quaternion.identity, new Vector3(0.09f, 0.09f, 0.09f),
            darkJointMat);
        darkJointRenderers.Add(elbowJointTransform.GetComponent<Renderer>());

        forearmTransform = CreatePrimitivePart(
            "Forearm", PrimitiveType.Capsule, rightShoulderPivot,
            new Vector3(0f, -(UpperArmLength + ForearmLength * 0.5f), 0f), Quaternion.identity, new Vector3(0.085f, ForearmLength * 0.5f, 0.085f),
            secondaryMat);
        secondaryRenderers.Add(forearmTransform.GetComponent<Renderer>());

        // 6. Brazo secundario de balance (silueta atlética equilibrada)
        leftShoulderPivot = CreateEmptyChild(
            "BalanceShoulderPivot", bodyPivot,
            new Vector3(-ShoulderHalfWidth * paddleSideSign, ShoulderHeightY, 0.01f));

        Transform leftShoulderCap = CreatePrimitivePart(
            "BalanceShoulderCap", PrimitiveType.Sphere, leftShoulderPivot,
            Vector3.zero, Quaternion.identity, new Vector3(0.12f, 0.12f, 0.12f),
            secondaryMat);
        secondaryRenderers.Add(leftShoulderCap.GetComponent<Renderer>());

        leftArmTransform = CreatePrimitivePart(
            "BalanceArm", PrimitiveType.Capsule, leftShoulderPivot,
            new Vector3(-0.04f * paddleSideSign, -0.26f, 0.06f),
            Quaternion.Euler(14f, 0f, -12f * paddleSideSign),
            new Vector3(0.09f, 0.28f, 0.09f),
            primaryMat);
        primaryRenderers.Add(leftArmTransform.GetComponent<Renderer>());

        // 7. Aro de Aura de Dificultad en el suelo (anillo hueco de 12 segmentos)
        difficultyAuraRoot = CreateEmptyChild("DifficultyAuraRing", avatarRoot, new Vector3(0f, FloorRingY, 0f));
        BuildSegmentedRing(
            difficultyAuraRoot, "AuraSeg", 12, 0.44f,
            new Vector3(0.20f, 0.025f, 0.048f), auraMat, difficultyAuraRenderers);

        // 8. Halo de Telegrafiado / Carga de Poder detrás de los hombros (anillo hueco de 8 segmentos)
        telegraphHaloRoot = CreateEmptyChild("TelegraphHalo", bodyPivot, new Vector3(0f, HaloBaseHeightY, -0.18f));
        telegraphHaloRoot.localRotation = Quaternion.Euler(90f, 0f, 0f);
        BuildSegmentedRing(
            telegraphHaloRoot, "HaloSeg", 8, 0.34f,
            new Vector3(0.22f, 0.032f, 0.038f), haloMat, telegraphHaloRenderers);

        PositionAvatarRootAtBaseline(immediate: true);
        UpdateArmRigToPaddle();
        HideHostPrimitiveMeshIfRequested();
        UpdateHaloVisuals(0f, forceEditSafe: !Application.isPlaying);
        ApplyThemeFromRivalSafe();
    }

    private void ApplyThemeFromRivalSafe()
    {
        if (rivalAI == null)
        {
            ApplyTheme(null, null);
            return;
        }

        if (Application.isPlaying)
        {
            ApplyTheme(rivalAI.ActiveProfile, rivalAI.ActiveDifficultyProfile);
            return;
        }

        RivalProfile editProfile = rivalAI.rivalProfile;
        RivalDifficultyProfile editDiff = rivalAI.ActiveDifficultyProfile;
        if (editDiff == null && editProfile != null)
        {
            switch (rivalAI.CurrentDifficulty)
            {
                case RivalDifficulty.Easy:
                    editDiff = editProfile.easyProfile;
                    break;
                case RivalDifficulty.Hard:
                    editDiff = editProfile.hardProfile;
                    break;
                default:
                    editDiff = editProfile.mediumProfile;
                    break;
            }
        }
        ApplyTheme(editProfile, editDiff);
    }

    /// <summary>
    /// Actualiza los colores de tema del rival (primario, secundario y acento de dificultad).
    /// Seguro en Edit Mode y Play Mode.
    /// </summary>
    public void ApplyTheme(RivalProfile profile, RivalDifficultyProfile difficultyProfile)
    {
        if (profile != null)
        {
            currentPrimaryColor = profile.primaryColor;
            currentSecondaryColor = profile.secondaryColor;
        }

        if (difficultyProfile != null)
        {
            currentDifficultyAccent = difficultyProfile.accentColor;
        }
        else if (rivalAI != null)
        {
            currentDifficultyAccent = GetFallbackDifficultyColor(rivalAI.CurrentDifficulty);
        }

        ApplyColorToRendererGroup(primaryRenderers, currentPrimaryColor, Color.black);
        ApplyColorToRendererGroup(secondaryRenderers, currentSecondaryColor, Color.black);
        ApplyColorToRendererGroup(visorEyeRenderers, currentDifficultyAccent, currentDifficultyAccent * 1.45f);
        ApplyColorToRendererGroup(difficultyAuraRenderers, currentDifficultyAccent, currentDifficultyAccent * 1.25f);

        if (visorGlassRenderer != null)
        {
            ApplyColorToRenderer(visorGlassRenderer, new Color(0.05f, 0.07f, 0.11f, 1f), currentDifficultyAccent * 0.25f);
        }

        if (chestCoreRenderer != null)
        {
            ApplyColorToRenderer(chestCoreRenderer, currentDifficultyAccent, currentDifficultyAccent * 1.15f);
        }
    }

    /// <summary>
    /// Reproduce una reacción animada de victoria (salto de celebración) o derrota (desplome) al finalizar el partido.
    /// </summary>
    public void PlayMatchResultReaction(bool rivalWon)
    {
        poseState = rivalWon ? ProceduralPoseState.MatchWin : ProceduralPoseState.MatchLose;
        poseTimer = 0f;
        poseDuration = 4f;

        if (rivalWon)
        {
            TriggerAnimator(animParamWin);
        }
        else
        {
            TriggerAnimator(animParamLose);
        }
    }

    /// <summary>
    /// Restaura el estado visual/animado del rival a postura neutral (útil al iniciar revancha).
    /// </summary>
    public void ResetVisualState()
    {
        poseState = ProceduralPoseState.Idle;
        poseTimer = 0f;
        telegraphFlashTimer = 0f;
        currentStepInX = 0f;
        currentTorsoTwistY = 0f;
        currentBodyLeanZ = 0f;
        currentBodyPitchX = 0f;
        currentPaddleEmissionStrength = 0f;
        ApplyPaddleEmission(Color.black, 0f);
        SetAnimatorBool(animParamPowerReady, isPowerReady);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying) return;
        if (avatarRoot == null) return;

        bool isMatchReaction = poseState == ProceduralPoseState.MatchWin || poseState == ProceduralPoseState.MatchLose;
        float dt = isMatchReaction
            ? (Time.deltaTime > 0.0001f ? Time.deltaTime : Time.unscaledDeltaTime)
            : Time.deltaTime;

        if (dt <= 0f) return;

        if (!hasCapturedPaddleBaseRotation && GetEffectivePaddleTransform() != null)
        {
            CapturePaddleBaseTransformAndMaterial();
        }

        UpdatePoseTimers(dt);
        UpdateAvatarBodyPositionAndLean(dt);
        UpdateHeadBallTracking(dt);
        UpdatePaddleProceduralRotation(dt);
        UpdateArmRigToPaddle();
        UpdateTelegraphAndPowerVisuals(dt);
        UpdateAnimatorLocomotion(dt);
    }

    // --- Suscripción a eventos de RivalAIController ---

    private void SubscribeToRivalEvents()
    {
        if (isSubscribed || rivalAI == null) return;

        rivalAI.OnDifficultyApplied += HandleDifficultyApplied;
        rivalAI.OnShotTelegraphed += HandleShotTelegraphed;
        rivalAI.OnShotExecuted += HandleShotExecuted;
        rivalAI.OnServeStarted += HandleServeStarted;
        rivalAI.OnPowerStateChanged += HandlePowerStateChanged;
        rivalAI.OnMissTriggered += HandleMissTriggered;
        isSubscribed = true;
    }

    private void UnsubscribeFromRivalEvents()
    {
        if (!isSubscribed || rivalAI == null) return;

        rivalAI.OnDifficultyApplied -= HandleDifficultyApplied;
        rivalAI.OnShotTelegraphed -= HandleShotTelegraphed;
        rivalAI.OnShotExecuted -= HandleShotExecuted;
        rivalAI.OnServeStarted -= HandleServeStarted;
        rivalAI.OnPowerStateChanged -= HandlePowerStateChanged;
        rivalAI.OnMissTriggered -= HandleMissTriggered;
        isSubscribed = false;
    }

    private void SyncInitialStateFromRival()
    {
        if (rivalAI == null) return;

        PositionAvatarRootAtBaseline(immediate: true);
        ApplyTheme(rivalAI.ActiveProfile, rivalAI.ActiveDifficultyProfile);
        isPowerReady = rivalAI.IsPowerReady;
        powerCharge01 = rivalAI.PowerCharge01;
        SetAnimatorBool(animParamPowerReady, isPowerReady);
        UpdateHaloVisuals(0f, forceEditSafe: false);
    }

    private void HandleDifficultyApplied(RivalProfile profile, RivalDifficultyProfile difficultyProfile)
    {
        if (poseState == ProceduralPoseState.MatchWin || poseState == ProceduralPoseState.MatchLose)
        {
            ResetVisualState();
        }
        ApplyTheme(profile, difficultyProfile);
    }

    private void HandleShotTelegraphed(ArcadeShotType shotType, SpecialPowerType specialPower, float leadTime)
    {
        activeShotType = shotType;
        activeSpecialPower = specialPower;
        activeIsServe = false;
        poseState = ProceduralPoseState.TelegraphBackswing;
        poseTimer = 0f;
        poseDuration = Mathf.Max(leadTime, 0.16f);

        if (shotType == ArcadeShotType.Smash || shotType == ArcadeShotType.Slice || shotType == ArcadeShotType.Special)
        {
            activeTelegraphColor = GetShotTelegraphColor(shotType);
            telegraphFlashDuration = Mathf.Max(leadTime + 0.18f, 0.35f);
            telegraphFlashTimer = telegraphFlashDuration;

            PlayShotTelegraphAudio(shotType);
        }
    }

    private void HandleShotExecuted(ArcadeShotType shotType, SpecialPowerType specialPower, bool isServe)
    {
        activeShotType = shotType;
        activeSpecialPower = specialPower;
        activeIsServe = isServe;
        poseState = ProceduralPoseState.StrikeFollowThrough;
        poseTimer = 0f;
        poseDuration = shotType == ArcadeShotType.Special
            ? strikeFollowThroughDuration * 1.35f
            : strikeFollowThroughDuration;

        // Destello breve en impacto
        activeTelegraphColor = GetShotTelegraphColor(shotType);
        float impactFlash = shotType == ArcadeShotType.Drive ? 0.14f : 0.28f;
        telegraphFlashDuration = impactFlash;
        telegraphFlashTimer = Mathf.Max(telegraphFlashTimer, impactFlash);

        if (shotType == ArcadeShotType.Special)
        {
            PlaySfx(specialStrikeClip != null ? specialStrikeClip : strikeClip, 1f);
            TriggerAnimator(animParamSpecial);
        }
        else if (shotType == ArcadeShotType.Smash)
        {
            PlaySfx(strikeClip, 1f);
            TriggerAnimator(animParamSmash);
        }
        else if (shotType == ArcadeShotType.Slice)
        {
            PlaySfx(strikeClip, 0.85f);
            TriggerAnimator(animParamSlice);
        }
        else
        {
            PlaySfx(strikeClip, 0.75f);
        }

        TriggerAnimator(animParamSwing);
    }

    private void HandleServeStarted()
    {
        poseState = ProceduralPoseState.ServeWindup;
        poseTimer = 0f;
        poseDuration = 1.6f;
        TriggerAnimator(animParamServe);
    }

    private void HandlePowerStateChanged(int current, int required, bool ready)
    {
        bool becameReady = ready && !isPowerReady;
        isPowerReady = ready;
        powerCharge01 = required > 0 ? Mathf.Clamp01((float)current / required) : 0f;

        if ((poseState == ProceduralPoseState.MatchWin || poseState == ProceduralPoseState.MatchLose) && current == 0 && !ready)
        {
            ResetVisualState();
        }

        SetAnimatorBool(animParamPowerReady, isPowerReady);

        if (becameReady)
        {
            PlaySfx(powerReadyClip, 0.9f);
        }
    }

    private void HandleMissTriggered()
    {
        poseState = ProceduralPoseState.MissWobble;
        poseTimer = 0f;
        poseDuration = missWobbleDuration;
    }

    // --- Actualización de Animaciones Procedurales ---

    private void UpdatePoseTimers(float dt)
    {
        if (poseState == ProceduralPoseState.Idle) return;

        poseTimer += dt;

        if (poseState == ProceduralPoseState.ServeWindup)
        {
            if (rivalAI != null && !rivalAI.IsServing && poseTimer > 0.25f)
            {
                poseState = ProceduralPoseState.Idle;
                poseTimer = 0f;
            }
            return;
        }

        if (poseState == ProceduralPoseState.MatchWin || poseState == ProceduralPoseState.MatchLose)
        {
            if (poseTimer > 0.25f && rivalAI != null && rivalAI.referee != null && !rivalAI.referee.IsMatchOver)
            {
                ResetVisualState();
            }
            return;
        }

        if (poseTimer >= poseDuration)
        {
            poseState = ProceduralPoseState.Idle;
            poseTimer = 0f;
        }
    }

    private void UpdateAvatarBodyPositionAndLean(float dt)
    {
        float sideMult = GetEffectiveSideMultiplier();
        Transform paddle = GetEffectivePaddleTransform();

        float anchorX = GetAvatarBaselineWorldX(sideMult);
        float centerZ = rivalAI != null ? rivalAI.StartPosition.z : avatarBaseAnchorWorld.z;
        float desiredZ = centerZ;

        if (paddle != null)
        {
            float deltaZ = Mathf.Clamp(paddle.position.z - centerZ, -maxLateralFollowOffset, maxLateralFollowOffset);
            desiredZ = centerZ + deltaZ * 0.82f;
        }

        currentAvatarZ = Mathf.Lerp(currentAvatarZ, desiredZ, 1f - Mathf.Exp(-bodyFollowSpeed * dt));

        // Paso adelante sutil durante golpe / paso atrás durante backswing
        float targetStepIn = 0f;
        float targetTwist = 0f;
        float poseNorm = poseDuration > 0.001f ? Mathf.Clamp01(poseTimer / poseDuration) : 0f;
        float paddleSideSign = paddleOnRightHand ? 1f : -1f;

        switch (poseState)
        {
            case ProceduralPoseState.TelegraphBackswing:
                float windCurve = Mathf.SmoothStep(0f, 1f, poseNorm);
                targetStepIn = (activeShotType == ArcadeShotType.Special ? -0.14f : -0.09f) * windCurve;
                targetTwist = (activeShotType == ArcadeShotType.Special ? 32f : 22f) * paddleSideSign * windCurve;
                break;

            case ProceduralPoseState.ServeWindup:
                float serveCurve = Mathf.Sin(Mathf.Min(poseNorm * 1.4f, 1f) * Mathf.PI * 0.5f);
                targetStepIn = -0.08f * serveCurve;
                targetTwist = 18f * paddleSideSign * serveCurve;
                break;

            case ProceduralPoseState.StrikeFollowThrough:
                float punchEnv = Mathf.Sin(poseNorm * Mathf.PI);
                float strikeScale = activeShotType == ArcadeShotType.Special ? 1.45f
                    : (activeShotType == ArcadeShotType.Smash ? 1.25f : 0.9f);
                targetStepIn = 0.14f * strikeScale * punchEnv;
                targetTwist = -28f * strikeScale * paddleSideSign * punchEnv;
                break;

            case ProceduralPoseState.MissWobble:
                targetStepIn = -0.06f * Mathf.Sin(poseNorm * Mathf.PI);
                targetTwist = Mathf.Sin(poseNorm * Mathf.PI * 3f) * 18f * (1f - poseNorm);
                break;

            case ProceduralPoseState.MatchWin:
                targetTwist = Mathf.Sin(poseTimer * 3.5f) * 16f;
                break;
        }

        currentStepInX = Mathf.Lerp(currentStepInX, targetStepIn, 1f - Mathf.Exp(-16f * dt));
        currentTorsoTwistY = Mathf.Lerp(currentTorsoTwistY, targetTwist, 1f - Mathf.Exp(-18f * dt));

        Vector3 targetWorldPos = new Vector3(
            anchorX + sideMult * currentStepInX,
            avatarFloorY,
            currentAvatarZ
        );

        avatarRoot.position = targetWorldPos;

        Vector3 forwardDir = new Vector3(sideMult, 0f, 0f);
        avatarRoot.rotation = Quaternion.LookRotation(forwardDir, Vector3.up);

        if (bodyPivot == null) return;

        // Velocidad lateral en espacio local del rival (+X es derecha del rival)
        Vector3 rivalVel = rivalAI != null ? rivalAI.CurrentVelocity : Vector3.zero;
        Vector3 rivalRight = Vector3.Cross(Vector3.up, forwardDir).normalized;
        float localRightSpeed = Vector3.Dot(rivalVel, rivalRight);
        float localForwardSpeed = Vector3.Dot(rivalVel, forwardDir);

        float targetLeanZ = Mathf.Clamp(-localRightSpeed * 2.8f, -maxMovementLeanAngle, maxMovementLeanAngle);
        float targetPitchX = Mathf.Clamp(localForwardSpeed * 1.8f, -8f, 10f);

        float bobY = Mathf.Sin(Time.time * idleBreathSpeed) * idleBreathAmplitude;
        float extraPitchX = 0f;
        float extraRollZ = 0f;

        if (poseState == ProceduralPoseState.StrikeFollowThrough)
        {
            float punchEnv = Mathf.Sin(poseNorm * Mathf.PI);
            if (activeShotType == ArcadeShotType.Smash || activeShotType == ArcadeShotType.Special)
            {
                extraPitchX += 14f * punchEnv;
            }
        }
        else if (poseState == ProceduralPoseState.MissWobble)
        {
            extraRollZ += Mathf.Sin(poseNorm * Mathf.PI * 4f) * 14f * (1f - poseNorm);
            extraPitchX += 10f * Mathf.Sin(poseNorm * Mathf.PI);
        }
        else if (poseState == ProceduralPoseState.MatchWin)
        {
            float jumpPhase = Mathf.Abs(Mathf.Sin(poseTimer * 5.2f));
            bobY = jumpPhase * 0.16f;
            extraPitchX = -6f * jumpPhase;
        }
        else if (poseState == ProceduralPoseState.MatchLose)
        {
            float slump = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(poseTimer * 1.6f));
            bobY = -0.10f * slump;
            extraPitchX = 24f * slump;
            extraRollZ = 6f * Mathf.Sin(poseTimer * 1.4f) * slump;
        }

        currentBodyLeanZ = Mathf.Lerp(currentBodyLeanZ, targetLeanZ + extraRollZ, 1f - Mathf.Exp(-12f * dt));
        currentBodyPitchX = Mathf.Lerp(currentBodyPitchX, targetPitchX + extraPitchX, 1f - Mathf.Exp(-12f * dt));

        bodyPivot.localPosition = new Vector3(0f, bobY, 0f);
        bodyPivot.localRotation = Quaternion.Euler(currentBodyPitchX, currentTorsoTwistY, currentBodyLeanZ);

        // Animar brazo secundario de balance
        if (leftShoulderPivot != null)
        {
            float balanceSwing = Mathf.Clamp(localRightSpeed * 4f, -18f, 18f);
            float winRaise = poseState == ProceduralPoseState.MatchWin
                ? -125f - Mathf.Abs(Mathf.Sin(poseTimer * 5.2f)) * 22f
                : (poseState == ProceduralPoseState.MatchLose ? 18f : -currentTorsoTwistY * 0.45f);
            leftShoulderPivot.localRotation = Quaternion.Euler(winRaise, 0f, balanceSwing);
        }
    }

    private void UpdateHeadBallTracking(float dt)
    {
        if (headPivot == null || bodyPivot == null) return;

        Quaternion targetLocalRot = Quaternion.identity;

        if (poseState == ProceduralPoseState.MatchLose)
        {
            float slump = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(poseTimer * 1.8f));
            targetLocalRot = Quaternion.Euler(26f * slump, 0f, 0f);
        }
        else if (poseState == ProceduralPoseState.MatchWin)
        {
            targetLocalRot = Quaternion.Euler(-12f, Mathf.Sin(poseTimer * 4.2f) * 18f, 0f);
        }
        else if (rivalAI != null && rivalAI.ball != null)
        {
            Vector3 toBallWorld = rivalAI.ball.position - headPivot.position;
            if (toBallWorld.sqrMagnitude > 0.002f)
            {
                Vector3 localDir = bodyPivot.InverseTransformDirection(toBallWorld.normalized);
                float yaw = Mathf.Atan2(localDir.x, Mathf.Max(0.15f, localDir.z)) * Mathf.Rad2Deg;
                float horizLen = Mathf.Sqrt(localDir.x * localDir.x + localDir.z * localDir.z);
                float pitch = -Mathf.Atan2(localDir.y, Mathf.Max(0.15f, horizLen)) * Mathf.Rad2Deg;

                yaw = Mathf.Clamp(yaw, -headMaxYaw, headMaxYaw);
                pitch = Mathf.Clamp(pitch, -headMaxPitch, headMaxPitch);

                targetLocalRot = Quaternion.Euler(pitch, yaw, 0f);
            }
        }

        currentHeadLocalRot = Quaternion.Slerp(currentHeadLocalRot, targetLocalRot, 1f - Mathf.Exp(-headTrackSpeed * dt));
        headPivot.localRotation = currentHeadLocalRot;
    }

    private void UpdatePaddleProceduralRotation(float dt)
    {
        Transform paddle = GetEffectivePaddleTransform();
        if (paddle == null || !hasCapturedPaddleBaseRotation) return;

        float sideMult = GetEffectiveSideMultiplier();
        Vector3 forward = new Vector3(sideMult, 0f, 0f);
        Vector3 rivalRight = Vector3.Cross(Vector3.up, forward).normalized;
        float paddleSideSign = paddleOnRightHand ? 1f : -1f;

        float pitchDeg = 0f;
        float yawDeg = 0f;
        float rollDeg = 0f;
        float norm = poseDuration > 0.001f ? Mathf.Clamp01(poseTimer / poseDuration) : 0f;

        switch (poseState)
        {
            case ProceduralPoseState.Idle:
                // Inclinación sutil hacia la pelota y movimiento
                Vector3 vel = rivalAI != null ? rivalAI.CurrentVelocity : Vector3.zero;
                yawDeg = Mathf.Clamp(Vector3.Dot(vel, rivalRight) * 3.5f, -14f, 14f);
                pitchDeg = Mathf.Sin(Time.time * idleBreathSpeed) * 2.5f;
                break;

            case ProceduralPoseState.ServeWindup:
                float servePhase = Mathf.SmoothStep(0f, 1f, Mathf.Min(norm * 1.5f, 1f));
                pitchDeg = -22f * servePhase;
                yawDeg = 28f * paddleSideSign * servePhase;
                rollDeg = -14f * servePhase;
                break;

            case ProceduralPoseState.TelegraphBackswing:
                float wind = Mathf.SmoothStep(0f, 1f, norm);
                switch (activeShotType)
                {
                    case ArcadeShotType.Smash:
                        pitchDeg = -46f * wind;
                        yawDeg = 22f * paddleSideSign * wind;
                        break;
                    case ArcadeShotType.Slice:
                        pitchDeg = 20f * wind;
                        yawDeg = 38f * paddleSideSign * wind;
                        rollDeg = 42f * paddleSideSign * wind;
                        break;
                    case ArcadeShotType.Special:
                        pitchDeg = -52f * wind;
                        yawDeg = 44f * paddleSideSign * wind;
                        rollDeg = -18f * paddleSideSign * wind;
                        break;
                    default:
                        pitchDeg = -16f * wind;
                        yawDeg = 22f * paddleSideSign * wind;
                        break;
                }
                break;

            case ProceduralPoseState.StrikeFollowThrough:
                // Pico rápido en impacto (norm ~0.22) y retorno suave
                float snapEnv = norm < 0.25f
                    ? Mathf.SmoothStep(0f, 1f, norm / 0.25f)
                    : 1f - Mathf.SmoothStep(0f, 1f, (norm - 0.25f) / 0.75f);

                switch (activeShotType)
                {
                    case ArcadeShotType.Smash:
                        pitchDeg = 54f * snapEnv;
                        yawDeg = -32f * paddleSideSign * snapEnv;
                        break;
                    case ArcadeShotType.Slice:
                        pitchDeg = 26f * snapEnv;
                        yawDeg = -46f * paddleSideSign * snapEnv;
                        rollDeg = 52f * paddleSideSign * snapEnv;
                        break;
                    case ArcadeShotType.Special:
                        pitchDeg = 62f * snapEnv;
                        yawDeg = -50f * paddleSideSign * snapEnv;
                        rollDeg = 24f * paddleSideSign * snapEnv;
                        break;
                    default:
                        pitchDeg = (activeIsServe ? 32f : 28f) * snapEnv;
                        yawDeg = -26f * paddleSideSign * snapEnv;
                        break;
                }
                break;

            case ProceduralPoseState.MissWobble:
                float wobble = Mathf.Sin(norm * Mathf.PI * 3.5f) * (1f - norm);
                pitchDeg = 24f * Mathf.Sin(norm * Mathf.PI);
                rollDeg = 28f * wobble;
                break;

            case ProceduralPoseState.MatchWin:
                pitchDeg = -34f + Mathf.Sin(poseTimer * 7f) * 16f;
                yawDeg = Mathf.Sin(poseTimer * 5f) * 20f;
                break;

            case ProceduralPoseState.MatchLose:
                float droop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(poseTimer * 1.5f));
                pitchDeg = 48f * droop;
                rollDeg = -18f * droop;
                break;
        }

        Quaternion deltaWorld =
            Quaternion.AngleAxis(yawDeg, Vector3.up) *
            Quaternion.AngleAxis(pitchDeg, rivalRight) *
            Quaternion.AngleAxis(rollDeg, forward);

        Quaternion targetRot = deltaWorld * paddleBaseWorldRotation;
        float rotSharpness = poseState == ProceduralPoseState.StrikeFollowThrough ? 26f : 14f;
        paddle.rotation = Quaternion.Slerp(paddle.rotation, targetRot, 1f - Mathf.Exp(-rotSharpness * dt));
    }

    private void UpdateArmRigToPaddle()
    {
        if (rightShoulderPivot == null || upperArmTransform == null || forearmTransform == null) return;

        Transform paddle = GetEffectivePaddleTransform();
        if (paddle == null) return;

        Vector3 shoulderWorld = rightShoulderPivot.position;
        Vector3 handWorld = paddle.position;
        Vector3 toHand = handWorld - shoulderWorld;
        float dist = toHand.magnitude;
        if (dist < 0.005f) return;

        Vector3 dir = toHand / dist;

        // Cinemática inversa analítica ligera de 2 huesos (Hombro -> Codo -> Mano/Pala)
        float maxNaturalReach = UpperArmLength + ForearmLength;
        float upperLen = UpperArmLength;
        float lowerLen = ForearmLength;

        if (dist > maxNaturalReach * 0.98f)
        {
            // Estiramiento limpio proporcional si la pala alcanza muy lejos para nunca desconectarse
            float ratio = dist / maxNaturalReach;
            upperLen *= ratio;
            lowerLen *= ratio;
        }

        float clampedDist = Mathf.Clamp(dist, Mathf.Abs(upperLen - lowerLen) + 0.01f, upperLen + lowerLen - 0.001f);
        float cosAngle = Mathf.Clamp(
            (upperLen * upperLen + clampedDist * clampedDist - lowerLen * lowerLen) / (2f * upperLen * clampedDist),
            -1f, 1f);
        float sinAngle = Mathf.Sqrt(Mathf.Max(0f, 1f - cosAngle * cosAngle));

        // Dirección natural de flexión del codo: hacia abajo y ligeramente hacia afuera del cuerpo
        Vector3 sideOut = avatarRoot != null
            ? avatarRoot.right * (paddleOnRightHand ? 0.45f : -0.45f)
            : Vector3.right * 0.45f;
        Vector3 bendPole = (Vector3.down + sideOut).normalized;
        Vector3 bendAxis = bendPole - dir * Vector3.Dot(bendPole, dir);
        if (bendAxis.sqrMagnitude < 0.0001f)
        {
            bendAxis = Vector3.down;
        }
        else
        {
            bendAxis.Normalize();
        }

        Vector3 elbowWorld = shoulderWorld + dir * (upperLen * cosAngle) + bendAxis * (upperLen * sinAngle);

        OrientCapsuleSegment(upperArmTransform, shoulderWorld, elbowWorld, 0.095f);
        if (elbowJointTransform != null)
        {
            elbowJointTransform.position = elbowWorld;
        }
        OrientCapsuleSegment(forearmTransform, elbowWorld, handWorld, 0.085f);
    }

    private void OrientCapsuleSegment(Transform segment, Vector3 startWorld, Vector3 endWorld, float diameter)
    {
        Vector3 delta = endWorld - startWorld;
        float length = delta.magnitude;
        if (length < 0.001f) return;

        segment.position = (startWorld + endWorld) * 0.5f;
        segment.rotation = Quaternion.FromToRotation(Vector3.up, delta / length);

        // En Unity, una Capsule primitiva mide 2m de alto a escala Y = 1
        Vector3 parentLossy = segment.parent != null ? segment.parent.lossyScale : Vector3.one;
        float safeX = Mathf.Max(0.001f, Mathf.Abs(parentLossy.x));
        float safeY = Mathf.Max(0.001f, Mathf.Abs(parentLossy.y));
        float safeZ = Mathf.Max(0.001f, Mathf.Abs(parentLossy.z));
        segment.localScale = new Vector3(
            diameter / safeX,
            (length * 0.5f) / safeY,
            diameter / safeZ
        );
    }

    private void UpdateTelegraphAndPowerVisuals(float dt)
    {
        if (telegraphFlashTimer > 0f)
        {
            telegraphFlashTimer = Mathf.Max(0f, telegraphFlashTimer - dt);
        }

        UpdateHaloVisuals(dt, forceEditSafe: false);
    }

    private void UpdateHaloVisuals(float dt, bool forceEditSafe)
    {
        float telegraph01 = (telegraphFlashDuration > 0.001f && telegraphFlashTimer > 0f)
            ? Mathf.Clamp01(telegraphFlashTimer / telegraphFlashDuration)
            : 0f;

        float pulseFast = 0.5f + 0.5f * Mathf.Sin(Time.time * 16f);
        float pulseSlow = 0.5f + 0.5f * Mathf.Sin(Time.time * 4.5f);

        // 1. Halo de Telegrafiado / Poder Listo
        if (telegraphHaloRoot != null)
        {
            bool showHalo = telegraph01 > 0.01f || isPowerReady;
            float targetScale;
            Color haloColor;
            float haloIntensity;

            if (telegraph01 > 0.01f)
            {
                targetScale = Mathf.Lerp(1.0f, 1.48f, telegraph01) + pulseFast * 0.14f * telegraph01;
                haloColor = activeTelegraphColor;
                haloIntensity = Mathf.Lerp(1.1f, telegraphEmissionIntensity, telegraph01) * (0.85f + 0.35f * pulseFast);
            }
            else if (isPowerReady)
            {
                targetScale = 1.05f + pulseSlow * 0.12f;
                haloColor = specialTelegraphColor;
                haloIntensity = powerReadyEmissionIntensity * (0.75f + 0.35f * pulseSlow);
            }
            else
            {
                targetScale = 0.001f;
                haloColor = currentDifficultyAccent;
                haloIntensity = 0f;
            }

            if (forceEditSafe)
            {
                telegraphHaloRoot.gameObject.SetActive(false);
            }
            else
            {
                if (telegraphHaloRoot.gameObject.activeSelf != showHalo)
                {
                    telegraphHaloRoot.gameObject.SetActive(showHalo);
                }

                if (showHalo)
                {
                    telegraphHaloRoot.localScale = Vector3.one * targetScale;
                    float spinSpeed = telegraph01 > 0.01f ? 220f : 65f;
                    telegraphHaloRoot.Rotate(0f, spinSpeed * dt, 0f, Space.Self);
                    ApplyColorToRendererGroup(telegraphHaloRenderers, haloColor, haloColor * haloIntensity);
                }
            }
        }

        // 2. Aro de Aura de Dificultad en el suelo
        if (difficultyAuraRoot != null && !forceEditSafe)
        {
            float auraScale = 1f + (isPowerReady ? pulseSlow * 0.08f : pulseSlow * 0.025f);
            if (telegraph01 > 0.01f) auraScale += telegraph01 * 0.12f;
            difficultyAuraRoot.localScale = new Vector3(auraScale, 1f, auraScale);

            Color auraBase = telegraph01 > 0.01f
                ? Color.Lerp(currentDifficultyAccent, activeTelegraphColor, telegraph01 * 0.7f)
                : (isPowerReady ? Color.Lerp(currentDifficultyAccent, specialTelegraphColor, 0.35f + 0.25f * pulseSlow) : currentDifficultyAccent);

            float auraEmit = telegraph01 > 0.01f
                ? Mathf.Lerp(1.2f, 2.4f, telegraph01)
                : (isPowerReady ? 1.6f + 0.5f * pulseSlow : 1.0f + 0.25f * pulseSlow);

            ApplyColorToRendererGroup(difficultyAuraRenderers, auraBase, auraBase * auraEmit);
        }

        // 3. Núcleo pectoral (indica progreso de carga de poder)
        if (chestCoreRenderer != null && !forceEditSafe)
        {
            float coreScale = Mathf.Lerp(0.18f, 0.28f, isPowerReady ? 1f : powerCharge01);
            chestCoreRenderer.transform.localScale = new Vector3(coreScale, coreScale, 0.14f);

            Color coreCol = isPowerReady
                ? specialTelegraphColor
                : Color.Lerp(currentDifficultyAccent, specialTelegraphColor, powerCharge01 * 0.65f);
            float coreEmit = isPowerReady ? (2.0f + 0.6f * pulseSlow) : Mathf.Lerp(0.6f, 1.5f, powerCharge01);
            ApplyColorToRenderer(chestCoreRenderer, coreCol, coreCol * coreEmit);
        }

        // 4. Visor / Ojos (brillan con color de telegrafiado al preparar tiro especial)
        if (!forceEditSafe && visorEyeRenderers.Count > 0)
        {
            Color eyeCol = telegraph01 > 0.01f
                ? Color.Lerp(currentDifficultyAccent, activeTelegraphColor, telegraph01)
                : (isPowerReady ? Color.Lerp(currentDifficultyAccent, specialTelegraphColor, 0.45f) : currentDifficultyAccent);
            float eyeEmit = telegraph01 > 0.01f ? 2.6f : (isPowerReady ? 1.9f : 1.4f);
            ApplyColorToRendererGroup(visorEyeRenderers, eyeCol, eyeCol * eyeEmit);
        }

        // 5. Emisión en la pala del rival (telegrafiado + aura de poder armado)
        if (!forceEditSafe)
        {
            Color targetPaddleCol = Color.black;
            float targetPaddleEmit = 0f;

            if (telegraph01 > 0.01f)
            {
                targetPaddleCol = activeTelegraphColor;
                targetPaddleEmit = telegraphEmissionIntensity * telegraph01 * (0.75f + 0.35f * pulseFast);
            }
            else if (isPowerReady)
            {
                targetPaddleCol = specialTelegraphColor;
                targetPaddleEmit = 0.75f + 0.45f * pulseSlow;
            }

            if (Mathf.Abs(targetPaddleEmit - currentPaddleEmissionStrength) > 0.01f || targetPaddleCol != currentPaddleEmissionColor)
            {
                currentPaddleEmissionStrength = targetPaddleEmit;
                currentPaddleEmissionColor = targetPaddleCol;
                ApplyPaddleEmission(targetPaddleCol, targetPaddleEmit);
            }
        }
    }

    // --- Hooks de Animator 3D ---

    private void CacheAnimatorParameters()
    {
        if (characterAnimator == null || characterAnimator == cachedValidatedAnimator) return;

        cachedValidatedAnimator = characterAnimator;
        validAnimatorParamHashes.Clear();

        AnimatorControllerParameter[] parameters = characterAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            validAnimatorParamHashes.Add(parameters[i].nameHash);
        }
    }

    private void UpdateAnimatorLocomotion(float dt)
    {
        if (characterAnimator == null) return;
        CacheAnimatorParameters();

        int hash = Animator.StringToHash(animParamMoveX);
        if (!validAnimatorParamHashes.Contains(hash)) return;

        float sideMult = GetEffectiveSideMultiplier();
        Vector3 forward = new Vector3(sideMult, 0f, 0f);
        Vector3 rivalRight = Vector3.Cross(Vector3.up, forward).normalized;
        float speedRight = rivalAI != null ? Vector3.Dot(rivalAI.CurrentVelocity, rivalRight) : 0f;
        float moveXNormalized = Mathf.Clamp(speedRight / 4.5f, -1f, 1f);
        characterAnimator.SetFloat(hash, moveXNormalized, 0.08f, dt);
    }

    private void TriggerAnimator(string paramName)
    {
        if (characterAnimator == null || string.IsNullOrEmpty(paramName)) return;
        CacheAnimatorParameters();

        int hash = Animator.StringToHash(paramName);
        if (validAnimatorParamHashes.Contains(hash))
        {
            characterAnimator.SetTrigger(hash);
        }
    }

    private void SetAnimatorBool(string paramName, bool value)
    {
        if (characterAnimator == null || string.IsNullOrEmpty(paramName)) return;
        CacheAnimatorParameters();

        int hash = Animator.StringToHash(paramName);
        if (validAnimatorParamHashes.Contains(hash))
        {
            characterAnimator.SetBool(hash, value);
        }
    }

    // --- Audio ---

    private void PlayShotTelegraphAudio(ArcadeShotType shotType)
    {
        AudioClip clip = null;
        switch (shotType)
        {
            case ArcadeShotType.Smash:
                clip = smashTelegraphClip;
                break;
            case ArcadeShotType.Slice:
                clip = sliceTelegraphClip;
                break;
            case ArcadeShotType.Special:
                clip = specialTelegraphClip != null ? specialTelegraphClip : smashTelegraphClip;
                break;
        }
        PlaySfx(clip, 1f);
    }

    private void PlaySfx(AudioClip clip, float localScale)
    {
        if (clip == null) return;

        if (audioSource == null && Application.isPlaying)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0.85f;
            }
        }

        if (audioSource != null)
        {
            float vol = Mathf.Clamp01(MenuManager.sfxVolume * sfxGain * localScale);
            if (vol > 0.001f)
            {
                audioSource.PlayOneShot(clip, vol);
            }
        }
    }

    // --- Utilidades de Geometría, Materiales URP y Posicionamiento ---

    private void CapturePaddleBaseTransformAndMaterial()
    {
        Transform paddle = GetEffectivePaddleTransform();
        if (paddle == null) return;

        paddleBaseWorldRotation = paddle.rotation;
        hasCapturedPaddleBaseRotation = true;

        if (!Application.isPlaying) return;

        cachedPaddleRenderer = paddle.GetComponentInChildren<Renderer>();
        if (cachedPaddleRenderer != null && cachedPaddleRenderer.sharedMaterial != null)
        {
            originalPaddleSharedMaterial = cachedPaddleRenderer.sharedMaterial;
            runtimePaddleMaterial = new Material(originalPaddleSharedMaterial);
            runtimePaddleMaterial.name = originalPaddleSharedMaterial.name + "_RivalRuntime";
            runtimePaddleMaterial.EnableKeyword("_EMISSION");
            runtimePaddleMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            cachedPaddleRenderer.sharedMaterial = runtimePaddleMaterial;
        }
    }

    private void ApplyPaddleEmission(Color color, float intensity)
    {
        if (!Application.isPlaying || runtimePaddleMaterial == null) return;

        if (runtimePaddleMaterial.HasProperty(EmissionColorId))
        {
            runtimePaddleMaterial.SetColor(EmissionColorId, color * intensity);
        }
    }

    private Transform GetEffectivePaddleTransform()
    {
        if (paddleVisualPivot != null) return paddleVisualPivot;
        return rivalAI != null ? rivalAI.aiPaddle : null;
    }

    private float GetEffectiveSideMultiplier()
    {
        if (rivalAI != null)
        {
            if (Application.isPlaying && Mathf.Abs(rivalAI.SideMultiplier) > 0.01f)
            {
                return Mathf.Sign(rivalAI.SideMultiplier);
            }

            if (rivalAI.playerTableTarget != null)
            {
                Collider col = rivalAI.playerTableTarget.GetComponent<Collider>();
                float playerCenterX = col != null ? col.bounds.center.x : rivalAI.playerTableTarget.position.x;
                if (Mathf.Abs(playerCenterX - rivalAI.baselineX) > 0.05f)
                {
                    return playerCenterX > rivalAI.baselineX ? 1f : -1f;
                }
            }

            return rivalAI.baselineX < 0f ? 1f : -1f;
        }
        return -1f;
    }

    private float GetAvatarBaselineWorldX(float sideMult)
    {
        if (rivalAI != null)
        {
            return rivalAI.baselineX - (sideMult * avatarBackOffset);
        }
        return transform.position.x - (sideMult * avatarBackOffset);
    }

    private void PositionAvatarRootAtBaseline(bool immediate)
    {
        if (avatarRoot == null) return;

        float sideMult = GetEffectiveSideMultiplier();
        float anchorX = GetAvatarBaselineWorldX(sideMult);

        float anchorZ = transform.position.z;
        Transform paddle = GetEffectivePaddleTransform();
        if (rivalAI != null && rivalAI.StartPosition != Vector3.zero)
        {
            anchorZ = rivalAI.StartPosition.z;
        }
        else if (paddle != null)
        {
            anchorZ = paddle.position.z;
        }

        avatarBaseAnchorWorld = new Vector3(anchorX, avatarFloorY, anchorZ);

        if (immediate)
        {
            currentAvatarZ = anchorZ;
            avatarRoot.position = avatarBaseAnchorWorld;
            avatarRoot.rotation = Quaternion.LookRotation(new Vector3(sideMult, 0f, 0f), Vector3.up);
        }
    }

    private Vector3 ComputeParentInverseScale()
    {
        Vector3 lossy = transform.lossyScale;
        return new Vector3(
            Mathf.Abs(lossy.x) > 0.001f ? 1f / lossy.x : 1f,
            Mathf.Abs(lossy.y) > 0.001f ? 1f / lossy.y : 1f,
            Mathf.Abs(lossy.z) > 0.001f ? 1f / lossy.z : 1f
        );
    }

    private void HideHostPrimitiveMeshIfRequested()
    {
        if (!hideHostPlaceholderMesh) return;

        MeshRenderer hostRenderer = GetComponent<MeshRenderer>();
        if (hostRenderer != null && GetComponent<MeshFilter>() != null)
        {
            hostRenderer.enabled = false;
        }
    }

    private void RebindAvatarHierarchyReferences()
    {
        primaryRenderers.Clear();
        secondaryRenderers.Clear();
        darkJointRenderers.Clear();
        visorEyeRenderers.Clear();
        difficultyAuraRenderers.Clear();
        telegraphHaloRenderers.Clear();
        chestCoreRenderer = null;
        visorGlassRenderer = null;

        if (avatarRoot == null) return;

        bodyPivot = avatarRoot.Find("BodyPivot");
        if (bodyPivot != null)
        {
            torsoTransform = bodyPivot.Find("Torso");
            headPivot = bodyPivot.Find("HeadPivot");
            rightShoulderPivot = bodyPivot.Find("PaddleShoulderPivot");
            leftShoulderPivot = bodyPivot.Find("BalanceShoulderPivot");
            telegraphHaloRoot = bodyPivot.Find("TelegraphHalo");

            if (rightShoulderPivot != null)
            {
                upperArmTransform = rightShoulderPivot.Find("UpperArm");
                elbowJointTransform = rightShoulderPivot.Find("ElbowJoint");
                forearmTransform = rightShoulderPivot.Find("Forearm");
            }

            if (leftShoulderPivot != null)
            {
                leftArmTransform = leftShoulderPivot.Find("BalanceArm");
            }
        }

        difficultyAuraRoot = avatarRoot.Find("DifficultyAuraRing");

        Renderer[] allRenderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < allRenderers.Length; i++)
        {
            Renderer r = allRenderers[i];
            string n = r.gameObject.name;

            if (n == "Torso" || n == "Head" || n == "UpperArm" || n == "BalanceArm")
            {
                primaryRenderers.Add(r);
            }
            else if (n == "ChestAccentBand" || n == "HeadCrest" || n == "PaddleShoulderCap" || n == "BalanceShoulderCap" || n == "Forearm")
            {
                secondaryRenderers.Add(r);
            }
            else if (n == "LowerBody" || n == "ShoulderYoke" || n == "Neck" || n == "ElbowJoint")
            {
                darkJointRenderers.Add(r);
            }
            else if (n == "VRVisor")
            {
                visorGlassRenderer = r;
            }
            else if (n == "VisorEyeLeft" || n == "VisorEyeRight")
            {
                visorEyeRenderers.Add(r);
            }
            else if (n == "ChestCoreEmblem")
            {
                chestCoreRenderer = r;
            }
            else if (n.StartsWith("AuraSeg"))
            {
                difficultyAuraRenderers.Add(r);
            }
            else if (n.StartsWith("HaloSeg"))
            {
                telegraphHaloRenderers.Add(r);
            }
        }
    }

    private static Transform CreateEmptyChild(string name, Transform parent, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        Transform t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        return t;
    }

    private Transform CreatePrimitivePart(
        string name,
        PrimitiveType type,
        Transform parent,
        Vector3 localPos,
        Quaternion localRot,
        Vector3 localScale,
        Material sharedMat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;

        // Quitar Collider para no interferir jamás con la pelota ni el árbitro
        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        Transform t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        t.localRotation = localRot;
        t.localScale = localScale;

        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null && sharedMat != null)
        {
            rend.sharedMaterial = sharedMat;
        }

        return t;
    }

    private void BuildSegmentedRing(
        Transform ringParent,
        string prefix,
        int segmentCount,
        float radius,
        Vector3 segmentScale,
        Material sharedMat,
        List<Renderer> targetList)
    {
        for (int i = 0; i < segmentCount; i++)
        {
            float angleRad = (i / (float)segmentCount) * Mathf.PI * 2f;
            Vector3 localPos = new Vector3(Mathf.Cos(angleRad) * radius, 0f, Mathf.Sin(angleRad) * radius);
            Quaternion localRot = Quaternion.Euler(0f, -angleRad * Mathf.Rad2Deg + 90f, 0f);

            Transform seg = CreatePrimitivePart(
                $"{prefix}_{i}", PrimitiveType.Cube, ringParent,
                localPos, localRot, segmentScale, sharedMat);

            Renderer r = seg.GetComponent<Renderer>();
            if (r != null) targetList.Add(r);
        }
    }

    private Material CreateAvatarMaterial(string matName, Color baseColor, float metallic, float smoothness, Color emissionColor)
    {
        Shader shader = FindSafeURPShader();
        Material mat = new Material(shader);
        mat.name = matName;

        ConfigureMaterialSurface(mat, baseColor, metallic, smoothness, emissionColor);

        if (Application.isPlaying)
        {
            runtimeCreatedMaterials.Add(mat);
        }

        return mat;
    }

    private static Shader FindSafeURPShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null) return shader;

        shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader != null) return shader;

        shader = Shader.Find("Standard");
        if (shader != null) return shader;

        return Shader.Find("Sprites/Default");
    }

    private static void ConfigureMaterialSurface(Material mat, Color baseColor, float metallic, float smoothness, Color emissionColor)
    {
        if (mat == null) return;

        if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, baseColor);
        if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, baseColor);
        if (mat.HasProperty(MetallicId)) mat.SetFloat(MetallicId, metallic);
        if (mat.HasProperty(SmoothnessId)) mat.SetFloat(SmoothnessId, smoothness);
        if (mat.HasProperty(GlossinessId)) mat.SetFloat(GlossinessId, smoothness);

        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (mat.HasProperty(EmissionColorId))
        {
            mat.SetColor(EmissionColorId, emissionColor);
        }
    }

    private void ApplyColorToRendererGroup(List<Renderer> renderers, Color baseColor, Color emissionColor)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            ApplyColorToRenderer(renderers[i], baseColor, emissionColor);
        }
    }

    private void ApplyColorToRenderer(Renderer r, Color baseColor, Color emissionColor)
    {
        if (r == null) return;

        if (!Application.isPlaying)
        {
            // En Edit Mode usamos sharedMaterial (cero fugas de instancias)
            Material shared = r.sharedMaterial;
            if (shared != null)
            {
                if (shared.HasProperty(BaseColorId)) shared.SetColor(BaseColorId, baseColor);
                if (shared.HasProperty(ColorId)) shared.SetColor(ColorId, baseColor);
                shared.EnableKeyword("_EMISSION");
                if (shared.HasProperty(EmissionColorId)) shared.SetColor(EmissionColorId, emissionColor);
            }
            return;
        }

        if (sharedPropBlock == null) sharedPropBlock = new MaterialPropertyBlock();

        r.GetPropertyBlock(sharedPropBlock);
        sharedPropBlock.SetColor(BaseColorId, baseColor);
        sharedPropBlock.SetColor(ColorId, baseColor);
        sharedPropBlock.SetColor(EmissionColorId, emissionColor);
        r.SetPropertyBlock(sharedPropBlock);
    }

    private Color GetShotTelegraphColor(ArcadeShotType shotType)
    {
        switch (shotType)
        {
            case ArcadeShotType.Smash:
                return smashTelegraphColor;
            case ArcadeShotType.Slice:
                return sliceTelegraphColor;
            case ArcadeShotType.Special:
                return specialTelegraphColor;
            default:
                return driveFlashColor;
        }
    }

    private static Color GetFallbackDifficultyColor(RivalDifficulty difficulty)
    {
        switch (difficulty)
        {
            case RivalDifficulty.Easy:
                return new Color(0.18f, 0.90f, 0.55f, 1f);
            case RivalDifficulty.Hard:
                return new Color(0.95f, 0.18f, 0.35f, 1f);
            default:
                return new Color(1.0f, 0.68f, 0.15f, 1f);
        }
    }

    private static void DestroyMaterialSafe(Material mat)
    {
        if (mat == null) return;
        if (Application.isPlaying) Destroy(mat);
        else DestroyImmediate(mat);
    }
}
