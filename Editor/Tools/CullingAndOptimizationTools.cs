using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for managing Unity Occlusion Culling (baking, clearing, status, static flags, occlusion areas/portals).
    /// </summary>
    public class ManageOcclusionCullingTool : McpToolBase
    {
        public ManageOcclusionCullingTool()
        {
            Name = "manage_occlusion_culling";
            Description = "Manages Occlusion Culling (bake, cancel, clear, status, setting Occluder/Occludee static flags, creating Occlusion Areas and Portals).";
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

                case "bake":
                    return HandleBake(parameters, reason);

                case "cancel":
                    return HandleCancel(reason);

                case "clear":
                    return HandleClear(reason);

                case "set_static_flags":
                    return HandleSetStaticFlags(parameters, reason);

                case "create_occlusion_area":
                    return HandleCreateOcclusionArea(parameters, reason);

                case "create_occlusion_portal":
                    return HandleCreateOcclusionPortal(parameters, reason);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'get_status', 'bake', 'cancel', 'clear', 'set_static_flags', 'create_occlusion_area', 'create_occlusion_portal'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleGetStatus()
        {
            bool isBaking = StaticOcclusionCulling.isRunning;
            bool isDataPresent = StaticOcclusionCulling.doesSceneHaveOcclusionCullingData;

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Occlusion Culling status: isBaking={isBaking}, hasData={isDataPresent}",
                ["isBaking"] = isBaking,
                ["hasOcclusionData"] = isDataPresent,
                ["smallestOccluder"] = StaticOcclusionCulling.smallestOccluder,
                ["smallestHole"] = StaticOcclusionCulling.smallestHole,
                ["backfaceThreshold"] = StaticOcclusionCulling.backfaceThreshold
            };
        }

        private JObject HandleBake(JObject parameters, string reason)
        {
            if (StaticOcclusionCulling.isRunning)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = "Occlusion culling computation is already running.",
                    ["isBaking"] = true
                };
            }

            if (parameters["smallestOccluder"] != null)
            {
                StaticOcclusionCulling.smallestOccluder = Mathf.Max(0.1f, parameters["smallestOccluder"].ToObject<float>());
            }

            if (parameters["smallestHole"] != null)
            {
                StaticOcclusionCulling.smallestHole = Mathf.Max(0.01f, parameters["smallestHole"].ToObject<float>());
            }

            if (parameters["backfaceThreshold"] != null)
            {
                StaticOcclusionCulling.backfaceThreshold = Mathf.Clamp(parameters["backfaceThreshold"].ToObject<float>(), 5f, 100f);
            }

            bool started = StaticOcclusionCulling.Compute();
            McpLogger.LogInfo($"[MCP Unity] Started Occlusion Culling computation (started: {started})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = started,
                ["type"] = "text",
                ["message"] = started ? "Successfully started Occlusion Culling computation in background." : "Failed to start Occlusion Culling. Ensure the scene contains GameObjects marked as Occluder Static / Occludee Static.",
                ["isBaking"] = started
            };
        }

        private JObject HandleCancel(string reason)
        {
            if (!StaticOcclusionCulling.isRunning)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = "No Occlusion Culling computation is currently running."
                };
            }

            StaticOcclusionCulling.Cancel();
            McpLogger.LogInfo($"[MCP Unity] Cancelled Occlusion Culling computation" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = "Occlusion Culling computation was cancelled."
            };
        }

        private JObject HandleClear(string reason)
        {
            StaticOcclusionCulling.Clear();
            McpLogger.LogInfo($"[MCP Unity] Cleared baked Occlusion Culling data" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = "Successfully cleared all baked Occlusion Culling data from the scene."
            };
        }

        private JObject HandleSetStaticFlags(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            bool includeChildren = parameters["includeChildren"]?.ToObject<bool?>() ?? true;

            bool? occluder = parameters["occluder"]?.ToObject<bool?>();
            bool? occludee = parameters["occludee"]?.ToObject<bool?>();
            bool? contributeGI = parameters["contributeGI"]?.ToObject<bool?>();
            bool? batching = parameters["batching"]?.ToObject<bool?>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            List<GameObject> objectsToUpdate = new List<GameObject>();
            if (includeChildren)
            {
                objectsToUpdate.AddRange(targetObject.GetComponentsInChildren<Transform>(true).ConvertAll(t => t.gameObject));
            }
            else
            {
                objectsToUpdate.Add(targetObject);
            }

            foreach (var go in objectsToUpdate)
            {
                Undo.RecordObject(go, "Update Static Flags");
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);

                if (occluder.HasValue)
                {
                    if (occluder.Value) flags |= StaticEditorFlags.OccluderStatic;
                    else flags &= ~StaticEditorFlags.OccluderStatic;
                }

                if (occludee.HasValue)
                {
                    if (occludee.Value) flags |= StaticEditorFlags.OccludeeStatic;
                    else flags &= ~StaticEditorFlags.OccludeeStatic;
                }

                if (contributeGI.HasValue)
                {
                    if (contributeGI.Value) flags |= StaticEditorFlags.ContributeGI;
                    else flags &= ~StaticEditorFlags.ContributeGI;
                }

                if (batching.HasValue)
                {
                    if (batching.Value) flags |= StaticEditorFlags.BatchingStatic;
                    else flags &= ~StaticEditorFlags.BatchingStatic;
                }

                GameObjectUtility.SetStaticEditorFlags(go, flags);
                EditorUtility.SetDirty(go);
            }

            McpLogger.LogInfo($"[MCP Unity] Updated static flags on '{targetObject.name}' ({objectsToUpdate.Count} objects, occluder={occluder}, occludee={occludee})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully updated static editor flags on '{targetObject.name}' ({objectsToUpdate.Count} objects updated)",
                ["objectsUpdated"] = objectsToUpdate.Count,
                ["rootName"] = targetObject.name
            };
        }

        private JObject HandleCreateOcclusionArea(JObject parameters, string reason)
        {
            string name = parameters["name"]?.ToObject<string>() ?? "OcclusionArea";
            JObject centerObj = parameters["center"] as JObject;
            JObject sizeObj = parameters["size"] as JObject;

            GameObject go = new GameObject(name);
            OcclusionArea area = go.AddComponent<OcclusionArea>();

            Vector3 center = new Vector3(
                centerObj?["x"]?.ToObject<float>() ?? 0f,
                centerObj?["y"]?.ToObject<float>() ?? 0f,
                centerObj?["z"]?.ToObject<float>() ?? 0f
            );

            Vector3 size = new Vector3(
                sizeObj?["x"]?.ToObject<float>() ?? 50f,
                sizeObj?["y"]?.ToObject<float>() ?? 10f,
                sizeObj?["z"]?.ToObject<float>() ?? 50f
            );

            area.center = center;
            area.size = size;

            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            EditorUtility.SetDirty(go);

            McpLogger.LogInfo($"[MCP Unity] Created Occlusion Area '{name}' (size: {size})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created Occlusion Area '{name}'",
                ["instanceId"] = UnityObjectId.GetObjectId(go),
                ["name"] = name,
                ["size"] = new JObject { ["x"] = size.x, ["y"] = size.y, ["z"] = size.z }
            };
        }

        private JObject HandleCreateOcclusionPortal(JObject parameters, string reason)
        {
            string name = parameters["name"]?.ToObject<string>() ?? "OcclusionPortal";
            JObject centerObj = parameters["center"] as JObject;
            JObject sizeObj = parameters["size"] as JObject;
            bool open = parameters["open"]?.ToObject<bool?>() ?? true;

            GameObject go = new GameObject(name);
            OcclusionPortal portal = go.AddComponent<OcclusionPortal>();

            Vector3 center = new Vector3(
                centerObj?["x"]?.ToObject<float>() ?? 0f,
                centerObj?["y"]?.ToObject<float>() ?? 1f,
                centerObj?["z"]?.ToObject<float>() ?? 0f
            );

            Vector3 size = new Vector3(
                sizeObj?["x"]?.ToObject<float>() ?? 2f,
                sizeObj?["y"]?.ToObject<float>() ?? 2f,
                sizeObj?["z"]?.ToObject<float>() ?? 0.5f
            );

            portal.center = center;
            portal.size = size;
            portal.open = open;

            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            EditorUtility.SetDirty(go);

            McpLogger.LogInfo($"[MCP Unity] Created Occlusion Portal '{name}' (open: {open})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created Occlusion Portal '{name}'",
                ["instanceId"] = UnityObjectId.GetObjectId(go),
                ["name"] = name,
                ["open"] = open,
                ["size"] = new JObject { ["x"] = size.x, ["y"] = size.y, ["z"] = size.z }
            };
        }
    }

    /// <summary>
    /// Tool for configuring LODGroup (Level of Detail) with distance/screen height thresholds and renderer slots.
    /// </summary>
    public class ConfigureLODGroupTool : McpToolBase
    {
        public ConfigureLODGroupTool()
        {
            Name = "configure_lod_group";
            Description = "Creates or configures a LODGroup (Level of Detail) on a GameObject with custom screen percentage transitions (LOD0, LOD1, LOD2, Culled).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            JArray lodsArray = parameters["lods"] as JArray;
            string fadeModeStr = parameters["fadeMode"]?.ToObject<string>()?.ToLowerInvariant() ?? "none";
            bool animateCrossFading = parameters["animateCrossFading"]?.ToObject<bool?>() ?? false;
            string reason = parameters["reason"]?.ToObject<string>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            LODGroup lodGroup = targetObject.GetComponent<LODGroup>();
            if (lodGroup == null)
            {
                lodGroup = Undo.AddComponent<LODGroup>(targetObject);
            }
            else
            {
                Undo.RecordObject(lodGroup, "Configure LODGroup");
            }

            // Parse LOD fade mode
            switch (fadeModeStr)
            {
                case "crossfade":
                    lodGroup.fadeMode = LODFadeMode.CrossFade;
                    break;
                case "speedtree":
                    lodGroup.fadeMode = LODFadeMode.SpeedTree;
                    break;
                case "none":
                default:
                    lodGroup.fadeMode = LODFadeMode.None;
                    break;
            }

            lodGroup.animateCrossFading = animateCrossFading;

            List<LOD> lodList = new List<LOD>();

            if (lodsArray != null && lodsArray.Count > 0)
            {
                for (int i = 0; i < lodsArray.Count; i++)
                {
                    JObject lodObj = lodsArray[i] as JObject;
                    if (lodObj == null) continue;

                    float transitionHeight = Mathf.Clamp01(lodObj["screenRelativeTransitionHeight"]?.ToObject<float>() ?? (0.6f / (i + 1)));

                    List<Renderer> renderers = new List<Renderer>();
                    JArray renderersArray = lodObj["renderers"] as JArray;
                    if (renderersArray != null)
                    {
                        foreach (var item in renderersArray)
                        {
                            string childPath = item.ToString();
                            Transform childTransform = targetObject.transform.Find(childPath);
                            if (childTransform != null)
                            {
                                Renderer r = childTransform.GetComponent<Renderer>();
                                if (r != null) renderers.Add(r);
                            }
                            else
                            {
                                GameObject go = GameObject.Find(childPath);
                                if (go != null)
                                {
                                    Renderer r = go.GetComponent<Renderer>();
                                    if (r != null) renderers.Add(r);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Auto-assign matching child renderers like "LOD_0", "LOD_1", etc.
                        Transform child = targetObject.transform.Find($"LOD_{i}") ?? targetObject.transform.Find($"LOD{i}");
                        if (child != null)
                        {
                            renderers.AddRange(child.GetComponentsInChildren<Renderer>(true));
                        }
                    }

                    lodList.Add(new LOD(transitionHeight, renderers.ToArray()));
                }
            }
            else
            {
                // Auto-generate standard 3-tier LOD setup from child objects
                List<Renderer> lod0Renderers = new List<Renderer>();
                List<Renderer> lod1Renderers = new List<Renderer>();
                List<Renderer> lod2Renderers = new List<Renderer>();

                Transform t0 = targetObject.transform.Find("LOD0") ?? targetObject.transform.Find("LOD_0");
                Transform t1 = targetObject.transform.Find("LOD1") ?? targetObject.transform.Find("LOD_1");
                Transform t2 = targetObject.transform.Find("LOD2") ?? targetObject.transform.Find("LOD_2");

                if (t0 != null) lod0Renderers.AddRange(t0.GetComponentsInChildren<Renderer>(true));
                if (t1 != null) lod1Renderers.AddRange(t1.GetComponentsInChildren<Renderer>(true));
                if (t2 != null) lod2Renderers.AddRange(t2.GetComponentsInChildren<Renderer>(true));

                if (lod0Renderers.Count == 0 && targetObject.GetComponent<Renderer>() != null)
                {
                    lod0Renderers.Add(targetObject.GetComponent<Renderer>());
                }

                lodList.Add(new LOD(0.6f, lod0Renderers.ToArray()));
                if (lod1Renderers.Count > 0) lodList.Add(new LOD(0.3f, lod1Renderers.ToArray()));
                if (lod2Renderers.Count > 0) lodList.Add(new LOD(0.1f, lod2Renderers.ToArray()));
            }

            lodGroup.SetLODs(lodList.ToArray());
            lodGroup.RecalculateBounds();
            EditorUtility.SetDirty(targetObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured LODGroup on '{targetObject.name}' with {lodList.Count} LOD tiers" + (reason != null ? $" — {reason}" : ""));

            JArray resultLods = new JArray();
            for (int i = 0; i < lodList.Count; i++)
            {
                resultLods.Add(new JObject
                {
                    ["lodIndex"] = i,
                    ["screenRelativeTransitionHeight"] = lodList[i].screenRelativeTransitionHeight,
                    ["rendererCount"] = lodList[i].renderers != null ? lodList[i].renderers.Length : 0
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured LODGroup on '{targetObject.name}' with {lodList.Count} levels",
                ["instanceId"] = UnityObjectId.GetObjectId(targetObject),
                ["gameObjectName"] = targetObject.name,
                ["lodCount"] = lodList.Count,
                ["fadeMode"] = lodGroup.fadeMode.ToString(),
                ["lods"] = resultLods
            };
        }
    }

    /// <summary>
    /// Tool for configuring Camera culling, distance culling (layerCullDistances), and far clipping planes.
    /// </summary>
    public class ConfigureCameraCullingTool : McpToolBase
    {
        public ConfigureCameraCullingTool()
        {
            Name = "configure_camera_culling";
            Description = "Configures Camera culling properties: layer-based draw distances (layerCullDistances), far clipping plane, and Occlusion Culling toggles.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string cameraName = parameters["cameraName"]?.ToObject<string>();
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();

            bool? useOcclusionCulling = parameters["useOcclusionCulling"]?.ToObject<bool?>();
            float? farClipPlane = parameters["farClipPlane"]?.ToObject<float?>();
            float? nearClipPlane = parameters["nearClipPlane"]?.ToObject<float?>();
            float? fieldOfView = parameters["fieldOfView"]?.ToObject<float?>();
            bool? orthographic = parameters["orthographic"]?.ToObject<bool?>();
            float? orthographicSize = parameters["orthographicSize"]?.ToObject<float?>();
            int? cullingMask = parameters["cullingMask"]?.ToObject<int?>();
            JArray layerDistances = parameters["layerCullDistances"] as JArray;

            int? testTargetId = parameters["testTargetInstanceId"]?.ToObject<int?>();
            string testTargetPath = parameters["testTargetObjectPath"]?.ToObject<string>();
            string reason = parameters["reason"]?.ToObject<string>();

            Camera cam = null;
            if (instanceId.HasValue || !string.IsNullOrEmpty(objectPath))
            {
                JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject go, out string idInfo);
                if (findError != null) return findError;
                cam = go.GetComponent<Camera>();
            }
            else if (!string.IsNullOrEmpty(cameraName))
            {
                GameObject go = GameObject.Find(cameraName);
                if (go != null) cam = go.GetComponent<Camera>();
            }
            else
            {
                cam = Camera.main;
            }

            if (cam == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse("No Camera found to configure.", "camera_not_found");
            }

            Undo.RecordObject(cam, "Configure Camera Frustum and Culling");

            if (useOcclusionCulling.HasValue)
            {
                cam.useOcclusionCulling = useOcclusionCulling.Value;
            }

            if (farClipPlane.HasValue)
            {
                cam.farClipPlane = Mathf.Max(1f, farClipPlane.Value);
            }

            if (nearClipPlane.HasValue)
            {
                cam.nearClipPlane = Mathf.Max(0.01f, nearClipPlane.Value);
            }

            if (fieldOfView.HasValue)
            {
                cam.fieldOfView = Mathf.Clamp(fieldOfView.Value, 1f, 179f);
            }

            if (orthographic.HasValue)
            {
                cam.orthographic = orthographic.Value;
            }

            if (orthographicSize.HasValue)
            {
                cam.orthographicSize = Mathf.Max(0.01f, orthographicSize.Value);
            }

            if (cullingMask.HasValue)
            {
                cam.cullingMask = cullingMask.Value;
            }

            if (layerDistances != null)
            {
                float[] distances = cam.layerCullDistances;
                if (distances == null || distances.Length != 32)
                {
                    distances = new float[32];
                }

                foreach (var item in layerDistances)
                {
                    JObject obj = item as JObject;
                    if (obj == null) continue;

                    string layerName = obj["layer"]?.ToObject<string>();
                    int? layerIdx = obj["layerIndex"]?.ToObject<int?>();
                    float dist = obj["distance"]?.ToObject<float>() ?? 0f;

                    int targetIdx = -1;
                    if (layerIdx.HasValue && layerIdx.Value >= 0 && layerIdx.Value < 32)
                    {
                        targetIdx = layerIdx.Value;
                    }
                    else if (!string.IsNullOrEmpty(layerName))
                    {
                        targetIdx = LayerMask.NameToLayer(layerName);
                    }

                    if (targetIdx >= 0 && targetIdx < 32)
                    {
                        distances[targetIdx] = Mathf.Max(0f, dist);
                    }
                }

                cam.layerCullDistances = distances;
            }

            EditorUtility.SetDirty(cam.gameObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            JObject response = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured frustum and culling on Camera '{cam.name}'",
                ["cameraName"] = cam.name,
                ["fieldOfView"] = cam.fieldOfView,
                ["orthographic"] = cam.orthographic,
                ["orthographicSize"] = cam.orthographicSize,
                ["useOcclusionCulling"] = cam.useOcclusionCulling,
                ["farClipPlane"] = cam.farClipPlane,
                ["nearClipPlane"] = cam.nearClipPlane,
                ["cullingMask"] = cam.cullingMask
            };

            // Perform Frustum visibility check on target object if requested
            if (testTargetId.HasValue || !string.IsNullOrEmpty(testTargetPath))
            {
                JObject findTargetErr = GameObjectToolUtils.FindGameObject(testTargetId, testTargetPath, out GameObject testTarget, out string targetInfo);
                if (findTargetErr == null && testTarget != null)
                {
                    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    Bounds bounds;

                    Renderer r = testTarget.GetComponentInChildren<Renderer>();
                    Collider c = testTarget.GetComponentInChildren<Collider>();

                    if (r != null) bounds = r.bounds;
                    else if (c != null) bounds = c.bounds;
                    else bounds = new Bounds(testTarget.transform.position, Vector3.one * 0.5f);

                    bool inFrustum = GeometryUtility.TestPlanesAABB(planes, bounds);

                    response["testTarget"] = new JObject
                    {
                        ["name"] = testTarget.name,
                        ["inFrustum"] = inFrustum,
                        ["boundsCenter"] = new JObject { ["x"] = bounds.center.x, ["y"] = bounds.center.y, ["z"] = bounds.center.z },
                        ["boundsExtents"] = new JObject { ["x"] = bounds.extents.x, ["y"] = bounds.extents.y, ["z"] = bounds.extents.z }
                    };
                }
            }

            McpLogger.LogInfo($"[MCP Unity] Configured frustum & culling on Camera '{cam.name}'" + (reason != null ? $" — {reason}" : ""));

            return response;
        }
    }
}
