using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem; // Para el gatillo
using TMPro; // Para el texto del medidor
using UnityEngine.UI; // Para el medidor visual (Slider)

public class PowerManager : MonoBehaviour
{
    public enum PowerType
    {
        SuperGolpe, // Aumenta la fuerza al máximo y es siempre preciso
        Placeholder1, // Placeholder 1
        Placeholder2, // Placeholder 2
        Placeholder3, // Placeholder 3
        Placeholder4  // Placeholder 4
    }

    [Serializable]
    public class PowerConfig
    {
        public string powerName;
        public PowerType type;
        public int hitsRequired;
        [HideInInspector] public int currentHits;
        [HideInInspector] public bool isReady;
    }

    [Header("Configuración de Poderes")]
    [Tooltip("Lista de los 5 poderes disponibles.")]
    public List<PowerConfig> powers = new List<PowerConfig>();

    [Header("Poder Seleccionado Actual")]
    public PowerType selectedPower = PowerType.SuperGolpe;

    [Header("Ajustes de 'Super Golpe'")]
    [Tooltip("Velocidad que se aplicará cuando el Super Golpe esté activo.")]
    public float superHitSpeed = 25f;
    [Tooltip("Precisión perfecta para el Super Golpe (1 = 100% hacia el objetivo).")]
    public float superHitAimAssist = 1f;

    [Header("Input (Activación Manual)")]
    [Tooltip("La acción de Input System para activar el poder (ej. Gatillo Derecho).")]
    public InputActionReference activatePowerInput;

    [Header("Interfaz de Usuario (UI)")]
    [Tooltip("Texto para mostrar los golpes restantes (Opcional).")]
    public TextMeshProUGUI powerMeterText;
    [Tooltip("Barra de progreso para mostrar visualmente la carga (Opcional).")]
    public Slider powerMeterSlider;

    [Header("Efectos Visuales (Opcionales)")]
    [Tooltip("El MeshRenderer de la pala para cambiar su color cuando el poder esté activo.")]
    public MeshRenderer paddleRenderer;
    [Tooltip("Color que tomará la pala cuando el Super Golpe esté cargado.")]
    public Color superHitColor = Color.red;
    [Tooltip("Sistema de partículas que se activará cuando el poder esté listo.")]
    public ParticleSystem powerReadyParticles;
    [Tooltip("Estela (TrailRenderer) que se activará durante el Super Golpe.")]
    public TrailRenderer powerTrail;

    [Header("Efectos de Sonido (Opcionales)")]
    public AudioSource audioSource;
    [Tooltip("Sonido que se reproduce cuando golpeas la pelota con el poder activo.")]
    public AudioClip powerHitSound;

    // Estado interno
    private bool isPowerActive = false;
    private PaddleFlickController paddle;

    // Variables para restaurar el estado normal después de un Super Golpe
    private float originalAimAssist;
    private float originalBaseHitSpeed;
    private float originalMaxHitSpeed;
    private Color originalColor;

    void Start()
    {
        paddle = GetComponent<PaddleFlickController>();

        // Llenar la lista por defecto con 5 poderes si está vacía
        if (powers.Count == 0)
        {
            powers.Add(new PowerConfig { powerName = "Super Golpe", type = PowerType.SuperGolpe, hitsRequired = 5 });
            powers.Add(new PowerConfig { powerName = "Poder Oculto 1", type = PowerType.Placeholder1, hitsRequired = 3 });
            powers.Add(new PowerConfig { powerName = "Poder Oculto 2", type = PowerType.Placeholder2, hitsRequired = 4 });
            powers.Add(new PowerConfig { powerName = "Poder Oculto 3", type = PowerType.Placeholder3, hitsRequired = 6 });
            powers.Add(new PowerConfig { powerName = "Poder Oculto 4", type = PowerType.Placeholder4, hitsRequired = 7 });
        }

        // Guardar las variables originales del paddle si existe
        if (paddle != null)
        {
            originalAimAssist = paddle.AimAssistStrength;
            originalBaseHitSpeed = paddle.BaseHitSpeed;
            originalMaxHitSpeed = paddle.MaxHitSpeed;
        }

        // Guardar color original y asegurar que la estela esté desactivada
        if (paddleRenderer != null)
        {
            originalColor = paddleRenderer.material.color;
        }
        
        if (powerTrail != null) powerTrail.emitting = false;
        if (powerReadyParticles != null) powerReadyParticles.Stop();

        // Inicializar UI
        UpdateUI();
    }

    void OnEnable()
    {
        // Suscribirse al evento de golpe
        PaddleFlickController.OnPlayerHit += HandlePlayerHit;
        
        // Suscribirse al input de activación
        if (activatePowerInput != null) 
            activatePowerInput.action.performed += TryActivatePower;
    }

