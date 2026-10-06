using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

// uGUI Enhancer: Auto Anchors + Game View resolution tools, all in one file.
// Menu: Tools > uGUI Enhancer.

// ───────────── Settings ─────────────

/// <summary>
/// Values you can adjust in the uGUI Enhancer window (Advanced). Stored per project.
/// </summary>
public static class UGUIEnhancerSettings
{
    public const float DefaultWallTolerance = 2f;
    public const float DefaultBreakAwayFraction = 0.2f;
    public const double DefaultIdleDelay = 0.4;

    static string Key(string name) => "UGUIEnhancer." + name + "." + Application.dataPath;

    /// <summary>Canvas units within which an element counts as "touching" a parent edge.</summary>
    public static float WallTolerance
    {
        get => Mathf.Clamp(EditorPrefs.GetFloat(Key("WallTolerance"), DefaultWallTolerance), 0f, 200f);
        set => EditorPrefs.SetFloat(Key("WallTolerance"), Mathf.Clamp(value, 0f, 200f));
    }

    /// <summary>Fraction of the parent, measured from its anchor, at which an element is released and becomes automatic again.</summary>
    public static float BreakAwayFraction
    {
        get => Mathf.Clamp(EditorPrefs.GetFloat(Key("BreakAwayFraction"), DefaultBreakAwayFraction), 0.02f, 1f);
        set => EditorPrefs.SetFloat(Key("BreakAwayFraction"), Mathf.Clamp(value, 0.02f, 1f));
    }

    /// <summary>Seconds without changes before anchors are applied after an Inspector edit.</summary>
    public static double IdleDelay
    {
        get => Mathf.Clamp(EditorPrefs.GetFloat(Key("IdleDelay"), (float)DefaultIdleDelay), 0.05f, 5f);
        set => EditorPrefs.SetFloat(Key("IdleDelay"), Mathf.Clamp((float)value, 0.05f, 5f));
    }

    public static void ResetToDefaults()
    {
        EditorPrefs.DeleteKey(Key("WallTolerance"));
        EditorPrefs.DeleteKey(Key("BreakAwayFraction"));
        EditorPrefs.DeleteKey(Key("IdleDelay"));
    }
}

// ───────────── Auto Anchors ─────────────

/// <summary>
/// When you move or scale a RectTransform (Button, Image, Panel...) in the editor,
/// anchors are set automatically without the element changing position or size:
/// Per axis: touching both walls -> stretch; touching one wall
/// -> wall anchor, but only if the other axis also has a wall or stretch.
/// Anything else -> anchors around the object.
/// An axis that already has center anchors is respected, and if you change the anchors by hand
/// (preset, Inspector) the element is left to Unity. In both cases, if you move the element
/// far enough from its anchor point (Advanced: release distance) it becomes automatic again.
/// Part of uGUI Enhancer. Toggle in Tools > uGUI Enhancer > Auto Anchors,
/// in the Scene View overlay or in the uGUI Enhancer window.
/// </summary>
[InitializeOnLoad]
public static class AutoAnchors
{
    const string PrefKey = "AutoAnchors.Enabled";
    const float Epsilon = 0.0001f;

    static readonly HashSet<RectTransform> pending = new HashSet<RectTransform>();
    static readonly HashSet<RectTransform> manual = new HashSet<RectTransform>(); // anchors changed by hand: leave alone
    static readonly Dictionary<RectTransform, (Vector2 min, Vector2 max, double time)> applied = new Dictionary<RectTransform, (Vector2 min, Vector2 max, double time)>();
    const double OwnChangeWindow = 1.0; // seconds during which an anchor change right after ours is still considered ours
    static bool mouseDown;
    static double lastChangeTime;

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static AutoAnchors()
    {
        Undo.postprocessModifications += OnPostprocessModifications;
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += OnUpdate;
        EditorApplication.delayCall += LoadManual; // scenes are ready by then
    }

    // --- Manual anchors survive script reloads (kept for this editor session) ---

    const string ManualKey = "UGUIEnhancer.ManualAnchors";

    static void SaveManual()
    {
        var ids = new List<string>();
        foreach (var rt in manual)
            if (rt != null)
                ids.Add(GlobalObjectId.GetGlobalObjectIdSlow(rt).ToString());
        SessionState.SetString(ManualKey, string.Join(";", ids));
    }

    static void LoadManual()
    {
        foreach (string text in SessionState.GetString(ManualKey, "").Split(';'))
        {
            if (GlobalObjectId.TryParse(text, out var id) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is RectTransform rt)
                manual.Add(rt);
        }
    }

