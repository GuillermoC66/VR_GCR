using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

public enum ControllerLayoutMode
{
    Menu,
    Lobby,
    Match
}

/// <summary>
/// Administra los 3 controller layouts de XRI (Menu, Lobby, Match) en la escena actual.
/// Controla Action Maps, Interactors de XRI 3.3, Locomoción, modelos de pala/control,
/// modo zurdo persistente (PlayerPrefs) y movimiento lateral suave durante los partidos.
/// </summary>
[DefaultExecutionOrder(-300)]
public class XRControllerLayoutManager : MonoBehaviour
{
    private const string LeftyPrefKey = "PingPong_IsLefty";

    // Nombres de mapas en XRI Default Input Actions
    private static readonly string[] XriAlwaysEnabledMaps = { "XRI Head", "XRI Left", "XRI Right" };
    private static readonly string[] XriInteractionMaps = { "XRI Left Interaction", "XRI Right Interaction", "XRI UI" };
    private static readonly string[] XriLocomotionMaps = { "XRI Left Locomotion", "XRI Right Locomotion" };

    // Nombres de mapas en PingPongInputActions
    private const string MapMenu = "Layout_Menu";
    private const string MapLobby = "Layout_Lobby";
    private const string MapMatchRight = "Layout_Match_RightHanded";
    private const string MapMatchLeft = "Layout_Match_LeftHanded";

    public static XRControllerLayoutManager Instance { get; private set; }

    [Header("Configuración de Escena")]
    [Tooltip("Layout inicial al cargar esta escena (Menu en Title, Lobby en HUB, Match en Partidos).")]
    public ControllerLayoutMode defaultLayout = ControllerLayoutMode.Match;

    [Tooltip("Si está activo, carga y guarda la preferencia de modo zurdo entre escenas.")]
    public bool syncLeftyWithPlayerPrefs = true;

    [Header("Assets de Input Actions")]
    [Tooltip("Arrastra Assets/05 Input/PingPongInputActions.inputactions")]
    public InputActionAsset pingPongInputActions;

    [Tooltip("Arrastra XRI Default Input Actions.inputactions (Starter Assets)")]
    public InputActionAsset xriDefaultInputActions;

    [Header("XR Rig e Interactors (XRI 3.3)")]
    public XROrigin xrOrigin;
    public NearFarInteractor[] nearFarInteractors;
    public XRPokeInteractor[] pokeInteractors;
    [Tooltip("Interactors dedicados al teletransporte (XRRayInteractor en hijos Teleport Interactor).")]
    public XRRayInteractor[] teleportInteractors;

    [Header("Proveedores de Locomoción (Lobby)")]
    public ContinuousMoveProvider continuousMoveProvider;
    public SnapTurnProvider snapTurnProvider;
    public ContinuousTurnProvider continuousTurnProvider;
    public TeleportationProvider teleportationProvider;

    [Header("Visuales de Mano / Pala (Partidos y Pausa)")]
    public GameObject rightHandPaddle;
    public GameObject leftHandPaddle;
    public GameObject rightControllerModel;
    public GameObject leftControllerModel;

    [Header("Manos VR (OpenXRCustomHandPrefab)")]
    [Tooltip("Controlador de mano virtual derecha (reemplaza al modelo 3D del mando derecho).")]
    public VRHandController rightHandVisual;

    [Tooltip("Controlador de mano virtual izquierda (reemplaza al modelo 3D del mando izquierdo).")]
    public VRHandController leftHandVisual;

    [Tooltip("Opcional: Prefab Variant de mano derecha para auto-instanciación de respaldo en runtime.")]
    public GameObject rightHandPrefab;

    [Tooltip("Opcional: Prefab Variant de mano izquierda para auto-instanciación de respaldo en runtime.")]
    public GameObject leftHandPrefab;

    /// <summary>
    /// Indica si esta escena corresponde a un partido con palas asignadas (Practice_Match o Practice_Bot_Scene).
    /// </summary>
    public bool IsMatchScene => defaultLayout == ControllerLayoutMode.Match && (rightHandPaddle != null || leftHandPaddle != null);