    void OnDisable()
    {
        // Limpiar suscripciones para evitar errores
        PaddleFlickController.OnPlayerHit -= HandlePlayerHit;
        
        if (activatePowerInput != null) 
            activatePowerInput.action.performed -= TryActivatePower;
    }

    private void HandlePlayerHit()
    {
        if (isPowerActive)
        {
            // Reproducir sonido especial de poder (si está asignado)
            if (audioSource != null && powerHitSound != null)
            {
                audioSource.PlayOneShot(powerHitSound);
            }

            // El poder ya estaba activo en este golpe, lo consumimos
            DeactivatePower();
            return;
        }

        // Buscar el poder actualmente seleccionado en la lista
        PowerConfig currentPower = powers.Find(p => p.type == selectedPower);
        
        if (currentPower != null && !currentPower.isReady)
        {
            currentPower.currentHits++;

            if (currentPower.currentHits >= currentPower.hitsRequired)
            {
                currentPower.currentHits = currentPower.hitsRequired; // Evitar que sobrepase el límite
                currentPower.isReady = true;
                Debug.Log($"[PowerManager] ¡El poder '{currentPower.powerName}' está CARGADO! Presiona el gatillo para activarlo.");
            }
            
            // Actualizar la interfaz
            UpdateUI();
        }
    }

    private void TryActivatePower(InputAction.CallbackContext context)
    {
        PowerConfig currentPower = powers.Find(p => p.type == selectedPower);

        if (currentPower != null && currentPower.isReady && !isPowerActive)
        {
            ActivatePower(currentPower);
        }
        else if (currentPower != null && !currentPower.isReady)
        {
            Debug.Log("[PowerManager] El poder aún no está listo. Faltan golpes.");
        }
    }

    private void ActivatePower(PowerConfig power)
    {
        isPowerActive = true;

        Debug.Log($"[PowerManager] ¡Poder '{power.powerName}' ACTIVADO! El próximo golpe tendrá el efecto.");

        if (power.type == PowerType.SuperGolpe && paddle != null)
        {
            // Modificar el paddle temporalmente para dar el super golpe
            paddle.AimAssistStrength = superHitAimAssist;
            paddle.BaseHitSpeed = superHitSpeed;
            paddle.MaxHitSpeed = superHitSpeed;

            // --- EFECTOS VISUALES AL ACTIVAR ---
            if (paddleRenderer != null)
            {
                paddleRenderer.material.color = superHitColor;
            }
            if (powerReadyParticles != null)
            {
                powerReadyParticles.Play();
            }
            if (powerTrail != null)
            {
                powerTrail.emitting = true;
            }
        }
        else
        {
            // Logica para los poderes placeholder
            Debug.Log($"[PowerManager] Efecto del '{power.powerName}' activado (Placeholder sin efecto real aún).");
        }
    }

    private void DeactivatePower()
    {
        isPowerActive = false;

        // Reiniciar el poder
        PowerConfig currentPower = powers.Find(p => p.type == selectedPower);
        if (currentPower != null)
        {
            currentPower.currentHits = 0;
            currentPower.isReady = false;
        }

        // Actualizar UI
        UpdateUI();

        if (selectedPower == PowerType.SuperGolpe && paddle != null)
        {
            // Restaurar los valores del paddle
            paddle.AimAssistStrength = originalAimAssist;
            paddle.BaseHitSpeed = originalBaseHitSpeed;
            paddle.MaxHitSpeed = originalMaxHitSpeed;
            
            // --- RESTAURAR EFECTOS VISUALES AL CONSUMIR ---
            if (paddleRenderer != null)
            {
                paddleRenderer.material.color = originalColor;
            }
            if (powerReadyParticles != null)
            {
                powerReadyParticles.Stop();
            }
            if (powerTrail != null)
            {
                powerTrail.emitting = false;
            }

            Debug.Log("[PowerManager] Poder consumido. Restaurando parámetros normales de la pala.");
        }
    }

    private void UpdateUI()
    {
        PowerConfig currentPower = powers.Find(p => p.type == selectedPower);
        if (currentPower == null) return;

        // Actualizar el Texto
        if (powerMeterText != null)
        {
            if (currentPower.isReady)
            {
                powerMeterText.text = "¡PODER LISTO!";
                powerMeterText.color = Color.green;
            }
            else
            {
                powerMeterText.text = $"Carga: {currentPower.currentHits} / {currentPower.hitsRequired}";
                powerMeterText.color = Color.white;
            }
        }

        // Actualizar el Slider / Barra de progreso
        if (powerMeterSlider != null)
        {
            powerMeterSlider.maxValue = currentPower.hitsRequired;
            powerMeterSlider.value = currentPower.currentHits;
        }
    }
}