    /// <summary>Gives the element back to the tool: it will be re-anchored automatically on its next change.</summary>
    [MenuItem("CONTEXT/RectTransform/uGUI Enhancer: Reset to Auto Anchors")]
    static void ResetToAutoContext(MenuCommand command) => ResetToAuto((RectTransform)command.context);

    [MenuItem("Tools/uGUI Enhancer/Reset Selected To Auto Anchors")]
    static void ResetSelectedToAuto()
    {
        foreach (var go in Selection.gameObjects)
            if (go.transform is RectTransform rt)
                ResetToAuto(rt);
    }

    public static void ResetToAuto(RectTransform rt)
    {
        if (rt == null)
            return;
        manual.Remove(rt);
        SaveManual();
        Apply(rt);
    }

    [Shortcut("uGUI Enhancer/Toggle Auto Anchors", KeyCode.T, ShortcutModifiers.Action | ShortcutModifiers.Alt)]
    static void Toggle()
    {
        Enabled = !Enabled;
        UGUIEnhancerNotify.Show("Auto Anchors: " + (Enabled ? "On" : "Off"));
    }

    static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] mods)
    {
        if (!Enabled || Application.isPlaying)
            return mods;

        // 1) Anchors changed by something other than us (preset, Inspector...): the user wins, let Unity work.
        foreach (var mod in mods)
        {
            if (mod.currentValue?.target is RectTransform rt && IsAnchorProperty(mod.currentValue.propertyPath) && !IsOurAnchors(rt))
            {
                if (manual.Add(rt))
                    SaveManual();
                pending.Remove(rt);
            }
        }

        // 2) Move / scale: recalculate, except on elements with manual anchors.
        foreach (var mod in mods)
        {
            if (mod.currentValue?.target is RectTransform rt && IsGeometryProperty(mod.currentValue.propertyPath))
            {
                pending.Add(rt);
                lastChangeTime = EditorApplication.timeSinceStartup;
            }
        }
        return mods;
    }

    static bool IsAnchorProperty(string path)
    {
        return path.StartsWith("m_AnchorMin") || path.StartsWith("m_AnchorMax");
    }

    static bool IsOurAnchors(RectTransform rt)
    {
        if (!applied.TryGetValue(rt, out var v))
            return false;

        // The undo flush of our own change can arrive late (or after a newer change of ours),
        // so a recent change of ours also counts, not only an exact value match.
        bool recent = EditorApplication.timeSinceStartup - v.time < OwnChangeWindow;
        return recent
            || ((rt.anchorMin - v.min).sqrMagnitude < Epsilon * Epsilon
                && (rt.anchorMax - v.max).sqrMagnitude < Epsilon * Epsilon);
    }

    static bool IsGeometryProperty(string path)
    {
        return path.StartsWith("m_AnchoredPosition")
            || path.StartsWith("m_SizeDelta")
            || path.StartsWith("m_LocalPosition")
            || path.StartsWith("m_LocalScale");
    }

    static void OnSceneGUI(SceneView view)
    {
        var e = Event.current;
        if (e.rawType == EventType.MouseDown)
            mouseDown = true;
        else if (e.rawType == EventType.MouseUp)
        {
            mouseDown = false;
            Flush();
        }
    }

    static void OnUpdate()
    {
        // Edits without the mouse in the Scene (Inspector, arrow keys...): apply once they settle.
        if (pending.Count > 0 && !mouseDown && EditorApplication.timeSinceStartup - lastChangeTime > UGUIEnhancerSettings.IdleDelay)
            Flush();
    }

    static void Flush()
    {
        if (pending.Count == 0)
            return;

        var items = new List<RectTransform>(pending);
        pending.Clear();
        foreach (var rt in items)
            if (rt != null)
                Apply(rt);
    }

    static void Apply(RectTransform rt)
    {
        var parent = rt.parent as RectTransform;
        if (parent == null)
            return; // root canvas or object without a UI parent

        // Explicitly excluded by the user.
        if (rt.TryGetComponent<NoAutoAnchors>(out _))
            return;

        // If a LayoutGroup drives the child, leave it alone.
        if (parent.TryGetComponent<LayoutGroup>(out _))
            return;

        // Axis-aligned anchors: with Z rotation the result would not be exact.
        if (Mathf.Abs(Mathf.DeltaAngle(0f, rt.localEulerAngles.z)) > 0.01f &&
            Mathf.Abs(Mathf.DeltaAngle(180f, rt.localEulerAngles.z)) > 0.01f)
            return;

        Rect pr = parent.rect;
        if (pr.width < Epsilon || pr.height < Epsilon)
            return;

        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector3 a = parent.InverseTransformPoint(corners[0]);
        Vector3 b = parent.InverseTransformPoint(corners[2]);

        Vector2 rMin = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
        Vector2 rMax = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        // Only touching a corner (a wall on each axis) anchors to the walls; otherwise anchors go around the object.
        // If you move far enough from your anchor point, the tool takes control again.
        bool farX = IsFarFromAnchor(rt.anchorMin.x, rt.anchorMax.x, rMin.x, rMax.x, pr.xMin, pr.xMax);
        bool farY = IsFarFromAnchor(rt.anchorMin.y, rt.anchorMax.y, rMin.y, rMax.y, pr.yMin, pr.yMax);

        // Anchors you set by hand (preset, anchor handles, Inspector): never overwritten, unless
        // you move the element far enough away from them or reset it to auto (context menu).
        if (manual.Contains(rt))
        {
            if (!farX && !farY)
                return;
            manual.Remove(rt);
            SaveManual();
        }

        // An axis that already has center anchors is respected as long as you don't move far enough away.
        AxisMode modeX = IsCentered(rt.anchorMin.x, rt.anchorMax.x) && !farX ? AxisMode.Locked : GetAxisMode(rMin.x, rMax.x, pr.xMin, pr.xMax);
        AxisMode modeY = IsCentered(rt.anchorMin.y, rt.anchorMax.y) && !farY ? AxisMode.Locked : GetAxisMode(rMin.y, rMax.y, pr.yMin, pr.yMax);

        // A lone wall (no wall/stretch/center on the other axis) doesn't count: corners only.
        bool xWall = modeX == AxisMode.Lo || modeX == AxisMode.Hi;
        bool yWall = modeY == AxisMode.Lo || modeY == AxisMode.Hi;
        if (xWall && modeY == AxisMode.None) modeX = AxisMode.None;
        if (yWall && modeX == AxisMode.None) modeY = AxisMode.None;

        AxisAnchors(modeX, rt.anchorMin.x, rt.anchorMax.x, rMin.x, rMax.x, pr.xMin, pr.xMax, out float minX, out float maxX);
        AxisAnchors(modeY, rt.anchorMin.y, rt.anchorMax.y, rMin.y, rMax.y, pr.yMin, pr.yMax, out float minY, out float maxY);
        var min = new Vector2(minX, minY);
        var max = new Vector2(maxX, maxY);

        // Offsets that keep the current rect exactly with those anchors.
        Vector2 anchorMinPos = new Vector2(pr.xMin + min.x * pr.width, pr.yMin + min.y * pr.height);
        Vector2 anchorMaxPos = new Vector2(pr.xMin + max.x * pr.width, pr.yMin + max.y * pr.height);
        Vector2 offMin = rMin - anchorMinPos;
        Vector2 offMax = rMax - anchorMaxPos;

        bool alreadyDone =
            (rt.anchorMin - min).sqrMagnitude < Epsilon * Epsilon &&
            (rt.anchorMax - max).sqrMagnitude < Epsilon * Epsilon &&
            (rt.offsetMin - offMin).sqrMagnitude < 0.0001f &&
            (rt.offsetMax - offMax).sqrMagnitude < 0.0001f;
        if (alreadyDone)
            return;

        bool anchorsChanged =
            (rt.anchorMin - min).sqrMagnitude >= Epsilon * Epsilon ||
            (rt.anchorMax - max).sqrMagnitude >= Epsilon * Epsilon;

        Undo.RecordObject(rt, "Auto Anchors");
        applied[rt] = (min, max, EditorApplication.timeSinceStartup);
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;

        // Notify only if the anchors really changed, and don't repeat the same notice
        // in a row unless it is a different object.
        if (anchorsChanged)
        {
            string description = Describe(modeX, modeY);
            if (rt != lastNotifiedObject || description != lastNotifiedText)
                UGUIEnhancerNotify.Show("Anchors: " + description);
            lastNotifiedObject = rt;
            lastNotifiedText = description;
        }
    }

    static RectTransform lastNotifiedObject;
    static string lastNotifiedText;

    // --- Excluding specific objects ---

    [MenuItem("CONTEXT/RectTransform/uGUI Enhancer: Exclude or Include in Auto Anchors")]
    static void ToggleExcludeContext(MenuCommand command) => ToggleExclude((RectTransform)command.context);

    public static bool IsExcluded(RectTransform rt) => rt != null && rt.TryGetComponent<NoAutoAnchors>(out _);

    public static void ToggleExclude(RectTransform rt)
    {
        if (rt == null)
            return;

        if (rt.TryGetComponent<NoAutoAnchors>(out var marker))
        {
            Undo.DestroyObjectImmediate(marker);
            UGUIEnhancerNotify.Show($"{rt.name}: included in Auto Anchors");
        }
        else
        {
            Undo.AddComponent<NoAutoAnchors>(rt.gameObject);
            UGUIEnhancerNotify.Show($"{rt.name}: excluded from Auto Anchors");
        }
    }

    // --- Readable description of the anchors set ---

    static string Describe(AxisMode x, AxisMode y)
    {
        if (x == AxisMode.None && y == AxisMode.None)
            return "around the object";

        bool xWall = x == AxisMode.Lo || x == AxisMode.Hi;
        bool yWall = y == AxisMode.Lo || y == AxisMode.Hi;
        if (xWall && yWall)
            return $"{(y == AxisMode.Hi ? "top" : "bottom")}-{(x == AxisMode.Hi ? "right" : "left")} corner";

        return $"{DescribeAxis(x, "horizontal", "left", "right")}, {DescribeAxis(y, "vertical", "bottom", "top")}";
    }

    static string DescribeAxis(AxisMode mode, string axis, string lo, string hi)
    {
        switch (mode)
        {
            case AxisMode.Lo: return "anchored " + lo;
            case AxisMode.Hi: return "anchored " + hi;
            case AxisMode.Stretch: return axis + " stretch";
            case AxisMode.Locked: return axis + " centered";
            default: return axis + " proportional";
        }
    }

    enum AxisMode { None, Lo, Hi, Stretch, Locked }

    // The element's center is far from its anchor point (or from the anchor range, for stretched anchors).
    static bool IsFarFromAnchor(float anchorLo, float anchorHi, float lo, float hi, float parentLo, float parentHi)
    {
        float size = parentHi - parentLo;
        float loPos = parentLo + anchorLo * size;
        float hiPos = parentLo + anchorHi * size;
        float center = (lo + hi) * 0.5f;
        float distance = center < loPos ? loPos - center : center > hiPos ? center - hiPos : 0f;
        return distance > UGUIEnhancerSettings.BreakAwayFraction * size;
    }


    static bool IsCentered(float anchorLo, float anchorHi)
    {
        return Mathf.Abs(anchorLo - 0.5f) < Epsilon && Mathf.Abs(anchorHi - 0.5f) < Epsilon;
    }

    // Priority: touches both walls (stretch) > one wall > nothing.
    static AxisMode GetAxisMode(float lo, float hi, float parentLo, float parentHi)
    {
        bool touchLo = Mathf.Abs(lo - parentLo) <= UGUIEnhancerSettings.WallTolerance;
        bool touchHi = Mathf.Abs(hi - parentHi) <= UGUIEnhancerSettings.WallTolerance;
        if (touchLo && touchHi) return AxisMode.Stretch;
        if (touchLo) return AxisMode.Lo;
        if (touchHi) return AxisMode.Hi;
        return AxisMode.None;
    }

    static void AxisAnchors(AxisMode mode, float curAnchorLo, float curAnchorHi, float lo, float hi, float parentLo, float parentHi, out float anchorLo, out float anchorHi)
    {
        float size = parentHi - parentLo;
        switch (mode)
        {
            case AxisMode.Locked: anchorLo = curAnchorLo; anchorHi = curAnchorHi; break;
            case AxisMode.Stretch: anchorLo = 0f; anchorHi = 1f; break;
            case AxisMode.Lo: anchorLo = anchorHi = 0f; break;
            case AxisMode.Hi: anchorLo = anchorHi = 1f; break;
            default: anchorLo = (lo - parentLo) / size; anchorHi = (hi - parentLo) / size; break;
        }
    }
}

