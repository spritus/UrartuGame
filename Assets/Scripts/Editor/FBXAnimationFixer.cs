using UnityEditor;
using UnityEngine;

public class FBXAnimationFixer : AssetPostprocessor
{
    // Bu kod FBX dosyasındaki animasyonların (Run ve Idle) döngüye (loop) girmesini sağlar.
    void OnPreprocessAnimation()
    {
        ModelImporter modelImporter = assetImporter as ModelImporter;
        if (modelImporter != null && assetPath.Contains("Meshy_AI_Apprentice_Mage"))
        {
            ModelImporterClipAnimation[] clipAnimations = modelImporter.defaultClipAnimations;
            if (clipAnimations != null && clipAnimations.Length > 0)
            {
                for (int i = 0; i < clipAnimations.Length; i++)
                {
                    if (clipAnimations[i].name.Contains("Run") || clipAnimations[i].name.Contains("Idle") || clipAnimations[i].name.Contains("Walk"))
                    {
                        clipAnimations[i].loopTime = true;
                    }
                }
                modelImporter.clipAnimations = clipAnimations;
            }
        }
    }
}
