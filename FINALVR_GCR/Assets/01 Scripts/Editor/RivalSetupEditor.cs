#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

public static class RivalSetupEditor
{
    private const string DataRootFolder = "Assets/06 Data";
    private const string RivalsFolder = "Assets/06 Data/Rivals";
    private const string SampleScenePath = "Assets/03 Scenes/SampleScene.unity";

    private const string EasyAssetPath = RivalsFolder + "/Difficulty_Easy.asset";
    private const string MediumAssetPath = RivalsFolder + "/Difficulty_Medium.asset";
    private const string HardAssetPath = RivalsFolder + "/Difficulty_Hard.asset";
    private const string KaiAssetPath = RivalsFolder + "/Rival_EntrenadorKai.asset";

    private const string DifficultyBarName = "DifficultySelectorBar";
    private const string RivalHudPanelName = "RivalHudPanel";
    private const string MatchEndPanelName = "MatchEndPanel";

    [MenuItem("PingPing PongPong/Rivals/Create Default Rival Assets")]
    public static void CreateDefaultRivalAssetsMenu()
    {
        RivalProfile kai = CreateOrUpdateDefaultRivalAssets();
        Selection.activeObject = kai;
        EditorGUIUtility.PingObject(kai);
        Debug.Log($"[RivalSetupEditor] Assets de rival creados/actualizados en '{RivalsFolder}' (Easy, Medium, Hard y Rival_EntrenadorKai).");
    }

