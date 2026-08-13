using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace McpUnity.Utils
{
    /// <summary>
    /// Helper to ensure scenes are saved autonomously without triggering blocking Unity modal dialogs.
    /// </summary>
    public static class SceneSaveHelper
    {
        public const string DefaultSceneFolder = "Assets/Scenes";

        /// <summary>
        /// Silently saves all open dirty or untitled scenes without showing any modal dialogs.
        /// If a scene is untitled (has no path), it automatically assigns a path under Assets/Scenes/
        /// and saves it, preventing Unity from popping up a blocking file save prompt.
        /// </summary>
        /// <returns>Number of scenes saved</returns>
        public static int EnsureAllScenesSavedSilently()
        {
            int savedCount = 0;
            int sceneCount = SceneManager.sceneCount;

            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                if (scene.isDirty || string.IsNullOrEmpty(scene.path))
                {
                    string target = scene.path;
                    if (string.IsNullOrEmpty(target))
                    {
                        EnsureFolderExists(DefaultSceneFolder);
                        string sceneName = string.IsNullOrEmpty(scene.name) || scene.name.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase)
                            ? "Untitled"
                            : scene.name;
                        target = AssetDatabase.GenerateUniqueAssetPath($"{DefaultSceneFolder}/{sceneName}.unity");
                    }

                    try
                    {
                        bool ok = EditorSceneManager.SaveScene(scene, target);
                        if (ok)
                        {
                            savedCount++;
                            McpLogger.LogInfo($"Auto-saved scene '{scene.name}' to '{target}' without prompt");
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLogger.LogWarning($"Could not auto-save scene '{scene.name}': {ex.Message}");
                    }
                }
            }

            if (savedCount > 0)
            {
                AssetDatabase.Refresh();
            }

            return savedCount;
        }

        /// <summary>
        /// Ensures a folder path exists under Assets/
        /// </summary>
        public static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string[] parts = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string current = parts.Length > 0 && parts[0] == "Assets" ? "Assets" : "Assets";

            for (int i = 0; i < parts.Length; i++)
            {
                if (i == 0 && parts[i] == "Assets") continue;

                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
