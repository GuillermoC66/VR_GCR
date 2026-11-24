using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

public enum HandVisualMode
{
    NormalControllerInput = 0,
    PaddleShakehandGrip = 1,
    MatchOffHandBallGrab = 2
}

/// <summary>
/// Controla una mano virtual basada en OpenXRCustomHandPrefab usando únicamente entradas de control VR.
/// Mantiene siempre visible tanto la mano virtual como el modelo visual del controlador (Left/Right Controller Visual).
/// </summary>
[DefaultExecutionOrder(-250)]
public class VRHandController : MonoBehaviour
{
    private static readonly int AnimParamFlex = Animator.StringToHash("Flex");
    private static readonly int AnimParamPinch = Animator.StringToHash("Pinch");
    private static readonly int AnimParamIndexSlide = Animator.StringToHash("IndexSlide");

    private const int LayerThumb = 1;
    private const int LayerPoint = 2;

    // Coordenadas locales de referencia en la malla 20260630 pala.fbx
    private static readonly Vector3 PaddleMeshButt = new Vector3(-0.0111f, 0f, -0.0048f);
    private static readonly Vector3 PaddleMeshTip = new Vector3(0.1588f, 0f, -0.0048f);
    private static readonly Vector3 PaddleMeshOriginMidZ = new Vector3(0f, 0f, -0.0048f);
    private static readonly Vector3 PaddleMeshEdgePosY = new Vector3(0.06f, 0.051f, -0.0048f);
    private static readonly Vector3 PaddleMeshEdgeNegY = new Vector3(0.06f, -0.051f, -0.0048f);
    private static readonly Vector3 PaddleMeshFacePosZ = new Vector3(0.06f, 0f, 0.010f);
    private static readonly Vector3 PaddleMeshFaceNegZ = new Vector3(0.06f, 0f, -0.020f);

    [Header("Identidad y Referencias")]
    [Tooltip("True para OpenXRCustomHandPrefab_L, False para OpenXRCustomHandPrefab_R.")]
    public bool isLeftHand = true;

    [Tooltip("Raíz del esqueleto/malla (OpenXRLeftHand u OpenXRRightHand, padre directo de XRHand_Wrist).")]
    public Transform handModelRoot;

    [Tooltip("Objeto visual del controlador (Left Controller Visual / Right Controller Visual) para mantenerlo siempre visible.")]
    public Transform controllerVisualRoot;

    [Tooltip("Animator con OpenXR_HandController_Left/Right en OpenXRLeftHand/OpenXRRightHand.")]
    public Animator handAnimator;

    [Tooltip("Punto calibrado entre pulgar, índice y medio donde aparece/se sujeta la pelota.")]
    public Transform ballAttachPoint;

    [Header("Modo Actual")]
    [SerializeField]
    private HandVisualMode currentMode = HandVisualMode.NormalControllerInput;

    [Tooltip("Transform de la pala activa cuando el modo es PaddleShakehandGrip (no se modifica).")]
    public Transform targetPaddle;

    [Header("Offset Normal respecto al Control XR")]
    [Tooltip("Posición local de la muñeca respecto al Left/Right Controller en modo normal y saque.")]
    public Vector3 normalLocalPosition = new Vector3(-0.020f, -0.015f, -0.080f);

    [Tooltip("Rotación Euler local de la muñeca respecto al Left/Right Controller (Izq: 0,0,90 | Der: 0,0,-90).")]
    public Vector3 normalLocalEuler = new Vector3(0f, 0f, 90f);

    [Header("Calibración Agarre Shakehand de Pala")]
    [Tooltip("Ángulo diagonal (grados) del eje de los nudillos respecto al ancho de la pala.")]
    public float paddleGripYawDegrees = 22f;

    [Tooltip("Punto objetivo en el mango de la pala (L = largo desde base, W = ancho, N = normal de goma) en metros.")]
    public Vector3 paddleHandleTargetLWN = new Vector3(0.030f, -0.002f, 0.000f);

