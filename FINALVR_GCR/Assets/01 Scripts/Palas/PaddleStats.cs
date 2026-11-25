using UnityEngine;

public enum SpecialPowerType
{
    FastballSmash,
    CorteHuracan,
    SeismicPunch,
    BodhisattvaDefensivo
}

/// <summary>
/// Estadísticas configurables por pala (GDD: Madera, Hex, Rayo, Curva, Plateada, Dorada).
/// Permite ajustar velocidad, ventana de timing, curva de slice y poder especial sin duplicar código.
/// </summary>
[CreateAssetMenu(fileName = "PaddleStats_New", menuName = "PingPing PongPong/Paddle Stats")]
public class PaddleStats : ScriptableObject
{
    [Header("Identidad")]
    public string paddleName = "Pala Inicial de Madera";
    [TextArea(2, 3)]
    public string description = "Posee un balance neutro.";

    [Header("Multiplicadores Arcade")]
    [Tooltip("Escala la rapidez de los tiros (>1 = vuelos más rápidos, <1 = vuelos más lentos).")]
    [Range(0.75f, 1.4f)]
    public float speedMultiplier = 1.0f;

    [Tooltip("Escala el tamaño de la ventana de Timing (>1 = Perfecto más generoso, <1 = más exigente).")]
    [Range(0.65f, 1.5f)]
    public float timingWindowMultiplier = 1.0f;

    [Tooltip("Escala la curva lateral (efecto Magnus) al hacer Slice o Corte Huracán.")]
    [Range(0.3f, 1.8f)]
    public float sliceCurveMultiplier = 1.0f;

    [Tooltip("Multiplicador extra de agresividad/profundidad para Smash.")]
    [Range(0.8f, 1.35f)]
    public float smashPowerMultiplier = 1.0f;

    [Header("Poder Especial Equipado")]
    public SpecialPowerType specialPower = SpecialPowerType.FastballSmash;
}
