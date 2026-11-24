#if UNITY_EDITOR
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Herramienta del Editor para crear/actualizar los Prefab Variants de OpenXRCustomHandPrefab
/// y configurar las manos VR en la escena activa o en las 4 escenas objetivo sin alterar los Transforms de las palas.
/// </summary>
public static class VRHandsSetupEditor
{
    public const string LeftHandVariantPath = "Assets/Resources/Hands/VRPlayerHand_L.prefab";
    public const string RightHandVariantPath = "Assets/Resources/Hands/VRPlayerHand_R.prefab";

    private const string SourceLeftPrefabGuid = "deccf1bd75222ab419fef0130b2a4d9e";
    private const string SourceRightPrefabGuid = "f4f1f0ff232ea644c8a980e61825dd44";
    private const string LeftAnimatorControllerGuid = "fbfc3d33a11c4074baa5d74bc735afe4";
    private const string RightAnimatorControllerGuid = "c3778580bf1578547a7d153adf3829b0";

    private const string PingPongActionsPath = "Assets/05 Input/PingPongInputActions.inputactions";
    private const string XriDefaultActionsPath = "Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/XRI Default Input Actions.inputactions";

    private static readonly string[] TargetScenes =
    {
        "Assets/03 Scenes/Scene_Title.unity",
        "Assets/03 Scenes/Lobby.unity",
        "Assets/03 Scenes/Practice_Match.unity",
        "Assets/03 Scenes/Practice_Bot_Scene.unity"
    };

    [MenuItem("Tools/PingPing PongPong/Configurar Manos VR en Escena Actual")]
    public static void SetupHandsInCurrentSceneMenu()
    {
        EnsureHandPrefabVariantsExist();
        SetupHandsInCurrentScene(showDialog: true);
    }

    [MenuItem("Tools/PingPing PongPong/Configurar Manos VR (4 Escenas: Title, Lobby, Partidos)")]
    public static void SetupHandsAcrossAllFourScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EnsureHandPrefabVariantsExist();

        string originalScenePath = SceneManager.GetActiveScene().path;
        int configuredCount = 0;

