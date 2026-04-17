using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections;

public class TitleScreenManager : MonoBehaviour
{
    [Header("Configuración de Escena")]
    [Tooltip("El nombre exacto de tu escena principal del juego")]
    public string mainSceneName = "SampleScene"; 

    [Header("Efectos Visuales")]
    [Tooltip("El texto que dice 'Presiona el gatillo para comenzar'")]
    public TextMeshProUGUI promptText;
    public float pulseSpeed = 2f; // Velocidad del parpadeo

    [Header("Input de VR")]
    [Tooltip("El gatillo de la mano izquierda")]
    public InputActionReference leftTrigger;
    [Tooltip("El gatillo de la mano derecha")]
    public InputActionReference rightTrigger;

    private bool isLoading = false; // Evita cargar la escena dos veces si presionas ambos gatillos

    void Start()
    {
        // Iniciar el efecto visual
        if (promptText != null)
        {
            StartCoroutine(PulseTextAlpha());
        }
    }

    void OnEnable()
    {
        // Suscribirse a los botones
        if (leftTrigger != null) leftTrigger.action.performed += StartGame;
        if (rightTrigger != null) rightTrigger.action.performed += StartGame;
    }

    void OnDisable()
    {
        // Limpiar la suscripción para evitar errores
        if (leftTrigger != null) leftTrigger.action.performed -= StartGame;
        if (rightTrigger != null) rightTrigger.action.performed -= StartGame;
    }

    private void StartGame(InputAction.CallbackContext context)
    {
        if (!isLoading)
        {
            isLoading = true;
            Debug.Log("Cargando escena principal...");
            SceneManager.LoadScene(mainSceneName);
        }
    }

    private IEnumerator PulseTextAlpha()
    {
        while (!isLoading)
        {
            // Usamos Mathf.Sin para crear una curva suave de opacidad entre 0.3 y 1.0
            float alpha = (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f; // Rango de 0 a 1
            alpha = Mathf.Lerp(0.1f, 1f, alpha); // Ajustamos para que nunca se vuelva invisible del todo

            promptText.color = new Color(promptText.color.r, promptText.color.g, promptText.color.b, alpha);
            
            yield return null; // Esperar al siguiente frame
        }
    }
}