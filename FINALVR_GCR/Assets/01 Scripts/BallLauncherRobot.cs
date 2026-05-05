using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class RobotShot
{
    [Tooltip("Nombre para identificar el tiro en el inspector (ej. Saque Fuerte, Dejada, Cruzado)")]
    public string shotName = "Tiro Básico";
    
    [Tooltip("Si está activo, el robot podrá usar este tiro aleatoriamente.")]
    public bool isEnabled = true;
    
    [Tooltip("El Empty/Transform en la mesa al que apuntará este tiro")]
    public Transform targetPoint;
    
    [Tooltip("Velocidad lineal del tiro")]
    public float speed = 10f;
    
    [Tooltip("Ángulo en grados (0 a 90) que tendrá el tiro hacia arriba. Afecta tanto al tiro físico como a la rotación Z visual del robot.")]
    [Range(0f, 90f)]
    public float upwardArc = 15f;
}

public class BallLauncherRobot : MonoBehaviour
{
    [Header("Configuración del Robot")]
    [Tooltip("Puntos desde donde el robot puede disparar (cambia aleatoriamente de lugar)")]
    public Transform[] launchPositions;
    
    [Tooltip("Referencia a la pelota (se re-utilizará para evitar sobrecargar memoria)")]
    public BallController ball;
    [Header("Modo Automático")]
    [Tooltip("Si está activo, el robot lanzará pelotas automáticamente en un bucle infinito.")]
    public bool isAutoMode = true;

    [Tooltip("Frecuencia (en segundos) con la que el robot dispara pelotas automáticamente")]
    [Range(1f, 10f)]
    public float shootFrequency = 3f;

    [Header("Tiros Disponibles (Game Feel Test)")]
    public List<RobotShot> availableShots = new List<RobotShot>();

    private Coroutine launchCoroutine;

    void OnEnable()
    {
        if (isAutoMode)
        {
            StartAutoMode();
        }
    }

    void OnDisable()
    {
        if (launchCoroutine != null)
        {
            StopCoroutine(launchCoroutine);
        }
    }

    public void StartAutoMode()
    {
        isAutoMode = true;
        if (launchCoroutine != null) StopCoroutine(launchCoroutine);
        launchCoroutine = StartCoroutine(AutoShootLoop());
    }

    private IEnumerator AutoShootLoop()
    {
        // Pequeña pausa antes del primer tiro
        yield return new WaitForSeconds(2f);
        
        while (isAutoMode)
        {
            LaunchBall();
            yield return new WaitForSeconds(shootFrequency);
        }
    }

