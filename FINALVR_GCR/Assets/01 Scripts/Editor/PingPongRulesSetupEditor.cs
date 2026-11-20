#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Herramienta de Editor idempotente para configurar las reglas oficiales de Ping Pong
/// en SampleScene:
/// - Etiqueta Collider_Net como "Net".
/// - Corrige el grosor Y=0 del BoxCollider de TableEnemy y desactiva el Collider_Surface solapado.
/// - Crea un Floor collider de seguridad etiquetado "Floor" si no existe ninguno en la escena.
/// - Crea y conecta ServeIndicator3D sobre la mesa.
/// - Añade SetsText y CalloutText (DEUCE / SET POINT / MATCH POINT / LET) al marcador y vincula MatchReferee y BallReporter.
/// </summary>
public static class PingPongRulesSetupEditor
{
    private const string SampleScenePath = "Assets/03 Scenes/SampleScene.unity";
    private const string ServeIndicatorName = "ServeIndicator3D";
    private const string FloorBoundsName = "Arena_Floor_Bounds";
    private const string RulesOverlayName = "PingPongRulesBanner";

    [MenuItem("PingPing PongPong/Rules/Setup Ping Pong Rules in SampleScene")]
    public static void SetupPingPongRulesInSampleScene()
    {
        Scene activeScene = EnsureSampleSceneOpen();
        if (!activeScene.IsValid()) return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup Ping Pong Rules in SampleScene");

        MatchReferee referee = Object.FindFirstObjectByType<MatchReferee>(FindObjectsInactive.Include);
        if (referee == null)
        {
            EditorUtility.DisplayDialog(
                "MatchReferee no encontrado",
                $"No se encontró ningún MatchReferee en '{activeScene.name}'. Abre SampleScene y vuelve a intentarlo.",
                "Entendido"
            );
            return;
        }

        // 1. Etiquetar Collider_Net como "Net" y reparar colliders de mesa (TableEnemy Y=0 + Collider_Surface solapado)
        Transform playerTable = FindTransformByTagOrName("TablePlayer", "TablePlayer");
        Transform enemyTable = FindTransformByTagOrName("TableEnemy", "TableEnemy");
        FixTableAndNetColliders(playerTable, enemyTable);

        // 2. Garantizar un collider de suelo etiquetado "Floor"
        EnsureFloorColliderExists(playerTable, enemyTable);

        // 3. Vincular BallReporter -> MatchReferee
        BallReporter ballReporter = Object.FindFirstObjectByType<BallReporter>(FindObjectsInactive.Include);
        if (ballReporter != null)
        {
            Undo.RecordObject(ballReporter, "Wire BallReporter Referee");
            ballReporter.referee = referee;
            EditorUtility.SetDirty(ballReporter);
        }

        // 4. Configurar parámetros oficiales de MatchReferee (Mejor de 3 sets a 11 pts)
        Undo.RecordObject(referee, "Configure MatchReferee Official Rules");
        referee.enabled = true;
        referee.pointsToWin = 11;
        referee.pointsToWinBy = 2;
        referee.setsToWin = 2;
        referee.delayBetweenPoints = 1.35f;
        referee.delayAfterLet = 1.15f;
        referee.delayBetweenSets = 2.25f;
        if (referee.playerTableTarget == null) referee.playerTableTarget = playerTable;
        if (referee.enemyTableTarget == null) referee.enemyTableTarget = enemyTable;
        if (referee.ballController == null)
        {
            referee.ballController = Object.FindFirstObjectByType<BallController>(FindObjectsInactive.Include);
        }
        EditorUtility.SetDirty(referee);

        // 5. Crear y conectar ServeIndicator3D sobre la mesa
        ServeIndicator3D indicator = SetupServeIndicator3D(referee, playerTable, enemyTable);
        Undo.RecordObject(referee, "Link ServeIndicator3D");
        referee.serveIndicator = indicator;
        EditorUtility.SetDirty(referee);

        // 6. Añadir textos de Sets y Callout (DEUCE / SET POINT / MATCH POINT / LET) al Canvas del marcador
        SetupRulesScoreboardUI(referee);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = referee.gameObject;

        Debug.Log($"[PingPongRulesSetupEditor] Reglas oficiales de Ping Pong configuradas en '{activeScene.name}' (Net, TableEnemy, Floor, Mejor de 3 sets, ServeIndicator3D y Callouts). Guarda la escena (Ctrl+S).");
    }