// ------------- Game View resolution -------------

/// <summary>Brief notices in the Scene View (Unity's native notification).</summary>
public static class UGUIEnhancerNotify
{
    public static void Show(string text)
    {
        foreach (SceneView view in SceneView.sceneViews)
            view.ShowNotification(new GUIContent(text), 1.5);
    }
}

/// <summary>
/// uGUI Enhancer: changes the Game View resolution without opening its dropdown.
/// The presets are the Game View's own size list (the Play Mode one), so
/// any resolution you add there shows up here and vice versa.
/// </summary>
public static class GameViewResolution
{
    public struct Preset
    {
        public string name;
        public int width, height;
        public Preset(string name, int w, int h) { this.name = name; width = w; height = h; }
        public string Label => string.IsNullOrEmpty(name) ? $"{width}x{height}" : $"{name}  {width}x{height}";
    }

    // Added once to the Game View list if missing.
    static readonly Preset[] Defaults =
    {
        new Preset("Full HD", 1920, 1080),
        new Preset("HD", 1280, 720),
        new Preset("QHD", 2560, 1440),
        new Preset("4K", 3840, 2160),
        new Preset("Ultrawide", 3440, 1440),
        new Preset("Mobile 16:9 portrait", 1080, 1920),
        new Preset("Mobile 720 portrait", 720, 1280),
        new Preset("iPhone portrait", 1170, 2532),
        new Preset("iPhone landscape", 2532, 1170),
        new Preset("iPad landscape", 2048, 1536),
        new Preset("iPad portrait", 1536, 2048),
    };

