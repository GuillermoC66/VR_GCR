using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PowerManager : MonoBehaviour
{
    [System.Serializable]
    public class PowerConfig
    {
        public string powerName = "Super Golpe";
        public int hitsRequired = 6;
        [HideInInspector] public int currentHits = 0;
        [HideInInspector] public bool isReady = false;
    }

    [Header("Configuración Arcade")]
    public PowerConfig power = new PowerConfig();

    [Tooltip("Poder especial por defecto si la pala no tiene un PaddleStats asignado.")]
    public SpecialPowerType defaultSpecialPower = SpecialPowerType.FastballSmash;

    [Header("Carga de Energía por Timing")]
    [Tooltip("Energía ganada al conectar un golpe Perfecto.")]
    public int perfectHitCharge = 2;
    [Tooltip("Energía ganada al conectar un golpe Adelantado / Atrasado.")]
    public int normalHitCharge = 1;
    [Tooltip("Energía ganada al conectar un golpe Muy Adelantado / Muy Atrasado.")]
    public int poorHitCharge = 0;

    [Header("UI de Poder y Timing")]
    public TextMeshProUGUI powerMeterText;
    public Slider powerMeterSlider;
    [Tooltip("Opcional: texto HUD/Diegético que muestra '¡PERFECTO!', 'Adelantado', 'Atrasado'.")]
    public TextMeshProUGUI timingFeedbackText;
    public float timingFeedbackDuration = 0.85f;

    [Header("Visuales del Poder")]
    public MeshRenderer paddleRenderer;
    public Color superHitColor = Color.red;
    public ParticleSystem powerReadyParticles;
    public TrailRenderer powerTrail;

    [Header("Sonido")]
    public AudioSource audioSource;
    public AudioClip powerHitSound;

    private PaddleFlickController paddle;
    private Color originalColor;
    private Coroutine timingFadeCoroutine;

    // Estado expuesto para el PaddleFlickController
    public bool isPowerActive { get; private set; } = false;

    private void Start()
    {
        paddle = GetComponent<PaddleFlickController>();

        if (paddleRenderer != null)
        {
            originalColor = paddleRenderer.material.color;
        }

        if (powerTrail != null) powerTrail.emitting = false;
        if (powerReadyParticles != null) powerReadyParticles.Stop();
        if (timingFeedbackText != null) timingFeedbackText.text = string.Empty;

        UpdateUI();
    }

    private void OnEnable()
    {
        PaddleFlickController.OnArcadeShotExecuted += HandleArcadeShot;
    }

    private void OnDisable()
    {
        PaddleFlickController.OnArcadeShotExecuted -= HandleArcadeShot;
    }

    private void Update()
    {
        bool holdingBoth = false;
        XRControllerLayoutManager layoutManager = XRControllerLayoutManager.Instance;

        if (layoutManager != null)
        {
            // Layout de Partidos: Trigger + Grip en la mano dominante (espejo automático para zurdos)
            holdingBoth = layoutManager.IsSuperComboPressed;
        }
        else if (paddle != null && paddle.buttonA != null && paddle.buttonB != null)
        {
            holdingBoth = paddle.buttonA.action.IsPressed() && paddle.buttonB.action.IsPressed();
        }

        if (power.isReady)
        {
            if (holdingBoth && !isPowerActive)
            {
                ActivatePowerVisuals();
            }
            else if (!holdingBoth && isPowerActive)
            {
                CancelPowerVisuals();
            }
        }
    }

    public SpecialPowerType GetActiveSpecialPower(PaddleStats equippedStats)
    {
        return equippedStats != null ? equippedStats.specialPower : defaultSpecialPower;
    }

    private void HandleArcadeShot(ArcadeShotResult result)
    {
        ShowTimingFeedback(result);

        if (isPowerActive)
        {
            if (audioSource != null && powerHitSound != null)
            {
                audioSource.PlayOneShot(powerHitSound, MenuManager.sfxVolume);
            }

            ConsumePower();
            return;
        }

        // Cargar la barra de energía premiando el Timing real
        if (!power.isReady)
        {
            int gained = GetChargeForTiming(result.timing);
            if (gained > 0)
            {
                power.currentHits = Mathf.Min(power.currentHits + gained, power.hitsRequired);
                if (power.currentHits >= power.hitsRequired)
                {
                    power.isReady = true;
                    Debug.Log("[PowerManager] ¡Poder LISTO! Mantén Trigger + Grip al golpear.");
                }
                UpdateUI();
            }
        }
    }

    private int GetChargeForTiming(TimingTier timing)
    {
        switch (timing)
        {
            case TimingTier.Perfecto: return perfectHitCharge;
            case TimingTier.Adelantado:
            case TimingTier.Atrasado: return normalHitCharge;
            default: return poorHitCharge;
        }
    }

    private void ShowTimingFeedback(ArcadeShotResult result)
    {
        if (timingFeedbackText == null) return;

        string label;
        Color color;

        if (result.shotType == ArcadeShotType.Special)
        {
            label = $"¡{result.specialPower}!";
            color = new Color(1f, 0.85f, 0.15f, 1f);
        }
        else
        {
            switch (result.timing)
            {
                case TimingTier.Perfecto:
                    label = result.shotType == ArcadeShotType.Smash ? "¡SMASH PERFECTO!" : "¡PERFECTO!";
                    color = new Color(1f, 0.88f, 0.2f, 1f);
                    break;
                case TimingTier.Adelantado:
                    label = "Adelantado";
                    color = new Color(0.35f, 0.9f, 1f, 1f);
                    break;
                case TimingTier.Atrasado:
                    label = "Atrasado";
                    color = new Color(1f, 0.7f, 0.25f, 1f);
                    break;
                case TimingTier.MuyAdelantado:
                    label = result.isFaultShot ? "¡MUY ADELANTADO! (Larga)" : "Muy Adelantado";
                    color = new Color(1f, 0.3f, 0.3f, 1f);
                    break;
                default:
                    label = result.isFaultShot ? "¡MUY ATRASADO! (Red)" : "Muy Atrasado";
                    color = new Color(1f, 0.3f, 0.3f, 1f);
                    break;
            }
        }

        timingFeedbackText.text = label;
        timingFeedbackText.color = color;

        if (timingFadeCoroutine != null) StopCoroutine(timingFadeCoroutine);
        timingFadeCoroutine = StartCoroutine(ClearTimingFeedbackAfterDelay());
    }

    private IEnumerator ClearTimingFeedbackAfterDelay()
    {
        yield return new WaitForSecondsRealtime(timingFeedbackDuration);
        if (timingFeedbackText != null) timingFeedbackText.text = string.Empty;
        timingFadeCoroutine = null;
    }

    private void ActivatePowerVisuals()
    {
        isPowerActive = true;
        if (paddleRenderer != null) paddleRenderer.material.color = superHitColor;
        if (powerReadyParticles != null) powerReadyParticles.Play();
        if (powerTrail != null) powerTrail.emitting = true;
    }

    private void CancelPowerVisuals()
    {
        isPowerActive = false;
        if (paddleRenderer != null) paddleRenderer.material.color = originalColor;
        if (powerReadyParticles != null) powerReadyParticles.Stop();
        if (powerTrail != null) powerTrail.emitting = false;
    }

    public void ConsumePower()
    {
        power.currentHits = 0;
        power.isReady = false;
        UpdateUI();
        CancelPowerVisuals();
    }

    private void UpdateUI()
    {
        if (powerMeterText != null)
        {
            powerMeterText.text = power.isReady ? "¡PODER LISTO!" : $"Carga: {power.currentHits} / {power.hitsRequired}";
            powerMeterText.color = power.isReady ? Color.green : Color.white;
        }
        if (powerMeterSlider != null)
        {
            powerMeterSlider.maxValue = power.hitsRequired;
            powerMeterSlider.value = power.currentHits;
        }
    }
}
