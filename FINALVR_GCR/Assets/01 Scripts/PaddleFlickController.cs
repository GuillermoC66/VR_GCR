using UnityEngine;
using System;
using UnityEngine.InputSystem;

public class PaddleFlickController : MonoBehaviour
{
    public static event Action OnPlayerHit;

    [Header("Arcade Inputs")]
    public InputActionReference buttonA; // Cortado
    public InputActionReference buttonB; // Mate

    [Header("Configuración de Velocidad Arcade")]
    public float baseHitSpeed = 8f;
    public float maxHitSpeed = 25f;
    [Tooltip("Multiplicador para golpes normales")]
    public float flickMultiplier = 0.5f;

    [Header("Puntería Arcade (Target Point)")]
    [Tooltip("El objeto vacío en la mesa hacia donde la pelota irá SIEMPRE.")]
    public Transform targetPoint; 
    
    [Tooltip("Añade una parábola artificial para que pase la red")]
    public float baseUpwardBoost = 0.2f;

    [Header("Desvío por Timing (Arcade)")]
    [Tooltip("Cantidad máxima de desvío lateral cuando el timing es 'Muy Atrasado' o 'Muy Adelantado'")]
    public float maxDeflection = 1.5f;

    public enum TimingTier { MuyAtrasado, Atrasado, Perfecto, Adelantado, MuyAdelantado }

    private Vector3 lastPosition;
    private Vector3 manualVelocity;

    private PowerManager powerManager;

    void Start()
    {
        lastPosition = transform.position;
        powerManager = GetComponent<PowerManager>();
    }

    void FixedUpdate()
    {
        manualVelocity = (transform.position - lastPosition) / Time.fixedDeltaTime;
        lastPosition = transform.position;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ball"))
        {
            Rigidbody ballRb = collision.gameObject.GetComponent<Rigidbody>();
            if (ballRb != null && manualVelocity.magnitude > 0.1f)
            {
                // 1. Calcular Dirección Base 100% Arcade
                // Ignoramos la rotación física de la pala. Siempre vamos al Target Point.
                Vector3 startPos = collision.contacts[0].point;
                Vector3 dirToTarget = (targetPoint != null) ? 
                                      (targetPoint.position - startPos).normalized : 
                                      transform.forward;

                // 2. Determinar el Timing basado en la física del choque (rawDirection)
                // Esto es mucho más robusto que usar la posición local, ya que ignora si el modelo 3D de tu pala tiene el pivote movido.
                Vector3 rawDirection = collision.contacts[0].normal.normalized;
                
                // Calculamos hacia dónde habría ido la pelota físicamente (izquierda o derecha del Target)
                Vector3 rawDirXZ = new Vector3(rawDirection.x, 0, rawDirection.z).normalized;
                Vector3 targetDirXZ = new Vector3(dirToTarget.x, 0, dirToTarget.z).normalized;
                Vector3 targetRightXZ = new Vector3(targetDirXZ.z, 0, -targetDirXZ.x); // Vector perpendicular (derecha)
                
                // horizontalError va de -1 a 1. Positivo = habría ido a la derecha. Negativo = habría ido a la izquierda.
                float horizontalError = Vector3.Dot(rawDirXZ, targetRightXZ); 
                
                TimingTier timing = GetTimingTier(horizontalError);
                float timingOffset = GetDeflectionOffset(timing);

                // 3. Modificadores de Botones (Mate / Cortado / Especial)
                bool isSmash = buttonB != null && buttonB.action.IsPressed();
                bool isSlice = buttonA != null && buttonA.action.IsPressed();
                bool isSuper = powerManager != null && powerManager.isPowerActive;

                float finalMultiplier = flickMultiplier;
                float finalBaseSpeed = baseHitSpeed;
                float finalUpward = baseUpwardBoost;
                
                string debugHitType = "Normal";

                if (isSuper)
                {
                    debugHitType = "SUPER GOLPE";
                    finalBaseSpeed = 30f;
                    finalMultiplier = 0f; // Velocidad fija
                    finalUpward = 0.05f; // Tiro láser
                    timingOffset = 0f; // Precisión 100%
                }
                else if (isSmash)
                {
                    debugHitType = "Mate";
                    finalBaseSpeed += 7f;
                    finalMultiplier *= 1.5f;
                    finalUpward = 0.02f; // Más plano
                }
                else if (isSlice)
                {
                    debugHitType = "Cortado";
                    finalBaseSpeed = 4f;
                    finalMultiplier *= 0.3f;
                    finalUpward = 0.6f; // Parábola alta y lenta
                }

                // 4. Aplicar Desvío de Timing a la dirección
                Vector3 finalDirection = dirToTarget;
                if (timingOffset != 0f)
                {
                    // Desviamos usando el vector derecho respecto a la dirección al objetivo
                    Vector3 rightDir = Vector3.Cross(Vector3.up, dirToTarget).normalized;
                    finalDirection = (dirToTarget + rightDir * timingOffset).normalized;
                }

                // 5. Aplicar Upward Boost
                finalDirection = (finalDirection + Vector3.up * finalUpward).normalized;

                // 6. Calcular Velocidad Final
                float rawSpeed = manualVelocity.magnitude * finalMultiplier;
                float finalSpeed = Mathf.Clamp(finalBaseSpeed + rawSpeed, 0f, maxHitSpeed);

                // 7. Sobrescribir Físicas de la Pelota por completo
                ballRb.linearVelocity = finalDirection * finalSpeed;
                ballRb.angularVelocity = Vector3.zero; // Sin efectos rotacionales físicos reales

                Debug.Log($"[Arcade Hit] Tipo: {debugHitType} | Timing: {timing} | Velocidad: {finalSpeed}");

                OnPlayerHit?.Invoke();
            }
        }
    }

    private TimingTier GetTimingTier(float errorHorizontal)
    {
        // Positivo significa que el jugador apuntó físicamente hacia la derecha.
        // Negativo significa que apuntó físicamente hacia la izquierda.
        // Ajusta estos números (0.15, 0.4) para hacer el "Perfecto" más grande o más pequeño.
        if (errorHorizontal > 0.4f) return TimingTier.MuyAtrasado; // Muy a la derecha
        if (errorHorizontal > 0.15f) return TimingTier.Atrasado;   // A la derecha
        if (errorHorizontal >= -0.15f && errorHorizontal <= 0.15f) return TimingTier.Perfecto; // Recto
        if (errorHorizontal > -0.4f) return TimingTier.Adelantado; // A la izquierda
        return TimingTier.MuyAdelantado; // Muy a la izquierda
    }

    private float GetDeflectionOffset(TimingTier tier)
    {
        switch (tier)
        {
            case TimingTier.MuyAdelantado: return maxDeflection;
            case TimingTier.Adelantado: return maxDeflection * 0.3f;
            case TimingTier.Perfecto: return 0f;
            case TimingTier.Atrasado: return -maxDeflection * 0.3f;
            case TimingTier.MuyAtrasado: return -maxDeflection;
            default: return 0f;
        }
    }
}