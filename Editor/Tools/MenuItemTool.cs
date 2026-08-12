using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using McpUnity.Unity;
using McpUnity.Utils;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for executing Unity Editor menu items
    /// </summary>
    public class MenuItemTool : McpToolBase
    {
        /// <summary>
        /// Menu paths that are never safe to expose to an MCP client: they quit the Editor, wipe
        /// project/version-control state, or open modal dialogs that block the Editor's main
        /// thread (and therefore this tool's own response) indefinitely. execute_menu_item
        /// otherwise has no restrictions at all - any MenuItem-tagged method in the project,
        /// including ones added by third-party packages, is reachable by exact path.
        /// </summary>
        private static readonly HashSet<string> DeniedMenuPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "File/Quit",
            "Edit/Preferences...",
            "Edit/Project Settings...",
            "Edit/Clear All PlayerPrefs",
            "Unity/Preferences...",
        };

        public MenuItemTool()
        {
            Name = "execute_menu_item";
            Description = "Executes functions tagged with the MenuItem attribute. A small set of destructive or modal-dialog menu paths (Quit, Preferences, Project Settings, Clear All PlayerPrefs) is blocked.";
        }

        /// <summary>
        /// Execute the MenuItem tool with the provided parameters synchronously
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            // Extract parameters with defaults
            string menuPath = parameters["menuPath"]?.ToObject<string>();
            if (string.IsNullOrEmpty(menuPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'menuPath' not provided",
                    "validation_error"
                );
            }

            if (DeniedMenuPaths.Contains(menuPath.Trim()))
            {
                McpLogger.LogWarning($"[MCP Unity] Blocked execution of denied menu item: {menuPath}");
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Menu item '{menuPath}' is blocked (destructive or modal-dialog action not permitted via MCP).",
                    "denied_error"
                );
            }

            // Log the execution
            McpLogger.LogInfo($"[MCP Unity] Executing menu item: {menuPath}");

            // Execute the menu item
            bool success = EditorApplication.ExecuteMenuItem(menuPath);
                
            // Create the response
            return new JObject
            {
                ["success"] = success,
                ["type"] = "text",
                ["message"] = success 
                    ? $"Successfully executed menu item: {menuPath}" 
                    : $"Failed to execute menu item: {menuPath}"
            };
        }
    }
}
