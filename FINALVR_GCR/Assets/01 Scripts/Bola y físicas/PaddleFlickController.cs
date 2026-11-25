using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controlador arcade de golpeo por Timing real, Fuerza de Swing y Tipo de Tiro.
/// Reemplaza colisiones volátiles de PhysX con un sistema híbrido:
/// 1) Trigger amplio en la pala (sin rebote sólido ni dependencia del ángulo de muñeca).
/// 2) Asistencia de alcance al hacer un swing rápido cerca de la pelota.
/// 3) Evaluación de Timing por fase de rebote (ápice) + plano ideal de golpeo.
/// 4) Solución balística exacta sobre la mitad rival de la mesa con riesgo/recompensa en Smash.
/// </summary>
public class PaddleFlickController : MonoBehaviour
{
    public static event Action OnPlayerHit;
    public static event Action<ArcadeShotResult> OnArcadeShotExecuted;

    [Header("Estadísticas de Pala (Opcional — GDD)")]
    public PaddleStats paddleStats;

    [Header("Fallback Inputs (si no hay XRControllerLayoutManager)")]
    public InputActionReference buttonA; // Slice
    public InputActionReference buttonB; // Smash

    [Header("Referencias de Mesa y Cancha")]
    [Tooltip("Centro de la mitad rival de la mesa (Target Point).")]
    public Transform targetPoint;

    [Tooltip("Opcional: plano/Transform ideal donde golpear produce Timing 'Perfecto' en profundidad.")]
    public Transform idealStrikePlane;

    [Tooltip("Altura Y superior de la red respecto a la superficie del targetPoint (estándar: 0.155m).")]
    public float netHeightAboveTable = 0.155f;

    [Tooltip("Mitad del ancho utilizable de la mesa rival (estándar: ~0.68m de 1.525m).")]
    public float enemyTableHalfWidth = 0.68f;

    [Tooltip("Mitad del largo utilizable de la mitad rival (estándar: ~0.58m de 1.37m).")]
    public float enemyTableHalfLength = 0.58f;

    [Header("Detección Híbrida (Trigger + Proximidad de Swing)")]
    [Tooltip("Convierte automáticamente el Collider de la pala en Trigger para evitar rebotes locos de PhysX.")]
    public bool forceTriggerCollider = true;

    [Tooltip("Velocidad mínima de la pala (m/s) para registrar un golpe al entrar al Trigger.")]
    public float minTriggerSwingSpeed = 0.45f;

    [Tooltip("Activa el sweet-spot de proximidad al hacer swing rápido cerca de la pelota.")]
    public bool enableSwingProximityAssist = true;

    [Tooltip("Radio máximo (m) alrededor de la pala donde un swing rápido conecta la pelota.")]
    public float swingAssistRadius = 0.10f;

    [Tooltip("Velocidad mínima de swing (m/s) para activar la asistencia por proximidad.")]
    public float assistMinSwingSpeed = 1.35f;

    [Tooltip("Velocidad de swing (m/s) que corresponde a fuerza máxima (100%).")]
    public float maxSwingSpeed = 5.5f;

    [Tooltip("Tiempo mínimo entre golpes para evitar dobles contactos.")]
    public float hitCooldown = 0.25f;

    [Header("Ventanas de Timing (0 = Perfecto)")]
    [Tooltip("Rango máximo de error para Timing Perfecto.")]
    public float perfectWindow = 0.22f;

    [Tooltip("Rango máximo de error para Adelantado / Atrasado (más allá es Muy Adelantado / Muy Atrasado).")]
    public float goodWindow = 0.58f;

    [Tooltip("Peso de la fase vertical del rebote (ápice) vs la profundidad en el plano ideal (0..1).")]
    [Range(0f, 1f)]
    public float bouncePhaseWeight = 0.7f;

    [Tooltip("Multiplicador de exigencia para la ventana de Timing en Smash (<1 = más difícil).")]
    [Range(0.4f, 1f)]
    public float smashWindowStrictness = 0.65f;

    [Header("Perfiles de Vuelo (Tiempo y Altura sobre Red)")]
    public float driveFlightTimeMin = 0.44f;
    public float driveFlightTimeMax = 0.62f;
    public float driveNetClearance = 0.26f;

    public float smashFlightTimeMin = 0.26f;
    public float smashFlightTimeMax = 0.36f;
    public float smashNetClearance = 0.09f;

    public float sliceFlightTimeMin = 0.68f;
    public float sliceFlightTimeMax = 0.88f;
    public float sliceNetClearance = 0.50f;
    public float sliceBaseCurveAccel = 3.2f;

