using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Rigidbody), typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class BallController : MonoBehaviour
{
    private Rigidbody rb;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    
    [Header("Arcade Physics Sliders")]
    [Tooltip("Límite máximo de velocidad para evitar que atraviese paredes")]
    [Range(5f, 30f)]
    [SerializeField] private float maxSpeedLimit = 15f;
    public float MaxVelocity => maxSpeedLimit;

    [Tooltip("Cuánto rebota la pelota en la mesa y paredes (1 = No pierde energía, 0 = Muere al chocar)")]
    [Range(0f, 1f)]
    [SerializeField] private float bounciness = 0.95f;

    [Tooltip("Resistencia del aire (Fricción). 0 = Vuela limpia, >0 = Se frena en el aire.")]
    [Range(0f, 2f)]
    [SerializeField] private float airResistance = 0.1f;

    [Header("Asistencia de Saque")]
    [Tooltip("Impulso extra hacia arriba al soltar la pelota")]
    [SerializeField] private float tossBoostUpward = 1.5f;
    [Tooltip("Impulso extra hacia adelante (según la mano) al soltar la pelota")]
    [SerializeField] private float tossBoostForward = 0.5f;

    private bool isGrabbed = false;
    private Vector3 initialPosition;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        // Vital para objetos pequeños a alta velocidad en VR
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);

        initialPosition = transform.position;

        SetupArcadePhysics();
    }

    private void SetupArcadePhysics()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            PhysicsMaterial arcadeMat = new PhysicsMaterial("ArcadeBall");
            arcadeMat.bounciness = bounciness; 
            arcadeMat.dynamicFriction = 0f; // Física Arcade: Sin fricción en superficies
            arcadeMat.staticFriction = 0f;
            arcadeMat.frictionCombine = PhysicsMaterialCombine.Minimum;
            arcadeMat.bounceCombine = PhysicsMaterialCombine.Maximum;
            col.material = arcadeMat;
        }
        
        rb.linearDamping = airResistance;
        rb.angularDamping = 1f; // Evitar giros locos
    }

    void OnValidate()
    {
        // Si cambiamos los sliders en Play Mode, actualizar físicas
        if (Application.isPlaying && rb != null)
        {
            SetupArcadePhysics();
        }
    }

    void FixedUpdate()
    {
        if (!isGrabbed && rb.linearVelocity.magnitude > maxSpeedLimit)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeedLimit;
        }
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        rb.useGravity = true; 
        rb.angularVelocity = Vector3.zero;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        isGrabbed = false;
        
        if (args.interactorObject != null)
        {
            Transform handTransform = args.interactorObject.transform;
            Vector3 boostDirection = (Vector3.up * tossBoostUpward) + (handTransform.forward * tossBoostForward);
            rb.linearVelocity += boostDirection;
        }
    }

    void OnDestroy()
    {
        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }

    public void ResetAndFloat(Vector3? spawnPosition = null)
    {
        if (spawnPosition.HasValue)
        {
            transform.position = spawnPosition.Value;
        }
        else
        {
            transform.position = initialPosition;
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = false; // Sin gravedad para que flote en el cañón del robot
    }

    public void SetGrabbable(bool grabbable)
    {
        if (grabInteractable != null)
        {
            grabInteractable.enabled = grabbable;
        }
    }

    public bool IsGrabbable
    {
        get { return grabInteractable != null && grabInteractable.enabled; }
    }
}