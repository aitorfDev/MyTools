// Assets/Editor/SceneComponentFinderWindow.cs

using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;

public class SceneComponentFinderWindow : EditorWindow
{
    private Vector2 leftScroll;
    private Vector2 rightScroll;

    // LEFT FILTER
    private string searchFilter = "";

    // RIGHT FILTER
    private string resultSearchFilter = "";

    private List<ComponentInfo> componentInfos = new List<ComponentInfo>();

    private Type selectedType;
    private GameObject[] results = new GameObject[0];

    [MenuItem("Tools/Scene Component Finder %#SPACE")]
    public static void ShowWindow()
    {
        GetWindow<SceneComponentFinderWindow>("Scene Component Finder");
    }

    private void OnEnable()
    {
        RefreshData();
    }

    private void OnHierarchyChange()
    {
        RefreshData();

        if (selectedType != null)
            FindObjects(selectedType);

        Repaint();
    }

    private void RefreshData()
    {
        var components = Resources.FindObjectsOfTypeAll<Component>()
            .Where(c => c != null && c.gameObject.scene.IsValid());

        componentInfos = components
            .GroupBy(c => c.GetType())
            .Select(g => new ComponentInfo
            {
                type = g.Key,
                count = g.Count()
            })
            .OrderByDescending(c => c.count)
            .ThenBy(c => c.type.Name)
            .ToList();
    }

    private void OnGUI()
    {
        GUILayout.Space(5);
        GUILayout.Label("Scene Component Finder", EditorStyles.boldLabel);

        searchFilter = EditorGUILayout.TextField("Search", searchFilter);

        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();

        DrawComponentList();

        DrawResultsPanel();

        EditorGUILayout.EndHorizontal();
    }

    // ---------------- LEFT PANEL ----------------
    private void DrawComponentList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.4f));

        GUILayout.Label("Scene Components", EditorStyles.boldLabel);

        if (GUILayout.Button("Refresh"))
        {
            RefreshData();
        }

        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        var filtered = componentInfos
            .Where(c => string.IsNullOrEmpty(searchFilter) ||
                        c.type.Name.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        foreach (var info in filtered)
        {
            bool isSelected = selectedType == info.type;

            Rect rect = EditorGUILayout.GetControlRect(false, 22);

            if (isSelected)
            {
                EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.15f));
            }

            // Detect click on the entire line
            if (Event.current.type == EventType.MouseDown &&
                rect.Contains(Event.current.mousePosition))
            {
                selectedType = info.type;

                // RESET RIGHT SEARCH
                resultSearchFilter = "";
                
                FindObjects(selectedType);

                Repaint();
                Event.current.Use();
            }

            Rect leftRect = rect;
            leftRect.x += 5;
            leftRect.width -= 40;

            GUIStyle labelStyle = new GUIStyle(EditorStyles.label);

            if (isSelected)
            {
                labelStyle.normal.textColor = Color.white;
                labelStyle.fontStyle = FontStyle.Bold;
            }

            GUI.Label(leftRect, info.type.Name, labelStyle);

            // Count right
            Rect rightRect = rect;
            rightRect.x = rect.xMax - 35;
            rightRect.width = 30;

            GUI.Label(rightRect, info.count.ToString());
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    // ---------------- RIGHT PANEL ----------------
    private void DrawResultsPanel()
    {
        EditorGUILayout.BeginVertical();

        GUILayout.Label("Found Objects", EditorStyles.boldLabel);

        GUILayout.Label($"Type: {(selectedType != null ? selectedType.Name : "none")}");

        // SEARCH BAR RIGHT PANEL
        resultSearchFilter = EditorGUILayout.TextField("Search Object", resultSearchFilter);

        var filteredResults = results
            .Where(go =>
                string.IsNullOrEmpty(resultSearchFilter) ||
                go.name.IndexOf(resultSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();

        GUILayout.Label($"Result: {filteredResults.Length}");

        if (GUILayout.Button("Select All"))
        {
            Selection.objects = filteredResults;
        }

        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        foreach (var obj in filteredResults)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.ObjectField(obj, typeof(GameObject), true);

            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                Selection.activeGameObject = obj;
                EditorGUIUtility.PingObject(obj);
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    // ---------------- LOGIC ----------------
    private void FindObjects(Type type)
    {
        if (type == null) return;

        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        results = allObjects
            .Where(go =>
                go.scene.IsValid() &&
                go.GetComponent(type) != null)
            .ToArray();
    }

    // ---------------- DATA ----------------
    private class ComponentInfo
    {
        public Type type;
        public int count;
    }
}