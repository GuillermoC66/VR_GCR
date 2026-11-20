using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Controlador principal de IA para rivales de PingPing PongPong VR.
/// Evoluciona SimplePingPongAI con perfiles de dificultad (Fácil / Medio / Difícil),
/// predicción balística de intercepción, colocación táctica anti-jugador, telegrafiado
/// de tiros, respeto estricto a las reglas de rebote de MatchReferee y Poder Especial activo
/// en las tres dificultades.
/// </summary>
public class RivalAIController : MonoBehaviour
{
    [Header("Referencias de Juego")]
    public Transform ball;
    public Rigidbody ballRb;
    public Transform aiPaddle;
    public MatchReferee referee;
    [Tooltip("Centro de la mitad de la mesa del jugador. Si está vacío se busca por tag 'TablePlayer'.")]
    public Transform playerTableTarget;

    [Header("Identidad y Dificultad del Rival")]
    public RivalProfile rivalProfile;
    public RivalDifficulty currentDifficulty = RivalDifficulty.Medium;

    [Header("Configuración de Cancha (Eje X)")]
    [Tooltip("Posición X base donde el rival espera la pelota.")]
    public float baselineX = 1.0f;
    [Tooltip("Distancia en X a la que el rival inicia el avance de golpeo.")]
    public float strikeDistance = 0.6f;

    [Header("Parámetros de Mesa y Devolución Determinista")]
    public float playerTableHalfWidth = 0.55f;
    public float playerTableHalfLength = 0.45f;
    public float netHeightAboveTable = 0.155f;
    public bool useDeterministicReturns = true;

    // --- Propiedades Públicas (Contrato Compartido) ---
    public RivalProfile ActiveProfile => rivalProfile != null ? rivalProfile : EnsureRuntimeFallbackProfile();
    public RivalDifficulty CurrentDifficulty => currentDifficulty;
    public RivalDifficultyProfile ActiveDifficultyProfile { get; private set; }
    public int CurrentPowerCharge { get; private set; }
    public int PowerChargeRequired => ActiveDifficultyProfile != null ? ActiveDifficultyProfile.powerHitsRequired : 6;
    public bool IsPowerReady { get; private set; }
    public float PowerCharge01 => PowerChargeRequired > 0 ? Mathf.Clamp01((float)CurrentPowerCharge / PowerChargeRequired) : 0f;
    public float SideMultiplier { get; private set; } = -1f;
    public Vector3 StartPosition { get; private set; }
    public Vector3 CurrentVelocity { get; private set; }
    public bool IsServing { get; private set; }

    // --- Eventos Públicos (Visuales, Audio y HUD) ---
    public event Action<RivalProfile, RivalDifficultyProfile> OnDifficultyApplied;
    public event Action<ArcadeShotType, SpecialPowerType, float> OnShotTelegraphed;
    public event Action<ArcadeShotType, SpecialPowerType, bool> OnShotExecuted;
    public event Action OnServeStarted;
    public event Action<int, int, bool> OnPowerStateChanged;
    public event Action OnMissTriggered;

    // --- Estado Interno ---
    private RivalProfile runtimeFallbackProfile;
    private BallController ballController;
    private BallReporter ballReporter;
    private Transform enemyTableTarget;
    private Collider playerTableCollider;
    private Collider enemyTableCollider;
    private Transform cachedPlayerHead;

    private Vector3 targetPosition;
    private bool hasCapturedStartPosition;
    private bool isMatchActive = true;
    private float netX;
    private float lastAiHitTime = -10f;

    // Estado por intercambio (rally)
    private bool wasMovingTowardsAI;
    private bool wasCrossedNet;
    private float incomingStartTime = -10f;
    private float netCrossTime = -10f;
    private bool hasIncomingShotInfo;
    private ArcadeShotResult incomingPlayerShot;
    private bool hasPlannedThisRally;
    private bool hasTelegraphedThisRally;
    private bool hasTriggeredMissThisRally;
    private bool willMissThisRally;
    private float currentErrorZ;
    private float currentErrorY;
    private ArcadeShotType plannedShotType = ArcadeShotType.Drive;
    private SpecialPowerType plannedSpecialPower = SpecialPowerType.FastballSmash;

    // Estado de saque
    private Coroutine serveCoroutine;
    private float activeServeMoveSpeed = 16f;

    private const float HitCooldownSeconds = 0.35f;

    private void Awake()
    {
        SideMultiplier = (baselineX < 0f) ? 1f : -1f;

        if (aiPaddle != null)
        {
            StartPosition = aiPaddle.position;
            targetPosition = StartPosition;
            hasCapturedStartPosition = true;
        }

        ResolveActiveProfiles();
    }

    private void OnEnable()
    {
        PaddleFlickController.OnArcadeShotExecuted += HandlePlayerArcadeShot;
    }

    private void OnDisable()
    {
        PaddleFlickController.OnArcadeShotExecuted -= HandlePlayerArcadeShot;
        StopServeCoroutine();
    }

    private void OnDestroy()
    {
        if (runtimeFallbackProfile != null)
        {
            Destroy(runtimeFallbackProfile);
            runtimeFallbackProfile = null;
        }
    }

    private void Start()
    {
        CacheSceneReferences();

        if (!hasCapturedStartPosition && aiPaddle != null)
        {
            StartPosition = aiPaddle.position;
            targetPosition = StartPosition;
            hasCapturedStartPosition = true;
        }

        ComputeCourtGeometry();
        ConfigureDeterministicPaddleColliders();
        ResolveActiveProfiles();

        OnDifficultyApplied?.Invoke(ActiveProfile, ActiveDifficultyProfile);
        OnPowerStateChanged?.Invoke(CurrentPowerCharge, PowerChargeRequired, IsPowerReady);
    }

