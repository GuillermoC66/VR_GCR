using UnityEngine;
using UnityEngine.InputSystem;

public class BallSummoner : MonoBehaviour
{
    [Header("Referencias Generales")]
    public GameObject ball;             
    private Rigidbody ballRb;

    [Header("Mano Izquierda (Modo Diestro)")]
    public Transform leftHandTransform; 
    [Tooltip("Opcional: punto calibrado entre los dedos de la mano izquierda (se auto-resuelve de VRHandController si es null).")]
    public Transform leftBallAttachPoint;
    public InputActionReference leftSummonAction; 

    [Header("Mano Derecha (Modo Zurdo)")]
    public Transform rightHandTransform; 
    [Tooltip("Opcional: punto calibrado entre los dedos de la mano derecha (se auto-resuelve de VRHandController si es null).")]
    public Transform rightBallAttachPoint;
    public InputActionReference rightSummonAction; 

    [Header("Auto-Saque Asistido (Botón Secundario Y/B)")]
    [Tooltip("Velocidad vertical al presionar el botón de saque asistido en la mano no dominante.")]
    public float autoServeUpwardSpeed = 3.2f;
    [Tooltip("Ligero impulso hacia adelante al hacer saque asistido.")]
    public float autoServeForwardSpeed = 0.25f;

    [HideInInspector]
    public bool isLefty = false; // Sincronizado con XRControllerLayoutManager / MenuManager

    // Variables de estado interno
    private bool isHoldingBall = false;
    private Vector3 lastHandPosition;
    private Vector3 handVelocity;
    private XRControllerLayoutManager layoutManager;
    private MatchReferee referee;

    private void Start()
    {
        if (ball != null) ballRb = ball.GetComponent<Rigidbody>();
        referee = FindFirstObjectByType<MatchReferee>();
        BindLayoutManager();
    }

    private void OnEnable()
    {
        BindLayoutManager();

        // Fallback legado solo si no hay XRControllerLayoutManager en la escena
        if (layoutManager == null)
        {
            if (leftSummonAction != null)
            {
                leftSummonAction.action.started += OnLeftPressed;
                leftSummonAction.action.canceled += OnLeftReleased;
            }
            if (rightSummonAction != null)
            {
                rightSummonAction.action.started += OnRightPressed;
                rightSummonAction.action.canceled += OnRightReleased;
            }
        }
    }

    private void OnDisable()
    {
        UnbindLayoutManager();

        if (leftSummonAction != null)
        {
            leftSummonAction.action.started -= OnLeftPressed;
            leftSummonAction.action.canceled -= OnLeftReleased;
        }
        if (rightSummonAction != null)
        {
            rightSummonAction.action.started -= OnRightPressed;
            rightSummonAction.action.canceled -= OnRightReleased;
        }
    }

    private void BindLayoutManager()
    {
        if (layoutManager != null) return;

        layoutManager = XRControllerLayoutManager.Instance;
        if (layoutManager == null) return;

        // Evitar doble disparo si el fallback legado quedó suscrito
        if (leftSummonAction != null)
        {
            leftSummonAction.action.started -= OnLeftPressed;
            leftSummonAction.action.canceled -= OnLeftReleased;
        }
        if (rightSummonAction != null)
        {
            rightSummonAction.action.started -= OnRightPressed;
            rightSummonAction.action.canceled -= OnRightReleased;
        }

        isLefty = layoutManager.IsLefty;
        layoutManager.OnLeftyChanged += HandleLeftyChanged;
        layoutManager.OnSummonBallStarted += StartHolding;
        layoutManager.OnSummonBallCanceled += StopHolding;
        layoutManager.OnAutoServePressed += TriggerAutoServe;
    }

    private void UnbindLayoutManager()
    {
        if (layoutManager == null) return;

        layoutManager.OnLeftyChanged -= HandleLeftyChanged;
        layoutManager.OnSummonBallStarted -= StartHolding;
        layoutManager.OnSummonBallCanceled -= StopHolding;
        layoutManager.OnAutoServePressed -= TriggerAutoServe;
        layoutManager = null;
    }

    private void HandleLeftyChanged(bool lefty)
    {
        isLefty = lefty;
    }

    /// <summary>
    /// Coloca la pelota en el punto entre los dedos de la mano no dominante y la lanza hacia arriba sin animar agarre.
    /// </summary>
    public void TriggerAutoServe()
    {
        if (ball == null || ballRb == null) return;
        if (referee == null) referee = FindFirstObjectByType<MatchReferee>();
        if (referee != null && !referee.CanPlayerSummonOrServeBall()) return;

        Transform activeHand = isLefty ? rightHandTransform : leftHandTransform;
        Transform attachTransform = GetActiveBallAttachTransform();
        if (attachTransform == null) return;

        isHoldingBall = false;
        // Si existe el punto calibrado entre los dedos lo usamos directo; si solo está la raíz del control, aplicamos el offset legado
        ball.transform.position = (attachTransform != activeHand)
            ? attachTransform.position
            : attachTransform.position + Vector3.up * 0.08f;

        ballRb.isKinematic = false;
        ballRb.useGravity = true;

        Transform dirRef = activeHand != null ? activeHand : attachTransform;
        Vector3 forward = dirRef.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f) forward.Normalize();

