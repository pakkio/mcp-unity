using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Services;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for building the Unity project for a specific target platform
    /// </summary>
    public class BuildProjectTool : McpToolBase
    {
        public BuildProjectTool()
        {
            Name = "build_project";
            Description = "Compiles and builds the Unity project for a target platform";
        }

        public override JObject Execute(JObject parameters)
        {
            string targetStr = parameters["target"]?.ToObject<string>();
            string outputPath = parameters["outputPath"]?.ToObject<string>();
            JArray optionsArray = parameters["options"] as JArray;

            if (string.IsNullOrEmpty(targetStr) || string.IsNullOrEmpty(outputPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameters 'target' and 'outputPath' must be provided",
                    "validation_error"
                );
            }

            if (!Enum.TryParse(targetStr, true, out BuildTarget buildTarget))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Invalid build target '{targetStr}'. Supported examples: StandaloneWindows64, WebGL, Android, iOS, StandaloneOSX",
                    "invalid_target"
                );
            }

            try
            {
                // Retrieve scenes active in Build Settings
                List<string> scenesList = new List<string>();
                foreach (var scene in EditorBuildSettings.scenes)
                {
                    if (scene.enabled)
                    {
                        scenesList.Add(scene.path);
                    }
                }

                if (scenesList.Count == 0)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "No enabled scenes found in Unity Build Settings. Cannot build project.",
                        "no_scenes"
                    );
                }

                BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
                {
                    scenes = scenesList.ToArray(),
                    locationPathName = outputPath,
                    target = buildTarget
                };

                // Parse build options
                BuildOptions buildOptions = BuildOptions.None;
                if (optionsArray != null)
                {
                    foreach (var optionItem in optionsArray)
                    {
                        string optStr = optionItem.ToString();
                        if (Enum.TryParse(optStr, true, out BuildOptions parsedOption))
                        {
                            buildOptions |= parsedOption;
                        }
                    }
                }
                buildPlayerOptions.options = buildOptions;

                McpLogger.LogInfo($"[MCP Unity] Starting build for {buildTarget} at '{outputPath}'...");
                var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
                var summary = report.summary;

                if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    McpLogger.LogInfo($"[MCP Unity] Build succeeded for {buildTarget}!");
                    return new JObject
                    {
                        ["success"] = true,
                        ["message"] = $"Successfully built project for {buildTarget} at '{outputPath}'",
                        ["totalErrors"] = summary.totalErrors,
                        ["totalWarnings"] = summary.totalWarnings,
                        ["totalSize"] = summary.totalSize,
                        ["totalTimeSeconds"] = summary.totalTime.TotalSeconds
                    };
                }
                else
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Build failed with {summary.totalErrors} errors. Check Unity editor log.",
                        "build_failed"
                    );
                }
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error building project: {ex.Message}",
                    "build_error"
                );
            }
        }
    }

    /// <summary>
    /// Tool to retrieve compilation errors and warnings from the project
    /// </summary>
    public class GetCompilationErrorsTool : McpToolBase
    {
        private readonly IConsoleLogsService _consoleLogsService;

        public GetCompilationErrorsTool(IConsoleLogsService consoleLogsService)
        {
            _consoleLogsService = consoleLogsService;
            Name = "get_compilation_errors";
            Description = "Retrieves active C# script compilation errors and warnings in the project";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                bool compilationFailed = EditorUtility.scriptCompilationFailed;
                JObject logsObj = _consoleLogsService.GetLogsAsJson("error", 0, 100, false);
                JArray logs = logsObj["logs"] as JArray;
                JArray compileErrors = new JArray();

                if (logs != null)
                {
                    foreach (var log in logs)
                    {
                        string msg = log["message"]?.ToString();
                        // Compilation errors usually contain compiler codes (e.g. "CS0103") or script file path parenthesis reference
                        if (!string.IsNullOrEmpty(msg) && (msg.Contains("error CS") || msg.Contains(".cs(")))
                        {
                            compileErrors.Add(log);
                        }
                    }
                }

                return new JObject
                {
                    ["success"] = true,
                    ["compilationFailed"] = compilationFailed,
                    ["errors"] = compileErrors
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error getting compilation errors: {ex.Message}",
                    "compilation_query_error"
                );
            }
        }
    }

    /// <summary>
    /// Tool to locate all GameObjects referencing a specific script class name
    /// </summary>
    public class FindScriptReferencesTool : McpToolBase
    {
        public FindScriptReferencesTool()
        {
            Name = "find_script_references";
            Description = "Finds all GameObjects in the active scene referencing a specified C# script class";
        }

        public override JObject Execute(JObject parameters)
        {
            string scriptName = parameters["scriptName"]?.ToObject<string>();

            if (string.IsNullOrEmpty(scriptName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'scriptName' must be provided",
                    "validation_error"
                );
            }

            try
            {
                Type scriptType = null;
                // Find type inside loaded domain assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    scriptType = assembly.GetType(scriptName);
                    if (scriptType == null)
                    {
                        // Check if it exists under UnityEngine namespace or namespace-less search
                        scriptType = assembly.GetType("UnityEngine." + scriptName);
                    }

                    if (scriptType == null)
                    {
                        // Match case-insensitively across assembly types
                        foreach (var t in GetLoadableTypes(assembly))
                        {
                            if (t.Name.Equals(scriptName, StringComparison.OrdinalIgnoreCase))
                            {
                                scriptType = t;
                                break;
                            }
                        }
                    }

                    if (scriptType != null) break;
                }

                if (scriptType == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Script type '{scriptName}' not found in any loaded assembly.",
                        "script_not_found"
                    );
                }

                JArray references = new JArray();
#if UNITY_2023_1_OR_NEWER
                var components = UnityEngine.Object.FindObjectsByType(scriptType, FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
                var components = UnityEngine.Object.FindObjectsOfType(scriptType, true);
#endif

                foreach (var comp in components)
                {
                    MonoBehaviour mb = comp as MonoBehaviour;
                    if (mb != null && mb.gameObject != null)
                    {
                        references.Add(new JObject
                        {
                            ["gameObjectName"] = mb.gameObject.name,
                            ["gameObjectPath"] = GetGameObjectPath(mb.gameObject),
                            // Public ID scheme shared with every other tool - a raw GetInstanceID()
                            // is not resolvable by GameObjectResolver under MCP_UNITY_ENTITY_ID_API.
                            ["instanceId"] = UnityObjectId.GetObjectId(mb.gameObject),
                            ["enabled"] = mb.enabled
                        });
                    }
                }

                return new JObject
                {
                    ["success"] = true,
                    ["scriptName"] = scriptName,
                    ["referenceCount"] = references.Count,
                    ["references"] = references
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error finding script references: {ex.Message}",
                    "find_references_error"
                );
            }
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = obj.name + "/" + path;
            }
            return path;
        }

        /// <summary>
        /// Assembly.GetTypes() throws ReflectionTypeLoadException whenever any type in the
        /// assembly references something that can't be resolved - routine in a Unity domain with
        /// optional or platform-conditional packages. The exception still carries the types that
        /// did load, so use those rather than letting one bad assembly fail the whole search.
        /// </summary>
        private static IEnumerable<Type> GetLoadableTypes(System.Reflection.Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
            catch (Exception)
            {
                // Some assemblies (e.g. reflection-only or dynamic) can fail in other ways.
                return Array.Empty<Type>();
            }
        }
    }
}
