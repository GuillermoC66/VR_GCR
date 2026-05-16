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
    
    [Tooltip("Ángulo en grados (0 a 90) que tendrá el tiro hacia arriba. Afecta al tiro físico de la pelota.")]
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
    
    [Tooltip("Objeto 3D que rotará hacia la dirección de lanzamiento (eje +X apuntará hacia el objetivo)")]
    public Transform visualModel;

    [Header("Ajuste Visual del Modelo")]
    [Tooltip("Ajuste de posición X (Izquierda/Derecha) para alinear el modelo con la pelota")]
    [Range(-1f, 1f)]
    public float modelOffsetX = 0f;

    [Tooltip("Ajuste de posición Z (Adelante/Atrás) para alinear el modelo con la pelota")]
    [Range(-1f, 1f)]
    public float modelOffsetZ = 0f;

    [Tooltip("Ajuste de posición Y (Arriba/Abajo) para alinear el modelo con la pelota")]
    [Range(-1f, 1f)]
    public float modelOffsetY = 0f;
    
    [Header("Animación")]
    [Tooltip("El Animator del modelo visual (arrastra el objeto que tiene el Animator aquí)")]
    public Animator robotAnimator;
    
    [Tooltip("Nombre del parámetro tipo Trigger en el Animator para reproducir el tiro")]
    public string shootTriggerName = "Shoot";

    [Tooltip("Tiempo en segundos que tarda la animación en disparar físicamente la pelota")]
    public float animationDelay = 0.5f;

    [Header("Sonido")]
    [Tooltip("El sonido que se reproducirá al iniciar la animación de disparo")]
    public AudioClip shootSound;
    
    [Tooltip("El AudioSource que reproducirá el sonido. Si lo dejas vacío, el script buscará uno automáticamente en este mismo objeto.")]
    public AudioSource audioSource;

    [Header("Modo Automático")]
    [Tooltip("Si está activo, el robot lanzará pelotas automáticamente en un bucle infinito.")]
    public bool isAutoMode = true;

    [Tooltip("Frecuencia (en segundos) con la que el robot dispara pelotas automáticamente")]
    [Range(1f, 10f)]
    public float shootFrequency = 3f;

    [Header("Tiros Disponibles (Game Feel Test)")]
    public List<RobotShot> availableShots = new List<RobotShot>();

    private Coroutine launchCoroutine;

    void Awake()
    {
        // Si el usuario olvidó asignar un AudioSource, tratamos de encontrar uno en el mismo objeto
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

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
        StartCoroutine(LaunchSequence());
    }

    private IEnumerator LaunchSequence()
    {
        if (ball == null) 
        {
            Debug.LogWarning("El robot no tiene una pelota asignada.");
            yield break;
        }

        // 1. Elegir posición de lanzamiento al azar para cambiar de lugar
        Transform launchPos = transform; // Por defecto usa su propia posición
        if (launchPositions != null && launchPositions.Length > 0)
        {
            launchPos = launchPositions[Random.Range(0, launchPositions.Length)];
            
            // Mover al robot físicamente a esa posición para que el jugador perciba el cambio
            // Aplicamos los sliders de offset para alinear la base del modelo
            transform.position = launchPos.position + new Vector3(modelOffsetX, modelOffsetY, modelOffsetZ);
        }

        // 2. Elegir un tiro aleatorio habilitado
        List<RobotShot> validShots = availableShots.FindAll(s => s.isEnabled && s.targetPoint != null);
        if (validShots.Count == 0)
        {
            Debug.LogWarning("El Robot no tiene tiros habilitados o les falta Target Point en el Inspector.");
            yield break;
        }

        RobotShot selectedShot = validShots[Random.Range(0, validShots.Count)];

        // 3. Re-utilizar la pelota y ponerla en el cañón del robot
        ball.ResetAndFloat(launchPos.position);

        // 4. Apuntar visualmente el robot y calcular dirección física
        Vector3 directionToTarget = (selectedShot.targetPoint.position - launchPos.position).normalized;
        Vector3 rightAxis = Vector3.Cross(Vector3.up, directionToTarget).normalized;
        Vector3 launchDirection = Quaternion.AngleAxis(-selectedShot.upwardArc, rightAxis) * directionToTarget;

        if (visualModel != null)
        {
            Vector3 targetPosXZ = selectedShot.targetPoint.position;
            targetPosXZ.y = visualModel.position.y; // Ignorar altura para Yaw
            Vector3 directionXZ = (targetPosXZ - visualModel.position).normalized;

            if (directionXZ != Vector3.zero)
            {
                visualModel.rotation = Quaternion.LookRotation(directionXZ) * Quaternion.Euler(0, 90, 0);
                Vector3 currentLocalRot = visualModel.localEulerAngles;
                currentLocalRot.z = -selectedShot.upwardArc;
                visualModel.localEulerAngles = currentLocalRot;
            }
        }

        // 5. Reproducir animación de disparo y sonido
        if (robotAnimator != null && !string.IsNullOrEmpty(shootTriggerName))
        {
            robotAnimator.SetTrigger(shootTriggerName);
        }

        if (shootSound != null && audioSource != null)
        {
            if (MenuManager.sfxVolume <= 0f)
            {
                Debug.LogWarning("El sonido del robot se intentó reproducir, pero MenuManager.sfxVolume está en 0.");
            }
            
            // Reproducimos el sonido utilizando tu variable de volumen global de SFX
            audioSource.PlayOneShot(shootSound, MenuManager.sfxVolume);
        }
        else
        {
            if (shootSound == null) Debug.LogWarning("Falta asignar el 'Shoot Sound' en el Inspector del Robot.");
            if (audioSource == null) Debug.LogWarning("El Robot no tiene un componente 'Audio Source'. Añádelo desde el Inspector.");
        }

        // 6. Esperar a que la animación llegue al punto exacto donde sale la pelota
        if (animationDelay > 0f)
        {
            yield return new WaitForSeconds(animationDelay);
        }

        // 7. Aplicar fuerza física a la pelota (disparo real)
        Rigidbody ballRb = ball.GetComponent<Rigidbody>();
        if (ballRb != null)
        {
            ballRb.useGravity = true;
            ballRb.linearVelocity = launchDirection * selectedShot.speed;
            ballRb.angularVelocity = Vector3.zero; 
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
