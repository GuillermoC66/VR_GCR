using UnityEngine;
using UnityEngine.InputSystem;
using TMPro; // Necesario para modificar textos

public class MenuManager : MonoBehaviour
{
    public const string RivalDifficultyPrefKey = "PingPong_RivalDifficulty";

    [Header("Referencias de UI")]
    public GameObject mainMenuPanel;
    public GameObject settingsMenuPanel;
    public GameObject uiRoot;

    [Header("Referencias del Juego")]
    public GameObject pingPongBall;
    public BallSummoner ballSummoner;

    [Header("Sistema de Rivales y Partido")]
    public RivalAIController rivalAI;
    public MatchReferee matchReferee;

    [Header("Selector de Dificultad")]
    public UnityEngine.UI.Button easyDifficultyButton;
    public UnityEngine.UI.Button mediumDifficultyButton;
    public UnityEngine.UI.Button hardDifficultyButton;
    public TextMeshProUGUI selectedDifficultyText;
    public TextMeshProUGUI difficultyDescriptionText;
    public Color selectedButtonColor = new Color(0.20f, 0.72f, 0.38f, 1f);
    public Color unselectedButtonColor = new Color(0.22f, 0.25f, 0.32f, 0.92f);

    [Header("Configuración para Zurdos")]
    public GameObject rightHandPaddle;
    public GameObject leftHandPaddle;
    public GameObject rightControllerModel; // Opcional: para ocultar el modelo del controlador si el jugador es zurdo
    public GameObject leftControllerModel;  // Opcional: para ocultar el modelo del controlador si el jugador es zurdo

    [Header("Configuración de Audio")]
    [Tooltip("Arrastra aquí el AudioSource que reproduce tu música de fondo")]
    public AudioSource backgroundMusic;

    // Variable estática para que la pelota y la pala puedan leer el volumen de los efectos
    public static float sfxVolume = 1f;

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
    private XRControllerLayoutManager layoutManager;
    private RivalDifficulty selectedDifficulty = RivalDifficulty.Medium;
    private bool hasStartedFirstMatch = false;
    private bool difficultyChangedSinceLastPlay = false;

    public RivalDifficulty SelectedDifficulty => selectedDifficulty;

    void Start()
    {
        BindLayoutManager();
        ResolveMatchReferences();

        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(false);
        if (matchReferee != null && matchReferee.matchEndPanel != null)
        {
            matchReferee.matchEndPanel.SetActive(false);
        }
        if (pingPongBall != null) pingPongBall.SetActive(false);
        if (uiRoot != null) uiRoot.SetActive(true);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        FreezeGame(true);

        if (layoutManager != null)
        {
            layoutManager.EnterPauseMenu();
        }

        // Cargar dificultad persistente y reflejarla en rival y UI
        int savedDiff = PlayerPrefs.GetInt(RivalDifficultyPrefKey, (int)RivalDifficulty.Medium);
        selectedDifficulty = (RivalDifficulty)Mathf.Clamp(savedDiff, (int)RivalDifficulty.Easy, (int)RivalDifficulty.Hard);
        ApplySelectedDifficultyToRival();
        RefreshDifficultyUI();

        if (rivalAI != null)
        {
            rivalAI.SetMatchActive(false);
        }

        // Inicializar el texto de altura (asumiendo que el slider empieza en el centro/0 offset)
        UpdateHeightText(0f);
    }

    void OnEnable()
    {
        BindLayoutManager();

        if (layoutManager == null && menuButtonAction != null)
            menuButtonAction.action.performed += ToggleMenuButton;
    }

    void OnDisable()
    {
        UnbindLayoutManager();

        if (menuButtonAction != null)
            menuButtonAction.action.performed -= ToggleMenuButton;
    }

    private void ResolveMatchReferences()
    {
        if (rivalAI == null)
        {
            rivalAI = FindFirstObjectByType<RivalAIController>();
        }

        if (matchReferee == null)
        {
            matchReferee = FindFirstObjectByType<MatchReferee>();
        }

        if (matchReferee != null && rivalAI != null)
        {
            matchReferee.BindRival(rivalAI);
        }
    }

    private void BindLayoutManager()
    {
        if (layoutManager != null) return;

        layoutManager = XRControllerLayoutManager.Instance;
        if (layoutManager == null) return;

        // Evitar doble disparo si el fallback legado quedó suscrito
        if (menuButtonAction != null)
            menuButtonAction.action.performed -= ToggleMenuButton;

        layoutManager.OnPauseTogglePressed += ToggleMenuFromLayout;
        layoutManager.OnBackPressed += HandleBackFromLayout;
    }

    private void UnbindLayoutManager()
    {
        if (layoutManager == null) return;

        layoutManager.OnPauseTogglePressed -= ToggleMenuFromLayout;
        layoutManager.OnBackPressed -= HandleBackFromLayout;
        layoutManager = null;
    }