    [Header("Movimiento Lateral en Partido (Stick Mano No Dominante)")]
    [Tooltip("Velocidad del desplazamiento lateral suave en m/s durante un partido.")]
    public float matchLateralSpeed = 1.6f;

    [Tooltip("Distancia máxima hacia izquierda o derecha respecto a la posición inicial frente a la mesa.")]
    public float maxLateralOffset = 1.2f;

    [Range(0.05f, 0.5f)]
    public float lateralDeadzone = 0.15f;

    [Tooltip("Opcional: referencia cuya dirección 'right' define el eje lateral de la mesa. Si es null usa la orientación inicial del XR Origin.")]
    public Transform lateralReferenceTransform;

    // --- Estado Público ---
    public ControllerLayoutMode CurrentLayout { get; private set; }
    public ControllerLayoutMode PreviousLayout { get; private set; }
    public bool IsLefty { get; private set; }

    // --- Eventos de Layout ---
    public event Action<ControllerLayoutMode> OnLayoutChanged;
    public event Action<bool> OnLeftyChanged;

    // --- Eventos de Acciones ---
    public event Action OnMenuConfirm;
    public event Action OnBackPressed;
    public event Action OnPauseTogglePressed;
    public event Action OnSummonBallStarted;
    public event Action OnSummonBallCanceled;
    public event Action OnAutoServePressed;

    // --- Lecturas en Tiempo Real (Partidos) ---
    public bool IsSmashPressed => CurrentLayout == ControllerLayoutMode.Match && activeSmashAction != null && activeSmashAction.IsPressed();
    public bool IsSlicePressed => CurrentLayout == ControllerLayoutMode.Match && activeSliceAction != null && activeSliceAction.IsPressed();
    public bool IsSuperComboPressed => IsSmashPressed && IsSlicePressed;
    public Vector2 MatchLateralInput => CurrentLayout == ControllerLayoutMode.Match && activeLateralMoveAction != null
        ? activeLateralMoveAction.ReadValue<Vector2>()
        : Vector2.zero;

    // Referencias internas a acciones activas
    private InputAction menuConfirmAction;
    private InputAction menuBackAction;
    private InputAction menuPauseAction;
    private InputAction lobbyBackAction;
    private InputAction lobbyPauseAction;

    private InputAction activeSmashAction;
    private InputAction activeSliceAction;
    private InputAction activeSummonAction;
    private InputAction activeAutoServeAction;
    private InputAction activeLateralMoveAction;
    private InputAction activeMatchPauseAction;

