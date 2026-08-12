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

    public class ImportLocalFileTool : McpToolBase
    {
        public ImportLocalFileTool()
        {
            Name = "import_local_file";
            Description = "Copies a file from an external path into the Unity Assets folder and imports it. Returns the asset path.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string sourcePath = parameters["sourcePath"]?.ToObject<string>();
            if (string.IsNullOrEmpty(sourcePath))
                return SpatialToolUtils.FindError("'sourcePath' is required");

            string destFolder = parameters["destFolder"]?.ToObject<string>() ?? "Assets";
            if (!destFolder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                return SpatialToolUtils.FindError("'destFolder' must be Assets-relative");

            string newName = parameters["newName"]?.ToObject<string>();
            bool overwrite = parameters["overwrite"]?.ToObject<bool>() ?? false;

            if (!File.Exists(sourcePath))
                return SpatialToolUtils.FindError($"Source file not found: '{sourcePath}'", "not_found_error");

            string fileName = !string.IsNullOrEmpty(newName) ? newName : Path.GetFileName(sourcePath);
            string destPath = Path.Combine(destFolder, fileName);
            destPath = destPath.Replace('\\', '/');

            string fullDestPath = System.IO.Path.GetFullPath(destPath);
            string fullSourcePath = System.IO.Path.GetFullPath(sourcePath);

            if (File.Exists(fullDestPath) && !overwrite)
                return SpatialToolUtils.FindError($"Destination file already exists: '{destPath}'. Set overwrite=true to replace.", "validation_error");

            try
            {
                string fullDestDir = Path.GetDirectoryName(fullDestPath);
                if (!Directory.Exists(fullDestDir))
                    Directory.CreateDirectory(fullDestDir);

                File.Copy(fullSourcePath, fullDestPath, overwrite);
                AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);
                string guid = AssetDatabase.AssetPathToGUID(destPath);
                string typeName = AssetDatabase.GetMainAssetTypeAtPath(destPath)?.Name ?? "Unknown";

                McpLogger.LogInfo($"{Name}: imported '{sourcePath}' -> '{destPath}' ({typeName})");
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Imported '{fileName}' to '{destPath}'.",
                    ["assetPath"] = destPath,
                    ["guid"] = guid,
                    ["type"] = typeName
                };
            }
            catch (Exception ex)
            {
                return SpatialToolUtils.FindError($"Failed to import file: {ex.Message}", "import_error");
            }
        }
    }

    public class MeasureDistanceTool : McpToolBase
    {
        public MeasureDistanceTool()
        {
            Name = "measure_distance";
            Description = "Measures distance between two GameObjects (center-to-center and bounds edge-to-edge).";
        }

        public override JObject Execute(JObject parameters)
        {
            var sourceResult = TransformToolUtils.FindGameObject(parameters);
            if (sourceResult.Error != null) return sourceResult.Error;

            string refPath = parameters["referencePath"]?.ToObject<string>();
            int? refId = parameters["referenceId"]?.ToObject<int?>();
            GameObject reference = refId.HasValue
                ? UnityObjectId.ObjectFromId(refId.Value) as GameObject
                : (!string.IsNullOrEmpty(refPath) ? GameObject.Find(refPath) : null);
            if (reference == null)
                return SpatialToolUtils.FindError("Either 'referenceId' or 'referencePath' must identify the reference GameObject");

            Vector3 sourceCenter = sourceResult.GameObject.transform.position;
            Vector3 refCenter = reference.transform.position;
            float centerDistance = Vector3.Distance(sourceCenter, refCenter);

            JObject boundsInfo = null;
            float edgeDistance = -1f;
            bool sourceHasBounds = SpatialToolUtils.TryGetBounds(sourceResult.GameObject, false, out Bounds sourceBounds);
            bool refHasBounds = SpatialToolUtils.TryGetBounds(reference, false, out Bounds refBounds);

            if (sourceHasBounds && refHasBounds)
            {
                Vector3 sourceClosest = sourceBounds.ClosestPoint(refBounds.center);
                Vector3 refClosest = refBounds.ClosestPoint(sourceBounds.center);
                edgeDistance = Vector3.Distance(sourceClosest, refClosest);
                boundsInfo = new JObject
                {
                    ["sourceBounds"] = SpatialToolUtils.BoundsJson(sourceBounds),
                    ["referenceBounds"] = SpatialToolUtils.BoundsJson(refBounds)
                };
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Distance: {centerDistance:F3}m (center), {edgeDistance:F3}m (edge)",
                ["source"] = sourceResult.GameObject.name,
                ["reference"] = reference.name,
                ["centerDistance"] = centerDistance,
                ["edgeDistance"] = edgeDistance,
                ["sourcePosition"] = SpatialToolUtils.VectorJson(sourceCenter),
                ["referencePosition"] = SpatialToolUtils.VectorJson(refCenter),
                ["bounds"] = boundsInfo
            };
        }
    }

    public class GetFloorHeightTool : McpToolBase
    {
        public GetFloorHeightTool()
        {
            Name = "get_floor_height";
            Description = "Raycasts downward from a position to find the floor/ground height. Returns the hit point and surface normal.";
        }

        public override JObject Execute(JObject parameters)
        {
            JObject posObj = parameters["position"] as JObject;
            Vector3 origin = posObj != null
                ? new Vector3(posObj["x"]?.ToObject<float>() ?? 0f, posObj["y"]?.ToObject<float>() ?? 100f, posObj["z"]?.ToObject<float>() ?? 0f)
                : new Vector3(0, 100, 0);

            float maxDistance = parameters["maxDistance"]?.ToObject<float>() ?? 200f;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDistance))
            {
                McpLogger.LogInfo($"{Name}: floor found at {hit.point.y:F3} (normal {hit.normal})");
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Floor at height {hit.point.y:F3}m.",
                    ["hit"] = true,
                    ["point"] = SpatialToolUtils.VectorJson(hit.point),
                    ["normal"] = SpatialToolUtils.VectorJson(hit.normal),
                    ["collider"] = hit.collider?.name ?? "",
                    ["distance"] = hit.distance
                };
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = "No floor found within maxDistance.",
                ["hit"] = false,
                ["origin"] = SpatialToolUtils.VectorJson(origin)
            };
        }
    }

    public class GetNearbyObjectsTool : McpToolBase
    {
        public GetNearbyObjectsTool()
        {
            Name = "get_nearby_objects";
            Description = "Finds all GameObjects with renderers within a radius of a position or GameObject.";
        }

        public override JObject Execute(JObject parameters)
        {
            Vector3 center;
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            JObject posObj = parameters["position"] as JObject;

            if (instanceId.HasValue || !string.IsNullOrEmpty(objectPath))
            {
                var findResult = TransformToolUtils.FindGameObject(parameters);
                if (findResult.Error != null) return findResult.Error;
                center = findResult.GameObject.transform.position;
            }
            else if (posObj != null)
            {
                center = new Vector3(posObj["x"]?.ToObject<float>() ?? 0f, posObj["y"]?.ToObject<float>() ?? 0f, posObj["z"]?.ToObject<float>() ?? 0f);
            }
            else
            {
                return SpatialToolUtils.FindError("Either 'instanceId'/'objectPath' or 'position' must be provided");
            }

            float radius = parameters["radius"]?.ToObject<float>() ?? 5f;
            int maxResults = Mathf.Clamp(parameters["maxResults"]?.ToObject<int>() ?? 50, 1, 200);
            bool includeInactive = parameters["includeInactive"]?.ToObject<bool>() ?? false;

            Collider[] colliders = Physics.OverlapSphere(center, radius);
            var results = new JArray();
            var seen = new HashSet<int>();

            foreach (var col in colliders)
            {
                GameObject go = col.gameObject;
                int id = go.GetInstanceID();
                if (seen.Contains(id)) continue;
                seen.Add(id);
                float dist = Vector3.Distance(center, go.transform.position);
                results.Add(new JObject
                {
                    ["name"] = go.name,
                    ["instanceId"] = UnityObjectId.GetObjectId(go),
                    ["distance"] = dist,
                    ["position"] = SpatialToolUtils.VectorJson(go.transform.position)
                });
                if (results.Count >= maxResults) break;
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Found {results.Count} object(s) within {radius}m.",
                ["center"] = SpatialToolUtils.VectorJson(center),
                ["radius"] = radius,
                ["objects"] = results
            };
        }
    }

    public class FrameCameraOnTool : McpToolBase
    {
        public FrameCameraOnTool()
        {
            Name = "frame_camera_on";
            Description = "Moves the Scene view camera to frame a specific GameObject. Optionally sets a distance multiplier.";
        }

        public override JObject Execute(JObject parameters)
        {
            var findResult = TransformToolUtils.FindGameObject(parameters);
            if (findResult.Error != null) return findResult.Error;

            float distanceFactor = parameters["distanceFactor"]?.ToObject<float>() ?? 2.0f;
            float minDistance = parameters["minDistance"]?.ToObject<float>() ?? 2f;
            float maxDistance = parameters["maxDistance"]?.ToObject<float>() ?? 50f;

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return SpatialToolUtils.FindError("No active Scene View found", "not_found_error");

            GameObject go = findResult.GameObject;
            Bounds bounds;
            if (!SpatialToolUtils.TryGetBounds(go, false, out bounds))
                bounds = new Bounds(go.transform.position, Vector3.one);

            float radius = Mathf.Max(bounds.extents.magnitude, 1f);
            float distance = Mathf.Clamp(radius * distanceFactor, minDistance, maxDistance);

            Vector3 direction = Quaternion.Euler(30, 0, 0) * Vector3.forward;
            Vector3 cameraPos = bounds.center - direction * distance;

            sceneView.LookAtDirect(bounds.center, Quaternion.LookRotation(direction, Vector3.up), distance);
            sceneView.RepaintAll();

            McpLogger.LogInfo($"{Name}: framed camera on '{go.name}' at distance {distance:F2}");
            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Framed camera on '{go.name}'.",
                ["target"] = go.name,
                ["center"] = SpatialToolUtils.VectorJson(bounds.center),
                ["distance"] = distance
            };
        }
    }
}
