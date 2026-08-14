using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for managing scene lighting, environment/ambient illumination, and light baking.
    /// </summary>
    public class ManageLightingTool : McpToolBase
    {
        public ManageLightingTool()
        {
            Name = "manage_lighting";
            Description = "Manages scene illumination, ambient lighting, skybox settings, and lightmap baking (bake, cancel, clear, status).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "get_status";
            string reason = parameters["reason"]?.ToObject<string>();

            switch (action)
            {
                case "get_status":
                    return HandleGetStatus();

                case "set_environment":
                    return HandleSetEnvironment(parameters, reason);

                case "bake":
                    return HandleBake(reason);

                case "cancel_bake":
                    return HandleCancelBake(reason);

                case "clear_bake":
                    return HandleClearBake(reason);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'get_status', 'set_environment', 'bake', 'cancel_bake', 'clear_bake'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleGetStatus()
        {
            bool isBaking = Lightmapping.isRunning;
            int lightmapCount = LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0;

            string ambientModeStr = RenderSettings.ambientMode.ToString();
            string skyboxName = RenderSettings.skybox != null ? RenderSettings.skybox.name : "None";
            string sunName = RenderSettings.sun != null ? RenderSettings.sun.name : "None";

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Lighting status: isBaking={isBaking}, lightmapCount={lightmapCount}, ambientMode={ambientModeStr}",
                ["isBaking"] = isBaking,
                ["lightmapCount"] = lightmapCount,
                ["ambientMode"] = ambientModeStr,
                ["ambientIntensity"] = RenderSettings.ambientIntensity,
                ["reflectionIntensity"] = RenderSettings.reflectionIntensity,
                ["skybox"] = skyboxName,
                ["sun"] = sunName,
                ["ambientSkyColor"] = ColorToJObject(RenderSettings.ambientSkyColor),
                ["ambientEquatorColor"] = ColorToJObject(RenderSettings.ambientEquatorColor),
                ["ambientGroundColor"] = ColorToJObject(RenderSettings.ambientGroundColor)
            };
        }

        private JObject HandleSetEnvironment(JObject parameters, string reason)
        {
            string ambientModeStr = parameters["ambientMode"]?.ToObject<string>();
            float? ambientIntensity = parameters["ambientIntensity"]?.ToObject<float?>();
            float? reflectionIntensity = parameters["reflectionIntensity"]?.ToObject<float?>();
            string skyboxMaterialPath = parameters["skyboxMaterial"]?.ToObject<string>();
            string sunLightPath = parameters["sunLight"]?.ToObject<string>();
            int? sunLightId = parameters["sunLightId"]?.ToObject<int?>();

            JObject ambientColorObj = parameters["ambientColor"] as JObject;
            JObject skyColorObj = parameters["ambientSkyColor"] as JObject;
            JObject equatorColorObj = parameters["ambientEquatorColor"] as JObject;
            JObject groundColorObj = parameters["ambientGroundColor"] as JObject;

            if (!string.IsNullOrEmpty(ambientModeStr))
            {
                if (Enum.TryParse(ambientModeStr, true, out AmbientMode mode))
                {
                    RenderSettings.ambientMode = mode;
                }
            }

            if (ambientIntensity.HasValue)
            {
                RenderSettings.ambientIntensity = Mathf.Clamp(ambientIntensity.Value, 0f, 8f);
            }

            if (reflectionIntensity.HasValue)
            {
                RenderSettings.reflectionIntensity = Mathf.Clamp01(reflectionIntensity.Value);
            }

            if (ambientColorObj != null)
            {
                RenderSettings.ambientLight = JObjectToColor(ambientColorObj);
            }

            if (skyColorObj != null)
            {
                RenderSettings.ambientSkyColor = JObjectToColor(skyColorObj);
            }

            if (equatorColorObj != null)
            {
                RenderSettings.ambientEquatorColor = JObjectToColor(equatorColorObj);
            }

            if (groundColorObj != null)
            {
                RenderSettings.ambientGroundColor = JObjectToColor(groundColorObj);
            }

            if (!string.IsNullOrEmpty(skyboxMaterialPath))
            {
                Material skyboxMat = AssetDatabase.LoadAssetAtPath<Material>(skyboxMaterialPath);
                if (skyboxMat == null)
                {
                    string[] guids = AssetDatabase.FindAssets($"{skyboxMaterialPath} t:Material");
                    if (guids.Length > 0)
                    {
                        skyboxMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    }
                }

                if (skyboxMat != null)
                {
                    RenderSettings.skybox = skyboxMat;
                }
                else
                {
                    return McpUnitySocketHandler.CreateErrorResponse($"Skybox material not found at '{skyboxMaterialPath}'", "material_not_found");
                }
            }

            if (sunLightId.HasValue || !string.IsNullOrEmpty(sunLightPath))
            {
                GameObject sunObj = null;
                if (sunLightId.HasValue)
                {
                    sunObj = UnityObjectId.ObjectFromId(sunLightId.Value) as GameObject;
                }
                else
                {
                    sunObj = GameObject.Find(sunLightPath);
                }

                if (sunObj != null)
                {
                    Light lightComp = sunObj.GetComponent<Light>();
                    if (lightComp != null)
                    {
                        RenderSettings.sun = lightComp;
                    }
                }
            }

            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            McpLogger.LogInfo($"[MCP Unity] Updated environment lighting settings" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = "Successfully updated environment lighting settings",
                ["ambientMode"] = RenderSettings.ambientMode.ToString(),
                ["ambientIntensity"] = RenderSettings.ambientIntensity,
                ["reflectionIntensity"] = RenderSettings.reflectionIntensity,
                ["skybox"] = RenderSettings.skybox != null ? RenderSettings.skybox.name : "None",
                ["sun"] = RenderSettings.sun != null ? RenderSettings.sun.name : "None"
            };
        }

        private JObject HandleBake(string reason)
        {
            if (Lightmapping.isRunning)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = "Lightmap baking is already in progress.",
                    ["isBaking"] = true
                };
            }

            bool started = Lightmapping.BakeAsync();
            McpLogger.LogInfo($"[MCP Unity] Triggered asynchronous lightmap bake (started: {started})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = started,
                ["type"] = "text",
                ["message"] = started ? "Successfully started background lightmap baking." : "Failed to start lightmap baking. Ensure the scene has static lightmap geometry and lights.",
                ["isBaking"] = started
            };
        }

        private JObject HandleCancelBake(string reason)
        {
            if (!Lightmapping.isRunning)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = "No lightmap baking process is currently running.",
                    ["isBaking"] = false
                };
            }

            Lightmapping.Cancel();
            McpLogger.LogInfo($"[MCP Unity] Cancelled lightmap baking" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = "Lightmap baking was cancelled.",
                ["isBaking"] = false
            };
        }

        private JObject HandleClearBake(string reason)
        {
            Lightmapping.Clear();
            Lightmapping.ClearLightingDataAsset();
            McpLogger.LogInfo($"[MCP Unity] Cleared baked lightmaps and lighting data" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = "Successfully cleared all baked lightmaps and scene lighting data."
            };
        }

        private JObject ColorToJObject(Color c)
        {
            return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };
        }

        private Color JObjectToColor(JObject obj)
        {
            return new Color(
                obj["r"]?.ToObject<float>() ?? 0f,
                obj["g"]?.ToObject<float>() ?? 0f,
                obj["b"]?.ToObject<float>() ?? 0f,
                obj["a"]?.ToObject<float>() ?? 1f
            );
        }
    }

    /// <summary>
    /// Tool for creating and laying out Light Probe Groups in 3D grids or volumes.
    /// </summary>
    public class ConfigureLightProbeGroupTool : McpToolBase
    {
        public ConfigureLightProbeGroupTool()
        {
            Name = "configure_light_probe_group";
            Description = "Creates or populates a LightProbeGroup with an automated 3D grid layout of light probes for indirect dynamic lighting.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string name = parameters["name"]?.ToObject<string>();

            JObject centerObj = parameters["center"] as JObject;
            JObject sizeObj = parameters["size"] as JObject;
            JObject spacingObj = parameters["spacing"] as JObject;
            string reason = parameters["reason"]?.ToObject<string>();

            GameObject targetObject = null;
            LightProbeGroup probeGroup = null;

            if (instanceId.HasValue || !string.IsNullOrEmpty(objectPath))
            {
                JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out targetObject, out string idInfo);
                if (findError != null) return findError;

                probeGroup = targetObject.GetComponent<LightProbeGroup>();
                if (probeGroup == null)
                {
                    probeGroup = Undo.AddComponent<LightProbeGroup>(targetObject);
                }
            }
            else
            {
                targetObject = new GameObject(string.IsNullOrEmpty(name) ? "LightProbeGroup" : name);
                probeGroup = targetObject.AddComponent<LightProbeGroup>();
                Undo.RegisterCreatedObjectUndo(targetObject, "Create LightProbeGroup");
            }

            Vector3 center = new Vector3(
                centerObj?["x"]?.ToObject<float>() ?? 0f,
                centerObj?["y"]?.ToObject<float>() ?? 1.5f,
                centerObj?["z"]?.ToObject<float>() ?? 0f
            );

            Vector3 size = new Vector3(
                sizeObj?["x"]?.ToObject<float>() ?? 10f,
                sizeObj?["y"]?.ToObject<float>() ?? 3f,
                sizeObj?["z"]?.ToObject<float>() ?? 10f
            );

            Vector3 spacing = new Vector3(
                Mathf.Max(0.5f, spacingObj?["x"]?.ToObject<float>() ?? 2.5f),
                Mathf.Max(0.5f, spacingObj?["y"]?.ToObject<float>() ?? 1.5f),
                Mathf.Max(0.5f, spacingObj?["z"]?.ToObject<float>() ?? 2.5f)
            );

            // Generate 3D grid of probe positions in local space
            List<Vector3> positions = new List<Vector3>();

            float halfX = size.x * 0.5f;
            float halfY = size.y * 0.5f;
            float halfZ = size.z * 0.5f;

            for (float x = -halfX; x <= halfX + 0.01f; x += spacing.x)
            {
                for (float y = -halfY; y <= halfY + 0.01f; y += spacing.y)
                {
                    for (float z = -halfZ; z <= halfZ + 0.01f; z += spacing.z)
                    {
                        positions.Add(center + new Vector3(x, y, z));
                    }
                }
            }

            Undo.RecordObject(probeGroup, "Configure Light Probes");
            probeGroup.probePositions = positions.ToArray();
            EditorUtility.SetDirty(targetObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured {positions.Count} Light Probes on '{targetObject.name}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured {positions.Count} light probe positions on '{targetObject.name}'",
                ["instanceId"] = UnityObjectId.GetObjectId(targetObject),
                ["gameObjectName"] = targetObject.name,
                ["probeCount"] = positions.Count,
                ["center"] = new JObject { ["x"] = center.x, ["y"] = center.y, ["z"] = center.z },
                ["size"] = new JObject { ["x"] = size.x, ["y"] = size.y, ["z"] = size.z }
            };
        }
    }

    /// <summary>
    /// Tool for creating and configuring Reflection Probes.
    /// </summary>
    public class CreateReflectionProbeTool : McpToolBase
    {
        public CreateReflectionProbeTool()
        {
            Name = "create_reflection_probe";
            Description = "Creates or configures a Reflection Probe for localized environment reflections (box projection, resolution, size).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string name = parameters["name"]?.ToObject<string>() ?? "ReflectionProbe";
            JObject posObj = parameters["position"] as JObject;
            JObject sizeObj = parameters["size"] as JObject;
            bool boxProjection = parameters["boxProjection"]?.ToObject<bool?>() ?? true;
            int resolution = parameters["resolution"]?.ToObject<int?>() ?? 128;
            string modeStr = parameters["mode"]?.ToObject<string>()?.ToLowerInvariant() ?? "baked";
            string parentPath = parameters["parentPath"]?.ToObject<string>();
            int? parentId = parameters["parentId"]?.ToObject<int?>();
            string reason = parameters["reason"]?.ToObject<string>();

            GameObject probeObj = new GameObject(name);
            ReflectionProbe probe = probeObj.AddComponent<ReflectionProbe>();

            Vector3 position = new Vector3(
                posObj?["x"]?.ToObject<float>() ?? 0f,
                posObj?["y"]?.ToObject<float>() ?? 1.5f,
                posObj?["z"]?.ToObject<float>() ?? 0f
            );

            Vector3 size = new Vector3(
                sizeObj?["x"]?.ToObject<float>() ?? 10f,
                sizeObj?["y"]?.ToObject<float>() ?? 5f,
                sizeObj?["z"]?.ToObject<float>() ?? 10f
            );

            probeObj.transform.position = position;
            probe.size = size;
            probe.boxProjection = boxProjection;
            probe.resolution = Mathf.ClosestPowerOfTwo(resolution);

            switch (modeStr)
            {
                case "realtime":
                    probe.mode = ReflectionProbeMode.Realtime;
                    probe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
                    break;
                case "custom":
                    probe.mode = ReflectionProbeMode.Custom;
                    break;
                case "baked":
                default:
                    probe.mode = ReflectionProbeMode.Baked;
                    break;
            }

            if (parentId.HasValue)
            {
                GameObject parent = UnityObjectId.ObjectFromId(parentId.Value) as GameObject;
                if (parent != null) probeObj.transform.SetParent(parent.transform, true);
            }
            else if (!string.IsNullOrEmpty(parentPath))
            {
                GameObject parent = GameObject.Find(parentPath);
                if (parent != null) probeObj.transform.SetParent(parent.transform, true);
            }

            Undo.RegisterCreatedObjectUndo(probeObj, $"Create Reflection Probe {name}");
            EditorUtility.SetDirty(probeObj);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Created Reflection Probe '{name}' at {position} (size: {size})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created Reflection Probe '{name}' at {position}",
                ["instanceId"] = UnityObjectId.GetObjectId(probeObj),
                ["name"] = name,
                ["path"] = GameObjectToolUtils.GetGameObjectPath(probeObj),
                ["mode"] = probe.mode.ToString(),
                ["resolution"] = probe.resolution,
                ["boxProjection"] = probe.boxProjection,
                ["size"] = new JObject { ["x"] = size.x, ["y"] = size.y, ["z"] = size.z }
            };
        }
    }
}