    // Estado interno para movimiento lateral
    private Vector3 matchLateralAxis = Vector3.right;
    private float currentLateralOffset = 0f;
    private bool hasInitializedOriginPos = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[XRControllerLayoutManager] Ya existe una instancia activa en la escena; reemplazando referencia.");
        }
        Instance = this;

        if (xrOrigin == null)
        {
            xrOrigin = GetComponentInParent<XROrigin>();
            if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();
        }

        if (nearFarInteractors == null || nearFarInteractors.Length == 0)
        {
            AutoDetectReferences();
        }

        if (syncLeftyWithPlayerPrefs)
        {
            IsLefty = PlayerPrefs.GetInt(LeftyPrefKey, 0) == 1;
        }

        // Garantizar desde Awake que XRInputModalityManager no apague Left/Right Controller y que ambas manos inicien prendidas
        EnsureControllersAndHandsActive();

        PreviousLayout = defaultLayout;
        CacheStaticActions();
    }

    private void OnEnable()
    {
        EnsureControllersAndHandsActive();
        SubscribeStaticActions();
    }

    private void Start()
    {
        CaptureMatchBasePosition();

        // Ejecutamos en Start para garantizar que corra después de InputActionManager.OnEnable() de XRI
        ApplyLayout(defaultLayout, recordPrevious: false);
    }

    private void OnDisable()
    {
        UnsubscribeStaticActions();
        UnsubscribeActiveMatchActions();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (CurrentLayout == ControllerLayoutMode.Match && Time.timeScale > 0f)
        {
            UpdateMatchLateralMovement();
        }
    }

    // =========================================================================
    // API PÚBLICA
    // =========================================================================

    /// <summary>
    /// Cambia el layout activo (Menu, Lobby o Match), actualizando Action Maps, Interactors y visuales.
    /// </summary>
    public void SetLayout(ControllerLayoutMode newMode)
    {
        ApplyLayout(newMode, recordPrevious: true);
    }

    /// <summary>
    /// Abre el layout de Menú recordando el layout previo (Match o Lobby) para restaurarlo al cerrar pausa.
    /// </summary>
    public void EnterPauseMenu()
    {
        if (CurrentLayout != ControllerLayoutMode.Menu)
        {
            PreviousLayout = CurrentLayout;
        }
        ApplyLayout(ControllerLayoutMode.Menu, recordPrevious: false);
    }

    /// <summary>
    /// Sale del layout de Menú y vuelve al layout previo (o al defaultLayout si el previo era Menu).
    /// </summary>
    public void ExitPauseMenu()
    {
        ControllerLayoutMode target = PreviousLayout != ControllerLayoutMode.Menu
            ? PreviousLayout
            : (defaultLayout != ControllerLayoutMode.Menu ? defaultLayout : ControllerLayoutMode.Match);

        ApplyLayout(target, recordPrevious: false);
    }

    /// <summary>
    /// Activa o desactiva el modo zurdo. En Match espejea inmediatamente los botones y modelos de mano.
    /// </summary>
    public void SetLefty(bool lefty, bool savePreference = true)
    {
        IsLefty = lefty;

        if (savePreference && syncLeftyWithPlayerPrefs)
        {
            PlayerPrefs.SetInt(LeftyPrefKey, lefty ? 1 : 0);
            PlayerPrefs.Save();
        }

        if (CurrentLayout == ControllerLayoutMode.Match)
        {
            ConfigurePingPongMaps(ControllerLayoutMode.Match);
        }

        // Actualiza pala y mano dominante inmediatamente tanto en partido como en el menú de ajustes del partido
        ApplyHandVisuals(CurrentLayout);

        OnLeftyChanged?.Invoke(IsLefty);
    }

    /// <summary>
    /// Centra nuevamente al jugador en el eje lateral frente a la mesa.
    /// </summary>
    public void ResetLateralPosition()
    {
        if (xrOrigin == null || !hasInitializedOriginPos) return;

        Vector3 pos = xrOrigin.transform.position;
        pos -= matchLateralAxis * currentLateralOffset;
        xrOrigin.transform.position = pos;
        currentLateralOffset = 0f;
    }

    /// <summary>
    /// Envía un pulso háptico al mando de la mano dominante (pala), respetando el modo zurdo.
    /// </summary>
    public void SendDominantHandHaptics(float amplitude, float duration)
    {
        UnityEngine.XR.XRNode node = IsLefty ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand;
        SendHapticsToNode(node, amplitude, duration);
    }

    /// <summary>
    /// Envía un pulso háptico al mando de la mano no dominante (pelota/soporte).
    /// </summary>
    public void SendOffHandHaptics(float amplitude, float duration)
    {
        UnityEngine.XR.XRNode node = IsLefty ? UnityEngine.XR.XRNode.RightHand : UnityEngine.XR.XRNode.LeftHand;
        SendHapticsToNode(node, amplitude, duration);
    }

    private static void SendHapticsToNode(UnityEngine.XR.XRNode node, float amplitude, float duration)
    {
        UnityEngine.XR.InputDevice device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid) return;

        if (device.TryGetHapticCapabilities(out UnityEngine.XR.HapticCapabilities capabilities) && capabilities.supportsImpulse)
        {
            device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Max(0.01f, duration));
        }
    }

    // =========================================================================
    // APLICACIÓN DE LAYOUTS
    // =========================================================================

    private void ApplyLayout(ControllerLayoutMode targetMode, bool recordPrevious)
    {
        if (recordPrevious && CurrentLayout != ControllerLayoutMode.Menu && CurrentLayout != targetMode)
        {
            PreviousLayout = CurrentLayout;
        }

        CurrentLayout = targetMode;

        ConfigureXriDefaultMaps(targetMode);
        ConfigurePingPongMaps(targetMode);
        ApplyInteractorsAndLocomotion(targetMode);
        ApplyHandVisuals(targetMode);

        OnLayoutChanged?.Invoke(CurrentLayout);
    }

    private void ConfigureXriDefaultMaps(ControllerLayoutMode mode)
    {
        if (xriDefaultInputActions == null) return;

        // Tracking siempre encendido (Head, Left, Right)
        SetMapsEnabled(xriDefaultInputActions, XriAlwaysEnabledMaps, true);

        switch (mode)
        {
            case ControllerLayoutMode.Menu:
                SetMapsEnabled(xriDefaultInputActions, XriInteractionMaps, true);
                SetMapsEnabled(xriDefaultInputActions, XriLocomotionMaps, false);
                break;

            case ControllerLayoutMode.Lobby:
                SetMapsEnabled(xriDefaultInputActions, XriInteractionMaps, true);
                SetMapsEnabled(xriDefaultInputActions, XriLocomotionMaps, true);
                break;

            case ControllerLayoutMode.Match:
                SetMapsEnabled(xriDefaultInputActions, XriInteractionMaps, false);
                SetMapsEnabled(xriDefaultInputActions, XriLocomotionMaps, false);
                break;
        }
    }

    private void ConfigurePingPongMaps(ControllerLayoutMode mode)
    {
        if (pingPongInputActions == null) return;

        UnsubscribeActiveMatchActions();

        SetMapEnabled(pingPongInputActions, MapMenu, mode == ControllerLayoutMode.Menu);
        SetMapEnabled(pingPongInputActions, MapLobby, mode == ControllerLayoutMode.Lobby);
        SetMapEnabled(pingPongInputActions, MapMatchRight, mode == ControllerLayoutMode.Match && !IsLefty);
        SetMapEnabled(pingPongInputActions, MapMatchLeft, mode == ControllerLayoutMode.Match && IsLefty);

        if (mode == ControllerLayoutMode.Match)
        {
            string activeMatchMapName = IsLefty ? MapMatchLeft : MapMatchRight;
            InputActionMap matchMap = pingPongInputActions.FindActionMap(activeMatchMapName, false);
            if (matchMap != null)
            {
                activeSmashAction = matchMap.FindAction("Smash", false);
                activeSliceAction = matchMap.FindAction("Slice", false);
                activeSummonAction = matchMap.FindAction("SummonBall", false);
                activeAutoServeAction = matchMap.FindAction("AutoServe", false);
                activeLateralMoveAction = matchMap.FindAction("LateralMove", false);
                activeMatchPauseAction = matchMap.FindAction("TogglePause", false);

                SubscribeActiveMatchActions();
            }
        }
    }

    private void ApplyInteractorsAndLocomotion(ControllerLayoutMode mode)
    {
        bool enableUIAndRay = mode == ControllerLayoutMode.Menu || mode == ControllerLayoutMode.Lobby;
        bool enableNearGrab = mode == ControllerLayoutMode.Lobby;
        bool enableLobbyLocomotion = mode == ControllerLayoutMode.Lobby;

        if (nearFarInteractors != null)
        {
            foreach (NearFarInteractor interactor in nearFarInteractors)
            {
                if (interactor == null) continue;

                interactor.enabled = enableUIAndRay;
                interactor.enableUIInteraction = enableUIAndRay;
                interactor.enableFarCasting = enableUIAndRay;
                interactor.enableNearCasting = enableNearGrab;

                // Ocultar visuales de línea en el mismo objeto para evitar rayos residuales en Match
                ToggleLineRenderers(interactor.gameObject, enableUIAndRay);
            }
        }

        if (pokeInteractors != null)
        {
            foreach (XRPokeInteractor poke in pokeInteractors)
            {
                if (poke == null) continue;
                poke.enabled = enableUIAndRay;
            }
        }

        if (teleportInteractors != null)
        {
            foreach (XRRayInteractor teleportRay in teleportInteractors)
            {
                if (teleportRay == null) continue;
                teleportRay.enabled = enableLobbyLocomotion;
                ToggleLineRenderers(teleportRay.gameObject, enableLobbyLocomotion);
            }
        }

        if (continuousMoveProvider != null) continuousMoveProvider.enabled = enableLobbyLocomotion;
        if (snapTurnProvider != null) snapTurnProvider.enabled = enableLobbyLocomotion;
        if (continuousTurnProvider != null) continuousTurnProvider.enabled = enableLobbyLocomotion;
        if (teleportationProvider != null) teleportationProvider.enabled = enableLobbyLocomotion;
    }

    private void ApplyHandVisuals(ControllerLayoutMode mode)
    {
        EnsureControllersAndHandsActive();
        bool hasVRHands = rightHandVisual != null || leftHandVisual != null;
        if (hasVRHands)
        {

            if (rightHandVisual != null && !rightHandVisual.gameObject.activeSelf)
                rightHandVisual.gameObject.SetActive(true);
            if (leftHandVisual != null && !leftHandVisual.gameObject.activeSelf)
                leftHandVisual.gameObject.SetActive(true);

            if (IsMatchScene)
            {
                // En escenas de partido, la pala y el agarre shakehand de la mano dominante se muestran SIEMPRE (desde el menú inicial, pausa y partido)
                if (rightHandPaddle != null) rightHandPaddle.SetActive(!IsLefty);
                if (leftHandPaddle != null) leftHandPaddle.SetActive(IsLefty);

                VRHandController dominantHand = IsLefty ? leftHandVisual : rightHandVisual;
                VRHandController offHand = IsLefty ? rightHandVisual : leftHandVisual;
                Transform activePaddleTransform = IsLefty
                    ? (leftHandPaddle != null ? leftHandPaddle.transform : null)
                    : (rightHandPaddle != null ? rightHandPaddle.transform : null);

                if (dominantHand != null)
                {
                    dominantHand.SetMode(HandVisualMode.PaddleShakehandGrip, activePaddleTransform);
                }

                if (offHand != null)
                {
                    HandVisualMode offHandMode = mode == ControllerLayoutMode.Match
                        ? HandVisualMode.MatchOffHandBallGrab
                        : HandVisualMode.NormalControllerInput;
                    offHand.SetMode(offHandMode, null);
                }
            }
            else
            {
                // En Lobby y Scene_Title: palas ocultas y ambas manos reaccionan normalmente a los inputs
                if (rightHandPaddle != null) rightHandPaddle.SetActive(false);
                if (leftHandPaddle != null) leftHandPaddle.SetActive(false);

                if (rightHandVisual != null) rightHandVisual.SetMode(HandVisualMode.NormalControllerInput, null);
                if (leftHandVisual != null) leftHandVisual.SetMode(HandVisualMode.NormalControllerInput, null);
            }
            return;
        }

        // Fallback legado solo si no hay manos VR en la escena
        if (mode == ControllerLayoutMode.Match)
        {
            if (rightHandPaddle != null) rightHandPaddle.SetActive(!IsLefty);
            if (leftHandPaddle != null) leftHandPaddle.SetActive(IsLefty);
            if (rightControllerModel != null) rightControllerModel.SetActive(IsLefty);
            if (leftControllerModel != null) leftControllerModel.SetActive(!IsLefty);
        }
        else
        {
            if (rightHandPaddle != null) rightHandPaddle.SetActive(false);
            if (leftHandPaddle != null) leftHandPaddle.SetActive(false);
            if (rightControllerModel != null) rightControllerModel.SetActive(true);
            if (leftControllerModel != null) leftControllerModel.SetActive(true);
        }
    }

    /// <summary>
    /// Desactiva XRInputModalityManager (que apaga Left/Right Controller cuando no detecta tracking inmediato)
    /// y fuerza que tanto Left Controller como Right Controller y sus manos VR estén encendidos desde el inicio.
    /// </summary>
    public void EnsureControllersAndHandsActive()
    {
        Transform searchRoot = xrOrigin != null ? xrOrigin.transform : transform;

        MonoBehaviour[] allBehaviours = searchRoot.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour mb in allBehaviours)
        {
            if (mb != null && mb.GetType().Name == "XRInputModalityManager" && mb.enabled)
            {
                mb.enabled = false;
            }
        }

        Transform leftControllerTr = FindControllerTransform(searchRoot, true);
        Transform rightControllerTr = FindControllerTransform(searchRoot, false);

        if (leftControllerTr != null && !leftControllerTr.gameObject.activeSelf)
        {
            leftControllerTr.gameObject.SetActive(true);
        }
        if (rightControllerTr != null && !rightControllerTr.gameObject.activeSelf)
        {
            rightControllerTr.gameObject.SetActive(true);
        }

        EnsureVRHandsInitialized();

        if (leftHandVisual != null && !leftHandVisual.gameObject.activeSelf)
        {
            leftHandVisual.gameObject.SetActive(true);
        }
        if (rightHandVisual != null && !rightHandVisual.gameObject.activeSelf)
        {
            rightHandVisual.gameObject.SetActive(true);
        }
    }

    private void EnsureVRHandsInitialized()
    {
        Transform searchRoot = xrOrigin != null ? xrOrigin.transform : transform;

        if (leftHandVisual == null || rightHandVisual == null)
        {
            VRHandController[] hands = searchRoot.GetComponentsInChildren<VRHandController>(true);
            foreach (VRHandController hand in hands)
            {
                if (hand == null) continue;
                if (hand.isLeftHand && leftHandVisual == null) leftHandVisual = hand;
                else if (!hand.isLeftHand && rightHandVisual == null) rightHandVisual = hand;
            }
        }

        if (Application.isPlaying && (leftHandVisual == null || rightHandVisual == null))
        {
            Transform leftControllerTr = FindControllerTransform(searchRoot, true);
            Transform rightControllerTr = FindControllerTransform(searchRoot, false);

            if (leftHandVisual == null && leftControllerTr != null)
            {
                GameObject prefabL = leftHandPrefab != null ? leftHandPrefab : Resources.Load<GameObject>("Hands/VRPlayerHand_L");
                if (prefabL != null)
                {
                    GameObject instL = Instantiate(prefabL, leftControllerTr, false);
                    instL.name = "VRPlayerHand_L";
                    leftHandVisual = instL.GetComponent<VRHandController>();
                }
            }

            if (rightHandVisual == null && rightControllerTr != null)
            {
                GameObject prefabR = rightHandPrefab != null ? rightHandPrefab : Resources.Load<GameObject>("Hands/VRPlayerHand_R");
                if (prefabR != null)
                {
                    GameObject instR = Instantiate(prefabR, rightControllerTr, false);
                    instR.name = "VRPlayerHand_R";
                    rightHandVisual = instR.GetComponent<VRHandController>();
                }
            }
        }
    }



    public static Transform FindControllerTransform(Transform root, bool left)
    {
        if (root == null) return null;
        string targetName = left ? "Left Controller" : "Right Controller";
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform tr in all)
        {
            if (tr != null && tr.name == targetName)
                return tr;
        }
        return null;
    }

    // =========================================================================
    // MOVIMIENTO LATERAL SUAVE EN PARTIDOS
    // =========================================================================

    private void CaptureMatchBasePosition()
    {
        if (xrOrigin == null) return;

        Transform refTransform = lateralReferenceTransform != null ? lateralReferenceTransform : xrOrigin.transform;
        Vector3 right = refTransform.right;
        right.y = 0f;
        matchLateralAxis = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
        currentLateralOffset = 0f;
        hasInitializedOriginPos = true;
    }

    private void UpdateMatchLateralMovement()
    {
        if (xrOrigin == null || activeLateralMoveAction == null) return;
        if (!hasInitializedOriginPos) CaptureMatchBasePosition();

        Vector2 stick = activeLateralMoveAction.ReadValue<Vector2>();
        if (Mathf.Abs(stick.x) < lateralDeadzone) return;

        float delta = stick.x * matchLateralSpeed * Time.deltaTime;
        float clampedTarget = Mathf.Clamp(currentLateralOffset + delta, -maxLateralOffset, maxLateralOffset);
        float appliedDelta = clampedTarget - currentLateralOffset;

        if (Mathf.Abs(appliedDelta) > 0.0001f)
        {
            xrOrigin.transform.position += matchLateralAxis * appliedDelta;
            currentLateralOffset = clampedTarget;
        }
    }

    // =========================================================================
    // SUSCRIPCIÓN DE INPUT ACTIONS
    // =========================================================================

    private void CacheStaticActions()
    {
        if (pingPongInputActions == null) return;

        InputActionMap menuMap = pingPongInputActions.FindActionMap(MapMenu, false);
        if (menuMap != null)
        {
            menuConfirmAction = menuMap.FindAction("Confirm", false);
            menuBackAction = menuMap.FindAction("Back", false);
            menuPauseAction = menuMap.FindAction("TogglePause", false);
        }

        InputActionMap lobbyMap = pingPongInputActions.FindActionMap(MapLobby, false);
        if (lobbyMap != null)
        {
            lobbyBackAction = lobbyMap.FindAction("Back", false);
            lobbyPauseAction = lobbyMap.FindAction("TogglePause", false);
        }
    }

    private void SubscribeStaticActions()
    {
        if (menuConfirmAction != null) menuConfirmAction.performed += HandleMenuConfirm;
        if (menuBackAction != null) menuBackAction.performed += HandleBackPressed;
        if (menuPauseAction != null) menuPauseAction.performed += HandlePauseToggle;
        if (lobbyBackAction != null) lobbyBackAction.performed += HandleBackPressed;
        if (lobbyPauseAction != null) lobbyPauseAction.performed += HandlePauseToggle;
    }

    private void UnsubscribeStaticActions()
    {
        if (menuConfirmAction != null) menuConfirmAction.performed -= HandleMenuConfirm;
        if (menuBackAction != null) menuBackAction.performed -= HandleBackPressed;
        if (menuPauseAction != null) menuPauseAction.performed -= HandlePauseToggle;
        if (lobbyBackAction != null) lobbyBackAction.performed -= HandleBackPressed;
        if (lobbyPauseAction != null) lobbyPauseAction.performed -= HandlePauseToggle;
    }

    private void SubscribeActiveMatchActions()
    {
        if (activeSummonAction != null)
        {
            activeSummonAction.started += HandleSummonStarted;
            activeSummonAction.canceled += HandleSummonCanceled;
        }
        if (activeAutoServeAction != null) activeAutoServeAction.performed += HandleAutoServe;
        if (activeMatchPauseAction != null) activeMatchPauseAction.performed += HandlePauseToggle;
    }

    private void UnsubscribeActiveMatchActions()
    {
        if (activeSummonAction != null)
        {
            activeSummonAction.started -= HandleSummonStarted;
            activeSummonAction.canceled -= HandleSummonCanceled;
        }
        if (activeAutoServeAction != null) activeAutoServeAction.performed -= HandleAutoServe;
        if (activeMatchPauseAction != null) activeMatchPauseAction.performed -= HandlePauseToggle;

        activeSmashAction = null;
        activeSliceAction = null;
        activeSummonAction = null;
        activeAutoServeAction = null;
        activeLateralMoveAction = null;
        activeMatchPauseAction = null;
    }

    private void HandleMenuConfirm(InputAction.CallbackContext _) => OnMenuConfirm?.Invoke();
    private void HandleBackPressed(InputAction.CallbackContext _) => OnBackPressed?.Invoke();
    private void HandlePauseToggle(InputAction.CallbackContext _) => OnPauseTogglePressed?.Invoke();
    private void HandleSummonStarted(InputAction.CallbackContext _) => OnSummonBallStarted?.Invoke();
    private void HandleSummonCanceled(InputAction.CallbackContext _) => OnSummonBallCanceled?.Invoke();
    private void HandleAutoServe(InputAction.CallbackContext _) => OnAutoServePressed?.Invoke();

    // =========================================================================
    // UTILIDADES
    // =========================================================================

    private static void SetMapsEnabled(InputActionAsset asset, string[] mapNames, bool enabled)
    {
        foreach (string mapName in mapNames)
        {
            SetMapEnabled(asset, mapName, enabled);
        }
    }

    private static void SetMapEnabled(InputActionAsset asset, string mapName, bool enabled)
    {
        InputActionMap map = asset.FindActionMap(mapName, false);
        if (map == null) return;

        if (enabled && !map.enabled) map.Enable();
        else if (!enabled && map.enabled) map.Disable();
    }

    private static void ToggleLineRenderers(GameObject target, bool enabled)
    {
        LineRenderer[] lines = target.GetComponents<LineRenderer>();
        foreach (LineRenderer lr in lines)
        {
            if (lr != null) lr.enabled = enabled;
        }
    }

    [ContextMenu("Auto-Detectar Referencias XR")]
    public void AutoDetectReferences()
    {
        if (xrOrigin == null)
        {
            xrOrigin = GetComponentInParent<XROrigin>();
            if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();
        }

        Transform searchRoot = xrOrigin != null ? xrOrigin.transform : transform;

        nearFarInteractors = searchRoot.GetComponentsInChildren<NearFarInteractor>(true);
        pokeInteractors = searchRoot.GetComponentsInChildren<XRPokeInteractor>(true);

        // Filtramos XRRayInteractor para quedarnos con los de Teleport
        XRRayInteractor[] allRays = searchRoot.GetComponentsInChildren<XRRayInteractor>(true);
        System.Collections.Generic.List<XRRayInteractor> teleports = new System.Collections.Generic.List<XRRayInteractor>();
        foreach (XRRayInteractor ray in allRays)
        {
            if (ray.gameObject.name.IndexOf("Teleport", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                teleports.Add(ray);
            }
        }
        teleportInteractors = teleports.ToArray();

        continuousMoveProvider = searchRoot.GetComponentInChildren<ContinuousMoveProvider>(true);
        snapTurnProvider = searchRoot.GetComponentInChildren<SnapTurnProvider>(true);
        continuousTurnProvider = searchRoot.GetComponentInChildren<ContinuousTurnProvider>(true);
        teleportationProvider = searchRoot.GetComponentInChildren<TeleportationProvider>(true);

        MenuManager menuManager = FindFirstObjectByType<MenuManager>();
        if (menuManager != null)
        {
            if (rightHandPaddle == null) rightHandPaddle = menuManager.rightHandPaddle;
            if (leftHandPaddle == null) leftHandPaddle = menuManager.leftHandPaddle;
            if (rightControllerModel == null) rightControllerModel = menuManager.rightControllerModel;
            if (leftControllerModel == null) leftControllerModel = menuManager.leftControllerModel;
        }

        VRHandController[] hands = searchRoot.GetComponentsInChildren<VRHandController>(true);
        foreach (VRHandController hand in hands)
        {
            if (hand == null) continue;
            if (hand.isLeftHand) leftHandVisual = hand;
            else rightHandVisual = hand;
        }
    }
}
