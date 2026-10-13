using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameObjects that Auto Anchors must never change. A list of categories (each one a checkbox in
/// Advanced, with a sensible default) plus a list of name patterns. The `No Auto Anchors` marker
/// component is always honored on top of this.
/// </summary>
public static class UGUIEnhancerExclusions
{
    public class Rule
    {
        public string id;
        public string label;
        public string tooltip;
        public bool enabledByDefault;
        public Func<RectTransform, bool> matches;
    }

    /// <summary>The categories shown as checkboxes, in the order they are shown.</summary>
    public static readonly Rule[] Rules =
    {
        new Rule
        {
            id = "LayoutGroupChildren", enabledByDefault = true,
            label = "Children of a Layout Group",
            tooltip = "A Layout Group positions and sizes its children, so their anchors are not touched.",
            matches = rt => rt.parent != null && rt.parent.TryGetComponent<LayoutGroup>(out _),
        },
        new Rule
        {
            id = "SelfSizeControllers", enabledByDefault = true,
            label = "Elements with Content Size Fitter / Aspect Ratio Fitter",
            tooltip = "These components resize the element on their own.",
            matches = rt => rt.TryGetComponent<ContentSizeFitter>(out _) || rt.TryGetComponent<AspectRatioFitter>(out _),
        },
        new Rule
        {
            id = "ScrollViewParts", enabledByDefault = true,
            label = "Viewport and Content of a Scroll View",
            tooltip = "A Scroll View expects its Viewport and Content to keep their own anchors.",
            matches = IsScrollViewPart,
        },
        new Rule
        {
            id = "WidgetParts", enabledByDefault = true,
            label = "Internal parts of Sliders, Scrollbars, Dropdowns, Toggles and Input Fields",
            tooltip = "Handle, Fill Area, Checkmark, Template, Text Area... are laid out by their widget.",
            matches = IsWidgetPart,
        },
        new Rule
        {
            id = "Texts", enabledByDefault = false,
            label = "Text elements (Text, TextMeshPro)",
            tooltip = "Never change the anchors of text elements.",
            matches = rt => HasComponentOfType(rt, "Text", "TMP_Text"),
        },
        new Rule
        {
            id = "Images", enabledByDefault = false,
            label = "Image and Raw Image elements",
            tooltip = "Never change the anchors of images.",
            matches = rt => HasComponentOfType(rt, "Image", "RawImage"),
        },
        new Rule
        {
            id = "Buttons", enabledByDefault = false,
            label = "Buttons",
            tooltip = "Never change the anchors of buttons.",
            matches = rt => HasComponentOfType(rt, "Button"),
        },
        new Rule
        {
            id = "Masks", enabledByDefault = false,
            label = "Elements with a Mask or Rect Mask 2D",
            tooltip = "Never change the anchors of masking elements.",
            matches = rt => HasComponentOfType(rt, "Mask", "RectMask2D"),
        },
    };

    /// <summary>True when Auto Anchors must leave this element alone (not counting the marker component).</summary>
    public static bool IsExcluded(RectTransform rt)
    {
        foreach (var rule in Rules)
            if (UGUIEnhancerSettings.IsRuleEnabled(rule) && rule.matches(rt))
                return true;

        return MatchesExcludedName(rt.name);
    }

    // --- Names ---

    /// <summary>One pattern per line; * matches any text; case-insensitive; the whole name must match.</summary>
    public static bool MatchesExcludedName(string objectName)
    {
        string patterns = UGUIEnhancerSettings.ExcludedNames;
        if (string.IsNullOrWhiteSpace(patterns))
            return false;

        foreach (string line in patterns.Split('\n'))
        {
            string pattern = line.Trim();
            if (pattern.Length == 0)
                continue;

            string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            if (Regex.IsMatch(objectName, regex, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }

    // --- Rule helpers ---

    static bool IsScrollViewPart(RectTransform rt)
    {
        if (rt.parent == null)
            return false;

        var scroll = rt.parent.GetComponentInParent<ScrollRect>();
        return scroll != null && (scroll.viewport == rt || scroll.content == rt);
    }

    static readonly string[] WidgetTypes = { "Slider", "Scrollbar", "Dropdown", "TMP_Dropdown", "Toggle", "InputField", "TMP_InputField" };

    // True for anything inside a Slider, Scrollbar, Dropdown, Toggle or Input Field (not the widget itself).
    static bool IsWidgetPart(RectTransform rt)
    {
        for (Transform ancestor = rt.parent; ancestor != null; ancestor = ancestor.parent)
        {
            foreach (var component in ancestor.GetComponents<Component>())
            {
                if (component == null)
                    continue;
                if (Array.IndexOf(WidgetTypes, component.GetType().Name) >= 0)
                    return true;
            }
        }
        return false;
    }

    // Compares by type name (including base classes), so TextMeshPro works without referencing its assembly.
    static bool HasComponentOfType(RectTransform rt, params string[] typeNames)
    {
        foreach (var component in rt.GetComponents<Component>())
        {
            if (component == null)
                continue;
            for (Type type = component.GetType(); type != null; type = type.BaseType)
                if (Array.IndexOf(typeNames, type.Name) >= 0)
                    return true;
        }
        return false;
    }
}
