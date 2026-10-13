using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UI;

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

    internal static readonly HashSet<RectTransform> pending = new HashSet<RectTransform>();
    internal static readonly HashSet<RectTransform> manual = new HashSet<RectTransform>(); // anchors changed by hand: leave alone
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

    internal static void SaveManual()
    {
        var ids = new List<string>();
        foreach (var rt in manual)
            if (rt != null)
                ids.Add(GlobalObjectId.GetGlobalObjectIdSlow(rt).ToString());
        SessionState.SetString(ManualKey, string.Join(";", ids));
    }

    internal static void LoadManual()
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

    internal static void Flush()
    {
        if (pending.Count == 0)
            return;

        var items = new List<RectTransform>(pending);
        pending.Clear();

        // Several objects moved together (multi-selection): one undo step and one notice for the whole batch.
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        batch.Clear();
        batching = true;
        try
        {
            foreach (var rt in items)
                if (rt != null)
                    Apply(rt);
        }
        finally
        {
            batching = false;
        }

        if (batch.Count == 0)
            return;

        Undo.SetCurrentGroupName("Auto Anchors");
        Undo.CollapseUndoOperations(undoGroup);

        // Only objects whose anchors really changed get a notice (the others just had their offsets adjusted).
        var changed = batch.FindAll(b => b.description != null);
        if (changed.Count == 1)
            NotifyAnchors(changed[0].rt, changed[0].description);
        else if (changed.Count > 1)
            UGUIEnhancerNotify.Show($"Anchors updated on {changed.Count} objects");
        batch.Clear();
    }

    static bool batching;
    static readonly List<(RectTransform rt, string description)> batch = new List<(RectTransform rt, string description)>();

    // Don't repeat the same notice in a row unless it is a different object.
    static void NotifyAnchors(RectTransform rt, string description)
    {
        if (rt != lastNotifiedObject || description != lastNotifiedText)
            UGUIEnhancerNotify.Show("Anchors: " + description);
        lastNotifiedObject = rt;
        lastNotifiedText = description;
    }

    internal static void Apply(RectTransform rt)
    {
        var parent = rt.parent as RectTransform;
        if (parent == null)
            return; // root canvas or object without a UI parent

        // Explicitly excluded by the user.
        if (rt.TryGetComponent<NoAutoAnchors>(out _))
            return;

        // Categories and names the user chose not to touch (Advanced), e.g. children of a Layout Group.
        if (UGUIEnhancerExclusions.IsExcluded(rt))
            return;

        // Axis-aligned anchors: with Z rotation the result would not be exact.
        if (Mathf.Abs(Mathf.DeltaAngle(0f, rt.localEulerAngles.z)) > 0.01f &&
            Mathf.Abs(Mathf.DeltaAngle(180f, rt.localEulerAngles.z)) > 0.01f)
            return;

        Rect pr = parent.rect;
        if (pr.width < Epsilon || pr.height < Epsilon)
            return;

        // The element's rect in its parent's space, before its own scale and rotation. That is the rect the
        // anchors and offsets describe, so keeping it unchanged keeps the element exactly where it is, whatever
        // its scale, pivot or flip (and whatever the parent's scale or rotation).
        Vector2 pivotPosition = rt.localPosition;
        Rect local = rt.rect;
        Vector2 rMin = pivotPosition + local.min;
        Vector2 rMax = pivotPosition + local.max;

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

        // On a prefab instance this stores the change as an override (Undo alone may not).
        PrefabUtility.RecordPrefabInstancePropertyModifications(rt);

        // Notify only if the anchors really changed.
        if (anchorsChanged)
        {
            string description = Describe(modeX, modeY);
            if (batching)
                batch.Add((rt, description));
            else
                NotifyAnchors(rt, description);
        }
        else if (batching)
        {
            batch.Add((rt, null)); // offsets changed: counts for the undo step, not for the notice
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
