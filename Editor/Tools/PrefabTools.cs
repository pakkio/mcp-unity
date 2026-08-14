using System;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for applying modifications from a prefab instance in the scene back to the source prefab asset.
    /// </summary>
    public class ApplyPrefabOverridesTool : McpToolBase
    {
        public ApplyPrefabOverridesTool()
        {
            Name = "apply_prefab_overrides";
            Description = "Applies modifications/overrides from a scene prefab instance back to its source Prefab Asset.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string reason = parameters["reason"]?.ToObject<string>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string identifierInfo);
            if (findError != null) return findError;

            if (!PrefabUtility.IsPartOfAnyPrefab(targetObject))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject '{targetObject.name}' ({identifierInfo}) is not part of a Prefab instance.",
                    "not_a_prefab"
                );
            }

            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(targetObject);
            if (root == null)
            {
                root = targetObject;
            }

            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (string.IsNullOrEmpty(assetPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Could not locate source Prefab Asset for '{targetObject.name}'.",
                    "prefab_asset_not_found"
                );
            }

            try
            {
                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.UserAction);
                AssetDatabase.SaveAssets();

                McpLogger.LogInfo($"[MCP Unity] Applied prefab overrides from '{root.name}' to '{assetPath}'" + (reason != null ? $" — {reason}" : ""));

                JObject response = new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully applied prefab overrides from '{root.name}' to '{assetPath}'",
                    ["assetPath"] = assetPath,
                    ["rootName"] = root.name
                };

                if (EditorApplication.isPlaying)
                {
                    response["warning"] = "Unity is in Play Mode. Prefab asset on disk was updated, but runtime scene instances might reset on stop.";
                }

                return response;
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to apply prefab overrides: {ex.Message}",
                    "prefab_apply_error"
                );
            }
        }
    }

    /// <summary>
    /// Tool for reverting modifications on a prefab instance back to the prefab asset defaults.
    /// </summary>
    public class RevertPrefabOverridesTool : McpToolBase
    {
        public RevertPrefabOverridesTool()
        {
            Name = "revert_prefab_overrides";
            Description = "Reverts property and component overrides on a scene prefab instance back to the prefab asset defaults.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string reason = parameters["reason"]?.ToObject<string>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string identifierInfo);
            if (findError != null) return findError;

            if (!PrefabUtility.IsPartOfAnyPrefab(targetObject))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject '{targetObject.name}' ({identifierInfo}) is not part of a Prefab instance.",
                    "not_a_prefab"
                );
            }

            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(targetObject);
            if (root == null)
            {
                root = targetObject;
            }

            try
            {
                PrefabUtility.RevertPrefabInstance(root, InteractionMode.UserAction);
                EditorUtility.SetDirty(root);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                McpLogger.LogInfo($"[MCP Unity] Reverted prefab overrides on '{root.name}'" + (reason != null ? $" — {reason}" : ""));

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully reverted all prefab overrides on '{root.name}'",
                    ["rootName"] = root.name
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to revert prefab overrides: {ex.Message}",
                    "prefab_revert_error"
                );
            }
        }
    }

    /// <summary>
    /// Tool for unpacking a prefab instance in the scene into normal GameObjects.
    /// </summary>
    public class UnpackPrefabTool : McpToolBase
    {
        public UnpackPrefabTool()
        {
            Name = "unpack_prefab";
            Description = "Unpacks a prefab instance in the scene into regular GameObjects, disconnecting it from the prefab asset.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string unpackModeStr = parameters["unpackMode"]?.ToObject<string>()?.ToLowerInvariant() ?? "outermost";
            string reason = parameters["reason"]?.ToObject<string>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string identifierInfo);
            if (findError != null) return findError;

            if (!PrefabUtility.IsPartOfAnyPrefab(targetObject))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject '{targetObject.name}' ({identifierInfo}) is not part of a Prefab instance.",
                    "not_a_prefab"
                );
            }

            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(targetObject);
            if (root == null)
            {
                root = targetObject;
            }

            PrefabUnpackMode unpackMode = unpackModeStr == "completely"
                ? PrefabUnpackMode.Completely
                : PrefabUnpackMode.OutermostRoot;

            try
            {
                PrefabUtility.UnpackPrefabInstance(root, unpackMode, InteractionMode.UserAction);
                EditorUtility.SetDirty(root);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                McpLogger.LogInfo($"[MCP Unity] Unpacked prefab '{root.name}' (mode: {unpackMode})" + (reason != null ? $" — {reason}" : ""));

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully unpacked prefab '{root.name}' ({unpackModeStr})",
                    ["rootName"] = root.name,
                    ["unpackMode"] = unpackModeStr
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to unpack prefab: {ex.Message}",
                    "prefab_unpack_error"
                );
            }
        }
    }
}
