using UnityEngine;

/// <summary>
/// Perfil táctico y paramétrico de dificultad para un rival de PingPing PongPong VR.
/// Diferencia Fácil, Medio y Difícil en predicción balística, velocidad, variedad de golpes,
/// colocación anti-jugador, vulnerabilidad justa y frecuencia del Poder Especial.
/// </summary>
[CreateAssetMenu(fileName = "Difficulty_New", menuName = "PingPing PongPong/Rival Difficulty Profile")]
public class RivalDifficultyProfile : ScriptableObject
{
    [Header("Identidad y Presentación")]
    public RivalDifficulty difficulty = RivalDifficulty.Medium;
    public string displayLabel = "Medio";
    [TextArea(2, 3)]
    public string description = "Rival equilibrado que predice el rebote, mezcla Drives, Slices y Remates, y aprovecha su poder especial con criterio.";
    public Color accentColor = new Color(1f, 0.70f, 0.16f, 1f);

    [Header("Movimiento y Seguimiento")]
    [Range(1.5f, 18f)]
    public float paddleMoveSpeed = 6.2f;
    [Range(1f, 12f)]
    public float returnToCenterSpeed = 3.8f;
    [Range(0f, 0.45f)]
    public float reactionDelay = 0.11f;
    [Tooltip("Si está activo, el rival predice el punto de intercepción en su línea base en lugar de solo perseguir la posición actual.")]
    public bool useBallisticPrediction = true;
    [Tooltip("Si está activo (Difícil), lee el golpe del jugador antes de que la pelota cruce la red.")]
    public bool predictBeforeNetCross = false;
    [Range(0f, 0.4f)]
    public float positionErrorMargin = 0.08f;
    [Range(0.2f, 0.85f)]
    public float reachRadius = 0.44f;

    [Header("Calidad de Devolución y Variedad de Tiros")]
    [Range(0.28f, 1.0f)]
    public float returnFlightTimeMin = 0.46f;
    [Range(0.35f, 1.2f)]
    public float returnFlightTimeMax = 0.62f;
    [Range(0f, 1f)]
    public float driveChance = 0.54f;
    [Range(0f, 1f)]
    public float sliceChance = 0.26f;
    [Range(0f, 1f)]
    public float smashChance = 0.20f;
    [Range(0f, 8f)]
    public float sliceCurveStrength = 2.8f;
    [Range(0.5f, 1f)]
    public float smashFlightTimeMultiplier = 0.78f;
    [Range(0.05f, 0.6f)]
    public float netClearanceDrive = 0.25f;
    [Range(0.04f, 0.4f)]
    public float netClearanceSmash = 0.12f;
    [Range(0.15f, 0.75f)]
    public float netClearanceSlice = 0.44f;

    [Header("Táctica de Colocación")]
    [Tooltip("Fracción del ancho de la mesa del jugador usada para colocar devoluciones (0.35 = centro, 0.95 = esquinas).")]
    [Range(0.1f, 1f)]
    public float placementSpreadWidth01 = 0.70f;
    [Range(-0.8f, 0.8f)]
    public float placementDepthMin01 = -0.35f;
    [Range(-0.5f, 0.95f)]
    public float placementDepthMax01 = 0.55f;
    [Tooltip("Peso de colocación táctica hacia el lado contrario a la posición actual del jugador (0 = neutro, 0.8 = busca huecos).")]
    [Range(0f, 1f)]
    public float antiPlayerPlacementWeight = 0.35f;

    [Header("Vulnerabilidad Justa y Escalado de Fallos")]
    [Range(0f, 0.6f)]
    public float baseMissChance = 0.07f;
    [Range(0f, 0.6f)]
    public float extraMissVsSmash = 0.16f;
    [Range(0f, 0.6f)]
    public float extraMissVsSpecial = 0.24f;
    [Range(0f, 0.5f)]
    public float extraMissVsExtremeAngle = 0.12f;
    [Range(0f, 0.3f)]
    public float serveFaultChance = 0.04f;