    [Tooltip("Centro del túnel de agarre en espacio de la mano (X se espejea automáticamente según mano).")]
    public Vector3 handGripCenterLocal = new Vector3(0.002f, -0.025f, 0.084f);

    [Header("Calibración Agarre de Pelota (Saque 3 Dedos)")]
    [Tooltip("Posición local del centro de la pelota (escala 0.04) en espacio de OpenXRLeftHand (X se espejea en derecha).")]
    public Vector3 ballPocketLocalOffset = new Vector3(0.0185f, -0.068f, 0.120f);

    [Tooltip("Velocidad de suavizado de animaciones de dedos (independiente de Time.timeScale).")]
    public float fingerSmoothSpeed = 16f;

    public HandVisualMode CurrentMode => currentMode;
    public Transform BallAttachPoint
    {
        get
        {
            EnsureBallAttachPoint();
            return ballAttachPoint;
        }
    }

    // Estado suavizado de parámetros
    private float smoothFlex;
    private float smoothPinch;
    private float smoothPoint;
    private float smoothThumb;
    private float smoothBallGrab;

    // Huesos cacheados para poses de precisión (shakehand y agarre de pelota de 3 dedos)
    private bool bonesCached;
    private Transform wristBone;
    private Transform thumbMetacarpal, thumbProximal, thumbDistal;
    private Transform indexProximal, indexIntermediate, indexDistal;
    private Transform middleProximal, middleIntermediate, middleDistal;
    private Transform ringProximal, ringIntermediate, ringDistal;
    private Transform littleMetacarpal, littleProximal, littleIntermediate, littleDistal;

    private SkinnedMeshRenderer[] handRenderers;
    private Renderer[] controllerRenderers;

    private void Awake()
    {
        InitializeHandRig();
    }

    private void OnEnable()
    {
        InitializeHandRig();
        EnsureRenderersVisible();
    }

