using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for checking the Unity Editor state, including modal dialogs, open windows,
    /// compilation/update status, and unsaved scene modifications.
    /// </summary>
    public class GetEditorStateTool : McpToolBase
    {
        public GetEditorStateTool()
        {
            Name = "get_editor_state";
            Description = "Gets the current Unity Editor state, including modal dialogs, focused/open windows, compilation/play status, and unsaved scene changes";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                var openWindows = new JArray();
                var modalWindows = new JArray();
                bool hasModalWindow = false;

                // Find all open EditorWindows
                var windows = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>();
                foreach (var window in windows)
                {
                    if (window == null) continue;

                    string typeName = window.GetType().Name;
                    string fullTypeName = window.GetType().FullName;
                    string title = window.titleContent != null ? window.titleContent.text : typeName;

                    bool isModalOrDialog = false;

                    // 1. Check window type name conventions
                    if (typeName.IndexOf("Modal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("SaveModified", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("Prompt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("Wizard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("Confirmation", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isModalOrDialog = true;
                    }

                    // 2. Check internal ContainerWindow showMode if accessible
                    try
                    {
                        var parentProp = typeof(EditorWindow).GetProperty("parent", BindingFlags.Instance | BindingFlags.NonPublic);
                        var hostView = parentProp?.GetValue(window);
                        if (hostView != null)
                        {
                            var windowProp = hostView.GetType().GetProperty("window", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            var containerWindow = windowProp?.GetValue(hostView);
                            if (containerWindow != null)
                            {
                                var showModeProp = containerWindow.GetType().GetProperty("showMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (showModeProp != null)
                                {
                                    object modeVal = showModeProp.GetValue(containerWindow);
                                    string modeStr = modeVal != null ? modeVal.ToString() : "";
                                    int modeInt = modeVal != null ? Convert.ToInt32(modeVal) : 0;
                                    // 2 = Utility, 6 = ModalUtility in Unity ContainerWindow.ShowMode
                                    if (modeInt == 2 || modeInt == 6 || modeStr.IndexOf("Modal", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        isModalOrDialog = true;
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    var winJson = new JObject
                    {
                        ["title"] = title,
                        ["typeName"] = typeName,
                        ["fullTypeName"] = fullTypeName,
                        ["hasFocus"] = window.hasFocus,
                        ["maximized"] = window.maximized,
                        ["docked"] = window.docked,
                        ["isModalOrDialog"] = isModalOrDialog
                    };

                    openWindows.Add(winJson);
                    if (isModalOrDialog)
                    {
                        modalWindows.Add(winJson);
                        hasModalWindow = true;
                    }
                }

                // Check unsaved / dirty scenes
                var unsavedScenes = new JArray();
                int sceneCount = SceneManager.sceneCount;
                for (int i = 0; i < sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene.isDirty || string.IsNullOrEmpty(scene.path))
                    {
                        unsavedScenes.Add(new JObject
                        {
                            ["name"] = scene.name,
                            ["path"] = scene.path,
                            ["isDirty"] = scene.isDirty,
                            ["isUntitled"] = string.IsNullOrEmpty(scene.path),
                            ["isLoaded"] = scene.isLoaded
                        });
                    }
                }

                EditorWindow focusedWindow = EditorWindow.focusedWindow;
                string focusedTitle = focusedWindow != null
                    ? (focusedWindow.titleContent != null ? focusedWindow.titleContent.text : focusedWindow.GetType().Name)
                    : null;

                var result = new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = hasModalWindow 
                        ? $"Editor has {modalWindows.Count} modal/dialog window(s) open"
                        : "Editor state retrieved successfully (no modal windows open)",
                    ["hasModalWindow"] = hasModalWindow,
                    ["modalWindowCount"] = modalWindows.Count,
                    ["modalWindows"] = modalWindows,
                    ["focusedWindow"] = focusedTitle,
                    ["openWindowCount"] = openWindows.Count,
                    ["openWindows"] = openWindows,
                    ["isCompiling"] = EditorApplication.isCompiling,
                    ["isUpdating"] = EditorApplication.isUpdating,
                    ["isPlaying"] = EditorApplication.isPlaying,
                    ["isPaused"] = EditorApplication.isPaused,
                    ["hasUnsavedChanges"] = unsavedScenes.Count > 0,
                    ["unsavedSceneCount"] = unsavedScenes.Count,
                    ["unsavedScenes"] = unsavedScenes
                };

                return result;
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error getting editor state: {ex.Message}",
                    "editor_state_error"
                );
            }
        }
    }
}