    const string SeededKey = "UGUIEnhancer.SeededResolutions";

    static readonly BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly Type GameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
    static readonly Type SizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
    static readonly Type SizeType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSize");
    static readonly Type SizeTypeEnum = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeType");

    [InitializeOnLoadMethod]
    static void SeedOnce()
    {
        if (EditorPrefs.GetBool(SeededKey, false))
            return;
        // Flag as done only if they were added correctly; otherwise retry on the next reload.
        EditorApplication.delayCall += () =>
        {
            if (TryAddDefaults())
                EditorPrefs.SetBool(SeededKey, true);
        };
    }

    static bool TryAddDefaults()
    {
        try
        {
            object group = GetGroup();
            foreach (var p in Defaults)
                if (FindIndex(group, p.width, p.height) < 0)
                    AddSize(group, p.width, p.height, p.name);
            SaveSizes();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("uGUI Enhancer: could not add the resolutions. " + e.Message);
            return false;
        }
    }

    /// <summary>Fixed-resolution sizes from the Game View list.</summary>
    public static List<Preset> GetPresets()
    {
        var list = new List<Preset>();
        try
        {
            object group = GetGroup();
            Type gt = group.GetType();
            int count = (int)gt.GetMethod("GetTotalCount", All).Invoke(group, null);
            var get = gt.GetMethod("GetGameViewSize", All);
            for (int i = 0; i < count; i++)
            {
                object s = get.Invoke(group, new object[] { i });
                if (Convert.ToInt32(SizeType.GetProperty("sizeType", All).GetValue(s)) != 1)
                    continue; // aspect ratios: not resolutions
                string name = SizeType.GetProperty("baseText", All)?.GetValue(s) as string ?? "";
                int w = (int)SizeType.GetProperty("width", All).GetValue(s);
                int h = (int)SizeType.GetProperty("height", All).GetValue(s);
                list.Add(new Preset(name, w, h));
            }
        }
        catch (Exception) { }

        if (list.Count == 0)
            list.AddRange(Defaults);
        return list;
    }

