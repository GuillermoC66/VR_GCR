using UnityEngine;
using UnityEngine.InputSystem;

public class BallSummoner : MonoBehaviour
{
    [Header("Referencias Generales")]
    public GameObject ball;             
    private Rigidbody ballRb;

    [Header("Mano Izquierda (Modo Diestro)")]
    public Transform leftHandTransform; 
    public InputActionReference leftSummonAction; 

    [Header("Mano Derecha (Modo Zurdo)")]
    public Transform rightHandTransform; 
    public InputActionReference rightSummonAction; 

    [HideInInspector]
    public bool isLefty = false; // Esta variable la controlará nuestro MenuManager

    // Variables de estado interno
    private bool isHoldingBall = false;
    private Vector3 lastHandPosition;
    private Vector3 handVelocity;

    private void Start()
    {
        if (ball != null) ballRb = ball.GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        // Suscribir eventos de la mano izquierda
        if (leftSummonAction != null)
        {
            leftSummonAction.action.started += OnLeftPressed;
            leftSummonAction.action.canceled += OnLeftReleased;
        }
        
        // Suscribir eventos de la mano derecha
        if (rightSummonAction != null)
        {
            rightSummonAction.action.started += OnRightPressed;
            rightSummonAction.action.canceled += OnRightReleased;
        }
    }

    private void OnDisable()
    {
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
            ballRb.isKinematic = false;
            ballRb.linearVelocity = handVelocity;
            ballRb.angularVelocity = Vector3.zero;
        }
    }

    private void Update()
    {
        // Determinar cuál es la mano activa (la que no tiene la pala)
        Transform activeHand = isLefty ? rightHandTransform : leftHandTransform;

        if (activeHand != null)
        {
            // Calcular la velocidad de la mano activa para el lanzamiento
            handVelocity = (activeHand.position - lastHandPosition) / Time.deltaTime;
            lastHandPosition = activeHand.position;
        }

        // Pegar la pelota a la mano activa
        if (isHoldingBall && ball != null && activeHand != null)
        {
            ball.transform.position = activeHand.position;
        }
    }
}