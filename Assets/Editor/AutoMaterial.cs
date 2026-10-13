using UnityEngine;
using UnityEditor;
using System.IO;

public class AutoMaterialCreatorURP_Context
{
    [MenuItem("Assets/Create/URP Auto Material", false, 1000)]
    static void CreateMaterialFromSelectedFolder()
    {
        Object selected = Selection.activeObject;

        if (selected == null)
        {
            Debug.LogError("No hay carpeta seleccionada.");
            return;
        }

        string folderPath = AssetDatabase.GetAssetPath(selected);

        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError("Debes seleccionar una carpeta.");
            return;
        }

        string[] files = Directory.GetFiles(folderPath);

        Texture2D albedo = null;
        Texture2D normal = null;
        Texture2D metallic = null;
        Texture2D occlusion = null;
        Texture2D emission = null;

        foreach (string file in files)
        {
            if (file.EndsWith(".meta") || file.EndsWith(".fbx")) continue;

            string assetPath = file.Replace("\\", "/");
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            if (tex == null) continue;

            string name = tex.name.ToLower();


            if (name.Contains("_albedotransparency"))
                albedo = tex;

            else if (name.Contains("_normal"))
            {
                normal = tex;

                TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(assetPath);
                if (importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.SaveAndReimport();
                }
            }

            else if (name.Contains("_metallicsmoothness"))
                metallic = tex;

            else if (name.Contains("_ambientocclusion")) // CHEKEAR
                occlusion = tex;

            else if (name.Contains("_emission"))
                emission = tex;
        }

        // Shader URP
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            Debug.LogError("URP/Lit shader no encontrado.");
            return;
        }

        Material mat = new Material(shader);

        if (albedo != null)
            mat.SetTexture("_BaseMap", albedo);

        if (normal != null)
        {
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", normal);
        }

        if (metallic != null)
        {
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_MetallicGlossMap", metallic);
            mat.SetFloat("_Metallic", 1.0f);
        }

        if (occlusion != null)
            mat.SetTexture("_OcclusionMap", occlusion);

        if (emission != null)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", emission);
        }

        // Name based on folder
        string folderName = Path.GetFileName(folderPath);
        string matPath = folderPath + "/" + folderName + "_mat.mat";

        AssetDatabase.CreateAsset(mat, matPath);
        AssetDatabase.SaveAssets();

        Debug.Log("URP Material created in: " + matPath);
    }

    // Validation to only show the menu item when a folder is selected

    [MenuItem("Assets/Create/URP Auto Material", true)]
    static bool ValidateCreateMaterialFromFolder()
    {
        return Selection.activeObject != null &&
               AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(Selection.activeObject));
    }
}