    /// <summary>"1920 x 1080 [Full HD]": the size plus the name it has in the Game View list, if any.</summary>
    public static string Describe(Vector2Int size)
    {
        string text = $"{size.x} x {size.y}";
        foreach (var p in GetPresets())
        {
            // "UI" is only the placeholder name given to sizes added by the tool, so it says nothing.
            if (p.width == size.x && p.height == size.y && !string.IsNullOrEmpty(p.name) && p.name != "UI")
                return $"{text} [{p.name}]";
        }
        return text;
    }

    public static Vector2Int Current
    {
        get
        {
            // Read the size selected in the Game View; Handles.GetMainGameViewSize goes stale if the view doesn't repaint.
            try
            {
                var views = Resources.FindObjectsOfTypeAll(GameViewType);
                if (views.Length > 0)
                {
                    object size = GameViewType.GetProperty("currentGameViewSize", All).GetValue(views[0]);
                    int w = (int)SizeType.GetProperty("width", All).GetValue(size);
                    int h = (int)SizeType.GetProperty("height", All).GetValue(size);
                    if (w > 0 && h > 0)
                        return new Vector2Int(w, h);
                }
            }
            catch (Exception) { }

            Vector2 s = Handles.GetMainGameViewSize();
            return new Vector2Int(Mathf.RoundToInt(s.x), Mathf.RoundToInt(s.y));
        }
    }

    public static void Set(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        try
        {
            Vector2Int before = Current;
            object group = GetGroup();
            int index = FindIndex(group, width, height);
            if (index < 0)
            {
                AddSize(group, width, height, "UI");
                SaveSizes();
                index = FindIndex(group, width, height);
            }

            var gameView = GetGameView();
            // Same method the Game View dropdown calls: it applies the size, not just the selected index.
            var callback = GameViewType.GetMethod("SizeSelectionCallback", All);
            if (callback != null)
                callback.Invoke(gameView, new object[] { index, null });
            else
                GameViewType.GetProperty("selectedSizeIndex", All).SetValue(gameView, index);
            gameView.Repaint();
            UGUIEnhancerNotify.Show($"Resolution: {width} x {height}");

            if (before.x != width || before.y != height)
                Last = before;
        }
        catch (Exception e)
        {
            Debug.LogWarning("uGUI Enhancer: could not change the Game View resolution. " + e.Message);
        }
    }