    [Header("Saques del Rival")]
    [Range(0.3f, 2.0f)]
    public float servePauseDuration = 0.90f;
    [Range(0.32f, 1.0f)]
    public float serveFlightTimeMin = 0.50f;
    [Range(0.40f, 1.1f)]
    public float serveFlightTimeMax = 0.62f;
    [Range(0f, 0.8f)]
    public float serveSliceChance = 0.22f;
    [Range(0f, 0.8f)]
    public float serveFastChance = 0.18f;
    [Range(0.1f, 1f)]
    public float serveWidthSpread01 = 0.65f;

    [Header("Poder Especial (Activo en las 3 Dificultades)")]
    [Range(2, 16)]
    public int powerHitsRequired = 6;
    [Range(1, 3)]
    public int powerChargePerReturn = 1;
    [Range(0.05f, 1f)]
    public float powerUseProbabilityWhenReady = 0.60f;
    [Range(0.55f, 1.25f)]
    public float specialFlightTimeMultiplier = 0.92f;
    [Range(0.12f, 0.85f)]
    public float specialTelegraphLeadTime = 0.42f;

    /// <summary>
    /// Rellena este perfil con valores calibrados y diferenciados para la dificultad indicada.
    /// </summary>
    public void ConfigureDefaults(RivalDifficulty diff)
    {
        difficulty = diff;

        switch (diff)
        {
            case RivalDifficulty.Easy:
                displayLabel = "Fácil";
                description = "Ritmo relajado y devoluciones al centro. Reacciona tarde ante remates y usa su poder especial con poca frecuencia.";
                accentColor = new Color(0.22f, 0.85f, 0.48f, 1f);

                paddleMoveSpeed = 3.6f;
                returnToCenterSpeed = 2.4f;
                reactionDelay = 0.24f;
                useBallisticPrediction = false;
                predictBeforeNetCross = false;
                positionErrorMargin = 0.16f;
                reachRadius = 0.36f;

                returnFlightTimeMin = 0.60f;
                returnFlightTimeMax = 0.78f;
                driveChance = 0.85f;
                sliceChance = 0.12f;
                smashChance = 0.03f;
                sliceCurveStrength = 1.4f;
                smashFlightTimeMultiplier = 0.88f;
                netClearanceDrive = 0.30f;
                netClearanceSmash = 0.18f;
                netClearanceSlice = 0.48f;

                placementSpreadWidth01 = 0.35f;
                placementDepthMin01 = -0.25f;
                placementDepthMax01 = 0.25f;
                antiPlayerPlacementWeight = 0.0f;

                baseMissChance = 0.16f;
                extraMissVsSmash = 0.28f;
                extraMissVsSpecial = 0.38f;
                extraMissVsExtremeAngle = 0.22f;
                serveFaultChance = 0.08f;

                servePauseDuration = 1.15f;
                serveFlightTimeMin = 0.62f;
                serveFlightTimeMax = 0.74f;
                serveSliceChance = 0.05f;
                serveFastChance = 0.0f;
                serveWidthSpread01 = 0.35f;

                powerHitsRequired = 10;
                powerChargePerReturn = 1;
                powerUseProbabilityWhenReady = 0.25f;
                specialFlightTimeMultiplier = 1.08f;
                specialTelegraphLeadTime = 0.55f;
                break;

            case RivalDifficulty.Hard:
                displayLabel = "Difícil";
                description = "Lectura anticipada desde tu golpe, colocación táctica a esquinas abiertas, efectos agresivos y poder especial frecuente.";
                accentColor = new Color(0.95f, 0.20f, 0.32f, 1f);

                paddleMoveSpeed = 9.8f;
                returnToCenterSpeed = 6.2f;
                reactionDelay = 0.03f;
                useBallisticPrediction = true;
                predictBeforeNetCross = true;
                positionErrorMargin = 0.025f;
                reachRadius = 0.52f;

                returnFlightTimeMin = 0.36f;
                returnFlightTimeMax = 0.50f;
                driveChance = 0.34f;
                sliceChance = 0.30f;
                smashChance = 0.36f;
                sliceCurveStrength = 4.4f;
                smashFlightTimeMultiplier = 0.72f;
                netClearanceDrive = 0.20f;
                netClearanceSmash = 0.09f;
                netClearanceSlice = 0.38f;

                placementSpreadWidth01 = 0.94f;
                placementDepthMin01 = -0.45f;
                placementDepthMax01 = 0.80f;
                antiPlayerPlacementWeight = 0.80f;

                baseMissChance = 0.02f;
                extraMissVsSmash = 0.09f;
                extraMissVsSpecial = 0.15f;
                extraMissVsExtremeAngle = 0.08f;
                serveFaultChance = 0.01f;

                servePauseDuration = 0.72f;
                serveFlightTimeMin = 0.40f;
                serveFlightTimeMax = 0.52f;
                serveSliceChance = 0.38f;
                serveFastChance = 0.35f;
                serveWidthSpread01 = 0.90f;

                powerHitsRequired = 4;
                powerChargePerReturn = 1;
                powerUseProbabilityWhenReady = 0.90f;
                specialFlightTimeMultiplier = 0.82f;
                specialTelegraphLeadTime = 0.32f;
                break;

            default: // Medium
                displayLabel = "Medio";
                description = "Rival equilibrado que predice el rebote, mezcla Drives, Slices y Remates, y aprovecha su poder especial con criterio.";
                accentColor = new Color(1f, 0.70f, 0.16f, 1f);

                paddleMoveSpeed = 6.2f;
                returnToCenterSpeed = 3.8f;
                reactionDelay = 0.11f;
                useBallisticPrediction = true;
                predictBeforeNetCross = false;
                positionErrorMargin = 0.08f;
                reachRadius = 0.44f;

                returnFlightTimeMin = 0.46f;
                returnFlightTimeMax = 0.62f;
                driveChance = 0.54f;
                sliceChance = 0.26f;
                smashChance = 0.20f;
                sliceCurveStrength = 2.8f;
                smashFlightTimeMultiplier = 0.78f;
                netClearanceDrive = 0.25f;
                netClearanceSmash = 0.12f;
                netClearanceSlice = 0.44f;

                placementSpreadWidth01 = 0.70f;
                placementDepthMin01 = -0.35f;
                placementDepthMax01 = 0.55f;
                antiPlayerPlacementWeight = 0.35f;

                baseMissChance = 0.07f;
                extraMissVsSmash = 0.16f;
                extraMissVsSpecial = 0.24f;
                extraMissVsExtremeAngle = 0.12f;
                serveFaultChance = 0.04f;

                servePauseDuration = 0.90f;
                serveFlightTimeMin = 0.50f;
                serveFlightTimeMax = 0.62f;
                serveSliceChance = 0.22f;
                serveFastChance = 0.18f;
                serveWidthSpread01 = 0.65f;

                powerHitsRequired = 6;
                powerChargePerReturn = 1;
                powerUseProbabilityWhenReady = 0.60f;
                specialFlightTimeMultiplier = 0.92f;
                specialTelegraphLeadTime = 0.42f;
                break;
        }
    }