    private static void FixTableAndNetColliders(Transform playerTable, Transform enemyTable)
    {
        // Etiquetar Collider_Net como "Net"
        GameObject netObj = GameObject.Find("Collider_Net");
        if (netObj != null)
        {
            Undo.RecordObject(netObj, "Tag Collider_Net as Net");
            TryAssignTagSafe(netObj, "Net");
            EditorUtility.SetDirty(netObj);
        }

        // Alinear TableEnemy BoxCollider con TablePlayer para eliminar grosor Y=0
        BoxCollider playerCol = playerTable != null ? playerTable.GetComponent<BoxCollider>() : null;
        BoxCollider enemyCol = enemyTable != null ? enemyTable.GetComponent<BoxCollider>() : null;

        if (enemyCol != null && enemyCol.size.y < 0.02f)
        {
            Undo.RecordObject(enemyCol, "Fix TableEnemy Zero-Height BoxCollider");
            Vector3 size = enemyCol.size;
            Vector3 center = enemyCol.center;

            if (playerCol != null && playerCol.size.y > 0.02f)
            {
                float targetTopY = playerCol.center.y + playerCol.size.y * 0.5f;
                size.y = playerCol.size.y;
                center.y = targetTopY - size.y * 0.5f;
            }
            else
            {
                size.y = 0.06f;
                center.y -= 0.03f;
            }

            enemyCol.size = size;
            enemyCol.center = center;
            EditorUtility.SetDirty(enemyCol);
        }

        // Desactivar Collider_Surface solapado cuando TablePlayer y TableEnemy existen para no interceptar rebotes
        if (playerCol != null && enemyCol != null)
        {
            GameObject surfaceObj = GameObject.Find("Collider_Surface");
            if (surfaceObj != null)
            {
                BoxCollider surfCol = surfaceObj.GetComponent<BoxCollider>();
                if (surfCol != null && surfCol.enabled)
                {
                    Undo.RecordObject(surfCol, "Disable Overlapping Collider_Surface");
                    surfCol.enabled = false;
                    EditorUtility.SetDirty(surfCol);
                }
            }
        }
    }

    private static void EnsureFloorColliderExists(Transform playerTable, Transform enemyTable)
    {
        try
        {
            GameObject existingFloor = GameObject.FindGameObjectWithTag("Floor");
            if (existingFloor != null) return;
        }
        catch (UnityException)
        {
            // Continuar si el tag aún no está registrado
        }

        GameObject floorObj = GameObject.Find(FloorBoundsName);
        if (floorObj == null)
        {
            floorObj = new GameObject(FloorBoundsName);
            Undo.RegisterCreatedObjectUndo(floorObj, "Create Arena Floor Bounds");
        }

        Vector3 centerPos = Vector3.zero;
        if (playerTable != null && enemyTable != null)
        {
            Collider pCol = playerTable.GetComponentInChildren<Collider>();
            Collider eCol = enemyTable.GetComponentInChildren<Collider>();
            if (pCol != null && eCol != null)
            {
                centerPos = 0.5f * (pCol.bounds.center + eCol.bounds.center);
            }
        }

        Undo.RecordObject(floorObj.transform, "Position Arena Floor Bounds");
        floorObj.transform.position = new Vector3(centerPos.x, -0.05f, centerPos.z);
        floorObj.transform.rotation = Quaternion.identity;
        floorObj.transform.localScale = Vector3.one;

        BoxCollider floorCol = floorObj.GetComponent<BoxCollider>();
        if (floorCol == null)
        {
            floorCol = Undo.AddComponent<BoxCollider>(floorObj);
        }

        Undo.RecordObject(floorCol, "Configure Arena Floor Bounds");
        floorCol.isTrigger = false;
        floorCol.center = Vector3.zero;
        floorCol.size = new Vector3(16f, 0.1f, 16f);

        Undo.RecordObject(floorObj, "Tag Arena Floor Bounds");
        TryAssignTagSafe(floorObj, "Floor");
        EditorUtility.SetDirty(floorObj);
        EditorUtility.SetDirty(floorCol);
    }

    private static ServeIndicator3D SetupServeIndicator3D(MatchReferee referee, Transform playerTable, Transform enemyTable)
    {
        ServeIndicator3D indicator = Object.FindFirstObjectByType<ServeIndicator3D>(FindObjectsInactive.Include);
        if (indicator == null)
        {
            GameObject go = new GameObject(ServeIndicatorName);
            Undo.RegisterCreatedObjectUndo(go, "Create ServeIndicator3D");
            indicator = Undo.AddComponent<ServeIndicator3D>(go);
        }

        Undo.RecordObject(indicator, "Configure ServeIndicator3D");
        indicator.referee = referee;
        indicator.playerTableTarget = playerTable;
        indicator.enemyTableTarget = enemyTable;
        indicator.BuildVisualIfNeeded();
        indicator.SnapToCurrentServer();
        EditorUtility.SetDirty(indicator);
        return indicator;
    }

