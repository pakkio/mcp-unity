using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for configuring MeshColliders, generating colliders across hierarchies, and creating PhysicMaterials.
    /// </summary>
    public class ConfigureCollidersTool : McpToolBase
    {
        public ConfigureCollidersTool()
        {
            Name = "configure_colliders";
            Description = "Configures MeshColliders (convex hull, triggers, cooking options), generates colliders across object hierarchies, or creates PhysicMaterial assets.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "configure_mesh_collider";
            string reason = parameters["reason"]?.ToObject<string>();

            switch (action)
            {
                case "configure_mesh_collider":
                    return HandleConfigureMeshCollider(parameters, reason);

                case "generate_hierarchy_colliders":
                    return HandleGenerateHierarchyColliders(parameters, reason);

                case "create_physics_material":
                    return HandleCreatePhysicsMaterial(parameters, reason);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'configure_mesh_collider', 'generate_hierarchy_colliders', 'create_physics_material'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleConfigureMeshCollider(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            bool? convex = parameters["convex"]?.ToObject<bool?>();
            bool? isTrigger = parameters["isTrigger"]?.ToObject<bool?>();
            string customMeshPath = parameters["meshPath"]?.ToObject<string>();
            string physicsMaterialPath = parameters["materialPath"]?.ToObject<string>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            MeshCollider meshCollider = targetObject.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = Undo.AddComponent<MeshCollider>(targetObject);
            }
            else
            {
                Undo.RecordObject(meshCollider, "Configure MeshCollider");
            }

            // Assign shared mesh if missing or custom mesh provided
            if (!string.IsNullOrEmpty(customMeshPath))
            {
                Mesh customMesh = LoadMesh(customMeshPath);
                if (customMesh != null)
                {
                    meshCollider.sharedMesh = customMesh;
                }
                else
                {
                    return McpUnitySocketHandler.CreateErrorResponse($"Could not load collision mesh at '{customMeshPath}'", "mesh_not_found");
                }
            }
            else if (meshCollider.sharedMesh == null)
            {
                MeshFilter mf = targetObject.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    meshCollider.sharedMesh = mf.sharedMesh;
                }
            }

            if (convex.HasValue)
            {
                meshCollider.convex = convex.Value;
            }

            if (isTrigger.HasValue)
            {
                // In Unity, MeshCollider must be convex to be a trigger
                if (isTrigger.Value && !meshCollider.convex)
                {
                    meshCollider.convex = true;
                }
                meshCollider.isTrigger = isTrigger.Value;
            }

            if (!string.IsNullOrEmpty(physicsMaterialPath))
            {
                PhysicMaterial mat = LoadPhysicMaterial(physicsMaterialPath);
                if (mat != null)
                {
                    meshCollider.sharedMaterial = mat;
                }
            }

            EditorUtility.SetDirty(targetObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured MeshCollider on '{targetObject.name}' (convex={meshCollider.convex}, isTrigger={meshCollider.isTrigger})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured MeshCollider on '{targetObject.name}'",
                ["instanceId"] = UnityObjectId.GetObjectId(targetObject),
                ["gameObjectName"] = targetObject.name,
                ["convex"] = meshCollider.convex,
                ["isTrigger"] = meshCollider.isTrigger,
                ["sharedMesh"] = meshCollider.sharedMesh != null ? meshCollider.sharedMesh.name : "none",
                ["material"] = meshCollider.sharedMaterial != null ? meshCollider.sharedMaterial.name : "default"
            };
        }

        private JObject HandleGenerateHierarchyColliders(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string colliderType = parameters["colliderType"]?.ToObject<string>()?.ToLowerInvariant() ?? "box";
            bool includeChildren = parameters["includeChildren"]?.ToObject<bool?>() ?? true;
            bool replaceExisting = parameters["replaceExisting"]?.ToObject<bool?>() ?? false;

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            List<GameObject> targets = new List<GameObject>();
            if (includeChildren)
            {
                MeshFilter[] mfs = targetObject.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in mfs)
                {
                    if (mf.sharedMesh != null && !targets.Contains(mf.gameObject))
                    {
                        targets.Add(mf.gameObject);
                    }
                }
                if (targets.Count == 0) targets.Add(targetObject);
            }
            else
            {
                targets.Add(targetObject);
            }

            int collidersAdded = 0;

            foreach (var go in targets)
            {
                if (replaceExisting)
                {
                    foreach (var col in go.GetComponents<Collider>())
                    {
                        Undo.DestroyObjectImmediate(col);
                    }
                }

                if (go.GetComponent<Collider>() != null && !replaceExisting)
                {
                    continue;
                }

                Undo.RecordObject(go, "Add Collider");

                switch (colliderType)
                {
                    case "box":
                        BoxCollider box = Undo.AddComponent<BoxCollider>(go);
                        break;

                    case "capsule":
                        CapsuleCollider cap = Undo.AddComponent<CapsuleCollider>(go);
                        break;

                    case "sphere":
                        SphereCollider sph = Undo.AddComponent<SphereCollider>(go);
                        break;

                    case "convex_mesh":
                        MeshCollider mcConvex = Undo.AddComponent<MeshCollider>(go);
                        MeshFilter mfConvex = go.GetComponent<MeshFilter>();
                        if (mfConvex != null) mcConvex.sharedMesh = mfConvex.sharedMesh;
                        mcConvex.convex = true;
                        break;

                    case "mesh":
                    default:
                        MeshCollider mc = Undo.AddComponent<MeshCollider>(go);
                        MeshFilter mf = go.GetComponent<MeshFilter>();
                        if (mf != null) mc.sharedMesh = mf.sharedMesh;
                        break;
                }

                EditorUtility.SetDirty(go);
                collidersAdded++;
            }

            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            McpLogger.LogInfo($"[MCP Unity] Generated {collidersAdded} {colliderType} colliders on '{targetObject.name}' hierarchy" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully added {collidersAdded} {colliderType} colliders across '{targetObject.name}' hierarchy",
                ["rootName"] = targetObject.name,
                ["collidersAdded"] = collidersAdded,
                ["colliderType"] = colliderType
            };
        }

        private JObject HandleCreatePhysicsMaterial(JObject parameters, string reason)
        {
            string assetPath = parameters["assetPath"]?.ToObject<string>();
            float dynamicFriction = Mathf.Max(0f, parameters["dynamicFriction"]?.ToObject<float?>() ?? 0.6f);
            float staticFriction = Mathf.Max(0f, parameters["staticFriction"]?.ToObject<float?>() ?? 0.6f);
            float bounciness = Mathf.Clamp01(parameters["bounciness"]?.ToObject<float?>() ?? 0f);
            string frictionCombineStr = parameters["frictionCombine"]?.ToObject<string>()?.ToLowerInvariant() ?? "average";
            string bounceCombineStr = parameters["bounceCombine"]?.ToObject<string>()?.ToLowerInvariant() ?? "average";

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                assetPath = "Assets/Physics/NewPhysicsMaterial.physicMaterial";
            }

            assetPath = assetPath.Replace('\\', '/').Trim();
            if (!assetPath.StartsWith("Assets/")) assetPath = "Assets/" + assetPath.TrimStart('/');
            if (!assetPath.EndsWith(".physicMaterial", StringComparison.OrdinalIgnoreCase) && !assetPath.EndsWith(".physicsMaterial", StringComparison.OrdinalIgnoreCase))
            {
                assetPath += ".physicMaterial";
            }

            string dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            PhysicMaterial mat = new PhysicMaterial
            {
                dynamicFriction = dynamicFriction,
                staticFriction = staticFriction,
                bounciness = bounciness,
                frictionCombine = ParseCombineMode(frictionCombineStr),
                bounceCombine = ParseCombineMode(bounceCombineStr)
            };

            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(mat, assetPath);
            AssetDatabase.SaveAssets();

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            McpLogger.LogInfo($"[MCP Unity] Created PhysicMaterial at '{assetPath}' (friction={dynamicFriction}, bounciness={bounciness})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created PhysicMaterial at '{assetPath}'",
                ["assetPath"] = assetPath,
                ["guid"] = guid,
                ["dynamicFriction"] = dynamicFriction,
                ["staticFriction"] = staticFriction,
                ["bounciness"] = bounciness
            };
        }

        private Mesh LoadMesh(string pathOrName)
        {
            if (pathOrName.StartsWith("Assets/") || pathOrName.StartsWith("Packages/"))
            {
                return AssetDatabase.LoadAssetAtPath<Mesh>(pathOrName);
            }

            string[] guids = AssetDatabase.FindAssets($"{pathOrName} t:Mesh");
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            return null;
        }

        private PhysicMaterial LoadPhysicMaterial(string pathOrName)
        {
            if (pathOrName.StartsWith("Assets/") || pathOrName.StartsWith("Packages/"))
            {
                return AssetDatabase.LoadAssetAtPath<PhysicMaterial>(pathOrName);
            }

            string[] guids = AssetDatabase.FindAssets($"{pathOrName} t:PhysicMaterial");
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<PhysicMaterial>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            return null;
        }

        private PhysicMaterialCombine ParseCombineMode(string str)
        {
            switch (str)
            {
                case "minimum":
                case "min": return PhysicMaterialCombine.Minimum;
                case "maximum":
                case "max": return PhysicMaterialCombine.Maximum;
                case "multiply": return PhysicMaterialCombine.Multiply;
                case "average":
                default: return PhysicMaterialCombine.Average;
            }
        }
    }
}
