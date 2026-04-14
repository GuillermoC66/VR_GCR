using UnityEngine;

public class PaddleFlickController : MonoBehaviour
{
    [Header("Configuración del Muñequeo")]
    [Tooltip("Multiplicador para aumentar la fuerza del 'muñequeo'. Empieza en 0.5 y ajusta.")]
    [SerializeField] private float flickMultiplier = 0.5f;

    [Header("Parámetros de Colisión")]
    [Tooltip("Tag para identificar la pelota.")]
    [SerializeField] private string ballTag = "Ball";

    // Variables para calcular la velocidad manual
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
        // 1. Calcular velocidad linear manual
        manualVelocity = (transform.position - lastPosition) / Time.fixedDeltaTime;
        lastPosition = transform.position;

        // 2. Calcular velocidad angular manual (aproximación)
        Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(lastRotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);
        // Asegurar que el ángulo esté en el rango -180 a 180
        if (angle > 180f) angle -= 360f;
        manualAngularVelocity = axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);
        lastRotation = transform.rotation;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag(ballTag))
        {
            // Obtenemos el Rigidbody de la pelota
            Rigidbody ballRb = collision.gameObject.GetComponent<Rigidbody>();
            if (ballRb != null)
            {
                // Punto de impacto
                Vector3 contactPoint = collision.contacts[0].point;
                
                // Calculamos la velocidad de la pala en el punto de impacto
                // La fórmula física: V_point = V_linear + W x (R_point - R_center)
                // donde W es la velocidad angular y R es el vector de posición.
                Vector3 pointVelocity = manualVelocity + Vector3.Cross(manualAngularVelocity, (contactPoint - transform.position));

                // Unity ya calculó el rebote base. Nosotros añadimos un "impulso" extra
                // basado en la velocidad de la pala al impactar, con un multiplicador.
                
                // Solo aplicamos si la velocidad del impacto es significativa
                if (pointVelocity.magnitude > 0.1f)
                {
                    // Vector de dirección de la fuerza. Usamos la dirección del rebote base.
                    Vector3 bounceDirection = ballRb.linearVelocity.normalized;
                    float extraForce = pointVelocity.magnitude * flickMultiplier;

                    // Aplicamos el impulso extra a la pelota
                    ballRb.AddForce(bounceDirection * extraForce, ForceMode.Impulse);
                }
            }
        }
    }
}