        Vector3 serveVel = Vector3.up * autoServeUpwardSpeed + forward * autoServeForwardSpeed;
        BallController bc = ball.GetComponent<BallController>();
        if (bc != null)
        {
            bc.ReleaseForServe(serveVel);
        }
        else
        {
            ballRb.isKinematic = false;
            ballRb.useGravity = true;
            ballRb.linearVelocity = serveVel;
            ballRb.angularVelocity = Vector3.zero;
        }
    }

    // --- Lógica Mano Izquierda ---
    private void OnLeftPressed(InputAction.CallbackContext context)
    {
        if (isLefty) return; // Si es zurdo, la mano izquierda tiene la pala. Ignoramos.
        StartHolding();
    }

    private void OnLeftReleased(InputAction.CallbackContext context)
    {
        if (isLefty) return;
        StopHolding();
    }

    // --- Lógica Mano Derecha ---
    private void OnRightPressed(InputAction.CallbackContext context)
    {
        if (!isLefty) return; // Si es diestro, la mano derecha tiene la pala. Ignoramos.
        StartHolding();
    }

    private void OnRightReleased(InputAction.CallbackContext context)
    {
        if (!isLefty) return;
        StopHolding();
    }

    // --- Físicas de Agarre y Lanzamiento ---
    private void StartHolding()
    {
        if (referee == null) referee = FindFirstObjectByType<MatchReferee>();
        if (referee != null && !referee.CanPlayerSummonOrServeBall()) return;

        if (ball != null && ballRb != null)
        {
            isHoldingBall = true;
            ballRb.isKinematic = true; 
        }
    }

    private void StopHolding()
    {
        if (ball != null && ballRb != null && isHoldingBall)
        {
            isHoldingBall = false;
            BallController bc = ball.GetComponent<BallController>();
            if (bc != null)
            {
                bc.ReleaseForServe(handVelocity);
            }
            else
            {
                ballRb.isKinematic = false;
                ballRb.linearVelocity = handVelocity;
                ballRb.angularVelocity = Vector3.zero;
            }
        }
    }

    private void Update()
    {
        Transform attachTransform = GetActiveBallAttachTransform();

        if (attachTransform != null)
        {
            if (Time.deltaTime > 0.0001f)
            {
                handVelocity = (attachTransform.position - lastHandPosition) / Time.deltaTime;
            }
            lastHandPosition = attachTransform.position;
        }

        if (isHoldingBall && ball != null && attachTransform != null)
        {
            ball.transform.position = attachTransform.position;
        }
    }

    private void LateUpdate()
    {
        if (isHoldingBall && ball != null)
        {
            Transform attachTransform = GetActiveBallAttachTransform();
            if (attachTransform != null)
            {
                ball.transform.position = attachTransform.position;
            }
        }
    }

    private Transform GetActiveBallAttachTransform()
    {
        if (layoutManager == null) BindLayoutManager();

        if (isLefty)
        {
            if (rightBallAttachPoint != null) return rightBallAttachPoint;
            if (layoutManager != null && layoutManager.rightHandVisual != null && layoutManager.rightHandVisual.BallAttachPoint != null)
            {
                rightBallAttachPoint = layoutManager.rightHandVisual.BallAttachPoint;
                return rightBallAttachPoint;
            }
            if (rightHandTransform != null)
            {
                VRHandController hand = rightHandTransform.GetComponentInChildren<VRHandController>(true);
                if (hand != null && hand.BallAttachPoint != null)
                {
                    rightBallAttachPoint = hand.BallAttachPoint;
                    return rightBallAttachPoint;
                }
            }
            return rightHandTransform;
        }
        else
        {
            if (leftBallAttachPoint != null) return leftBallAttachPoint;
            if (layoutManager != null && layoutManager.leftHandVisual != null && layoutManager.leftHandVisual.BallAttachPoint != null)
            {
                leftBallAttachPoint = layoutManager.leftHandVisual.BallAttachPoint;
                return leftBallAttachPoint;
            }
            if (leftHandTransform != null)
            {
                VRHandController hand = leftHandTransform.GetComponentInChildren<VRHandController>(true);
                if (hand != null && hand.BallAttachPoint != null)
                {
                    leftBallAttachPoint = hand.BallAttachPoint;
                    return leftBallAttachPoint;
                }
            }
            return leftHandTransform;
        }
    }
}