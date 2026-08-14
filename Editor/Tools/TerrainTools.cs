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
    /// Tool for creating, sculpting, and managing Unity Terrains (heightmaps, texture layers, trees).
    /// </summary>
    public class ManageTerrainTool : McpToolBase
    {
        public ManageTerrainTool()
        {
            Name = "manage_terrain";
            Description = "Creates, sculpts, paints, and manages Unity Terrains (procedural Perlin noise, height editing, terrain layers, tree scattering).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "get_info";
            string reason = parameters["reason"]?.ToObject<string>();

            switch (action)
            {
                case "create":
                    return HandleCreate(parameters, reason);
                case "sculpt":
                    return HandleSculpt(parameters, reason);
                case "add_layer":
                    return HandleAddLayer(parameters, reason);
                case "add_trees":
                    return HandleAddTrees(parameters, reason);
                case "get_info":
                    return HandleGetInfo(parameters);
                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'create', 'sculpt', 'add_layer', 'add_trees', 'get_info'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleCreate(JObject parameters, string reason)
        {
            string terrainName = parameters["terrainName"]?.ToObject<string>() ?? "Terrain";
            string assetPath = parameters["assetPath"]?.ToObject<string>();
            JObject sizeObj = parameters["size"] as JObject;
            int heightmapRes = parameters["heightmapResolution"]?.ToObject<int?>() ?? 513;
            int alphamapRes = parameters["alphamapResolution"]?.ToObject<int?>() ?? 512;
            JObject posObj = parameters["position"] as JObject;

            // Ensure heightmap resolution is power of two + 1
            if (!IsPowerOfTwo(heightmapRes - 1))
            {
                heightmapRes = Mathf.ClosestPowerOfTwo(heightmapRes) + 1;
            }

            Vector3 size = new Vector3(
                sizeObj?["x"]?.ToObject<float>() ?? 500f,
                sizeObj?["y"]?.ToObject<float>() ?? 100f,
                sizeObj?["z"]?.ToObject<float>() ?? 500f
            );

            Vector3 position = new Vector3(
                posObj?["x"]?.ToObject<float>() ?? 0f,
                posObj?["y"]?.ToObject<float>() ?? 0f,
                posObj?["z"]?.ToObject<float>() ?? 0f
            );

            // Determine asset path for TerrainData
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                string folder = "Assets/Terrains";
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    AssetDatabase.Refresh();
                }
                assetPath = $"{folder}/{terrainName}_Data.asset";
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            }
            else
            {
                assetPath = assetPath.Replace('\\', '/').Trim();
                if (!assetPath.StartsWith("Assets/")) assetPath = "Assets/" + assetPath.TrimStart('/');
                if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) assetPath += ".asset";
                string dir = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    AssetDatabase.Refresh();
                }
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            }

            try
            {
                // Create TerrainData
                TerrainData terrainData = new TerrainData
                {
                    heightmapResolution = heightmapRes,
                    alphamapResolution = alphamapRes,
                    size = size
                };

                AssetDatabase.CreateAsset(terrainData, assetPath);
                AssetDatabase.SaveAssets();

                // Create GameObject
                GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
                terrainObject.name = terrainName;
                terrainObject.transform.position = position;

                Undo.RegisterCreatedObjectUndo(terrainObject, $"Create Terrain {terrainName}");
                EditorUtility.SetDirty(terrainObject);

                McpLogger.LogInfo($"[MCP Unity] Created Terrain '{terrainName}' at {position} with data '{assetPath}'" + (reason != null ? $" — {reason}" : ""));

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully created Terrain '{terrainName}' (Size: {size.x}x{size.y}x{size.z})",
                    ["instanceId"] = UnityObjectId.GetObjectId(terrainObject),
                    ["terrainPath"] = GameObjectToolUtils.GetGameObjectPath(terrainObject),
                    ["assetPath"] = assetPath,
                    ["size"] = new JObject { ["x"] = size.x, ["y"] = size.y, ["z"] = size.z }
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Failed to create terrain: {ex.Message}", "terrain_creation_error");
            }
        }

        private JObject HandleSculpt(JObject parameters, string reason)
        {
            if (!TryGetTerrain(parameters, out Terrain terrain, out TerrainData terrainData, out JObject error))
            {
                return error;
            }

            string operation = parameters["operation"]?.ToObject<string>()?.ToLowerInvariant() ?? "perlin_noise";
            int res = terrainData.heightmapResolution;

            Undo.RegisterCompleteObjectUndo(terrainData, $"Sculpt Terrain {terrain.name} ({operation})");

            switch (operation)
            {
                case "flatten":
                    {
                        float targetHeight = Mathf.Clamp01(parameters["height"]?.ToObject<float>() ?? 0f);
                        float[,] heights = new float[res, res];
                        for (int y = 0; y < res; y++)
                        {
                            for (int x = 0; x < res; x++)
                            {
                                heights[y, x] = targetHeight;
                            }
                        }
                        terrainData.SetHeights(0, 0, heights);
                        break;
                    }

                case "raise_lower":
                    {
                        float delta = parameters["delta"]?.ToObject<float>() ?? 0.1f;
                        JObject region = parameters["region"] as JObject;
                        float normStartX = Mathf.Clamp01(region?["startX"]?.ToObject<float>() ?? 0f);
                        float normStartZ = Mathf.Clamp01(region?["startZ"]?.ToObject<float>() ?? 0f);
                        float normWidth = Mathf.Clamp01(region?["width"]?.ToObject<float>() ?? 1f);
                        float normHeight = Mathf.Clamp01(region?["height"]?.ToObject<float>() ?? 1f);

                        int startX = Mathf.FloorToInt(normStartX * (res - 1));
                        int startZ = Mathf.FloorToInt(normStartZ * (res - 1));
                        int width = Mathf.Clamp(Mathf.FloorToInt(normWidth * res), 1, res - startX);
                        int height = Mathf.Clamp(Mathf.FloorToInt(normHeight * res), 1, res - startZ);

                        float[,] currentHeights = terrainData.GetHeights(startX, startZ, width, height);
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                currentHeights[y, x] = Mathf.Clamp01(currentHeights[y, x] + delta);
                            }
                        }
                        terrainData.SetHeights(startX, startZ, currentHeights);
                        break;
                    }

                case "perlin_noise":
                    {
                        float scale = parameters["scale"]?.ToObject<float>() ?? 20f;
                        float heightScale = Mathf.Clamp01(parameters["heightScale"]?.ToObject<float>() ?? 0.35f);
                        int octaves = Mathf.Clamp(parameters["octaves"]?.ToObject<int>() ?? 3, 1, 8);
                        float persistence = parameters["persistence"]?.ToObject<float>() ?? 0.5f;
                        float lacunarity = parameters["lacunarity"]?.ToObject<float>() ?? 2f;
                        float offsetX = parameters["offsetX"]?.ToObject<float>() ?? 0f;
                        float offsetZ = parameters["offsetZ"]?.ToObject<float>() ?? 0f;

                        float[,] heights = new float[res, res];
                        for (int y = 0; y < res; y++)
                        {
                            for (int x = 0; x < res; x++)
                            {
                                float amplitude = 1f;
                                float frequency = 1f;
                                float noiseHeight = 0f;
                                float maxAmp = 0f;

                                for (int i = 0; i < octaves; i++)
                                {
                                    float sampleX = ((float)x / res * scale + offsetX) * frequency;
                                    float sampleZ = ((float)y / res * scale + offsetZ) * frequency;
                                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ);
                                    noiseHeight += perlinValue * amplitude;
                                    maxAmp += amplitude;

                                    amplitude *= persistence;
                                    frequency *= lacunarity;
                                }

                                heights[y, x] = Mathf.Clamp01((noiseHeight / maxAmp) * heightScale);
                            }
                        }
                        terrainData.SetHeights(0, 0, heights);
                        break;
                    }

                case "smooth":
                    {
                        int iterations = Mathf.Clamp(parameters["iterations"]?.ToObject<int>() ?? 1, 1, 10);
                        float[,] heights = terrainData.GetHeights(0, 0, res, res);
                        for (int iter = 0; iter < iterations; iter++)
                        {
                            float[,] smoothed = new float[res, res];
                            for (int y = 0; y < res; y++)
                            {
                                for (int x = 0; x < res; x++)
                                {
                                    float sum = 0f;
                                    int count = 0;
                                    for (int dy = -1; dy <= 1; dy++)
                                    {
                                        for (int dx = -1; dx <= 1; dx++)
                                        {
                                            int ny = y + dy;
                                            int nx = x + dx;
                                            if (nx >= 0 && nx < res && ny >= 0 && ny < res)
                                            {
                                                sum += heights[ny, nx];
                                                count++;
                                            }
                                        }
                                    }
                                    smoothed[y, x] = sum / count;
                                }
                            }
                            heights = smoothed;
                        }
                        terrainData.SetHeights(0, 0, heights);
                        break;
                    }

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown sculpting operation '{operation}'. Supported: 'perlin_noise', 'flatten', 'raise_lower', 'smooth'",
                        "invalid_operation"
                    );
            }

            EditorUtility.SetDirty(terrainData);
            terrain.Flush();

            McpLogger.LogInfo($"[MCP Unity] Sculpted Terrain '{terrain.name}' with operation '{operation}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully sculpted terrain '{terrain.name}' using '{operation}'",
                ["terrainName"] = terrain.name,
                ["operation"] = operation
            };
        }

        private JObject HandleAddLayer(JObject parameters, string reason)
        {
            if (!TryGetTerrain(parameters, out Terrain terrain, out TerrainData terrainData, out JObject error))
            {
                return error;
            }

            string texturePath = parameters["diffuseTexture"]?.ToObject<string>();
            string normalPath = parameters["normalMap"]?.ToObject<string>();
            JObject tileSizeObj = parameters["tileSize"] as JObject;
            string layerAssetPath = parameters["layerAssetPath"]?.ToObject<string>();

            if (string.IsNullOrWhiteSpace(texturePath))
            {
                return McpUnitySocketHandler.CreateErrorResponse("Parameter 'diffuseTexture' (path or name) is required to create a terrain layer.", "validation_error");
            }

            Texture2D diffuseTex = LoadTexture(texturePath);
            if (diffuseTex == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Could not load diffuse texture at '{texturePath}'", "texture_not_found");
            }

            Texture2D normalTex = !string.IsNullOrWhiteSpace(normalPath) ? LoadTexture(normalPath) : null;

            Vector2 tileSize = new Vector2(
                tileSizeObj?["x"]?.ToObject<float>() ?? 15f,
                tileSizeObj?["y"]?.ToObject<float>() ?? 15f
            );

            // Create TerrainLayer
            TerrainLayer layer = new TerrainLayer
            {
                diffuseTexture = diffuseTex,
                normalMapTexture = normalTex,
                tileSize = tileSize
            };

            // Save layer asset if requested or auto-generate
            if (string.IsNullOrWhiteSpace(layerAssetPath))
            {
                string folder = "Assets/Terrains/Layers";
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    AssetDatabase.Refresh();
                }
                layerAssetPath = $"{folder}/{diffuseTex.name}_Layer.terrainlayer";
                layerAssetPath = AssetDatabase.GenerateUniqueAssetPath(layerAssetPath);
            }

            AssetDatabase.CreateAsset(layer, layerAssetPath);
            AssetDatabase.SaveAssets();

            // Append layer to terrain
            List<TerrainLayer> currentLayers = new List<TerrainLayer>(terrainData.terrainLayers ?? new TerrainLayer[0]);
            currentLayers.Add(layer);
            terrainData.terrainLayers = currentLayers.ToArray();

            EditorUtility.SetDirty(terrainData);
            terrain.Flush();

            McpLogger.LogInfo($"[MCP Unity] Added TerrainLayer '{diffuseTex.name}' to Terrain '{terrain.name}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully added terrain texture layer '{diffuseTex.name}' to '{terrain.name}' (Layer index: {currentLayers.Count - 1})",
                ["layerIndex"] = currentLayers.Count - 1,
                ["layerAssetPath"] = layerAssetPath,
                ["diffuseTexture"] = diffuseTex.name
            };
        }

        private JObject HandleAddTrees(JObject parameters, string reason)
        {
            if (!TryGetTerrain(parameters, out Terrain terrain, out TerrainData terrainData, out JObject error))
            {
                return error;
            }

            string prefabPath = parameters["treePrefab"]?.ToObject<string>();
            int count = parameters["count"]?.ToObject<int?>() ?? 50;
            float minHeight = Mathf.Clamp01(parameters["minHeight"]?.ToObject<float>() ?? 0f);
            float maxHeight = Mathf.Clamp01(parameters["maxHeight"]?.ToObject<float>() ?? 1f);
            float minScale = parameters["randomScaleMin"]?.ToObject<float>() ?? 0.8f;
            float maxScale = parameters["randomScaleMax"]?.ToObject<float>() ?? 1.2f;
            bool clearExisting = parameters["clearExisting"]?.ToObject<bool?>() ?? false;

            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse("Parameter 'treePrefab' is required.", "validation_error");
            }

            GameObject treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (treePrefab == null)
            {
                string[] guids = AssetDatabase.FindAssets($"{prefabPath} t:Prefab");
                if (guids.Length > 0)
                {
                    treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            if (treePrefab == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Could not find tree prefab at '{prefabPath}'", "prefab_not_found");
            }

            // Find or add tree prototype
            int prototypeIndex = -1;
            List<TreePrototype> prototypes = new List<TreePrototype>(terrainData.treePrototypes ?? new TreePrototype[0]);
            for (int i = 0; i < prototypes.Count; i++)
            {
                if (prototypes[i].prefab == treePrefab)
                {
                    prototypeIndex = i;
                    break;
                }
            }

            if (prototypeIndex == -1)
            {
                TreePrototype newProto = new TreePrototype { prefab = treePrefab };
                prototypes.Add(newProto);
                terrainData.treePrototypes = prototypes.ToArray();
                prototypeIndex = prototypes.Count - 1;
            }

            List<TreeInstance> treeInstances = clearExisting ? new List<TreeInstance>() : new List<TreeInstance>(terrainData.treeInstances);
            int addedCount = 0;

            for (int i = 0; i < count; i++)
            {
                float normX = UnityEngine.Random.Range(0.02f, 0.98f);
                float normZ = UnityEngine.Random.Range(0.02f, 0.98f);
                float normHeight = terrainData.GetInterpolatedHeight(normX, normZ) / terrainData.size.y;

                if (normHeight < minHeight || normHeight > maxHeight)
                {
                    continue;
                }

                float scale = UnityEngine.Random.Range(minScale, maxScale);
                TreeInstance instance = new TreeInstance
                {
                    position = new Vector3(normX, normHeight, normZ),
                    prototypeIndex = prototypeIndex,
                    widthScale = scale,
                    heightScale = scale,
                    color = Color.white,
                    lightmapColor = Color.white,
                    rotation = UnityEngine.Random.Range(0f, Mathf.PI * 2f)
                };

                treeInstances.Add(instance);
                addedCount++;
            }

            terrainData.treeInstances = treeInstances.ToArray();
            terrain.Flush();
            EditorUtility.SetDirty(terrainData);

            McpLogger.LogInfo($"[MCP Unity] Scattered {addedCount} trees on Terrain '{terrain.name}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully scattered {addedCount} tree instances on '{terrain.name}' (Total trees: {terrainData.treeInstanceCount})",
                ["treesAdded"] = addedCount,
                ["totalTrees"] = terrainData.treeInstanceCount,
                ["treePrefab"] = treePrefab.name
            };
        }

        private JObject HandleGetInfo(JObject parameters)
        {
            if (!TryGetTerrain(parameters, out Terrain terrain, out TerrainData terrainData, out JObject error))
            {
                return error;
            }

            JArray layersArray = new JArray();
            if (terrainData.terrainLayers != null)
            {
                for (int i = 0; i < terrainData.terrainLayers.Length; i++)
                {
                    var l = terrainData.terrainLayers[i];
                    layersArray.Add(new JObject
                    {
                        ["index"] = i,
                        ["name"] = l != null ? l.name : "null",
                        ["diffuse"] = l?.diffuseTexture != null ? l.diffuseTexture.name : null,
                        ["tileSize"] = l != null ? new JObject { ["x"] = l.tileSize.x, ["y"] = l.tileSize.y } : null
                    });
                }
            }

            JArray prototypesArray = new JArray();
            if (terrainData.treePrototypes != null)
            {
                for (int i = 0; i < terrainData.treePrototypes.Length; i++)
                {
                    var p = terrainData.treePrototypes[i];
                    prototypesArray.Add(new JObject
                    {
                        ["index"] = i,
                        ["prefabName"] = p.prefab != null ? p.prefab.name : "null"
                    });
                }
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Terrain '{terrain.name}' info retrieved.",
                ["terrainName"] = terrain.name,
                ["position"] = new JObject { ["x"] = terrain.transform.position.x, ["y"] = terrain.transform.position.y, ["z"] = terrain.transform.position.z },
                ["size"] = new JObject { ["x"] = terrainData.size.x, ["y"] = terrainData.size.y, ["z"] = terrainData.size.z },
                ["heightmapResolution"] = terrainData.heightmapResolution,
                ["alphamapResolution"] = terrainData.alphamapResolution,
                ["treeCount"] = terrainData.treeInstanceCount,
                ["layers"] = layersArray,
                ["treePrototypes"] = prototypesArray
            };
        }

        private bool TryGetTerrain(JObject parameters, out Terrain terrain, out TerrainData terrainData, out JObject error)
        {
            terrain = null;
            terrainData = null;
            error = null;

            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();

            GameObject go;
            if (instanceId.HasValue || !string.IsNullOrEmpty(objectPath))
            {
                error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out go, out string idInfo);
                if (error != null) return false;
            }
            else
            {
                terrain = Terrain.activeTerrain;
                if (terrain != null)
                {
                    terrainData = terrain.terrainData;
                    return true;
                }
                error = McpUnitySocketHandler.CreateErrorResponse("No active Terrain found in scene, and no instanceId or objectPath was provided.", "terrain_not_found");
                return false;
            }

            terrain = go.GetComponent<Terrain>();
            if (terrain == null)
            {
                error = McpUnitySocketHandler.CreateErrorResponse($"GameObject '{go.name}' does not have a Terrain component.", "not_a_terrain");
                return false;
            }

            terrainData = terrain.terrainData;
            if (terrainData == null)
            {
                error = McpUnitySocketHandler.CreateErrorResponse($"Terrain on GameObject '{go.name}' has no TerrainData assigned.", "missing_terrain_data");
                return false;
            }

            return true;
        }

        private Texture2D LoadTexture(string pathOrName)
        {
            if (pathOrName.StartsWith("Assets/") || pathOrName.StartsWith("Packages/"))
            {
                return AssetDatabase.LoadAssetAtPath<Texture2D>(pathOrName);
            }

            string[] guids = AssetDatabase.FindAssets($"{pathOrName} t:Texture2D");
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            return null;
        }

        private bool IsPowerOfTwo(int x)
        {
            return (x > 0) && ((x & (x - 1)) == 0);
        }
    }
}