    private static void SetupRulesScoreboardUI(MatchReferee referee)
    {
        Canvas scoreboardCanvas = null;
        if (referee.scoreText != null)
        {
            scoreboardCanvas = referee.scoreText.GetComponentInParent<Canvas>(true);
        }
        if (scoreboardCanvas == null)
        {
            scoreboardCanvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        }
        if (scoreboardCanvas == null) return;

        TMP_FontAsset sharedFont = referee.scoreText != null && referee.scoreText.font != null
            ? referee.scoreText.font
            : TMP_Settings.defaultFontAsset;

        Transform existingBanner = scoreboardCanvas.transform.Find(RulesOverlayName);
        GameObject bannerGo;
        if (existingBanner != null)
        {
            bannerGo = existingBanner.gameObject;
        }
        else
        {
            bannerGo = new GameObject(RulesOverlayName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(bannerGo, "Create PingPongRulesBanner");
            bannerGo.layer = scoreboardCanvas.gameObject.layer;
            bannerGo.transform.SetParent(scoreboardCanvas.transform, false);
        }

        RectTransform bannerRect = bannerGo.GetComponent<RectTransform>();
        bannerRect.anchorMin = new Vector2(0.5f, 0f);
        bannerRect.anchorMax = new Vector2(0.5f, 0f);
        bannerRect.pivot = new Vector2(0.5f, 1f);
        bannerRect.anchoredPosition = new Vector2(0f, -14f);
        bannerRect.sizeDelta = new Vector2(780f, 108f);
        bannerRect.localScale = Vector3.one;

        UnityEngine.UI.Image bg = bannerGo.GetComponent<UnityEngine.UI.Image>();
        if (bg == null) bg = Undo.AddComponent<UnityEngine.UI.Image>(bannerGo);
        bg.color = new Color(0.06f, 0.08f, 0.13f, 0.90f);
        bg.raycastTarget = false;

        UnityEngine.UI.VerticalLayoutGroup vlg = bannerGo.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (vlg == null) vlg = Undo.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(bannerGo);
        vlg.padding = new RectOffset(20, 20, 10, 10);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TextMeshProUGUI setsTmp = FindOrCreateTMP(
            bannerGo.transform,
            "SetsScoreText",
            "Sets: 0 - 0  (Mejor de 3)",
            28f,
            FontStyles.Bold,
            new Color(0.92f, 0.95f, 1f, 1f),
            sharedFont
        );
        ConfigureLayoutHeight(setsTmp.gameObject, 38f);

        TextMeshProUGUI calloutTmp = FindOrCreateTMP(
            bannerGo.transform,
            "MatchCalloutText",
            "",
            30f,
            FontStyles.Bold,
            new Color(1f, 0.85f, 0.20f, 1f),
            sharedFont
        );
        ConfigureLayoutHeight(calloutTmp.gameObject, 40f);
        calloutTmp.gameObject.SetActive(false);

        Undo.RecordObject(referee, "Wire SetsText and CalloutText");
        referee.setsText = setsTmp;
        referee.calloutText = calloutTmp;
        EditorUtility.SetDirty(referee);
    }

    private static TextMeshProUGUI FindOrCreateTMP(
        Transform parent,
        string name,
        string initialText,
        float fontSize,
        FontStyles style,
        Color color,
        TMP_FontAsset font)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
        }

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp == null) tmp = Undo.AddComponent<TextMeshProUGUI>(go);

        Undo.RecordObject(tmp, $"Configure {name}");
        tmp.text = initialText;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        if (font != null) tmp.font = font;
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

    private static void ConfigureLayoutHeight(GameObject go, float height)
    {
        UnityEngine.UI.LayoutElement le = go.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le == null) le = Undo.AddComponent<UnityEngine.UI.LayoutElement>(go);
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
    }

    private static Scene EnsureSampleSceneOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path == SampleScenePath || active.name == "SampleScene")
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

        return active;
    }

    private static Transform FindTransformByTagOrName(string tagName, string fallbackName)
    {
        try
        {
            GameObject tagged = GameObject.FindGameObjectWithTag(tagName);
            if (tagged != null) return tagged.transform;
        }
        catch (UnityException)
        {
            // Ignorar si no existe el tag
        }

        GameObject named = GameObject.Find(fallbackName);
        return named != null ? named.transform : null;
    }

    private static void TryAssignTagSafe(GameObject go, string tagName)
    {
        if (go == null) return;
        try
        {
            go.tag = tagName;
        }
        catch (UnityException)
        {
            Debug.LogWarning($"[PingPongRulesSetupEditor] El tag '{tagName}' no está definido en TagManager.");
        }
    }
}
#endif
