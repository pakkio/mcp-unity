using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool to draw temporary shapes in the Unity Editor Scene View using Handles API
    /// </summary>
    public class DrawSceneGizmoTool : McpToolBase
    {
        private class DebugShape
        {
            public string Type { get; set; }
            public Vector3 Position { get; set; }
            public Vector3 Size { get; set; }
            public Vector3 EndPosition { get; set; }
            public Color Color { get; set; }
            public double ExpirationTime { get; set; }
        }

        private static readonly List<DebugShape> Shapes = new List<DebugShape>();
        private static bool _hooked = false;

        public DrawSceneGizmoTool()
        {
            Name = "draw_scene_gizmo";
            Description = "Draws a temporary debug shape (line, box, or sphere) in the Unity Editor Scene View";
            EnsureHooked();
        }

        private static void EnsureHooked()
        {
            if (!_hooked)
            {
                SceneView.duringSceneGui += OnSceneGUI;
                _hooked = true;
            }
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            double currentTime = EditorApplication.timeSinceStartup;
            lock (Shapes)
            {
                Shapes.RemoveAll(s => currentTime > s.ExpirationTime);
                if (Shapes.Count == 0) return;

                foreach (var shape in Shapes)
                {
                    Handles.color = shape.Color;
                    if (shape.Type.Equals("line", StringComparison.OrdinalIgnoreCase))
                    {
                        Handles.DrawLine(shape.Position, shape.EndPosition);
                    }
                    else if (shape.Type.Equals("box", StringComparison.OrdinalIgnoreCase))
                    {
                        Handles.DrawWireCube(shape.Position, shape.Size);
                    }
                    else if (shape.Type.Equals("sphere", StringComparison.OrdinalIgnoreCase))
                    {
                        Handles.DrawWireDisc(shape.Position, Vector3.up, shape.Size.x);
                        Handles.DrawWireDisc(shape.Position, Vector3.forward, shape.Size.x);
                        Handles.DrawWireDisc(shape.Position, Vector3.right, shape.Size.x);
                    }
                }
            }
            sceneView.Repaint();
        }

        public override JObject Execute(JObject parameters)
        {
            string shapeType = parameters["shapeType"]?.ToObject<string>();
            JObject posObj = parameters["position"] as JObject;
            JObject endPosObj = parameters["endPosition"] as JObject;
            JObject sizeObj = parameters["size"] as JObject;
            JObject colorObj = parameters["color"] as JObject;
            float duration = parameters["duration"]?.ToObject<float>() ?? 5f;

            if (string.IsNullOrEmpty(shapeType))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'shapeType' (line, box, sphere) must be provided",
                    "validation_error"
                );
            }

            try
            {
                Vector3 position = posObj != null ? new Vector3(
                    posObj["x"]?.ToObject<float>() ?? 0f,
                    posObj["y"]?.ToObject<float>() ?? 0f,
                    posObj["z"]?.ToObject<float>() ?? 0f
                ) : Vector3.zero;

                Vector3 endPosition = endPosObj != null ? new Vector3(
                    endPosObj["x"]?.ToObject<float>() ?? 0f,
                    endPosObj["y"]?.ToObject<float>() ?? 0f,
                    endPosObj["z"]?.ToObject<float>() ?? 0f
                ) : Vector3.zero;

                Vector3 size = sizeObj != null ? new Vector3(
                    sizeObj["x"]?.ToObject<float>() ?? 1f,
                    sizeObj["y"]?.ToObject<float>() ?? 1f,
                    sizeObj["z"]?.ToObject<float>() ?? 1f
                ) : Vector3.one;

                Color color = Color.green;
                if (colorObj != null)
                {
                    color = new Color(
                        colorObj["r"]?.ToObject<float>() ?? 0f,
                        colorObj["g"]?.ToObject<float>() ?? 1f,
                        colorObj["b"]?.ToObject<float>() ?? 0f,
                        colorObj["a"]?.ToObject<float>() ?? 1f
                    );
                }

                lock (Shapes)
                {
                    Shapes.Add(new DebugShape
                    {
                        Type = shapeType,
                        Position = position,
                        EndPosition = endPosition,
                        Size = size,
                        Color = color,
                        ExpirationTime = EditorApplication.timeSinceStartup + duration
                    });
                }

                // Force Scene View to redraw immediately
                SceneView.RepaintAll();

                return new JObject
                {
                    ["success"] = true,
                    ["message"] = $"Successfully queued debug shape '{shapeType}' in Scene view for {duration} seconds."
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error queueing debug shape: {ex.Message}",
                    "gizmo_error"
                );
            }
        }
    }
}
