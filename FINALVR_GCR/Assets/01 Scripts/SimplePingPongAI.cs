using UnityEngine;
using System.Collections;

public class SimplePingPongAI : MonoBehaviour
{
    [Header("Referencias")]
    public Transform ball;
    public Rigidbody ballRb;
    public Transform aiPaddle; 
    public MatchReferee referee; // Añadido para saber de quién es el turno

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
    private bool isServing = false; // Estado para bloquear Update durante el saque

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

        // --- LÓGICA DE SAQUE DE IA ---
        if (referee != null && !referee.GetIsPlayerTurnToServe() && referee.GetIsServePhase())
        {
            if (!isServing)
            {
                StartCoroutine(ServeRoutine());
            }
            return; // Bloqueamos el rastreo normal mientras saca
        }

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

    // --- CORRUTINA DE SAQUE ---
    private IEnumerator ServeRoutine()
    {
        isServing = true;
        
        // Pausa inicial para que el jugador se prepare
        yield return new WaitForSeconds(1.0f);

        // Verificar que aún sea nuestro turno (por si el jugador reinició el juego)
        if (referee == null || referee.GetIsPlayerTurnToServe() || !referee.GetIsServePhase())
        {
            isServing = false;
            yield break;
        }

        // 1. Preparar la pelota para el Toss (Lanzamiento)
        float serveZ = Random.Range(-0.3f, 0.3f); // Pequeña variación lateral
        Vector3 serveStartPos = new Vector3(baselineX + (0.3f * sideMultiplier), startPosition.y + 0.3f, startPosition.z + serveZ);
        
        BallController bc = ball.GetComponent<BallController>();
        if (bc != null) bc.ResetAndFloat(serveStartPos);
        else 
        {
            ball.position = serveStartPos;
            ballRb.linearVelocity = Vector3.zero;
            ballRb.useGravity = false;
        }

        // 2. Mover la pala hacia atrás para tomar impulso
        targetPosition = new Vector3(baselineX - (0.3f * sideMultiplier), startPosition.y + 0.5f, startPosition.z + serveZ);
        
        yield return new WaitForSeconds(0.6f); 

        // 3. El Toss (Lanzar la pelota un poco hacia arriba)
        ballRb.useGravity = true;
        ballRb.linearVelocity = new Vector3(0f, 1.8f, 0f);

        // Esperamos a que la pelota suba y empiece a bajar un poco
        yield return new WaitForSeconds(0.35f);

        // 4. El Golpe (Strike)
        float originalSpeed = aiSpeed;
        aiSpeed = 20f; // Velocidad muy rápida para asegurar el impacto visual y colisión

        // Movemos la pala a través de la pelota
        targetPosition = new Vector3(baselineX + (0.6f * sideMultiplier), startPosition.y + 0.2f, startPosition.z + serveZ);

        // Damos un brevísimo instante para que la pala toque físicamente la pelota 
        // y el BallReporter registre el "OnCollisionEnter" con el EnemyPaddle.
        yield return new WaitForSeconds(0.05f);

        // 5. Aplicar la trayectoria perfecta (Magia)
        // Sobrescribimos la velocidad física resultante para garantizar un buen saque
        bool willMiss = Random.value < (missProbability * 0.2f);
        
        if (!willMiss)
        {
            // Saque exitoso: va hacia abajo para picar en su mesa, y con fuerza hacia adelante
            // Calculamos un ligero ángulo en Z para que no vaya siempre recto
            float randomAngZ = Random.Range(-0.8f, 0.8f);
            ballRb.linearVelocity = new Vector3(3.5f * sideMultiplier, -2.5f, randomAngZ);
        }
        else
        {
            // Saque fallido a propósito: directo a la red o mesa muy cerca
            ballRb.linearVelocity = new Vector3(1.5f * sideMultiplier, -1.0f, 0f);
        }

        yield return new WaitForSeconds(0.5f); // Dar tiempo a que termine el movimiento de la pala

        // Termina el saque, regresar a normalidad
        aiSpeed = originalSpeed;
        targetPosition = startPosition;
        isServing = false;
    }
}