    /// <summary>
    /// Configura referencias internas, desactiva componentes ópticos OVRHand/OVRSkeleton y asegura Animator en UnscaledTime.
    /// </summary>
    public void InitializeHandRig()
    {
        DisableOpticalTrackingComponents();

        if (handModelRoot == null)
        {
            Transform found = transform.Find(isLeftHand ? "OpenXRLeftHand" : "OpenXRRightHand");
            if (found == null)
            {
                found = FindDeepChild(transform, "XRHand_Wrist")?.parent;
            }
            handModelRoot = found != null ? found : transform;
        }

        // Localizar automáticamente Left Controller Visual / Right Controller Visual
        if (controllerVisualRoot == null)
        {
            string expectedVisualName = isLeftHand ? "Left Controller Visual" : "Right Controller Visual";
            Transform searchScope = transform.parent != null ? transform.parent : transform.root;

            controllerVisualRoot = FindDeepChild(searchScope, expectedVisualName);
            if (controllerVisualRoot == null)
            {
                controllerVisualRoot = FindDeepChild(searchScope, "Controller Visual");
            }
        }

        if (controllerVisualRoot != null)
        {
            controllerRenderers = controllerVisualRoot.GetComponentsInChildren<Renderer>(true);
        }

        if (handAnimator == null && handModelRoot != null)
        {
            handAnimator = handModelRoot.GetComponent<Animator>();
            if (handAnimator == null)
            {
                handAnimator = GetComponentInChildren<Animator>(true);
            }
        }

        if (handAnimator != null)
        {
            handAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            handAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        handRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        EnsureRenderersVisible();
        CacheBones();
        EnsureBallAttachPoint();
    }

    /// <summary>
    /// Cambia el modo visual de la mano y opcionalmente asigna la pala de referencia.
    /// </summary>
    public void SetMode(HandVisualMode mode, Transform paddleTransform = null)
    {
        currentMode = mode;
        if (paddleTransform != null || mode != HandVisualMode.PaddleShakehandGrip)
        {
            targetPaddle = paddleTransform;
        }

        if (currentMode != HandVisualMode.PaddleShakehandGrip)
        {
            ApplyNormalControllerRootPose();
        }
    }

    private void Update()
    {
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        float step = 1f - Mathf.Exp(-fingerSmoothSpeed * dt);

        ReadControllerInputs(
            out float grip,
            out float trigger,
            out bool anyThumbTouchOrPress);

        switch (currentMode)
        {
            case HandVisualMode.NormalControllerInput:
            {
                float targetFlex = grip;
                float targetPinch = trigger;
                float targetPoint = (grip > 0.45f && trigger < 0.25f)
                    ? Mathf.Clamp01((grip - 0.35f) / 0.40f) * Mathf.Clamp01(1f - trigger * 4f)
                    : 0f;
                float targetThumb = anyThumbTouchOrPress ? 0f : 0.45f;

                smoothFlex = Mathf.Lerp(smoothFlex, targetFlex, step);
                smoothPinch = Mathf.Lerp(smoothPinch, targetPinch, step);
                smoothPoint = Mathf.Lerp(smoothPoint, targetPoint, step);
                smoothThumb = Mathf.Lerp(smoothThumb, targetThumb, step);
                smoothBallGrab = Mathf.Lerp(smoothBallGrab, 0f, step);
                break;
            }

            case HandVisualMode.PaddleShakehandGrip:
            {
                smoothFlex = 1f;
                smoothPinch = 0f;
                smoothPoint = 0f;
                smoothThumb = 0f;
                smoothBallGrab = 0f;
                break;
            }

            case HandVisualMode.MatchOffHandBallGrab:
            {
                smoothFlex = 0f;
                smoothPinch = 0f;
                smoothPoint = 0f;
                smoothThumb = 0f;
                smoothBallGrab = Mathf.Lerp(smoothBallGrab, grip, step);
                break;
            }
        }

        DriveAnimatorParameters();
    }

    private void LateUpdate()
    {
        EnsureRenderersVisible();
        UpdateBallAttachLocalPosition();

        bool hasValidAnimator = handAnimator != null && handAnimator.runtimeAnimatorController != null && handAnimator.isActiveAndEnabled;

        switch (currentMode)
        {
            case HandVisualMode.NormalControllerInput:
                ApplyNormalControllerRootPose();
                if (!hasValidAnimator)
                {
                    ApplyProceduralNormalFallback();
                }
                break;

            case HandVisualMode.PaddleShakehandGrip:
                AlignRootToPaddleHandle();
                if (!hasValidAnimator)
                {
                    ApplyFistBasePose();
                }
                ApplyShakehandFingerOverrides();
                break;

            case HandVisualMode.MatchOffHandBallGrab:
                ApplyNormalControllerRootPose();
                if (!hasValidAnimator)
                {
                    ApplyCapOpenPose();
                }
                if (smoothBallGrab > 0.001f || !hasValidAnimator)
                {
                    ApplyThreeFingerBallGrabPose(smoothBallGrab);
                }
                break;
        }
    }

    // =========================================================================
    // POSICIONAMIENTO DE RAÍZ (CONTROLADOR NORMAL VS MANGO DE PALA)
    // =========================================================================

    public void ApplyNormalControllerRootPose()
    {
        Vector3 expectedPos = new Vector3(
            isLeftHand ? -Mathf.Abs(normalLocalPosition.x) : Mathf.Abs(normalLocalPosition.x),
            normalLocalPosition.y,
            normalLocalPosition.z);

        Vector3 expectedEuler = new Vector3(
            normalLocalEuler.x,
            isLeftHand ? normalLocalEuler.y : -normalLocalEuler.y,
            isLeftHand ? Mathf.Abs(normalLocalEuler.z) : -Mathf.Abs(normalLocalEuler.z));

        transform.localPosition = expectedPos;
        transform.localRotation = Quaternion.Euler(expectedEuler);

        if (handModelRoot != null && handModelRoot != transform)
        {
            handModelRoot.localPosition = Vector3.zero;
            handModelRoot.localRotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// Posiciona y orienta la mano sobre el mango de la pala activa sin modificar las transformaciones de la pala.
    /// </summary>
    private void AlignRootToPaddleHandle()
    {
        if (targetPaddle == null)
        {
            ApplyNormalControllerRootPose();
            return;
        }

        Vector3 buttWorld = targetPaddle.TransformPoint(PaddleMeshButt);
        Vector3 tipWorld = targetPaddle.TransformPoint(PaddleMeshTip);
        Vector3 edgePosYWorld = targetPaddle.TransformPoint(PaddleMeshEdgePosY);
        Vector3 edgeNegYWorld = targetPaddle.TransformPoint(PaddleMeshEdgeNegY);
        Vector3 facePosZWorld = targetPaddle.TransformPoint(PaddleMeshFacePosZ);
        Vector3 faceNegZWorld = targetPaddle.TransformPoint(PaddleMeshFaceNegZ);

        Vector3 axisL = (tipWorld - buttWorld).normalized;
        Vector3 axisWidRaw = (edgePosYWorld - edgeNegYWorld).normalized;
        Vector3 axisW = isLeftHand ? -axisWidRaw : axisWidRaw;
        Vector3 axisN = (facePosZWorld - faceNegZWorld).normalized;

        if (axisL.sqrMagnitude < 0.001f || axisW.sqrMagnitude < 0.001f || axisN.sqrMagnitude < 0.001f)
        {
            return;
        }

        float yawRad = paddleGripYawDegrees * Mathf.Deg2Rad;
        Vector3 handForward = (Mathf.Sin(yawRad) * axisL + Mathf.Cos(yawRad) * axisW).normalized;
        Vector3 handUp = -axisN;

        Quaternion worldRot = Quaternion.LookRotation(handForward, handUp);

        Vector3 paddleBaseWorld = targetPaddle.TransformPoint(PaddleMeshOriginMidZ);
        Vector3 handleTargetWorld = paddleBaseWorld
            + axisL * paddleHandleTargetLWN.x
            + axisW * paddleHandleTargetLWN.y
            + axisN * paddleHandleTargetLWN.z;

        Vector3 gripLocal = new Vector3(
            isLeftHand ? Mathf.Abs(handGripCenterLocal.x) : -Mathf.Abs(handGripCenterLocal.x),
            handGripCenterLocal.y,
            handGripCenterLocal.z);

        Vector3 worldPos = handleTargetWorld - (worldRot * gripLocal);
        transform.SetPositionAndRotation(worldPos, worldRot);

        if (handModelRoot != null && handModelRoot != transform)
        {
            handModelRoot.localPosition = Vector3.zero;
            handModelRoot.localRotation = Quaternion.identity;
        }
    }

    // =========================================================================
    // POSES ÓSEAS ESPECÍFICAS (SHAKEHAND PADDLE GRIP & 3-FINGER BALL GRAB)
    // =========================================================================

    private void ApplyShakehandFingerOverrides()
    {
        if (!bonesCached) CacheBones();

        if (indexProximal != null) indexProximal.localRotation = HandEuler(0f, 12f, -8f);
        if (indexIntermediate != null) indexIntermediate.localRotation = HandEuler(3f, 0f, 0f);
        if (indexDistal != null) indexDistal.localRotation = HandEuler(2f, 0f, 0f);

        if (thumbMetacarpal != null) thumbMetacarpal.localRotation = HandEuler(26f, 34f, 295f);
        if (thumbProximal != null) thumbProximal.localRotation = HandEuler(24f, -4f, 10f);
        if (thumbDistal != null) thumbDistal.localRotation = HandEuler(20f, 0f, -6f);
    }

    private void ApplyThreeFingerBallGrabPose(float t)
    {
        if (!bonesCached) CacheBones();
        t = Mathf.Clamp01(t);

        BlendBone(thumbMetacarpal, 16.6f, 46.3f, 293.6f, 36.0f, 42.0f, 286.0f, t);
        BlendBone(thumbProximal, 33.1f, -2.6f, 8.8f, 31.0f, -2.0f, 9.0f, t);
        BlendBone(thumbDistal, 5.0f, 6.2f, -9.1f, 16.0f, 1.0f, -8.0f, t);

        BlendBone(indexProximal, 21.9f, -2.2f, -4.4f, 35.0f, -2.0f, -3.5f, t);
        BlendBone(indexIntermediate, 32.5f, 1.0f, 3.4f, 40.0f, 2.0f, 4.0f, t);
        BlendBone(indexDistal, 13.1f, -2.6f, 1.7f, 22.0f, -1.0f, 1.5f, t);

        BlendBone(middleProximal, 28.2f, -5.6f, 0.7f, 39.0f, -5.0f, -4.0f, t);
        BlendBone(middleIntermediate, 28.4f, 0.2f, 1.5f, 40.0f, 0.5f, 2.0f, t);
        BlendBone(middleDistal, 11.3f, 1.0f, 6.2f, 22.0f, 1.0f, 5.0f, t);

        BlendBone(ringProximal, 28.9f, -11.2f, 5.9f, 54.0f, -8.0f, 8.0f, t);
        BlendBone(ringIntermediate, 29.7f, 2.0f, 5.1f, 58.0f, 2.0f, 5.0f, t);
        BlendBone(ringDistal, 17.6f, 3.7f, 1.9f, 38.0f, 4.0f, 2.0f, t);

        if (littleMetacarpal != null) littleMetacarpal.localRotation = HandEuler(5.4f, -15.4f, 23.4f);
        BlendBone(littleProximal, 17.7f, -16.1f, -10.1f, 58.0f, -14.0f, -10.0f, t);
        BlendBone(littleIntermediate, 23.8f, -2.7f, 6.0f, 60.0f, -2.0f, 6.0f, t);
        BlendBone(littleDistal, 8.7f, 5.7f, 0.3f, 40.0f, 6.0f, 1.0f, t);
    }

    private void ApplyCapOpenPose()
    {
        ApplyThreeFingerBallGrabPose(0f);
    }

    private void ApplyFistBasePose()
    {
        if (!bonesCached) CacheBones();

        if (thumbMetacarpal != null) thumbMetacarpal.localRotation = HandEuler(21.8f, 37.8f, 286.5f);
        if (thumbProximal != null) thumbProximal.localRotation = HandEuler(35.5f, -2.2f, 8.9f);
        if (thumbDistal != null) thumbDistal.localRotation = HandEuler(45.5f, -0.6f, -8.6f);

        if (indexProximal != null) indexProximal.localRotation = HandEuler(65.6f, -14.9f, -12.7f);
        if (indexIntermediate != null) indexIntermediate.localRotation = HandEuler(76.0f, 167.8f, 168.2f);
        if (indexDistal != null) indexDistal.localRotation = HandEuler(64.0f, -0.7f, 1.8f);

        if (middleProximal != null) middleProximal.localRotation = HandEuler(79.0f, -9.4f, 1.2f);
        if (middleIntermediate != null) middleIntermediate.localRotation = HandEuler(82.3f, 168.8f, 169.4f);
        if (middleDistal != null) middleDistal.localRotation = HandEuler(66.1f, 20.3f, 25.8f);

        if (ringProximal != null) ringProximal.localRotation = HandEuler(83.6f, 35.2f, 49.5f);
        if (ringIntermediate != null) ringIntermediate.localRotation = HandEuler(82.5f, 135.5f, 137.0f);
        if (ringDistal != null) ringDistal.localRotation = HandEuler(67.4f, 11.7f, 12.0f);

        if (littleMetacarpal != null) littleMetacarpal.localRotation = HandEuler(5.4f, -15.4f, 23.4f);
        if (littleProximal != null) littleProximal.localRotation = HandEuler(81.0f, -56.8f, -59.8f);
        if (littleIntermediate != null) littleIntermediate.localRotation = HandEuler(82.9f, 73.0f, 80.5f);
        if (littleDistal != null) littleDistal.localRotation = HandEuler(60.9f, 8.3f, 4.5f);
    }

    private void ApplyProceduralNormalFallback()
    {
        if (!bonesCached) CacheBones();

        float g = Mathf.Clamp01(smoothFlex);
        float p = Mathf.Clamp01(smoothPinch);
        float idx = smoothPoint > 0.01f ? Mathf.Lerp(Mathf.Max(g, p), 0f, smoothPoint) : Mathf.Max(g, p);

        BlendBone(thumbMetacarpal, 16.6f, 46.3f, 293.6f, 32.0f, 39.0f, 286.0f, Mathf.Max(g, p));
        BlendBone(thumbProximal, 33.1f, -2.6f, 8.8f, 35.8f, -2.1f, 8.9f, Mathf.Max(g, p));
        BlendBone(thumbDistal, 5.0f, 6.2f, -9.1f, 40.0f, 0.2f, -8.4f, Mathf.Max(g, p));

        BlendBone(indexProximal, 21.9f, -2.2f, -4.4f, 62.0f, -8.0f, -10.0f, idx);
        BlendBone(indexIntermediate, 32.5f, 1.0f, 3.4f, 72.0f, 4.0f, 6.0f, idx);
        BlendBone(indexDistal, 13.1f, -2.6f, 1.7f, 52.0f, -1.5f, 1.6f, idx);

        BlendBone(middleProximal, 28.2f, -5.6f, 0.7f, 79.0f, -9.4f, 1.2f, g);
        BlendBone(middleIntermediate, 28.4f, 0.2f, 1.5f, 80.0f, 2.0f, 3.0f, g);
        BlendBone(middleDistal, 11.3f, 1.0f, 6.2f, 62.0f, 6.0f, 10.0f, g);

        BlendBone(ringProximal, 28.9f, -11.2f, 5.9f, 80.0f, -6.0f, 8.0f, g);
        BlendBone(ringIntermediate, 29.7f, 2.0f, 5.1f, 80.0f, 4.0f, 6.0f, g);
        BlendBone(ringDistal, 17.6f, 3.7f, 1.9f, 62.0f, 6.0f, 4.0f, g);

        if (littleMetacarpal != null) littleMetacarpal.localRotation = HandEuler(5.4f, -15.4f, 23.4f);
        BlendBone(littleProximal, 17.7f, -16.1f, -10.1f, 78.0f, -18.0f, -12.0f, g);
        BlendBone(littleIntermediate, 23.8f, -2.7f, 6.0f, 80.0f, 2.0f, 8.0f, g);
        BlendBone(littleDistal, 8.7f, 5.7f, 0.3f, 58.0f, 7.0f, 3.0f, g);
    }

    private void BlendBone(Transform bone, float x0, float y0, float z0, float x1, float y1, float z1, float t)
    {
        if (bone == null) return;
        bone.localRotation = Quaternion.Slerp(HandEuler(x0, y0, z0), HandEuler(x1, y1, z1), t);
    }

    private Quaternion HandEuler(float x, float y, float z)
    {
        return isLeftHand ? Quaternion.Euler(x, y, z) : Quaternion.Euler(x, -y, -z);
    }

    // =========================================================================
    // LECTURA DE INPUTS DE CONTROLADOR Y ANIMATOR
    // =========================================================================

    private void DriveAnimatorParameters()
    {
        if (handAnimator == null || handAnimator.runtimeAnimatorController == null || !handAnimator.isActiveAndEnabled)
        {
            return;
        }

        handAnimator.SetFloat(AnimParamFlex, smoothFlex);
        handAnimator.SetFloat(AnimParamPinch, smoothPinch);
        handAnimator.SetFloat(AnimParamIndexSlide, 0f);

        int layers = handAnimator.layerCount;
        if (layers > LayerThumb) handAnimator.SetLayerWeight(LayerThumb, smoothThumb);
        if (layers > LayerPoint) handAnimator.SetLayerWeight(LayerPoint, smoothPoint);
    }

    private void ReadControllerInputs(out float grip, out float trigger, out bool anyThumbActive)
    {
        grip = 0f;
        trigger = 0f;
        anyThumbActive = false;

        XRNode node = isLeftHand ? XRNode.LeftHand : XRNode.RightHand;
        UnityEngine.XR.InputDevice xrDevice = InputDevices.GetDeviceAtXRNode(node);
        if (xrDevice.isValid)
        {
            if (xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float xrGrip))
                grip = Mathf.Max(grip, xrGrip);
            if (xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool xrGripBtn) && xrGripBtn)
                grip = Mathf.Max(grip, 1f);

            if (xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float xrTrigger))
                trigger = Mathf.Max(trigger, xrTrigger);
            if (xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool xrTrigBtn) && xrTrigBtn)
                trigger = Mathf.Max(trigger, 1f);

            bool pBtn = xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool v1) && v1;
            bool sBtn = xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool v2) && v2;
            bool pTch = xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryTouch, out bool v3) && v3;
            bool sTch = xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryTouch, out bool v4) && v4;
            bool aTch = xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisTouch, out bool v5) && v5;
            bool aClk = xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out bool v6) && v6;
            bool stickMoved = xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 stick) && stick.sqrMagnitude > 0.04f;

            anyThumbActive |= pBtn || sBtn || pTch || sTch || aTch || aClk || stickMoved;
        }

        var sysController = InputSystem.GetDevice<UnityEngine.InputSystem.XR.XRController>(
            isLeftHand ? UnityEngine.InputSystem.CommonUsages.LeftHand : UnityEngine.InputSystem.CommonUsages.RightHand);

        if (sysController != null)
        {
            grip = Mathf.Max(grip, ReadAxisChild(sysController, "grip"));
            if (ReadButtonChild(sysController, "gripPressed")) grip = Mathf.Max(grip, 1f);

            trigger = Mathf.Max(trigger, ReadAxisChild(sysController, "trigger"));
            if (ReadButtonChild(sysController, "triggerPressed")) trigger = Mathf.Max(trigger, 1f);

            anyThumbActive |= ReadButtonChild(sysController, "primaryButton")
                           || ReadButtonChild(sysController, "secondaryButton")
                           || ReadButtonChild(sysController, "primaryTouched")
                           || ReadButtonChild(sysController, "secondaryTouched")
                           || ReadButtonChild(sysController, "thumbstickTouched")
                           || ReadButtonChild(sysController, "thumbstickClicked")
                           || ReadStickChild(sysController, "thumbstick").sqrMagnitude > 0.04f;
        }

        grip = Mathf.Clamp01(grip);
        trigger = Mathf.Clamp01(trigger);
    }

    private static float ReadAxisChild(InputControl parent, string childName)
    {
        InputControl ctrl = parent.TryGetChildControl(childName);
        return ctrl is InputControl<float> floatCtrl ? floatCtrl.ReadValue() : 0f;
    }

    private static bool ReadButtonChild(InputControl parent, string childName)
    {
        InputControl ctrl = parent.TryGetChildControl(childName);
        return ctrl is InputControl<float> floatCtrl && floatCtrl.ReadValue() > 0.5f;
    }

    private static Vector2 ReadStickChild(InputControl parent, string childName)
    {
        InputControl ctrl = parent.TryGetChildControl(childName);
        return ctrl is InputControl<Vector2> vecCtrl ? vecCtrl.ReadValue() : Vector2.zero;
    }

    // =========================================================================
    // INICIALIZACIÓN INTERNA DE HUESOS, PUNTO DE PELOTA Y VISIBILIDAD
    // =========================================================================

    private void DisableOpticalTrackingComponents()
    {
        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour mb in behaviours)
        {
            if (mb == null || mb == this) continue;
            string typeName = mb.GetType().Name;
            if (typeName == "OVRHand" ||
                typeName == "OVRSkeleton" ||
                typeName == "OVRCustomSkeleton" ||
                typeName == "OVRMesh" ||
                typeName == "OVRMeshRenderer")
            {
                if (mb.enabled) mb.enabled = false;
            }
        }
    }

    private void EnsureRenderersVisible()
    {
        // 1. Mantener siempre visible la mano virtual
        if (handModelRoot != null && !handModelRoot.gameObject.activeSelf)
        {
            handModelRoot.gameObject.SetActive(true);
        }

        if (handRenderers != null)
        {
            foreach (SkinnedMeshRenderer smr in handRenderers)
            {
                if (smr == null) continue;
                if (!smr.gameObject.activeSelf) smr.gameObject.SetActive(true);
                if (!smr.enabled) smr.enabled = true;
                if (!smr.updateWhenOffscreen) smr.updateWhenOffscreen = true;
            }
        }

        // 2. Mantener siempre visible el modelo visual del controlador (Left/Right Controller Visual)
        if (controllerVisualRoot != null)
        {
            if (!controllerVisualRoot.gameObject.activeSelf)
            {
                controllerVisualRoot.gameObject.SetActive(true);
            }

            if (controllerRenderers == null || controllerRenderers.Length == 0)
            {
                controllerRenderers = controllerVisualRoot.GetComponentsInChildren<Renderer>(true);
            }

            foreach (Renderer r in controllerRenderers)
            {
                if (r == null) continue;
                if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                if (!r.enabled) r.enabled = true;
            }
        }
    }

    private void EnsureBallAttachPoint()
    {
        Transform parentTarget = handModelRoot != null ? handModelRoot : transform;

        if (ballAttachPoint == null)
        {
            Transform existing = parentTarget.Find("BallAttachPoint");
            if (existing == null) existing = FindDeepChild(transform, "BallAttachPoint");

            if (existing != null)
            {
                ballAttachPoint = existing;
            }
            else if (Application.isPlaying)
            {
                GameObject attachGo = new GameObject("BallAttachPoint");
                ballAttachPoint = attachGo.transform;
                ballAttachPoint.SetParent(parentTarget, false);
            }
        }

        UpdateBallAttachLocalPosition();
    }

    private void UpdateBallAttachLocalPosition()
    {
        if (ballAttachPoint == null) return;
        ballAttachPoint.localPosition = new Vector3(
            isLeftHand ? Mathf.Abs(ballPocketLocalOffset.x) : -Mathf.Abs(ballPocketLocalOffset.x),
            ballPocketLocalOffset.y,
            ballPocketLocalOffset.z);
        ballAttachPoint.localRotation = Quaternion.identity;
    }

    private void CacheBones()
    {
        Transform searchRoot = handModelRoot != null ? handModelRoot : transform;
        wristBone = FindDeepChild(searchRoot, "XRHand_Wrist");
        if (wristBone == null)
        {
            bonesCached = false;
            return;
        }

        thumbMetacarpal = FindDeepChild(wristBone, "XRHand_ThumbMetacarpal");
        thumbProximal = FindDeepChild(wristBone, "XRHand_ThumbProximal");
        thumbDistal = FindDeepChild(wristBone, "XRHand_ThumbDistal");

        indexProximal = FindDeepChild(wristBone, "XRHand_IndexProximal");
        indexIntermediate = FindDeepChild(wristBone, "XRHand_IndexIntermediate");
        indexDistal = FindDeepChild(wristBone, "XRHand_IndexDistal");

        middleProximal = FindDeepChild(wristBone, "XRHand_MiddleProximal");
        middleIntermediate = FindDeepChild(wristBone, "XRHand_MiddleIntermediate");
        middleDistal = FindDeepChild(wristBone, "XRHand_MiddleDistal");

        ringProximal = FindDeepChild(wristBone, "XRHand_RingProximal");
        ringIntermediate = FindDeepChild(wristBone, "XRHand_RingIntermediate");
        ringDistal = FindDeepChild(wristBone, "XRHand_RingDistal");

        littleMetacarpal = FindDeepChild(wristBone, "XRHand_LittleMetacarpal");
        littleProximal = FindDeepChild(wristBone, "XRHand_LittleProximal");
        littleIntermediate = FindDeepChild(wristBone, "XRHand_LittleIntermediate");
        littleDistal = FindDeepChild(wristBone, "XRHand_LittleDistal");

        bonesCached = true;
    }

    private static Transform FindDeepChild(Transform parent, string exactName)
    {
        if (parent == null) return null;
        if (parent.name == exactName) return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeepChild(parent.GetChild(i), exactName);
            if (found != null) return found;
        }
        return null;
    }
}