    [ContextMenu("Forzar Lanzamiento Manual (Test)")]
    public void LaunchBall()
    {
        if (ball == null) 
        {
            Debug.LogWarning("El robot no tiene una pelota asignada.");
            return;
        }

        // 1. Elegir posición de lanzamiento al azar para cambiar de lugar
        Transform launchPos = transform; // Por defecto usa su propia posición
        if (launchPositions != null && launchPositions.Length > 0)
        {
            launchPos = launchPositions[Random.Range(0, launchPositions.Length)];
            
            // Mover al robot físicamente a esa posición para que el jugador perciba el cambio
            transform.position = launchPos.position;
        }

        // 2. Elegir un tiro aleatorio habilitado
        List<RobotShot> validShots = availableShots.FindAll(s => s.isEnabled && s.targetPoint != null);
        if (validShots.Count == 0)
        {
            Debug.LogWarning("El Robot no tiene tiros habilitados o les falta Target Point en el Inspector.");
            return;
        }

        RobotShot selectedShot = validShots[Random.Range(0, validShots.Count)];

        // 3. Re-utilizar la pelota: esto hará que desaparezca de la mesa y se teletransporte al robot.
        // Esto ahorra muchísima memoria y evita "basura" (garbage collection) comparado con Instanciar/Destruir.
        ball.ResetAndFloat(launchPos.position);

        // 4. Disparar
        Rigidbody ballRb = ball.GetComponent<Rigidbody>();
        if (ballRb != null)
        {
            // ResetAndFloat apaga la gravedad, así que la reactivamos para el vuelo de la pelota
            ballRb.useGravity = true;

            // Calcular dirección base hacia el objetivo
            Vector3 directionToTarget = (selectedShot.targetPoint.position - launchPos.position).normalized;

            // Rotar la dirección hacia arriba basándonos en los grados del upwardArc
            // (Para que físicamente la pelota también haga el arco y no se estrelle en la red)
            Vector3 rightAxis = Vector3.Cross(Vector3.up, directionToTarget).normalized;
            Vector3 launchDirection = Quaternion.AngleAxis(-selectedShot.upwardArc, rightAxis) * directionToTarget;

            // Aplicar la física arcade con la nueva dirección elevada
            ballRb.linearVelocity = launchDirection * selectedShot.speed;
            ballRb.angularVelocity = Vector3.zero; 
            
            // --- ACTUALIZACIÓN VISUAL DEL ROBOT ---
            // 1. Mirar hacia el objetivo SOLO en el eje Y (para que no se incline hacia abajo)
            Vector3 lookPos = selectedShot.targetPoint.position;
            lookPos.y = transform.position.y; // Ignoramos la altura del target
            transform.LookAt(lookPos);
            
            // 2. Obtener la rotación actual para modificar Z
            Vector3 currentRotation = transform.localEulerAngles;
            currentRotation.x = 0f; // Mantener la inclinación frontal en 0 (solo rota en Y y Z)
            
            // Conectar el arco directamente al eje Z (en grados)
            if(selectedShot.upwardArc>=0 && selectedShot.upwardArc<=45)
            {
                currentRotation.z = 90;
            }
            else
            {
                currentRotation.z = selectedShot.upwardArc;
            }
            
            transform.localEulerAngles = currentRotation;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (availableShots == null) return;

        // Utilizamos la posición actual del robot para dibujar los gizmos
        Vector3 startPos = transform.position;

        foreach (var shot in availableShots)
        {
            if (!shot.isEnabled || shot.targetPoint == null) continue;

            // 1. Dibujar línea directa al Target Point (gris)
            Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Gizmos.DrawLine(startPos, shot.targetPoint.position);
            Gizmos.DrawWireSphere(shot.targetPoint.position, 0.15f);

            // 2. Simular trayectoria parabólica con las físicas (línea verde/roja)
            Gizmos.color = Color.green;
            
            Vector3 directionToTarget = (shot.targetPoint.position - startPos).normalized;
            Vector3 rightAxis = Vector3.Cross(Vector3.up, directionToTarget).normalized;
            Vector3 launchDirection = Quaternion.AngleAxis(-shot.upwardArc, rightAxis) * directionToTarget;
            
            // Obtener variables de física de la pelota
            float maxVel = ball != null ? ball.MaxVelocity : 15f;
            Rigidbody ballRb = ball != null ? ball.GetComponent<Rigidbody>() : null;
            float mass = ballRb != null ? ballRb.mass : 1f;
            float drag = ballRb != null ? ballRb.linearDamping : 0f;

            // En Unity, si aplicas "velocidad" directa, la masa no afecta la curva. 
            // Pero si trataras 'speed' como Fuerza (Impulse), la velocidad inicial sería speed / mass.
            // Aquí simularemos la velocidad directa (como lo hace el script), limitada por el maxVelocity de BallController
            Vector3 currentVelocity = launchDirection * shot.speed;
            if (currentVelocity.magnitude > maxVel)
            {
                currentVelocity = currentVelocity.normalized * maxVel;
            }

            Vector3 currentPos = startPos;
            Vector3 prevPos = startPos;
            
            float timeStep = Time.fixedDeltaTime; // Usar el mismo paso de tiempo que Unity (0.02s) para exactitud
            int maxSteps = (int)(3f / timeStep); // 3 segundos de simulación

            // Simulación iterativa frame por frame (igual que el motor físico de Unity)
            for (int i = 1; i <= maxSteps; i++)
            {
                // 1. Gravedad
                currentVelocity += Physics.gravity * timeStep;
                
                // 2. Resistencia del aire (Drag)
                currentVelocity *= Mathf.Clamp01(1f - drag * timeStep);

                // 3. Límite de velocidad (el que configuraste en BallController)
                if (currentVelocity.magnitude > maxVel)
                {
                    currentVelocity = currentVelocity.normalized * maxVel;
                }

                // 4. Actualizar posición
                currentPos += currentVelocity * timeStep;
                
                Gizmos.DrawLine(prevPos, currentPos);
                prevPos = currentPos;

                // Romper el ciclo si ya cayó debajo de la mesa
                if (currentPos.y < shot.targetPoint.position.y - 0.5f && currentVelocity.y <= 0)
                {
                    break;
                }
            }
        }
    }
}
