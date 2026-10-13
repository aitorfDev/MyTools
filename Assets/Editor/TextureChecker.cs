using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;

public class TexelDensitySceneTool : EditorWindow
{
    private static Texture2D checkerTexture;

    // 1024 = estándar común de texel density
    private static float referenceResolution = 1024f;

    private static bool checkerApplied = false;

    // Guarda materiales originales
    private static Dictionary<Renderer, Material[]> originalMaterials =
        new Dictionary<Renderer, Material[]>();

    // Materiales temporales creados
    private static List<Material> generatedMaterials =
        new List<Material>();

    private static string duplicatedScenePath = "";

    [MenuItem("Tools/Texel Density Scene Tool")]
    public static void ShowWindow()
    {
        GetWindow<TexelDensitySceneTool>("Texel Density");
    }

    private void OnGUI()
    {
        GUILayout.Space(10);

        EditorGUILayout.LabelField(
            "Texel Density Validation",
            EditorStyles.boldLabel
        );

        GUILayout.Space(10);

        checkerTexture = (Texture2D)EditorGUILayout.ObjectField(
            "Checker Texture",
            checkerTexture,
            typeof(Texture2D),
            false
        );

        referenceResolution = EditorGUILayout.FloatField(
            "Reference Resolution",
            referenceResolution
        );

        GUILayout.Space(15);

        GUI.enabled = !checkerApplied;

        if (GUILayout.Button("Create Validation Scene"))
        {
            CreateValidationScene();
        }

        GUI.enabled = checkerApplied;

        if (GUILayout.Button("Restore Original Materials"))
        {
            RestoreMaterials();
        }

        if (GUILayout.Button("Close Validation Scene"))
        {
            CloseValidationScene();
        }

        GUI.enabled = true;
    }

    // =========================================================
    // MAIN
    // =========================================================

    private static void CreateValidationScene()
    {
        if (checkerTexture == null)
        {
            Debug.LogError("Assign a checker texture.");
            return;
        }

        bool save =
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        if (!save)
            return;

        DuplicateCurrentScene();

        ApplyCheckerMaterials();

        ConfigureSceneView();

        checkerApplied = true;

        Debug.Log("Texel Density Validation Scene Created.");
    }

    // =========================================================
    // DUPLICATE SCENE
    // =========================================================

    private static void DuplicateCurrentScene()
    {
        Scene currentScene = SceneManager.GetActiveScene();

        string currentScenePath = currentScene.path;

        string directory =
            Path.GetDirectoryName(currentScenePath);

        string sceneName =
            Path.GetFileNameWithoutExtension(currentScenePath);

        duplicatedScenePath =
            Path.Combine(
                directory,
                sceneName + "_TEXELCHECK.unity"
            );

        duplicatedScenePath =
            duplicatedScenePath.Replace("\\", "/");

        if (File.Exists(duplicatedScenePath))
        {
            AssetDatabase.DeleteAsset(duplicatedScenePath);
        }

        bool copied =
            AssetDatabase.CopyAsset(
                currentScenePath,
                duplicatedScenePath
            );

        if (!copied)
        {
            Debug.LogError("Failed to duplicate scene.");
            return;
        }

        AssetDatabase.Refresh();

        EditorSceneManager.OpenScene(
            duplicatedScenePath,
            OpenSceneMode.Single
        );
    }

    // =========================================================
    // APPLY CHECKER
    // =========================================================

