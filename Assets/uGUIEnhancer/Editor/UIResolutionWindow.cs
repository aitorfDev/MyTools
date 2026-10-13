using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class UIResolutionWindow : EditorWindow
{
    int customW = 1920, customH = 1080;
    Vector2 scroll;

    [MenuItem("Tools/uGUI Enhancer/Window")]
    static void Open() => GetWindow<UIResolutionWindow>("uGUI Enhancer");

    void OnEnable()
    {
        var c = GameViewResolution.Current;
        customW = c.x;
        customH = c.y;
    }

    void OnInspectorUpdate() => Repaint();

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(12, 12, 10, 10) });

        AutoAnchors.Enabled = EditorGUILayout.ToggleLeft("Auto Anchors", AutoAnchors.Enabled, EditorStyles.boldLabel);

        var rt = Selection.activeTransform as RectTransform;
        using (new EditorGUI.DisabledScope(rt == null))
        {
            bool excluded = AutoAnchors.IsExcluded(rt);
            bool now = EditorGUILayout.ToggleLeft("Exclude selected from Auto Anchors", excluded);
            if (now != excluded)
                AutoAnchors.ToggleExclude(rt);
        }

        EditorGUILayout.Space(12);

        if (!GameViewResolution.Available)
            EditorGUILayout.HelpBox("The resolution tools are unavailable in this Unity version (" + GameViewResolution.UnavailableReason + "). Auto Anchors still works.", MessageType.Warning);

        EditorGUI.BeginDisabledGroup(!GameViewResolution.Available);
        var cur = GameViewResolution.Current;
        EditorGUILayout.LabelField("Game View: " + GameViewResolution.Describe(cur), EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("< Previous"))
                GameViewResolution.Cycle(-1);
            if (GUILayout.Button("Rotate (portrait/landscape)"))
                GameViewResolution.Swap();
            if (GUILayout.Button("Next >"))
                GameViewResolution.Cycle(1);
        }

        GameViewResolution.Orientation = (GameViewResolution.OrientationFilter)GUILayout.Toolbar(
            (int)GameViewResolution.Orientation, new[] { "All", "Landscape", "Portrait" });

        var baseRes = GameViewResolution.Base;
        var lastRes = GameViewResolution.Last;
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(lastRes.x <= 0))
                if (GUILayout.Button(lastRes.x > 0 ? $"Last {lastRes.x}x{lastRes.y}" : "Last"))
                    GameViewResolution.GoToLast();
            using (new EditorGUI.DisabledScope(baseRes.x <= 0))
                if (GUILayout.Button(baseRes.x > 0 ? $"Base {baseRes.x}x{baseRes.y}" : "Base (not set)"))
                    GameViewResolution.GoToBase();
            if (GUILayout.Button("Set current as base"))
                GameViewResolution.SetCurrentAsBase();
        }

        EditorGUILayout.Space(12);
        // Foldable list of the Game View resolutions (remembered between sessions).
        bool showResolutions = EditorPrefs.GetBool(ResolutionsOpenKey, false);
        showResolutions = EditorGUILayout.Foldout(showResolutions, "Resolutions (Game View list)", true, EditorStyles.foldoutHeader);
        EditorPrefs.SetBool(ResolutionsOpenKey, showResolutions);
        if (showResolutions)
        {
            foreach (var p in GameViewResolution.GetPresets())
            {
                bool active = p.width == cur.x && p.height == cur.y;
                using (new EditorGUI.DisabledScope(active))
                    if (GUILayout.Button(p.Label))
                        GameViewResolution.Set(p.width, p.height);
            }
        }

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Custom", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            customW = EditorGUILayout.IntField(customW);
            EditorGUILayout.LabelField("x", GUILayout.Width(12));
            customH = EditorGUILayout.IntField(customH);
            if (GUILayout.Button("Apply", GUILayout.Width(60)))
                GameViewResolution.Set(customW, customH);
        }

        var scaler = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<CanvasScaler>() : null;
        if (scaler != null)
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Selected Canvas Scaler", EditorStyles.boldLabel);
            var r = scaler.referenceResolution;
            int rw = Mathf.RoundToInt(r.x), rh = Mathf.RoundToInt(r.y);
            if (GUILayout.Button($"Use its Reference Resolution ({rw} x {rh})"))
                GameViewResolution.Set(rw, rh);
        }

        EditorGUI.EndDisabledGroup();

        DrawAdvanced();

        GUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    // --- Advanced settings ---

    const string AdvancedOpenKey = "UGUIEnhancer.AdvancedOpen";
    const string ResolutionsOpenKey = "UGUIEnhancer.ResolutionsOpen";
    const string ExclusionsOpenKey = "UGUIEnhancer.ExclusionsOpen";

    /// <summary>Opens the window with the Advanced section expanded.</summary>
    public static void OpenAdvanced()
    {
        SessionState.SetBool(AdvancedOpenKey, true);
        var window = GetWindow<UIResolutionWindow>("uGUI Enhancer");
        window.Show();
        window.Repaint();
    }

    static void DrawAdvanced()
    {
        EditorGUILayout.Space(12);
        bool open = SessionState.GetBool(AdvancedOpenKey, false);
        open = EditorGUILayout.Foldout(open, "Advanced", true, EditorStyles.foldoutHeader);
        SessionState.SetBool(AdvancedOpenKey, open);
        if (!open)
            return;

        EditorGUI.indentLevel++;

        UGUIEnhancerSettings.WallTolerance = EditorGUILayout.Slider(
            new GUIContent("Wall tolerance", "How close (in canvas units) an element must be to a parent edge to count as touching it. Higher values anchor to walls and corners from further away."),
            UGUIEnhancerSettings.WallTolerance, 0f, 50f);

        float percent = EditorGUILayout.Slider(
            new GUIContent("Release distance (%)", "How far (as % of the parent size) you must move an element from its manual or centered anchors before Auto Anchors takes control again."),
            UGUIEnhancerSettings.BreakAwayFraction * 100f, 5f, 100f);
        UGUIEnhancerSettings.BreakAwayFraction = percent / 100f;

        UGUIEnhancerSettings.IdleDelay = EditorGUILayout.Slider(
            new GUIContent("Inspector edit delay (s)", "Seconds to wait after the last Inspector edit before anchors are applied."),
            (float)UGUIEnhancerSettings.IdleDelay, 0.1f, 2f);

        EditorGUILayout.Space(8);
        bool showExclusions = EditorPrefs.GetBool(ExclusionsOpenKey, false);
        showExclusions = EditorGUILayout.Foldout(showExclusions, new GUIContent("Never auto adjust", "GameObjects whose anchors Auto Anchors never changes."), true);
        EditorPrefs.SetBool(ExclusionsOpenKey, showExclusions);
        if (showExclusions)
        {
            foreach (var rule in UGUIEnhancerExclusions.Rules)
            {
                bool enabled = UGUIEnhancerSettings.IsRuleEnabled(rule);
                bool now = EditorGUILayout.ToggleLeft(new GUIContent(rule.label, rule.tooltip), enabled);
                if (now != enabled)
                    UGUIEnhancerSettings.SetRuleEnabled(rule, now);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                new GUIContent("Also skip objects named (one per line, * is a wildcard)",
                    "For example: Background*, *_Label. Not case sensitive; the whole name must match."),
                EditorStyles.wordWrappedMiniLabel);
            string names = EditorGUILayout.TextArea(UGUIEnhancerSettings.ExcludedNames, GUILayout.MinHeight(44));
            if (names != UGUIEnhancerSettings.ExcludedNames)
                UGUIEnhancerSettings.ExcludedNames = names;
        }

        EditorGUILayout.Space(8);
        if (GUILayout.Button("Reset to defaults"))
            UGUIEnhancerSettings.ResetToDefaults();

        EditorGUI.indentLevel--;
    }
}
