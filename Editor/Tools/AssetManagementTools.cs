using System;
using System.IO;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Shared validation for the project-relative paths these tools accept.
    /// </summary>
    internal static class AssetPathGuard
    {
        /// <summary>
        /// Validates that a path is genuinely inside the project's Assets folder.
        ///
        /// A bare StartsWith("Assets") check is not enough: it accepts sibling directories such
        /// as "AssetsBackup/..." and, more importantly, traversal like "Assets/../ProjectSettings"
        /// which escapes the folder entirely. That matters most for delete_asset, where the cost
        /// of getting it wrong is destroyed files outside the intended tree.
        /// </summary>
        /// <param name="path">The candidate project-relative path.</param>
        /// <param name="parameterName">Parameter name to quote in the error message.</param>
        /// <param name="error">Populated with an error response when validation fails.</param>
        public static bool IsValidAssetsPath(string path, string parameterName, out JObject error)
        {
            error = null;

            if (string.IsNullOrEmpty(path))
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    $"Parameter '{parameterName}' must be provided",
                    "validation_error"
                );
                return false;
            }

            // Normalize separators so Windows-style input is checked the same way.
            string normalized = path.Replace('\\', '/').TrimEnd('/');

            bool underAssets = normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);

            if (!underAssets)
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    $"Parameter '{parameterName}' must be a project-relative path under 'Assets/'",
                    "validation_error"
                );
                return false;
            }

            foreach (string segment in normalized.Split('/'))
            {
                if (segment == "..")
                {
                    error = McpUnitySocketHandler.CreateErrorResponse(
                        $"Parameter '{parameterName}' must not contain '..' path segments",
                        "validation_error"
                    );
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Extension of an asset path, or empty for folders. Path.GetExtension alone is wrong
        /// here: a folder named "v1.2" yields ".2", which would then be stripped off a rename and
        /// re-appended to the resulting path.
        /// </summary>
        public static string GetAssetExtension(string assetPath)
        {
            return AssetDatabase.IsValidFolder(assetPath) ? string.Empty : Path.GetExtension(assetPath);
        }
    }

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

            if (!AssetPathGuard.IsValidAssetsPath(assetPath, "assetPath", out JObject pathError))
            {
                return pathError;
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

            if (string.IsNullOrEmpty(newName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'newName' must be provided",
                    "validation_error"
                );
            }

            if (!AssetPathGuard.IsValidAssetsPath(assetPath, "assetPath", out JObject pathError))
            {
                return pathError;
            }

            if (newName.Contains("/") || newName.Contains("\\") || newName.Contains(".."))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'newName' must be a bare name, not a path. Use the asset's existing folder or move it separately.",
                    "validation_error"
                );
            }

            try
            {
                // Folders have no extension even when their name contains a dot (e.g. "v1.2").
                string extension = AssetPathGuard.GetAssetExtension(assetPath);
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

            if (string.IsNullOrEmpty(newFolderName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'newFolderName' must be provided",
                    "validation_error"
                );
            }

            if (!AssetPathGuard.IsValidAssetsPath(parentFolder, "parentFolder", out JObject pathError))
            {
                return pathError;
            }

            if (newFolderName.Contains("/") || newFolderName.Contains("\\") || newFolderName.Contains(".."))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'newFolderName' must be a bare folder name, not a path. Create nested folders one level at a time.",
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
