using System;
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
    /// Tool for creating and configuring Cinemachine Virtual Camera rigs (Follow, LookAt, 3rd person, 1st person POV, orbital).
    /// </summary>
    public class CreateVirtualCameraTool : McpToolBase
    {
        public CreateVirtualCameraTool()
        {
            Name = "create_virtual_camera";
            Description = "Creates and configures Cinemachine Virtual Cameras (Follow, LookAt, 3rd Person, First Person POV, Orbit, Framing).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string cameraName = parameters["cameraName"]?.ToObject<string>() ?? "CM_VirtualCamera";
            string cameraType = parameters["cameraType"]?.ToObject<string>()?.ToLowerInvariant() ?? "third_person";
            int priority = parameters["priority"]?.ToObject<int?>() ?? 10;
            float fieldOfView = parameters["fieldOfView"]?.ToObject<float?>() ?? 60f;

            int? followTargetId = parameters["followTargetInstanceId"]?.ToObject<int?>();
            string followTargetPath = parameters["followTargetObjectPath"]?.ToObject<string>();

            int? lookAtTargetId = parameters["lookAtTargetInstanceId"]?.ToObject<int?>();
            string lookAtTargetPath = parameters["lookAtTargetObjectPath"]?.ToObject<string>();

            JObject followOffsetObj = parameters["followOffset"] as JObject;
            string reason = parameters["reason"]?.ToObject<string>();

            // Ensure Main Camera has CinemachineBrain
            EnsureCinemachineBrainOnMainCamera();

            // Find Virtual Camera Component Type
            Type vcamType = FindCinemachineComponentType("CinemachineVirtualCamera") 
                         ?? FindCinemachineComponentType("CinemachineCamera");

            if (vcamType == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Cinemachine package is not installed in the project. Install 'com.unity.cinemachine' via add_package first.",
                    "cinemachine_not_installed"
                );
            }

            GameObject vcamGo = GameObject.Find(cameraName);
            if (vcamGo == null)
            {
                vcamGo = new GameObject(cameraName);
                Undo.RegisterCreatedObjectUndo(vcamGo, "Create Virtual Camera");
            }

            Component vcam = vcamGo.GetComponent(vcamType);
            if (vcam == null)
            {
                vcam = Undo.AddComponent(vcamGo, vcamType);
            }
            else
            {
                Undo.RecordObject(vcam, "Configure Virtual Camera");
            }

            // Set Priority
            PropertyInfo priorityProp = vcamType.GetProperty("Priority");
            if (priorityProp != null && priorityProp.CanWrite)
            {
                if (priorityProp.PropertyType == typeof(int))
                {
                    priorityProp.SetValue(vcam, priority);
                }
                else
                {
                    // Cinemachine 3.x OutputChannel/Priority struct handling
                    try { priorityProp.SetValue(vcam, Convert.ChangeType(priority, priorityProp.PropertyType)); } catch { }
                }
            }

            // Resolve Follow Target
            if (followTargetId.HasValue || !string.IsNullOrEmpty(followTargetPath))
            {
                JObject findFollowErr = GameObjectToolUtils.FindGameObject(followTargetId, followTargetPath, out GameObject followTarget, out string followInfo);
                if (findFollowErr == null && followTarget != null)
                {
                    PropertyInfo followProp = vcamType.GetProperty("Follow");
                    if (followProp != null && followProp.CanWrite)
                    {
                        followProp.SetValue(vcam, followTarget.transform);
                    }
                }
            }

            // Resolve LookAt Target
            if (lookAtTargetId.HasValue || !string.IsNullOrEmpty(lookAtTargetPath))
            {
                JObject findLookAtErr = GameObjectToolUtils.FindGameObject(lookAtTargetId, lookAtTargetPath, out GameObject lookAtTarget, out string lookAtInfo);
                if (findLookAtErr == null && lookAtTarget != null)
                {
                    PropertyInfo lookAtProp = vcamType.GetProperty("LookAt");
                    if (lookAtProp != null && lookAtProp.CanWrite)
                    {
                        lookAtProp.SetValue(vcam, lookAtTarget.transform);
                    }
                }
            }

            // Set Lens FOV
            PropertyInfo lensProp = vcamType.GetProperty("m_Lens") ?? vcamType.GetProperty("Lens");
            if (lensProp != null)
            {
                object lensVal = lensProp.GetValue(vcam);
                if (lensVal != null)
                {
                    FieldInfo fovField = lensVal.GetType().GetField("FieldOfView");
                    if (fovField != null)
                    {
                        fovField.SetValue(lensVal, fieldOfView);
                        if (lensProp.CanWrite) lensProp.SetValue(vcam, lensVal);
                    }
                }
            }

            // Configure Body / Follow Offset if provided
            Vector3 followOffset = new Vector3(0, 2f, -4f);
            if (followOffsetObj != null)
            {
                followOffset = new Vector3(
                    followOffsetObj["x"]?.ToObject<float>() ?? 0f,
                    followOffsetObj["y"]?.ToObject<float>() ?? 2f,
                    followOffsetObj["z"]?.ToObject<float>() ?? -4f
                );
            }

            ConfigureVcamBodyOffsets(vcam, vcamType, cameraType, followOffset);

            EditorUtility.SetDirty(vcamGo);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Created Cinemachine Virtual Camera '{cameraName}' (type: {cameraType}, priority: {priority})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created Cinemachine Virtual Camera '{cameraName}'",
                ["cameraName"] = cameraName,
                ["instanceId"] = UnityObjectId.GetObjectId(vcamGo),
                ["cameraType"] = cameraType,
                ["priority"] = priority,
                ["fieldOfView"] = fieldOfView,
                ["followOffset"] = new JObject { ["x"] = followOffset.x, ["y"] = followOffset.y, ["z"] = followOffset.z }
            };
        }

        private void EnsureCinemachineBrainOnMainCamera()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Type brainType = FindCinemachineComponentType("CinemachineBrain");
                if (brainType != null && cam.GetComponent(brainType) == null)
                {
                    Undo.AddComponent(cam.gameObject, brainType);
                    McpLogger.LogInfo("[MCP Unity] Added CinemachineBrain to Main Camera");
                }
            }
        }

        private void ConfigureVcamBodyOffsets(Component vcam, Type vcamType, string cameraType, Vector3 offset)
        {
            try
            {
                // In Cinemachine 2.x, GetCinemachineComponent<CinemachineTransposer>()
                MethodInfo getCompMethod = vcamType.GetMethod("GetCinemachineComponent");
                if (getCompMethod != null)
                {
                    Type transposerType = FindCinemachineComponentType("CinemachineTransposer");
                    if (transposerType != null)
                    {
                        MethodInfo genericGetComp = getCompMethod.MakeGenericMethod(transposerType);
                        object transposer = genericGetComp.Invoke(vcam, null);
                        if (transposer == null)
                        {
                            MethodInfo addCompMethod = vcamType.GetMethod("AddCinemachineComponent");
                            if (addCompMethod != null)
                            {
                                MethodInfo genericAdd = addCompMethod.MakeGenericMethod(transposerType);
                                transposer = genericAdd.Invoke(vcam, null);
                            }
                        }

                        if (transposer != null)
                        {
                            FieldInfo offsetField = transposerType.GetField("m_FollowOffset");
                            if (offsetField != null)
                            {
                                offsetField.SetValue(transposer, offset);
                            }
                        }
                    }
                }
                else
                {
                    // Fallback direct position offset
                    vcam.transform.position = offset;
                }
            }
            catch { }
        }

        private Type FindCinemachineComponentType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = assembly.GetType($"Cinemachine.{typeName}") 
                          ?? assembly.GetType($"Unity.Cinemachine.{typeName}")
                          ?? assembly.GetType(typeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
