using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for creating ScriptableObject asset instances in the Unity project.
    /// </summary>
    public class CreateScriptableObjectTool : McpToolBase
    {
        public CreateScriptableObjectTool()
        {
            Name = "create_scriptable_object";
            Description = "Creates a new ScriptableObject asset file (.asset) in the project with optional field values.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string className = parameters["className"]?.ToObject<string>();
            string assetPath = parameters["assetPath"]?.ToObject<string>();
            JObject fieldValues = parameters["fieldValues"] as JObject;
            string reason = parameters["reason"]?.ToObject<string>();

            if (string.IsNullOrWhiteSpace(className))
            {
                return McpUnitySocketHandler.CreateErrorResponse("Parameter 'className' must be provided.", "validation_error");
            }

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse("Parameter 'assetPath' must be provided (e.g. 'Assets/Data/MyConfig.asset').", "validation_error");
            }

            // Normalize asset path
            assetPath = assetPath.Replace('\\', '/').Trim();
            if (!assetPath.StartsWith("Assets/"))
            {
                assetPath = "Assets/" + assetPath.TrimStart('/');
            }
            if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                assetPath += ".asset";
            }

            // Find ScriptableObject type
            Type soType = FindScriptableObjectType(className);
            if (soType == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"ScriptableObject class '{className}' not found. Make sure it inherits from ScriptableObject and is compiled.",
                    "type_not_found"
                );
            }

            try
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    AssetDatabase.Refresh();
                }

                // Check if file already exists
                string existingGuid = AssetDatabase.AssetPathToGUID(assetPath);
                if (!string.IsNullOrEmpty(existingGuid))
                {
                    // Generate unique path
                    assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                }

                // Create instance
                ScriptableObject soInstance = ScriptableObject.CreateInstance(soType);
                if (soInstance == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse($"Failed to instantiate ScriptableObject of type '{className}'", "creation_failed");
                }

                // Apply field values if provided
                if (fieldValues != null && fieldValues.Count > 0)
                {
                    SerializedObject serializedObject = new SerializedObject(soInstance);
                    foreach (var prop in fieldValues.Properties())
                    {
                        SerializedProperty serializedProp = serializedObject.FindProperty(prop.Name);
                        if (serializedProp != null)
                        {
                            ApplySerializedValue(serializedProp, prop.Value);
                        }
                        else
                        {
                            FieldInfo field = soType.GetField(prop.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (field != null)
                            {
                                try
                                {
                                    object val = prop.Value.ToObject(field.FieldType);
                                    field.SetValue(soInstance, val);
                                }
                                catch { }
                            }
                        }
                    }
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                }

                // Save as asset
                AssetDatabase.CreateAsset(soInstance, assetPath);
                EditorUtility.SetDirty(soInstance);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                string guid = AssetDatabase.AssetPathToGUID(assetPath);

                McpLogger.LogInfo($"[MCP Unity] Created ScriptableObject '{className}' at '{assetPath}' (GUID: {guid})" + (reason != null ? $" — {reason}" : ""));

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully created ScriptableObject '{className}' at '{assetPath}'",
                    ["assetPath"] = assetPath,
                    ["guid"] = guid,
                    ["className"] = className
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error creating ScriptableObject at '{assetPath}': {ex.Message}",
                    "scriptable_object_error"
                );
            }
        }

        private Type FindScriptableObjectType(string className)
        {
            Type direct = Type.GetType(className);
            if (direct != null && typeof(ScriptableObject).IsAssignableFrom(direct))
            {
                return direct;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type t in assembly.GetTypes())
                    {
                        if ((t.Name == className || t.FullName == className) && typeof(ScriptableObject).IsAssignableFrom(t) && !t.IsAbstract)
                        {
                            return t;
                        }
                    }
                }
                catch
                {
                    // Skip assemblies that fail to enumerate
                }
            }

            return null;
        }

        private void ApplySerializedValue(SerializedProperty prop, JToken value)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = value.ToObject<int>();
                        break;
                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.ToObject<bool>();
                        break;
                    case SerializedPropertyType.Float:
                        prop.floatValue = value.ToObject<float>();
                        break;
                    case SerializedPropertyType.String:
                        prop.stringValue = value.Type == JTokenType.Null ? null : value.ToObject<string>();
                        break;
                    case SerializedPropertyType.Color:
                        if (value.Type == JTokenType.Object)
                        {
                            float r = value["r"]?.ToObject<float>() ?? 0;
                            float g = value["g"]?.ToObject<float>() ?? 0;
                            float b = value["b"]?.ToObject<float>() ?? 0;
                            float a = value["a"]?.ToObject<float>() ?? 1;
                            prop.colorValue = new Color(r, g, b, a);
                        }
                        break;
                    case SerializedPropertyType.Vector2:
                        if (value.Type == JTokenType.Object)
                        {
                            prop.vector2Value = new Vector2(value["x"]?.ToObject<float>() ?? 0, value["y"]?.ToObject<float>() ?? 0);
                        }
                        break;
                    case SerializedPropertyType.Vector3:
                        if (value.Type == JTokenType.Object)
                        {
                            prop.vector3Value = new Vector3(value["x"]?.ToObject<float>() ?? 0, value["y"]?.ToObject<float>() ?? 0, value["z"]?.ToObject<float>() ?? 0);
                        }
                        break;
                    case SerializedPropertyType.Enum:
                        if (value.Type == JTokenType.Integer)
                        {
                            prop.enumValueIndex = value.ToObject<int>();
                        }
                        else if (value.Type == JTokenType.String)
                        {
                            string s = value.ToObject<string>();
                            for (int i = 0; i < prop.enumNames.Length; i++)
                            {
                                if (string.Equals(prop.enumNames[i], s, StringComparison.OrdinalIgnoreCase))
                                {
                                    prop.enumValueIndex = i;
                                    break;
                                }
                            }
                        }
                        break;
                    case SerializedPropertyType.ObjectReference:
                        if (value.Type == JTokenType.String)
                        {
                            string p = value.ToObject<string>();
                            if (p.StartsWith("Assets/"))
                            {
                                prop.objectReferenceValue = AssetDatabase.LoadMainAssetAtPath(p);
                            }
                            else
                            {
                                string[] guids = AssetDatabase.FindAssets(p);
                                if (guids.Length > 0)
                                {
                                    prop.objectReferenceValue = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]));
                                }
                            }
                        }
                        break;
                }
            }
            catch { }
        }
    }
}
