using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for creating and configuring ChilloutVR CCK World components (CVRWorld, CVRSpawnPoint, CVRMirror, CVRSeat, CVRPortal).
    /// </summary>
    public class ManageCvrWorldTool : McpToolBase
    {
        public ManageCvrWorldTool()
        {
            Name = "manage_cvr_world";
            Description = "Manages ChilloutVR CCK World components: CVRWorld root settings, spawn points, optimized mirrors, seats/chairs, and portals.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "setup_world";
            string reason = parameters["reason"]?.ToObject<string>();

            switch (action)
            {
                case "setup_world":
                    return HandleSetupWorld(parameters, reason);

                case "add_spawn_point":
                    return HandleAddSpawnPoint(parameters, reason);

                case "create_mirror":
                    return HandleCreateMirror(parameters, reason);

                case "create_seat":
                    return HandleCreateSeat(parameters, reason);

                case "create_portal":
                    return HandleCreatePortal(parameters, reason);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'setup_world', 'add_spawn_point', 'create_mirror', 'create_seat', 'create_portal'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleSetupWorld(JObject parameters, string reason)
        {
            string objectName = parameters["objectName"]?.ToObject<string>() ?? "CVRWorld";
            float respawnHeight = parameters["respawnHeight"]?.ToObject<float?>() ?? -50f;
            float runSpeed = parameters["runSpeed"]?.ToObject<float?>() ?? 4.0f;
            float sprintMultiplier = parameters["sprintMultiplier"]?.ToObject<float?>() ?? 2.0f;
            float jumpHeight = parameters["jumpHeight"]?.ToObject<float?>() ?? 1.5f;
            bool allowFlight = parameters["allowFlight"]?.ToObject<bool?>() ?? true;
            bool allowTeleport = parameters["allowTeleport"]?.ToObject<bool?>() ?? true;

            JObject gravityObj = parameters["gravity"] as JObject;
            Vector3 gravity = Physics.gravity;
            if (gravityObj != null)
            {
                gravity = new Vector3(
                    gravityObj["x"]?.ToObject<float>() ?? 0f,
                    gravityObj["y"]?.ToObject<float>() ?? -9.81f,
                    gravityObj["z"]?.ToObject<float>() ?? 0f
                );
                Physics.gravity = gravity;
            }

            GameObject worldGo = GameObject.Find(objectName);
            if (worldGo == null)
            {
                worldGo = new GameObject(objectName);
                Undo.RegisterCreatedObjectUndo(worldGo, "Create CVRWorld");
            }

            Type cvrWorldType = FindCckType("CVRWorld");
            Component cvrWorldComp = null;

            if (cvrWorldType != null)
            {
                cvrWorldComp = worldGo.GetComponent(cvrWorldType) ?? Undo.AddComponent(worldGo, cvrWorldType);
                SetComponentField(cvrWorldComp, "respawnHeight", respawnHeight);
                SetComponentField(cvrWorldComp, "runSpeed", runSpeed);
                SetComponentField(cvrWorldComp, "sprintMultiplier", sprintMultiplier);
                SetComponentField(cvrWorldComp, "jumpHeight", jumpHeight);
                SetComponentField(cvrWorldComp, "allowFlight", allowFlight);
                SetComponentField(cvrWorldComp, "allowTeleport", allowTeleport);
            }

            // Ensure at least one spawn point exists
            GameObject defaultSpawn = GameObject.Find("SpawnPoint");
            if (defaultSpawn == null)
            {
                defaultSpawn = new GameObject("SpawnPoint");
                defaultSpawn.transform.position = new Vector3(0, 0, 0);
                defaultSpawn.transform.SetParent(worldGo.transform);
                Undo.RegisterCreatedObjectUndo(defaultSpawn, "Create Default SpawnPoint");

                Type cvrSpawnType = FindCckType("CVRSpawnPoint");
                if (cvrSpawnType != null)
                {
                    Undo.AddComponent(defaultSpawn, cvrSpawnType);
                }
            }

            EditorUtility.SetDirty(worldGo);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured CVRWorld on '{worldGo.name}' (respawnHeight={respawnHeight}, allowFlight={allowFlight})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured ChilloutVR CVRWorld on '{worldGo.name}'" + (cvrWorldType == null ? " (Note: CCK package not imported, created standard placeholder structure)" : ""),
                ["worldName"] = worldGo.name,
                ["instanceId"] = UnityObjectId.GetObjectId(worldGo),
                ["cckInstalled"] = cvrWorldType != null,
                ["respawnHeight"] = respawnHeight,
                ["runSpeed"] = runSpeed,
                ["jumpHeight"] = jumpHeight,
                ["allowFlight"] = allowFlight,
                ["allowTeleport"] = allowTeleport,
                ["gravity"] = new JObject { ["x"] = gravity.x, ["y"] = gravity.y, ["z"] = gravity.z }
            };
        }

        private JObject HandleAddSpawnPoint(JObject parameters, string reason)
        {
            string spawnName = parameters["spawnName"]?.ToObject<string>() ?? "CVRSpawnPoint";
            JObject posObj = parameters["position"] as JObject;
            JObject rotObj = parameters["rotation"] as JObject;

            Vector3 pos = Vector3.zero;
            if (posObj != null)
            {
                pos = new Vector3(
                    posObj["x"]?.ToObject<float>() ?? 0f,
                    posObj["y"]?.ToObject<float>() ?? 0f,
                    posObj["z"]?.ToObject<float>() ?? 0f
                );
            }

            Quaternion rot = Quaternion.identity;
            if (rotObj != null)
            {
                rot = Quaternion.Euler(
                    rotObj["x"]?.ToObject<float>() ?? 0f,
                    rotObj["y"]?.ToObject<float>() ?? 0f,
                    rotObj["z"]?.ToObject<float>() ?? 0f
                );
            }

            GameObject spawnGo = new GameObject(spawnName);
            spawnGo.transform.position = pos;
            spawnGo.transform.rotation = rot;
            Undo.RegisterCreatedObjectUndo(spawnGo, "Create Spawn Point");

            Type cvrSpawnType = FindCckType("CVRSpawnPoint");
            if (cvrSpawnType != null)
            {
                Undo.AddComponent(spawnGo, cvrSpawnType);
            }

            // Link to CVRWorld if present
            Type cvrWorldType = FindCckType("CVRWorld");
            if (cvrWorldType != null)
            {
                UnityEngine.Object cvrWorld = UnityEngine.Object.FindFirstObjectByType(cvrWorldType);
                if (cvrWorld != null)
                {
                    FieldInfo spawnsField = cvrWorldType.GetField("spawns");
                    if (spawnsField != null)
                    {
                        IList list = spawnsField.GetValue(cvrWorld) as IList;
                        if (list != null)
                        {
                            Component spawnComp = spawnGo.GetComponent(cvrSpawnType);
                            if (spawnComp != null && !list.Contains(spawnComp))
                            {
                                list.Add(spawnComp);
                            }
                        }
                    }
                }
            }

            EditorUtility.SetDirty(spawnGo);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Created CVRSpawnPoint '{spawnName}' at {pos}" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created CVRSpawnPoint '{spawnName}'",
                ["spawnName"] = spawnName,
                ["instanceId"] = UnityObjectId.GetObjectId(spawnGo),
                ["position"] = new JObject { ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z },
                ["rotation"] = new JObject { ["x"] = rot.eulerAngles.x, ["y"] = rot.eulerAngles.y, ["z"] = rot.eulerAngles.z }
            };
        }

        private JObject HandleCreateMirror(JObject parameters, string reason)
        {
            string mirrorName = parameters["mirrorName"]?.ToObject<string>() ?? "CVRMirror";
            JObject posObj = parameters["position"] as JObject;
            JObject sizeObj = parameters["size"] as JObject;
            string mirrorTypeStr = parameters["mirrorType"]?.ToObject<string>()?.ToLowerInvariant() ?? "optimized";

            Vector3 pos = new Vector3(0, 1.5f, 2f);
            if (posObj != null)
            {
                pos = new Vector3(
                    posObj["x"]?.ToObject<float>() ?? 0f,
                    posObj["y"]?.ToObject<float>() ?? 1.5f,
                    posObj["z"]?.ToObject<float>() ?? 2f
                );
            }

            Vector2 size = new Vector2(3f, 2f);
            if (sizeObj != null)
            {
                size = new Vector2(
                    sizeObj["x"]?.ToObject<float>() ?? 3f,
                    sizeObj["y"]?.ToObject<float>() ?? 2f
                );
            }

            GameObject mirrorQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mirrorQuad.name = mirrorName;
            mirrorQuad.transform.position = pos;
            mirrorQuad.transform.localScale = new Vector3(size.x, size.y, 1f);
            mirrorQuad.transform.rotation = Quaternion.Euler(0, 180f, 0); // Face viewer
            Undo.RegisterCreatedObjectUndo(mirrorQuad, "Create CVR Mirror");

            Type cvrMirrorType = FindCckType("CVRMirror");
            if (cvrMirrorType != null)
            {
                Component mirrorComp = Undo.AddComponent(mirrorQuad, cvrMirrorType);
                // Attempt to set mirror reflection quality/type
                SetComponentField(mirrorComp, "mirrorType", mirrorTypeStr);
            }

            EditorUtility.SetDirty(mirrorQuad);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Created CVRMirror '{mirrorName}' with size {size}" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created CVRMirror '{mirrorName}'",
                ["mirrorName"] = mirrorName,
                ["instanceId"] = UnityObjectId.GetObjectId(mirrorQuad),
                ["position"] = new JObject { ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z },
                ["size"] = new JObject { ["width"] = size.x, ["height"] = size.y },
                ["mirrorType"] = mirrorTypeStr
            };
        }

        private JObject HandleCreateSeat(JObject parameters, string reason)
        {
            string seatName = parameters["seatName"]?.ToObject<string>() ?? "CVRSeat";
            int? targetInstanceId = parameters["instanceId"]?.ToObject<int?>();
            string targetPath = parameters["objectPath"]?.ToObject<string>();

            GameObject seatGo = null;
            if (targetInstanceId.HasValue || !string.IsNullOrEmpty(targetPath))
            {
                JObject findErr = GameObjectToolUtils.FindGameObject(targetInstanceId, targetPath, out seatGo, out string info);
                if (findErr != null) return findErr;
            }
            else
            {
                seatGo = new GameObject(seatName);
                seatGo.transform.position = Vector3.zero;
                Undo.RegisterCreatedObjectUndo(seatGo, "Create CVR Seat");
            }

            Type cvrSeatType = FindCckType("CVRSeat") ?? FindCckType("CVRChair");
            if (cvrSeatType != null)
            {
                Component seatComp = seatGo.GetComponent(cvrSeatType) ?? Undo.AddComponent(seatGo, cvrSeatType);
            }

            EditorUtility.SetDirty(seatGo);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured CVRSeat on '{seatGo.name}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured CVRSeat on '{seatGo.name}'",
                ["seatName"] = seatGo.name,
                ["instanceId"] = UnityObjectId.GetObjectId(seatGo)
            };
        }

        private JObject HandleCreatePortal(JObject parameters, string reason)
        {
            string portalName = parameters["portalName"]?.ToObject<string>() ?? "CVRPortal";
            string targetWorldId = parameters["targetWorldId"]?.ToObject<string>() ?? "wrld_example";
            string targetInstanceId = parameters["targetInstanceId"]?.ToObject<string>() ?? "";

            GameObject portalGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            portalGo.name = portalName;
            portalGo.transform.localScale = new Vector3(1.2f, 2.2f, 0.2f);
            Undo.RegisterCreatedObjectUndo(portalGo, "Create CVR Portal");

            Type cvrPortalType = FindCckType("CVRPortal");
            if (cvrPortalType != null)
            {
                Component portalComp = Undo.AddComponent(portalGo, cvrPortalType);
                SetComponentField(portalComp, "worldId", targetWorldId);
                SetComponentField(portalComp, "instanceId", targetInstanceId);
            }

            EditorUtility.SetDirty(portalGo);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Created CVRPortal '{portalName}' targeting world '{targetWorldId}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created CVRPortal '{portalName}' targeting '{targetWorldId}'",
                ["portalName"] = portalName,
                ["instanceId"] = UnityObjectId.GetObjectId(portalGo),
                ["targetWorldId"] = targetWorldId
            };
        }

        private static Type FindCckType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = assembly.GetType($"ABI.CCK.Components.{typeName}")
                          ?? assembly.GetType($"ABI.CCK.Scripts.{typeName}")
                          ?? assembly.GetType(typeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static void SetComponentField(Component comp, string fieldOrPropName, object value)
        {
            if (comp == null || value == null) return;
            Type t = comp.GetType();

            FieldInfo field = t.GetField(fieldOrPropName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                try { field.SetValue(comp, Convert.ChangeType(value, field.FieldType)); return; } catch { }
            }

            PropertyInfo prop = t.GetProperty(fieldOrPropName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                try { prop.SetValue(comp, Convert.ChangeType(value, prop.PropertyType)); return; } catch { }
            }
        }
    }
}