    [MenuItem("PingPing PongPong/Rivals/Setup Rival System in SampleScene")]
    public static void SetupRivalSystemInSampleScene()
    {
        RivalProfile kaiProfile = CreateOrUpdateDefaultRivalAssets();

        Scene activeScene = EnsureTargetSceneOpen();
        if (!activeScene.IsValid())
        {
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup Rival System in SampleScene");

        MatchReferee referee = Object.FindFirstObjectByType<MatchReferee>(FindObjectsInactive.Include);
        MenuManager menuManager = Object.FindFirstObjectByType<MenuManager>(FindObjectsInactive.Include);
        SimplePingPongAI legacyAI = Object.FindFirstObjectByType<SimplePingPongAI>(FindObjectsInactive.Include);
        RivalAIController rivalAI = Object.FindFirstObjectByType<RivalAIController>(FindObjectsInactive.Include);

        // 1. Asegurar MatchReferee habilitado
        if (referee != null && !referee.enabled)
        {
            Undo.RecordObject(referee, "Enable MatchReferee");
            referee.enabled = true;
            EditorUtility.SetDirty(referee);
        }

        // 2. Migrar SimplePingPongAI -> RivalAIController + RivalVisualController
        GameObject rivalHost = ResolveRivalHost(rivalAI, legacyAI);
        if (rivalAI == null)
        {
            rivalAI = rivalHost.GetComponent<RivalAIController>();
            if (rivalAI == null)
            {
                rivalAI = Undo.AddComponent<RivalAIController>(rivalHost);
            }
        }

        Undo.RecordObject(rivalAI, "Configure RivalAIController");

        if (legacyAI != null)
        {
            Undo.RecordObject(legacyAI, "Disable Legacy SimplePingPongAI");
            legacyAI.enabled = false;
            EditorUtility.SetDirty(legacyAI);

            if (rivalAI.ball == null) rivalAI.ball = legacyAI.ball;
            if (rivalAI.ballRb == null) rivalAI.ballRb = legacyAI.ballRb;
            if (rivalAI.aiPaddle == null) rivalAI.aiPaddle = legacyAI.aiPaddle;
            if (rivalAI.playerTableTarget == null) rivalAI.playerTableTarget = legacyAI.playerTableTarget;
            rivalAI.baselineX = legacyAI.baselineX;
            rivalAI.strikeDistance = legacyAI.strikeDistance;
            rivalAI.useDeterministicReturns = legacyAI.useDeterministicReturns;
            rivalAI.playerTableHalfWidth = legacyAI.playerTableHalfWidth;
            rivalAI.playerTableHalfLength = legacyAI.playerTableHalfLength;
            rivalAI.netHeightAboveTable = legacyAI.netHeightAboveTable;
        }

        if (rivalAI.ball == null)
        {
            BallController ballCtrl = Object.FindFirstObjectByType<BallController>(FindObjectsInactive.Include);
            if (ballCtrl != null)
            {
                rivalAI.ball = ballCtrl.transform;
                rivalAI.ballRb = ballCtrl.GetComponent<Rigidbody>();
            }
        }
        else if (rivalAI.ballRb == null)
        {
            rivalAI.ballRb = rivalAI.ball.GetComponent<Rigidbody>();
        }

        if (rivalAI.playerTableTarget == null)
        {
            rivalAI.playerTableTarget = FindObjectTransformByTagOrName("TablePlayer", "TablePlayer");
        }

        if (rivalAI.referee == null && referee != null)
        {
            rivalAI.referee = referee;
        }

        rivalAI.rivalProfile = kaiProfile;
        EditorUtility.SetDirty(rivalAI);

        // Desactivar cualquier PaddleFlickController residual en la pala del rival para evitar falsos golpes del jugador
        if (rivalAI.aiPaddle != null)
        {
            PaddleFlickController[] strayPlayerControllers = rivalAI.aiPaddle.GetComponentsInChildren<PaddleFlickController>(true);
            for (int i = 0; i < strayPlayerControllers.Length; i++)
            {
                PaddleFlickController stray = strayPlayerControllers[i];
                if (stray != null && stray.enabled)
                {
                    Undo.RecordObject(stray, "Disable Stray PaddleFlickController on AI Paddle");
                    stray.enabled = false;
                    EditorUtility.SetDirty(stray);
                }
            }
        }

        RivalVisualController rivalVisuals = rivalHost.GetComponent<RivalVisualController>();
        if (rivalVisuals == null)
        {
            rivalVisuals = Undo.AddComponent<RivalVisualController>(rivalHost);
        }

        Undo.RecordObject(rivalVisuals, "Configure RivalVisualController");
        rivalVisuals.rivalAI = rivalAI;
        rivalVisuals.BuildPlaceholderAvatarIfNeeded();
        rivalVisuals.ApplyTheme(kaiProfile, kaiProfile.GetProfile(rivalAI.currentDifficulty));
        EditorUtility.SetDirty(rivalVisuals);

        // 3. Vincular MatchReferee y MenuManager al rival
        if (referee != null)
        {
            Undo.RecordObject(referee, "Wire MatchReferee Rival References");
            referee.rivalAI = rivalAI;
            referee.rivalVisuals = rivalVisuals;
            EditorUtility.SetDirty(referee);
        }

        if (menuManager != null)
        {
            Undo.RecordObject(menuManager, "Wire MenuManager Rival References");
            menuManager.rivalAI = rivalAI;
            menuManager.matchReferee = referee;
            EditorUtility.SetDirty(menuManager);
        }

        // 4. Construir/actualizar UI de manera idempotente
        TMP_FontAsset sharedFont = ResolveSharedTmpFont(referee, menuManager);

        if (menuManager != null && menuManager.mainMenuPanel != null)
        {
            SetupDifficultySelectorUI(menuManager, kaiProfile, sharedFont);
        }
        else
        {
            Debug.LogWarning("[RivalSetupEditor] No se encontró MenuManager.mainMenuPanel; se omitió el selector de dificultad en el menú.");
        }

        Canvas scoreboardCanvas = ResolveScoreboardCanvas(referee);
        if (referee != null && scoreboardCanvas != null)
        {
            SetupRivalHudUI(referee, scoreboardCanvas.transform, kaiProfile, rivalAI.currentDifficulty, sharedFont);
            SetupMatchEndPanelUI(referee, menuManager, scoreboardCanvas, sharedFont);
        }
        else
        {
            Debug.LogWarning("[RivalSetupEditor] No se encontró MatchReferee o Canvas de marcador; se omitieron Rival HUD y MatchEndPanel.");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = rivalHost;

        Debug.Log($"[RivalSetupEditor] Sistema de Rivales configurado en '{activeScene.name}' (Rival: {kaiProfile.rivalName}). Recuerda guardar la escena (Ctrl+S).");
    }

    // =========================================================================
    // SCRIPTABLEOBJECT ASSETS
    // =========================================================================

    public static RivalProfile CreateOrUpdateDefaultRivalAssets()
    {
        EnsureFolderExists("Assets", "06 Data");
        EnsureFolderExists(DataRootFolder, "Rivals");

        RivalDifficultyProfile easy = CreateOrUpdateDifficultyProfile(EasyAssetPath, RivalDifficulty.Easy);
        RivalDifficultyProfile medium = CreateOrUpdateDifficultyProfile(MediumAssetPath, RivalDifficulty.Medium);
        RivalDifficultyProfile hard = CreateOrUpdateDifficultyProfile(HardAssetPath, RivalDifficulty.Hard);

        RivalProfile kai = AssetDatabase.LoadAssetAtPath<RivalProfile>(KaiAssetPath);
        if (kai == null)
        {
            kai = ScriptableObject.CreateInstance<RivalProfile>();
            AssetDatabase.CreateAsset(kai, KaiAssetPath);
        }

        kai.rivalName = "Entrenador Kai";
        kai.rivalTitle = "Rival Equilibrado";
        kai.description = "Un maestro versátil que adapta su ritmo, colocación y poder especial para poner a prueba tus reflejos.";
        kai.specialPower = SpecialPowerType.FastballSmash;
        kai.primaryColor = new Color(0.18f, 0.45f, 0.85f, 1f);
        kai.secondaryColor = new Color(0.95f, 0.85f, 0.25f, 1f);
        kai.speedMultiplier = 1f;
        kai.powerCurveMultiplier = 1f;
        kai.aggressionMultiplier = 1f;
        kai.easyProfile = easy;
        kai.mediumProfile = medium;
        kai.hardProfile = hard;
        EditorUtility.SetDirty(kai);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return kai;
    }

    private static RivalDifficultyProfile CreateOrUpdateDifficultyProfile(string assetPath, RivalDifficulty difficulty)
    {
        RivalDifficultyProfile profile = AssetDatabase.LoadAssetAtPath<RivalDifficultyProfile>(assetPath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<RivalDifficultyProfile>();
            AssetDatabase.CreateAsset(profile, assetPath);
        }

        profile.ConfigureDefaults(difficulty);
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void EnsureFolderExists(string parentFolder, string childFolderName)
    {
        string combined = $"{parentFolder}/{childFolderName}";
        if (!AssetDatabase.IsValidFolder(combined))
        {
            AssetDatabase.CreateFolder(parentFolder, childFolderName);
        }
    }

    // =========================================================================
    // SCENE & RIVAL HOST HELPERS
    // =========================================================================

    private static Scene EnsureTargetSceneOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        bool hasMatchObjects =
            Object.FindFirstObjectByType<MatchReferee>(FindObjectsInactive.Include) != null ||
            Object.FindFirstObjectByType<SimplePingPongAI>(FindObjectsInactive.Include) != null ||
            Object.FindFirstObjectByType<RivalAIController>(FindObjectsInactive.Include) != null;

        if (hasMatchObjects)
        {
            return active;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) != null)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }
            return default;
        }

