using UnityEngine;

/// <summary>
/// Attach this to your PingPongTable GameObject after importing the FBX.
/// It sets up colliders, physics material, and VR interaction layer automatically.
/// </summary>
[ExecuteInEditMode]
public class TableSetup : MonoBehaviour
{
    [Header("Table Dimensions (metres)")]
    [Tooltip("Standard ping pong table: 2.74m long, 1.525m wide, 0.76m tall")]
    public Vector3 tableDimensions = new Vector3(2.74f, 0.76f, 1.525f);

    [Header("Physics")]
    public float bounciness = 0.85f;       // how bouncy the surface is (0–1)
    public float frictionStatic = 0.4f;    // static friction
    public float frictionDynamic = 0.35f;  // dynamic friction

    [Header("VR Layers")]
    [Tooltip("Assign the layer you use for interactable/collidable environment objects")]
    public string environmentLayer = "Environment";

    [Header("References — assign in Inspector or auto-detected")]
    public MeshRenderer tableRenderer;
    public MeshRenderer netRenderer;

    void Start()
    {
        SetupLayer();
        SetupColliders();
        SetupPhysicsMaterial();

    }

    // ─────────────────────────────────────────────
    // LAYER
    // ─────────────────────────────────────────────
    void SetupLayer()
    {
        int layer = LayerMask.NameToLayer(environmentLayer);
        if (layer == -1)
        {
            Debug.LogWarning($"[TableSetup] Layer '{environmentLayer}' not found. " +
                             "Create it in Edit > Project Settings > Tags and Layers.");
            return;
        }
        // Apply layer to this object and all children
        SetLayerRecursive(gameObject, layer);
    }

    void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    // ─────────────────────────────────────────────
    // COLLIDERS
    // ─────────────────────────────────────────────
    void SetupColliders()
    {
        // Remove any auto-generated mesh colliders from FBX import
        // (they're expensive — we replace them with box colliders)
        MeshCollider[] meshColliders = GetComponentsInChildren<MeshCollider>();
        foreach (var mc in meshColliders)
        {
            Debug.Log($"[TableSetup] Removing mesh collider from {mc.gameObject.name}, replacing with box collider.");
            DestroyImmediate(mc);
        }

        // TABLE SURFACE — main flat playing area
        // This is the collider the ball will bounce on
        AddBoxCollider(
            gameObject,
            name: "Collider_Surface",
            center: new Vector3(-0.333f, tableDimensions.y, 2.168f),       // top of table
            size: new Vector3(tableDimensions.x, 0.04f, tableDimensions.z)  // thin slab
        );

        // TABLE BODY — the legs and frame as a single blocker
        // Players shouldn't walk through the table base
        AddBoxCollider(
            gameObject,
            name: "Collider_Body",
            center: new Vector3(-0.333f, tableDimensions.y / 2f, 2.168f),
            size: new Vector3(tableDimensions.x, tableDimensions.y, tableDimensions.z)
        );

        // NET — thin vertical blocker at the centre
        AddBoxCollider(
            gameObject,
            name: "Collider_Net",
            center: new Vector3(-0.333f, tableDimensions.y + 0.08f, 2.168f),  // sits above surface
            size: new Vector3(0.02f, 0.16f, tableDimensions.z + 0.1f) // thin wall
        );

        Debug.Log("[TableSetup] Colliders set up successfully.");
    }

    void AddBoxCollider(GameObject parent, string name, Vector3 center, Vector3 size)
    {
        // Re-use existing child with this name, or create new one
        Transform existing = parent.transform.Find(name);
        GameObject colliderObj = existing != null
            ? existing.gameObject
            : new GameObject(name);

        colliderObj.transform.SetParent(parent.transform, false);
        colliderObj.transform.localPosition = Vector3.zero;
        colliderObj.transform.localRotation = Quaternion.identity;

        BoxCollider bc = colliderObj.GetComponent<BoxCollider>();
        if (bc == null) bc = colliderObj.AddComponent<BoxCollider>();

        bc.center = center;
        bc.size = size;
    }

    // ─────────────────────────────────────────────
    // PHYSICS MATERIAL
    // ─────────────────────────────────────────────
    void SetupPhysicsMaterial()
    {
        PhysicsMaterial mat = new PhysicsMaterial("TableSurface")
        {
            bounciness = this.bounciness,
            staticFriction = frictionStatic,
            dynamicFriction = frictionDynamic,
            bounceCombine = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Average
        };

        // Apply to the surface collider only (not the body/net)
        BoxCollider surfaceCollider = transform.Find("Collider_Surface")
            ?.GetComponent<BoxCollider>();

        if (surfaceCollider != null)
        {
            surfaceCollider.material = mat;
            Debug.Log("[TableSetup] Physics material applied to table surface.");
        }
    }
}