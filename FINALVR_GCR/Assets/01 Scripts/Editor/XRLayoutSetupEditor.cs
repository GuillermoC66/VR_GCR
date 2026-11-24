#if UNITY_EDITOR
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public static class XRLayoutSetupEditor
{
    private const string PingPongActionsPath = "Assets/05 Input/PingPongInputActions.inputactions";
    private const string XriDefaultActionsPath = "Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/XRI Default Input Actions.inputactions";

    [MenuItem("Tools/PingPing PongPong/Configurar XR Layout Manager en Escena Actual")]
    public static void SetupInCurrentScene()
    {
        XROrigin xrOrigin = Object.FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null)
        {
            EditorUtility.DisplayDialog(
                "XR Origin no encontrado",
                "No se encontró un XR Origin en la escena actual. Agrega primero tu XR Origin y vuelve a ejecutar esta herramienta.",
                "Entendido"
            );
            return;
        }

        XRControllerLayoutManager manager = Object.FindFirstObjectByType<XRControllerLayoutManager>();
        if (manager == null)
        {
            manager = Undo.AddComponent<XRControllerLayoutManager>(xrOrigin.gameObject);
        }
        else
        {
            Undo.RecordObject(manager, "Configurar XRControllerLayoutManager");
        }

        manager.xrOrigin = xrOrigin;
        manager.AutoDetectReferences();

        InputActionAsset pingPongActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(PingPongActionsPath);
        InputActionAsset xriActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(XriDefaultActionsPath);

        if (pingPongActions != null) manager.pingPongInputActions = pingPongActions;
        if (xriActions != null) manager.xriDefaultInputActions = xriActions;

        // Inferir el layout por defecto según el nombre de la escena
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName.IndexOf("Title", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            sceneName.IndexOf("Menu", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            manager.defaultLayout = ControllerLayoutMode.Menu;
        }
        else if (sceneName.IndexOf("Lobby", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 sceneName.IndexOf("Hub", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            manager.defaultLayout = ControllerLayoutMode.Lobby;
        }
        else
        {
            manager.defaultLayout = ControllerLayoutMode.Match;
        }

        VRHandsSetupEditor.EnsureHandPrefabVariantsExist();
        VRHandsSetupEditor.SetupHandsInCurrentScene(showDialog: false);

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = manager.gameObject;

        Debug.Log($"[XRLayoutSetupEditor] XRControllerLayoutManager configurado en '{manager.gameObject.name}' con defaultLayout = {manager.defaultLayout}. Recuerda guardar la escena (Ctrl+S).");
    }

    [MenuItem("Tools/PingPing PongPong/Crear 6 Palas del GDD (PaddleStats)")]
    public static void CreateGddPaddleStats()
    {
        const string folder = "Assets/06 Data/Paddles";
        if (!AssetDatabase.IsValidFolder("Assets/06 Data"))
            AssetDatabase.CreateFolder("Assets", "06 Data");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/06 Data", "Paddles");

        CreateOrUpdatePaddle(folder, "Pala_01_Madera", "Pala Inicial de Madera", "Posee un balance neutro.", 1.00f, 1.00f, 1.00f, 1.00f, SpecialPowerType.FastballSmash);
        CreateOrUpdatePaddle(folder, "Pala_02_Hex", "Pala Hex", "Más lenta, mejor ventana de timing, slice leve.", 0.90f, 1.30f, 0.85f, 0.95f, SpecialPowerType.BodhisattvaDefensivo);
        CreateOrUpdatePaddle(folder, "Pala_03_Rayo", "Pala Rayo", "Más veloz, timing más preciso, slice mínimo.", 1.22f, 0.80f, 0.50f, 1.25f, SpecialPowerType.FastballSmash);
        CreateOrUpdatePaddle(folder, "Pala_04_Curva", "Pala Curva", "Balance neutro/lento, slices con más giro.", 0.94f, 1.05f, 1.55f, 0.95f, SpecialPowerType.CorteHuracan);
        CreateOrUpdatePaddle(folder, "Pala_05_Plateada", "Pala Plateada", "Todo bueno, no excelente.", 1.12f, 1.15f, 1.15f, 1.12f, SpecialPowerType.SeismicPunch);
        CreateOrUpdatePaddle(folder, "Pala_06_Dorada", "Pala Dorada", "(Tarde en el juego) Todo excelente.", 1.28f, 1.30f, 1.45f, 1.30f, SpecialPowerType.FastballSmash);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[XRLayoutSetupEditor] Se crearon/actualizaron las 6 palas del GDD en {folder}.");
    }

    private static void CreateOrUpdatePaddle(
        string folder,
        string fileName,
        string displayName,
        string description,
        float speed,
        float timingWindow,
        float sliceCurve,
        float smashPower,
        SpecialPowerType power)
    {
        string path = $"{folder}/{fileName}.asset";
        PaddleStats asset = AssetDatabase.LoadAssetAtPath<PaddleStats>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<PaddleStats>();
            AssetDatabase.CreateAsset(asset, path);
        }

        asset.paddleName = displayName;
        asset.description = description;
        asset.speedMultiplier = speed;
        asset.timingWindowMultiplier = timingWindow;
        asset.sliceCurveMultiplier = sliceCurve;
        asset.smashPowerMultiplier = smashPower;
        asset.specialPower = power;
        EditorUtility.SetDirty(asset);
    }
}
#endif
