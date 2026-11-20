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

    [Header("Devolución Balística Determinista (Arcade)")]
    [Tooltip("Usa BallController.LaunchBallistic para devolver bolas limpias y consistentes a la mesa del jugador.")]
    public bool useDeterministicReturns = true;
    [Tooltip("Centro de la mitad de la mesa del jugador. Si está vacío se busca por tag 'TablePlayer'.")]
    public Transform playerTableTarget;
    public float playerTableHalfWidth = 0.55f;
    public float playerTableHalfLength = 0.45f;
    public float netHeightAboveTable = 0.155f;
    public float aiReturnFlightTimeMin = 0.48f;
    public float aiReturnFlightTimeMax = 0.66f;
    [Range(0f, 0.5f)]
    public float aiSliceChance = 0.2f;
    [Range(0f, 0.4f)]
    public float aiSmashChance = 0.15f;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    
    // El error ahora se calcula en Y (altura) y Z (ancho de la mesa)
    private float currentErrorZ;
    private float currentErrorY;
    private bool isMissingThisTurn = false;

    // Para saber si la IA está del lado izquierdo (negativo) o derecho (positivo)
    private float sideMultiplier; 
    private bool isServing = false; // Estado para bloquear Update durante el saque
    private float lastAiHitTime = -10f;
    private BallController ballController;
    private BallReporter ballReporter;

    void Start()
    {
        startPosition = aiPaddle.position;
        targetPosition = startPosition;
        
        // Determina hacia qué dirección debe "empujar" la pala al atacar
        // Si baselineX es negativo, empuja hacia positivo (hacia la red) y viceversa.
        sideMultiplier = (baselineX < 0) ? 1f : -1f;

        if (ball != null)
        {
            ballController = ball.GetComponent<BallController>();
            ballReporter = ball.GetComponent<BallReporter>();
        }

        if (playerTableTarget == null)
        {
            GameObject playerTable = GameObject.FindGameObjectWithTag("TablePlayer");
            if (playerTable != null) playerTableTarget = playerTable.transform;
        }

        // En modo determinista evitamos que la pala de la IA meta rebotes PhysX antes de nuestra devolución balística
        if (useDeterministicReturns && aiPaddle != null)
        {
            Collider[] aiCols = aiPaddle.GetComponentsInChildren<Collider>();
            foreach (Collider col in aiCols)
            {
                if (col is MeshCollider mc && !mc.convex) mc.convex = true;
                col.isTrigger = true;
            }
        }
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

                if (useDeterministicReturns && ballController != null && Time.time - lastAiHitTime > 0.35f)
                {
                    float distToBall = Vector3.Distance(aiPaddle.position, ball.position);
                    if (distToBall < 0.32f)
                    {
                        ExecuteDeterministicAIReturn();
                    }
                }
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
        if (bc != null)
        {
            bc.ReleaseForServe(new Vector3(0f, 1.8f, 0f));
        }
        else
        {
            ballRb.useGravity = true;
            ballRb.linearVelocity = new Vector3(0f, 1.8f, 0f);
        }

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

        // 5. Aplicar la trayectoria perfecta (Balística Determinista)
        bool willMiss = Random.value < (missProbability * 0.2f);

        if (useDeterministicReturns && bc != null && playerTableTarget != null)
        {
            if (ballReporter != null) ballReporter.NotifyEnemyPaddleHit(0.65f);

            Vector3 landing = playerTableTarget.position + new Vector3(
                Random.Range(-playerTableHalfLength * 0.5f, playerTableHalfLength * 0.5f),
                0f,
                Random.Range(-playerTableHalfWidth * 0.7f, playerTableHalfWidth * 0.7f)
            );
            float netTopY = playerTableTarget.position.y + netHeightAboveTable;
            float clearance = willMiss ? -0.05f : 0.24f;
            bc.LaunchBallistic(landing, 0.58f, netTopY, clearance, ArcadeShotType.Drive, Vector3.zero);
        }
        else if (!willMiss)
        {
            float randomAngZ = Random.Range(-0.8f, 0.8f);
            ballRb.linearVelocity = new Vector3(3.5f * sideMultiplier, -2.5f, randomAngZ);
        }
        else
        {
            ballRb.linearVelocity = new Vector3(1.5f * sideMultiplier, -1.0f, 0f);
        }

        yield return new WaitForSeconds(0.5f); // Dar tiempo a que termine el movimiento de la pala

        // Termina el saque, regresar a normalidad
        aiSpeed = originalSpeed;
        targetPosition = startPosition;
        isServing = false;
    }

    private void ExecuteDeterministicAIReturn()
    {
        if (ballController == null || playerTableTarget == null) return;

        lastAiHitTime = Time.time;

        Vector3 forwardDir = playerTableTarget.position - ball.position;
        forwardDir.y = 0f;
        forwardDir = forwardDir.sqrMagnitude > 0.001f ? forwardDir.normalized : new Vector3(sideMultiplier, 0f, 0f);
        Vector3 rightDir = Vector3.Cross(Vector3.up, forwardDir).normalized;

        float roll = Random.value;
        ArcadeShotType aiShot = ArcadeShotType.Drive;
        float flightTime = Random.Range(aiReturnFlightTimeMin, aiReturnFlightTimeMax);
        float clearance = 0.26f;
        Vector3 curve = Vector3.zero;

        if (roll < aiSmashChance)
        {
            aiShot = ArcadeShotType.Smash;
            flightTime = aiReturnFlightTimeMin * 0.78f;
            clearance = 0.12f;
        }
        else if (roll < aiSmashChance + aiSliceChance)
        {
            aiShot = ArcadeShotType.Slice;
            flightTime = aiReturnFlightTimeMax * 1.25f;
            clearance = 0.45f;
            curve = rightDir * (Random.value < 0.5f ? -2.6f : 2.6f);
        }

        Vector3 targetLanding = playerTableTarget.position
            + rightDir * Random.Range(-playerTableHalfWidth, playerTableHalfWidth)
            + forwardDir * Random.Range(-playerTableHalfLength * 0.4f, playerTableHalfLength * 0.65f);

        float netTopY = playerTableTarget.position.y + netHeightAboveTable;
        ballController.LaunchBallistic(targetLanding, flightTime, netTopY, clearance, aiShot, curve);

        if (ballReporter != null)
        {
            ballReporter.NotifyEnemyPaddleHit(aiShot == ArcadeShotType.Smash ? 0.95f : 0.65f);
        }
    }
}