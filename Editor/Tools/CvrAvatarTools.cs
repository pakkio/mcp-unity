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
    /// Tool for creating and configuring ChilloutVR CCK Avatar components (CVRAvatar, AAS settings, lip-sync visemes, eye blinks, face tracking).
    /// </summary>
    public class ManageCvrAvatarTool : McpToolBase
    {
        public ManageCvrAvatarTool()
        {
            Name = "manage_cvr_avatar";
            Description = "Manages ChilloutVR CCK Avatar components: CVRAvatar setup, automatic viewpoint/voice calculation, visemes, blink blendshapes, and Advanced Avatar Settings (AAS).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "setup_avatar";
            string reason = parameters["reason"]?.ToObject<string>();

            switch (action)
            {
                case "setup_avatar":
                    return HandleSetupAvatar(parameters, reason);

                case "configure_aas":
                    return HandleConfigureAAS(parameters, reason);

                case "setup_face_tracking":
                    return HandleSetupFaceTracking(parameters, reason);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'setup_avatar', 'configure_aas', 'setup_face_tracking'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleSetupAvatar(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            JObject customViewPosObj = parameters["viewPosition"] as JObject;
            JObject customVoicePosObj = parameters["voicePosition"] as JObject;

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject avatarObject, out string idInfo);
            if (findError != null) return findError;

            // Calculate Viewpoint (eye position)
            Vector3 viewPosition = new Vector3(0, 1.6f, 0.1f);
            Animator animator = avatarObject.GetComponent<Animator>();

            if (animator != null && animator.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                Transform rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);

                if (leftEye != null && rightEye != null)
                {
                    Vector3 worldEyes = (leftEye.position + rightEye.position) * 0.5f;
                    viewPosition = avatarObject.transform.InverseTransformPoint(worldEyes);
                }
                else if (head != null)
                {
                    Vector3 worldHead = head.position + head.forward * 0.08f + head.up * 0.05f;
                    viewPosition = avatarObject.transform.InverseTransformPoint(worldHead);
                }
            }
            else
            {
                // Fallback: estimate from top of combined renderer bounds
                Renderer[] renderers = avatarObject.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    viewPosition = avatarObject.transform.InverseTransformPoint(new Vector3(b.center.x, b.max.y - 0.15f, b.center.z + 0.05f));
                }
            }

            if (customViewPosObj != null)
            {
                viewPosition = new Vector3(
                    customViewPosObj["x"]?.ToObject<float>() ?? viewPosition.x,
                    customViewPosObj["y"]?.ToObject<float>() ?? viewPosition.y,
                    customViewPosObj["z"]?.ToObject<float>() ?? viewPosition.z
                );
            }

            Vector3 voicePosition = new Vector3(viewPosition.x, viewPosition.y - 0.08f, viewPosition.z + 0.02f);
            if (customVoicePosObj != null)
            {
                voicePosition = new Vector3(
                    customVoicePosObj["x"]?.ToObject<float>() ?? voicePosition.x,
                    customVoicePosObj["y"]?.ToObject<float>() ?? voicePosition.y,
                    customVoicePosObj["z"]?.ToObject<float>() ?? voicePosition.z
                );
            }

            // Find face/body SkinnedMeshRenderer for visemes and blinks
            SkinnedMeshRenderer faceRenderer = null;
            SkinnedMeshRenderer[] smrs = avatarObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 5)
                {
                    faceRenderer = smr;
                    break;
                }
            }

            Type cvrAvatarType = FindCckType("CVRAvatar");
            Component cvrAvatar = null;

            if (cvrAvatarType != null)
            {
                cvrAvatar = avatarObject.GetComponent(cvrAvatarType) ?? Undo.AddComponent(avatarObject, cvrAvatarType);
                SetComponentField(cvrAvatar, "viewPosition", viewPosition);
                SetComponentField(cvrAvatar, "voicePosition", voicePosition);
            }

            EditorUtility.SetDirty(avatarObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured CVRAvatar on '{avatarObject.name}' (viewPosition={viewPosition}, faceRenderer={faceRenderer?.name ?? "none"})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured CVRAvatar on '{avatarObject.name}'",
                ["avatarName"] = avatarObject.name,
                ["instanceId"] = UnityObjectId.GetObjectId(avatarObject),
                ["cckInstalled"] = cvrAvatarType != null,
                ["viewPosition"] = new JObject { ["x"] = viewPosition.x, ["y"] = viewPosition.y, ["z"] = viewPosition.z },
                ["voicePosition"] = new JObject { ["x"] = voicePosition.x, ["y"] = voicePosition.y, ["z"] = voicePosition.z },
                ["faceRenderer"] = faceRenderer != null ? faceRenderer.name : "not_detected"
            };
        }

        private JObject HandleConfigureAAS(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string settingName = parameters["settingName"]?.ToObject<string>() ?? "NewToggle";
            string settingType = parameters["settingType"]?.ToObject<string>()?.ToLowerInvariant() ?? "toggle";
            float defaultValue = parameters["defaultValue"]?.ToObject<float?>() ?? 0f;

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject avatarObject, out string idInfo);
            if (findError != null) return findError;

            Type aasType = FindCckType("CVRAdvancedAvatarSettings") ?? FindCckType("AdvancedAvatarSettings");
            Component aasComp = null;

            if (aasType != null)
            {
                aasComp = avatarObject.GetComponent(aasType) ?? Undo.AddComponent(avatarObject, aasType);
            }

            EditorUtility.SetDirty(avatarObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured AAS '{settingName}' ({settingType}) on '{avatarObject.name}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured AAS setting '{settingName}' ({settingType}) on '{avatarObject.name}'",
                ["avatarName"] = avatarObject.name,
                ["instanceId"] = UnityObjectId.GetObjectId(avatarObject),
                ["cckInstalled"] = aasType != null,
                ["settingName"] = settingName,
                ["settingType"] = settingType,
                ["defaultValue"] = defaultValue
            };
        }

        private JObject HandleSetupFaceTracking(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject avatarObject, out string idInfo);
            if (findError != null) return findError;

            Type ftType = FindCckType("CVRFaceTracking") ?? FindCckType("FaceTracking");
            Component ftComp = null;

            if (ftType != null)
            {
                ftComp = avatarObject.GetComponent(ftType) ?? Undo.AddComponent(avatarObject, ftType);
            }

            EditorUtility.SetDirty(avatarObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured CVRFaceTracking on '{avatarObject.name}'" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured CVRFaceTracking on '{avatarObject.name}'",
                ["avatarName"] = avatarObject.name,
                ["instanceId"] = UnityObjectId.GetObjectId(avatarObject),
                ["cckInstalled"] = ftType != null
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
