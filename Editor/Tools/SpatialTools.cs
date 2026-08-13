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

        /// <summary>
        /// Projects a GameObject's renderer geometry onto a world-space direction and returns the
        /// resulting [min, max] interval, using each renderer's own local-space bounds transformed
        /// by its own transform (an oriented bounding box) rather than the axis-aligned world
        /// Bounds. For a rotated object the world AABB is inflated relative to its true footprint,
        /// so AABB-based edge placement leaves a visibly larger gap than requested.
        ///
        /// Returns the full interval rather than a half-width on purpose: the interval's midpoint
        /// is NOT generally the AABB center's projection, so mixing an OBB half-width with an AABB
        /// center (as an earlier revision of place_next_to did) misplaces the object by exactly
        /// that difference - zero for symmetric geometry with a centered pivot, but wrong for
        /// asymmetric meshes or offset pivots. Callers must work in one convention end to end.
        ///
        /// Returns false when the object has no renderers - colliders don't expose a uniform
        /// local-bounds shape across collider types (Box vs. Sphere vs. Mesh), so the caller
        /// should fall back to <see cref="AabbInterval"/> in that case.
        /// </summary>
        public static bool TryGetOrientedInterval(GameObject gameObject, Vector3 worldDirection, bool includeInactive, out float min, out float max)
        {
            bool hasAny = false;
            min = float.PositiveInfinity;
            max = float.NegativeInfinity;

            foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(includeInactive))
            {
                if (!renderer.enabled) continue;

                Bounds localBounds = renderer.localBounds;
                Vector3 center = localBounds.center;
                Vector3 ext = localBounds.extents;
                Transform t = renderer.transform;

                for (int xi = -1; xi <= 1; xi += 2)
                {
                    for (int yi = -1; yi <= 1; yi += 2)
                    {
                        for (int zi = -1; zi <= 1; zi += 2)
                        {
                            Vector3 localCorner = center + Vector3.Scale(ext, new Vector3(xi, yi, zi));
                            Vector3 worldCorner = t.TransformPoint(localCorner);
                            float projection = Vector3.Dot(worldCorner, worldDirection);
                            if (projection < min) min = projection;
                            if (projection > max) max = projection;
                            hasAny = true;
                        }
                    }
                }
            }

            return hasAny;
        }

        /// <summary>
        /// Projection interval of a world-space AABB onto a direction (its exact support mapping).
        /// Fallback for objects with colliders but no renderers.
        /// </summary>
        public static void AabbInterval(Bounds bounds, Vector3 direction, out float min, out float max)
        {
            float center = Vector3.Dot(bounds.center, direction);
            float extent = Mathf.Abs(bounds.extents.x * direction.x)
                + Mathf.Abs(bounds.extents.y * direction.y)
                + Mathf.Abs(bounds.extents.z * direction.z);
            min = center - extent;
            max = center + extent;
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
            if (!referenceId.HasValue && string.IsNullOrEmpty(referencePath))
                return SpatialToolUtils.FindError("Either 'referenceId' or 'referencePath' must identify the reference GameObject", "validation_error");
            var referenceResult = GameObjectResolver.Find(referenceId, referencePath);
            if (referenceResult.Error != null) return referenceResult.Error;
            GameObject reference = referenceResult.GameObject;

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

            // Prefer the oriented (rotation-aware) projection interval over the world-AABB one: a
            // rotated object's world AABB is inflated relative to its true footprint, which
            // otherwise leaves a visibly larger gap than 'distance' requests. Falls back to the
            // AABB interval for collider-only objects (no renderers).
            //
            // Everything below stays in ONE convention (projection intervals along `direction`).
            // Deriving the along-axis offset from an OBB half-width but applying it to an AABB
            // center mixes conventions and misplaces the object whenever the interval midpoint
            // and the AABB center's projection disagree (asymmetric mesh / offset pivot).
            if (!SpatialToolUtils.TryGetOrientedInterval(reference, direction, false, out float refMin, out float refMax))
                SpatialToolUtils.AabbInterval(referenceBounds, direction, out refMin, out refMax);
            if (!SpatialToolUtils.TryGetOrientedInterval(sourceResult.GameObject, direction, false, out float srcMin, out float srcMax))
                SpatialToolUtils.AabbInterval(sourceBounds, direction, out srcMin, out srcMax);

            // Along `direction`: put the source's near face exactly `distance` past the
            // reference's far face. Perpendicular to it: keep the historical behavior of aligning
            // the two AABB centers, so "place B to the right of A" still lines B up with A.
            Vector3 centerDelta = referenceBounds.center - sourceBounds.center;
            Vector3 perpendicularDelta = centerDelta - direction * Vector3.Dot(centerDelta, direction);
            float alongDelta = (refMax + Mathf.Max(0f, distance)) - srcMin;

            Undo.RecordObject(sourceResult.GameObject.transform, "Place GameObject Next To");
            sourceResult.GameObject.transform.position += perpendicularDelta + direction * alongDelta;
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
    }

    public class FindLocalAssetsTool : McpToolBase
    {
        public FindLocalAssetsTool()
        {
            Name = "find_local_assets";
            Description = "Finds local assets in the Unity project by name, type, or extension. Results are ranked by relevance (exact match, then prefix match, then substring match), not by project scan order.";
        }

        private struct Candidate
        {
            public string Path;
            public string FileName;
            public int Rank;
        }

        public override JObject Execute(JObject parameters)
        {
            string query = parameters["query"]?.ToObject<string>()?.ToLowerInvariant() ?? "";
            string extension = parameters["extension"]?.ToObject<string>()?.ToLowerInvariant();
            int maxResults = Mathf.Clamp(parameters["maxResults"]?.ToObject<int>() ?? 100, 1, 500);
            string root = parameters["root"]?.ToObject<string>() ?? "Assets";
            if (!root.Equals("Assets", StringComparison.OrdinalIgnoreCase) && !root.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return SpatialToolUtils.FindError("root must be an Assets-relative project path");

            // Collect matches using only cheap string work, rank them, and resolve GUID/type for
            // the top maxResults ONLY. AssetDatabase.GetMainAssetTypeAtPath (and to a lesser
            // extent AssetPathToGUID) are expensive per-asset calls, and the default query is ""
            // which matches every asset in the project - resolving metadata for every match before
            // truncating made the common call orders of magnitude slower than it needs to be.
            //
            // Every path is scanned, deliberately: capping the scan would mean ranking only ever
            // sees a prefix of GetAllAssetPaths() order, so an exact-name match late in that order
            // would be silently dropped despite the tool promising relevance ordering. The scan
            // itself is only string comparisons, and GetAllAssetPaths() has already materialized
            // every path in memory by this point, so retaining a struct per match is the same
            // order of magnitude as the array we are iterating.
            var candidates = new List<Candidate>();

            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || path.EndsWith("/")) continue;
                string fileNameLower = Path.GetFileName(path).ToLowerInvariant();
                if (!fileNameLower.Contains(query)) continue;
                if (!string.IsNullOrEmpty(extension) && !path.EndsWith(extension.StartsWith(".") ? extension : "." + extension, StringComparison.OrdinalIgnoreCase)) continue;

                candidates.Add(new Candidate
                {
                    Path = path,
                    FileName = fileNameLower,
                    Rank = RelevanceRank(fileNameLower, query)
                });
            }

            candidates.Sort((a, b) =>
            {
                int rankCompare = a.Rank.CompareTo(b.Rank);
                return rankCompare != 0 ? rankCompare : string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            });

            var assets = new JArray();
            int resolveCount = Mathf.Min(maxResults, candidates.Count);
            for (int i = 0; i < resolveCount; i++)
            {
                string path = candidates[i].Path;
                assets.Add(new JObject
                {
                    ["path"] = path,
                    ["guid"] = AssetDatabase.AssetPathToGUID(path),
                    ["type"] = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? "Unknown"
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Found {assets.Count} local asset(s)" + (candidates.Count > assets.Count ? $" ({candidates.Count} total, truncated to maxResults)." : "."),
                ["totalFound"] = candidates.Count,
                ["assets"] = assets
            };
        }

        private static int RelevanceRank(string fileNameLower, string queryLower)
        {
            if (string.IsNullOrEmpty(queryLower)) return 2;
            if (fileNameLower == queryLower) return 0;
            if (Path.GetFileNameWithoutExtension(fileNameLower) == queryLower) return 0;
            if (fileNameLower.StartsWith(queryLower, StringComparison.Ordinal)) return 1;
            return 2;
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
            if (!refId.HasValue && string.IsNullOrEmpty(refPath))
                return SpatialToolUtils.FindError("Either 'referenceId' or 'referencePath' must identify the reference GameObject");
            var referenceResult = GameObjectResolver.Find(refId, refPath);
            if (referenceResult.Error != null) return referenceResult.Error;
            GameObject reference = referenceResult.GameObject;

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
            Description = "Raycasts downward from a position to find the floor/ground height. Returns the hit point and surface normal. Supports filtering by layer mask and excluding a specific GameObject's own colliders (e.g. the object you're about to drop).";
        }

        public override JObject Execute(JObject parameters)
        {
            JObject posObj = parameters["position"] as JObject;
            Vector3 origin = posObj != null
                ? new Vector3(posObj["x"]?.ToObject<float>() ?? 0f, posObj["y"]?.ToObject<float>() ?? 100f, posObj["z"]?.ToObject<float>() ?? 0f)
                : new Vector3(0, 100, 0);

            float maxDistance = parameters["maxDistance"]?.ToObject<float>() ?? 200f;
            int layerMask = parameters["layerMask"]?.ToObject<int?>() ?? Physics.DefaultRaycastLayers;

            int? ignoreInstanceId = parameters["ignoreInstanceId"]?.ToObject<int?>();
            string ignoreObjectPath = parameters["ignoreObjectPath"]?.ToObject<string>();
            GameObject ignoreObject = null;
            if (ignoreInstanceId.HasValue || !string.IsNullOrEmpty(ignoreObjectPath))
            {
                var ignoreResult = GameObjectResolver.Find(ignoreInstanceId, ignoreObjectPath);
                if (ignoreResult.Error != null) return ignoreResult.Error;
                ignoreObject = ignoreResult.GameObject;
            }

            // Editor transform edits (move_gameobject, set_transform, etc.) don't automatically
            // push updated colliders into the physics world until the next physics step - which
            // in Edit mode may never come. Without this, a raycast run immediately after moving
            // an object can hit its stale pre-move position.
            Physics.SyncTransforms();

            // RaycastAll (not the single-hit Raycast) so an ignored object's collider can be
            // skipped in favor of the next hit along the ray, rather than only being able to
            // accept or reject whatever the single nearest hit happens to be.
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, layerMask);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                if (ignoreObject != null &&
                    (hit.collider.transform == ignoreObject.transform || hit.collider.transform.IsChildOf(ignoreObject.transform)))
                {
                    continue;
                }

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
            Description = "Finds all GameObjects with colliders within a radius of a position or GameObject, sorted nearest-first.";
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

            Physics.SyncTransforms();

            var seen = new HashSet<int>();
            var candidates = new List<(GameObject go, float dist)>();

            if (includeInactive)
            {
                // Physics.OverlapSphere only ever considers active colliders in the physics
                // world, so honoring includeInactive requires a manual distance scan instead.
                Collider[] allColliders = UnityEngine.Object.FindObjectsByType<Collider>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var col in allColliders)
                {
                    if (!col.enabled) continue;
                    GameObject go = col.gameObject;
                    int id = go.GetInstanceID();
                    if (seen.Contains(id)) continue;
                    float dist = Vector3.Distance(center, go.transform.position);
                    if (dist > radius) continue;
                    seen.Add(id);
                    candidates.Add((go, dist));
                }
            }
            else
            {
                Collider[] colliders = Physics.OverlapSphere(center, radius);
                foreach (var col in colliders)
                {
                    GameObject go = col.gameObject;
                    int id = go.GetInstanceID();
                    if (seen.Contains(id)) continue;
                    seen.Add(id);
                    candidates.Add((go, Vector3.Distance(center, go.transform.position)));
                }
            }

            candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

            var results = new JArray();
            foreach (var (go, dist) in candidates)
            {
                if (results.Count >= maxResults) break;
                results.Add(new JObject
                {
                    ["name"] = go.name,
                    ["instanceId"] = UnityObjectId.GetObjectId(go),
                    ["distance"] = dist,
                    ["position"] = SpatialToolUtils.VectorJson(go.transform.position)
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Found {results.Count} object(s) within {radius}m" +
                    (candidates.Count > results.Count ? $" ({candidates.Count} total, truncated to maxResults)." : "."),
                ["center"] = SpatialToolUtils.VectorJson(center),
                ["radius"] = radius,
                ["totalFound"] = candidates.Count,
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
            // RepaintAll is static - repaints every SceneView, which is what we want here.
            SceneView.RepaintAll();

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