    /// <summary>
    /// Crea una instancia en memoria configurada con los valores por defecto de la dificultad.
    /// Útil como respaldo seguro en runtime cuando no se ha asignado un asset en el Inspector.
    /// </summary>
    public static RivalDifficultyProfile CreateRuntimeDefault(RivalDifficulty diff)
    {
        RivalDifficultyProfile profile = CreateInstance<RivalDifficultyProfile>();
        profile.name = $"RuntimeDefault_{diff}";
        profile.hideFlags = HideFlags.DontSave;
        profile.ConfigureDefaults(diff);
        return profile;
    }

    private void OnValidate()
    {
        if (returnFlightTimeMax < returnFlightTimeMin)
        {
            returnFlightTimeMax = returnFlightTimeMin;
        }

        if (serveFlightTimeMax < serveFlightTimeMin)
        {
            serveFlightTimeMax = serveFlightTimeMin;
        }

        if (placementDepthMax01 < placementDepthMin01)
        {
            placementDepthMax01 = placementDepthMin01;
        }

        powerHitsRequired = Mathf.Max(2, powerHitsRequired);
        powerChargePerReturn = Mathf.Max(1, powerChargePerReturn);
        powerUseProbabilityWhenReady = Mathf.Clamp(powerUseProbabilityWhenReady, 0.05f, 1f);
    }
}