        foreach (string scenePath in TargetScenes)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogWarning($"[VRHandsSetupEditor] Escena no encontrada: {scenePath}");
                continue;
            }

            Scene opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (SetupHandsInCurrentScene(showDialog: false))
            {
                EditorSceneManager.SaveScene(opened);
                configuredCount++;
            }
        }

        if (!string.IsNullOrEmpty(originalScenePath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(originalScenePath) != null)
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }

        EditorUtility.DisplayDialog(
            "Manos VR Configuradas",
            $"Se configuraron correctamente las manos VR (OpenXRCustomHandPrefab) en {configuredCount} escenas sin modificar las transformaciones de las palas.",
            "Excelente");
    }

    public static void EnsureHandPrefabVariantsExist()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Hands"))
            AssetDatabase.CreateFolder("Assets/Resources", "Hands");

        EnsureSingleHandVariant(
            LeftHandVariantPath,
            SourceLeftPrefabGuid,
            LeftAnimatorControllerGuid,
            isLeft: true,
            new Vector3(-0.02f, -0.015f, -0.08f),
            new Vector3(0f, 0f, 90f));

        EnsureSingleHandVariant(
            RightHandVariantPath,
            SourceRightPrefabGuid,
            RightAnimatorControllerGuid,
            isLeft: false,
            new Vector3(0.02f, -0.015f, -0.08f),
            new Vector3(0f, 0f, -90f));

        AssetDatabase.SaveAssets();
    }

    private static void EnsureSingleHandVariant(
        string variantPath,
        string sourceGuid,
        string controllerGuid,
        bool isLeft,
        Vector3 normalLocalPos,
        Vector3 normalLocalEuler)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
        if (existing != null && existing.GetComponent<VRHandController>() != null)
        {
            return;
        }

        string sourcePath = AssetDatabase.GUIDToAssetPath(sourceGuid);
        GameObject sourcePrefab = !string.IsNullOrEmpty(sourcePath)
            ? AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath)
            : null;
        if (sourcePrefab == null)
        {
            Debug.LogError($"[VRHandsSetupEditor] No se encontró OpenXRCustomHandPrefab con GUID {sourceGuid}.");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
        try
        {
            instance.name = isLeft ? "VRPlayerHand_L" : "VRPlayerHand_R";

            MonoBehaviour[] mbs = instance.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour mb in mbs)
            {
                if (mb == null) continue;
                string tn = mb.GetType().Name;
                if (tn == "OVRHand" || tn == "OVRSkeleton" || tn == "OVRCustomSkeleton")
                {
                    mb.enabled = false;
                }
            }

            SkinnedMeshRenderer[] smrs = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer smr in smrs)
            {
                smr.enabled = true;
                smr.updateWhenOffscreen = true;
            }

            Transform modelRoot = instance.transform.Find(isLeft ? "OpenXRLeftHand" : "OpenXRRightHand");
            if (modelRoot == null) modelRoot = instance.transform;

            Animator anim = modelRoot.GetComponent<Animator>();
            if (anim == null) anim = modelRoot.gameObject.AddComponent<Animator>();

            string ctrlPath = AssetDatabase.GUIDToAssetPath(controllerGuid);
            if (!string.IsNullOrEmpty(ctrlPath))
            {
                RuntimeAnimatorController rac = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ctrlPath);
                if (rac != null) anim.runtimeAnimatorController = rac;
            }
            anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.applyRootMotion = false;

            Transform attach = modelRoot.Find("BallAttachPoint");
            if (attach == null)
            {
                GameObject attachGo = new GameObject("BallAttachPoint");
                attach = attachGo.transform;
                attach.SetParent(modelRoot, false);
            }
            attach.localPosition = new Vector3(isLeft ? 0.0185f : -0.0185f, -0.068f, 0.12f);
            attach.localRotation = Quaternion.identity;

            VRHandController handCtrl = instance.GetComponent<VRHandController>();
            if (handCtrl == null) handCtrl = instance.AddComponent<VRHandController>();
            handCtrl.isLeftHand = isLeft;
            handCtrl.handModelRoot = modelRoot;
            handCtrl.handAnimator = anim;
            handCtrl.ballAttachPoint = attach;
            handCtrl.normalLocalPosition = normalLocalPos;
            handCtrl.normalLocalEuler = normalLocalEuler;
            handCtrl.ApplyNormalControllerRootPose();

            PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    public static bool SetupHandsInCurrentScene(bool showDialog)
    {
        XROrigin xrOrigin = Object.FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null)
        {
            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "XR Origin no encontrado",
                    "No se encontró un XR Origin en la escena actual.",
                    "Entendido");
            }
            return false;
        }

        GameObject prefabL = AssetDatabase.LoadAssetAtPath<GameObject>(LeftHandVariantPath);
        GameObject prefabR = AssetDatabase.LoadAssetAtPath<GameObject>(RightHandVariantPath);
        if (prefabL == null || prefabR == null)
        {
            Debug.LogError("[VRHandsSetupEditor] Faltan los Prefab Variants VRPlayerHand_L / VRPlayerHand_R.");
            return false;
        }

        Transform leftController = XRControllerLayoutManager.FindControllerTransform(xrOrigin.transform, true);
        Transform rightController = XRControllerLayoutManager.FindControllerTransform(xrOrigin.transform, false);
        if (leftController == null || rightController == null)
        {
            Debug.LogError("[VRHandsSetupEditor] No se encontraron 'Left Controller' o 'Right Controller' dentro del XR Origin.");
            return false;
        }

        if (!leftController.gameObject.activeSelf)
        {
            Undo.RecordObject(leftController.gameObject, "Activar Left Controller");
            leftController.gameObject.SetActive(true);
            EditorUtility.SetDirty(leftController.gameObject);
        }
        if (!rightController.gameObject.activeSelf)
        {
            Undo.RecordObject(rightController.gameObject, "Activar Right Controller");
            rightController.gameObject.SetActive(true);
            EditorUtility.SetDirty(rightController.gameObject);
        }

        DisableModalityManagerInHierarchy(xrOrigin.transform);

        VRHandController leftHand = GetOrInstantiateHand(leftController, prefabL, true);
        VRHandController rightHand = GetOrInstantiateHand(rightController, prefabR, false);

        HideControllerVisualsInHierarchy(xrOrigin.transform);

        XRControllerLayoutManager manager = Object.FindFirstObjectByType<XRControllerLayoutManager>();
        if (manager == null)
        {
            manager = Undo.AddComponent<XRControllerLayoutManager>(xrOrigin.gameObject);
            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName.IndexOf("Title", System.StringComparison.OrdinalIgnoreCase) >= 0)
                manager.defaultLayout = ControllerLayoutMode.Menu;
            else if (sceneName.IndexOf("Lobby", System.StringComparison.OrdinalIgnoreCase) >= 0)
                manager.defaultLayout = ControllerLayoutMode.Lobby;
            else
                manager.defaultLayout = ControllerLayoutMode.Match;
        }
        else
        {
            Undo.RecordObject(manager, "Configurar Manos VR en XRControllerLayoutManager");
        }

        manager.xrOrigin = xrOrigin;
        manager.leftHandVisual = leftHand;
        manager.rightHandVisual = rightHand;
        manager.leftHandPrefab = prefabL;
        manager.rightHandPrefab = prefabR;

        if (manager.pingPongInputActions == null)
            manager.pingPongInputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(PingPongActionsPath);
        if (manager.xriDefaultInputActions == null)
            manager.xriDefaultInputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(XriDefaultActionsPath);

        manager.AutoDetectReferences();
        EditorUtility.SetDirty(manager);

        BallSummoner summoner = Object.FindFirstObjectByType<BallSummoner>();
        if (summoner != null)
        {
            Undo.RecordObject(summoner, "Vincular BallAttachPoint en BallSummoner");
            if (leftHand != null) summoner.leftBallAttachPoint = leftHand.BallAttachPoint;
            if (rightHand != null) summoner.rightBallAttachPoint = rightHand.BallAttachPoint;
            EditorUtility.SetDirty(summoner);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        if (showDialog)
        {
            EditorUtility.DisplayDialog(
                "Manos VR Listas",
                $"Manos VR configuradas en '{SceneManager.GetActiveScene().name}'. Recuerda guardar la escena (Ctrl+S).",
                "Entendido");
        }

        return true;
    }

    private static VRHandController GetOrInstantiateHand(Transform controllerParent, GameObject handPrefab, bool isLeft)
    {
        VRHandController[] existing = controllerParent.GetComponentsInChildren<VRHandController>(true);
        foreach (VRHandController h in existing)
        {
            if (h != null && h.isLeftHand == isLeft)
            {
                h.gameObject.SetActive(true);
                h.InitializeHandRig();
                h.ApplyNormalControllerRootPose();
                EditorUtility.SetDirty(h);
                return h;
            }
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, controllerParent);
        inst.name = isLeft ? "VRPlayerHand_L" : "VRPlayerHand_R";
        Undo.RegisterCreatedObjectUndo(inst, "Instanciar Mano VR");

        VRHandController ctrl = inst.GetComponent<VRHandController>();
        if (ctrl != null)
        {
            ctrl.InitializeHandRig();
            ctrl.ApplyNormalControllerRootPose();
            EditorUtility.SetDirty(ctrl);
        }
        return ctrl;
    }

    private static void DisableModalityManagerInHierarchy(Transform root)
    {
        MonoBehaviour[] mbs = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour mb in mbs)
        {
            if (mb != null && mb.GetType().Name == "XRInputModalityManager" && mb.enabled)
            {
                Undo.RecordObject(mb, "Desactivar XRInputModalityManager");
                mb.enabled = false;
                EditorUtility.SetDirty(mb);
            }
        }
    }

    private static void HideControllerVisualsInHierarchy(Transform root)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform tr in all)
        {
            if (tr == null) continue;
            string n = tr.name;
            if (n == "Left Controller Visual" ||
                n == "Right Controller Visual" ||
                n == "UniversalController" ||
                n == "Universal Controller Materials Variant")
            {
                if (tr.gameObject.activeSelf)
                {
                    Undo.RecordObject(tr.gameObject, "Ocultar Control 3D");
                    tr.gameObject.SetActive(false);
                    EditorUtility.SetDirty(tr.gameObject);
                }
            }
        }
    }
}
#endif
