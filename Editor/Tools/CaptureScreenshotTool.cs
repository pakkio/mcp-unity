using System;
using UnityEngine;
using UnityEditor;
using McpUnity.Unity;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for capturing a screenshot of the Scene view or a specific Camera in the Unity Editor
    /// </summary>
    public class CaptureScreenshotTool : McpToolBase
    {
        public CaptureScreenshotTool()
        {
            Name = "capture_screenshot";
            Description = "Captures a screenshot of the Scene view or a named/main Camera, returned as a base64-encoded image.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string view = parameters["view"]?.ToObject<string>() ?? "scene";
            string cameraName = parameters["cameraName"]?.ToObject<string>();
            int maxWidth = parameters["maxWidth"]?.ToObject<int?>() ?? 1024;
            string format = (parameters["format"]?.ToObject<string>() ?? "png").ToLower();
            int jpgQuality = parameters["jpgQuality"]?.ToObject<int?>() ?? 90;
            string savePath = parameters["savePath"]?.ToObject<string>();

            if (maxWidth < 64 || maxWidth > 4096)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "'maxWidth' must be between 64 and 4096.",
                    "validation_error"
                );
            }

            if (format != "png" && format != "jpg")
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "'format' must be either 'png' or 'jpg'.",
                    "validation_error"
                );
            }

            Camera camera;
            float sourceAspect;
            string sourceDescription;

            if (view == "scene")
            {
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null || sceneView.camera == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "No active Scene View found. Open and focus a Scene view window in the Unity Editor first.",
                        "not_found_error"
                    );
                }

                camera = sceneView.camera;
                sourceAspect = sceneView.position.height > 0
                    ? sceneView.position.width / sceneView.position.height
                    : camera.aspect;
                sourceDescription = "Scene view";
            }
            else if (view == "camera")
            {
                camera = !string.IsNullOrEmpty(cameraName)
                    ? GameObject.Find(cameraName)?.GetComponent<Camera>()
                    : Camera.main;

                if (camera == null)
                {
                    string identifier = !string.IsNullOrEmpty(cameraName) ? $"named '{cameraName}'" : "'Camera.main'";
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"No Camera found ({identifier}). Provide 'cameraName' or ensure a MainCamera-tagged camera exists.",
                        "not_found_error"
                    );
                }

                sourceAspect = camera.aspect;
                sourceDescription = $"Camera '{camera.name}'";
            }
            else
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "'view' must be either 'scene' or 'camera'.",
                    "validation_error"
                );
            }

            int width = maxWidth;
            int height = Mathf.Max(1, Mathf.RoundToInt(width / Mathf.Max(sourceAspect, 0.01f)));

            RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousTargetTexture = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            Texture2D texture = null;

            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                byte[] imageBytes = format == "jpg"
                    ? texture.EncodeToJPG(jpgQuality)
                    : texture.EncodeToPNG();

                string base64 = "";
                string mimeType = format == "jpg" ? "image/jpeg" : "image/png";
                bool savedToFile = false;
                string absoluteSavePath = "";

                if (!string.IsNullOrEmpty(savePath))
                {
                    try
                    {
                        absoluteSavePath = System.IO.Path.GetFullPath(savePath);
                        string directory = System.IO.Path.GetDirectoryName(absoluteSavePath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            System.IO.Directory.CreateDirectory(directory);
                        }
                        System.IO.File.WriteAllBytes(absoluteSavePath, imageBytes);
                        savedToFile = true;

                        // If saved inside Assets, refresh the AssetDatabase so it shows up in Unity
                        if (absoluteSavePath.Replace('\\', '/').Contains("/Assets/"))
                        {
                            AssetDatabase.Refresh();
                        }
                    }
                    catch (Exception ex)
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Failed to save screenshot to path '{savePath}': {ex.Message}",
                            "io_error"
                        );
                    }
                }
                else
                {
                    base64 = Convert.ToBase64String(imageBytes);
                }

                McpLogger.LogInfo($"{Name}: captured {width}x{height} {format} screenshot of {sourceDescription}" + (savedToFile ? $" saved to {absoluteSavePath}" : ""));

                var result = new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = savedToFile
                        ? $"Captured {width}x{height} screenshot of {sourceDescription} and saved to {absoluteSavePath}"
                        : $"Captured {width}x{height} screenshot of {sourceDescription}",
                    ["width"] = width,
                    ["height"] = height
                };

                if (savedToFile)
                {
                    result["savePath"] = absoluteSavePath;
                }
                else
                {
                    result["imageBase64"] = base64;
                    result["mimeType"] = mimeType;
                }

                return result;
            }
            finally
            {
                camera.targetTexture = previousTargetTexture;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }
    }
}
