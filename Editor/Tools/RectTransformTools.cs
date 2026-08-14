using System;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for configuring RectTransform properties on UI elements.
    /// </summary>
    public class SetRectTransformTool : McpToolBase
    {
        public SetRectTransformTool()
        {
            Name = "set_rect_transform";
            Description = "Sets RectTransform UI layout properties (anchor presets, anchoredPosition, sizeDelta, pivot, anchorMin/Max, offsets) on UI Canvas elements.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string anchorPreset = parameters["anchorPreset"]?.ToObject<string>()?.ToLowerInvariant();
            JObject anchoredPosObj = parameters["anchoredPosition"] as JObject;
            JObject sizeDeltaObj = parameters["sizeDelta"] as JObject;
            JObject anchorMinObj = parameters["anchorMin"] as JObject;
            JObject anchorMaxObj = parameters["anchorMax"] as JObject;
            JObject pivotObj = parameters["pivot"] as JObject;
            JObject offsetMinObj = parameters["offsetMin"] as JObject;
            JObject offsetMaxObj = parameters["offsetMax"] as JObject;
            string reason = parameters["reason"]?.ToObject<string>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string identifierInfo);
            if (findError != null) return findError;

            RectTransform rectTransform = targetObject.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject '{targetObject.name}' ({identifierInfo}) does not have a RectTransform component. It is not a UI element.",
                    "not_a_rect_transform"
                );
            }

            Undo.RecordObject(rectTransform, "Set RectTransform Layout");

            // Apply anchor preset if specified
            if (!string.IsNullOrEmpty(anchorPreset))
            {
                ApplyAnchorPreset(rectTransform, anchorPreset);
            }

            // Apply explicit properties if provided
            if (anchorMinObj != null)
            {
                rectTransform.anchorMin = new Vector2(
                    anchorMinObj["x"]?.ToObject<float>() ?? rectTransform.anchorMin.x,
                    anchorMinObj["y"]?.ToObject<float>() ?? rectTransform.anchorMin.y
                );
            }

            if (anchorMaxObj != null)
            {
                rectTransform.anchorMax = new Vector2(
                    anchorMaxObj["x"]?.ToObject<float>() ?? rectTransform.anchorMax.x,
                    anchorMaxObj["y"]?.ToObject<float>() ?? rectTransform.anchorMax.y
                );
            }

            if (pivotObj != null)
            {
                rectTransform.pivot = new Vector2(
                    pivotObj["x"]?.ToObject<float>() ?? rectTransform.pivot.x,
                    pivotObj["y"]?.ToObject<float>() ?? rectTransform.pivot.y
                );
            }

            if (anchoredPosObj != null)
            {
                rectTransform.anchoredPosition = new Vector2(
                    anchoredPosObj["x"]?.ToObject<float>() ?? rectTransform.anchoredPosition.x,
                    anchoredPosObj["y"]?.ToObject<float>() ?? rectTransform.anchoredPosition.y
                );
            }

            if (sizeDeltaObj != null)
            {
                rectTransform.sizeDelta = new Vector2(
                    sizeDeltaObj["x"]?.ToObject<float>() ?? rectTransform.sizeDelta.x,
                    sizeDeltaObj["y"]?.ToObject<float>() ?? rectTransform.sizeDelta.y
                );
            }

            if (offsetMinObj != null)
            {
                rectTransform.offsetMin = new Vector2(
                    offsetMinObj["x"]?.ToObject<float>() ?? rectTransform.offsetMin.x,
                    offsetMinObj["y"]?.ToObject<float>() ?? rectTransform.offsetMin.y
                );
            }

            if (offsetMaxObj != null)
            {
                rectTransform.offsetMax = new Vector2(
                    offsetMaxObj["x"]?.ToObject<float>() ?? rectTransform.offsetMax.x,
                    offsetMaxObj["y"]?.ToObject<float>() ?? rectTransform.offsetMax.y
                );
            }

            EditorUtility.SetDirty(targetObject);
            if (PrefabUtility.IsPartOfAnyPrefab(targetObject))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(rectTransform);
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured RectTransform on '{targetObject.name}'" + (reason != null ? $" — {reason}" : ""));

            JObject response = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully updated RectTransform on '{targetObject.name}'",
                ["anchoredPosition"] = new JObject { ["x"] = rectTransform.anchoredPosition.x, ["y"] = rectTransform.anchoredPosition.y },
                ["sizeDelta"] = new JObject { ["x"] = rectTransform.sizeDelta.x, ["y"] = rectTransform.sizeDelta.y },
                ["anchorMin"] = new JObject { ["x"] = rectTransform.anchorMin.x, ["y"] = rectTransform.anchorMin.y },
                ["anchorMax"] = new JObject { ["x"] = rectTransform.anchorMax.x, ["y"] = rectTransform.anchorMax.y },
                ["pivot"] = new JObject { ["x"] = rectTransform.pivot.x, ["y"] = rectTransform.pivot.y },
                ["rect"] = new JObject
                {
                    ["x"] = rectTransform.rect.x,
                    ["y"] = rectTransform.rect.y,
                    ["width"] = rectTransform.rect.width,
                    ["height"] = rectTransform.rect.height
                }
            };

            if (EditorApplication.isPlaying)
            {
                response["warning"] = "Unity is in Play Mode. RectTransform edits will reset when Play Mode stops unless saved to a prefab.";
            }

            return response;
        }

        private void ApplyAnchorPreset(RectTransform rt, string preset)
        {
            switch (preset)
            {
                case "top_left":
                    rt.anchorMin = new Vector2(0, 1);
                    rt.anchorMax = new Vector2(0, 1);
                    rt.pivot = new Vector2(0, 1);
                    break;
                case "top_center":
                    rt.anchorMin = new Vector2(0.5f, 1);
                    rt.anchorMax = new Vector2(0.5f, 1);
                    rt.pivot = new Vector2(0.5f, 1);
                    break;
                case "top_right":
                    rt.anchorMin = new Vector2(1, 1);
                    rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(1, 1);
                    break;
                case "middle_left":
                    rt.anchorMin = new Vector2(0, 0.5f);
                    rt.anchorMax = new Vector2(0, 0.5f);
                    rt.pivot = new Vector2(0, 0.5f);
                    break;
                case "center":
                case "middle_center":
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    break;
                case "middle_right":
                    rt.anchorMin = new Vector2(1, 0.5f);
                    rt.anchorMax = new Vector2(1, 0.5f);
                    rt.pivot = new Vector2(1, 0.5f);
                    break;
                case "bottom_left":
                    rt.anchorMin = new Vector2(0, 0);
                    rt.anchorMax = new Vector2(0, 0);
                    rt.pivot = new Vector2(0, 0);
                    break;
                case "bottom_center":
                    rt.anchorMin = new Vector2(0.5f, 0);
                    rt.anchorMax = new Vector2(0.5f, 0);
                    rt.pivot = new Vector2(0.5f, 0);
                    break;
                case "bottom_right":
                    rt.anchorMin = new Vector2(1, 0);
                    rt.anchorMax = new Vector2(1, 0);
                    rt.pivot = new Vector2(1, 0);
                    break;
                case "stretch_all":
                    rt.anchorMin = new Vector2(0, 0);
                    rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    break;
                case "top_stretch":
                    rt.anchorMin = new Vector2(0, 1);
                    rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(0.5f, 1);
                    break;
                case "bottom_stretch":
                    rt.anchorMin = new Vector2(0, 0);
                    rt.anchorMax = new Vector2(1, 0);
                    rt.pivot = new Vector2(0.5f, 0);
                    break;
                case "left_stretch":
                    rt.anchorMin = new Vector2(0, 0);
                    rt.anchorMax = new Vector2(0, 1);
                    rt.pivot = new Vector2(0, 0.5f);
                    break;
                case "right_stretch":
                    rt.anchorMin = new Vector2(1, 0);
                    rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(1, 0.5f);
                    break;
                case "stretch_horizontal":
                    rt.anchorMin = new Vector2(0, 0.5f);
                    rt.anchorMax = new Vector2(1, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    break;
                case "stretch_vertical":
                    rt.anchorMin = new Vector2(0.5f, 0);
                    rt.anchorMax = new Vector2(0.5f, 1);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    break;
            }
        }
    }
}
