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
                            // Unity silently refuses to enter play mode while scripts are broken
                            // or still compiling. Since the actual transition is deferred (below)
                            // and can no longer be read back before this response is built, catch
                            // the cases we CAN determine now rather than reporting a success the
                            // Editor is going to ignore.
                            if (EditorUtility.scriptCompilationFailed)
                            {
                                return McpUnitySocketHandler.CreateErrorResponse(
                                    "Cannot enter play mode: the project has script compilation errors. " +
                                    "Use 'get_compilation_errors' to inspect them.",
                                    "compilation_failed"
                                );
                            }

                            if (EditorApplication.isCompiling)
                            {
                                return McpUnitySocketHandler.CreateErrorResponse(
                                    "Cannot enter play mode: scripts are still compiling. Retry once compilation finishes.",
                                    "compiling"
                                );
                            }

                            // Entering play mode synchronously fires ExitingEditMode, which
                            // unconditionally closes this WebSocket (StopServer, see
                            // McpUnityServer.OnPlayModeStateChanged) so a client can use fast
                            // polling to reconnect. Assigning isPlaying here directly races
                            // that close against this response actually reaching the client -
                            // observed as "Connection failed" or a timeout despite the action
                            // succeeding. Deferring the assignment to the next editor tick lets
                            // the response for THIS request finish sending first.
                            deferredTransition = () =>
                            {
                                SceneSaveHelper.EnsureAllScenesSavedSilently();
                                EditorApplication.isPlaying = true;
                            };
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

                bool transitionPending = deferredTransition != null;
                string targetState = isPlaying ? (isPaused ? "Playing (paused)" : "Playing") : "Edit mode";

                var result = new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    // 'success' means the action was accepted, not that the Editor has reached the
                    // target state: a deferred play/stop is applied a tick after this response is
                    // sent, and can still be vetoed by a playModeStateChanged handler or an
                    // unsaved-scene prompt. Say so instead of asserting the final state.
                    ["message"] = transitionPending
                        ? $"Action '{action}' accepted. Target state: {targetState} (applied on the next editor tick - confirm with 'get_play_mode_status')"
                        : $"Action '{action}' executed. State: {targetState}",
                    ["action"] = action,
                    ["wasPlaying"] = wasPlaying,
                    ["wasPaused"] = wasPaused,
                    ["isPlaying"] = isPlaying,
                    ["isPaused"] = isPaused,
                    // True when isPlaying/isPaused describe the requested target rather than the
                    // state actually observed at the time this response was produced.
                    ["transitionPending"] = transitionPending
                };

                McpLogger.LogInfo($"Play mode action '{action}' {(transitionPending ? "accepted" : "executed")}. isPlaying={isPlaying}, isPaused={isPaused}, transitionPending={transitionPending}");

                if (transitionPending)
                {
                    // EditorApplication.delayCall is NOT reliable here: it is a plain static
                    // delegate, and per McpMainThreadDispatcher's own doc comment (see
                    // McpUnitySocketHandler.cs) it is not reliably drained while the Editor is
                    // idle/unfocused in the background - exactly the state this Editor is in when
                    // driven purely through MCP tool calls with no human interacting with the
                    // window. McpUnityServer.ScheduleStartServer hit the same issue and pairs
                    // delayCall with an EditorApplication.update subscription for that reason.
                    // A self-removing EditorApplication.update handler is used here instead:
                    // update keeps firing regardless of focus, and standard multicast-delegate
                    // invocation semantics guarantee a handler added via "+=" during the current
                    // tick's invocation is not included in that same invocation - so this still
                    // genuinely defers to a later tick rather than running before this response
                    // has been sent.
                    bool expectedPlaying = isPlaying;
                    string capturedAction = action;
                    ApplyDeferredTransition(deferredTransition, expectedPlaying, capturedAction);
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

        /// <summary>
        /// Runs <paramref name="deferredTransition"/> on the next EditorApplication.update tick
        /// (not delayCall - see the caller for why), then a tick after that, logs a warning if
        /// the transition did not take effect (e.g. vetoed by another playModeStateChanged
        /// handler in the project).
        /// </summary>
        private static void ApplyDeferredTransition(Action deferredTransition, bool expectedPlaying, string action)
        {
            EditorApplication.CallbackFunction apply = null;
            apply = () =>
            {
                EditorApplication.update -= apply;
                deferredTransition();

                EditorApplication.CallbackFunction verify = null;
                verify = () =>
                {
                    EditorApplication.update -= verify;
                    if (EditorApplication.isPlaying != expectedPlaying)
                    {
                        McpLogger.LogWarning(
                            $"Play mode action '{action}' did not take effect: expected isPlaying={expectedPlaying}, " +
                            $"actual isPlaying={EditorApplication.isPlaying}. The transition was likely blocked by the Editor.");
                    }
                };
                EditorApplication.update += verify;
            };
            EditorApplication.update += apply;
        }
    }
}
