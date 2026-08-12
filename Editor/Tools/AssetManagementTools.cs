using System;
using System.IO;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for deleting an asset or folder in the project and removing its .meta file
    /// </summary>
    public class DeleteAssetTool : McpToolBase
    {
        public DeleteAssetTool()
        {
            Name = "delete_asset";
            Description = "Deletes an asset or folder at the specified path and cleans up its corresponding .meta file";
        }

        public override JObject Execute(JObject parameters)
        {
            string assetPath = parameters["assetPath"]?.ToObject<string>();

            if (string.IsNullOrEmpty(assetPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'assetPath' must be provided",
                    "validation_error"
                );
            }

            if (!assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'assetPath' must start with 'Assets'",
                    "validation_error"
                );
            }

            try
            {
                bool success = AssetDatabase.DeleteAsset(assetPath);
                if (success)
                {
                    McpLogger.LogInfo($"[MCP Unity] Deleted asset at '{assetPath}'");
                    return new JObject
                    {
                        ["success"] = true,
                        ["message"] = $"Successfully deleted asset at '{assetPath}'"
                    };
                }
                else
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to delete asset at '{assetPath}'. File may not exist or is write-protected.",
                        "delete_failed"
                    );
                }
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error deleting asset: {ex.Message}",
                    "delete_error"
                );
            }
        }
    }

    /// <summary>
    /// Tool for renaming an asset or folder in the project
    /// </summary>
    public class RenameAssetTool : McpToolBase
    {
        public RenameAssetTool()
        {
            Name = "rename_asset";
            Description = "Renames an asset or folder at the specified path";
        }

        public override JObject Execute(JObject parameters)
        {
            string assetPath = parameters["assetPath"]?.ToObject<string>();
            string newName = parameters["newName"]?.ToObject<string>();

            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(newName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameters 'assetPath' and 'newName' must be provided",
                    "validation_error"
                );
            }

            if (!assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'assetPath' must start with 'Assets'",
                    "validation_error"
                );
            }

            try
            {
                string extension = Path.GetExtension(assetPath);
                // Strip extension from newName if user provided it, as RenameAsset expects just the name
                if (!string.IsNullOrEmpty(extension) && newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    newName = newName.Substring(0, newName.Length - extension.Length);
                }

                string resultMessage = AssetDatabase.RenameAsset(assetPath, newName);
                if (string.IsNullOrEmpty(resultMessage))
                {
                    string directory = Path.GetDirectoryName(assetPath);
                    string newPath = Path.Combine(directory, newName + extension).Replace("\\", "/");
                    McpLogger.LogInfo($"[MCP Unity] Renamed asset at '{assetPath}' to '{newName}'");

                    return new JObject
                    {
                        ["success"] = true,
                        ["message"] = $"Successfully renamed asset at '{assetPath}' to '{newName}'",
                        ["newName"] = newName,
                        ["newPath"] = newPath
                    };
                }
                else
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to rename asset: {resultMessage}",
                        "rename_failed"
                    );
                }
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error renaming asset: {ex.Message}",
                    "rename_error"
                );
            }
        }
    }

    /// <summary>
    /// Tool for creating a new folder in the project Assets database
    /// </summary>
    public class CreateFolderTool : McpToolBase
    {
        public CreateFolderTool()
        {
            Name = "create_folder";
            Description = "Creates a new folder at the specified parent path";
        }

        public override JObject Execute(JObject parameters)
        {
            string parentFolder = parameters["parentFolder"]?.ToObject<string>();
            string newFolderName = parameters["newFolderName"]?.ToObject<string>();

            if (string.IsNullOrEmpty(parentFolder) || string.IsNullOrEmpty(newFolderName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameters 'parentFolder' and 'newFolderName' must be provided",
                    "validation_error"
                );
            }

            if (!parentFolder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'parentFolder' must start with 'Assets'",
                    "validation_error"
                );
            }

            try
            {
                string guid = AssetDatabase.CreateFolder(parentFolder, newFolderName);
                if (!string.IsNullOrEmpty(guid))
                {
                    string newFolderPath = Path.Combine(parentFolder, newFolderName).Replace("\\", "/");
                    McpLogger.LogInfo($"[MCP Unity] Created folder '{newFolderName}' in '{parentFolder}'");

                    return new JObject
                    {
                        ["success"] = true,
                        ["message"] = $"Successfully created folder '{newFolderName}' in '{parentFolder}'",
                        ["guid"] = guid,
                        ["folderPath"] = newFolderPath
                    };
                }
                else
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to create folder '{newFolderName}' in '{parentFolder}'. Ensure parent path exists and folder name is valid.",
                        "create_folder_failed"
                    );
                }
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error creating folder: {ex.Message}",
                    "create_folder_error"
                );
            }
        }
    }
}
