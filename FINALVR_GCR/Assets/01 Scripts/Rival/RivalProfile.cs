using System;
using UnityEngine;

/// <summary>
/// Perfil de identidad, estilo visual, poder especial y curvas de dificultad de un Rival.
/// Permite escalar fácilmente a futuros rivales con distintos poderes y personalidades.
/// </summary>
[CreateAssetMenu(fileName = "Rival_New", menuName = "PingPing PongPong/Rival Profile")]
public class RivalProfile : ScriptableObject
{
    [Header("Identidad del Rival")]
    public string rivalName = "Entrenador Kai";
    public string rivalTitle = "Rival Equilibrado";
    [TextArea(2, 3)]
    public string description = "Un maestro versátil que adapta su ritmo, colocación y poder especial para poner a prueba tus reflejos.";

    [Header("Poder Especial y Temática Visual")]
    public SpecialPowerType specialPower = SpecialPowerType.FastballSmash;
    public Color primaryColor = new Color(0.18f, 0.45f, 0.85f, 1f);
    public Color secondaryColor = new Color(0.95f, 0.85f, 0.25f, 1f);

    [Header("Multiplicadores de Especialización (Futuros Rivales)")]
    [Tooltip("Escala la velocidad de desplazamiento y devolución del rival (>1 más rápido).")]
    [Range(0.75f, 1.35f)]
    public float speedMultiplier = 1f;
    [Tooltip("Escala la intensidad de curva de Slice y Corte Huracán.")]
    [Range(0.75f, 1.5f)]
    public float powerCurveMultiplier = 1f;
    [Tooltip("Escala la tendencia a rematar y presionar en profundidad.")]
    [Range(0.75f, 1.35f)]
    public float aggressionMultiplier = 1f;

    [Header("Perfiles de Dificultad")]
    public RivalDifficultyProfile easyProfile;
    public RivalDifficultyProfile mediumProfile;
    public RivalDifficultyProfile hardProfile;

    [NonSerialized] private RivalDifficultyProfile fallbackEasy;
    [NonSerialized] private RivalDifficultyProfile fallbackMedium;
    [NonSerialized] private RivalDifficultyProfile fallbackHard;

    /// <summary>
    /// Devuelve el perfil asignado para la dificultad solicitada, o un perfil en memoria calibrado
    /// si el asset correspondiente aún no ha sido asignado.
    /// </summary>
    public RivalDifficultyProfile GetProfile(RivalDifficulty difficulty)
    {
        switch (difficulty)
        {
            case RivalDifficulty.Easy:
                if (easyProfile != null) return easyProfile;
                if (fallbackEasy == null) fallbackEasy = RivalDifficultyProfile.CreateRuntimeDefault(RivalDifficulty.Easy);
                return fallbackEasy;

            case RivalDifficulty.Hard:
                if (hardProfile != null) return hardProfile;
                if (fallbackHard == null) fallbackHard = RivalDifficultyProfile.CreateRuntimeDefault(RivalDifficulty.Hard);
                return fallbackHard;

            default:
                if (mediumProfile != null) return mediumProfile;
                if (fallbackMedium == null) fallbackMedium = RivalDifficultyProfile.CreateRuntimeDefault(RivalDifficulty.Medium);
                return fallbackMedium;
        }
    }

    public static RivalProfile CreateRuntimeDefault()
    {
        RivalProfile profile = CreateInstance<RivalProfile>();
        profile.name = "RuntimeDefault_EntrenadorKai";
        profile.hideFlags = HideFlags.DontSave;
        return profile;
    }
}