    [Header("Poderes Especiales y Anime Hitstop")]
    public float fastballFlightTime = 0.23f;
    public float hurricaneCurveAccel = 7.5f;
    public float perfectSmashHitstop = 0.038f;
    public float specialHitstop = 0.048f;

    private Vector3 lastPosition;
    private Vector3 smoothedVelocity;
    private float lastHitTime = -10f;

    private PowerManager powerManager;
    private BallController cachedBall;
    private BallReporter cachedBallReporter;
    private MatchReferee cachedReferee;
    private Transform cachedPlayerTable;
    private Collider cachedPlayerTableCollider;
    private Collider cachedTargetCollider;

    private void Awake()
    {
        if (forceTriggerCollider)
        {
            Collider[] cols = GetComponentsInChildren<Collider>();
            foreach (Collider col in cols)
            {
                if (col is MeshCollider mc && !mc.convex)
                {
                    mc.convex = true;
                }
                col.isTrigger = true;
            }
        }
    }

    private void Start()
    {
        lastPosition = transform.position;
        powerManager = GetComponent<PowerManager>();
        CacheBallReferences();
    }

    private void FixedUpdate()
    {
        Vector3 instantVel = (transform.position - lastPosition) / Time.fixedDeltaTime;
        // Suavizamos ligeramente para eliminar picos de tracking de 1 frame en VR
        smoothedVelocity = Vector3.Lerp(smoothedVelocity, instantVel, 0.65f);
        lastPosition = transform.position;

        if (enableSwingProximityAssist)
        {
            TrySwingProximityHit();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball")) return;
        TryExecuteHitFromObject(other.gameObject, minTriggerSwingSpeed);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Ball")) return;
        TryExecuteHitFromObject(other.gameObject, minTriggerSwingSpeed);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Fallback si el usuario desactivó forceTriggerCollider
        if (!collision.gameObject.CompareTag("Ball")) return;
        TryExecuteHitFromObject(collision.gameObject, minTriggerSwingSpeed);
    }

    private bool CanAttemptPlayerHit()
    {
        if (cachedReferee == null) return true;
        if (!cachedReferee.IsPlayInputAllowed()) return false;
        // Evitar faltas accidentales si la bola aún flota esperando el inicio del saque de la IA
        if (cachedReferee.CurrentPhase == MatchReferee.PointPhase.WaitingForServe && !cachedReferee.GetIsPlayerTurnToServe())
        {
            return false;
        }
        return true;
    }

    private void TrySwingProximityHit()
    {
        if (Time.time - lastHitTime < hitCooldown) return;
        if (!CanAttemptPlayerHit()) return;
        if (smoothedVelocity.magnitude < assistMinSwingSpeed) return;

        if (cachedBall == null) CacheBallReferences();
        if (cachedBall == null || !cachedBall.gameObject.activeInHierarchy) return;
        if (cachedBall.IsGrabbed || cachedBall.IsInHitstop) return;

        float dist = Vector3.Distance(transform.position, cachedBall.transform.position);
        if (dist > swingAssistRadius) return;

        // Solo asistir si el swing va generalmente hacia la mesa rival (evita golpear al preparar el brazo hacia atrás)
        Vector3 toTarget = GetFlatDirectionToTarget(cachedBall.transform.position);
        float forwardDot = Vector3.Dot(smoothedVelocity.normalized, toTarget);
        if (forwardDot < 0.15f) return;

        ExecuteArcadeShot(cachedBall, cachedBallReporter);
    }

    private void TryExecuteHitFromObject(GameObject ballObj, float requiredSpeed)
    {
        if (Time.time - lastHitTime < hitCooldown) return;
        if (!CanAttemptPlayerHit()) return;
        if (smoothedVelocity.magnitude < requiredSpeed) return;

        BallController bc = ballObj.GetComponent<BallController>();
        if (bc == null || bc.IsGrabbed || bc.IsInHitstop) return;

        BallReporter reporter = ballObj.GetComponent<BallReporter>();
        ExecuteArcadeShot(bc, reporter);
    }

    private void ExecuteArcadeShot(BallController ball, BallReporter reporter)
    {
        lastHitTime = Time.time;

        // 1. Leer modificadores de entrada desde XRControllerLayoutManager (o fallback legado)
        XRControllerLayoutManager layoutManager = XRControllerLayoutManager.Instance;
        bool isSmashInput = layoutManager != null
            ? layoutManager.IsSmashPressed
            : (buttonB != null && buttonB.action.IsPressed());
        bool isSliceInput = layoutManager != null
            ? layoutManager.IsSlicePressed
            : (buttonA != null && buttonA.action.IsPressed());
        bool isSuper = powerManager != null && powerManager.isPowerActive;

        ArcadeShotType shotType = ArcadeShotType.Drive;
        if (isSuper) shotType = ArcadeShotType.Special;
        else if (isSmashInput) shotType = ArcadeShotType.Smash;
        else if (isSliceInput) shotType = ArcadeShotType.Slice;

        SpecialPowerType activeSpecial = powerManager != null
            ? powerManager.GetActiveSpecialPower(paddleStats)
            : (paddleStats != null ? paddleStats.specialPower : SpecialPowerType.FastballSmash);

        // 2. Fuerza normalizada del swing (0..1)
        float swingSpeed = smoothedVelocity.magnitude;
        float strength01 = Mathf.InverseLerp(minTriggerSwingSpeed, maxSwingSpeed, swingSpeed);

        // 3. Evaluar Timing REAL (fase del rebote + profundidad)
        float timingWindowMult = paddleStats != null ? paddleStats.timingWindowMultiplier : 1f;
        if (shotType == ArcadeShotType.Smash) timingWindowMult *= smashWindowStrictness;

        TimingTier timing = EvaluateRealTiming(ball, timingWindowMult);

        // Bodhisattva Defensivo garantiza timing Perfecto
        if (shotType == ArcadeShotType.Special && activeSpecial == SpecialPowerType.BodhisattvaDefensivo)
        {
            timing = TimingTier.Perfecto;
        }

        // 4. Calcular orientación de la mesa rival (axis-agnostic: funciona en X o en Z)
        Vector3 ballPos = ball.transform.position;
        Vector3 enemyCenter = GetEnemyTableSurfaceCenter(ballPos);

        Vector3 forwardDir = GetFlatDirectionToTarget(ballPos);
        Vector3 rightDir = Vector3.Cross(Vector3.up, forwardDir).normalized;

        // 5. Mapear Timing -> Desvío lateral (Izquierda = Adelantado, Centro = Perfecto, Derecha = Atrasado)
        float lateralFactor = GetLateralFactorFromTiming(timing);
        float depthFactor = Mathf.Lerp(-0.40f, 0.72f, strength01);

        float speedMult = paddleStats != null ? paddleStats.speedMultiplier : 1f;
        float curveMult = paddleStats != null ? paddleStats.sliceCurveMultiplier : 1f;
        float smashMult = paddleStats != null ? paddleStats.smashPowerMultiplier : 1f;

        float flightTime;
        float netClearance;
        Vector3 curveAccel = Vector3.zero;
        bool isFaultShot = false;
        float hitstop = 0f;

        // Detectar si el swing fue de derecha o revés para la dirección de la curva del Slice
        float swingRightDot = Vector3.Dot(smoothedVelocity, rightDir);
        float curveSign = swingRightDot >= 0f ? 1f : -1f;

        switch (shotType)
        {
            case ArcadeShotType.Smash:
                depthFactor = Mathf.Lerp(0.25f, 0.88f, strength01) * smashMult;
                flightTime = Mathf.Lerp(smashFlightTimeMax, smashFlightTimeMin, strength01) / speedMult;
                netClearance = smashNetClearance;

                // Riesgo/Recompensa: Smash con timing extremo falla (largo o a la red)
                if (timing == TimingTier.MuyAdelantado)
                {
                    isFaultShot = true;
                    depthFactor = 1.45f; // Se va larga fuera de la mesa
                }
                else if (timing == TimingTier.MuyAtrasado)
                {
                    isFaultShot = true;
                    netClearance = -0.06f; // Se estrella en la red
                }
                else if (timing == TimingTier.Perfecto)
                {
                    hitstop = perfectSmashHitstop;
                }
                break;

            case ArcadeShotType.Slice:
                depthFactor = Mathf.Lerp(-0.55f, 0.25f, strength01);
                flightTime = Mathf.Lerp(sliceFlightTimeMax, sliceFlightTimeMin, strength01) / speedMult;
                netClearance = sliceNetClearance;
                curveAccel = rightDir * (curveSign * sliceBaseCurveAccel * curveMult);
                break;

            case ArcadeShotType.Special:
                hitstop = specialHitstop;
                switch (activeSpecial)
                {
                    case SpecialPowerType.FastballSmash:
                        lateralFactor *= 0.45f; // Súper preciso
                        depthFactor = 0.85f;
                        flightTime = fastballFlightTime / speedMult;
                        netClearance = 0.08f;
                        break;

                    case SpecialPowerType.CorteHuracan:
                        depthFactor = 0.45f;
                        flightTime = 0.56f / speedMult;
                        netClearance = 0.34f;
                        curveAccel = rightDir * (curveSign * hurricaneCurveAccel * curveMult);
                        break;

                    case SpecialPowerType.SeismicPunch:
                        depthFactor = 0.55f;
                        flightTime = 0.40f / speedMult;
                        netClearance = 0.22f;
                        break;

                    default: // BodhisattvaDefensivo
                        lateralFactor = 0f;
                        depthFactor = 0.65f;
                        flightTime = 0.45f / speedMult;
                        netClearance = 0.26f;
                        break;
                }
                break;

            default: // Drive
                flightTime = Mathf.Lerp(driveFlightTimeMax, driveFlightTimeMin, strength01) / speedMult;
                netClearance = driveNetClearance;
                // En Drive, un timing extremo reduce potencia y lo manda pegado al borde, pero siempre entra
                if (timing == TimingTier.MuyAdelantado || timing == TimingTier.MuyAtrasado)
                {
                    flightTime *= 1.15f;
                    depthFactor = Mathf.Clamp(depthFactor, -0.35f, 0.25f);
                }
                break;
        }

        // 6. Construir punto exacto de aterrizaje sobre la mesa rival
        Vector3 landingPoint = enemyCenter
            + rightDir * (lateralFactor * enemyTableHalfWidth)
            + forwardDir * (depthFactor * enemyTableHalfLength);

        float netTopY = enemyCenter.y + netHeightAboveTable;

        // 7. Disparar trayectoria balística determinista (Saque Oficial ITTF de 2 botes o Rally directo)
        bool isOfficialServeShot = cachedReferee != null && cachedReferee.IsPlayerServeShot();
        if (isOfficialServeShot && TryGetPlayerTableSurfaceCenter(out Vector3 playerSurfaceCenter))
        {
            // Tramo 1: rebote limpio en la mitad del jugador; Tramo 2: cruce sobre la red hacia landingPoint
            float serveLateral = Mathf.Clamp(lateralFactor * 0.45f, -0.55f, 0.55f) * enemyTableHalfWidth;
            Vector3 ownBouncePoint = playerSurfaceCenter
                + rightDir * serveLateral
                - forwardDir * 0.10f;

            float firstLegTime = 0.18f;
            float secondLegTime = Mathf.Clamp(flightTime * 0.88f, 0.28f, 0.95f);

            ball.LaunchServeBallistic(
                ownBouncePoint,
                landingPoint,
                firstLegTime,
                secondLegTime,
                netTopY,
                netClearance,
                shotType,
                curveAccel,
                activeSpecial,
                MatchReferee.Hitter.Player
            );
        }
        else
        {
            ball.LaunchBallistic(
                landingPoint,
                flightTime,
                netTopY,
                netClearance,
                shotType,
                curveAccel,
                activeSpecial,
                hitstop,
                MatchReferee.Hitter.Player
            );
        }

        // 8. Notificar árbitro y audio (vital ya que la pala usa Trigger en vez de colisión sólida)
        if (reporter != null)
        {
            reporter.NotifyPlayerPaddleHit(strength01);
        }

        // 9. Hápticos diferenciados en el mando dominante
        TriggerHitHaptics(layoutManager, shotType, timing);

        ArcadeShotResult result = new ArcadeShotResult
        {
            shotType = shotType,
            timing = timing,
            specialPower = activeSpecial,
            swingStrength01 = strength01,
            isFaultShot = isFaultShot,
            landingTarget = landingPoint
        };

        Debug.Log($"[Arcade Shot] Tipo: {shotType} | Timing: {timing} | Fuerza: {strength01:P0} | Falta: {isFaultShot}");

        OnArcadeShotExecuted?.Invoke(result);
        OnPlayerHit?.Invoke();
    }

    private TimingTier EvaluateRealTiming(BallController ball, float windowMultiplier)
    {
        // Componente 1: Fase del rebote (negativo = subiendo/adelantado, 0 = ápice/perfecto, positivo = cayendo/atrasado)
        float bouncePhase = ball.GetBounceTimingPhase();

        // Componente 2: Profundidad respecto al plano ideal (si existe)
        float planePhase = 0f;
        if (idealStrikePlane != null)
        {
            Vector3 toTarget = GetFlatDirectionToTarget(ball.transform.position);
            // Positivo si la pelota está más cerca de la red (adelantado -> signo negativo en timing)
            float signedDistTowardNet = Vector3.Dot(ball.transform.position - idealStrikePlane.position, toTarget);
            planePhase = Mathf.Clamp(-signedDistTowardNet / 0.45f, -1.5f, 1.5f);
        }

        float combinedError = idealStrikePlane != null
            ? Mathf.Lerp(planePhase, bouncePhase, bouncePhaseWeight)
            : bouncePhase;

        float effPerfect = perfectWindow * Mathf.Max(0.4f, windowMultiplier);
        float effGood = goodWindow * Mathf.Max(0.4f, windowMultiplier);

        if (combinedError < -effGood) return TimingTier.MuyAdelantado;
        if (combinedError < -effPerfect) return TimingTier.Adelantado;
        if (combinedError <= effPerfect) return TimingTier.Perfecto;
        if (combinedError <= effGood) return TimingTier.Atrasado;
        return TimingTier.MuyAtrasado;
    }

    private static float GetLateralFactorFromTiming(TimingTier tier)
    {
        switch (tier)
        {
            case TimingTier.MuyAdelantado: return -0.92f; // Muy a la izquierda
            case TimingTier.Adelantado: return -0.52f;    // Izquierda
            case TimingTier.Perfecto: return 0f;          // Recto al centro
            case TimingTier.Atrasado: return 0.52f;       // Derecha
            case TimingTier.MuyAtrasado: return 0.92f;    // Muy a la derecha
            default: return 0f;
        }
    }

    private Vector3 GetFlatDirectionToTarget(Vector3 fromPos)
    {
        if (targetPoint == null)
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
        }

        Vector3 dir = GetEnemyTableSurfaceCenter(fromPos) - fromPos;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
    }

