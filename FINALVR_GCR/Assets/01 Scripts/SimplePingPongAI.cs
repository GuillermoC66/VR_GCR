using UnityEngine;

public class SimplePingPongAI : MonoBehaviour
{
    [Header("Referencias")]
    public Transform ball;
    public Rigidbody ballRb;
    public Transform aiPaddle; 

    [Header("Gestor de Dificultad")]
    [Range(1f, 15f)]
    public float aiSpeed = 5f; 
    [Range(0f, 0.5f)]
    public float errorMargin = 0.15f; 
    [Range(0f, 1f)]
    public float missProbability = 0.1f;

    [Header("Configuración de Cancha (Eje X)")]
    [Tooltip("Posición X base donde la IA espera la pelota (ej. -1.5)")]
    public float baselineX = -1.5f;
    [Tooltip("Distancia en X a la que la IA decide dar el 'golpe'")]
    public float strikeDistance = 0.6f;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    
    // El error ahora se calcula en Y (altura) y Z (ancho de la mesa)
    private float currentErrorZ;
    private float currentErrorY;
    private bool isMissingThisTurn = false;

    // Para saber si la IA está del lado izquierdo (negativo) o derecho (positivo)
    private float sideMultiplier; 

    void Start()
    {
        startPosition = aiPaddle.position;
        targetPosition = startPosition;
        
        // Determina hacia qué dirección debe "empujar" la pala al atacar
        // Si baselineX es negativo, empuja hacia positivo (hacia la red) y viceversa.
        sideMultiplier = (baselineX < 0) ? 1f : -1f;
    }

    void Update()
    {
        if (ball == null || ballRb == null || aiPaddle == null) return;

        // 1. ¿La pelota viene hacia la IA?
        // Comparamos el signo de la velocidad con el lado de la mesa
        bool isMovingTowardsAI = (baselineX < 0) ? ballRb.linearVelocity.x < -0.1f : ballRb.linearVelocity.x > 0.1f;
        
        // 2. ¿La pelota ya cruzó la red? (X = 0)
        bool hasCrossedNet = (baselineX < 0) ? ball.position.x < 0f : ball.position.x > 0f;

        if (isMovingTowardsAI && hasCrossedNet)
        {
            // RASTREO CON ERROR: La IA sigue la pelota en Z (ancho) y Y (alto)
            targetPosition = new Vector3(
                baselineX, // Se mantiene en su línea base de X
                Mathf.Clamp(ball.position.y + currentErrorY, startPosition.y - 0.2f, startPosition.y + 0.6f), 
                ball.position.z + currentErrorZ 
            );

            // ATAQUE: Si la pelota está cerca en el eje X, empujar la pala hacia adelante
            if (Mathf.Abs(ball.position.x - aiPaddle.position.x) < strikeDistance && !isMissingThisTurn)
            {
                // Empuja la pala 0.4 metros hacia la red
                targetPosition.x = baselineX + (0.4f * sideMultiplier); 
            }
            else
            {
                targetPosition.x = baselineX;
            }
        }
        else
        {
            // La pelota va hacia el jugador. La IA vuelve al centro.
            targetPosition = startPosition;
            
            // RECALCULAR ERROR: Preparamos la suerte para el PRÓXIMO golpe
            currentErrorZ = Random.Range(-errorMargin, errorMargin);
            currentErrorY = Random.Range(-errorMargin, errorMargin);
            isMissingThisTurn = Random.value < missProbability;
        }

        // MOVER LA PALA
        aiPaddle.position = Vector3.MoveTowards(aiPaddle.position, targetPosition, aiSpeed * Time.deltaTime);
    }
}