    private void Update()
    {
        if (aiPaddle == null || ball == null || ballRb == null) return;

        if (!isMatchActive || Time.timeScale <= 0f || (referee != null && referee.IsMatchOver))
        {
            CurrentVelocity = Vector3.zero;
            return;
        }

        RivalDifficultyProfile diff = ActiveDifficultyProfile != null ? ActiveDifficultyProfile : ResolveActiveProfiles();
        float speedMult = ActiveProfile.speedMultiplier;

        // --- Fase de Pausa entre Puntos / Sets ---
        if (referee != null && !referee.IsPlayInputAllowed())
        {
            ResetRallyDecisionState();
            targetPosition = StartPosition;
            MovePaddleTowards(targetPosition, diff.returnToCenterSpeed * speedMult);
            return;
        }

        // --- Fase de Saque ---
        if (IsServing)
        {
            MovePaddleTowards(targetPosition, activeServeMoveSpeed * speedMult);
            return;
        }

        if (referee != null && referee.IsAIReadyToServe())
        {
            serveCoroutine = StartCoroutine(ServeRoutine());
            return;
        }

        // Si estamos esperando el saque del jugador (aún sin golpear), volver suavemente al centro
        if (referee != null && referee.CurrentPhase == MatchReferee.PointPhase.WaitingForServe)
        {
            ResetRallyDecisionState();
            targetPosition = StartPosition;
            MovePaddleTowards(targetPosition, diff.returnToCenterSpeed * speedMult);
            return;
        }

        // --- Evaluación de Trayectoria Entrante ---
        bool isMovingTowardsAI = IsBallMovingTowardsAI();
        bool hasCrossedNet = HasBallCrossedNet();

        if (isMovingTowardsAI)
        {
            if (!wasMovingTowardsAI)
            {
                incomingStartTime = Time.time;
                if (!hasPlannedThisRally)
                {
                    PlanRallyResponse(diff);
                }
            }

            if (hasCrossedNet && !wasCrossedNet)
            {
                netCrossTime = Time.time;
                if (!hasPlannedThisRally)
                {
                    PlanRallyResponse(diff);
                }
            }

            bool canTrackNow = CanStartTracking(diff, hasCrossedNet);

            if (canTrackNow)
            {
                Vector3 interceptTarget = ComputeInterceptionTarget(diff, hasCrossedNet);
                targetPosition = interceptTarget;

                // Telegrafiado antes del impacto cuando la pelota ya se aproxima
                TryTriggerTelegraph(diff, hasCrossedNet);

                float distXToPaddle = Mathf.Abs(ball.position.x - aiPaddle.position.x);
                bool hasBouncedOnAI = ballController == null || ballController.HasBouncedOnAISide;
                bool isRallyLegalToHit = referee == null || referee.CurrentPhase == MatchReferee.PointPhase.RallyInProgress;
                bool canStrikeBall = hasBouncedOnAI && isRallyLegalToHit;

                if (willMissThisRally)
                {
                    // Fallo justo: deja un leve hueco lateral y dispara el evento de fallo al pasar la zona de golpeo
                    if (!hasTriggeredMissThisRally && canStrikeBall && distXToPaddle < strikeDistance * 0.65f)
                    {
                        hasTriggeredMissThisRally = true;
                        OnMissTriggered?.Invoke();
                    }
                }
                else
                {
                    // Avance de golpeo hacia la red solo cuando la bola ya botó en su lado y es legal devolverla
                    if (distXToPaddle < strikeDistance && canStrikeBall)
                    {
                        targetPosition.x = baselineX + (0.38f * SideMultiplier);
                    }

                    // IMPORTANTE (Reglas MatchReferee): NUNCA devolver de volea antes de que bote en el lado de la IA.
                    if (useDeterministicReturns && ballController != null && canStrikeBall &&
                        !ballController.IsInHitstop && !ballController.IsGrabbed &&
                        Time.time - lastAiHitTime > HitCooldownSeconds)
                    {
                        float distToBall = Vector3.Distance(aiPaddle.position, ball.position);
                        float effectiveReach = diff.reachRadius;

                        // Tolerancia vertical/lateral de 1 frame si la bola cruza justo el plano X de la pala
                        Vector2 planarOffset = new Vector2(
                            ball.position.y - aiPaddle.position.y,
                            ball.position.z - aiPaddle.position.z
                        );
                        bool passedBaselineWithinReach = distXToPaddle <= 0.24f && planarOffset.magnitude <= effectiveReach * 1.1f;

                        if (distToBall <= effectiveReach || passedBaselineWithinReach)
                        {
                            ExecuteDeterministicReturn(diff);
                        }
                    }
                }

                MovePaddleTowards(targetPosition, diff.paddleMoveSpeed * speedMult);
            }
            else
            {
                // Aún en tiempo de reacción o esperando cruce de red: posición de guardia
                targetPosition = StartPosition;
                MovePaddleTowards(targetPosition, diff.returnToCenterSpeed * speedMult);
            }
        }
        else
        {
            if (wasMovingTowardsAI)
            {
                ResetRallyDecisionState();
            }

            targetPosition = StartPosition;
            MovePaddleTowards(targetPosition, diff.returnToCenterSpeed * speedMult);
        }

        wasMovingTowardsAI = isMovingTowardsAI;
        wasCrossedNet = hasCrossedNet;
    }

    // --- API Pública de Control ---

