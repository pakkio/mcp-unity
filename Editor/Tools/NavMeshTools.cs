using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for managing Unity AI Navigation and NavMesh (baking, agents, obstacles, pathfinding queries).
    /// </summary>
    public class ManageNavMeshTool : McpToolBase
    {
        public ManageNavMeshTool()
        {
            Name = "manage_navmesh";
            Description = "Manages Unity AI Navigation: bakes/clears NavMesh, configures NavMeshAgents and NavMeshObstacles, and queries pathfinding routes.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "get_status";
            string reason = parameters["reason"]?.ToObject<string>();

            switch (action)
            {
                case "get_status":
                    return HandleGetStatus();

                case "bake":
                    return HandleBake(parameters, reason);

                case "clear":
                    return HandleClear(reason);

                case "add_agent":
                    return HandleAddAgent(parameters, reason);

                case "add_obstacle":
                    return HandleAddObstacle(parameters, reason);

                case "calculate_path":
                    return HandleCalculatePath(parameters);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'get_status', 'bake', 'clear', 'add_agent', 'add_obstacle', 'calculate_path'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleGetStatus()
        {
            // Count agents and obstacles in the scene
            NavMeshAgent[] agents = UnityEngine.Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None);
            NavMeshObstacle[] obstacles = UnityEngine.Object.FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None);

            // Test if NavMesh exists by sampling at origin or any object
            NavMeshHit hit;
            bool hasNavMesh = NavMesh.SamplePosition(Vector3.zero, out hit, 1000f, NavMesh.AllAreas);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"NavMesh Status: hasNavMesh={hasNavMesh}, activeAgents={agents.Length}, activeObstacles={obstacles.Length}",
                ["hasNavMesh"] = hasNavMesh,
                ["agentCount"] = agents.Length,
                ["obstacleCount"] = obstacles.Length
            };
        }

        private JObject HandleBake(JObject parameters, string reason)
        {
            try
            {
                // First check for modern Unity.AI.Navigation.NavMeshSurface in scene
                Type navMeshSurfaceType = FindNavMeshSurfaceType();
                if (navMeshSurfaceType != null)
                {
                    UnityEngine.Object[] surfaces = UnityEngine.Object.FindObjectsByType(navMeshSurfaceType, FindObjectsSortMode.None);
                    if (surfaces != null && surfaces.Length > 0)
                    {
                        MethodInfo buildMethod = navMeshSurfaceType.GetMethod("BuildNavMesh", BindingFlags.Public | BindingFlags.Instance);
                        if (buildMethod != null)
                        {
                            foreach (var surface in surfaces)
                            {
                                buildMethod.Invoke(surface, null);
                            }

                            McpLogger.LogInfo($"[MCP Unity] Baked {surfaces.Length} NavMeshSurface components" + (reason != null ? $" — {reason}" : ""));
                            return new JObject
                            {
                                ["success"] = true,
                                ["type"] = "text",
                                ["message"] = $"Successfully baked {surfaces.Length} NavMeshSurface instances in the scene.",
                                ["surfacesBaked"] = surfaces.Length
                            };
                        }
                    }
                }

                // Fallback to classic UnityEditor.AI.NavMeshBuilder or menu item
                Type builderType = Type.GetType("UnityEditor.AI.NavMeshBuilder, UnityEditor");
                if (builderType != null)
                {
                    MethodInfo buildMethod = builderType.GetMethod("BuildNavMesh", BindingFlags.Public | BindingFlags.Static);
                    if (buildMethod != null)
                    {
                        buildMethod.Invoke(null, null);
                        McpLogger.LogInfo($"[MCP Unity] Baked classic scene NavMesh via NavMeshBuilder" + (reason != null ? $" — {reason}" : ""));

                        return new JObject
                        {
                            ["success"] = true,
                            ["type"] = "text",
                            ["message"] = "Successfully baked scene NavMesh using NavMeshBuilder."
                        };
                    }
                }

                // Final fallback: execute menu item
                EditorApplication.ExecuteMenuItem("Window/AI/Navigation");
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = "Triggered NavMesh bake. If using Unity 2022+ with com.unity.ai.navigation, add a NavMeshSurface component to your ground geometry."
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Failed to bake NavMesh: {ex.Message}", "navmesh_bake_error");
            }
        }

        private JObject HandleClear(string reason)
        {
            try
            {
                Type builderType = Type.GetType("UnityEditor.AI.NavMeshBuilder, UnityEditor");
                if (builderType != null)
                {
                    MethodInfo clearMethod = builderType.GetMethod("ClearAllNavMeshes", BindingFlags.Public | BindingFlags.Static);
                    if (clearMethod != null)
                    {
                        clearMethod.Invoke(null, null);
                    }
                }

                McpLogger.LogInfo($"[MCP Unity] Cleared all scene NavMesh data" + (reason != null ? $" — {reason}" : ""));

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = "Successfully cleared all NavMesh data from scene."
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Failed to clear NavMesh: {ex.Message}", "navmesh_clear_error");
            }
        }

        private JObject HandleAddAgent(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();

            float speed = parameters["speed"]?.ToObject<float?>() ?? 3.5f;
            float angularSpeed = parameters["angularSpeed"]?.ToObject<float?>() ?? 120f;
            float acceleration = parameters["acceleration"]?.ToObject<float?>() ?? 8f;
            float stoppingDistance = parameters["stoppingDistance"]?.ToObject<float?>() ?? 0.5f;
            float radius = parameters["radius"]?.ToObject<float?>() ?? 0.5f;
            float height = parameters["height"]?.ToObject<float?>() ?? 2.0f;
            bool autoBraking = parameters["autoBraking"]?.ToObject<bool?>() ?? true;

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            NavMeshAgent agent = targetObject.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = Undo.AddComponent<NavMeshAgent>(targetObject);
            }
            else
            {
                Undo.RecordObject(agent, "Configure NavMeshAgent");
            }

            agent.speed = speed;
            agent.angularSpeed = angularSpeed;
            agent.acceleration = acceleration;
            agent.stoppingDistance = stoppingDistance;
            agent.radius = radius;
            agent.height = height;
            agent.autoBraking = autoBraking;

            EditorUtility.SetDirty(targetObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured NavMeshAgent on '{targetObject.name}' (speed={speed}, stoppingDistance={stoppingDistance})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured NavMeshAgent on '{targetObject.name}'",
                ["instanceId"] = UnityObjectId.GetObjectId(targetObject),
                ["gameObjectName"] = targetObject.name,
                ["speed"] = agent.speed,
                ["stoppingDistance"] = agent.stoppingDistance,
                ["radius"] = agent.radius,
                ["height"] = agent.height
            };
        }

        private JObject HandleAddObstacle(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            bool carving = parameters["carving"]?.ToObject<bool?>() ?? true;
            string shapeStr = parameters["shape"]?.ToObject<string>()?.ToLowerInvariant() ?? "box";

            JObject sizeObj = parameters["size"] as JObject;
            JObject centerObj = parameters["center"] as JObject;

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            NavMeshObstacle obstacle = targetObject.GetComponent<NavMeshObstacle>();
            if (obstacle == null)
            {
                obstacle = Undo.AddComponent<NavMeshObstacle>(targetObject);
            }
            else
            {
                Undo.RecordObject(obstacle, "Configure NavMeshObstacle");
            }

            obstacle.carving = carving;
            obstacle.shape = shapeStr == "capsule" ? NavMeshObstacleShape.Capsule : NavMeshObstacleShape.Box;

            if (sizeObj != null)
            {
                obstacle.size = new Vector3(
                    sizeObj["x"]?.ToObject<float>() ?? obstacle.size.x,
                    sizeObj["y"]?.ToObject<float>() ?? obstacle.size.y,
                    sizeObj["z"]?.ToObject<float>() ?? obstacle.size.z
                );
            }

            if (centerObj != null)
            {
                obstacle.center = new Vector3(
                    centerObj["x"]?.ToObject<float>() ?? obstacle.center.x,
                    centerObj["y"]?.ToObject<float>() ?? obstacle.center.y,
                    centerObj["z"]?.ToObject<float>() ?? obstacle.center.z
                );
            }

            EditorUtility.SetDirty(targetObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured NavMeshObstacle on '{targetObject.name}' (carving={carving}, shape={obstacle.shape})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured NavMeshObstacle on '{targetObject.name}'",
                ["instanceId"] = UnityObjectId.GetObjectId(targetObject),
                ["gameObjectName"] = targetObject.name,
                ["carving"] = obstacle.carving,
                ["shape"] = obstacle.shape.ToString(),
                ["size"] = new JObject { ["x"] = obstacle.size.x, ["y"] = obstacle.size.y, ["z"] = obstacle.size.z }
            };
        }

        private JObject HandleCalculatePath(JObject parameters)
        {
            JObject startObj = parameters["startPosition"] as JObject;
            JObject targetObj = parameters["targetPosition"] as JObject;

            if (startObj == null || targetObj == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse("Parameters 'startPosition' and 'targetPosition' ({ x, y, z }) are required.", "validation_error");
            }

            Vector3 startPos = new Vector3(
                startObj["x"]?.ToObject<float>() ?? 0f,
                startObj["y"]?.ToObject<float>() ?? 0f,
                startObj["z"]?.ToObject<float>() ?? 0f
            );

            Vector3 targetPos = new Vector3(
                targetObj["x"]?.ToObject<float>() ?? 0f,
                targetObj["y"]?.ToObject<float>() ?? 0f,
                targetObj["z"]?.ToObject<float>() ?? 0f
            );

            NavMeshPath path = new NavMeshPath();
            bool pathCalculated = NavMesh.CalculatePath(startPos, targetPos, NavMesh.AllAreas, path);

            JArray waypoints = new JArray();
            float totalDistance = 0f;

            if (path.corners != null && path.corners.Length > 0)
            {
                for (int i = 0; i < path.corners.Length; i++)
                {
                    Vector3 corner = path.corners[i];
                    waypoints.Add(new JObject
                    {
                        ["index"] = i,
                        ["x"] = corner.x,
                        ["y"] = corner.y,
                        ["z"] = corner.z
                    });

                    if (i > 0)
                    {
                        totalDistance += Vector3.Distance(path.corners[i - 1], corner);
                    }
                }
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Path calculated: status={path.status}, waypointCount={waypoints.Count}, totalDistance={totalDistance:F2}m",
                ["status"] = path.status.ToString(),
                ["isComplete"] = path.status == NavMeshPathStatus.PathComplete,
                ["waypointCount"] = waypoints.Count,
                ["totalDistance"] = totalDistance,
                ["waypoints"] = waypoints
            };
        }

        private Type FindNavMeshSurfaceType()
        {
            Type t = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
            if (t != null) return t;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = assembly.GetType("Unity.AI.Navigation.NavMeshSurface");
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
