using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for querying and managing Unity Tags, Layers, and Sorting Layers.
    /// </summary>
    public class ManageTagsAndLayersTool : McpToolBase
    {
        public ManageTagsAndLayersTool()
        {
            Name = "manage_tags_and_layers";
            Description = "Gets or adds Unity Tags, Physics Layers (indices 8-31), and Sorting Layers.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "get";
            string name = parameters["name"]?.ToObject<string>();
            int? layerIndex = parameters["layerIndex"]?.ToObject<int?>();
            string reason = parameters["reason"]?.ToObject<string>();

            switch (action)
            {
                case "get":
                    return HandleGet();

                case "add_tag":
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return McpUnitySocketHandler.CreateErrorResponse("Parameter 'name' is required for action 'add_tag'.", "validation_error");
                    }
                    return HandleAddTag(name.Trim(), reason);

                case "add_layer":
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return McpUnitySocketHandler.CreateErrorResponse("Parameter 'name' is required for action 'add_layer'.", "validation_error");
                    }
                    return HandleAddLayer(name.Trim(), layerIndex, reason);

                case "add_sorting_layer":
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return McpUnitySocketHandler.CreateErrorResponse("Parameter 'name' is required for action 'add_sorting_layer'.", "validation_error");
                    }
                    return HandleAddSortingLayer(name.Trim(), reason);

                case "set_collision_matrix":
                    return HandleSetCollisionMatrix(parameters, reason);

                case "set_object_layer":
                    return HandleSetObjectLayer(parameters, reason);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'get', 'add_tag', 'add_layer', 'add_sorting_layer', 'set_collision_matrix', 'set_object_layer'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleGet()
        {
            // Tags
            JArray tagsArray = new JArray();
            foreach (string tag in InternalEditorUtility.tags)
            {
                tagsArray.Add(tag);
            }

            // Physics Layers (all 32 slots)
            JArray layersArray = new JArray();
            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                layersArray.Add(new JObject
                {
                    ["index"] = i,
                    ["name"] = string.IsNullOrEmpty(layerName) ? null : layerName,
                    ["isBuiltIn"] = i < 8,
                    ["isUserLayer"] = i >= 8
                });
            }

            // Sorting Layers
            JArray sortingLayersArray = new JArray();
            foreach (var sortingLayer in SortingLayer.layers)
            {
                sortingLayersArray.Add(new JObject
                {
                    ["id"] = sortingLayer.id,
                    ["name"] = sortingLayer.name,
                    ["value"] = sortingLayer.value
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Retrieved {tagsArray.Count} tags, 32 layer slots, and {sortingLayersArray.Count} sorting layers.",
                ["tags"] = tagsArray,
                ["layers"] = layersArray,
                ["sortingLayers"] = sortingLayersArray
            };
        }

        private JObject HandleAddTag(string tagName, string reason)
        {
            // Check if tag already exists
            foreach (string tag in InternalEditorUtility.tags)
            {
                if (string.Equals(tag, tagName, StringComparison.OrdinalIgnoreCase))
                {
                    return new JObject
                    {
                        ["success"] = true,
                        ["type"] = "text",
                        ["message"] = $"Tag '{tagName}' already exists.",
                        ["tag"] = tag,
                        ["alreadyExisted"] = true
                    };
                }
            }

            try
            {
                InternalEditorUtility.AddTag(tagName);
                McpLogger.LogInfo($"[MCP Unity] Added new tag '{tagName}'" + (reason != null ? $" — {reason}" : ""));

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully added tag '{tagName}'",
                    ["tag"] = tagName,
                    ["alreadyExisted"] = false
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Failed to add tag '{tagName}': {ex.Message}", "tag_error");
            }
        }

        private JObject HandleAddLayer(string layerName, int? requestedIndex, string reason)
        {
            // Check if layer already exists
            for (int i = 0; i < 32; i++)
            {
                string existing = LayerMask.LayerToName(i);
                if (string.Equals(existing, layerName, StringComparison.OrdinalIgnoreCase))
                {
                    return new JObject
                    {
                        ["success"] = true,
                        ["type"] = "text",
                        ["message"] = $"Layer '{layerName}' already exists at index {i}.",
                        ["layerIndex"] = i,
                        ["name"] = existing,
                        ["alreadyExisted"] = true
                    };
                }
            }

            SerializedObject tagManager = GetTagManager();
            if (tagManager == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse("Could not load ProjectSettings/TagManager.asset", "tagmanager_not_found");
            }

            SerializedProperty layersProp = tagManager.FindProperty("layers");
            if (layersProp == null || !layersProp.isArray)
            {
                return McpUnitySocketHandler.CreateErrorResponse("Could not find 'layers' property in TagManager", "tagmanager_error");
            }

            int targetIndex = -1;

            if (requestedIndex.HasValue)
            {
                if (requestedIndex.Value < 8 || requestedIndex.Value > 31)
                {
                    return McpUnitySocketHandler.CreateErrorResponse("User layers must be between index 8 and 31 (0-7 are built-in).", "validation_error");
                }

                SerializedProperty elem = layersProp.GetArrayElementAtIndex(requestedIndex.Value);
                if (!string.IsNullOrEmpty(elem.stringValue))
                {
                    return McpUnitySocketHandler.CreateErrorResponse($"Layer slot {requestedIndex.Value} is already occupied by '{elem.stringValue}'.", "slot_occupied");
                }

                targetIndex = requestedIndex.Value;
            }
            else
            {
                // Find first free slot between 8 and 31
                for (int i = 8; i < 32; i++)
                {
                    if (i < layersProp.arraySize)
                    {
                        SerializedProperty elem = layersProp.GetArrayElementAtIndex(i);
                        if (string.IsNullOrEmpty(elem.stringValue))
                        {
                            targetIndex = i;
                            break;
                        }
                    }
                }
            }

            if (targetIndex == -1)
            {
                return McpUnitySocketHandler.CreateErrorResponse("No available user layer slots found (slots 8-31 are all occupied).", "no_slots_available");
            }

            SerializedProperty targetProp = layersProp.GetArrayElementAtIndex(targetIndex);
            targetProp.stringValue = layerName;
            tagManager.ApplyModifiedProperties();

            McpLogger.LogInfo($"[MCP Unity] Assigned layer '{layerName}' to slot {targetIndex}" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully added layer '{layerName}' at index {targetIndex}",
                ["layerIndex"] = targetIndex,
                ["name"] = layerName,
                ["alreadyExisted"] = false
            };
        }

        private JObject HandleAddSortingLayer(string layerName, string reason)
        {
            foreach (var sortingLayer in SortingLayer.layers)
            {
                if (string.Equals(sortingLayer.name, layerName, StringComparison.OrdinalIgnoreCase))
                {
                    return new JObject
                    {
                        ["success"] = true,
                        ["type"] = "text",
                        ["message"] = $"Sorting layer '{layerName}' already exists with ID {sortingLayer.id}.",
                        ["sortingLayerId"] = sortingLayer.id,
                        ["name"] = sortingLayer.name,
                        ["alreadyExisted"] = true
                    };
                }
            }

            SerializedObject tagManager = GetTagManager();
            if (tagManager == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse("Could not load ProjectSettings/TagManager.asset", "tagmanager_not_found");
            }

            SerializedProperty sortingLayersProp = tagManager.FindProperty("m_SortingLayers");
            if (sortingLayersProp == null || !sortingLayersProp.isArray)
            {
                return McpUnitySocketHandler.CreateErrorResponse("Could not find 'm_SortingLayers' in TagManager", "tagmanager_error");
            }

            int newIndex = sortingLayersProp.arraySize;
            sortingLayersProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newElem = sortingLayersProp.GetArrayElementAtIndex(newIndex);

            SerializedProperty nameProp = newElem.FindPropertyRelative("name");
            SerializedProperty uniqueIdProp = newElem.FindPropertyRelative("uniqueID");

            if (nameProp != null) nameProp.stringValue = layerName;
            if (uniqueIdProp != null) uniqueIdProp.intValue = UnityEngine.Random.Range(100000, 999999);

            tagManager.ApplyModifiedProperties();

            McpLogger.LogInfo($"[MCP Unity] Added sorting layer '{layerName}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully added sorting layer '{layerName}'",
                ["name"] = layerName,
                ["alreadyExisted"] = false
            };
        }

        private JObject HandleSetCollisionMatrix(JObject parameters, string reason)
        {
            string layerAName = parameters["layerA"]?.ToObject<string>();
            int? layerAIdx = parameters["layerAIndex"]?.ToObject<int?>();

            string layerBName = parameters["layerB"]?.ToObject<string>();
            int? layerBIdx = parameters["layerBIndex"]?.ToObject<int?>();

            bool ignoreCollision = parameters["ignoreCollision"]?.ToObject<bool?>() ?? true;

            int idxA = ResolveLayerIndex(layerAName, layerAIdx);
            int idxB = ResolveLayerIndex(layerBName, layerBIdx);

            if (idxA < 0 || idxA > 31)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Invalid or unresolvable layer A ('{layerAName ?? layerAIdx?.ToString()}').", "invalid_layer");
            }

            if (idxB < 0 || idxB > 31)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Invalid or unresolvable layer B ('{layerBName ?? layerBIdx?.ToString()}').", "invalid_layer");
            }

            Physics.IgnoreLayerCollision(idxA, idxB, ignoreCollision);
            string nameA = LayerMask.LayerToName(idxA);
            string nameB = LayerMask.LayerToName(idxB);

            McpLogger.LogInfo($"[MCP Unity] Set collision between Layer {idxA} ('{nameA}') and Layer {idxB} ('{nameB}') to ignore={ignoreCollision}" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully updated collision matrix: Layer '{nameA}' ({idxA}) and Layer '{nameB}' ({idxB}) are now {(ignoreCollision ? "IGNORED (no collision)" : "COLLIDING")}",
                ["layerA"] = nameA,
                ["layerAIndex"] = idxA,
                ["layerB"] = nameB,
                ["layerBIndex"] = idxB,
                ["ignoreCollision"] = ignoreCollision
            };
        }

        private JObject HandleSetObjectLayer(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string layerName = parameters["layer"]?.ToObject<string>();
            int? layerIdx = parameters["layerIndex"]?.ToObject<int?>();
            bool includeChildren = parameters["includeChildren"]?.ToObject<bool?>() ?? true;

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            int targetLayer = ResolveLayerIndex(layerName, layerIdx);
            if (targetLayer < 0 || targetLayer > 31)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Invalid or unresolvable layer ('{layerName ?? layerIdx?.ToString()}').", "invalid_layer");
            }

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
                Undo.RecordObject(go, "Set GameObject Layer");
                go.layer = targetLayer;
                EditorUtility.SetDirty(go);
            }

            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            string resolvedName = LayerMask.LayerToName(targetLayer);

            McpLogger.LogInfo($"[MCP Unity] Set layer '{resolvedName}' ({targetLayer}) on '{targetObject.name}' ({objectsToUpdate.Count} objects)" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully set layer to '{resolvedName}' ({targetLayer}) on '{targetObject.name}' and {objectsToUpdate.Count - 1} children",
                ["targetObject"] = targetObject.name,
                ["layerName"] = resolvedName,
                ["layerIndex"] = targetLayer,
                ["objectsUpdated"] = objectsToUpdate.Count
            };
        }

        private int ResolveLayerIndex(string layerName, int? layerIndex)
        {
            if (layerIndex.HasValue && layerIndex.Value >= 0 && layerIndex.Value <= 31)
            {
                return layerIndex.Value;
            }

            if (!string.IsNullOrEmpty(layerName))
            {
                return LayerMask.NameToLayer(layerName);
            }

            return -1;
        }

        private SerializedObject GetTagManager()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                return null;
            }
            return new SerializedObject(assets[0]);
        }
    }
}