    // --- SELECTOR DE DIFICULTAD Y FLUJO DE PARTIDO ---

    public void SelectDifficulty(int difficultyIndex)
    {
        int clamped = Mathf.Clamp(difficultyIndex, (int)RivalDifficulty.Easy, (int)RivalDifficulty.Hard);
        RivalDifficulty newDiff = (RivalDifficulty)clamped;

        if (newDiff != selectedDifficulty)
        {
            selectedDifficulty = newDiff;
            difficultyChangedSinceLastPlay = true;
        }

        PlayerPrefs.SetInt(RivalDifficultyPrefKey, clamped);
        PlayerPrefs.Save();

        ResolveMatchReferences();
        ApplySelectedDifficultyToRival();
        RefreshDifficultyUI();
    }

    public void SelectEasy() => SelectDifficulty((int)RivalDifficulty.Easy);
    public void SelectMedium() => SelectDifficulty((int)RivalDifficulty.Medium);
    public void SelectHard() => SelectDifficulty((int)RivalDifficulty.Hard);

    public void StartNewMatchWithSelectedDifficulty()
    {
        ResolveMatchReferences();

        if (uiRoot != null) uiRoot.SetActive(false);
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(false);
        if (matchReferee != null && matchReferee.matchEndPanel != null)
        {
            matchReferee.matchEndPanel.SetActive(false);
        }
        if (pingPongBall != null) pingPongBall.SetActive(true);

        ApplySelectedDifficultyToRival();

        if (matchReferee != null)
        {
            matchReferee.ResetMatch();
        }
        else if (rivalAI != null)
        {
            rivalAI.ResetForNewMatch();
            rivalAI.SetMatchActive(true);
        }

        hasStartedFirstMatch = true;
        difficultyChangedSinceLastPlay = false;

        FreezeGame(false);

        if (layoutManager != null)
        {
            layoutManager.ExitPauseMenu();
        }
    }

    public void Rematch()
    {
        StartNewMatchWithSelectedDifficulty();
    }

    public void OpenDifficultyMenu()
    {
        ResolveMatchReferences();

        if (matchReferee != null && matchReferee.matchEndPanel != null)
        {
            matchReferee.matchEndPanel.SetActive(false);
        }

        if (uiRoot != null) uiRoot.SetActive(true);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(false);

        if (rivalAI != null)
        {
            rivalAI.SetMatchActive(false);
        }

        RefreshDifficultyUI();
        FreezeGame(true);

        if (layoutManager != null)
        {
            layoutManager.EnterPauseMenu();
        }
    }

    public void ReturnToMainMenu()
    {
        OpenDifficultyMenu();
    }

    private void ApplySelectedDifficultyToRival()
    {
        if (rivalAI != null)
        {
            rivalAI.SetDifficulty(selectedDifficulty);
        }
    }

    private void RefreshDifficultyUI()
    {
        RivalDifficultyProfile activeDiffProfile = null;
        if (rivalAI != null)
        {
            if (rivalAI.ActiveProfile != null)
            {
                activeDiffProfile = rivalAI.ActiveProfile.GetProfile(selectedDifficulty);
            }
            else if (rivalAI.ActiveDifficultyProfile != null && rivalAI.ActiveDifficultyProfile.difficulty == selectedDifficulty)
            {
                activeDiffProfile = rivalAI.ActiveDifficultyProfile;
            }
        }

        Color activeHighlight = activeDiffProfile != null ? activeDiffProfile.accentColor : selectedButtonColor;

        SetDifficultyButtonHighlight(easyDifficultyButton, selectedDifficulty == RivalDifficulty.Easy, activeHighlight);
        SetDifficultyButtonHighlight(mediumDifficultyButton, selectedDifficulty == RivalDifficulty.Medium, activeHighlight);
        SetDifficultyButtonHighlight(hardDifficultyButton, selectedDifficulty == RivalDifficulty.Hard, activeHighlight);

        string label = activeDiffProfile != null && !string.IsNullOrEmpty(activeDiffProfile.displayLabel)
            ? activeDiffProfile.displayLabel
            : GetDefaultDifficultyLabel(selectedDifficulty);

        string desc = activeDiffProfile != null && !string.IsNullOrEmpty(activeDiffProfile.description)
            ? activeDiffProfile.description
            : GetDefaultDifficultyDescription(selectedDifficulty);

        if (selectedDifficultyText != null)
        {
            selectedDifficultyText.text = $"Dificultad: {label}";
        }

        if (difficultyDescriptionText != null)
        {
            difficultyDescriptionText.text = desc;
        }
    }