    public void SetDifficulty(RivalDifficulty difficulty)
    {
        currentDifficulty = difficulty;
        ResolveActiveProfiles();

        if (CurrentPowerCharge >= PowerChargeRequired)
        {
            CurrentPowerCharge = PowerChargeRequired;
            IsPowerReady = true;
        }
        else
        {
            IsPowerReady = false;
        }

        OnDifficultyApplied?.Invoke(ActiveProfile, ActiveDifficultyProfile);
        OnPowerStateChanged?.Invoke(CurrentPowerCharge, PowerChargeRequired, IsPowerReady);
    }

    public void SetRival(RivalProfile profile, RivalDifficulty difficulty)
    {
        rivalProfile = profile;
        SetDifficulty(difficulty);
    }

    public void ResetForNewMatch()
    {
        StopServeCoroutine();
        ResetRallyDecisionState();

        CurrentPowerCharge = 0;
        IsPowerReady = false;
        lastAiHitTime = -10f;
        isMatchActive = true;

        ResolveActiveProfiles();

        if (aiPaddle != null && hasCapturedStartPosition)
        {
            aiPaddle.position = StartPosition;
            targetPosition = StartPosition;
        }
        CurrentVelocity = Vector3.zero;

        OnDifficultyApplied?.Invoke(ActiveProfile, ActiveDifficultyProfile);
        OnPowerStateChanged?.Invoke(CurrentPowerCharge, PowerChargeRequired, IsPowerReady);
    }

    public void ResetForNewPoint()
    {
        StopServeCoroutine();
        ResetRallyDecisionState();

        if (hasCapturedStartPosition)
        {
            targetPosition = StartPosition;
        }
    }

    public void SetMatchActive(bool active)
    {
        isMatchActive = active;
        if (!active)
        {
            StopServeCoroutine();
            ResetRallyDecisionState();
            CurrentVelocity = Vector3.zero;
        }
    }

    // --- Suscripción al Golpe del Jugador ---

    private void HandlePlayerArcadeShot(ArcadeShotResult result)
    {
        if (!isMatchActive || (referee != null && referee.IsMatchOver)) return;

        incomingPlayerShot = result;
        hasIncomingShotInfo = true;
        incomingStartTime = Time.time;

        // Planificamos de inmediato la respuesta a este intercambio usando los datos reales del golpe entrante
        RivalDifficultyProfile diff = ActiveDifficultyProfile != null ? ActiveDifficultyProfile : ResolveActiveProfiles();
        PlanRallyResponse(diff);
    }

    // --- Planificación Táctica por Rally ---

    private void PlanRallyResponse(RivalDifficultyProfile diff)
    {
        hasPlannedThisRally = true;
        hasTelegraphedThisRally = false;
        hasTriggeredMissThisRally = false;

        // 1. Evaluar probabilidad de fallo justa (vulnerabilidad ante Smash, Special y ángulos extremos)
        float missProb = diff.baseMissChance;

        if (hasIncomingShotInfo)
        {
            if (incomingPlayerShot.shotType == ArcadeShotType.Smash)
            {
                missProb += diff.extraMissVsSmash;
            }
            else if (incomingPlayerShot.shotType == ArcadeShotType.Special)
            {
                missProb += diff.extraMissVsSpecial;
            }

            float lateralOffset = Mathf.Abs(incomingPlayerShot.landingTarget.z - StartPosition.z);
            bool isExtremeAngle = lateralOffset > playerTableHalfWidth * 0.72f ||
                                  incomingPlayerShot.timing == TimingTier.MuyAdelantado ||
                                  incomingPlayerShot.timing == TimingTier.MuyAtrasado;
            if (isExtremeAngle)
            {
                missProb += diff.extraMissVsExtremeAngle;
            }
        }

        missProb = Mathf.Clamp(missProb, 0f, 0.85f);
        willMissThisRally = UnityEngine.Random.value < missProb;

        if (willMissThisRally)
        {
            // Desvío deliberado para que el fallo se vea físico y natural en VR
            float missSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            currentErrorZ = missSide * (diff.reachRadius + 0.22f);
            currentErrorY = UnityEngine.Random.Range(-0.14f, 0.14f);
        }
        else
        {
            currentErrorZ = UnityEngine.Random.Range(-diff.positionErrorMargin, diff.positionErrorMargin);
            currentErrorY = UnityEngine.Random.Range(-diff.positionErrorMargin * 0.7f, diff.positionErrorMargin * 0.7f);
        }

        // 2. Decidir el tipo de tiro de devolución (incluye Poder Especial activo en las 3 dificultades)
        plannedSpecialPower = ActiveProfile.specialPower;

        if (IsPowerReady && UnityEngine.Random.value < diff.powerUseProbabilityWhenReady)
        {
            plannedShotType = ArcadeShotType.Special;
            return;
        }

        float driveWeight = Mathf.Max(0f, diff.driveChance);
        float sliceWeight = Mathf.Max(0f, diff.sliceChance);
        float smashWeight = Mathf.Max(0f, diff.smashChance * ActiveProfile.aggressionMultiplier);
        float totalWeight = driveWeight + sliceWeight + smashWeight;

        if (totalWeight <= 0.001f)
        {
            plannedShotType = ArcadeShotType.Drive;
            return;
        }

        float roll = UnityEngine.Random.value * totalWeight;
        if (roll < smashWeight)
        {
            plannedShotType = ArcadeShotType.Smash;
        }
        else if (roll < smashWeight + sliceWeight)
        {
            plannedShotType = ArcadeShotType.Slice;
        }
        else
        {
            plannedShotType = ArcadeShotType.Drive;
        }
    }

