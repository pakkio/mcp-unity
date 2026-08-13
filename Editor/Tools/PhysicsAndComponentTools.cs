using System;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for executing a Physics Raycast in the scene
    /// </summary>
    public class RaycastQueryTool : McpToolBase
    {
        public RaycastQueryTool()
        {
            Name = "raycast_query";
            Description = "Executes a physics raycast query in the Unity scene and returns hit information";
        }

        public override JObject Execute(JObject parameters)
        {
            JObject originObj = parameters["origin"] as JObject;
            JObject directionObj = parameters["direction"] as JObject;
            float maxDistance = parameters["maxDistance"]?.ToObject<float>() ?? 1000f;
            int layerMask = parameters["layerMask"]?.ToObject<int>() ?? Physics.AllLayers;

            if (originObj == null || directionObj == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameters 'origin' and 'direction' containing 'x', 'y', 'z' must be provided",
                    "validation_error"
                );
            }

            try
            {
                Vector3 origin = new Vector3(
                    originObj["x"]?.ToObject<float>() ?? 0f,
                    originObj["y"]?.ToObject<float>() ?? 0f,
                    originObj["z"]?.ToObject<float>() ?? 0f
                );

                Vector3 direction = new Vector3(
                    directionObj["x"]?.ToObject<float>() ?? 0f,
                    directionObj["y"]?.ToObject<float>() ?? 0f,
                    directionObj["z"]?.ToObject<float>() ?? 0f
                );

                if (direction == Vector3.zero)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Raycast direction cannot be zero vector",
                        "validation_error"
                    );
                }

                direction.Normalize();

                // Editor transform edits (move_gameobject, set_transform, etc.) don't automatically
                // push updated colliders into the physics world until the next physics step - which
                // in Edit mode may never come. Without this, a raycast run immediately after moving
                // an object can hit its stale pre-move position.
                Physics.SyncTransforms();

                bool hit = Physics.Raycast(origin, direction, out RaycastHit hitInfo, maxDistance, layerMask);

                if (hit)
                {
                    return new JObject
                    {
                        ["success"] = true,
                        ["hit"] = true,
                        ["distance"] = hitInfo.distance,
                        ["point"] = new JObject
                        {
                            ["x"] = hitInfo.point.x,
                            ["y"] = hitInfo.point.y,
                            ["z"] = hitInfo.point.z
                        },
                        ["normal"] = new JObject
                        {
                            ["x"] = hitInfo.normal.x,
                            ["y"] = hitInfo.normal.y,
                            ["z"] = hitInfo.normal.z
                        },
                        ["gameObjectName"] = hitInfo.collider.gameObject.name,
                        ["gameObjectPath"] = GetGameObjectPath(hitInfo.collider.gameObject),
                        // Public ID scheme shared with every other tool - a raw GetInstanceID()
                        // is not resolvable by GameObjectResolver under MCP_UNITY_ENTITY_ID_API.
                        ["instanceId"] = UnityObjectId.GetObjectId(hitInfo.collider.gameObject)
                    };
                }
                else
                {
                    return new JObject
                    {
                        ["success"] = true,
                        ["hit"] = false,
                        ["message"] = "Raycast did not hit any collider."
                    };
                }
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error executing raycast: {ex.Message}",
                    "raycast_error"
                );
            }
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = obj.name + "/" + path;
            }
            return path;
        }
    }

    /// <summary>
    /// Tool to copy component values/setup from a source GameObject to a target GameObject
    /// </summary>
    public class CopyComponentTool : McpToolBase
    {
        public CopyComponentTool()
        {
            Name = "copy_component";
            Description = "Copies a component from a source GameObject to a target GameObject, preserving its field values";
        }

        public override JObject Execute(JObject parameters)
        {
            string componentType = parameters["componentType"]?.ToObject<string>();

            if (string.IsNullOrEmpty(componentType))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'componentType' must be provided",
                    "validation_error"
                );
            }

            try
            {
                GameObjectResolver.Result sourceResult = ResolveGameObject(parameters, "Source", "sourceInstanceId", "sourceObjectPath");
                if (sourceResult.Error != null) return sourceResult.Error;

                GameObjectResolver.Result targetResult = ResolveGameObject(parameters, "Target", "targetInstanceId", "targetObjectPath");
                if (targetResult.Error != null) return targetResult.Error;

                GameObject sourceObj = sourceResult.GameObject;
                GameObject targetObj = targetResult.GameObject;

                Component sourceComp = sourceObj.GetComponent(componentType);
                if (sourceComp == null)
                {
                    // Try finding component with case-insensitive search
                    foreach (var c in sourceObj.GetComponents<Component>())
                    {
                        if (c != null && c.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceComp = c;
                            break;
                        }
                    }
                }

                if (sourceComp == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Component of type '{componentType}' not found on source GameObject '{sourceObj.name}'",
                        "component_not_found"
                    );
                }

                // Copy component to internal clipboard
                if (!UnityEditorInternal.ComponentUtility.CopyComponent(sourceComp))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to copy component '{componentType}' from '{sourceObj.name}'",
                        "copy_failed"
                    );
                }

                // Paste on target
                Component targetComp = targetObj.GetComponent(sourceComp.GetType());
                bool alreadyExists = (targetComp != null);

                if (alreadyExists)
                {
                    if (UnityEditorInternal.ComponentUtility.PasteComponentValues(targetComp))
                    {
                        McpLogger.LogInfo($"[MCP Unity] Pasted component values of '{componentType}' from '{sourceObj.name}' to '{targetObj.name}'");
                        return new JObject
                        {
                            ["success"] = true,
                            ["message"] = $"Successfully pasted component values of '{componentType}' to '{targetObj.name}'",
                            ["action"] = "pasted_values"
                        };
                    }
                }
                else
                {
                    if (UnityEditorInternal.ComponentUtility.PasteComponentAsNew(targetObj))
                    {
                        McpLogger.LogInfo($"[MCP Unity] Added new component '{componentType}' from '{sourceObj.name}' to '{targetObj.name}'");
                        return new JObject
                        {
                            ["success"] = true,
                            ["message"] = $"Successfully added new component '{componentType}' to '{targetObj.name}'",
                            ["action"] = "added_new_component"
                        };
                    }
                }

                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to paste component '{componentType}' on '{targetObj.name}'",
                    "paste_failed"
                );
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error copying component: {ex.Message}",
                    "copy_error"
                );
            }
        }

        /// <summary>
        /// Resolve one end of the copy via the shared resolver, so instance IDs use the same
        /// public-ID scheme every other tool hands out (synthetic under MCP_UNITY_ENTITY_ID_API,
        /// raw instance IDs otherwise) and paths reach inactive objects in every loaded scene.
        /// </summary>
        private GameObjectResolver.Result ResolveGameObject(JObject parameters, string role, string instanceIdKey, string pathKey)
        {
            int? instanceId = parameters[instanceIdKey]?.ToObject<int?>();
            string path = parameters[pathKey]?.ToObject<string>();

            if (!instanceId.HasValue && string.IsNullOrEmpty(path))
            {
                return new GameObjectResolver.Result
                {
                    Error = McpUnitySocketHandler.CreateErrorResponse(
                        $"{role} GameObject not specified. Provide a valid '{instanceIdKey}' or '{pathKey}'.",
                        "validation_error"
                    )
                };
            }

            return GameObjectResolver.Find(instanceId, path);
        }
    }
}
