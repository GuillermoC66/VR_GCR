using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.UI;

public class PowerManager : MonoBehaviour
{
    [System.Serializable]
    public class PowerConfig
    {
        public string powerName = "Super Golpe";
        public int hitsRequired = 5;
        [HideInInspector] public int currentHits = 0;
        [HideInInspector] public bool isReady = false;
    }

    [Header("Configuración Arcade")]
    public PowerConfig power = new PowerConfig();
    
    [Header("UI")]
    public TextMeshProUGUI powerMeterText;
    public Slider powerMeterSlider;

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
    
    // Estado expuesto para el PaddleFlickController
    public bool isPowerActive { get; private set; } = false;

    void Start()
    {
        paddle = GetComponent<PaddleFlickController>();

        if (paddleRenderer != null)
        {
            originalColor = paddleRenderer.material.color;
        }
        
        if (powerTrail != null) powerTrail.emitting = false;
        if (powerReadyParticles != null) powerReadyParticles.Stop();

        UpdateUI();
    }

    void OnEnable()
    {
        PaddleFlickController.OnPlayerHit += HandlePlayerHit;
    }

    void OnDisable()
    {
        PaddleFlickController.OnPlayerHit -= HandlePlayerHit;
    }

    void Update()
    {
        if (paddle != null && paddle.buttonA != null && paddle.buttonB != null)
        {
            bool holdingBoth = paddle.buttonA.action.IsPressed() && paddle.buttonB.action.IsPressed();
            
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
    }

    private void HandlePlayerHit()
    {
        if (isPowerActive)
        {
            // Consumimos el golpe
            if (audioSource != null && powerHitSound != null)
            {
                audioSource.PlayOneShot(powerHitSound, MenuManager.sfxVolume);
            }
            
            ConsumePower();
            return;
        }

        // Si no está activo, cargamos la barra
        if (!power.isReady)
        {
            power.currentHits++;
            if (power.currentHits >= power.hitsRequired)
            {
                power.currentHits = power.hitsRequired;
                power.isReady = true;
                Debug.Log("[PowerManager] ¡Poder LISTO! Mantén A y B al golpear.");
            }
            UpdateUI();
        }
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
