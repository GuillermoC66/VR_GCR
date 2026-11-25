using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Árbitro oficial de Ping Pong para PingPing PongPong VR (SampleScene).
/// Implementa reglas ITTF adaptadas a VR:
/// - Mejor de 3 sets (configurable: setsToWin, pointsToWin = 11, pointsToWinBy = 2).
/// - Alternancia de saques cada 2 puntos (cada 1 en Deuce 10-10) y alternancia de sacador inicial por set.
/// - Saque oficial de 2 botes (1 bote en lado propio -> red -> 1 bote en lado receptor) con regla de LET.
/// - Reglas de rally: punto ganador tras 1 bote en mesa rival + fuera/suelo/pared, doble bote, doble toque,
///   falta por bote en propia mesa en rally, volea vs bola ya salida detrás de la línea de fondo.
/// - Pausas breves entre puntos y sets, red de seguridad fuera de límites, avisos de DEUCE / SET POINT / MATCH POINT
///   e indicador visual 3D sobre la mesa.
/// </summary>
public class MatchReferee : MonoBehaviour
{
    public enum Hitter { None, Player, AI }

    public enum PointPhase
    {
        WaitingForServe,
        ServeAwaitingOwnBounce,
        ServeAwaitingOpponentBounce,
        RallyInProgress,
        BetweenPoints,
        BetweenSets,
        MatchOver
    }

    [Header("UI Marcador")]
    public TextMeshProUGUI scoreText;   // Puntos del set actual: "0 - 0"
    public TextMeshProUGUI statusText;  // Estado y turno: "Falta", "Punto", "Tu turno de sacar"
    [Tooltip("Opcional: texto dedicado para marcador de sets (ej. 'Sets: 1 - 0'). Si es null, se muestra junto al marcador.")]
    public TextMeshProUGUI setsText;
    [Tooltip("Opcional: banner destacado para DEUCE, SET POINT, MATCH POINT, LET.")]
    public TextMeshProUGUI calloutText;

    [Header("Reglas Oficiales (ITTF)")]
    public int pointsToWin = 11;
    public int pointsToWinBy = 2;
    [Tooltip("Sets necesarios para ganar el partido (2 = Mejor de 3, 1 = Set único, 3 = Mejor de 5).")]
    public int setsToWin = 2;

    [Header("Ritmo entre Puntos y Sets")]
    [Tooltip("Pausa breve tras cada punto para leer el resultado antes de resetear la pelota.")]
    public float delayBetweenPoints = 1.35f;
    [Tooltip("Pausa breve tras un LET antes de repetir el saque.")]
    public float delayAfterLet = 1.15f;
    [Tooltip("Pausa entre sets para celebrar el set ganado.")]
    public float delayBetweenSets = 2.25f;

    [Header("Pelota, Mesa y Seguridad Fuera de Límites")]
    public BallController ballController;
    [Tooltip("Punto donde flotará la pelota al resetear (opcional).")]
    public Transform floatSpawnPoint;
    [Tooltip("Mitad de mesa del jugador (auto-detectada por tag 'TablePlayer').")]
    public Transform playerTableTarget;
    [Tooltip("Mitad de mesa del rival (auto-detectada por tag 'TableEnemy').")]
    public Transform enemyTableTarget;
    [Tooltip("Distancia vertical por debajo de la superficie de la mesa para considerar la pelota fuera/en el suelo.")]
    public float fallOutBelowTableOffset = 0.55f;
    [Tooltip("Distancia horizontal máxima desde el centro de la mesa antes de declarar fuera automáticamente.")]
    public float maxOutOfBoundsRadius = 4.2f;
    [Tooltip("Segundos que la pelota puede quedar casi quieta en juego antes de resolver el punto automáticamente.")]
    public float stalledBallTimeout = 1.25f;
    [Tooltip("Margen extra detrás de la línea de fondo para considerar que una pelota sin botar ya iba claramente larga.")]
    public float outPastEndLineMargin = 0.04f;

    [Header("Sistema de Rivales e Indicador 3D")]
    public RivalAIController rivalAI;
    public RivalVisualController rivalVisuals;
    public ServeIndicator3D serveIndicator;

    [Header("HUD del Rival (Opcional)")]
    public TextMeshProUGUI rivalNameText;
    public TextMeshProUGUI rivalDifficultyText;
    public UnityEngine.UI.Slider rivalPowerSlider;
    public TextMeshProUGUI rivalPowerText;
    public UnityEngine.UI.Image rivalDifficultyBadge;

    [Header("UI Fin de Partido (Opcional)")]
    public GameObject matchEndPanel;
    public TextMeshProUGUI matchEndTitleText;
    public TextMeshProUGUI matchEndScoreText;

    [Header("Sonidos de Árbitro (Opcional)")]
    public AudioSource audioSource;
    public AudioClip pointWonClip;
    public AudioClip pointLostClip;
    public AudioClip letClip;
    public AudioClip setWonClip;
    public AudioClip matchWonClip;
    public AudioClip matchLostClip;

    // --- Estado del Marcador ---
    private int playerScore = 0;
    private int aiScore = 0;
    private int playerSets = 0;
    private int aiSets = 0;
    private bool setOpeningServerIsPlayer = true;
    private bool isPlayerTurnToServe = true;
    private string lastStatusMessage = string.Empty;
    private string activeCallout = string.Empty;

    // --- Estado del Punto ---
    private Hitter lastHitter = Hitter.None;
    private int bouncesOnPlayerSide = 0;
    private int bouncesOnAISide = 0;
    private bool touchedNet = false;
    private float stalledTimer = 0f;

