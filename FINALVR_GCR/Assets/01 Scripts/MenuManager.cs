using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro; // Necesario para modificar textos

public class MenuManager : MonoBehaviour
{
    [Header("Referencias de UI")]
    public GameObject mainMenuPanel;
    public GameObject settingsMenuPanel;
    public GameObject uiRoot;

    [Header("Referencias del Juego")]
    public GameObject pingPongBall;
    public BallSummoner ballSummoner;
    
    [Header("Configuración para Zurdos")]
    public GameObject rightHandPaddle;
    public GameObject leftHandPaddle;
    public GameObject rightControllerModel; // Opcional: para ocultar el modelo del controlador si el jugador es zurdo
    public GameObject leftControllerModel;  // Opcional: para ocultar el modelo del controlador si el jugador es zurdo

    [Header("Configuración de Altura (NUEVO)")]
    [Tooltip("El objeto 'Camera Offset' dentro de tu XR Origin")]
    public Transform cameraOffset;
    [Tooltip("El texto donde mostraremos '1.70 m'")]
    public TextMeshProUGUI heightDisplayText; 
    
    // Asumimos que 0 es el suelo base real del jugador. 
    // Usaremos un rango de compensación, por ejemplo de -0.5m a +0.5m.
    private float baseReferenceHeight = 1.70f; 

    [Header("Input de Pausa")]
    public InputActionReference menuButtonAction;

    private bool isGamePaused = true;

    void Start()
    {
        settingsMenuPanel.SetActive(false);
        pingPongBall.SetActive(false);
        uiRoot.SetActive(true);
        FreezeGame(true);
        
        // Inicializar el texto de altura (asumiendo que el slider empieza en el centro/0 offset)
        UpdateHeightText(0f);
    }

    void OnEnable()
    {
        if (menuButtonAction != null)
            menuButtonAction.action.performed += ToggleMenuButton;
    }

    void OnDisable()
    {
        if (menuButtonAction != null)
            menuButtonAction.action.performed -= ToggleMenuButton;
    }

    // --- FUNCIONES PARA LOS BOTONES ---

    // 1. Btn_Start
    public void StartGame()
    {
        uiRoot.SetActive(false);      // Ocultar el menú
        pingPongBall.SetActive(true); // Aparecer la pelota
        FreezeGame(false);            // Descongelar el juego
    }

    // 2. Btn_Settings (Abre el placeholder)
    public void OpenSettings()
    {
        mainMenuPanel.SetActive(false);
        settingsMenuPanel.SetActive(true);
    }

    // Botón para volver de Settings a Main Menu
    public void CloseSettings()
    {
        settingsMenuPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    // 3. VolumeSlider
    public void SetVolume(float volume)
    {
        // AudioListener controla el volumen global de Unity (va de 0.0 a 1.0)
        AudioListener.volume = volume;
    }

    // 4. Lst_Toggle (Lefty)
    public void ToggleLefty(bool isLefty)
{
    // Avisarle al Summoner de qué lado está jugando
        if (ballSummoner != null)
        {
            ballSummoner.isLefty = isLefty;
        }
        if (isLefty)
        {
            rightHandPaddle.SetActive(false);
            leftHandPaddle.SetActive(true);
            leftControllerModel.SetActive(false);
            rightControllerModel.SetActive(true); // Opcional: ocultar el modelo del controlador derecho
        }
        else
        {
            rightHandPaddle.SetActive(true);
            leftHandPaddle.SetActive(false);
            leftControllerModel.SetActive(true); // Opcional: mostrar el modelo del controlador izquierdo
            rightControllerModel.SetActive(false); // Opcional: ocultar el modelo del controlador derecho
        }
    }

    // 5. Btn_Exit
    public void ExitGame()
    {
        Debug.Log("Saliendo del juego...");
        Application.Quit(); // Nota: Esto no hace nada en el Editor, solo en la build final.
    }
  // 6. HeightSlider
    public void SetPlayerHeightOffset(float offsetValue)
    {
        if (cameraOffset != null)
        {
            // Mantenemos X y Z igual, solo sumamos el offset al eje Y local
            Vector3 currentPos = cameraOffset.localPosition;
            cameraOffset.localPosition = new Vector3(currentPos.x, offsetValue, currentPos.z);
        }
        
        UpdateHeightText(offsetValue);
    }

    private void UpdateHeightText(float offsetValue)
    {
        if (heightDisplayText != null)
        {
            // Simulamos una altura visual para el jugador sumando el offset a una base promedio
            float simulatedHeight = baseReferenceHeight + offsetValue;
            
            // "F2" formatea el número a 2 decimales (ej: 1.75)
            heightDisplayText.text = simulatedHeight.ToString("F2") + " m"; 
        }
    }

    // --- LÓGICA DE PAUSA Y BOTÓN DEL MANDO ---

    private void ToggleMenuButton(InputAction.CallbackContext context)
    {
        // Si el menú está activo, lo cerramos. Si está cerrado, lo abrimos y pausamos.
        bool isMenuCurrentlyActive = uiRoot.activeSelf;
        
        if (isMenuCurrentlyActive)
        {
            StartGame(); // Reanudamos
        }
        else
        {
            uiRoot.SetActive(true);
            mainMenuPanel.SetActive(true);
            settingsMenuPanel.SetActive(false);
            FreezeGame(true);
        }
    }

    private void FreezeGame(bool freeze)
    {
        isGamePaused = freeze;
        // Time.timeScale congela las físicas (la pelota se detiene en el aire). 
        // El tracking de VR (las manos) sigue funcionando normalmente a escala 0.
        Time.timeScale = freeze ? 0f : 1f; 
    }
}