        EditorUtility.DisplayDialog(
            "Escena no válida",
            $"La escena activa '{active.name}' no contiene MatchReferee ni SimplePingPongAI y no se encontró '{SampleScenePath}'.",
            "Entendido"
        );
        return default;
    }

    private static GameObject ResolveRivalHost(RivalAIController existingRival, SimplePingPongAI legacyAI)
    {
        if (existingRival != null) return existingRival.gameObject;
        if (legacyAI != null) return legacyAI.gameObject;

        GameObject host = new GameObject("RivalAI");
        Undo.RegisterCreatedObjectUndo(host, "Create RivalAI GameObject");
        return host;
    }

    private static Transform FindObjectTransformByTagOrName(string tagName, string fallbackName)
    {
        try
        {
            GameObject tagged = GameObject.FindGameObjectWithTag(tagName);
            if (tagged != null) return tagged.transform;
        }
        catch (UnityException)
        {
            // Tag no definido en el proyecto; usar búsqueda por nombre
        }

        GameObject byName = GameObject.Find(fallbackName);
        return byName != null ? byName.transform : null;
    }

    private static TMP_FontAsset ResolveSharedTmpFont(MatchReferee referee, MenuManager menuManager)
    {
        if (referee != null && referee.scoreText != null && referee.scoreText.font != null)
        {
            return referee.scoreText.font;
        }

        if (menuManager != null && menuManager.heightDisplayText != null && menuManager.heightDisplayText.font != null)
        {
            return menuManager.heightDisplayText.font;
        }

        TextMeshProUGUI anyTmp = Object.FindFirstObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
        if (anyTmp != null && anyTmp.font != null)
        {
            return anyTmp.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    private static Canvas ResolveScoreboardCanvas(MatchReferee referee)
    {
        if (referee != null && referee.scoreText != null)
        {
            Canvas c = referee.scoreText.GetComponentInParent<Canvas>(true);
            if (c != null) return c;
        }

        return Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
    }

    // =========================================================================
    // 1. DIFFICULTY SELECTOR BAR (INSIDE MAIN MENU PANEL)
    // =========================================================================

    private static void SetupDifficultySelectorUI(MenuManager menuManager, RivalProfile kaiProfile, TMP_FontAsset font)
    {
        Transform menuParent = menuManager.mainMenuPanel.transform;
        GameObject barGo = FindOrCreateUIChild(menuParent, DifficultyBarName);
        RectTransform barRect = barGo.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.sizeDelta = new Vector2(440f, 152f);
        barRect.localScale = Vector3.one;

        UnityEngine.UI.Image barBg = GetOrAddComponent<UnityEngine.UI.Image>(barGo);
        barBg.color = new Color(0.08f, 0.11f, 0.16f, 0.88f);
        barBg.raycastTarget = false;

        UnityEngine.UI.LayoutElement barLayout = GetOrAddComponent<UnityEngine.UI.LayoutElement>(barGo);
        barLayout.minHeight = 144f;
        barLayout.preferredHeight = 152f;
        barLayout.flexibleWidth = 1f;

        UnityEngine.UI.VerticalLayoutGroup barVlg = GetOrAddComponent<UnityEngine.UI.VerticalLayoutGroup>(barGo);
        barVlg.padding = new RectOffset(14, 14, 10, 10);
        barVlg.spacing = 8f;
        barVlg.childAlignment = TextAnchor.UpperCenter;
        barVlg.childControlWidth = true;
        barVlg.childControlHeight = true;
        barVlg.childForceExpandWidth = true;
        barVlg.childForceExpandHeight = false;

        // Encabezado "Dificultad: Medio"
        TextMeshProUGUI selectedText = FindOrCreateTMP(barGo.transform, "SelectedDifficultyText", "Dificultad: Medio", 20f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
        ConfigureLayoutElement(selectedText.gameObject, 26f, 28f, flexibleWidth: 1f);

        // Fila de 3 botones: Fácil / Medio / Difícil
        GameObject rowGo = FindOrCreateUIChild(barGo.transform, "DifficultyButtonsRow");
        ConfigureLayoutElement(rowGo, 46f, 48f, flexibleWidth: 1f);
        UnityEngine.UI.HorizontalLayoutGroup rowHlg = GetOrAddComponent<UnityEngine.UI.HorizontalLayoutGroup>(rowGo);
        rowHlg.spacing = 10f;
        rowHlg.childAlignment = TextAnchor.MiddleCenter;
        rowHlg.childControlWidth = true;
        rowHlg.childControlHeight = true;
        rowHlg.childForceExpandWidth = true;
        rowHlg.childForceExpandHeight = true;

        Color unselected = menuManager.unselectedButtonColor;
        Color mediumAccent = kaiProfile != null && kaiProfile.mediumProfile != null
            ? kaiProfile.mediumProfile.accentColor
            : menuManager.selectedButtonColor;

        UnityEngine.UI.Button easyBtn = FindOrCreateButton(rowGo.transform, "Btn_Difficulty_Easy", "Fácil", unselected, 18f, font);
        UnityEngine.UI.Button mediumBtn = FindOrCreateButton(rowGo.transform, "Btn_Difficulty_Medium", "Medio", mediumAccent, 18f, font);
        UnityEngine.UI.Button hardBtn = FindOrCreateButton(rowGo.transform, "Btn_Difficulty_Hard", "Difícil", unselected, 18f, font);

        BindButtonVoidListener(easyBtn, menuManager.SelectEasy);
        BindButtonVoidListener(mediumBtn, menuManager.SelectMedium);
        BindButtonVoidListener(hardBtn, menuManager.SelectHard);

        // Descripción táctica
        string defaultDesc = kaiProfile != null && kaiProfile.mediumProfile != null
            ? kaiProfile.mediumProfile.description
            : "Ritmo equilibrado con drives, slices ocasionales y uso moderado de poder.";
        TextMeshProUGUI descText = FindOrCreateTMP(barGo.transform, "DifficultyDescriptionText", defaultDesc, 13.5f, FontStyles.Normal, new Color(0.84f, 0.88f, 0.94f, 1f), TextAlignmentOptions.Center, font);
        descText.textWrappingMode = TextWrappingModes.Normal;
        ConfigureLayoutElement(descText.gameObject, 38f, 44f, flexibleWidth: 1f);

        Undo.RecordObject(menuManager, "Wire Difficulty Selector UI");
        menuManager.easyDifficultyButton = easyBtn;
        menuManager.mediumDifficultyButton = mediumBtn;
        menuManager.hardDifficultyButton = hardBtn;
        menuManager.selectedDifficultyText = selectedText;
        menuManager.difficultyDescriptionText = descText;
        EditorUtility.SetDirty(menuManager);
    }

    // =========================================================================
    // 2. RIVAL HUD BLOCK (ABOVE SCOREBOARD)
    // =========================================================================

    private static void SetupRivalHudUI(
        MatchReferee referee,
        Transform canvasTransform,
        RivalProfile kaiProfile,
        RivalDifficulty activeDifficulty,
        TMP_FontAsset font)
    {
        GameObject hudGo = FindOrCreateUIChild(canvasTransform, RivalHudPanelName);
        RectTransform hudRect = hudGo.GetComponent<RectTransform>();
        hudRect.anchorMin = new Vector2(0.5f, 1f);
        hudRect.anchorMax = new Vector2(0.5f, 1f);
        hudRect.pivot = new Vector2(0.5f, 0f);
        hudRect.anchoredPosition = new Vector2(0f, 16f);
        hudRect.sizeDelta = new Vector2(780f, 128f);
        hudRect.localScale = Vector3.one;

        UnityEngine.UI.Image hudBg = GetOrAddComponent<UnityEngine.UI.Image>(hudGo);
        hudBg.color = new Color(0.07f, 0.09f, 0.14f, 0.92f);
        hudBg.raycastTarget = false;

        UnityEngine.UI.VerticalLayoutGroup hudVlg = GetOrAddComponent<UnityEngine.UI.VerticalLayoutGroup>(hudGo);
        hudVlg.padding = new RectOffset(22, 22, 12, 12);
        hudVlg.spacing = 10f;
        hudVlg.childAlignment = TextAnchor.MiddleCenter;
        hudVlg.childControlWidth = true;
        hudVlg.childControlHeight = true;
        hudVlg.childForceExpandWidth = true;
        hudVlg.childForceExpandHeight = false;

        // Fila 1: Nombre del rival + Badge de dificultad
        GameObject headerRow = FindOrCreateUIChild(hudGo.transform, "RivalHeaderRow");
        ConfigureLayoutElement(headerRow, 46f, 48f, flexibleWidth: 1f);
        UnityEngine.UI.HorizontalLayoutGroup headerHlg = GetOrAddComponent<UnityEngine.UI.HorizontalLayoutGroup>(headerRow);
        headerHlg.spacing = 16f;
        headerHlg.childAlignment = TextAnchor.MiddleLeft;
        headerHlg.childControlWidth = true;
        headerHlg.childControlHeight = true;
        headerHlg.childForceExpandWidth = false;
        headerHlg.childForceExpandHeight = true;

        RivalDifficultyProfile diffProfile = kaiProfile != null ? kaiProfile.GetProfile(activeDifficulty) : null;
        string rivalName = kaiProfile != null ? kaiProfile.rivalName : "Entrenador Kai";
        string diffLabel = diffProfile != null ? diffProfile.displayLabel : "Medio";
        Color diffColor = diffProfile != null ? diffProfile.accentColor : new Color(1f, 0.70f, 0.16f, 1f);
        int powerMax = diffProfile != null ? diffProfile.powerHitsRequired : 6;

        TextMeshProUGUI rivalNameText = FindOrCreateTMP(headerRow.transform, "RivalNameText", rivalName, 32f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, font);
        ConfigureLayoutElement(rivalNameText.gameObject, 42f, 46f, flexibleWidth: 1f);

        GameObject badgeGo = FindOrCreateUIChild(headerRow.transform, "RivalDifficultyBadge");
        ConfigureLayoutElement(badgeGo, 40f, 42f, minWidth: 170f, preferredWidth: 185f, flexibleWidth: 0f);
        UnityEngine.UI.Image badgeImg = GetOrAddComponent<UnityEngine.UI.Image>(badgeGo);
        badgeImg.color = diffColor;
        badgeImg.raycastTarget = false;

        TextMeshProUGUI badgeText = FindOrCreateTMP(badgeGo.transform, "RivalDifficultyText", diffLabel, 24f, FontStyles.Bold, new Color(0.06f, 0.08f, 0.12f, 1f), TextAlignmentOptions.Center, font);
        StretchFull(badgeText.rectTransform);

        // Fila 2: Texto de poder + Slider de energía
        GameObject powerRow = FindOrCreateUIChild(hudGo.transform, "RivalPowerRow");
        ConfigureLayoutElement(powerRow, 42f, 44f, flexibleWidth: 1f);
        UnityEngine.UI.HorizontalLayoutGroup powerHlg = GetOrAddComponent<UnityEngine.UI.HorizontalLayoutGroup>(powerRow);
        powerHlg.spacing = 16f;
        powerHlg.childAlignment = TextAnchor.MiddleLeft;
        powerHlg.childControlWidth = true;
        powerHlg.childControlHeight = true;
        powerHlg.childForceExpandWidth = false;
        powerHlg.childForceExpandHeight = false;

        TextMeshProUGUI powerText = FindOrCreateTMP(powerRow.transform, "RivalPowerText", $"Poder Rival: 0 / {powerMax}", 23f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, font);
        ConfigureLayoutElement(powerText.gameObject, 36f, 38f, minWidth: 250f, preferredWidth: 265f, flexibleWidth: 0f);

        UnityEngine.UI.Slider powerSlider = FindOrCreateProgressSlider(powerRow.transform, "RivalPowerSlider", powerMax, new Color(1f, 0.78f, 0.18f, 1f));
        ConfigureLayoutElement(powerSlider.gameObject, 26f, 28f, flexibleWidth: 1f);

        Undo.RecordObject(referee, "Wire Rival HUD References");
        referee.rivalNameText = rivalNameText;
        referee.rivalDifficultyText = badgeText;
        referee.rivalDifficultyBadge = badgeImg;
        referee.rivalPowerText = powerText;
        referee.rivalPowerSlider = powerSlider;
        EditorUtility.SetDirty(referee);
    }

    // =========================================================================
    // 3. MATCH END PANEL (INACTIVE BY DEFAULT, VR RAYCAST READY)
    // =========================================================================

    private static void SetupMatchEndPanelUI(
        MatchReferee referee,
        MenuManager menuManager,
        Canvas hostCanvas,
        TMP_FontAsset font)
    {
        // Garantizar Raycasters en el Canvas para que los punteros láser VR interactúen al terminar el partido
        GetOrAddComponent<UnityEngine.UI.GraphicRaycaster>(hostCanvas.gameObject);
        GetOrAddComponent<TrackedDeviceGraphicRaycaster>(hostCanvas.gameObject);

        GameObject panelGo = FindOrCreateUIChild(hostCanvas.transform, MatchEndPanelName);
        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(780f, 410f);
        panelRect.localScale = Vector3.one;
        panelGo.transform.SetAsLastSibling();

        UnityEngine.UI.Image panelBg = GetOrAddComponent<UnityEngine.UI.Image>(panelGo);
        panelBg.color = new Color(0.06f, 0.08f, 0.13f, 0.96f);
        panelBg.raycastTarget = true;

        UnityEngine.UI.VerticalLayoutGroup panelVlg = GetOrAddComponent<UnityEngine.UI.VerticalLayoutGroup>(panelGo);
        panelVlg.padding = new RectOffset(32, 32, 28, 28);
        panelVlg.spacing = 20f;
        panelVlg.childAlignment = TextAnchor.MiddleCenter;
        panelVlg.childControlWidth = true;
        panelVlg.childControlHeight = true;
        panelVlg.childForceExpandWidth = true;
        panelVlg.childForceExpandHeight = false;

        TextMeshProUGUI titleText = FindOrCreateTMP(panelGo.transform, "MatchEndTitleText", "¡VICTORIA!", 48f, FontStyles.Bold, new Color(1f, 0.85f, 0.22f, 1f), TextAlignmentOptions.Center, font);
        ConfigureLayoutElement(titleText.gameObject, 66f, 72f, flexibleWidth: 1f);

        TextMeshProUGUI scoreText = FindOrCreateTMP(panelGo.transform, "MatchEndScoreText", "Marcador Final: 11 - 7\nDificultad: Medio", 30f, FontStyles.Normal, new Color(0.92f, 0.94f, 0.98f, 1f), TextAlignmentOptions.Center, font);
        scoreText.textWrappingMode = TextWrappingModes.Normal;
        ConfigureLayoutElement(scoreText.gameObject, 92f, 102f, flexibleWidth: 1f);

        GameObject buttonsRow = FindOrCreateUIChild(panelGo.transform, "MatchEndButtonsRow");
        ConfigureLayoutElement(buttonsRow, 86f, 92f, flexibleWidth: 1f);
        UnityEngine.UI.HorizontalLayoutGroup btnHlg = GetOrAddComponent<UnityEngine.UI.HorizontalLayoutGroup>(buttonsRow);
        btnHlg.spacing = 18f;
        btnHlg.childAlignment = TextAnchor.MiddleCenter;
        btnHlg.childControlWidth = true;
        btnHlg.childControlHeight = true;
        btnHlg.childForceExpandWidth = true;
        btnHlg.childForceExpandHeight = true;

        UnityEngine.UI.Button rematchBtn = FindOrCreateButton(buttonsRow.transform, "Btn_MatchEnd_Rematch", "Revancha", new Color(0.16f, 0.68f, 0.36f, 1f), 24f, font);
        UnityEngine.UI.Button diffBtn = FindOrCreateButton(buttonsRow.transform, "Btn_MatchEnd_ChangeDifficulty", "Cambiar Dificultad", new Color(0.20f, 0.48f, 0.82f, 1f), 22f, font);
        UnityEngine.UI.Button menuBtn = FindOrCreateButton(buttonsRow.transform, "Btn_MatchEnd_MainMenu", "Menú", new Color(0.32f, 0.36f, 0.44f, 1f), 24f, font);

        if (menuManager != null)
        {
            BindButtonVoidListener(rematchBtn, menuManager.Rematch);
            BindButtonVoidListener(diffBtn, menuManager.OpenDifficultyMenu);
            BindButtonVoidListener(menuBtn, menuManager.ReturnToMainMenu);
        }

        // Oculto por defecto hasta que termine un partido
        panelGo.SetActive(false);

        Undo.RecordObject(referee, "Wire MatchEndPanel References");
        referee.matchEndPanel = panelGo;
        referee.matchEndTitleText = titleText;
        referee.matchEndScoreText = scoreText;
        EditorUtility.SetDirty(referee);
    }

    // =========================================================================
    // UGUI FACTORY HELPERS (IDEMPOTENT)
    // =========================================================================

    private static GameObject FindOrCreateUIChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            if (existing.GetComponent<RectTransform>() == null)
            {
                Undo.AddComponent<RectTransform>(existing.gameObject);
            }
            existing.gameObject.layer = parent.gameObject.layer;
            return existing.gameObject;
        }

        GameObject go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, $"Create UI Element {name}");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static T GetOrAddComponent<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        if (comp == null)
        {
            comp = Undo.AddComponent<T>(go);
        }
        return comp;
    }

    private static TextMeshProUGUI FindOrCreateTMP(
        Transform parent,
        string name,
        string defaultText,
        float fontSize,
        FontStyles style,
        Color color,
        TextAlignmentOptions alignment,
        TMP_FontAsset font)
    {
        GameObject go = FindOrCreateUIChild(parent, name);
        TextMeshProUGUI tmp = GetOrAddComponent<TextMeshProUGUI>(go);
        Undo.RecordObject(tmp, $"Configure TMP {name}");
        tmp.text = defaultText;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        if (font != null)
        {
            tmp.font = font;
        }
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

    private static UnityEngine.UI.Button FindOrCreateButton(
        Transform parent,
        string name,
        string label,
        Color backgroundColor,
        float fontSize,
        TMP_FontAsset font)
    {
        GameObject go = FindOrCreateUIChild(parent, name);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(140f, 48f);

        UnityEngine.UI.Image bg = GetOrAddComponent<UnityEngine.UI.Image>(go);
        Undo.RecordObject(bg, $"Configure Button Image {name}");
        bg.color = backgroundColor;
        bg.raycastTarget = true;

        UnityEngine.UI.Button btn = GetOrAddComponent<UnityEngine.UI.Button>(go);
        Undo.RecordObject(btn, $"Configure Button {name}");
        btn.targetGraphic = bg;
        UnityEngine.UI.ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.selectedColor = Color.white;
        btn.colors = cb;

        TextMeshProUGUI text = FindOrCreateTMP(go.transform, "Label", label, fontSize, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
        text.textWrappingMode = TextWrappingModes.Normal;
        StretchFull(text.rectTransform, 8f, 4f);

        EditorUtility.SetDirty(bg);
        EditorUtility.SetDirty(btn);
        return btn;
    }

    private static UnityEngine.UI.Slider FindOrCreateProgressSlider(Transform parent, string name, int maxValue, Color fillColor)
    {
        GameObject sliderGo = FindOrCreateUIChild(parent, name);
        RectTransform sliderRect = sliderGo.GetComponent<RectTransform>();
        sliderRect.sizeDelta = new Vector2(400f, 26f);

        GameObject bgGo = FindOrCreateUIChild(sliderGo.transform, "Background");
        StretchFull(bgGo.GetComponent<RectTransform>());
        UnityEngine.UI.Image bgImg = GetOrAddComponent<UnityEngine.UI.Image>(bgGo);
        bgImg.color = new Color(0.18f, 0.22f, 0.30f, 1f);
        bgImg.raycastTarget = false;

        GameObject fillAreaGo = FindOrCreateUIChild(sliderGo.transform, "Fill Area");
        StretchFull(fillAreaGo.GetComponent<RectTransform>(), 3f, 3f);

        GameObject fillGo = FindOrCreateUIChild(fillAreaGo.transform, "Fill");
        RectTransform fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        UnityEngine.UI.Image fillImg = GetOrAddComponent<UnityEngine.UI.Image>(fillGo);
        fillImg.color = fillColor;
        fillImg.raycastTarget = false;

        UnityEngine.UI.Slider slider = GetOrAddComponent<UnityEngine.UI.Slider>(sliderGo);
        Undo.RecordObject(slider, $"Configure Slider {name}");
        slider.interactable = false;
        slider.transition = UnityEngine.UI.Selectable.Transition.None;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.fillRect = fillRect;
        slider.handleRect = null;
        slider.minValue = 0f;
        slider.maxValue = Mathf.Max(1, maxValue);
        slider.wholeNumbers = true;
        slider.value = 0f;

        EditorUtility.SetDirty(slider);
        return slider;
    }

    private static void BindButtonVoidListener(UnityEngine.UI.Button button, UnityAction action)
    {
        if (button == null || action == null) return;

        Undo.RecordObject(button, $"Bind Button {button.name}");
        int count = button.onClick.GetPersistentEventCount();
        for (int i = count - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        }

        UnityEventTools.AddVoidPersistentListener(button.onClick, action);
        EditorUtility.SetDirty(button);
    }

    private static void ConfigureLayoutElement(
        GameObject go,
        float minHeight,
        float preferredHeight,
        float minWidth = -1f,
        float preferredWidth = -1f,
        float flexibleWidth = -1f)
    {
        UnityEngine.UI.LayoutElement le = GetOrAddComponent<UnityEngine.UI.LayoutElement>(go);
        le.minHeight = minHeight;
        le.preferredHeight = preferredHeight;
        if (minWidth >= 0f) le.minWidth = minWidth;
        if (preferredWidth >= 0f) le.preferredWidth = preferredWidth;
        if (flexibleWidth >= 0f) le.flexibleWidth = flexibleWidth;
    }

    private static void StretchFull(RectTransform rect, float padX = 0f, float padY = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(padX, padY);
        rect.offsetMax = new Vector2(-padX, -padY);
    }
}
#endif
