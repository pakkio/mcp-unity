using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using McpUnity.Unity;

namespace McpUnity.Utils
{
    /// <summary>
    /// Single shared GameObject resolver used by every tool that accepts an instanceId/objectPath
    /// (or a combined idOrName) identifier.
    ///
    /// Before this existed, four independent implementations had quietly diverged:
    /// TransformToolUtils.FindGameObject used only GameObject.Find (misses inactive objects
    /// entirely); GameObjectToolUtils.FindGameObject and UpdateComponentTool's private
    /// FindGameObjectByPath fell back to a root-object traversal that reaches inactive
    /// descendants but only in the active scene; MaterialTools had its own near-identical copy;
    /// GetGameObjectTool/GetGameObjectResource used GameObject.Find with no fallback at all.
    /// Which tool you called determined whether a given inactive object or an object in an
    /// additively-loaded (non-active) scene could be found - a source of confusing, tool-specific
    /// "not found" errors that had nothing to do with the object actually being missing.
    /// </summary>
    public static class GameObjectResolver
    {
        public struct Result
        {
            public GameObject GameObject;
            public JObject Error;
        }

        /// <summary>
        /// Resolve a GameObject by instance ID (checked first, if provided) or hierarchy
        /// path/name. Searches every loaded scene and includes inactive objects. A bare name
        /// (no '/') that matches more than one object returns an ambiguity error listing every
        /// candidate's full path and instance ID instead of silently picking one, unlike the
        /// native GameObject.Find (active objects only, first match undefined when ambiguous).
        /// </summary>
        public static Result Find(int? instanceId, string objectPath)
        {
            if (instanceId.HasValue)
            {
                GameObject byId = UnityObjectId.ObjectFromId(instanceId.Value) as GameObject;
                if (byId == null)
                {
                    return Fail($"GameObject not found with instance ID {instanceId.Value}.");
                }
                return new Result { GameObject = byId };
            }

            if (string.IsNullOrEmpty(objectPath))
            {
                return Fail("Either 'instanceId' or 'objectPath' must be provided.", "validation_error");
            }

            string trimmedPath = objectPath.Trim().TrimStart('/');
            if (trimmedPath.Length == 0)
            {
                return Fail("'objectPath' cannot be empty.", "validation_error");
            }

            string[] segments = trimmedPath.Split('/');
            var matches = new List<GameObject>();

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (segments.Length == 1)
                    {
                        // Bare name: match the root itself or any descendant (a caller may pass
                        // just a leaf name rather than a fully-qualified path).
                        CollectByName(root.transform, segments[0], matches);
                    }
                    else if (root.name == segments[0])
                    {
                        GameObject match = TraversePath(root.transform, segments, 1);
                        if (match != null) matches.Add(match);
                    }
                }
            }

            if (matches.Count == 0)
            {
                return Fail($"GameObject not found with path '{objectPath}'.");
            }

            List<GameObject> distinct = matches.Distinct().ToList();
            if (distinct.Count > 1)
            {
                string candidates = string.Join(", ", distinct.Select(go =>
                    $"'{GetPath(go)}' (instanceId {UnityObjectId.GetObjectId(go)})"));
                return Fail(
                    $"'{objectPath}' is ambiguous - {distinct.Count} GameObjects match: {candidates}. " +
                    "Use 'instanceId' or a fully-qualified path to disambiguate.",
                    "ambiguous_reference_error");
            }

            return new Result { GameObject = distinct[0] };
        }

        /// <summary>
        /// Convenience overload for tools that accept a single combined identifier which is
        /// either a numeric instance ID or a name/path (e.g. GetGameObjectTool's 'idOrName').
        /// </summary>
        public static Result FindByIdOrName(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName))
            {
                return Fail("Identifier cannot be null or empty.", "validation_error");
            }

            return int.TryParse(idOrName, out int instanceId)
                ? Find(instanceId, null)
                : Find(null, idOrName);
        }

        private static void CollectByName(Transform current, string name, List<GameObject> matches)
        {
            if (current.name == name)
            {
                matches.Add(current.gameObject);
            }

            for (int i = 0; i < current.childCount; i++)
            {
                CollectByName(current.GetChild(i), name, matches);
            }
        }

        private static GameObject TraversePath(Transform current, string[] segments, int index)
        {
            if (index >= segments.Length) return current.gameObject;

            Transform child = current.Find(segments[index]);
            return child == null ? null : TraversePath(child, segments, index + 1);
        }

        /// <summary>
        /// Full hierarchy path of a GameObject, root-first, slash-separated (no leading slash).
        /// </summary>
        public static string GetPath(GameObject obj)
        {
            if (obj == null) return null;
            string path = obj.name;
            Transform current = obj.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        private static Result Fail(string message, string errorType = "not_found_error")
        {
            return new Result { Error = McpUnitySocketHandler.CreateErrorResponse(message, errorType) };
        }
    }
}
