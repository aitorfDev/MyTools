using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

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
    static readonly Type GroupType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeGroup");

    // --- Compatibility check ---
    // Everything here relies on internal Unity APIs, which can change between versions.
    // We check once that they are all present and, if not, switch the resolution tools off
    // with a clear message instead of failing on every click.

    static string unavailableReason;
    static bool checkedApi;

    /// <summary>False when this Unity version doesn't expose the Game View internals the resolution tools need.</summary>
    public static bool Available
    {
        get
        {
            if (!checkedApi)
            {
                checkedApi = true;
                unavailableReason = FindMissingApi();
                if (unavailableReason != null)
                    Debug.LogWarning($"uGUI Enhancer: the resolution tools are disabled because Unity {Application.unityVersion} " +
                                     $"changed the Game View internals ({unavailableReason}). Auto Anchors is not affected.");
            }
            return unavailableReason == null;
        }
    }

    /// <summary>Why the resolution tools are unavailable, or null when they work.</summary>
    public static string UnavailableReason => Available ? null : unavailableReason;

    static string FindMissingApi()
    {
        if (GameViewType == null) return "type GameView";
        if (SizesType == null) return "type GameViewSizes";
        if (SizeType == null) return "type GameViewSize";
        if (SizeTypeEnum == null) return "type GameViewSizeType";
        if (GroupType == null) return "type GameViewSizeGroup";

        if (typeof(ScriptableSingleton<>).MakeGenericType(SizesType).GetProperty("instance", All) == null) return "GameViewSizes.instance";
        if (SizesType.GetProperty("currentGroupType", All) == null) return "GameViewSizes.currentGroupType";
        if (SizesType.GetMethod("GetGroup", All) == null) return "GameViewSizes.GetGroup";
        if (GroupType.GetMethod("GetTotalCount", All) == null) return "GameViewSizeGroup.GetTotalCount";
        if (GroupType.GetMethod("GetGameViewSize", All) == null) return "GameViewSizeGroup.GetGameViewSize";
        if (GroupType.GetMethod("AddCustomSize", All) == null) return "GameViewSizeGroup.AddCustomSize";
        foreach (string property in new[] { "sizeType", "width", "height", "baseText" })
            if (SizeType.GetProperty(property, All) == null) return "GameViewSize." + property;
        if (GameViewType.GetProperty("currentGameViewSize", All) == null) return "GameView.currentGameViewSize";
        if (GameViewType.GetMethod("SizeSelectionCallback", All) == null && GameViewType.GetProperty("selectedSizeIndex", All) == null)
            return "GameView.SizeSelectionCallback";
        return null;
    }

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

    internal static bool TryAddDefaults()
    {
        if (!Available)
            return false;

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
        if (!Available)
            return new List<Preset>(Defaults);
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
        if (!Available)
        {
            UGUIEnhancerNotify.Show("Resolution tools unavailable in this Unity version");
            return;
        }

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
