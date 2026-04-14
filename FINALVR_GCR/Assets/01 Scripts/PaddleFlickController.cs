using UnityEngine;

public class PaddleFlickController : MonoBehaviour
{
    [Header("Configuración del Golpe")]
    [Tooltip("Multiplicador de fuerza del golpe. Sube si el golpe se siente débil.")]
    [SerializeField] private float flickMultiplier = 3.5f;

    [Tooltip("Velocidad mínima de la pala para registrar un golpe válido.")]
    [SerializeField] private float minHitSpeed = 0.4f;

    [Tooltip("Tiempo mínimo entre golpes (segundos). Evita doble-impacto.")]
    [SerializeField] private float hitCooldown = 0.15f;

    [Tooltip("Tag de la pelota.")]
    [SerializeField] private string ballTag = "Ball";

    // ── Velocity tracking ────────────────────────────────
    private Vector3    _lastPosition;
    private Quaternion _lastRotation;
    private Vector3    _manualVelocity;
    private Vector3    _manualAngularVelocity;
    private float      _cooldownTimer;

    void Start()
    {
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
    }

    void FixedUpdate()
    {
        // ── Track linear velocity ────────────────────────
        _manualVelocity = (transform.position - _lastPosition) / Time.fixedDeltaTime;
        _lastPosition   = transform.position;

        // ── Track angular velocity ───────────────────────
        Quaternion delta = transform.rotation * Quaternion.Inverse(_lastRotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        _manualAngularVelocity = axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);
        _lastRotation = transform.rotation;

        // ── Tick cooldown ────────────────────────────────
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.fixedDeltaTime;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag(ballTag)) return;
        if (_cooldownTimer > 0f) return;   // prevent double-hit same frame

        Rigidbody ballRb = collision.gameObject.GetComponent<Rigidbody>();
        if (ballRb == null) return;

        // ── Speed of paddle at the contact point ─────────
        Vector3 contactPoint  = collision.contacts[0].point;
        Vector3 contactNormal = collision.contacts[0].normal;

        // Full point velocity: linear + angular contribution
        Vector3 pointVelocity = _manualVelocity +
            Vector3.Cross(_manualAngularVelocity, contactPoint - transform.position);

        float paddleSpeed = pointVelocity.magnitude;

        // ── Ignore weak touches ──────────────────────────
        if (paddleSpeed < minHitSpeed)
        {
            Debug.Log($"[Paddle] Golpe débil ignorado ({paddleSpeed:F2} m/s)");
            return;
        }

        // ── Build hit direction ───────────────────────────
        // Primary direction = where the paddle is moving
        // We ensure the ball always goes away from the paddle face (contactNormal)
        Vector3 hitDir = pointVelocity.normalized;

        // If paddle direction opposes the contact normal, flip to avoid
        // the ball being pushed INTO the paddle
        if (Vector3.Dot(hitDir, contactNormal) < 0f)
            hitDir = Vector3.Reflect(hitDir, contactNormal);

        // ── Apply velocity directly ───────────────────────
        // Instead of AddForce on top of an existing velocity (additive = unpredictable),
        // we SET the velocity so the result is always proportional to paddle speed.
        // This feels much more responsive and consistent.
        Vector3 newVelocity = hitDir * (paddleSpeed * flickMultiplier);

        // Guarantee a minimum upward component so ball clears the net
        newVelocity.y = Mathf.Max(newVelocity.y, 1.5f);

        ballRb.linearVelocity  = newVelocity;
        ballRb.angularVelocity = Vector3.zero;  // clean spin

        _cooldownTimer = hitCooldown;

        Debug.Log($"[Paddle] Golpe — pala:{paddleSpeed:F1} m/s  pelota:{newVelocity.magnitude:F1} m/s  dir:{hitDir}");
    }
}