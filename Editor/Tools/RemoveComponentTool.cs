using System;
using System.Linq;
using System.Reflection;
using McpUnity.Unity;
using McpUnity.Utils;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for removing a component from a GameObject in the Unity Editor.
    /// Uses the Undo system so the removal can be reverted in the Editor.
    /// </summary>
    public class RemoveComponentTool : McpToolBase
    {
        public RemoveComponentTool()
        {
            Name = "remove_component";
            Description = "Removes a component from a GameObject. The removal is registered with the Undo system, so it can be reverted with Ctrl/Cmd+Z in the Editor.";
        }

        /// <summary>
        /// Execute the RemoveComponent tool with the provided parameters synchronously
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string componentName = parameters["componentName"]?.ToObject<string>();
            string reason = parameters["reason"]?.ToObject<string>();

            // Validate parameters - require either instanceId or objectPath
            if (!instanceId.HasValue && string.IsNullOrEmpty(objectPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Either 'instanceId' or 'objectPath' must be provided",
                    "validation_error"
                );
            }

            if (string.IsNullOrEmpty(componentName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'componentName' not provided",
                    "validation_error"
                );
            }

            // Find the GameObject by instance ID or path (shared resolver: all loaded scenes,
            // includes inactive objects - see Editor/Utils/GameObjectResolver.cs)
            GameObjectResolver.Result findResult = GameObjectResolver.Find(instanceId, objectPath);
            if (findResult.Error != null) return findResult.Error;

            GameObject gameObject = findResult.GameObject;
            string identifier = instanceId.HasValue ? $"ID {instanceId.Value}" : $"path '{objectPath}'";

            // Find the component by name
            Component component = gameObject.GetComponent(componentName);
            if (component == null)
            {
                Type componentType = FindComponentType(componentName);
                if (componentType == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Component type '{componentName}' not found in Unity",
                        "component_error"
                    );
                }

                component = gameObject.GetComponent(componentType);
                if (component == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Component '{componentName}' not found on GameObject '{gameObject.name}'",
                        "component_error"
                    );
                }
            }

            string componentDisplayName = component.GetType().Name;

            McpLogger.LogInfo($"[MCP Unity] Removing component '{componentDisplayName}' from GameObject '{gameObject.name}' (found by {identifier})");

            // Remove the component with Undo support so it can be reverted in the Editor
            Undo.DestroyObjectImmediate(component);

            // Ensure changes are saved
            EditorUtility.SetDirty(gameObject);
            if (PrefabUtility.IsPartOfAnyPrefab(gameObject))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
            }

            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"{Name}: removed component '{componentDisplayName}' from '{gameObject.name}'" + (reason != null ? $" — {reason}" : ""));

            // Create the response
            JObject response = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully removed component '{componentDisplayName}' from GameObject '{gameObject.name}'"
            };

            if (EditorApplication.isPlaying)
            {
                response["warning"] = "Unity is in Play Mode. Scene component edits will be discarded when Play Mode stops unless saved to a prefab.";
            }

            return response;
        }

        /// <summary>
        /// Find a component type by name (mirrors UpdateComponentTool lookup)
        /// </summary>
        /// <param name="componentName">The name of the component type</param>
        /// <returns>The component type, or null if not found</returns>
        private Type FindComponentType(string componentName)
        {
            // First try direct match
            Type type = Type.GetType(componentName);
            if (type != null && typeof(Component).IsAssignableFrom(type))
            {
                return type;
            }

            // Try common Unity namespaces
            string[] commonNamespaces = new string[]
            {
                "UnityEngine",
                "UnityEngine.UI",
                "UnityEngine.EventSystems",
                "UnityEngine.Animations",
                "UnityEngine.Rendering",
                "TMPro"
            };

            foreach (string ns in commonNamespaces)
            {
                type = Type.GetType($"{ns}.{componentName}, UnityEngine");
                if (type != null && typeof(Component).IsAssignableFrom(type))
                {
                    return type;
                }
            }

            // Try assemblies search
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type t in assembly.GetTypes())
                    {
                        if (t.Name == componentName && typeof(Component).IsAssignableFrom(t))
                        {
                            return t;
                        }
                    }
                }
                catch (Exception)
                {
                    // Some assemblies might throw exceptions when getting types
                    continue;
                }
            }

            return null;
        }
    }
}