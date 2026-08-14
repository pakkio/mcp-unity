using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for creating and configuring Post-Processing Volumes and VolumeProfile assets (Bloom, ACES Tonemapping, Vignette, Color Grading).
    /// </summary>
    public class ConfigurePostProcessingTool : McpToolBase
    {
        public ConfigurePostProcessingTool()
        {
            Name = "configure_post_processing";
            Description = "Creates and configures Post-Processing Volumes and Volume Profiles (Bloom, Tonemapping, Vignette, Color Adjustments, Depth of Field, Chromatic Aberration).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "create_volume";
            string reason = parameters["reason"]?.ToObject<string>();

            switch (action)
            {
                case "create_volume":
                case "setup_global_volume":
                    return HandleSetupGlobalVolume(parameters, reason);

                case "add_override":
                case "set_override":
                    return HandleSetOverride(parameters, reason);

                case "get_profile_info":
                    return HandleGetProfileInfo(parameters);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'create_volume', 'add_override', 'get_profile_info'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleSetupGlobalVolume(JObject parameters, string reason)
        {
            string volumeName = parameters["volumeName"]?.ToObject<string>() ?? "Global Post Processing Volume";
            string profilePath = parameters["profilePath"]?.ToObject<string>();
            bool isGlobal = parameters["isGlobal"]?.ToObject<bool?>() ?? true;
            float weight = Mathf.Clamp01(parameters["weight"]?.ToObject<float?>() ?? 1.0f);
            float priority = parameters["priority"]?.ToObject<float?>() ?? 0.0f;

            // Find or create Volume GameObject
            GameObject volumeGo = GameObject.Find(volumeName);
            if (volumeGo == null)
            {
                volumeGo = new GameObject(volumeName);
                Undo.RegisterCreatedObjectUndo(volumeGo, "Create Post Processing Volume");
            }

            Volume volume = volumeGo.GetComponent<Volume>();
            if (volume == null)
            {
                volume = Undo.AddComponent<Volume>(volumeGo);
            }
            else
            {
                Undo.RecordObject(volume, "Configure Volume");
            }

            volume.isGlobal = isGlobal;
            volume.weight = weight;
            volume.priority = priority;

            // Create or load VolumeProfile asset
            if (string.IsNullOrWhiteSpace(profilePath))
            {
                profilePath = $"Assets/Settings/{volumeName.Replace(" ", "_")}_Profile.asset";
            }

            profilePath = profilePath.Replace('\\', '/').Trim();
            if (!profilePath.StartsWith("Assets/")) profilePath = "Assets/" + profilePath.TrimStart('/');
            if (!profilePath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) profilePath += ".asset";

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                string dir = Path.GetDirectoryName(profilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    AssetDatabase.Refresh();
                }

                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profilePath = AssetDatabase.GenerateUniqueAssetPath(profilePath);
                AssetDatabase.CreateAsset(profile, profilePath);
                AssetDatabase.SaveAssets();
            }

            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volumeGo);
            EditorUtility.SetDirty(profile);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured Global Post Processing Volume '{volumeName}' with profile at '{profilePath}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured Post-Processing Volume '{volumeName}' with profile '{profilePath}'",
                ["volumeName"] = volumeName,
                ["instanceId"] = UnityObjectId.GetObjectId(volumeGo),
                ["profilePath"] = profilePath,
                ["isGlobal"] = volume.isGlobal,
                ["weight"] = volume.weight
            };
        }

        private JObject HandleSetOverride(JObject parameters, string reason)
        {
            string profilePath = parameters["profilePath"]?.ToObject<string>();
            string volumeName = parameters["volumeName"]?.ToObject<string>();
            string effectType = parameters["effectType"]?.ToObject<string>()?.ToLowerInvariant();
            JObject settings = parameters["settings"] as JObject;

            if (string.IsNullOrEmpty(effectType))
            {
                return McpUnitySocketHandler.CreateErrorResponse("Parameter 'effectType' is required (e.g. 'bloom', 'tonemapping', 'vignette', 'color_adjustments', 'depth_of_field', 'chromatic_aberration').", "validation_error");
            }

            VolumeProfile profile = null;
            if (!string.IsNullOrEmpty(profilePath))
            {
                profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            }
            else if (!string.IsNullOrEmpty(volumeName))
            {
                GameObject go = GameObject.Find(volumeName);
                if (go != null)
                {
                    Volume v = go.GetComponent<Volume>();
                    if (v != null) profile = v.sharedProfile;
                }
            }
            else
            {
                // Find any global volume in scene
                Volume[] volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
                foreach (var v in volumes)
                {
                    if (v.isGlobal && v.sharedProfile != null)
                    {
                        profile = v.sharedProfile;
                        break;
                    }
                }
            }

            if (profile == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse("No VolumeProfile found. Create a volume first using 'create_volume'.", "profile_not_found");
            }

            Undo.RecordObject(profile, "Add Post Processing Override");

            Type componentType = ResolveVolumeComponentType(effectType);
            if (componentType == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Post-processing effect type '{effectType}' not found in current render pipeline assemblies.", "component_type_not_found");
            }

            VolumeComponent comp = null;
            foreach (var item in profile.components)
            {
                if (item != null && item.GetType() == componentType)
                {
                    comp = item;
                    break;
                }
            }

            if (comp == null)
            {
                comp = profile.Add(componentType, true);
            }

            comp.active = true;

            // Apply settings via reflection on VolumeParameter properties
            if (settings != null)
            {
                ApplyComponentSettings(comp, settings);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured {effectType} override on VolumeProfile '{profile.name}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured '{effectType}' override on VolumeProfile '{profile.name}'",
                ["profileName"] = profile.name,
                ["effectType"] = effectType,
                ["componentClass"] = componentType.Name
            };
        }

        private JObject HandleGetProfileInfo(JObject parameters)
        {
            string profilePath = parameters["profilePath"]?.ToObject<string>();
            VolumeProfile profile = null;

            if (!string.IsNullOrEmpty(profilePath))
            {
                profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            }
            else
            {
                Volume[] volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
                foreach (var v in volumes)
                {
                    if (v.sharedProfile != null)
                    {
                        profile = v.sharedProfile;
                        break;
                    }
                }
            }

            if (profile == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse("No VolumeProfile found.", "profile_not_found");
            }

            JArray effectsArray = new JArray();
            foreach (var comp in profile.components)
            {
                if (comp == null) continue;
                effectsArray.Add(new JObject
                {
                    ["name"] = comp.GetType().Name,
                    ["active"] = comp.active
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"VolumeProfile '{profile.name}' contains {effectsArray.Count} overrides.",
                ["profileName"] = profile.name,
                ["overrideCount"] = effectsArray.Count,
                ["overrides"] = effectsArray
            };
        }

        private Type ResolveVolumeComponentType(string effectType)
        {
            string[] possibleNames = null;
            switch (effectType)
            {
                case "bloom":
                    possibleNames = new[] { "UnityEngine.Rendering.Universal.Bloom", "UnityEngine.Rendering.HighDefinition.Bloom", "UnityEngine.Rendering.PostProcessing.Bloom" };
                    break;
                case "tonemapping":
                    possibleNames = new[] { "UnityEngine.Rendering.Universal.Tonemapping", "UnityEngine.Rendering.HighDefinition.Tonemapping" };
                    break;
                case "vignette":
                    possibleNames = new[] { "UnityEngine.Rendering.Universal.Vignette", "UnityEngine.Rendering.HighDefinition.Vignette" };
                    break;
                case "color_adjustments":
                case "coloradjustments":
                case "colorgraiding":
                    possibleNames = new[] { "UnityEngine.Rendering.Universal.ColorAdjustments", "UnityEngine.Rendering.HighDefinition.ColorAdjustments" };
                    break;
                case "depth_of_field":
                case "depthoffield":
                case "dof":
                    possibleNames = new[] { "UnityEngine.Rendering.Universal.DepthOfField", "UnityEngine.Rendering.HighDefinition.DepthOfField" };
                    break;
                case "chromatic_aberration":
                case "chromaticaberration":
                    possibleNames = new[] { "UnityEngine.Rendering.Universal.ChromaticAberration", "UnityEngine.Rendering.HighDefinition.ChromaticAberration" };
                    break;
                case "motion_blur":
                case "motionblur":
                    possibleNames = new[] { "UnityEngine.Rendering.Universal.MotionBlur", "UnityEngine.Rendering.HighDefinition.MotionBlur" };
                    break;
                case "white_balance":
                case "whitebalance":
                    possibleNames = new[] { "UnityEngine.Rendering.Universal.WhiteBalance", "UnityEngine.Rendering.HighDefinition.WhiteBalance" };
                    break;
                case "film_grain":
                case "filmgrain":
                    possibleNames = new[] { "UnityEngine.Rendering.Universal.FilmGrain", "UnityEngine.Rendering.HighDefinition.FilmGrain" };
                    break;
            }

            if (possibleNames != null)
            {
                foreach (var name in possibleNames)
                {
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            Type t = assembly.GetType(name);
                            if (t != null && typeof(VolumeComponent).IsAssignableFrom(t)) return t;
                        }
                        catch { }
                    }
                }
            }

            return null;
        }

        private void ApplyComponentSettings(VolumeComponent comp, JObject settings)
        {
            Type compType = comp.GetType();
            FieldInfo[] fields = compType.GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in settings.Properties())
            {
                string propName = prop.Name;
                JToken propVal = prop.Value;

                foreach (var field in fields)
                {
                    if (string.Equals(field.Name, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        object volumeParam = field.GetValue(comp);
                        if (volumeParam != null)
                        {
                            Type paramType = volumeParam.GetType();
                            PropertyInfo overrideStateProp = paramType.GetProperty("overrideState");
                            PropertyInfo valueProp = paramType.GetProperty("value");

                            if (overrideStateProp != null) overrideStateProp.SetValue(volumeParam, true);

                            if (valueProp != null)
                            {
                                object convertedVal = ConvertJsonToVolumeValue(propVal, valueProp.PropertyType);
                                if (convertedVal != null)
                                {
                                    valueProp.SetValue(volumeParam, convertedVal);
                                }
                            }
                        }
                        break;
                    }
                }
            }
        }

        private object ConvertJsonToVolumeValue(JToken token, Type targetType)
        {
            try
            {
                if (targetType == typeof(float)) return token.ToObject<float>();
                if (targetType == typeof(int)) return token.ToObject<int>();
                if (targetType == typeof(bool)) return token.ToObject<bool>();
                if (targetType == typeof(Color))
                {
                    if (token.Type == JTokenType.String)
                    {
                        if (ColorUtility.TryParseHtmlString(token.ToString(), out Color parsedColor)) return parsedColor;
                    }
                    else if (token is JObject obj)
                    {
                        return new Color(
                            obj["r"]?.ToObject<float>() ?? 1f,
                            obj["g"]?.ToObject<float>() ?? 1f,
                            obj["b"]?.ToObject<float>() ?? 1f,
                            obj["a"]?.ToObject<float>() ?? 1f
                        );
                    }
                }
                if (targetType.IsEnum)
                {
                    string strVal = token.ToString();
                    return Enum.Parse(targetType, strVal, true);
                }
            }
            catch { }
            return null;
        }
    }
}
