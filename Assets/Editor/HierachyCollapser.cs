using UnityEngine;
using UnityEditor;
using System.Reflection;
using System.Collections.Generic;

public class HierarchyDeepCollapser
{
    // Prioridad -100 lo coloca arriba del todo en el menú GameObject
    [MenuItem("GameObject/Collapse Absolute All", false, -100)]
    public static void DeepCollapse()
    {
        var windowType = System.Type.GetType("UnityEditor.SceneHierarchyWindow,UnityEditor");
        var hierarchy = EditorWindow.GetWindow(windowType);

        MethodInfo setExpandedMethod = hierarchy.GetType().GetMethod("SetExpanded",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(int), typeof(bool) }, null);

        if (setExpandedMethod != null)
        {
 
            Transform[] allTransforms = GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (Transform t in allTransforms)
            {
                setExpandedMethod.Invoke(hierarchy, new object[] { t.gameObject.GetInstanceID(), false });
            }
        }
    }
}