    private static void ApplyCheckerMaterials()
    {
        originalMaterials.Clear();
        generatedMaterials.Clear();

        Renderer[] renderers =
            Object.FindObjectsOfType<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            Material[] originalMats =
                renderer.sharedMaterials;

            originalMaterials.Add(renderer, originalMats);

            Material[] checkerMats =
                new Material[originalMats.Length];

            for (int i = 0; i < originalMats.Length; i++)
            {
                Material originalMat = originalMats[i];

                if (originalMat == null)
                    continue;

                string textureProperty =
                    GetTextureProperty(originalMat);

                Texture originalTexture = null;

                Vector2 originalScale = Vector2.one;

                if (!string.IsNullOrEmpty(textureProperty))
                {
                    originalTexture =
                        originalMat.GetTexture(textureProperty);

                    originalScale =
                        originalMat.GetTextureScale(textureProperty);
                }

                int textureWidth = 1024;

                if (originalTexture is Texture2D tex2D)
                {
                    textureWidth = tex2D.width;
                }

                float densityMultiplier =
                    textureWidth / referenceResolution;

                Vector2 checkerScale =
                    originalScale * densityMultiplier;

                Material checkerMat =
                    CreateCheckerMaterial(
                        originalMat.name,
                        checkerScale
                    );

                checkerMats[i] = checkerMat;

                generatedMaterials.Add(checkerMat);
            }

            renderer.sharedMaterials = checkerMats;

            EditorUtility.SetDirty(renderer);
        }
    }

    // =========================================================
    // CREATE CHECKER MATERIAL
    // =========================================================

    private static Material CreateCheckerMaterial(
    string originalName,
    Vector2 checkerScale
)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        Material mat = new Material(shader);

        mat.name = originalName + "_CHECKER";

        // Albedo only replacement
        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTexture("_BaseMap", checkerTexture);
            mat.SetTextureScale("_BaseMap", checkerScale);
        }

        if (mat.HasProperty("_MainTex"))
        {
            mat.SetTexture("_MainTex", checkerTexture);
            mat.SetTextureScale("_MainTex", checkerScale);
        }

        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", 0f);
        }

        // IMPORTANT:
        // NO tocar metallic / smoothness / normal maps
        // para mantener lectura PBR real

        return mat;
    }

    // =========================================================
    // SHADER
    // =========================================================

    private static Shader GetUnlitShader()
    {
        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit");

        if (shader != null)
            return shader;

        shader = Shader.Find("Unlit/Texture");

        if (shader != null)
            return shader;

        shader = Shader.Find("Standard");

        return shader;
    }

    // =========================================================
    // TEXTURE PROPERTY
    // =========================================================

    private static string GetTextureProperty(Material mat)
    {
        if (mat.HasProperty("_BaseMap"))
            return "_BaseMap";

        if (mat.HasProperty("_MainTex"))
            return "_MainTex";

        return null;
    }

    // =========================================================
    // SCENE VIEW CONFIG
    // =========================================================

    private static void ConfigureSceneView()
    {
        SceneView sceneView =
            SceneView.lastActiveSceneView;

        if (sceneView == null)
            return;

        // No scene lighting
        sceneView.sceneLighting = false;

        // Hide skybox
        sceneView.sceneViewState.showSkybox = false;

        // Hide gizmos
        sceneView.drawGizmos = false;

        // Force repaint
        sceneView.Repaint();
    }

    // =========================================================
    // RESTORE
    // =========================================================

    private static void RestoreMaterials()
    {
        foreach (var pair in originalMaterials)
        {
            if (pair.Key != null)
            {
                pair.Key.sharedMaterials = pair.Value;

                EditorUtility.SetDirty(pair.Key);
            }
        }

        foreach (Material mat in generatedMaterials)
        {
            if (mat != null)
            {
                Object.DestroyImmediate(mat);
            }
        }

        generatedMaterials.Clear();

        checkerApplied = false;

        Debug.Log("Original materials restored.");
    }

    // =========================================================
    // CLOSE VALIDATION SCENE
    // =========================================================

    private static void CloseValidationScene()
    {
        if (string.IsNullOrEmpty(duplicatedScenePath))
        {
            Debug.LogWarning("No validation scene found.");
            return;
        }

        bool close =
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        if (!close)
            return;

        RestoreMaterials();

        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);

        AssetDatabase.DeleteAsset(duplicatedScenePath);

        duplicatedScenePath = "";

        checkerApplied = false;

        Debug.Log("Validation scene closed and deleted.");
    }
}