using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Rigidbody), typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class BallController : MonoBehaviour
{
    private Rigidbody rb;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    
    [Header("Físicas")]
    [SerializeField] private float maxVelocity = 15f; // Límite para evitar que atraviese paredes
    public float MaxVelocity => maxVelocity;

    [Header("Asistencia de Saque")]
    [Tooltip("Impulso extra hacia arriba al soltar la pelota")]
    [SerializeField] private float tossBoostUpward = 1.5f;
    [Tooltip("Impulso extra hacia adelante (según la mano) al soltar la pelota")]
    [SerializeField] private float tossBoostForward = 0.5f;

    private bool isGrabbed = false;
    private Vector3 initialPosition;

    [Header("Arcade Physics")]
    [Tooltip("Si es true, se genera un material físico sin fricción para rebotes limpios en paredes y mesa.")]
    [SerializeField] private bool useArcadePhysics = true;
    [Range(0f, 1f)]
    [SerializeField] private float arcadeBounciness = 0.95f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        // Vital para objetos pequeños a alta velocidad en VR
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Suscribirse a los eventos de agarre nativos de XRI
        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);

        initialPosition = transform.position;

        if (useArcadePhysics)
        {
            SetupArcadePhysics();
        }
    }

    private void SetupArcadePhysics()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            PhysicsMaterial arcadeMat = new PhysicsMaterial("ArcadeBall");
            arcadeMat.bounciness = arcadeBounciness; 
            arcadeMat.dynamicFriction = 0f; // Sin fricción para no tomar efectos raros en las paredes
            arcadeMat.staticFriction = 0f;
            arcadeMat.frictionCombine = PhysicsMaterialCombine.Minimum; // Siempre usar la fricción 0
            arcadeMat.bounceCombine = PhysicsMaterialCombine.Maximum;   // Priorizar el rebote alto
            col.material = arcadeMat;
        }
        
        // Reducir la resistencia del aire para que mantenga su velocidad
        rb.linearDamping = 0.1f;
        rb.angularDamping = 1f;
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
        rb.useGravity = true; // Restaurar gravedad al agarrar
        // Al agarrarla, podemos silenciar sonidos o detener rotaciones extrañas
        rb.angularVelocity = Vector3.zero;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        isGrabbed = false;
        
        // Darle un pequeño "boost" de empuje hacia adelante y arriba al soltarla 
        // para facilitar el saque (lanzamiento o 'toss').
        if (args.interactorObject != null)
        {
            Transform handTransform = args.interactorObject.transform;
            
            // Usamos Vector3.up global para que siempre vaya hacia arriba sin importar la rotación de la mano.
            // Y usamos el forward de la mano para que vaya ligeramente hacia donde apunta el jugador.
            Vector3 boostDirection = (Vector3.up * tossBoostUpward) + (handTransform.forward * tossBoostForward);
            
            rb.linearVelocity += boostDirection;
        }
    }

    void OnDestroy()
    {
        // Limpieza de eventos para evitar memory leaks
        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }

    /// <summary>
    /// Resetea la pelota a una posición y la deja flotando (sin gravedad)
    /// </summary>
    public void ResetAndFloat(Vector3? spawnPosition = null)
    {
        if (spawnPosition.HasValue)
        {
            transform.position = spawnPosition.Value;
        }
        else
        {
            // Si no hay posición, la regresamos a donde empezó en la escena
            transform.position = initialPosition;
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = false; // Desactivar gravedad para que flote
    }

    /// <summary>
    /// Permite o bloquea el agarre de la pelota
    /// </summary>
    public void SetGrabbable(bool grabbable)
    {
        if (grabInteractable != null)
        {
            // Si le quitamos el grabbable mientras la tiene en la mano, XRI la suelta automáticamente
            grabInteractable.enabled = grabbable;
        }
    }

    public bool IsGrabbable
    {
        get { return grabInteractable != null && grabInteractable.enabled; }
    }
}