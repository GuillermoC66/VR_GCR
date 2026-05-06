using UnityEngine;
using System;

public class PaddleFlickController : MonoBehaviour
{
    public static event Action OnPlayerHit;
    [Header("Configuración del Muñequeo (Arcade)")]
    [SerializeField] private float flickMultiplier = 0.5f;
    [Tooltip("Velocidad mínima garantizada al golpear")]
    [SerializeField] private float baseHitSpeed = 8f;
    [Tooltip("Velocidad máxima permitida al golpear")]
    [SerializeField] private float maxHitSpeed = 15f;

    [Header("Asistencia de Apuntado (Aim Assist)")]
    [Tooltip("0 = Física pura (Difícil). 1 = Apuntado automático (Fácil). Usa 0.1 o 0.2 para un toque sutil.")]
    [Range(0f, 1f)]
    [SerializeField] private float aimAssistStrength = 0.2f;
    [Tooltip("Elevación artificial para crear una parábola y asegurar que la pelota pase la red.")]
    [SerializeField] private float upwardBoost = 0.2f;

    // Propiedades públicas para que el PowerManager pueda modificarlos
    public float AimAssistStrength { get => aimAssistStrength; set => aimAssistStrength = value; }
    public float UpwardBoost { get => upwardBoost; set => upwardBoost = value; }
    public float BaseHitSpeed { get => baseHitSpeed; set => baseHitSpeed = value; }
    public float MaxHitSpeed { get => maxHitSpeed; set => maxHitSpeed = value; }

    
    [Tooltip("El objeto vacío en la mesa hacia donde la pelota intentará ir.")]
    public Transform targetPoint; 

    [Header("Parámetros de Colisión")]
    [SerializeField] private string ballTag = "Ball";

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 manualVelocity;
    private Vector3 manualAngularVelocity;

    void Start()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
    }

    void FixedUpdate()
    {
        // Calcular velocidad manual (se mantiene igual)
        manualVelocity = (transform.position - lastPosition) / Time.fixedDeltaTime;
        lastPosition = transform.position;

        Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(lastRotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        manualAngularVelocity = axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);
        lastRotation = transform.rotation;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag(ballTag))
        {
            Rigidbody ballRb = collision.gameObject.GetComponent<Rigidbody>();
            if (ballRb != null)
            {
                Vector3 contactPoint = collision.contacts[0].point;
                Vector3 pointVelocity = manualVelocity + Vector3.Cross(manualAngularVelocity, (contactPoint - transform.position));

                if (pointVelocity.magnitude > 0.1f)
                {
                    // 1. Vector Real (La normal de la pala)
                    Vector3 rawDirection = collision.contacts[0].normal.normalized;

                    // 2. Vector Ideal (Hacia el objetivo)
                    Vector3 idealDirection = rawDirection; // Por defecto es igual al real
                    
                    if (targetPoint != null)
                    {
                        Vector3 dirToTarget = (targetPoint.position - ballRb.position).normalized;
                        
                        // Si la asistencia está al máximo (Super Golpe), forzamos que vaya al objetivo sin importar cómo pegue
                        if (aimAssistStrength >= 1f)
                        {
                            idealDirection = dirToTarget;
                        }
                        // De lo contrario, solo asistimos si el golpe tiene una dirección medianamente correcta (hacia adelante)
                        else if (Vector3.Dot(rawDirection, dirToTarget) > 0f) 
                        {
                            idealDirection = dirToTarget;
                        }
                    }

                    // 3. Mezclar los vectores (Magia del Aim Assist)
                    Vector3 finalDirection = Vector3.Lerp(rawDirection, idealDirection, aimAssistStrength).normalized;

                    // 3.5 Añadir la parábola artificial hacia arriba
                    finalDirection = (finalDirection + Vector3.up * upwardBoost).normalized;

                    // 4. Calcular velocidad de salida estilo Arcade
                    float rawSpeed = pointVelocity.magnitude * flickMultiplier;
                    float finalSpeed = Mathf.Clamp(baseHitSpeed + rawSpeed, baseHitSpeed, maxHitSpeed);
                    
                    // Aplicar la velocidad directamente en lugar de usar fuerzas para evitar física volátil
                    ballRb.linearVelocity = finalDirection * finalSpeed; 
                    ballRb.angularVelocity = Vector3.zero; // Evitar efectos de rotación no deseados
                    
                    // Notificar que el jugador golpeó la pelota (útil para lógica de robots/AI)
                    OnPlayerHit?.Invoke();
                }
            }
        }
    }
}