    private void TryTriggerTelegraph(RivalDifficultyProfile diff, bool hasCrossedNet)
    {
        if (hasTelegraphedThisRally || willMissThisRally) return;

        float leadTime = plannedShotType == ArcadeShotType.Special
            ? diff.specialTelegraphLeadTime
            : (plannedShotType == ArcadeShotType.Drive ? 0.22f : Mathf.Clamp(diff.specialTelegraphLeadTime * 0.85f, 0.20f, 0.45f));

        float velXAbs = Mathf.Abs(ballRb.linearVelocity.x);
        float distX = Mathf.Abs(baselineX - ball.position.x);
        float estimatedTimeToBaseline = velXAbs > 0.25f ? distX / velXAbs : 99f;

        bool readyToTelegraph = estimatedTimeToBaseline <= leadTime ||
                                (hasCrossedNet && distX <= strikeDistance * 1.45f);

        if (readyToTelegraph)
        {
            hasTelegraphedThisRally = true;
            OnShotTelegraphed?.Invoke(plannedShotType, plannedSpecialPower, leadTime);
        }
    }

    // --- Predicción Balística de Intercepción ---

    private bool CanStartTracking(RivalDifficultyProfile diff, bool hasCrossedNet)
    {
        if (diff.predictBeforeNetCross && hasIncomingShotInfo)
        {
            return Time.time >= incomingStartTime + diff.reactionDelay;
        }

        if (!hasCrossedNet) return false;
        return Time.time >= netCrossTime + diff.reactionDelay;
    }

    private Vector3 ComputeInterceptionTarget(RivalDifficultyProfile diff, bool hasCrossedNet)
    {
        float minY = StartPosition.y - 0.22f;
        float maxY = StartPosition.y + 0.65f;
        float maxLateral = Mathf.Max(playerTableHalfWidth + 0.55f, 1.05f);

        float predictedZ = ball.position.z;
        float predictedY = ball.position.y;

        if (diff.useBallisticPrediction)
        {
            Vector3 vel = ballRb.linearVelocity;
            bool hasBounced = ballController != null && ballController.HasBouncedOnAISide;

            if (hasBounced)
            {
                // Post-rebote: trayectoria parabólica pura hacia baselineX
                float dx = baselineX - ball.position.x;
                if (Mathf.Abs(vel.x) > 0.25f && Mathf.Sign(dx) == Mathf.Sign(vel.x))
                {
                    float t = Mathf.Clamp(dx / vel.x, 0f, 0.75f);
                    float g = ballController != null ? ballController.StandardGravity : -9.81f;
                    predictedZ = ball.position.z + vel.z * t;
                    predictedY = ball.position.y + vel.y * t + 0.5f * g * t * t;
                }
            }
            else if (hasIncomingShotInfo)
            {
                // Pre-rebote (Medio post-red / Difícil anticipado): estimamos el cruce en baselineX tras botar en landingTarget
                Vector3 landing = incomingPlayerShot.landingTarget;
                float dxAfterBounce = baselineX - landing.x;
                Vector3 horizVel = new Vector3(vel.x, 0f, vel.z);

                if (Mathf.Abs(horizVel.x) > 0.25f && Mathf.Sign(dxAfterBounce) == Mathf.Sign(horizVel.x))
                {
                    float retention = incomingPlayerShot.shotType == ArcadeShotType.Slice ? 0.65f : 0.86f;
                    float postBounceVx = horizVel.x * retention;
                    float tAfterBounce = Mathf.Clamp(dxAfterBounce / postBounceVx, 0f, 0.75f);
                    predictedZ = landing.z + (horizVel.z * retention) * tAfterBounce;
                }
                else
                {
                    predictedZ = landing.z;
                }

                // Altura estimada cómoda de contacto tras el rebote
                float tableY = enemyTableTarget != null ? GetEnemyTableSurfaceCenter().y : (StartPosition.y - 0.25f);
                float expectedBounceLift = incomingPlayerShot.shotType == ArcadeShotType.Slice ? 0.24f : 0.38f;
                predictedY = tableY + expectedBounceLift;
            }
            else if (hasCrossedNet)
            {
                // Fallback balístico si no llegó evento de PaddleFlickController
                float dx = baselineX - ball.position.x;
                if (Mathf.Abs(vel.x) > 0.25f && Mathf.Sign(dx) == Mathf.Sign(vel.x))
                {
                    float t = Mathf.Clamp(dx / vel.x, 0f, 0.65f);
                    Vector3 curve = ballController != null ? ballController.CurrentCurveAccel : Vector3.zero;
                    predictedZ = ball.position.z + vel.z * t + 0.5f * curve.z * t * t;
                    predictedY = StartPosition.y + 0.12f;
                }
            }
        }

        float clampedZ = Mathf.Clamp(predictedZ + currentErrorZ, StartPosition.z - maxLateral, StartPosition.z + maxLateral);
        float clampedY = Mathf.Clamp(predictedY + currentErrorY, minY, maxY);

        return new Vector3(baselineX, clampedY, clampedZ);
    }

    // --- Ejecución Determinista de Devoluciones y Poderes Especiales ---

