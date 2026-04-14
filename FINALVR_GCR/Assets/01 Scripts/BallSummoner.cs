using UnityEngine;
using UnityEngine.InputSystem;

public class BallSummoner : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("El punto exacto donde aparecerá la pelota (ej. la palma de la mano izquierda)")]
    public Transform leftHandTransform; 
    
    [Tooltip("El objeto de la pelota en tu escena")]
    public GameObject ball;             

    private Rigidbody ballRb;

    [Header("Input")]
    [Tooltip("El botón que llamará a la pelota (ej. XRI LeftHand/Select)")]
    public InputActionReference summonAction; 

    private void Start()
    {
        if (ball != null)
        {
            // Guardamos la referencia al Rigidbody para detener su movimiento al invocarla
            ballRb = ball.GetComponent<Rigidbody>();
        }
        else
        {
            Debug.LogWarning("BallSummoner: No has asignado la pelota en el inspector.");
        }
    }

    // Suscribimos la función al evento de presionar el botón
    private void OnEnable()
    {
        if (summonAction != null)
        {
            summonAction.action.performed += SummonBall;
        }
    }

    // Limpiamos el evento si el objeto se desactiva para evitar errores
    private void OnDisable()
    {
        if (summonAction != null)
        {
            summonAction.action.performed -= SummonBall;
        }
    }

    private void SummonBall(InputAction.CallbackContext context)
    {
        if (ball != null && leftHandTransform != null)
        {
            // 1. Mover la pelota a la posición de la mano
            ball.transform.position = leftHandTransform.position;

            // 2. Matar cualquier inercia o rebote previo
            if (ballRb != null)
            {
                ballRb.linearVelocity = Vector3.zero; // Usamos linearVelocity en versiones recientes de Unity
                ballRb.angularVelocity = Vector3.zero;
            }
        }
    }
}