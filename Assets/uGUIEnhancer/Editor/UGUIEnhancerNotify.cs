using UnityEditor;
using UnityEngine;

/// <summary>Brief notices in the Scene View (Unity's native notification).</summary>
public static class UGUIEnhancerNotify
{
    // What was shown last and how many notices there have been (used by the tests).
    internal static string LastText { get; private set; }
    internal static int ShownCount { get; private set; }

    public static void Show(string text)
    {
        LastText = text;
        ShownCount++;

        foreach (SceneView view in SceneView.sceneViews)
            view.ShowNotification(new GUIContent(text), 1.5);
    }
}
