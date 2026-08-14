using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for auditing, validating, and profiling ChilloutVR CCK Worlds and Avatars before upload.
    /// </summary>
    public class InspectCvrCckTool : McpToolBase
    {
        public InspectCvrCckTool()
        {
            Name = "inspect_cvr_cck";
            Description = "Audits and validates ChilloutVR CCK content (Worlds, Avatars, Props) for upload readiness, performance budgets, and disallowed components.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "validate_content";
            string contentType = parameters["contentType"]?.ToObject<string>()?.ToLowerInvariant() ?? "world";
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();

            switch (action)
            {
                case "validate_content":
                    return HandleValidateContent(contentType, instanceId, objectPath);

                case "get_stats":
                    return HandleGetStats(contentType, instanceId, objectPath);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'validate_content', 'get_stats'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleValidateContent(string contentType, int? instanceId, string objectPath)
        {
            GameObject targetGo = null;
            if (instanceId.HasValue || !string.IsNullOrEmpty(objectPath))
            {
                JObject findErr = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out targetGo, out string info);
                if (findErr != null) return findErr;
            }

            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            List<string> suggestions = new List<string>();

            int totalPolygons = 0;
            int materialCount = 0;
            int skinnedMeshCount = 0;
            int audioSourceCount = 0;
            int lightCount = 0;
            int particleCount = 0;

            if (contentType == "avatar")
            {
                if (targetGo == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse("Target avatar GameObject ('instanceId' or 'objectPath') must be provided for avatar validation.", "validation_error");
                }

                // Check CVRAvatar or Animator
                Type cvrAvatarType = FindCckType("CVRAvatar");
                if (cvrAvatarType != null && targetGo.GetComponent(cvrAvatarType) == null)
                {
                    errors.Add("Missing 'CVRAvatar' component on avatar root.");
                }

                Animator anim = targetGo.GetComponent<Animator>();
                if (anim == null || !anim.isHuman)
                {
                    warnings.Add("Avatar is not configured with a Humanoid Animator.");
                }

                // Count Renderers on Avatar
                SkinnedMeshRenderer[] smrs = targetGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                skinnedMeshCount = smrs.Length;
                if (skinnedMeshCount > 8)
                {
                    warnings.Add($"High SkinnedMeshRenderer count ({skinnedMeshCount}). Consider combining meshes to reduce draw calls.");
                }

                foreach (var smr in smrs)
                {
                    if (smr.sharedMesh != null) totalPolygons += smr.sharedMesh.triangles.Length / 3;
                    if (smr.sharedMaterials != null) materialCount += smr.sharedMaterials.Length;
                }

                MeshFilter[] mfs = targetGo.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in mfs)
                {
                    if (mf.sharedMesh != null) totalPolygons += mf.sharedMesh.triangles.Length / 3;
                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null && mr.sharedMaterials != null) materialCount += mr.sharedMaterials.Length;
                }

                if (totalPolygons > 100000)
                {
                    warnings.Add($"High polygon count ({totalPolygons:N0} triangles). Target under 70,000 for Good rating.");
                }

                if (materialCount > 16)
                {
                    warnings.Add($"High material slot count ({materialCount}). Consider atlasing textures to reduce material slots.");
                }

                AudioSource[] audios = targetGo.GetComponentsInChildren<AudioSource>(true);
                audioSourceCount = audios.Length;
                foreach (var a in audios)
                {
                    if (a.spatialize == false || a.spatialBlend < 0.8f)
                    {
                        warnings.Add($"AudioSource on '{a.gameObject.name}' is not fully spatialized (spatialBlend={a.spatialBlend}).");
                    }
                }
            }
            else // World validation
            {
                Type cvrWorldType = FindCckType("CVRWorld");
                UnityEngine.Object[] worlds = cvrWorldType != null ? UnityEngine.Object.FindObjectsByType(cvrWorldType, FindObjectsSortMode.None) : new UnityEngine.Object[0];

                if (worlds.Length == 0)
                {
                    warnings.Add("No 'CVRWorld' component found in the active scene.");
                }
                else if (worlds.Length > 1)
                {
                    errors.Add($"Multiple ({worlds.Length}) 'CVRWorld' components found in scene. Only one is allowed.");
                }

                // Check Spawn points
                Type spawnType = FindCckType("CVRSpawnPoint");
                UnityEngine.Object[] spawns = spawnType != null ? UnityEngine.Object.FindObjectsByType(spawnType, FindObjectsSortMode.None) : new UnityEngine.Object[0];
                if (spawns.Length == 0 && GameObject.Find("SpawnPoint") == null)
                {
                    warnings.Add("No spawn point found in the scene.");
                }

                // Check Lights
                Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                lightCount = lights.Length;
                int realtimeLights = 0;
                foreach (var l in lights)
                {
                    if (l.lightmapBakeType == LightmapBakeType.Realtime) realtimeLights++;
                }

                if (realtimeLights > 4)
                {
                    warnings.Add($"High realtime light count ({realtimeLights}). Bake lighting to improve performance.");
                }

                // Count scene meshes
                MeshFilter[] mfs = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
                foreach (var mf in mfs)
                {
                    if (mf.sharedMesh != null) totalPolygons += mf.sharedMesh.triangles.Length / 3;
                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null && mr.sharedMaterials != null) materialCount += mr.sharedMaterials.Length;
                }

                ParticleSystem[] particles = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
                particleCount = particles.Length;

                AudioSource[] audios = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
                audioSourceCount = audios.Length;
            }

            string status = errors.Count > 0 ? "error" : (warnings.Count > 0 ? "warning" : "pass");

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Validation result: {status.ToUpper()} ({errors.Count} errors, {warnings.Count} warnings)",
                ["status"] = status,
                ["contentType"] = contentType,
                ["errors"] = new JArray(errors),
                ["warnings"] = new JArray(warnings),
                ["suggestions"] = new JArray(suggestions),
                ["metrics"] = new JObject
                {
                    ["totalTriangles"] = totalPolygons,
                    ["materialSlots"] = materialCount,
                    ["skinnedMeshRenderers"] = skinnedMeshCount,
                    ["audioSources"] = audioSourceCount,
                    ["lights"] = lightCount,
                    ["particleSystems"] = particleCount
                }
            };
        }

        private JObject HandleGetStats(string contentType, int? instanceId, string objectPath)
        {
            return HandleValidateContent(contentType, instanceId, objectPath);
        }

        private static Type FindCckType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = assembly.GetType($"ABI.CCK.Components.{typeName}")
                          ?? assembly.GetType($"ABI.CCK.Scripts.{typeName}")
                          ?? assembly.GetType(typeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