    private RivalAIController boundRivalAI;
    private Collider playerTableCollider;
    private Collider enemyTableCollider;
    private Coroutine transitionCoroutine;

    // --- PROPIEDADES PÚBLICAS ---
    public bool IsMatchOver { get; private set; }
    public PointPhase CurrentPhase { get; private set; } = PointPhase.WaitingForServe;
    public int PlayerScore => playerScore;
    public int AIScore => aiScore;
    public int PlayerSets => playerSets;
    public int AISets => aiSets;
    public Hitter MatchWinner { get; private set; } = Hitter.None;
    public Hitter LastHitter => lastHitter;

    // --- EVENTOS DEL PARTIDO ---
    public event Action<int, int> OnScoreChanged;
    public event Action<int, int> OnSetsChanged;
    public event Action<Hitter, string> OnPointAwarded;
    public event Action<string> OnLetCalled;
    public event Action<bool, PointPhase> OnServeTurnChanged;
    public event Action<Hitter, int, int> OnMatchEnded;
    public event Action OnMatchReset;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();

        if (rivalAI != null)
        {
            BindRival(rivalAI);
        }

        if (matchEndPanel != null)
        {
            matchEndPanel.SetActive(false);
        }

        lastStatusMessage = "Esperando saque...";
        UpdateCalloutBanner();
        ResetPlayState();
        UpdateUI();
        RefreshRivalHUD();
    }

    private void OnDisable()
    {
        StopTransitionCoroutine();
    }

    private void OnDestroy()
    {
        UnbindRival();
    }

    private void Update()
    {
        if (IsMatchOver || Time.timeScale <= 0f) return;
        if (!IsBallLiveInPlay())
        {
            stalledTimer = 0f;
            return;
        }

        MonitorOutOfBoundsSafetyNet();
    }

    // --- MÉTODOS PÚBLICOS DE ESTADO (Consultados por RivalAIController, PaddleFlickController, BallSummoner) ---

    public bool GetIsPlayerTurnToServe() => isPlayerTurnToServe;

    /// <summary>
    /// True mientras el punto está en espera de saque o el saque aún está completando sus 2 botes oficiales.
    /// </summary>
    public bool GetIsServePhase()
    {
        return CurrentPhase == PointPhase.WaitingForServe ||
               CurrentPhase == PointPhase.ServeAwaitingOwnBounce ||
               CurrentPhase == PointPhase.ServeAwaitingOpponentBounce;
    }

    /// <summary>
    /// True únicamente cuando el jugador puede invocar/agarrar la pelota y ejecutar su saque.
    /// </summary>
    public bool CanPlayerSummonOrServeBall()
    {
        return !IsMatchOver && CurrentPhase == PointPhase.WaitingForServe && isPlayerTurnToServe;
    }

    /// <summary>
    /// True cuando el próximo golpe del jugador corresponde al saque oficial (2 botes).
    /// </summary>
    public bool IsPlayerServeShot()
    {
        return !IsMatchOver && CurrentPhase == PointPhase.WaitingForServe && isPlayerTurnToServe;
    }

    /// <summary>
    /// True únicamente cuando la IA debe iniciar su rutina de saque (no durante pausas entre puntos/sets).
    /// </summary>
    public bool IsAIReadyToServe()
    {
        return !IsMatchOver && CurrentPhase == PointPhase.WaitingForServe && !isPlayerTurnToServe;
    }

    /// <summary>
    /// True mientras la pelota no está en pausa entre puntos/sets ni el partido terminó.
    /// </summary>
    public bool IsPlayInputAllowed()
    {
        return !IsMatchOver &&
               CurrentPhase != PointPhase.BetweenPoints &&
               CurrentPhase != PointPhase.BetweenSets &&
               CurrentPhase != PointPhase.MatchOver;
    }

    public Vector3 GetPlayerTableSurfaceCenter() => GetTableSurfaceCenter(playerTableTarget, ref playerTableCollider);
    public Vector3 GetEnemyTableSurfaceCenter() => GetTableSurfaceCenter(enemyTableTarget, ref enemyTableCollider);

    /// <summary>
    /// Vincula un RivalAIController para mantener sincronizados el HUD del rival y la barra de poder.
    /// </summary>
    public void BindRival(RivalAIController rival)
    {
        if (boundRivalAI == rival && rival != null)
        {
            RefreshRivalHUD();
            return;
        }

        UnbindRival();

        rivalAI = rival;
        boundRivalAI = rival;

        if (boundRivalAI != null)
        {
            boundRivalAI.OnDifficultyApplied += HandleRivalDifficultyApplied;
            boundRivalAI.OnPowerStateChanged += HandleRivalPowerStateChanged;

            if (rivalVisuals == null)
            {
                rivalVisuals = boundRivalAI.GetComponent<RivalVisualController>();
                if (rivalVisuals == null)
                {
                    rivalVisuals = FindFirstObjectByType<RivalVisualController>();
                }
            }
        }

        RefreshRivalHUD();
    }

    /// <summary>
    /// Reinicia completamente el partido (sets 0-0, puntos 0-0, estado de saque, poder de jugador y rival, y pelota).
    /// </summary>
    public void ResetMatch()
    {
        StopTransitionCoroutine();
        ResolveReferences();

        if (rivalAI != null && boundRivalAI != rivalAI)
        {
            BindRival(rivalAI);
        }

        playerScore = 0;
        aiScore = 0;
        playerSets = 0;
        aiSets = 0;
        IsMatchOver = false;
        MatchWinner = Hitter.None;
        setOpeningServerIsPlayer = true;
        isPlayerTurnToServe = true;
        lastStatusMessage = "¡Nuevo partido!";
        activeCallout = string.Empty;

        if (matchEndPanel != null)
        {
            matchEndPanel.SetActive(false);
        }

        if (rivalAI != null)
        {
            rivalAI.ResetForNewMatch();
            rivalAI.SetMatchActive(true);
        }

        if (rivalVisuals != null)
        {
            rivalVisuals.ResetVisualState();
        }

        ResetPlayerPowers();
        ResetPlayState();
        UpdateCalloutBanner();
        UpdateUI();
        RefreshRivalHUD();

        OnScoreChanged?.Invoke(playerScore, aiScore);
        OnSetsChanged?.Invoke(playerSets, aiSets);
        OnMatchReset?.Invoke();
    }

    // =========================================================================
    // REGLAS DE PALAS (SAQUE, DOBLE TOQUE, VOLEA VS BOLA LARGA)
    // =========================================================================

    public void OnBallHitPaddle(Hitter hitter)
    {
        Vector3 ballPos = ballController != null ? ballController.transform.position : Vector3.zero;
        OnBallHitPaddle(hitter, ballPos);
    }

    public void OnBallHitPaddle(Hitter hitter, Vector3 ballPos)
    {
        if (!IsPlayInputAllowed() || hitter == Hitter.None) return;

        Hitter opponent = GetOpponent(hitter);

        // --- Caso 1: Inicio de Saque ---
        if (CurrentPhase == PointPhase.WaitingForServe)
        {
            bool expectedPlayerServe = isPlayerTurnToServe;
            bool hitterIsExpectedServer = (hitter == Hitter.Player && expectedPlayerServe) ||
                                          (hitter == Hitter.AI && !expectedPlayerServe);

            if (!hitterIsExpectedServer)
            {
                string msg = hitter == Hitter.Player
                    ? $"Falta: Era saque de {GetRivalDisplayName()}"
                    : "Falta rival: Era tu saque";
                AwardPoint(opponent, msg);
                return;
            }

            lastHitter = hitter;
            bouncesOnPlayerSide = 0;
            bouncesOnAISide = 0;
            touchedNet = false;
            stalledTimer = 0f;
            SetPhase(PointPhase.ServeAwaitingOwnBounce);

            if (ballController != null)
            {
                ballController.SetGrabbable(false);
            }
            return;
        }

        // --- Caso 2: Contacto durante Saque en Vuelo ---
        if (CurrentPhase == PointPhase.ServeAwaitingOwnBounce || CurrentPhase == PointPhase.ServeAwaitingOpponentBounce)
        {
            if (lastHitter == hitter)
            {
                string msg = hitter == Hitter.Player ? "Falta: Doble toque en el saque" : $"Falta de {GetRivalDisplayName()}: Doble toque";
                AwardPoint(opponent, msg);
                return;
            }

            // El receptor tocó el saque antes de que botara en su mesa:
            // Si la pelota YA rebasó la línea de fondo del receptor (saque largo), es punto para el receptor.
            if (HasBallPassedReceiverEndLine(hitter, ballPos))
            {
                string msg = hitter == Hitter.Player
                    ? "¡Saque largo! Punto para ti"
                    : $"Saque largo: Punto para {GetRivalDisplayName()}";
                AwardPoint(hitter, msg);
                return;
            }

            string volleyMsg = hitter == Hitter.Player
                ? "Falta: Volea en el saque (deja que bote)"
                : $"Falta de {GetRivalDisplayName()}: Volea en el saque";
            AwardPoint(opponent, volleyMsg);
            return;
        }

        // --- Caso 3: Rally Activo ---
        if (CurrentPhase == PointPhase.RallyInProgress)
        {
            if (lastHitter == hitter)
            {
                string msg = hitter == Hitter.Player ? "Falta: Doble toque" : $"Falta de {GetRivalDisplayName()}: Doble toque";
                AwardPoint(opponent, msg);
                return;
            }

            int bouncesOnHitterSide = hitter == Hitter.Player ? bouncesOnPlayerSide : bouncesOnAISide;
            if (lastHitter == opponent && bouncesOnHitterSide == 0)
            {
                // Regla ITTF + sentido común VR: si ya pasó la línea de fondo sin botar, ya estaba fuera -> punto al receptor
                if (HasBallPassedReceiverEndLine(hitter, ballPos))
                {
                    string longMsg = hitter == Hitter.Player
                        ? "¡Bola larga! Punto para ti"
                        : $"Bola larga: Punto para {GetRivalDisplayName()}";
                    AwardPoint(hitter, longMsg);
                    return;
                }

                string volleyMsg = hitter == Hitter.Player
                    ? "Falta: Volea no permitida"
                    : $"Falta de {GetRivalDisplayName()}: Volea";
                AwardPoint(opponent, volleyMsg);
                return;
            }

            // Golpe de devolución válido
            lastHitter = hitter;
            bouncesOnPlayerSide = 0;
            bouncesOnAISide = 0;
            touchedNet = false;
            stalledTimer = 0f;

            if (ballController != null)
            {
                ballController.SetGrabbable(false);
            }
        }
    }

    // =========================================================================
    // REGLAS DE MESA (SAQUE DE 2 BOTES, LET, BOTE PROPIO, DOBLE BOTE)
    // =========================================================================

    public void OnBallHitTable(Hitter tableSide)
    {
        if (!IsPlayInputAllowed() || tableSide == Hitter.None) return;

        stalledTimer = 0f;

        // --- Caso 1: Pelota cayó a la mesa durante un toss fallido sin golpearla ---
        if (CurrentPhase == PointPhase.WaitingForServe || lastHitter == Hitter.None)
        {
            CallFriendlyServeRetry("Saque fallido: la pelota botó sin golpearla. Intenta de nuevo.");
            return;
        }

        // --- Caso 2: Saque esperando 1er bote en lado del sacador ---
        if (CurrentPhase == PointPhase.ServeAwaitingOwnBounce)
        {
            if (tableSide == lastHitter)
            {
                IncrementBounce(tableSide);
                SetPhase(PointPhase.ServeAwaitingOpponentBounce);
            }
            else
            {
                // Saltó directamente al lado rival sin botar primero en su lado -> Falta de saque
                Hitter receiver = GetOpponent(lastHitter);
                string msg = lastHitter == Hitter.Player
                    ? "Falta de saque: Debe botar primero en tu lado"
                    : $"Falta de saque de {GetRivalDisplayName()}";
                AwardPoint(receiver, msg);
            }
            return;
        }

        // --- Caso 3: Saque esperando 2do bote en lado del receptor ---
        if (CurrentPhase == PointPhase.ServeAwaitingOpponentBounce)
        {
            if (tableSide == lastHitter)
            {
                // Doble bote en el propio lado durante el saque -> Falta
                Hitter receiver = GetOpponent(lastHitter);
                string msg = lastHitter == Hitter.Player
                    ? "Falta de saque: Doble bote en tu lado"
                    : $"Falta de {GetRivalDisplayName()}: Doble bote al sacar";
                AwardPoint(receiver, msg);
                return;
            }

            // Bote válido en la mesa del receptor
            IncrementBounce(tableSide);

            if (touchedNet)
            {
                // Regla oficial de LET: saque tocó la red pero completó 1 bote propio + 1 bote rival
                CallLet("¡LET! Rozó la red. Repite el saque.");
                return;
            }

            // Saque completado legalmente -> inicia el rally
            SetPhase(PointPhase.RallyInProgress);
            return;
        }

        // --- Caso 4: Rally en curso ---
        if (CurrentPhase == PointPhase.RallyInProgress)
        {
            if (tableSide == lastHitter)
            {
                // En rally, botar en tu propia mesa es falta inmediata
                Hitter opponent = GetOpponent(lastHitter);
                string msg = lastHitter == Hitter.Player
                    ? "Falta: La pelota botó en tu propia mesa"
                    : $"Falta de {GetRivalDisplayName()}: Botó en su mesa";
                AwardPoint(opponent, msg);
                return;
            }

            int bouncesOnReceiver = IncrementBounce(tableSide);
            if (bouncesOnReceiver >= 2)
            {
                string msg = lastHitter == Hitter.Player
                    ? "¡Doble bote! Punto para ti"
                    : $"¡Doble bote! Punto para {GetRivalDisplayName()}";
                AwardPoint(lastHitter, msg);
            }
        }
    }

    // =========================================================================
    // REGLAS DE ENTORNO (RED, SUELO, PARED, FUERA DE LÍMITES)
    // =========================================================================

    public void OnBallHitNet()
    {
        if (!IsPlayInputAllowed()) return;
        touchedNet = true;
    }

    public void OnBallHitFloor()
    {
        OnBallOutOfBounds("suelo");
    }

    public void OnBallHitWall()
    {
        OnBallOutOfBounds("pared");
    }

    public void OnBallOutOfBounds(string surfaceLabel = "fuera")
    {
        if (!IsPlayInputAllowed()) return;

        // Si el jugador dejó caer la pelota antes de golpear el saque -> reintento amigable VR
        if (CurrentPhase == PointPhase.WaitingForServe || lastHitter == Hitter.None)
        {
            CallFriendlyServeRetry("La pelota cayó. Saca de nuevo.");
            return;
        }

        Hitter receiver = GetOpponent(lastHitter);

        // Si estaba en saque y no completó ambos botes -> falta de saque
        if (CurrentPhase == PointPhase.ServeAwaitingOwnBounce || CurrentPhase == PointPhase.ServeAwaitingOpponentBounce)
        {
            string reason;
            if (touchedNet)
            {
                reason = lastHitter == Hitter.Player
                    ? "Falta de saque: Se quedó en la red"
                    : $"Falta de saque: {GetRivalDisplayName()} tocó la red";
            }
            else
            {
                reason = lastHitter == Hitter.Player
                    ? "Falta de saque: Pelota fuera"
                    : $"Falta de saque: {GetRivalDisplayName()} la mandó fuera";
            }
            AwardPoint(receiver, reason);
            return;
        }

        // En Rally: si YA botó 1 vez en el lado receptor -> ¡PUNTO GANADOR para lastHitter!
        // Si NO botó en el lado receptor -> ¡FUERA / RED! Punto para el receptor.
        int bouncesOnReceiver = receiver == Hitter.Player ? bouncesOnPlayerSide : bouncesOnAISide;
        if (bouncesOnReceiver >= 1)
        {
            string winMsg = lastHitter == Hitter.Player
                ? "¡Punto ganador para ti!"
                : $"¡Punto para {GetRivalDisplayName()}!";
            AwardPoint(lastHitter, winMsg);
        }
        else
        {
            string faultMsg;
            if (touchedNet)
            {
                faultMsg = lastHitter == Hitter.Player
                    ? "Falta: La pelota se quedó en la red"
                    : $"{GetRivalDisplayName()} mandó la pelota a la red";
            }
            else
            {
                faultMsg = lastHitter == Hitter.Player
                    ? "Fuera de mesa: Punto rival"
                    : $"{GetRivalDisplayName()} la mandó fuera";
            }
            AwardPoint(receiver, faultMsg);
        }
    }

    // =========================================================================
    // RESOLUCIÓN DE PUNTOS, LETS, SETS Y PAUSAS
    // =========================================================================

    private void CallFriendlyServeRetry(string reason)
    {
        if (!IsPlayInputAllowed()) return;

        lastStatusMessage = reason;
        activeCallout = "REPITE SAQUE";
        PlaySound(letClip);
        OnLetCalled?.Invoke(reason);

        if (rivalAI != null) rivalAI.ResetForNewPoint();
        SetPhase(PointPhase.BetweenPoints);
        UpdateUI();

        StartTransitionAfterDelay(Mathf.Min(delayAfterLet, 0.9f), false);
    }

    private void CallLet(string reason)
    {
        if (!IsPlayInputAllowed()) return;

        lastStatusMessage = reason;
        activeCallout = "¡LET!";
        PlaySound(letClip);
        OnLetCalled?.Invoke(reason);

        if (rivalAI != null) rivalAI.ResetForNewPoint();
        SetPhase(PointPhase.BetweenPoints);
        UpdateUI();

        StartTransitionAfterDelay(delayAfterLet, false);
    }

    private void AwardPoint(Hitter winner, string reason)
    {
        if (!IsPlayInputAllowed()) return;

        if (winner == Hitter.Player) playerScore++;
        else aiScore++;

        lastStatusMessage = reason;

        bool setJustWon = CheckSetAndMatchWinCondition(out Hitter setWinner);
        if (!setJustWon && !IsMatchOver)
        {
            UpdateServeTurnForCurrentScore();
        }

        UpdateCalloutBanner();
        OnScoreChanged?.Invoke(playerScore, aiScore);
        OnPointAwarded?.Invoke(winner, reason);

        if (IsMatchOver)
        {
            PlaySound(winner == Hitter.Player ? matchWonClip : matchLostClip);
            UpdateUI();
            HandleMatchEnded(MatchWinner);
            return;
        }

        if (rivalAI != null)
        {
            rivalAI.ResetForNewPoint();
        }

        if (setJustWon)
        {
            PlaySound(setWinner == Hitter.Player ? setWonClip : pointLostClip);
            OnSetsChanged?.Invoke(playerSets, aiSets);
            SetPhase(PointPhase.BetweenSets);
            UpdateUI();
            StartTransitionAfterDelay(delayBetweenSets, isNewSet: true);
        }
        else
        {
            PlaySound(winner == Hitter.Player ? pointWonClip : pointLostClip);
            SetPhase(PointPhase.BetweenPoints);
            UpdateUI();
            StartTransitionAfterDelay(delayBetweenPoints, isNewSet: false);
        }
    }

    private bool CheckSetAndMatchWinCondition(out Hitter setWinner)
    {
        setWinner = Hitter.None;

        bool playerWonSet = playerScore >= pointsToWin && (playerScore - aiScore) >= pointsToWinBy;
        bool aiWonSet = aiScore >= pointsToWin && (aiScore - playerScore) >= pointsToWinBy;

        if (!playerWonSet && !aiWonSet) return false;

        setWinner = playerWonSet ? Hitter.Player : Hitter.AI;
        if (playerWonSet) playerSets++;
        else aiSets++;

        int requiredSets = Mathf.Max(1, setsToWin);
        if (playerSets >= requiredSets)
        {
            IsMatchOver = true;
            MatchWinner = Hitter.Player;
            lastStatusMessage = setsToWin > 1
                ? $"¡GANASTE EL PARTIDO! ({playerSets} - {aiSets} sets)"
                : "¡GANASTE EL JUEGO!";
            return true;
        }

        if (aiSets >= requiredSets)
        {
            IsMatchOver = true;
            MatchWinner = Hitter.AI;
            string rivalDisplay = GetRivalDisplayName();
            lastStatusMessage = setsToWin > 1
                ? $"¡{rivalDisplay} GANA EL PARTIDO! ({playerSets} - {aiSets} sets)"
                : $"¡{rivalDisplay} GANA EL JUEGO!";
            return true;
        }

        // Set ganado pero el partido continúa
        string setWinnerName = playerWonSet ? "¡GANASTE EL SET!" : $"¡{GetRivalDisplayName()} GANA EL SET!";
        lastStatusMessage = $"{setWinnerName} (Sets: {playerSets} - {aiSets})";
        return true;
    }

    private void UpdateServeTurnForCurrentScore()
    {
        int totalPoints = playerScore + aiScore;
        int deuceThreshold = Mathf.Max(1, pointsToWin - 1);

        bool flipFromOpening;
        if (playerScore >= deuceThreshold && aiScore >= deuceThreshold)
        {
            // En Deuce (10-10+): se alterna el saque cada 1 punto
            flipFromOpening = (totalPoints % 2 == 1);
        }
        else
        {
            // Regla ITTF: se alterna el saque cada 2 puntos
            flipFromOpening = ((totalPoints / 2) % 2 == 1);
        }

        isPlayerTurnToServe = flipFromOpening ? !setOpeningServerIsPlayer : setOpeningServerIsPlayer;
    }

    private void StartTransitionAfterDelay(float delaySeconds, bool isNewSet)
    {
        StopTransitionCoroutine();
        transitionCoroutine = StartCoroutine(TransitionToNextPointRoutine(delaySeconds, isNewSet));
    }

    private IEnumerator TransitionToNextPointRoutine(float delaySeconds, bool isNewSet)
    {
        if (ballController != null)
        {
            ballController.SetGrabbable(false);
        }

        if (delaySeconds > 0.01f)
        {
            yield return new WaitForSeconds(delaySeconds);
        }

        transitionCoroutine = null;
        if (IsMatchOver) yield break;

        if (isNewSet)
        {
            playerScore = 0;
            aiScore = 0;
            // Regla ITTF: alterna quién abre el saque en cada set
            setOpeningServerIsPlayer = !setOpeningServerIsPlayer;
            isPlayerTurnToServe = setOpeningServerIsPlayer;
            lastStatusMessage = $"¡Set {playerSets + aiSets + 1}!";
            OnScoreChanged?.Invoke(playerScore, aiScore);
        }

        ResetPlayState();
        UpdateCalloutBanner();
        UpdateUI();
    }

    private void StopTransitionCoroutine()
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }
    }

    private void HandleMatchEnded(Hitter winner)
    {
        StopTransitionCoroutine();
        SetPhase(PointPhase.MatchOver);

        bool rivalWon = winner == Hitter.AI;

        if (rivalAI != null)
        {
            rivalAI.SetMatchActive(false);
        }

        if (rivalVisuals != null)
        {
            rivalVisuals.PlayMatchResultReaction(rivalWon);
        }

        ResetBallToFloatOnly();
        if (ballController != null)
        {
            ballController.SetGrabbable(false);
        }

        if (matchEndPanel != null)
        {
            matchEndPanel.SetActive(true);
        }

        if (matchEndTitleText != null)
        {
            string rivalDisplay = GetRivalDisplayName();
            matchEndTitleText.text = rivalWon ? $"DERROTA VS {rivalDisplay}" : "¡VICTORIA!";
        }

        if (matchEndScoreText != null)
        {
            string diffLabel = GetActiveDifficultyLabel();
            string setsLine = setsToWin > 1 ? $"Sets: {playerSets} - {aiSets}\n" : string.Empty;
            string scoreLine = $"Último Set: {playerScore} - {aiScore}";
            matchEndScoreText.text = string.IsNullOrEmpty(diffLabel)
                ? $"{setsLine}{scoreLine}"
                : $"{setsLine}{scoreLine}\nDificultad: {diffLabel}";
        }

        // Habilitar láseres de menú en VR para interactuar con Revancha / Cambiar Dificultad / Menú
        XRControllerLayoutManager.Instance?.EnterPauseMenu();

        OnMatchEnded?.Invoke(winner, playerScore, aiScore);
    }

    private void ResetPlayState()
    {
        lastHitter = Hitter.None;
        bouncesOnPlayerSide = 0;
        bouncesOnAISide = 0;
        touchedNet = false;
        stalledTimer = 0f;
        SetPhase(PointPhase.WaitingForServe);

        ResetBallToFloatOnly();
        if (ballController != null)
        {
            // Solo permitir agarrar la pelota al jugador si es su turno de sacar y el partido sigue activo
            ballController.SetGrabbable(!IsMatchOver && isPlayerTurnToServe);
        }
    }

    private void ResetBallToFloatOnly()
    {
        if (ballController == null) return;
        Vector3? spawnPos = floatSpawnPoint != null ? floatSpawnPoint.position : (Vector3?)null;
        ballController.ResetAndFloat(spawnPos);
    }

    private void SetPhase(PointPhase nextPhase)
    {
        CurrentPhase = nextPhase;
        OnServeTurnChanged?.Invoke(isPlayerTurnToServe, CurrentPhase);
    }

    // =========================================================================
    // GEOMETRÍA DE CANCHA Y RED DE SEGURIDAD FUERA DE LÍMITES
    // =========================================================================

    private bool IsBallLiveInPlay()
    {
        return CurrentPhase == PointPhase.ServeAwaitingOwnBounce ||
               CurrentPhase == PointPhase.ServeAwaitingOpponentBounce ||
               CurrentPhase == PointPhase.RallyInProgress;
    }

    private void MonitorOutOfBoundsSafetyNet()
    {
        if (ballController == null || ballController.IsGrabbed || ballController.IsInHitstop) return;
        Rigidbody rb = ballController.BallRigidbody;
        if (rb == null || rb.isKinematic) return;

        Vector3 ballPos = ballController.transform.position;
        Vector3 tableCenter = GetCourtCenter();

        // 1. Caída por debajo de la mesa (incluso si falta collider de Floor en algún área)
        if (ballPos.y < tableCenter.y - fallOutBelowTableOffset)
        {
            OnBallOutOfBounds("caída");
            return;
        }

        // 2. Alejamiento excesivo horizontal
        Vector2 horizDelta = new Vector2(ballPos.x - tableCenter.x, ballPos.z - tableCenter.z);
        if (horizDelta.magnitude > maxOutOfBoundsRadius)
        {
            OnBallOutOfBounds("distancia");
            return;
        }

        // 3. Pelota atascada/detenida sobre la red o borde
        if (rb.linearVelocity.magnitude < 0.08f)
        {
            stalledTimer += Time.deltaTime;
            if (stalledTimer >= stalledBallTimeout)
            {
                stalledTimer = 0f;
                OnBallOutOfBounds("detenida");
            }
        }
        else
        {
            stalledTimer = 0f;
        }
    }

    /// <summary>
    /// Determina si la pelota ya cruzó por detrás de la línea de fondo del receptor sin botar (bola claramente larga).
    /// </summary>
    private bool HasBallPassedReceiverEndLine(Hitter receiver, Vector3 ballPos)
    {
        Transform receiverTable = receiver == Hitter.Player ? playerTableTarget : enemyTableTarget;
        Transform otherTable = receiver == Hitter.Player ? enemyTableTarget : playerTableTarget;
        if (receiverTable == null || otherTable == null) return false;

        Collider receiverCol = receiver == Hitter.Player
            ? (playerTableCollider != null ? playerTableCollider : playerTableTarget.GetComponentInChildren<Collider>())
            : (enemyTableCollider != null ? enemyTableCollider : enemyTableTarget.GetComponentInChildren<Collider>());
        if (receiverCol == null) return false;

        Vector3 receiverCenter = GetTableSurfaceCenter(receiverTable, ref receiverCol);
        Collider otherCol = receiver == Hitter.Player ? enemyTableCollider : playerTableCollider;
        Vector3 otherCenter = GetTableSurfaceCenter(otherTable, ref otherCol);

        // Vector desde la red hacia el borde trasero del receptor
        Vector3 toEndLine = receiverCenter - otherCenter;
        toEndLine.y = 0f;
        if (toEndLine.sqrMagnitude < 0.001f) return false;
        toEndLine.Normalize();

        Bounds b = receiverCol.bounds;
        float halfLen = Mathf.Abs(b.extents.x * toEndLine.x) + Mathf.Abs(b.extents.z * toEndLine.z);
        float projFromReceiverCenter = Vector3.Dot(ballPos - receiverCenter, toEndLine);

        return projFromReceiverCenter > (halfLen + outPastEndLineMargin);
    }

    private Vector3 GetCourtCenter()
    {
        bool hasPlayer = playerTableTarget != null;
        bool hasEnemy = enemyTableTarget != null;

        if (hasPlayer && hasEnemy)
        {
            return 0.5f * (GetPlayerTableSurfaceCenter() + GetEnemyTableSurfaceCenter());
        }
        if (hasPlayer) return GetPlayerTableSurfaceCenter();
        if (hasEnemy) return GetEnemyTableSurfaceCenter();
        return new Vector3(0f, 0.76f, 0f);
    }

    private int IncrementBounce(Hitter side)
    {
        if (side == Hitter.Player)
        {
            bouncesOnPlayerSide++;
            return bouncesOnPlayerSide;
        }
        bouncesOnAISide++;
        return bouncesOnAISide;
    }

    private static Hitter GetOpponent(Hitter hitter)
    {
        return hitter == Hitter.Player ? Hitter.AI : Hitter.Player;
    }

    // =========================================================================
    // UI DE MARCADOR, SETS, CALLOUTS Y HUD DEL RIVAL
    // =========================================================================

    private void UpdateCalloutBanner()
    {
        if (IsMatchOver)
        {
            activeCallout = MatchWinner == Hitter.Player ? "¡VICTORIA!" : "FIN DEL PARTIDO";
            return;
        }

        int deuceThreshold = Mathf.Max(1, pointsToWin - 1);
        int reqSets = Mathf.Max(1, setsToWin);

        bool playerHasPointToWinSet = playerScore >= deuceThreshold && (playerScore - aiScore) >= (pointsToWinBy - 1);
        bool aiHasPointToWinSet = aiScore >= deuceThreshold && (aiScore - playerScore) >= (pointsToWinBy - 1);

        if (playerScore >= deuceThreshold && aiScore >= deuceThreshold && playerScore == aiScore)
        {
            activeCallout = "¡DEUCE! (Saque cada punto)";
        }
        else if (playerHasPointToWinSet)
        {
            activeCallout = (playerSets + 1 >= reqSets) ? "¡MATCH POINT!" : "¡SET POINT!";
        }
        else if (aiHasPointToWinSet)
        {
            activeCallout = (aiSets + 1 >= reqSets) ? "MATCH POINT RIVAL" : "SET POINT RIVAL";
        }
        else
        {
            activeCallout = string.Empty;
        }
    }

    private void UpdateUI()
    {
        if (scoreText != null)
        {
            if (setsText != null || setsToWin <= 1)
            {
                scoreText.text = $"{playerScore} - {aiScore}";
            }
            else
            {
                scoreText.text = $"{playerScore} - {aiScore}  <size=60%>(Sets {playerSets}-{aiSets})</size>";
            }
        }

        if (setsText != null)
        {
            setsText.text = setsToWin > 1
                ? $"Sets: {playerSets} - {aiSets}  (Mejor de {setsToWin * 2 - 1})"
                : "Set Único";
        }

        if (calloutText != null)
        {
            calloutText.text = activeCallout;
            calloutText.gameObject.SetActive(!string.IsNullOrEmpty(activeCallout));
        }

        if (statusText != null)
        {
            if (IsMatchOver)
            {
                statusText.text = lastStatusMessage;
            }
            else
            {
                string turnInfo = isPlayerTurnToServe
                    ? "Tu turno de sacar"
                    : $"Turno de {GetRivalDisplayName()}";
                string calloutSuffix = (calloutText == null && !string.IsNullOrEmpty(activeCallout))
                    ? $"  •  {activeCallout}"
                    : string.Empty;

                statusText.text = string.IsNullOrEmpty(lastStatusMessage)
                    ? turnInfo + calloutSuffix
                    : lastStatusMessage + "\n" + turnInfo + calloutSuffix;
            }
        }
    }

    private void ResolveReferences()
    {
        if (rivalAI == null)
        {
            rivalAI = FindFirstObjectByType<RivalAIController>();
        }

        if (rivalVisuals == null)
        {
            if (rivalAI != null) rivalVisuals = rivalAI.GetComponent<RivalVisualController>();
            if (rivalVisuals == null) rivalVisuals = FindFirstObjectByType<RivalVisualController>();
        }

        if (serveIndicator == null)
        {
            serveIndicator = FindFirstObjectByType<ServeIndicator3D>();
        }

        if (ballController == null)
        {
            ballController = FindFirstObjectByType<BallController>();
        }

        if (playerTableTarget == null)
        {
            playerTableTarget = FindTransformSafe("TablePlayer");
        }
        if (playerTableTarget != null && playerTableCollider == null)
        {
            playerTableCollider = playerTableTarget.GetComponentInChildren<Collider>();
        }

        if (enemyTableTarget == null)
        {
            enemyTableTarget = FindTransformSafe("TableEnemy");
        }
        if (enemyTableTarget != null && enemyTableCollider == null)
        {
            enemyTableCollider = enemyTableTarget.GetComponentInChildren<Collider>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    private static Transform FindTransformSafe(string tagOrName)
    {
        try
        {
            GameObject tagged = GameObject.FindGameObjectWithTag(tagOrName);
            if (tagged != null) return tagged.transform;
        }
        catch (UnityException)
        {
            // Tag no definido
        }

        GameObject named = GameObject.Find(tagOrName);
        return named != null ? named.transform : null;
    }

    private static Vector3 GetTableSurfaceCenter(Transform target, ref Collider cachedCollider)
    {
        if (target == null) return Vector3.zero;
        if (cachedCollider == null) cachedCollider = target.GetComponentInChildren<Collider>();
        if (cachedCollider != null)
        {
            Bounds b = cachedCollider.bounds;
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }
        return target.position;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip, MenuManager.sfxVolume);
        }
    }

    private void UnbindRival()
    {
        if (boundRivalAI == null) return;

        boundRivalAI.OnDifficultyApplied -= HandleRivalDifficultyApplied;
        boundRivalAI.OnPowerStateChanged -= HandleRivalPowerStateChanged;
        boundRivalAI = null;
    }

    private void HandleRivalDifficultyApplied(RivalProfile profile, RivalDifficultyProfile diffProfile)
    {
        UpdateRivalIdentityHUD(profile, diffProfile);
        if (rivalAI != null)
        {
            UpdateRivalPowerHUD(rivalAI.CurrentPowerCharge, rivalAI.PowerChargeRequired, rivalAI.IsPowerReady);
        }
    }

    private void HandleRivalPowerStateChanged(int current, int required, bool isReady)
    {
        UpdateRivalPowerHUD(current, required, isReady);
    }

    private void RefreshRivalHUD()
    {
        if (rivalAI == null) return;

        UpdateRivalIdentityHUD(rivalAI.ActiveProfile, rivalAI.ActiveDifficultyProfile);
        UpdateRivalPowerHUD(rivalAI.CurrentPowerCharge, rivalAI.PowerChargeRequired, rivalAI.IsPowerReady);
    }

    private void UpdateRivalIdentityHUD(RivalProfile profile, RivalDifficultyProfile diffProfile)
    {
        if (rivalNameText != null)
        {
            string displayName = profile != null && !string.IsNullOrEmpty(profile.rivalName)
                ? profile.rivalName
                : "Rival IA";
            rivalNameText.text = displayName;
        }

        if (rivalDifficultyText != null)
        {
            string diffLabel = diffProfile != null && !string.IsNullOrEmpty(diffProfile.displayLabel)
                ? diffProfile.displayLabel
                : (rivalAI != null ? GetFallbackDifficultyLabel(rivalAI.CurrentDifficulty) : "Medio");
            rivalDifficultyText.text = diffLabel;
        }

        if (rivalDifficultyBadge != null && diffProfile != null)
        {
            rivalDifficultyBadge.color = diffProfile.accentColor;
        }
    }

    private void UpdateRivalPowerHUD(int current, int required, bool isReady)
    {
        int safeRequired = Mathf.Max(1, required);
        int clampedCurrent = Mathf.Clamp(current, 0, safeRequired);

        if (rivalPowerSlider != null)
        {
            rivalPowerSlider.minValue = 0f;
            rivalPowerSlider.maxValue = safeRequired;
            rivalPowerSlider.value = clampedCurrent;
        }

        if (rivalPowerText != null)
        {
            rivalPowerText.text = isReady
                ? "¡PODER RIVAL LISTO!"
                : $"Poder Rival: {clampedCurrent} / {safeRequired}";
            rivalPowerText.color = isReady
                ? new Color(1f, 0.84f, 0.18f, 1f)
                : Color.white;
        }
    }

    private void ResetPlayerPowers()
    {
        PowerManager[] powerManagers = FindObjectsByType<PowerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < powerManagers.Length; i++)
        {
            PowerManager pm = powerManagers[i];
            if (pm != null && pm.power != null && (pm.power.currentHits > 0 || pm.power.isReady || pm.isPowerActive))
            {
                pm.ConsumePower();
            }
        }
    }

    private string GetRivalDisplayName()
    {
        if (rivalAI != null && rivalAI.ActiveProfile != null && !string.IsNullOrEmpty(rivalAI.ActiveProfile.rivalName))
        {
            return rivalAI.ActiveProfile.rivalName;
        }
        return "IA";
    }

    private string GetActiveDifficultyLabel()
    {
        if (rivalAI == null) return string.Empty;

        if (rivalAI.ActiveDifficultyProfile != null && !string.IsNullOrEmpty(rivalAI.ActiveDifficultyProfile.displayLabel))
        {
            return rivalAI.ActiveDifficultyProfile.displayLabel;
        }
        return GetFallbackDifficultyLabel(rivalAI.CurrentDifficulty);
    }

    private static string GetFallbackDifficultyLabel(RivalDifficulty difficulty)
    {
        switch (difficulty)
        {
            case RivalDifficulty.Easy: return "Fácil";
            case RivalDifficulty.Hard: return "Difícil";
            default: return "Medio";
        }
    }
}
