using UnityEngine;

public class PaddleFlickController : MonoBehaviour
{
    [Header("Configuración del Muñequeo")]
    [SerializeField] private float flickMultiplier = 0.5f;

    [Header("Asistencia de Apuntado (Aim Assist)")]
    [Tooltip("0 = Física pura (Difícil). 1 = Apuntado automático (Fácil). Usa 0.1 o 0.2 para un toque sutil.")]
    [Range(0f, 1f)]
    [SerializeField] private float aimAssistStrength = 0.2f;
    
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
                    
                    // Solo asistimos si tenemos un objetivo asignado y la pelota va hacia adelante
                    if (targetPoint != null)
                    {
                        Vector3 dirToTarget = (targetPoint.position - ballRb.position).normalized;
                        // Opcional: Solo asistir si el golpe tiene una dirección medianamente correcta (hacia la red)
                        if (Vector3.Dot(rawDirection, dirToTarget) > 0f) 
                        {
                            idealDirection = dirToTarget;
                        }
                    }

                    // 3. Mezclar los vectores (Magia del Aim Assist)
                    Vector3 finalDirection = Vector3.Lerp(rawDirection, idealDirection, aimAssistStrength).normalized;

                    // 4. Aplicar la fuerza extra en la nueva dirección asistida
                    float extraForce = pointVelocity.magnitude * flickMultiplier;
                    
                    // Resetear la velocidad actual de la pelota para evitar que rebotes extraños interfieran
                    ballRb.linearVelocity = Vector3.zero; 
                    ballRb.AddForce(finalDirection * extraForce, ForceMode.Impulse);
                }
            }
        }
    }
}