    // --- Base and last resolution ---

    static string BaseKey => "UGUIEnhancer.Base." + Application.dataPath;
    const string LastKey = "UGUIEnhancer.Last";

    static Vector2Int Load(string stored)
    {
        var parts = (stored ?? "").Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)
            ? new Vector2Int(w, h)
            : Vector2Int.zero;
    }

    /// <summary>Resolution you set as base (kept per project). (0, 0) when none is set.</summary>
    public static Vector2Int Base
    {
        get => Load(EditorPrefs.GetString(BaseKey, ""));
        private set => EditorPrefs.SetString(BaseKey, $"{value.x},{value.y}");
    }

    /// <summary>The resolution you were on before the current one (for this editor session).</summary>
    public static Vector2Int Last
    {
        get => Load(SessionState.GetString(LastKey, ""));
        private set => SessionState.SetString(LastKey, $"{value.x},{value.y}");
    }

    public static void SetCurrentAsBase()
    {
        Base = Current;
        UGUIEnhancerNotify.Show($"Base resolution: {Base.x} x {Base.y}");
    }

    [Shortcut("uGUI Enhancer/Go To Base Resolution", KeyCode.B, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
    public static void GoToBase()
    {
        Vector2Int b = Base;
        if (b.x > 0 && b.y > 0)
            Set(b.x, b.y);
        else
            UGUIEnhancerNotify.Show("No base resolution set");
    }

    [Shortcut("uGUI Enhancer/Go To Last Resolution", KeyCode.DownArrow, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
    public static void GoToLast()
    {
        Vector2Int l = Last;
        if (l.x > 0 && l.y > 0)
            Set(l.x, l.y);
        else
            UGUIEnhancerNotify.Show("No previous resolution yet");
    }

    public static void Swap()
    {
        Vector2Int c = Current;
        Set(c.y, c.x);

        // Keep the Next/Previous filter in step with the new orientation.
        if (Orientation != OrientationFilter.All)
            Orientation = c.y >= c.x ? OrientationFilter.Landscape : OrientationFilter.Portrait;
    }

    // --- Orientation filter for Next / Previous ---

    public enum OrientationFilter { All, Landscape, Portrait }

    const string OrientationKey = "UGUIEnhancer.Orientation";

    /// <summary>Which resolutions Next / Previous go through. Landscape skips portrait ones and vice versa.</summary>
    public static OrientationFilter Orientation
    {
        get => (OrientationFilter)EditorPrefs.GetInt(OrientationKey, (int)OrientationFilter.All);
        set => EditorPrefs.SetInt(OrientationKey, (int)value);
    }

    static bool MatchesOrientation(Preset p)
    {
        switch (Orientation)
        {
            case OrientationFilter.Landscape: return p.width >= p.height;
            case OrientationFilter.Portrait: return p.height >= p.width;
            default: return true;
        }
    }

    [Shortcut("uGUI Enhancer/Cycle Orientation Filter", KeyCode.O, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
    static void CycleOrientation()
    {
        Orientation = (OrientationFilter)(((int)Orientation + 1) % 3);
        UGUIEnhancerNotify.Show("Next/Previous: " + Orientation);
    }

    /// <summary>Moves to the next (1) or previous (-1) resolution in the list (respecting the orientation filter).</summary>
    public static void Cycle(int direction)
    {
        var list = GetPresets().FindAll(MatchesOrientation);
        if (list.Count == 0)
            return;

        Vector2Int c = Current;
        int i = list.FindIndex(p => p.width == c.x && p.height == c.y);
        i = i < 0 ? (direction > 0 ? 0 : list.Count - 1) : (i + direction + list.Count) % list.Count;
        Set(list[i].width, list[i].height);
    }

    [Shortcut("uGUI Enhancer/Next Resolution", KeyCode.RightArrow, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
    static void Next() => Cycle(1);

    [Shortcut("uGUI Enhancer/Previous Resolution", KeyCode.LeftArrow, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
    static void Previous() => Cycle(-1);

    [Shortcut("uGUI Enhancer/Rotate Resolution", KeyCode.UpArrow, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
    static void Rotate() => Swap();

    // --- Access to the Game View's internal list ---

    static object GetSizesInstance()
    {
        return typeof(ScriptableSingleton<>).MakeGenericType(SizesType).GetProperty("instance", All).GetValue(null);
    }

    static object GetGroup()
    {
        object instance = GetSizesInstance();
        object groupType = SizesType.GetProperty("currentGroupType", All).GetValue(instance);
        return SizesType.GetMethod("GetGroup", All).Invoke(instance, new[] { groupType });
    }

    static void AddSize(object group, int width, int height, string name)
    {
        object size = Activator.CreateInstance(SizeType, All, null,
            new object[] { Enum.ToObject(SizeTypeEnum, 1), width, height, name }, null);
        group.GetType().GetMethod("AddCustomSize", All).Invoke(group, new[] { size });
    }

    static void SaveSizes()
    {
        SizesType.GetMethod("SaveToHDD", All)?.Invoke(GetSizesInstance(), null);
    }

    static int FindIndex(object group, int width, int height)
    {
        Type gt = group.GetType();
        int count = (int)gt.GetMethod("GetTotalCount", All).Invoke(group, null);
        var get = gt.GetMethod("GetGameViewSize", All);
        for (int i = 0; i < count; i++)
        {
            object s = get.Invoke(group, new object[] { i });
            int type = Convert.ToInt32(SizeType.GetProperty("sizeType", All).GetValue(s));
            int w = (int)SizeType.GetProperty("width", All).GetValue(s);
            int h = (int)SizeType.GetProperty("height", All).GetValue(s);
            if (type == 1 && w == width && h == height)
                return i;
        }
        return -1;
    }

    static EditorWindow GetGameView()
    {
        var views = Resources.FindObjectsOfTypeAll(GameViewType);
        if (views.Length > 0)
            return (EditorWindow)views[0];
        return EditorWindow.GetWindow(GameViewType);
    }
}

[Overlay(typeof(SceneView), "uGUI Enhancer", true)]
public class UIResolutionOverlay : Overlay
{
    // Shown when a Canvas, or an object inside one, is selected; hidden when anything else is selected.
    // With nothing selected, it keeps its previous state.
    // If you hide it yourself (Overlay menu, the ` key in the Scene View) the tool stops showing it
    // until you turn it on again from that same menu.
    const string AllowedKey = "UGUIEnhancer.OverlayAllowed";

    bool ready;            // ignore the visibility events fired while the layout is being restored
    bool settingDisplayed; // true while WE change `displayed`, to tell it apart from the user doing it

    public override void OnCreated()
    {
        Selection.selectionChanged += UpdateVisibility;
        displayedChanged += OnDisplayedChanged;
        UpdateVisibility();
        EditorApplication.delayCall += () => ready = true;
    }

    public override void OnWillBeDestroyed()
    {
        Selection.selectionChanged -= UpdateVisibility;
        displayedChanged -= OnDisplayedChanged;
    }

    void OnDisplayedChanged(bool value)
    {
        if (ready && !settingDisplayed)
            EditorPrefs.SetBool(AllowedKey, value);
    }

    void UpdateVisibility()
    {
        var go = Selection.activeGameObject;
        if (go == null)
            return; // nothing selected: keep as it was

        bool show = go.GetComponentInParent<Canvas>(true) != null && EditorPrefs.GetBool(AllowedKey, true);
        if (displayed == show)
            return;

        settingDisplayed = true;
        displayed = show;
        settingDisplayed = false;
    }

    public override VisualElement CreatePanelContent()
    {
        var root = new VisualElement { style = { minWidth = 250, paddingTop = 6, paddingBottom = 6, paddingLeft = 6, paddingRight = 6 } };

        var current = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, marginBottom = 8 } };

        var dropdown = new DropdownField(new List<string>(), 0) { label = null, style = { marginBottom = 6 } };
        dropdown.RegisterValueChangedCallback(evt =>
        {
            var list = GameViewResolution.GetPresets();
            if (dropdown.index >= 0 && dropdown.index < list.Count)
                GameViewResolution.Set(list[dropdown.index].width, list[dropdown.index].height);
            dropdown.SetValueWithoutNotify("Resolutions...");
        });

        var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        var prev = new UnityEngine.UIElements.Button(() => GameViewResolution.Cycle(-1)) { text = "< Previous", style = { flexGrow = 1 } };
        var swap = new UnityEngine.UIElements.Button(GameViewResolution.Swap) { text = "Rotate", style = { flexGrow = 1 } };
        var next = new UnityEngine.UIElements.Button(() => GameViewResolution.Cycle(1)) { text = "Next >", style = { flexGrow = 1 } };
        buttons.Add(prev);
        buttons.Add(swap);
        buttons.Add(next);

        var orientationNames = new List<string> { "All", "Landscape", "Portrait" };
        var orientation = new DropdownField("Next/Prev", orientationNames, (int)GameViewResolution.Orientation) { style = { marginTop = 8 } };
        orientation.RegisterValueChangedCallback(evt => GameViewResolution.Orientation = (GameViewResolution.OrientationFilter)orientation.index);

        var baseButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        var last = new UnityEngine.UIElements.Button(GameViewResolution.GoToLast) { text = "Last", style = { flexGrow = 1 } };
        var goBase = new UnityEngine.UIElements.Button(GameViewResolution.GoToBase) { text = "Base", style = { flexGrow = 2 } };
        var setBase = new UnityEngine.UIElements.Button(GameViewResolution.SetCurrentAsBase) { text = "Set base", style = { flexGrow = 1 } };
        baseButtons.Add(last);
        baseButtons.Add(goBase);
        baseButtons.Add(setBase);

        var autoAnchors = new UnityEngine.UIElements.Toggle("Auto Anchors") { style = { marginBottom = 2 } };
        autoAnchors.RegisterValueChangedCallback(evt => AutoAnchors.Enabled = evt.newValue);

        var exclude = new UnityEngine.UIElements.Toggle("Exclude selected") { style = { marginTop = 2 } };
        exclude.RegisterValueChangedCallback(evt =>
        {
            var rt = Selection.activeTransform as RectTransform;
            if (rt != null && AutoAnchors.IsExcluded(rt) != evt.newValue)
                AutoAnchors.ToggleExclude(rt);
        });

        // A thin line with some air around it to separate the sections.
        VisualElement Separator() => new VisualElement
        {
            style =
            {
                height = 1,
                marginTop = 10,
                marginBottom = 10,
                backgroundColor = new Color(1f, 1f, 1f, 0.12f),
            }
        };

        root.Add(current);
        root.Add(dropdown);
        root.Add(buttons);
        root.Add(orientation);
        root.Add(Separator());
        root.Add(baseButtons);
        root.Add(Separator());
        root.Add(autoAnchors);
        root.Add(exclude);

        var advanced = new UnityEngine.UIElements.Button(UIResolutionWindow.OpenAdvanced) { text = "Advanced settings...", style = { marginTop = 8 } };
        root.Add(advanced);

        var shownLabels = new List<string>();

        void Refresh()
        {
            var c = GameViewResolution.Current;
            current.text = GameViewResolution.Describe(c);

            orientation.SetValueWithoutNotify(orientationNames[(int)GameViewResolution.Orientation]);

            var b = GameViewResolution.Base;
            goBase.text = b.x > 0 ? $"Base {b.x}x{b.y}" : "Base (not set)";
            goBase.SetEnabled(b.x > 0);
            var l = GameViewResolution.Last;
            last.tooltip = l.x > 0 ? $"Back to {l.x} x {l.y}" : "No previous resolution yet";
            last.SetEnabled(l.x > 0);

            // The list stays in sync with the Game View's.
            var labels = new List<string>();
            foreach (var p in GameViewResolution.GetPresets())
                labels.Add(p.Label);
            bool changed = labels.Count != shownLabels.Count;
            for (int i = 0; !changed && i < labels.Count; i++)
                changed = labels[i] != shownLabels[i];
            if (changed)
            {
                shownLabels = labels;
                dropdown.choices = labels;
                dropdown.SetValueWithoutNotify("Resolutions...");
            }

            autoAnchors.SetValueWithoutNotify(AutoAnchors.Enabled);

            var rt = Selection.activeTransform as RectTransform;
            exclude.SetEnabled(rt != null);
            exclude.SetValueWithoutNotify(rt != null && AutoAnchors.IsExcluded(rt));
        }
        Refresh();
        root.schedule.Execute(Refresh).Every(500);
        return root;
    }
}

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
        EditorGUILayout.LabelField("Resolutions (Game View list)", EditorStyles.boldLabel);
        foreach (var p in GameViewResolution.GetPresets())
        {
            bool active = p.width == cur.x && p.height == cur.y;
            using (new EditorGUI.DisabledScope(active))
                if (GUILayout.Button(p.Label))
                    GameViewResolution.Set(p.width, p.height);
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

        DrawAdvanced();

        GUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    // --- Advanced settings ---

    const string AdvancedOpenKey = "UGUIEnhancer.AdvancedOpen";

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

        if (GUILayout.Button("Reset to defaults"))
            UGUIEnhancerSettings.ResetToDefaults();

        EditorGUI.indentLevel--;
    }
}