    private void ExecuteDeterministicReturn(RivalDifficultyProfile diff)
    {
        if (ballController == null || playerTableTarget == null) return;

        lastAiHitTime = Time.time;

        ArcadeShotType shotToExecute = plannedShotType;
        SpecialPowerType specialToExecute = plannedSpecialPower;

        Vector3 forwardDir = GetFlatDirectionToPlayerTable(ball.position);
        Vector3 rightDir = Vector3.Cross(Vector3.up, forwardDir).normalized;

        float speedMult = Mathf.Max(0.5f, ActiveProfile.speedMultiplier);
        float curveMult = Mathf.Max(0.5f, ActiveProfile.powerCurveMultiplier);
        float aggressionMult = Mathf.Max(0.5f, ActiveProfile.aggressionMultiplier);

        float flightTime = UnityEngine.Random.Range(diff.returnFlightTimeMin, diff.returnFlightTimeMax) / speedMult;
        float clearance = diff.netClearanceDrive;
        Vector3 curveAccel = Vector3.zero;
        float hitstop = 0f;
        float depth01 = UnityEngine.Random.Range(diff.placementDepthMin01, diff.placementDepthMax01);
        float antiPlayerWeight = diff.antiPlayerPlacementWeight;
        float widthSpread = diff.placementSpreadWidth01;

        float curveSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;

        switch (shotToExecute)
        {
            case ArcadeShotType.Smash:
                flightTime = (diff.returnFlightTimeMin * diff.smashFlightTimeMultiplier) / speedMult;
                clearance = diff.netClearanceSmash;
                depth01 = Mathf.Clamp(Mathf.Lerp(depth01, 0.82f, 0.55f) * aggressionMult, -0.2f, 0.90f);
                hitstop = 0.028f;
                break;

            case ArcadeShotType.Slice:
                flightTime = (diff.returnFlightTimeMax * 1.18f) / speedMult;
                clearance = diff.netClearanceSlice;
                depth01 = Mathf.Clamp(depth01 - 0.22f, -0.55f, 0.35f);
                curveAccel = rightDir * (curveSign * diff.sliceCurveStrength * curveMult);
                break;

            case ArcadeShotType.Special:
                hitstop = 0.045f;
                switch (specialToExecute)
                {
                    case SpecialPowerType.FastballSmash:
                        flightTime = Mathf.Clamp((0.28f * diff.specialFlightTimeMultiplier) / speedMult, 0.22f, 0.52f);
                        clearance = 0.09f;
                        depth01 = Mathf.Clamp(Mathf.Max(depth01, 0.68f), 0.45f, 0.88f);
                        antiPlayerWeight = Mathf.Max(antiPlayerWeight, 0.55f);
                        break;

                    case SpecialPowerType.CorteHuracan:
                        flightTime = Mathf.Clamp((0.56f * diff.specialFlightTimeMultiplier) / speedMult, 0.36f, 0.78f);
                        clearance = 0.34f;
                        depth01 = Mathf.Clamp(depth01, -0.15f, 0.55f);
                        curveAccel = rightDir * (curveSign * 7.2f * curveMult);
                        break;

                    case SpecialPowerType.SeismicPunch:
                        flightTime = Mathf.Clamp((0.42f * diff.specialFlightTimeMultiplier) / speedMult, 0.30f, 0.65f);
                        clearance = 0.22f;
                        depth01 = Mathf.Clamp(Mathf.Max(depth01, 0.45f), 0.25f, 0.78f);
                        break;

                    default: // BodhisattvaDefensivo
                        flightTime = Mathf.Clamp((0.46f * diff.specialFlightTimeMultiplier) / speedMult, 0.34f, 0.68f);
                        clearance = 0.26f;
                        depth01 = 0.72f;
                        antiPlayerWeight = Mathf.Max(antiPlayerWeight, 0.85f);
                        widthSpread = Mathf.Max(widthSpread, 0.85f);
                        break;
                }
                break;
        }

        Vector3 targetLanding = ComputeTacticalLandingPoint(forwardDir, rightDir, widthSpread, depth01, antiPlayerWeight);
        float netTopY = GetPlayerTableSurfaceCenter().y + netHeightAboveTable;

        ballController.LaunchBallistic(
            targetLanding,
            flightTime,
            netTopY,
            clearance,
            shotToExecute,
            curveAccel,
            specialToExecute,
            hitstop,
            MatchReferee.Hitter.AI
        );

        if (ballReporter != null)
        {
            float hitStrength = shotToExecute == ArcadeShotType.Special
                ? 1.0f
                : (shotToExecute == ArcadeShotType.Smash ? 0.92f : (shotToExecute == ArcadeShotType.Slice ? 0.50f : 0.68f));
            ballReporter.NotifyEnemyPaddleHit(hitStrength);
        }

        // Actualizar carga de poder del rival
        if (shotToExecute == ArcadeShotType.Special)
        {
            ConsumePower();
        }
        else
        {
            AddPowerCharge(diff.powerChargePerReturn);
        }

        OnShotExecuted?.Invoke(shotToExecute, specialToExecute, false);
        ResetRallyDecisionState();
    }

    private Vector3 ComputeTacticalLandingPoint(
        Vector3 forwardDir,
        Vector3 rightDir,
        float widthSpread01,
        float depth01,
        float antiPlayerWeight)
    {
        float randomLateral01 = UnityEngine.Random.Range(-1f, 1f);
        float finalLateral01 = randomLateral01;

        if (antiPlayerWeight > 0.001f)
        {
            float playerLateral01 = GetPlayerNormalizedLateralOffset(rightDir);
            float openCornerSign = Mathf.Abs(playerLateral01) < 0.14f
                ? (UnityEngine.Random.value < 0.5f ? -1f : 1f)
                : -Mathf.Sign(playerLateral01);

            float antiPlayerLateral01 = openCornerSign * UnityEngine.Random.Range(0.72f, 1.0f);
            finalLateral01 = Mathf.Lerp(randomLateral01, antiPlayerLateral01, Mathf.Clamp01(antiPlayerWeight));
        }

        float clampedWidth = Mathf.Clamp(finalLateral01 * widthSpread01, -0.96f, 0.96f) * playerTableHalfWidth;
        float clampedDepth = Mathf.Clamp(depth01, -0.75f, 0.90f) * playerTableHalfLength;

        return GetPlayerTableSurfaceCenter()
            + rightDir * clampedWidth
            + forwardDir * clampedDepth;
    }

