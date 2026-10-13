using UnityEditor;
using UnityEngine;

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

    // --- GameObjects Auto Anchors must never change ---

    public static bool IsRuleEnabled(UGUIEnhancerExclusions.Rule rule)
    {
        return EditorPrefs.GetBool(Key("Exclude." + rule.id), rule.enabledByDefault);
    }

    public static void SetRuleEnabled(UGUIEnhancerExclusions.Rule rule, bool enabled)
    {
        EditorPrefs.SetBool(Key("Exclude." + rule.id), enabled);
    }

    /// <summary>Name patterns, one per line (* is a wildcard). Elements whose name matches are never changed.</summary>
    public static string ExcludedNames
    {
        get => EditorPrefs.GetString(Key("ExcludedNames"), "");
        set => EditorPrefs.SetString(Key("ExcludedNames"), value ?? "");
    }

    public static void ResetToDefaults()
    {
        EditorPrefs.DeleteKey(Key("WallTolerance"));
        EditorPrefs.DeleteKey(Key("BreakAwayFraction"));
        EditorPrefs.DeleteKey(Key("IdleDelay"));
        EditorPrefs.DeleteKey(Key("ExcludedNames"));
        foreach (var rule in UGUIEnhancerExclusions.Rules)
            EditorPrefs.DeleteKey(Key("Exclude." + rule.id));
    }
}
