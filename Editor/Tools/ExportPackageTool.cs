using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for exporting assets or scenes into a .unitypackage file
    /// </summary>
    public class ExportPackageTool : McpToolBase
    {
        public ExportPackageTool()
        {
            Name = "export_package";
            Description = "Exports assets or scenes to a .unitypackage file with optional dependencies";
        }

        /// <summary>
        /// Execute the ExportPackage tool with the provided parameters
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            string assetPath = parameters["assetPath"]?.ToObject<string>();
            JArray assetPathsArray = parameters["assetPaths"] as JArray;
            string exportPath = parameters["exportPath"]?.ToObject<string>();
            bool includeDependencies = parameters["includeDependencies"]?.ToObject<bool?>() ?? true;
            bool recurse = parameters["recurse"]?.ToObject<bool?>() ?? true;

            try
            {
                List<string> pathsToExport = new List<string>();

                if (!string.IsNullOrEmpty(assetPath))
                {
                    pathsToExport.Add(assetPath);
                }

                if (assetPathsArray != null)
                {
                    foreach (var item in assetPathsArray)
                    {
                        string p = item?.ToObject<string>();
                        if (!string.IsNullOrEmpty(p) && !pathsToExport.Contains(p))
                        {
                            pathsToExport.Add(p);
                        }
                    }
                }

                if (pathsToExport.Count == 0)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Parameter 'assetPath' or 'assetPaths' must be provided",
                        "validation_error"
                    );
                }

                if (string.IsNullOrEmpty(exportPath))
                {
                    // Default export name based on first asset name
                    string firstAsset = Path.GetFileNameWithoutExtension(pathsToExport[0]);
                    exportPath = $"{firstAsset}.unitypackage";
                }

                if (!exportPath.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                {
                    exportPath += ".unitypackage";
                }

                // Ensure parent directory exists if exportPath specifies a directory
                string parentDir = Path.GetDirectoryName(exportPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                ExportPackageOptions options = ExportPackageOptions.Default;
                if (includeDependencies) options |= ExportPackageOptions.IncludeDependencies;
                if (recurse) options |= ExportPackageOptions.Recurse;

                if (pathsToExport.Count == 1)
                {
                    AssetDatabase.ExportPackage(pathsToExport[0], exportPath, options);
                }
                else
                {
                    AssetDatabase.ExportPackage(pathsToExport.ToArray(), exportPath, options);
                }

                McpLogger.LogInfo($"[MCP Unity] Exported package to '{exportPath}' ({pathsToExport.Count} asset(s))");

                string fullPath = Path.GetFullPath(exportPath);

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully exported package to '{exportPath}'",
                    ["exportPath"] = exportPath,
                    ["fullPath"] = fullPath,
                    ["assetCount"] = pathsToExport.Count
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error exporting package: {ex.Message}",
                    "export_package_error"
                );
            }
        }
    }
}
