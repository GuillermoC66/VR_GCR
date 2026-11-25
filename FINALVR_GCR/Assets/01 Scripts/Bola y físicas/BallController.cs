using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public enum ArcadeShotType
{
    Drive,
    Smash,
    Slice,
    Special
}

public enum TimingTier
{
    MuyAdelantado,
    Adelantado,
    Perfecto,
    Atrasado,
    MuyAtrasado
}

public struct ArcadeShotResult
{
    public ArcadeShotType shotType;
    public TimingTier timing;
    public SpecialPowerType specialPower;
    public float swingStrength01;
    public bool isFaultShot;
    public Vector3 landingTarget;
}

/// <summary>
/// Motor físico balístico determinista para PingPing PongPong.
/// Resuelve arcos exactos hacia el punto de caída objetivo sobre la mesa (con compensación de curva Magnus),
/// controla los rebotes en la mesa para eliminar volatilidad de PhysX, rastrea la fase de rebote para el timing real
/// y aplica micro-hitstop estilo anime + colores de estela por tipo de tiro.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class BallController : MonoBehaviour
{
    private Rigidbody rb;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;

    [Header("Límites de Física Arcade")]
    [Tooltip("Límite máximo de velocidad para evitar tunneling.")]
    [Range(8f, 40f)]
    [SerializeField] private float maxSpeedLimit = 28f;
    public float MaxVelocity => maxSpeedLimit;

    [Tooltip("Rebote base del material físico (usado fuera de mesa o si el rebote controlado está desactivado).")]
    [Range(0f, 1f)]
    [SerializeField] private float bounciness = 0.85f;

    [Tooltip("Gravedad estándar usada fuera de vuelos balísticos dirigidos (ej. saque o post-rebote).")]
    [SerializeField] private float standardGravity = -9.81f;

    [Header("Rebote Controlado en Mesa (Cero Volatilidad)")]
    [Tooltip("Al rebotar en la mesa, normaliza la altura y velocidad del rebote para una ventana de timing consistente.")]
    [SerializeField] private bool useControlledTableBounce = true;

    [SerializeField] private float driveBounceHeight = 0.42f;
    [SerializeField] private float smashBounceHeight = 0.52f;
    [SerializeField] private float sliceBounceHeight = 0.28f;
    [SerializeField] private float driveBounceSpeedRetention = 0.86f;
    [SerializeField] private float smashBounceSpeedRetention = 0.94f;
    [SerializeField] private float sliceBounceSpeedRetention = 0.62f;

    [Header("Asistencia de Saque (Al soltar con XRGrab)")]
    [SerializeField] private float tossBoostUpward = 1.5f;
    [SerializeField] private float tossBoostForward = 0.5f;

    [Header("Feedback Visual de Estela (Trail)")]
    [SerializeField] private TrailRenderer ballTrail;
    [SerializeField] private Color driveTrailColor = Color.white;
    [SerializeField] private Color smashTrailColor = new Color(1f, 0.35f, 0.08f, 1f);
    [SerializeField] private Color sliceTrailColor = new Color(0.15f, 0.85f, 1f, 1f);
    [SerializeField] private Color specialTrailColor = new Color(1f, 0.82f, 0.1f, 1f);

    // --- Estado Público de Vuelo y Rebote (Leído por PaddleFlickController y RivalAIController) ---
    public bool IsGrabbed { get; private set; }
    public bool IsInHitstop { get; private set; }
    public bool IsInBallisticFlight { get; private set; }
    public bool IsPendingServeSecondLeg { get; private set; }
    public bool HasBouncedOnPlayerSide { get; private set; }
    public bool HasBouncedOnAISide { get; private set; }
    public float LastPlayerBounceTime { get; private set; } = -10f;
    public float LastAIBounceTime { get; private set; } = -10f;
    public float ExpectedBounceRiseDuration { get; private set; } = 0.28f;
    public Vector3 LastBouncePoint { get; private set; }
    public ArcadeShotType ActiveShotType { get; private set; } = ArcadeShotType.Drive;
    public SpecialPowerType ActiveSpecialPower { get; private set; } = SpecialPowerType.FastballSmash;
    public MatchReferee.Hitter LastLaunchOrigin { get; private set; } = MatchReferee.Hitter.None;
    public float StandardGravity => standardGravity;
    public float CurrentGravityY => currentGravityY;
    public Vector3 CurrentCurveAccel => currentCurveAccel;
    public Rigidbody BallRigidbody => rb;

    // Parámetros activos del vuelo determinista
    private float currentGravityY;
    private Vector3 currentCurveAccel = Vector3.zero;
    private bool pendingSeismicBounce = false;
    private MatchReferee.Hitter pendingSeismicTargetSide = MatchReferee.Hitter.AI;
    private Vector3 initialPosition;
    private Coroutine hitstopCoroutine;

    // Parámetros del segundo tramo para saque oficial ITTF (bote propio -> cruce de red -> bote rival)
    private MatchReferee.Hitter pendingServeServerSide = MatchReferee.Hitter.None;
    private Vector3 pendingServeOpponentTarget;
    private float pendingServeSecondLegTime = 0.52f;
    private float pendingServeNetTopY = 0.915f;
    private float pendingServeNetClearance = 0.22f;
    private Vector3 pendingServeCurveAccel = Vector3.zero;
    private ArcadeShotType pendingServeShotType = ArcadeShotType.Drive;
    private SpecialPowerType pendingServeSpecialPower = SpecialPowerType.FastballSmash;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        if (ballTrail == null)
        {
            ballTrail = GetComponentInChildren<TrailRenderer>();
        }

        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.useGravity = false; // Integramos gravedad propia en FixedUpdate para exactitud matemática

        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);

        initialPosition = transform.position;
        currentGravityY = standardGravity;

        SetupArcadePhysics();
    }

    private void SetupArcadePhysics()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            PhysicsMaterial arcadeMat = new PhysicsMaterial("ArcadeBall");
            arcadeMat.bounciness = bounciness;
            arcadeMat.dynamicFriction = 0f;
            arcadeMat.staticFriction = 0f;
            arcadeMat.frictionCombine = PhysicsMaterialCombine.Minimum;
            arcadeMat.bounceCombine = PhysicsMaterialCombine.Maximum;
            col.material = arcadeMat;
        }

        // Sin drag aéreo durante vuelos dirigidos para no alterar el punto de caída calculado
        rb.linearDamping = 0f;
        rb.angularDamping = 1f;
    }

    private void OnValidate()
    {
        if (Application.isPlaying && rb != null)
        {
            SetupArcadePhysics();
        }
    }

    private void FixedUpdate()
    {
        if (IsGrabbed || IsInHitstop || rb.isKinematic) return;

        // Integración determinista de gravedad efectiva + curva lateral artificial (Magnus)
        Vector3 accel = new Vector3(currentCurveAccel.x, currentGravityY, currentCurveAccel.z);
        rb.linearVelocity += accel * Time.fixedDeltaTime;
        rb.angularVelocity = Vector3.zero;

        if (rb.linearVelocity.magnitude > maxSpeedLimit)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeedLimit;
        }
    }

    /// <summary>
    /// Lanza la pelota con una trayectoria balística exacta hacia <paramref name="targetPos"/>,
    /// garantizando que cruce la altura de la red con <paramref name="netClearance"/> (o falle intencionalmente
    /// si <paramref name="netClearance"/> es negativo) y compensando exactamente la curva lateral.
    /// </summary>
    public void LaunchBallistic(
        Vector3 targetPos,
        float flightTime,
        float netTopY,
        float netClearance,
        ArcadeShotType shotType,
        Vector3 lateralCurveAccel,
        SpecialPowerType specialPower = SpecialPowerType.FastballSmash,
        float hitstopDuration = 0f,
        MatchReferee.Hitter launcher = MatchReferee.Hitter.Player)
    {
        if (rb == null) return;

        if (hitstopCoroutine != null)
        {
            StopCoroutine(hitstopCoroutine);
            hitstopCoroutine = null;
            IsInHitstop = false;
        }

        float T = Mathf.Clamp(flightTime, 0.18f, 1.35f);
        Vector3 startPos = transform.position;
        Vector3 displacement = targetPos - startPos;

        // Calculamos la gravedad efectiva para garantizar que el arco pase exactamente a (netTopY + netClearance)
        // en el punto medio horizontal del vuelo (u = 0.5).
        const float u = 0.5f;
        float linearMidY = Mathf.Lerp(startPos.y, targetPos.y, u);
        float desiredMidY = netTopY + netClearance;
        float neededLift = desiredMidY - linearMidY;

        float solvedGravity;
        if (netClearance < 0f)
        {
            // Fallo intencional hacia la red: arco plano sin elevación artificial
            solvedGravity = standardGravity;
        }
        else
        {
            // y(uT) = linearMidY - 0.5 * g * (uT)*(T - uT)  =>  g = -2 * neededLift / (T^2 * u * (1 - u))
            float minLift = 0.04f;
            float lift = Mathf.Max(neededLift, minLift);
            solvedGravity = -(2f * lift) / (T * T * u * (1f - u));
            solvedGravity = Mathf.Clamp(solvedGravity, -38f, -4.5f);
        }

        // Velocidad inicial exacta compensando gravedad efectiva y curva lateral:
        // p(T) = p0 + v0*T + 0.5*a*T^2  =>  v0 = (pTarget - p0)/T - 0.5*a*T
        Vector3 effectiveCurve = new Vector3(lateralCurveAccel.x, 0f, lateralCurveAccel.z);
        Vector3 totalAccel = new Vector3(effectiveCurve.x, solvedGravity, effectiveCurve.z);
        Vector3 initialVelocity = (displacement / T) - 0.5f * totalAccel * T;

        if (initialVelocity.magnitude > maxSpeedLimit)
        {
            initialVelocity = initialVelocity.normalized * maxSpeedLimit;
        }

        ActiveShotType = shotType;
        ActiveSpecialPower = specialPower;
        LastLaunchOrigin = launcher;
        IsPendingServeSecondLeg = false;
        pendingServeServerSide = MatchReferee.Hitter.None;
        pendingSeismicBounce = shotType == ArcadeShotType.Special && specialPower == SpecialPowerType.SeismicPunch;
        pendingSeismicTargetSide = launcher == MatchReferee.Hitter.AI ? MatchReferee.Hitter.Player : MatchReferee.Hitter.AI;
        HasBouncedOnPlayerSide = false;
        HasBouncedOnAISide = false;
        IsInBallisticFlight = true;

        ApplyTrailColor(shotType);

        if (hitstopDuration > 0.005f)
        {
            hitstopCoroutine = StartCoroutine(HitstopThenLaunchRoutine(initialVelocity, solvedGravity, effectiveCurve, hitstopDuration));
        }
        else
        {
            ApplySolvedLaunch(initialVelocity, solvedGravity, effectiveCurve);
        }
    }

    /// <summary>
    /// Lanza un saque oficial ITTF en 2 tramos deterministas:
    /// Tramo 1: desde el golpe hasta <paramref name="ownTableBouncePos"/> en el lado del sacador.
    /// Tramo 2 (al botar en lado propio): arco balístico sobre la red hacia <paramref name="opponentLandingPos"/>.
    /// </summary>
    public void LaunchServeBallistic(
        Vector3 ownTableBouncePos,
        Vector3 opponentLandingPos,
        float firstLegTime,
        float secondLegTime,
        float netTopY,
        float netClearance,
        ArcadeShotType shotType,
        Vector3 lateralCurveAccel,
        SpecialPowerType specialPower = SpecialPowerType.FastballSmash,
        MatchReferee.Hitter launcher = MatchReferee.Hitter.Player)
    {
        if (rb == null) return;

        if (hitstopCoroutine != null)
        {
            StopCoroutine(hitstopCoroutine);
            hitstopCoroutine = null;
            IsInHitstop = false;
        }

        float T1 = Mathf.Clamp(firstLegTime, 0.12f, 0.45f);
        Vector3 startPos = transform.position;
        Vector3 displacement = ownTableBouncePos - startPos;

        // Tramo 1 limpio y descendente hacia la mesa del sacador con gravedad estándar
        float solvedGravity = standardGravity;
        Vector3 initialVelocity = (displacement / T1) - 0.5f * new Vector3(0f, solvedGravity, 0f) * T1;
        if (initialVelocity.magnitude > maxSpeedLimit)
        {
            initialVelocity = initialVelocity.normalized * maxSpeedLimit;
        }

        ActiveShotType = shotType;
        ActiveSpecialPower = specialPower;
        LastLaunchOrigin = launcher;
        pendingSeismicBounce = false;
        HasBouncedOnPlayerSide = false;
        HasBouncedOnAISide = false;
        IsInBallisticFlight = true;

        // Guardamos los parámetros del Tramo 2 para ejecutarlo al detectar el bote en el lado propio
        IsPendingServeSecondLeg = true;
        pendingServeServerSide = launcher;
        pendingServeOpponentTarget = opponentLandingPos;
        pendingServeSecondLegTime = Mathf.Clamp(secondLegTime, 0.22f, 1.15f);
        pendingServeNetTopY = netTopY;
        pendingServeNetClearance = netClearance;
        pendingServeCurveAccel = new Vector3(lateralCurveAccel.x, 0f, lateralCurveAccel.z);
        pendingServeShotType = shotType;
        pendingServeSpecialPower = specialPower;

        ApplyTrailColor(shotType);
        ApplySolvedLaunch(initialVelocity, solvedGravity, Vector3.zero);
    }

    /// <summary>
    /// Llamado por BallReporter al chocar con la red: cancela aceleración artificial de curva/tramo 2 para física natural.
    /// </summary>
    public void NotifyNetCollision()
    {
        IsPendingServeSecondLeg = false;
        pendingServeServerSide = MatchReferee.Hitter.None;
        IsInBallisticFlight = false;
        currentCurveAccel = Vector3.zero;
        currentGravityY = standardGravity;
    }

    private IEnumerator HitstopThenLaunchRoutine(Vector3 velocity, float gravityY, Vector3 curveAccel, float duration)
    {
        IsInHitstop = true;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.isKinematic = true;

        yield return new WaitForSecondsRealtime(duration);

        IsInHitstop = false;
        ApplySolvedLaunch(velocity, gravityY, curveAccel);
        hitstopCoroutine = null;
    }

    private void ApplySolvedLaunch(Vector3 velocity, float gravityY, Vector3 curveAccel)
    {
        rb.isKinematic = false;
        rb.useGravity = false;
        currentGravityY = gravityY;
        currentCurveAccel = curveAccel;
        rb.linearVelocity = velocity;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Llamado por BallReporter al tocar TablePlayer o TableEnemy.
    /// Estabiliza el rebote para que siempre tenga una altura y ritmo limpios para el timing.
    /// </summary>
    public void NotifyTableBounce(MatchReferee.Hitter tableSide, Vector3 contactPoint)
    {
        if (rb == null || rb.isKinematic) return;

        LastBouncePoint = contactPoint;

        // Tramo 2 del Saque Oficial ITTF: al botar en el lado del sacador, resolvemos el arco exacto sobre la red al lado receptor
        if (IsPendingServeSecondLeg && tableSide == pendingServeServerSide)
        {
            IsPendingServeSecondLeg = false;
            MatchReferee.Hitter server = pendingServeServerSide;
            pendingServeServerSide = MatchReferee.Hitter.None;

            if (tableSide == MatchReferee.Hitter.Player)
            {
                HasBouncedOnPlayerSide = true;
                LastPlayerBounceTime = Time.time;
                HasBouncedOnAISide = false;
            }
            else
            {
                HasBouncedOnAISide = true;
                LastAIBounceTime = Time.time;
                HasBouncedOnPlayerSide = false;
            }

            // Elevamos mínimamente el punto de partida para evitar re-colisión inmediata con el collider de la mesa
            Vector3 bounceOrigin = new Vector3(contactPoint.x, Mathf.Max(transform.position.y, contactPoint.y + 0.025f), contactPoint.z);
            transform.position = bounceOrigin;

            LaunchBallistic(
                pendingServeOpponentTarget,
                pendingServeSecondLegTime,
                pendingServeNetTopY,
                pendingServeNetClearance,
                pendingServeShotType,
                pendingServeCurveAccel,
                pendingServeSpecialPower,
                0f,
                server
            );
            return;
        }

        IsPendingServeSecondLeg = false;
        pendingServeServerSide = MatchReferee.Hitter.None;
        IsInBallisticFlight = false;
        currentCurveAccel = Vector3.zero;
        currentGravityY = standardGravity;

        if (tableSide == MatchReferee.Hitter.Player)
        {
            HasBouncedOnPlayerSide = true;
            LastPlayerBounceTime = Time.time;
            HasBouncedOnAISide = false;
        }
        else
        {
            HasBouncedOnAISide = true;
            LastAIBounceTime = Time.time;
            HasBouncedOnPlayerSide = false;
        }

        if (!useControlledTableBounce) return;

        Vector3 vel = rb.linearVelocity;
        Vector3 horizVel = new Vector3(vel.x, 0f, vel.z);

        float targetHeight = driveBounceHeight;
        float speedRetention = driveBounceSpeedRetention;

        switch (ActiveShotType)
        {
            case ArcadeShotType.Smash:
                targetHeight = smashBounceHeight;
                speedRetention = smashBounceSpeedRetention;
                break;
            case ArcadeShotType.Slice:
                targetHeight = sliceBounceHeight;
                speedRetention = sliceBounceSpeedRetention;
                break;
            case ArcadeShotType.Special:
                targetHeight = ActiveSpecialPower == SpecialPowerType.FastballSmash ? smashBounceHeight * 1.1f : driveBounceHeight;
                speedRetention = ActiveSpecialPower == SpecialPowerType.CorteHuracan ? 0.80f : 0.96f;
                break;
        }

        // Poder especial Seismic Punch: salto irregular lateral + altura variable al botar en la mesa receptora (Player o AI)
        if (pendingSeismicBounce && tableSide == pendingSeismicTargetSide)
        {
            pendingSeismicBounce = false;
            targetHeight *= UnityEngine.Random.Range(0.75f, 1.45f);
            Vector3 perp = horizVel.sqrMagnitude > 0.01f
                ? Vector3.Cross(Vector3.up, horizVel.normalized)
                : Vector3.right;
            float sideKick = UnityEngine.Random.Range(-1f, 1f) >= 0f ? 1f : -1f;
            horizVel += perp * (sideKick * UnityEngine.Random.Range(1.4f, 2.6f));
        }

        // v_y = sqrt(2 * |g| * H) para alcanzar exactamente targetHeight
        float gAbs = Mathf.Abs(standardGravity);
        float bounceVy = Mathf.Sqrt(2f * gAbs * Mathf.Max(0.15f, targetHeight));
        ExpectedBounceRiseDuration = bounceVy / gAbs;

        horizVel *= speedRetention;
        rb.linearVelocity = new Vector3(horizVel.x, bounceVy, horizVel.z);
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Devuelve la fase normalizada del rebote en el lado del jugador para el timing real:
    /// Negativo = subiendo (Adelantado), 0 = ápice (Perfecto), Positivo = cayendo (Atrasado).
    /// </summary>
    public float GetBounceTimingPhase()
    {
        if (!HasBouncedOnPlayerSide || rb == null)
        {
            // Durante un saque (lanzamiento hacia arriba): medimos respecto al ápice del toss
            if (rb == null) return 0f;
            return Mathf.Clamp(-rb.linearVelocity.y / 2.2f, -1f, 1f);
        }

        float elapsed = Time.time - LastPlayerBounceTime;
        float expectedApexTime = Mathf.Max(0.12f, ExpectedBounceRiseDuration);
        return Mathf.Clamp((elapsed - expectedApexTime) / expectedApexTime, -1.5f, 1.5f);
    }

    private void ApplyTrailColor(ArcadeShotType shotType)
    {
        if (ballTrail == null) return;

        Color targetColor = driveTrailColor;
        switch (shotType)
        {
            case ArcadeShotType.Smash: targetColor = smashTrailColor; break;
            case ArcadeShotType.Slice: targetColor = sliceTrailColor; break;
            case ArcadeShotType.Special: targetColor = specialTrailColor; break;
        }

        ballTrail.startColor = targetColor;
        ballTrail.endColor = new Color(targetColor.r, targetColor.g, targetColor.b, 0f);
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        IsGrabbed = true;
        IsInBallisticFlight = false;
        IsPendingServeSecondLeg = false;
        pendingServeServerSide = MatchReferee.Hitter.None;
        HasBouncedOnPlayerSide = false;
        HasBouncedOnAISide = false;
        LastLaunchOrigin = MatchReferee.Hitter.None;
        currentCurveAccel = Vector3.zero;
        currentGravityY = standardGravity;
        rb.isKinematic = false;
        rb.angularVelocity = Vector3.zero;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        IsGrabbed = false;
        currentGravityY = standardGravity;
        currentCurveAccel = Vector3.zero;

        if (args.interactorObject != null)
        {
            Transform handTransform = args.interactorObject.transform;
            Vector3 boostDirection = (Vector3.up * tossBoostUpward) + (handTransform.forward * tossBoostForward);
            rb.linearVelocity += boostDirection;
        }
    }

    private void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }
    }

    public void ResetAndFloat(Vector3? spawnPosition = null)
    {
        if (hitstopCoroutine != null)
        {
            StopCoroutine(hitstopCoroutine);
            hitstopCoroutine = null;
        }

        IsInHitstop = false;
        IsInBallisticFlight = false;
        IsPendingServeSecondLeg = false;
        pendingServeServerSide = MatchReferee.Hitter.None;
        HasBouncedOnPlayerSide = false;
        HasBouncedOnAISide = false;
        pendingSeismicBounce = false;
        LastLaunchOrigin = MatchReferee.Hitter.None;
        currentCurveAccel = Vector3.zero;
        currentGravityY = standardGravity;

        transform.position = spawnPosition.HasValue ? spawnPosition.Value : initialPosition;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.isKinematic = true; // Flota limpiamente sin caer
        rb.useGravity = false;

        if (ballTrail != null) ballTrail.Clear();
    }

    /// <summary>
    /// Libera la pelota del estado flotante para que empiece a moverse con gravedad estándar (usado en saques).
    /// </summary>
    public void ReleaseForServe(Vector3 initialVelocity)
    {
        IsInBallisticFlight = false;
        IsPendingServeSecondLeg = false;
        pendingServeServerSide = MatchReferee.Hitter.None;
        HasBouncedOnPlayerSide = false;
        HasBouncedOnAISide = false;
        currentCurveAccel = Vector3.zero;
        currentGravityY = standardGravity;

        rb.isKinematic = false;
        rb.useGravity = false;
        rb.linearVelocity = initialVelocity;
        rb.angularVelocity = Vector3.zero;
    }

    public void SetGrabbable(bool grabbable)
    {
        if (grabInteractable != null)
        {
            grabInteractable.enabled = grabbable;
        }
    }

    public bool IsGrabbable => grabInteractable != null && grabInteractable.enabled;
}