    private float GetPlayerNormalizedLateralOffset(Vector3 rightDir)
    {
        if (cachedPlayerHead == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                cachedPlayerHead = mainCam.transform;
            }
            else
            {
                Camera anyCam = FindFirstObjectByType<Camera>();
                if (anyCam != null) cachedPlayerHead = anyCam.transform;
            }
        }

        if (cachedPlayerHead == null || playerTableTarget == null) return 0f;

        Vector3 offset = cachedPlayerHead.position - GetPlayerTableSurfaceCenter();
        float lateralMeters = Vector3.Dot(offset, rightDir);
        return Mathf.Clamp(lateralMeters / Mathf.Max(0.2f, playerTableHalfWidth), -1f, 1f);
    }

    // --- Corrutina de Saque Segura y Variada ---

    private IEnumerator ServeRoutine()
    {
        IsServing = true;
        ResetRallyDecisionState();

        RivalDifficultyProfile diff = ActiveDifficultyProfile != null ? ActiveDifficultyProfile : ResolveActiveProfiles();
        activeServeMoveSpeed = Mathf.Max(diff.paddleMoveSpeed, 6.5f);

        OnServeStarted?.Invoke();

        yield return new WaitForSeconds(diff.servePauseDuration);
        if (!CanContinueServe())
        {
            FinishServeCleanup();
            yield break;
        }

        // 1. Posicionar la pelota para el toss
        float serveZ = UnityEngine.Random.Range(-0.28f, 0.28f) * Mathf.Clamp01(diff.serveWidthSpread01 + 0.2f);
        Vector3 serveStartPos = new Vector3(
            baselineX + (0.30f * SideMultiplier),
            StartPosition.y + 0.28f,
            StartPosition.z + serveZ
        );

        if (ballController != null)
        {
            ballController.ResetAndFloat(serveStartPos);
            ballController.SetGrabbable(false);
        }
        else
        {
            ball.position = serveStartPos;
            ballRb.linearVelocity = Vector3.zero;
            ballRb.useGravity = false;
        }

        // 2. Retroceso de la pala para tomar impulso
        targetPosition = new Vector3(
            baselineX - (0.28f * SideMultiplier),
            StartPosition.y + 0.42f,
            StartPosition.z + serveZ
        );

        yield return new WaitForSeconds(0.50f);
        if (!CanContinueServe())
        {
            FinishServeCleanup();
            yield break;
        }

        // Elegir tipo de saque según dificultad
        bool willFault = UnityEngine.Random.value < diff.serveFaultChance;
        ArcadeShotType serveShotType = ArcadeShotType.Drive;
        float roll = UnityEngine.Random.value;
        if (!willFault)
        {
            if (roll < diff.serveFastChance)
            {
                serveShotType = ArcadeShotType.Smash;
            }
            else if (roll < diff.serveFastChance + diff.serveSliceChance)
            {
                serveShotType = ArcadeShotType.Slice;
            }
        }

        if (serveShotType != ArcadeShotType.Drive)
        {
            OnShotTelegraphed?.Invoke(serveShotType, ActiveProfile.specialPower, 0.35f);
        }

        // 3. El Toss
        if (ballController != null)
        {
            ballController.ReleaseForServe(new Vector3(0f, 1.75f, 0f));
        }
        else
        {
            ballRb.useGravity = true;
            ballRb.linearVelocity = new Vector3(0f, 1.75f, 0f);
        }

        yield return new WaitForSeconds(0.32f);
        if (!CanContinueServe())
        {
            FinishServeCleanup();
            yield break;
        }

        // 4. Golpe rápido de saque
        activeServeMoveSpeed = 18f;
        targetPosition = new Vector3(
            baselineX + (0.55f * SideMultiplier),
            StartPosition.y + 0.18f,
            StartPosition.z + serveZ
        );

        yield return new WaitForSeconds(0.05f);
        if (!CanContinueServe())
        {
            FinishServeCleanup();
            yield break;
        }

        // 5. Trayectoria balística de saque
        lastAiHitTime = Time.time;

        if (useDeterministicReturns && ballController != null && playerTableTarget != null)
        {
            Vector3 forwardDir = GetFlatDirectionToPlayerTable(ball.position);
            Vector3 rightDir = Vector3.Cross(Vector3.up, forwardDir).normalized;

            float speedMult = Mathf.Max(0.5f, ActiveProfile.speedMultiplier);
            float curveMult = Mathf.Max(0.5f, ActiveProfile.powerCurveMultiplier);

            Vector3 playerCenter = GetPlayerTableSurfaceCenter();
            float netTopY = playerCenter.y + netHeightAboveTable;

            float flightTime = UnityEngine.Random.Range(diff.serveFlightTimeMin, diff.serveFlightTimeMax) / speedMult;
            float clearance = diff.netClearanceDrive;
            Vector3 curveAccel = Vector3.zero;
            float depth01 = UnityEngine.Random.Range(-0.25f, 0.40f);
            Vector3 landing;

            if (willFault)
            {
                float faultLateral = UnityEngine.Random.Range(-0.45f, 0.45f) * playerTableHalfWidth;
                if (UnityEngine.Random.value < 0.5f)
                {
                    // Falta de saque larga: sobrepasa claramente el borde posterior de la mesa del jugador
                    float longDepthMeters = Mathf.Max(playerTableHalfLength * 1.85f, GetPlayerTableHalfLengthWorld(forwardDir) + 0.40f);
                    landing = playerCenter + rightDir * faultLateral + forwardDir * longDepthMeters;
                    clearance = 0.14f;
                }
                else
                {
                    // Falta de saque a la red: trayectoria rasa y corta directo contra la red
                    landing = new Vector3(netX, playerCenter.y + netHeightAboveTable * 0.45f, playerCenter.z + faultLateral * 0.4f);
                    flightTime = 0.22f;
                    clearance = -0.10f;
                }
            }
            else
            {
                if (serveShotType == ArcadeShotType.Smash)
                {
                    flightTime = (diff.serveFlightTimeMin * 0.82f) / speedMult;
                    clearance = diff.netClearanceSmash + 0.03f;
                    depth01 = UnityEngine.Random.Range(0.25f, 0.65f);
                }
                else if (serveShotType == ArcadeShotType.Slice)
                {
                    flightTime = (diff.serveFlightTimeMax * 1.15f) / speedMult;
                    clearance = diff.netClearanceSlice;
                    depth01 = UnityEngine.Random.Range(-0.45f, 0.10f);
                    float curveSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                    curveAccel = rightDir * (curveSign * diff.sliceCurveStrength * 0.85f * curveMult);
                }

                landing = ComputeTacticalLandingPoint(
                    forwardDir,
                    rightDir,
                    diff.serveWidthSpread01,
                    depth01,
                    diff.antiPlayerPlacementWeight * 0.6f
                );
            }

            // Saque oficial ITTF de 2 botes (1er bote en mesa del rival -> cruce sobre la red -> bote en mesa del jugador)
            if (enemyTableTarget != null)
            {
                Vector3 ownCenter = GetEnemyTableSurfaceCenter();
                float ownLateral = Mathf.Clamp(serveZ * 0.65f, -playerTableHalfWidth * 0.5f, playerTableHalfWidth * 0.5f);
                Vector3 ownBouncePos = ownCenter
                    + rightDir * ownLateral
                    - forwardDir * 0.12f;

                float firstLegTime = 0.18f;
                float secondLegTime = willFault && clearance < 0f
                    ? 0.24f
                    : Mathf.Clamp(flightTime * 0.90f, 0.30f, 0.95f);

                ballController.LaunchServeBallistic(
                    ownBouncePos,
                    landing,
                    firstLegTime,
                    secondLegTime,
                    netTopY,
                    clearance,
                    serveShotType,
                    curveAccel,
                    ActiveProfile.specialPower,
                    MatchReferee.Hitter.AI
                );
            }
            else
            {
                ballController.LaunchBallistic(
                    landing,
                    flightTime,
                    netTopY,
                    clearance,
                    serveShotType,
                    curveAccel,
                    ActiveProfile.specialPower,
                    0f,
                    MatchReferee.Hitter.AI
                );
            }

            if (ballReporter != null)
            {
                ballReporter.NotifyEnemyPaddleHit(serveShotType == ArcadeShotType.Smash ? 0.85f : 0.65f);
            }
        }
        else if (!willFault)
        {
            float randomAngZ = UnityEngine.Random.Range(-0.8f, 0.8f);
            ballRb.linearVelocity = new Vector3(3.5f * SideMultiplier, -2.5f, randomAngZ);
            if (ballReporter != null) ballReporter.NotifyEnemyPaddleHit(0.65f);
        }
        else
        {
            ballRb.linearVelocity = new Vector3(1.5f * SideMultiplier, -1.0f, 0f);
            if (ballReporter != null) ballReporter.NotifyEnemyPaddleHit(0.45f);
        }

        OnShotExecuted?.Invoke(serveShotType, ActiveProfile.specialPower, true);

        yield return new WaitForSeconds(0.26f);
        FinishServeCleanup();
    }

    private bool CanContinueServe()
    {
        return isMatchActive &&
               referee != null &&
               referee.IsAIReadyToServe();
    }

    private void FinishServeCleanup()
    {
        IsServing = false;
        serveCoroutine = null;
        if (hasCapturedStartPosition)
        {
            targetPosition = StartPosition;
        }
    }

    private void StopServeCoroutine()
    {
        if (serveCoroutine != null)
        {
            StopCoroutine(serveCoroutine);
            serveCoroutine = null;
        }
        IsServing = false;
    }

    // --- Gestión de Poder Especial ---

    private void AddPowerCharge(int amount)
    {
        if (amount <= 0) return;

        int required = PowerChargeRequired;
        int nextCharge = Mathf.Min(CurrentPowerCharge + amount, required);
        bool nextReady = nextCharge >= required;

        if (nextCharge != CurrentPowerCharge || nextReady != IsPowerReady)
        {
            CurrentPowerCharge = nextCharge;
            IsPowerReady = nextReady;
            OnPowerStateChanged?.Invoke(CurrentPowerCharge, required, IsPowerReady);
        }
    }

    private void ConsumePower()
    {
        CurrentPowerCharge = 0;
        IsPowerReady = false;
        OnPowerStateChanged?.Invoke(CurrentPowerCharge, PowerChargeRequired, IsPowerReady);
    }

    // --- Auxiliares Internos ---

    private RivalProfile EnsureRuntimeFallbackProfile()
    {
        if (runtimeFallbackProfile == null)
        {
            runtimeFallbackProfile = RivalProfile.CreateRuntimeDefault();
        }
        return runtimeFallbackProfile;
    }

    private RivalDifficultyProfile ResolveActiveProfiles()
    {
        ActiveDifficultyProfile = ActiveProfile.GetProfile(currentDifficulty);
        return ActiveDifficultyProfile;
    }

    private void CacheSceneReferences()
    {
        if (ball == null)
        {
            BallController foundBall = FindFirstObjectByType<BallController>();
            if (foundBall != null)
            {
                ball = foundBall.transform;
                ballController = foundBall;
                ballRb = foundBall.GetComponent<Rigidbody>();
                ballReporter = foundBall.GetComponent<BallReporter>();
            }
        }
        else
        {
            ballController = ball.GetComponent<BallController>();
            ballReporter = ball.GetComponent<BallReporter>();
            if (ballRb == null) ballRb = ball.GetComponent<Rigidbody>();
        }

        if (referee == null)
        {
            referee = FindFirstObjectByType<MatchReferee>();
        }

        if (playerTableTarget == null)
        {
            GameObject playerTable = GameObject.FindGameObjectWithTag("TablePlayer");
            if (playerTable != null) playerTableTarget = playerTable.transform;
        }
        playerTableCollider = playerTableTarget != null ? playerTableTarget.GetComponentInChildren<Collider>() : null;

        if (enemyTableTarget == null)
        {
            GameObject enemyTable = GameObject.FindGameObjectWithTag("TableEnemy");
            if (enemyTable != null) enemyTableTarget = enemyTable.transform;
        }
        enemyTableCollider = enemyTableTarget != null ? enemyTableTarget.GetComponentInChildren<Collider>() : null;
    }

    private void ComputeCourtGeometry()
    {
        if (playerTableTarget != null)
        {
            Vector3 playerCenter = GetPlayerTableSurfaceCenter();
            SideMultiplier = (playerCenter.x > baselineX) ? 1f : -1f;
        }
        else
        {
            SideMultiplier = (baselineX < 0f) ? 1f : -1f;
        }

        if (playerTableTarget != null && enemyTableTarget != null)
        {
            netX = 0.5f * (GetPlayerTableSurfaceCenter().x + GetEnemyTableSurfaceCenter().x);
        }
        else
        {
            netX = 0f;
        }
    }

    private void ConfigureDeterministicPaddleColliders()
    {
        if (aiPaddle == null) return;

        // Salvaguarda en runtime: desactivar cualquier PaddleFlickController accidental en la pala rival
        PaddleFlickController[] strayPlayerControllers = aiPaddle.GetComponentsInChildren<PaddleFlickController>(true);
        foreach (PaddleFlickController stray in strayPlayerControllers)
        {
            if (stray != null && stray.enabled)
            {
                stray.enabled = false;
            }
        }

        if (!useDeterministicReturns) return;

        Collider[] aiCols = aiPaddle.GetComponentsInChildren<Collider>();
        foreach (Collider col in aiCols)
        {
            if (col is MeshCollider mc && !mc.convex) mc.convex = true;
            col.isTrigger = true;
        }
    }

    private Vector3 GetPlayerTableSurfaceCenter()
    {
        return GetTableSurfaceCenter(playerTableTarget, ref playerTableCollider);
    }

    private Vector3 GetEnemyTableSurfaceCenter()
    {
        return GetTableSurfaceCenter(enemyTableTarget, ref enemyTableCollider);
    }

    private static Vector3 GetTableSurfaceCenter(Transform tableTransform, ref Collider cachedCollider)
    {
        if (tableTransform == null) return Vector3.zero;
        if (cachedCollider == null)
        {
            cachedCollider = tableTransform.GetComponentInChildren<Collider>();
        }

        if (cachedCollider != null)
        {
            Bounds b = cachedCollider.bounds;
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        return tableTransform.position;
    }

    private float GetPlayerTableHalfLengthWorld(Vector3 forwardDir)
    {
        if (playerTableTarget != null && playerTableCollider == null)
        {
            playerTableCollider = playerTableTarget.GetComponentInChildren<Collider>();
        }

        if (playerTableCollider != null)
        {
            Vector3 extents = playerTableCollider.bounds.extents;
            float projected = Mathf.Abs(extents.x * forwardDir.x) + Mathf.Abs(extents.z * forwardDir.z);
            if (projected > 0.1f) return projected;
        }

        return playerTableHalfLength;
    }

    private bool IsBallMovingTowardsAI()
    {
        // Si SideMultiplier > 0 (IA en X negativo, empuja hacia +X), la bola viene hacia IA cuando vx < -0.1
        return SideMultiplier > 0f
            ? ballRb.linearVelocity.x < -0.1f
            : ballRb.linearVelocity.x > 0.1f;
    }

    private bool HasBallCrossedNet()
    {
        return SideMultiplier > 0f
            ? ball.position.x < netX
            : ball.position.x > netX;
    }

    private Vector3 GetFlatDirectionToPlayerTable(Vector3 fromPos)
    {
        if (playerTableTarget == null)
        {
            return new Vector3(SideMultiplier, 0f, 0f);
        }

        Vector3 dir = GetPlayerTableSurfaceCenter() - fromPos;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : new Vector3(SideMultiplier, 0f, 0f);
    }

    private void MovePaddleTowards(Vector3 desiredPos, float speed)
    {
        float dt = Time.deltaTime;
        if (dt <= 0.0001f)
        {
            CurrentVelocity = Vector3.zero;
            return;
        }

        Vector3 prevPos = aiPaddle.position;
        Vector3 nextPos = Vector3.MoveTowards(prevPos, desiredPos, speed * dt);
        aiPaddle.position = nextPos;

        Vector3 instantVel = (nextPos - prevPos) / dt;
        CurrentVelocity = Vector3.Lerp(CurrentVelocity, instantVel, Mathf.Clamp01(dt * 18f));
    }

    private void ResetRallyDecisionState()
    {
        wasMovingTowardsAI = false;
        wasCrossedNet = false;
        hasIncomingShotInfo = false;
        hasPlannedThisRally = false;
        hasTelegraphedThisRally = false;
        hasTriggeredMissThisRally = false;
        willMissThisRally = false;
        currentErrorZ = 0f;
        currentErrorY = 0f;
    }
}
