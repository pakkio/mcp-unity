using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    internal static class SpatialToolUtils
    {
        public static bool TryGetBounds(GameObject gameObject, bool includeInactive, out Bounds bounds)
        {
            bool hasBounds = false;
            bounds = new Bounds();
            foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(includeInactive))
            {
                if (!renderer.enabled) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!hasBounds)
            {
                foreach (Collider collider in gameObject.GetComponentsInChildren<Collider>(includeInactive))
                {
                    if (!collider.enabled) continue;
                    if (!hasBounds) { bounds = collider.bounds; hasBounds = true; }
                    else bounds.Encapsulate(collider.bounds);
                }
            }
            return hasBounds;
        }

        public static JObject BoundsJson(Bounds bounds)
        {
            return new JObject
            {
                ["center"] = VectorJson(bounds.center),
                ["size"] = VectorJson(bounds.size),
                ["min"] = VectorJson(bounds.min),
                ["max"] = VectorJson(bounds.max)
            };
        }

        public static JObject VectorJson(Vector3 value)
        {
            return new JObject { ["x"] = value.x, ["y"] = value.y, ["z"] = value.z };
        }

        public static JObject FindError(string message, string type = "validation_error")
        {
            return McpUnitySocketHandler.CreateErrorResponse(message, type);
        }
    }

    public class GetBoundsTool : McpToolBase
    {
        public GetBoundsTool()
        {
            Name = "get_bounds";
            Description = "Gets combined world-space bounds for a GameObject's renderers or colliders.";
        }

        public override JObject Execute(JObject parameters)
        {
            var result = TransformToolUtils.FindGameObject(parameters);
            if (result.Error != null) return result.Error;
            bool includeInactive = parameters["includeInactive"]?.ToObject<bool>() ?? false;
            if (!SpatialToolUtils.TryGetBounds(result.GameObject, includeInactive, out Bounds bounds))
                return SpatialToolUtils.FindError($"GameObject '{result.GameObject.name}' has no renderer or collider bounds", "not_found_error");

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Bounds calculated for '{result.GameObject.name}'.",
                ["instanceId"] = UnityObjectId.GetObjectId(result.GameObject),
                ["name"] = result.GameObject.name,
                ["path"] = TransformToolUtils.GetGameObjectPath(result.GameObject),
                ["bounds"] = SpatialToolUtils.BoundsJson(bounds)
            };
        }
    }

    public class PlaceNextToTool : McpToolBase
    {
        public PlaceNextToTool()
        {
            Name = "place_next_to";
            Description = "Places one GameObject next to another using world or reference-local directions and an edge-to-edge distance in meters.";
        }

        public override JObject Execute(JObject parameters)
        {
            var sourceResult = TransformToolUtils.FindGameObject(parameters);
            if (sourceResult.Error != null) return sourceResult.Error;
            string referencePath = parameters["referencePath"]?.ToObject<string>();
            int? referenceId = parameters["referenceId"]?.ToObject<int?>();
            GameObject reference = referenceId.HasValue
                ? UnityObjectId.ObjectFromId(referenceId.Value) as GameObject
                : (!string.IsNullOrEmpty(referencePath) ? GameObject.Find(referencePath) : null);
            if (reference == null)
                return SpatialToolUtils.FindError("Either 'referenceId' or 'referencePath' must identify the reference GameObject", "validation_error");

            string directionName = parameters["direction"]?.ToObject<string>()?.ToLowerInvariant() ?? "right";
            float distance = parameters["distance"]?.ToObject<float>() ?? 0f;
            bool useReferenceRotation = parameters["useReferenceRotation"]?.ToObject<bool>() ?? true;
            Vector3 direction;
            switch (directionName)
            {
                case "left": direction = Vector3.left; break;
                case "right": direction = Vector3.right; break;
                case "forward": direction = Vector3.forward; break;
                case "back": case "backward": direction = Vector3.back; break;
                case "above": case "up": direction = Vector3.up; break;
                case "below": case "down": direction = Vector3.down; break;
                default: return SpatialToolUtils.FindError("direction must be left, right, forward, back, above, or below");
            }
            if (useReferenceRotation && directionName != "above" && directionName != "up" && directionName != "below" && directionName != "down")
                direction = reference.transform.TransformDirection(direction);
            direction.Normalize();

            if (!SpatialToolUtils.TryGetBounds(sourceResult.GameObject, false, out Bounds sourceBounds) ||
                !SpatialToolUtils.TryGetBounds(reference, false, out Bounds referenceBounds))
                return SpatialToolUtils.FindError("Both GameObjects must have renderer or collider bounds", "not_found_error");

            float referenceExtent = Vector3.Dot(Vector3.Scale(referenceBounds.extents, Abs(direction)), Vector3.one);
            float sourceExtent = Vector3.Dot(Vector3.Scale(sourceBounds.extents, Abs(direction)), Vector3.one);
            Vector3 desiredCenter = referenceBounds.center + direction * (referenceExtent + sourceExtent + Mathf.Max(0f, distance));
            Undo.RecordObject(sourceResult.GameObject.transform, "Place GameObject Next To");
            sourceResult.GameObject.transform.position += desiredCenter - sourceBounds.center;
            EditorUtility.SetDirty(sourceResult.GameObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            SpatialToolUtils.TryGetBounds(sourceResult.GameObject, false, out Bounds finalBounds);
            McpLogger.LogInfo($"{Name}: placed '{sourceResult.GameObject.name}' {distance}m {directionName} of '{reference.name}'");
            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Placed '{sourceResult.GameObject.name}' {distance}m {directionName} of '{reference.name}'.",
                ["instanceId"] = UnityObjectId.GetObjectId(sourceResult.GameObject),
                ["position"] = SpatialToolUtils.VectorJson(sourceResult.GameObject.transform.position),
                ["bounds"] = SpatialToolUtils.BoundsJson(finalBounds),
                ["reference"] = reference.name
            };
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }

    public class FindLocalAssetsTool : McpToolBase
    {
        public FindLocalAssetsTool()
        {
            Name = "find_local_assets";
            Description = "Finds local assets in the Unity project by name, type, or extension.";
        }

        public override JObject Execute(JObject parameters)
        {
            string query = parameters["query"]?.ToObject<string>()?.ToLowerInvariant() ?? "";
            string extension = parameters["extension"]?.ToObject<string>()?.ToLowerInvariant();
            int maxResults = Mathf.Clamp(parameters["maxResults"]?.ToObject<int>() ?? 100, 1, 500);
            string root = parameters["root"]?.ToObject<string>() ?? "Assets";
            if (!root.Equals("Assets", StringComparison.OrdinalIgnoreCase) && !root.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return SpatialToolUtils.FindError("root must be an Assets-relative project path");

            var assets = new JArray();
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || path.EndsWith("/")) continue;
                string fileName = Path.GetFileName(path);
                if (!fileName.ToLowerInvariant().Contains(query)) continue;
                if (!string.IsNullOrEmpty(extension) && !path.EndsWith(extension.StartsWith(".") ? extension : "." + extension, StringComparison.OrdinalIgnoreCase)) continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                assets.Add(new JObject { ["path"] = path, ["guid"] = guid, ["type"] = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? "Unknown" });
                if (assets.Count >= maxResults) break;
            }
            return new JObject { ["success"] = true, ["type"] = "text", ["message"] = $"Found {assets.Count} local asset(s).", ["assets"] = assets };
        }
    }
}
