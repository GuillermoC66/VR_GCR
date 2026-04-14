using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Rigidbody), typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class BallController : MonoBehaviour
{
    private Rigidbody rb;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    
    [Header("Físicas")]
    [SerializeField] private float maxVelocity = 15f; // Límite para evitar que atraviese paredes
    private bool isGrabbed = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        // Vital para objetos pequeños a alta velocidad en VR
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Suscribirse a los eventos de agarre nativos de XRI
        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);
    }

    void FixedUpdate()
    {
        // Limitar la velocidad máxima solo si la pelota está libre (no agarrada)
        if (!isGrabbed && rb.linearVelocity.magnitude > maxVelocity)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxVelocity;
        }
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        // Al agarrarla, podemos silenciar sonidos o detener rotaciones extrañas
        rb.angularVelocity = Vector3.zero;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        isGrabbed = false;
        // Opcional: Darle un pequeño "boost" de empuje hacia adelante al soltarla 
        // para facilitar el saque, pero el XRI por defecto hereda bien la velocidad.
    }

    void OnDestroy()
    {
        // Limpieza de eventos para evitar memory leaks
        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }
}