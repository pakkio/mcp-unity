using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for controlling Unity play mode (play, pause, step)
    /// </summary>
    public class SetPlayModeStatusTool : McpToolBase
    {
        public SetPlayModeStatusTool()
        {
            Name = "set_play_mode_status";
            Description = "Controls Unity play mode. Actions: 'play', 'pause', 'stop', 'step'.";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                string action = parameters?["action"]?.ToString()?.ToLowerInvariant();
                
                if (string.IsNullOrEmpty(action))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Missing required parameter 'action'. Valid actions: 'play', 'pause', 'stop', 'step'",
                        "missing_parameter"
                    );
                }

                bool wasPlaying = EditorApplication.isPlaying;
                bool wasPaused = EditorApplication.isPaused;

                // Target state to report - for 'play'/'stop' this is the intended state, not yet
                // applied (see deferral note below).
                bool isPlaying = wasPlaying;
                bool isPaused = wasPaused;
                Action deferredTransition = null;

                switch (action)
                {
                    case "play":
                        if (!EditorApplication.isPlaying)
                        {
                            // Entering play mode synchronously fires ExitingEditMode, which
                            // unconditionally closes this WebSocket (StopServer, see
                            // McpUnityServer.OnPlayModeStateChanged) so a client can use fast
                            // polling to reconnect. Assigning isPlaying here directly races
                            // that close against this response actually reaching the client -
                            // observed as "Connection failed" or a timeout despite the action
                            // succeeding. Deferring the assignment to the next editor tick lets
                            // the response for THIS request finish sending first.
                            deferredTransition = () => EditorApplication.isPlaying = true;
                            isPlaying = true;
                            isPaused = false;
                        }
                        else if (EditorApplication.isPaused)
                        {
                            // Unpause if already playing
                            EditorApplication.isPaused = false;
                            isPaused = false;
                        }
                        break;

                    case "pause":
                        if (EditorApplication.isPlaying)
                        {
                            EditorApplication.isPaused = !EditorApplication.isPaused;
                            isPaused = EditorApplication.isPaused;
                        }
                        else
                        {
                            return McpUnitySocketHandler.CreateErrorResponse(
                                "Cannot pause: Editor is not in play mode",
                                "invalid_state"
                            );
                        }
                        break;

                    case "stop":
                        if (EditorApplication.isPlaying)
                        {
                            // Same race as 'play' above: ExitingEditMode fires on exit too.
                            deferredTransition = () => EditorApplication.isPlaying = false;
                            isPlaying = false;
                            isPaused = false;
                        }
                        break;

                    case "step":
                        if (EditorApplication.isPlaying)
                        {
                            EditorApplication.Step();
                        }
                        else
                        {
                            return McpUnitySocketHandler.CreateErrorResponse(
                                "Cannot step: Editor is not in play mode",
                                "invalid_state"
                            );
                        }
                        break;

                    default:
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Invalid action '{action}'. Valid actions: 'play', 'pause', 'stop', 'step'",
                            "invalid_parameter"
                        );
                }

                var result = new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Action '{action}' executed. State: {(isPlaying ? (isPaused ? "Playing (paused)" : "Playing") : "Edit mode")}"
                        + (deferredTransition != null ? " (transition applying on next editor tick)" : ""),
                    ["action"] = action,
                    ["wasPlaying"] = wasPlaying,
                    ["wasPaused"] = wasPaused,
                    ["isPlaying"] = isPlaying,
                    ["isPaused"] = isPaused
                };

                McpLogger.LogInfo($"Play mode action '{action}' executed. isPlaying={isPlaying}, isPaused={isPaused}");

                if (deferredTransition != null)
                {
                    EditorApplication.delayCall += () => deferredTransition();
                }

                return result;
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error controlling play mode: {ex.Message}",
                    "play_mode_error"
                );
            }
        }
    }
}