    private void SetDifficultyButtonHighlight(UnityEngine.UI.Button button, bool isSelected, Color highlightColor)
    {
        if (button == null) return;

        Color targetColor = isSelected ? highlightColor : unselectedButtonColor;

        UnityEngine.UI.Image img = button.targetGraphic as UnityEngine.UI.Image;
        if (img == null) img = button.GetComponent<UnityEngine.UI.Image>();
        if (img != null)
        {
            img.color = targetColor;
        }

        // Mantener normalColor en blanco para evitar multiplicar dos veces targetColor con Graphic.color
        UnityEngine.UI.ColorBlock cb = button.colors;
        cb.normalColor = Color.white;
        cb.selectedColor = Color.white;
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.90f);
        cb.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
        button.colors = cb;
    }

    private static string GetDefaultDifficultyLabel(RivalDifficulty difficulty)
    {
        switch (difficulty)
        {
            case RivalDifficulty.Easy: return "Fácil";
            case RivalDifficulty.Hard: return "Difícil";
            default: return "Medio";
        }
    }

    private static string GetDefaultDifficultyDescription(RivalDifficulty difficulty)
    {
        switch (difficulty)
        {
            case RivalDifficulty.Easy:
                return "Reacción relajada, golpes al centro y poder especial poco frecuente.";
            case RivalDifficulty.Hard:
                return "Lectura temprana, tiros a las esquinas, slices con curva y poder frecuente.";
            default:
                return "Ritmo equilibrado con drives, slices ocasionales y uso moderado de poder.";
        }
    }

    // --- FUNCIONES PARA LOS BOTONES ---

    // 1. Btn_Start
    public void StartGame()
    {
        ResolveMatchReferences();

        if (!hasStartedFirstMatch || difficultyChangedSinceLastPlay || (matchReferee != null && matchReferee.IsMatchOver))
        {
            StartNewMatchWithSelectedDifficulty();
            return;
        }

        if (uiRoot != null) uiRoot.SetActive(false);      // Ocultar el menú
        if (matchReferee != null && matchReferee.matchEndPanel != null)
        {
            matchReferee.matchEndPanel.SetActive(false);
        }
        if (pingPongBall != null) pingPongBall.SetActive(true); // Aparecer la pelota
        if (rivalAI != null) rivalAI.SetMatchActive(true);
        FreezeGame(false);            // Descongelar el juego

        if (layoutManager != null)
        {
            layoutManager.ExitPauseMenu();
        }
    }

    // 2. Btn_Settings (Abre el placeholder)
    public void OpenSettings()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(true);
    }

    // Botón para volver de Settings a Main Menu
    public void CloseSettings()
    {
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    // 3. VolumeSlider (Música)
    public void SetVolume(float volume)
    {
        if (backgroundMusic != null)
        {
            backgroundMusic.volume = volume;
        }
    }

    // Nuevo Slider (Efectos de Sonido - SFX)
    public void SetSFXVolume(float volume)
    {
        sfxVolume = volume;
    }

    // 4. Lst_Toggle (Lefty)
    public void ToggleLefty(bool isLefty)
    {
        // Avisarle al Summoner de qué lado está jugando
        if (ballSummoner != null)
        {
            ballSummoner.isLefty = isLefty;
        }

        // Si hay XRControllerLayoutManager, delegamos el espejo y guardado en PlayerPrefs
        if (layoutManager != null)
        {
            layoutManager.SetLefty(isLefty);
            return;
        }

        if (isLefty)
        {
            if (rightHandPaddle != null) rightHandPaddle.SetActive(false);
            if (leftHandPaddle != null) leftHandPaddle.SetActive(true);
            if (leftControllerModel != null) leftControllerModel.SetActive(false);
            if (rightControllerModel != null) rightControllerModel.SetActive(true);
        }
        else
        {
            if (rightHandPaddle != null) rightHandPaddle.SetActive(true);
            if (leftHandPaddle != null) leftHandPaddle.SetActive(false);
            if (leftControllerModel != null) leftControllerModel.SetActive(true);
            if (rightControllerModel != null) rightControllerModel.SetActive(false);
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
        ToggleMenuFromLayout();
    }

    private void ToggleMenuFromLayout()
    {
        if (uiRoot == null) return;

        // Si el menú está activo, lo cerramos (si el partido no terminó). Si está cerrado, lo abrimos y pausamos.
        bool isMenuCurrentlyActive = uiRoot.activeSelf;

        if (isMenuCurrentlyActive)
        {
            if (matchReferee != null && matchReferee.IsMatchOver)
            {
                return;
            }
            StartGame(); // Reanudamos y restauramos layout previo
        }
        else
        {
            OpenDifficultyMenu();
        }
    }

    private void HandleBackFromLayout()
    {
        if (uiRoot == null || !uiRoot.activeSelf) return;

        if (settingsMenuPanel != null && settingsMenuPanel.activeSelf)
        {
            CloseSettings();
        }
        else if (matchReferee == null || !matchReferee.IsMatchOver)
        {
            StartGame();
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
