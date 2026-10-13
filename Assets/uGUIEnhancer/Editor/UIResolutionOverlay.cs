using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

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

        // Shown instead of the resolution controls when this Unity version hides the Game View internals.
        var unavailable = new Label { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.8f, marginBottom = 6, display = DisplayStyle.None } };

        var resolutionGroup = new VisualElement();
        resolutionGroup.Add(current);
        resolutionGroup.Add(dropdown);
        resolutionGroup.Add(buttons);
        resolutionGroup.Add(orientation);
        resolutionGroup.Add(Separator());
        resolutionGroup.Add(baseButtons);

        root.Add(unavailable);
        root.Add(resolutionGroup);
        root.Add(Separator());
        root.Add(autoAnchors);
        root.Add(exclude);

        var advanced = new UnityEngine.UIElements.Button(UIResolutionWindow.OpenAdvanced) { text = "Advanced settings...", style = { marginTop = 8 } };
        root.Add(advanced);

        var shownLabels = new List<string>();

        void Refresh()
        {
            bool available = GameViewResolution.Available;
            resolutionGroup.SetEnabled(available);
            unavailable.style.display = available ? DisplayStyle.None : DisplayStyle.Flex;
            unavailable.text = available ? "" : "Resolution tools unavailable in this Unity version (" + GameViewResolution.UnavailableReason + "). Auto Anchors still works.";

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
