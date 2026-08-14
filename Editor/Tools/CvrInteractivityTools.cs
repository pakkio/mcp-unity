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
    /// Tool for configuring ChilloutVR CCK interactive triggers, pickups, and networked state synchronization.
    /// </summary>
    public class ConfigureCvrInteractivityTool : McpToolBase
    {
        public ConfigureCvrInteractivityTool()
        {
            Name = "configure_cvr_interactivity";
            Description = "Configures ChilloutVR CCK interactivity: CVRInteractable trigger actions, CVRPickupObject physics grips, and CVRVariableBuffer networked variables.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string action = parameters["action"]?.ToObject<string>()?.ToLowerInvariant() ?? "add_interactable";
            string reason = parameters["reason"]?.ToObject<string>();

            switch (action)
            {
                case "add_interactable":
                    return HandleAddInteractable(parameters, reason);

                case "configure_pickup":
                    return HandleConfigurePickup(parameters, reason);

                case "setup_variable_buffer":
                    return HandleSetupVariableBuffer(parameters, reason);

                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown action '{action}'. Supported actions: 'add_interactable', 'configure_pickup', 'setup_variable_buffer'",
                        "invalid_action"
                    );
            }
        }

        private JObject HandleAddInteractable(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string interactionType = parameters["interactionType"]?.ToObject<string>()?.ToLowerInvariant() ?? "interact";
            string actionType = parameters["actionType"]?.ToObject<string>()?.ToLowerInvariant() ?? "toggle_gameobject";

            int? targetInstanceId = parameters["targetInstanceId"]?.ToObject<int?>();
            string targetObjectPath = parameters["targetObjectPath"]?.ToObject<string>();
            string parameterName = parameters["parameterName"]?.ToObject<string>();
            string parameterValue = parameters["parameterValue"]?.ToObject<string>();

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            // Ensure collider exists for interaction raycast/trigger detection
            if (targetObject.GetComponent<Collider>() == null)
            {
                BoxCollider box = Undo.AddComponent<BoxCollider>(targetObject);
                if (interactionType == "area_trigger" || interactionType == "touch")
                {
                    box.isTrigger = true;
                }
            }

            Type cvrInteractableType = FindCckType("CVRInteractable");
            Component interactable = null;

            if (cvrInteractableType != null)
            {
                interactable = targetObject.GetComponent(cvrInteractableType) ?? Undo.AddComponent(targetObject, cvrInteractableType);
                SetComponentField(interactable, "interactionType", interactionType);
            }

            GameObject actionTarget = null;
            if (targetInstanceId.HasValue || !string.IsNullOrEmpty(targetObjectPath))
            {
                GameObjectToolUtils.FindGameObject(targetInstanceId, targetObjectPath, out actionTarget, out string targetInfo);
            }

            EditorUtility.SetDirty(targetObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured CVRInteractable on '{targetObject.name}' (type={interactionType}, action={actionType})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured CVRInteractable on '{targetObject.name}'",
                ["instanceId"] = UnityObjectId.GetObjectId(targetObject),
                ["gameObjectName"] = targetObject.name,
                ["cckInstalled"] = cvrInteractableType != null,
                ["interactionType"] = interactionType,
                ["actionType"] = actionType,
                ["actionTarget"] = actionTarget != null ? actionTarget.name : "none",
                ["parameterName"] = parameterName,
                ["parameterValue"] = parameterValue
            };
        }

        private JObject HandleConfigurePickup(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string gripType = parameters["gripType"]?.ToObject<string>()?.ToLowerInvariant() ?? "origin";
            bool autoHold = parameters["autoHold"]?.ToObject<bool?>() ?? true;
            float throwMultiplier = parameters["throwVelocityMultiplier"]?.ToObject<float?>() ?? 1.0f;
            bool dropOnTeleport = parameters["dropOnTeleport"]?.ToObject<bool?>() ?? false;

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            // Ensure Rigidbody exists for physics pickup
            Rigidbody rb = targetObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = Undo.AddComponent<Rigidbody>(targetObject);
                rb.mass = 1.0f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            // Ensure Collider exists
            if (targetObject.GetComponent<Collider>() == null)
            {
                Undo.AddComponent<BoxCollider>(targetObject);
            }

            Type cvrPickupType = FindCckType("CVRPickupObject");
            Component pickupComp = null;

            if (cvrPickupType != null)
            {
                pickupComp = targetObject.GetComponent(cvrPickupType) ?? Undo.AddComponent(targetObject, cvrPickupType);
                SetComponentField(pickupComp, "gripType", gripType);
                SetComponentField(pickupComp, "autoHold", autoHold);
                SetComponentField(pickupComp, "throwVelocityMultiplier", throwMultiplier);
                SetComponentField(pickupComp, "dropOnTeleport", dropOnTeleport);
            }

            EditorUtility.SetDirty(targetObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured CVRPickupObject on '{targetObject.name}' (grip={gripType}, throwMultiplier={throwMultiplier})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured CVRPickupObject on '{targetObject.name}'",
                ["instanceId"] = UnityObjectId.GetObjectId(targetObject),
                ["gameObjectName"] = targetObject.name,
                ["cckInstalled"] = cvrPickupType != null,
                ["gripType"] = gripType,
                ["autoHold"] = autoHold,
                ["throwVelocityMultiplier"] = throwMultiplier,
                ["dropOnTeleport"] = dropOnTeleport
            };
        }

        private JObject HandleSetupVariableBuffer(JObject parameters, string reason)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string variableName = parameters["variableName"]?.ToObject<string>() ?? "SyncedScore";
            string variableType = parameters["variableType"]?.ToObject<string>()?.ToLowerInvariant() ?? "int";
            string defaultValue = parameters["defaultValue"]?.ToObject<string>() ?? "0";

            JObject findError = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string idInfo);
            if (findError != null) return findError;

            Type cvrVarBufferType = FindCckType("CVRVariableBuffer");
            Component varBuffer = null;

            if (cvrVarBufferType != null)
            {
                varBuffer = targetObject.GetComponent(cvrVarBufferType) ?? Undo.AddComponent(targetObject, cvrVarBufferType);
                SetComponentField(varBuffer, "variableName", variableName);
                SetComponentField(varBuffer, "variableType", variableType);
            }

            EditorUtility.SetDirty(targetObject);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            McpLogger.LogInfo($"[MCP Unity] Configured CVRVariableBuffer on '{targetObject.name}' ({variableName}: {variableType})" + (reason != null ? $" — {reason}" : ""));

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully configured CVRVariableBuffer '{variableName}' ({variableType}) on '{targetObject.name}'",
                ["instanceId"] = UnityObjectId.GetObjectId(targetObject),
                ["gameObjectName"] = targetObject.name,
                ["cckInstalled"] = cvrVarBufferType != null,
                ["variableName"] = variableName,
                ["variableType"] = variableType,
                ["defaultValue"] = defaultValue
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
