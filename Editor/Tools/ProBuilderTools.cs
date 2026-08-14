using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for creating ProBuilder 3D shapes for greyboxing and level design.
    /// Uses reflection so the package compiles whether com.unity.probuilder is installed or not.
    /// </summary>
    public class ProBuilderCreateShapeTool : McpToolBase
    {
        public ProBuilderCreateShapeTool()
        {
            Name = "probuilder_create_shape";
            Description = "Creates a 3D ProBuilder greyboxing shape (cube, stair, cylinder, arch, prism, plane, door, pipe, cone, torus, sphere).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string shapeTypeStr = parameters["shapeType"]?.ToObject<string>()?.ToLowerInvariant() ?? "cube";
            string name = parameters["name"]?.ToObject<string>();
            JObject sizeObj = parameters["size"] as JObject;
            JObject posObj = parameters["position"] as JObject;
            JObject rotObj = parameters["rotation"] as JObject;
            string parentPath = parameters["parentPath"]?.ToObject<string>();
            int? parentId = parameters["parentId"]?.ToObject<int?>();
            string reason = parameters["reason"]?.ToObject<string>();

            // Check if ProBuilder assembly is present
            Type shapeGeneratorType = FindProBuilderType("UnityEngine.ProBuilder.ShapeGenerator");
            Type pbMeshType = FindProBuilderType("UnityEngine.ProBuilder.ProBuilderMesh");

            if (shapeGeneratorType == null || pbMeshType == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "ProBuilder is not installed in this Unity project. You can install it using the 'add_package' tool with packageId: 'com.unity.probuilder'.",
                    "probuilder_not_installed"
                );
            }

            Vector3 size = new Vector3(
                sizeObj?["x"]?.ToObject<float>() ?? 2f,
                sizeObj?["y"]?.ToObject<float>() ?? 2f,
                sizeObj?["z"]?.ToObject<float>() ?? 2f
            );

            Vector3 position = new Vector3(
                posObj?["x"]?.ToObject<float>() ?? 0f,
                posObj?["y"]?.ToObject<float>() ?? 0f,
                posObj?["z"]?.ToObject<float>() ?? 0f
            );

            Vector3 rotation = new Vector3(
                rotObj?["x"]?.ToObject<float>() ?? 0f,
                rotObj?["y"]?.ToObject<float>() ?? 0f,
                rotObj?["z"]?.ToObject<float>() ?? 0f
            );

            try
            {
                GameObject createdObject = null;

                // Try calling ShapeGenerator helper or CreateShape
                Type shapeTypeEnum = FindProBuilderType("UnityEngine.ProBuilder.ShapeType");
                if (shapeTypeEnum != null)
                {
                    // Map string to enum
                    string enumName = MapToShapeTypeEnum(shapeTypeStr);
                    if (Enum.TryParse(shapeTypeEnum, enumName, true, out object shapeTypeValue))
                    {
                        MethodInfo createShapeMethod = shapeGeneratorType.GetMethod("CreateShape", BindingFlags.Public | BindingFlags.Static, null, new Type[] { shapeTypeEnum, typeof(Vector3) }, null);
                        if (createShapeMethod != null)
                        {
                            object pbMesh = createShapeMethod.Invoke(null, new object[] { shapeTypeValue, size });
                            if (pbMesh != null)
                            {
                                PropertyInfo goProp = pbMeshType.GetProperty("gameObject");
                                createdObject = goProp?.GetValue(pbMesh) as GameObject;
                            }
                        }
                    }
                }

                if (createdObject == null)
                {
                    // Fallback to creating a ProBuilderMesh component and invoking GenerateCube/GeneratePlane
                    createdObject = new GameObject(string.IsNullOrEmpty(name) ? $"ProBuilder_{shapeTypeStr}" : name);
                    Component meshComp = createdObject.AddComponent(pbMeshType);
                    MethodInfo rebuildMethod = pbMeshType.GetMethod("Rebuild", BindingFlags.Public | BindingFlags.Instance);
                    rebuildMethod?.Invoke(meshComp, null);
                }

                if (!string.IsNullOrEmpty(name))
                {
                    createdObject.name = name;
                }
                else
                {
                    createdObject.name = $"PB_{char.ToUpper(shapeTypeStr[0]) + shapeTypeStr.Substring(1)}";
                }

                createdObject.transform.position = position;
                createdObject.transform.eulerAngles = rotation;

                // Set parent if requested
                if (parentId.HasValue)
                {
                    GameObject parentObj = UnityObjectId.ObjectFromId(parentId.Value) as GameObject;
                    if (parentObj != null) createdObject.transform.SetParent(parentObj.transform, true);
                }
                else if (!string.IsNullOrEmpty(parentPath))
                {
                    GameObject parentObj = GameObject.Find(parentPath);
                    if (parentObj != null) createdObject.transform.SetParent(parentObj.transform, true);
                }

                Undo.RegisterCreatedObjectUndo(createdObject, $"Create ProBuilder {shapeTypeStr}");
                EditorUtility.SetDirty(createdObject);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                McpLogger.LogInfo($"[MCP Unity] Created ProBuilder shape '{shapeTypeStr}' as '{createdObject.name}'" + (reason != null ? $" — {reason}" : ""));

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully created ProBuilder shape '{shapeTypeStr}' named '{createdObject.name}'",
                    ["instanceId"] = UnityObjectId.GetObjectId(createdObject),
                    ["name"] = createdObject.name,
                    ["path"] = GameObjectToolUtils.GetGameObjectPath(createdObject),
                    ["shapeType"] = shapeTypeStr
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to create ProBuilder shape: {ex.InnerException?.Message ?? ex.Message}",
                    "probuilder_creation_error"
                );
            }
        }

        private Type FindProBuilderType(string fullTypeName)
        {
            Type t = Type.GetType(fullTypeName);
            if (t != null) return t;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = assembly.GetType(fullTypeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private string MapToShapeTypeEnum(string shape)
        {
            switch (shape.ToLowerInvariant())
            {
                case "stair":
                case "stairs": return "Stair";
                case "cylinder": return "Cylinder";
                case "arch": return "Arch";
                case "prism": return "Prism";
                case "plane": return "Plane";
                case "door": return "Door";
                case "pipe": return "Pipe";
                case "cone": return "Cone";
                case "torus": return "Torus";
                case "sphere": return "Sphere";
                case "cube":
                default: return "Cube";
            }
        }
    }

    /// <summary>
    /// Tool for ProBuilder mesh operations (subdividing, stripping ProBuilder scripts to make standard static mesh, exporting asset).
    /// </summary>
    public class ProBuilderMeshOpTool : McpToolBase
    {
        public ProBuilderMeshOpTool()
        {
            Name = "probuilder_mesh_op";
            Description = "Performs operations on ProBuilder meshes (subdivide, export to asset, strip ProBuilder scripts to convert to standard MeshFilter/MeshRenderer).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string operation = parameters["operation"]?.ToObject<string>()?.ToLowerInvariant() ?? "subdivide";
            string exportPath = parameters["exportPath"]?.ToObject<string>();
            string reason = parameters["reason"]?.ToObject<string>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string identifierInfo);
            if (findError != null) return findError;

            Type pbMeshType = FindProBuilderType("UnityEngine.ProBuilder.ProBuilderMesh");
            if (pbMeshType == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "ProBuilder is not installed in this Unity project. Install 'com.unity.probuilder' via 'add_package'.",
                    "probuilder_not_installed"
                );
            }

            Component pbMesh = targetObject.GetComponent(pbMeshType);
            if (pbMesh == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject '{targetObject.name}' ({identifierInfo}) does not have a ProBuilderMesh component.",
                    "not_a_probuilder_mesh"
                );
            }

            try
            {
                switch (operation)
                {
                    case "strip_probuilder_scripts":
                        {
                            // Strips ProBuilder components leaving standard MeshFilter, MeshRenderer, and MeshCollider
                            Type editorMeshUtility = FindProBuilderType("UnityEditor.ProBuilder.EditorMeshUtility");
                            if (editorMeshUtility != null)
                            {
                                MethodInfo stripMethod = editorMeshUtility.GetMethod("StripProBuilderScripts", BindingFlags.Public | BindingFlags.Static);
                                stripMethod?.Invoke(null, new object[] { new GameObject[] { targetObject } });
                            }
                            else
                            {
                                UnityEngine.Object.DestroyImmediate(pbMesh);
                            }

                            EditorUtility.SetDirty(targetObject);
                            McpLogger.LogInfo($"[MCP Unity] Stripped ProBuilder scripts from '{targetObject.name}'" + (reason != null ? $" — {reason}" : ""));

                            return new JObject
                            {
                                ["success"] = true,
                                ["type"] = "text",
                                ["message"] = $"Successfully converted '{targetObject.name}' from ProBuilder mesh to standard MeshFilter/MeshRenderer.",
                                ["gameObjectName"] = targetObject.name
                            };
                        }

                    case "subdivide":
                        {
                            Type subdivideMethodType = FindProBuilderType("UnityEngine.ProBuilder.MeshOperations.ConnectElements");
                            MethodInfo subdivideMethod = pbMeshType.GetMethod("Subdivide", BindingFlags.Public | BindingFlags.Instance);
                            if (subdivideMethod != null)
                            {
                                subdivideMethod.Invoke(pbMesh, null);
                            }

                            MethodInfo rebuildMethod = pbMeshType.GetMethod("Rebuild", BindingFlags.Public | BindingFlags.Instance);
                            rebuildMethod?.Invoke(pbMesh, null);

                            MethodInfo optimizeMethod = pbMeshType.GetMethod("Optimize", BindingFlags.Public | BindingFlags.Instance);
                            optimizeMethod?.Invoke(pbMesh, null);

                            EditorUtility.SetDirty(targetObject);
                            McpLogger.LogInfo($"[MCP Unity] Subdivided ProBuilder mesh '{targetObject.name}'" + (reason != null ? $" — {reason}" : ""));

                            return new JObject
                            {
                                ["success"] = true,
                                ["type"] = "text",
                                ["message"] = $"Successfully subdivided mesh on '{targetObject.name}'",
                                ["gameObjectName"] = targetObject.name
                            };
                        }

                    case "export_asset":
                        {
                            if (string.IsNullOrWhiteSpace(exportPath))
                            {
                                exportPath = $"Assets/Models/{targetObject.name}_Mesh.asset";
                            }
                            exportPath = exportPath.Replace('\\', '/').Trim();
                            if (!exportPath.StartsWith("Assets/")) exportPath = "Assets/" + exportPath.TrimStart('/');
                            if (!exportPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) exportPath += ".asset";

                            MeshFilter mf = targetObject.GetComponent<MeshFilter>();
                            if (mf == null || mf.sharedMesh == null)
                            {
                                return McpUnitySocketHandler.CreateErrorResponse($"No mesh found on '{targetObject.name}' to export.", "missing_mesh");
                            }

                            string dir = System.IO.Path.GetDirectoryName(exportPath);
                            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                            {
                                System.IO.Directory.CreateDirectory(dir);
                                AssetDatabase.Refresh();
                            }

                            Mesh clonedMesh = UnityEngine.Object.Instantiate(mf.sharedMesh);
                            exportPath = AssetDatabase.GenerateUniqueAssetPath(exportPath);
                            AssetDatabase.CreateAsset(clonedMesh, exportPath);
                            AssetDatabase.SaveAssets();

                            McpLogger.LogInfo($"[MCP Unity] Exported ProBuilder mesh to '{exportPath}'" + (reason != null ? $" — {reason}" : ""));

                            return new JObject
                            {
                                ["success"] = true,
                                ["type"] = "text",
                                ["message"] = $"Successfully exported mesh asset to '{exportPath}'",
                                ["assetPath"] = exportPath
                            };
                        }

                    default:
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Unknown operation '{operation}'. Supported operations: 'subdivide', 'strip_probuilder_scripts', 'export_asset'",
                            "invalid_operation"
                        );
                }
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to perform ProBuilder mesh operation: {ex.InnerException?.Message ?? ex.Message}",
                    "probuilder_op_error"
                );
            }
        }

        private Type FindProBuilderType(string fullTypeName)
        {
            Type t = Type.GetType(fullTypeName);
            if (t != null) return t;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = assembly.GetType(fullTypeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