    private Vector3 GetEnemyTableSurfaceCenter(Vector3 fallbackOrigin)
    {
        if (targetPoint == null)
        {
            return fallbackOrigin + transform.forward * 2.0f;
        }

        if (cachedTargetCollider == null)
        {
            cachedTargetCollider = targetPoint.GetComponentInChildren<Collider>();
        }

        if (cachedTargetCollider != null)
        {
            Bounds b = cachedTargetCollider.bounds;
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        return targetPoint.position;
    }

    private bool TryGetPlayerTableSurfaceCenter(out Vector3 surfaceCenter)
    {
        if (cachedReferee != null && cachedReferee.playerTableTarget != null)
        {
            surfaceCenter = cachedReferee.GetPlayerTableSurfaceCenter();
            return true;
        }

        if (cachedPlayerTable == null)
        {
            try
            {
                GameObject tagged = GameObject.FindGameObjectWithTag("TablePlayer");
                if (tagged != null) cachedPlayerTable = tagged.transform;
            }
            catch (UnityException)
            {
                // Ignorar si el tag no existe
            }
        }

        if (cachedPlayerTable == null)
        {
            surfaceCenter = Vector3.zero;
            return false;
        }

        if (cachedPlayerTableCollider == null)
        {
            cachedPlayerTableCollider = cachedPlayerTable.GetComponentInChildren<Collider>();
        }

        if (cachedPlayerTableCollider != null)
        {
            Bounds b = cachedPlayerTableCollider.bounds;
            surfaceCenter = new Vector3(b.center.x, b.max.y, b.center.z);
            return true;
        }

        surfaceCenter = cachedPlayerTable.position;
        return true;
    }

    private static void TriggerHitHaptics(XRControllerLayoutManager layoutManager, ArcadeShotType shotType, TimingTier timing)
    {
        if (layoutManager == null) return;

        switch (shotType)
        {
            case ArcadeShotType.Special:
                layoutManager.SendDominantHandHaptics(1.0f, 0.14f);
                break;
            case ArcadeShotType.Smash:
                layoutManager.SendDominantHandHaptics(timing == TimingTier.Perfecto ? 0.85f : 0.65f, 0.09f);
                break;
            case ArcadeShotType.Slice:
                layoutManager.SendDominantHandHaptics(0.30f, 0.06f);
                break;
            default:
                layoutManager.SendDominantHandHaptics(timing == TimingTier.Perfecto ? 0.55f : 0.35f, 0.05f);
                break;
        }
    }

    private void CacheBallReferences()
    {
        cachedBall = FindFirstObjectByType<BallController>();
        if (cachedBall != null)
        {
            cachedBallReporter = cachedBall.GetComponent<BallReporter>();
        }

        if (cachedReferee == null)
        {
            cachedReferee = FindFirstObjectByType<MatchReferee>();
        }
    }
}
