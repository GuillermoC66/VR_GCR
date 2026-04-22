using UnityEngine;

public class SimplePingPongAI : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("La pelota de Ping Pong")]
    public Transform ball;
    [Tooltip("El Rigidbody de la pelota para leer su velocidad")]
    public Rigidbody ballRb;
    [Tooltip("El objeto de la pala que controla la IA")]
    public Transform aiPaddle; 

    [Header("Gestor de Dificultad")]
    [Range(1f, 15f)]
    [Tooltip("Qué tan rápido se mueve la pala de la IA")]
    public float aiSpeed = 5f; 
    
    [Range(0f, 0.5f)]
    [Tooltip("Radio de error al apuntar (en metros). 0 = Puntería perfecta.")]
    public float errorMargin = 0.15f; 
    
    [Range(0f, 1f)]
    [Tooltip("Probabilidad de que la IA ignore la pelota por completo (0.2 = 20% de fallo)")]
    public float missProbability = 0.1f;

    [Header("Configuración de Cancha")]
    [Tooltip("Posición Z base donde la IA espera la pelota (ej. borde de la mesa)")]
    public float baselineZ = 1.5f;
    [Tooltip("Distancia a la que la IA decide dar el 'golpe' hacia adelante")]
    public float strikeDistance = 0.6f;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    
    // Variables para guardar el error de este turno
    private float currentErrorX;
    private float currentErrorY;
    private bool isMissingThisTurn = false;

    void Start()
    {
        // Guardamos la posición inicial de la pala como punto de reposo
        startPosition = aiPaddle.position;
        targetPosition = startPosition;
    }

    void Update()
    {
        if (ball == null || ballRb == null || aiPaddle == null) return;

        // ¿La pelota se mueve hacia la IA? (Asumiendo que la IA está en el lado positivo de Z)
        // Y asegurándonos de que la pelota ya pasó la red (z > 0)
        if (ballRb.linearVelocity.z > 0.1f && ball.position.z > 0f)
        {
            // 1. RASTREO CON ERROR: La IA sigue la pelota en X y Y, sumando su error
            targetPosition = new Vector3(
                ball.position.x + currentErrorX, 
                Mathf.Clamp(ball.position.y + currentErrorY, startPosition.y - 0.2f, startPosition.y + 0.6f), 
                baselineZ // Mantenerse en la línea de fondo por defecto
            );

            // 2. ATAQUE: Si la pelota está muy cerca y no toca fallar este turno, empujar la pala hacia adelante
            if (Mathf.Abs(ball.position.z - aiPaddle.position.z) < strikeDistance && !isMissingThisTurn)
            {
                // Empuja la pala hacia la pelota (hacia -Z)
                targetPosition.z = baselineZ - 0.4f; 
            }
            else
            {
                targetPosition.z = baselineZ;
            }
        }
        else
        {
            // La pelota va hacia el jugador. La IA vuelve al centro.
            targetPosition = startPosition;
            
            // 3. RECALCULAR ERROR: Preparamos la "torpeza" para el PRÓXIMO golpe
            currentErrorX = Random.Range(-errorMargin, errorMargin);
            currentErrorY = Random.Range(-errorMargin, errorMargin);
            isMissingThisTurn = Random.value < missProbability;
        }

        // 4. MOVER LA PALA: Usamos MoveTowards para un movimiento robótico pero constante
        aiPaddle.position = Vector3.MoveTowards(aiPaddle.position, targetPosition, aiSpeed * Time.deltaTime);
    }
}