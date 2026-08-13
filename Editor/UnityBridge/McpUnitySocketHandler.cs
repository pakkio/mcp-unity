using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using McpUnity.Tools;
using McpUnity.Resources;
using Unity.EditorCoroutines.Editor;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using McpUnity.Utils;

namespace McpUnity.Unity
{
    /// <summary>
    /// Drains work queued from background WebSocket threads on the Unity main thread via
    /// EditorApplication.update, which keeps firing even when the Editor is unfocused.
    ///
    /// This replaces dispatching through EditorApplication.delayCall: delayCall is a plain
    /// static delegate, and a "+=" performed from the WebSocketSharp background thread is not
    /// reliably observed/drained by the main thread while the Editor is idle in the
    /// background. Draining a thread-safe queue
    /// from EditorApplication.update fixes this without relying on cross-thread delegate
    /// mutation.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpMainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        static McpMainThreadDispatcher()
        {
            EditorApplication.update -= Drain;
            EditorApplication.update += Drain;
        }

        public static void Enqueue(Action action)
        {
            if (action != null) _queue.Enqueue(action);
        }

        private static void Drain()
        {
            while (_queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { McpLogger.LogError($"MainThreadDispatcher action failed: {ex}"); }
            }
        }
    }

    /// <summary>
    /// WebSocket handler for MCP Unity communications. Uses native System.Net.WebSockets.WebSocket.
    /// </summary>
    public class McpUnitySocketHandler
    {
        private readonly McpUnityServer _server;
        private readonly int _connectionGeneration;
        private readonly string _id;
        private readonly WebSocket _webSocket;
        private readonly NameValueCollection _headers;

        public string ID => _id;

        /// <summary>
        /// Creates a WebSocket handler for the active server generation.
        /// </summary>
        public McpUnitySocketHandler(McpUnityServer server, int connectionGeneration, string id, WebSocket webSocket, NameValueCollection headers)
        {
            _server = server;
            _connectionGeneration = connectionGeneration;
            _id = id;
            _webSocket = webSocket;
            _headers = headers;
        }

        /// <summary>
        /// Maximum characters logged for a single request/response payload. Some tool responses
        /// (e.g. capture_screenshot) carry megabytes of base64 image data; logging that in full
        /// on every call floods the Unity console and the main thread with string formatting
        /// work for no diagnostic benefit.
        /// </summary>
        private const int MaxLoggedPayloadChars = 2000;

        /// <summary>
        /// Truncates a payload string for logging, leaving a marker with the omitted length.
        /// </summary>
        private static string TruncateForLog(string payload)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length <= MaxLoggedPayloadChars)
            {
                return payload;
            }

            return payload.Substring(0, MaxLoggedPayloadChars)
                + $"... [truncated, {payload.Length - MaxLoggedPayloadChars} more chars]";
        }

        /// <summary>
        /// Constant-time string comparison for the auth token, so response timing can't be used
        /// to guess the configured secret one character at a time.
        /// </summary>
        private static bool SecureTokenEquals(string provided, string expected)
        {
            if (provided == null || expected == null) return false;
            if (provided.Length != expected.Length) return false;

            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                diff |= provided[i] ^ expected[i];
            }
            return diff == 0;
        }

        /// <summary>
        /// Create a standardized error response
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="errorType">Type of error</param>
        /// <returns>A JObject containing the error information</returns>
        public static JObject CreateErrorResponse(string message, string errorType)
        {
            return new JObject
            {
                ["error"] = new JObject
                {
                    ["type"] = errorType,
                    ["message"] = message
                }
            };
        }

        /// <summary>
        /// Sends a text message to the WebSocket client.
        /// </summary>
        public void Send(string message)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(message);
                var segment = new ArraySegment<byte>(bytes);
                try
                {
                    // WebSocket.SendAsync is not thread-safe for concurrent operations
                    lock (_webSocket)
                    {
                        _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                    }
                }
                catch (Exception ex)
                {
                    McpLogger.LogError($"Error sending WebSocket message: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Closes the WebSocket connection gracefully.
        /// </summary>
        public void Close(ushort code, string reason)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    _webSocket.CloseAsync((WebSocketCloseStatus)code, reason ?? "", CancellationToken.None).Wait();
                }
                catch (Exception ex)
                {
                    McpLogger.LogInfo($"Error closing WebSocket connection: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handle incoming messages from WebSocket clients.
        /// </summary>
        public void OnMessage(string data)
        {
            if (!_server.ShouldTrackClient(_connectionGeneration))
            {
                CloseUntrackedConnection();
                return;
            }

            // Dispatch via a thread-safe queue drained in EditorApplication.update rather than
            // EditorApplication.delayCall. A delayCall "+=" from this background thread is not
            // reliably drained by the main thread while the Editor is unfocused/idle, so the
            // request would never run and the client would time out. See McpMainThreadDispatcher.
            McpMainThreadDispatcher.Enqueue(() => HandleMessageAsync(data));
        }

        /// <summary>
        /// Handle WebSocket connection open.
        /// Supports multiple concurrent MCP clients (e.g. multiple Claude Code instances).
        /// </summary>
        public void OnOpen()
        {
            if (!_server.ShouldTrackClient(_connectionGeneration))
            {
                CloseUntrackedConnection();
                return;
            }

            // Extract client name from the X-Client-Name header (if available)
            string clientName = "";
            NameValueCollection headers = _headers;
            if (headers != null && headers.Get("X-Client-Name") != null)
            {
                clientName = headers.Get("X-Client-Name");
            }

            // When remote connections are allowed, the server is reachable by any host on the
            // network. Require a matching shared secret if one is configured - otherwise any
            // network peer can drive destructive tools (execute_menu_item, import_local_file,
            // add_package) with zero authentication.
            McpUnitySettings settings = McpUnitySettings.Instance;
            if (settings.AllowRemoteConnections && !string.IsNullOrEmpty(settings.AuthToken))
            {
                string providedToken = headers != null && headers.Get("X-Mcp-Auth-Token") != null
                    ? headers.Get("X-Mcp-Auth-Token")
                    : null;

                if (!SecureTokenEquals(providedToken, settings.AuthToken))
                {
                    McpLogger.LogWarning(
                        $"Rejected WebSocket connection from client '{(string.IsNullOrEmpty(clientName) ? "Unknown" : clientName)}': missing or invalid X-Mcp-Auth-Token.");
                    try
                    {
                        if (_webSocket.State == WebSocketState.Open)
                        {
                            _webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Missing or invalid auth token", CancellationToken.None).Wait();
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLogger.LogInfo($"Error closing unauthenticated connection: {ex.Message}");
                    }
                    return;
                }
            }

            if (!_server.ShouldTrackClient(_connectionGeneration))
            {
                CloseUntrackedConnection();
                return;
            }

            // Add the client to the server's tracking dictionary
            _server.Clients[ID] = clientName;

            McpLogger.LogInfo($"WebSocket client connected (ID: {ID}, Name: {(string.IsNullOrEmpty(clientName) ? "Unknown" : clientName)}, Total clients: {_server.Clients.Count})");
        }

        /// <summary>
        /// Handle WebSocket connection close
        /// </summary>
        public void OnClose()
        {
            _server.Clients.TryGetValue(ID, out string clientName);

            // Remove the client from the server
            _server.Clients.TryRemove(ID, out _);

            McpLogger.LogInfo($"WebSocket client '{clientName}' disconnected (Remaining clients: {_server.Clients.Count})");
        }

        /// <summary>
        /// Handle WebSocket errors
        /// </summary>
        public void OnError(Exception ex)
        {
            McpLogger.LogError($"WebSocket error: {ex.Message}");
        }

        /// <summary>
        /// Process a WebSocket message on the Unity main thread.
        /// Safe to call EditorCoroutineUtility, Selection, and other Editor APIs from here.
        /// </summary>
        private async void HandleMessageAsync(string data)
        {
            try
            {
                if (!_server.ShouldTrackClient(_connectionGeneration))
                {
                    CloseUntrackedConnection();
                    return;
                }

                McpLogger.LogInfo($"WebSocket message received: {TruncateForLog(data)}");
                JObject requestJson;
                try
                {
                    requestJson = JObject.Parse(data);
                }
                catch (JsonReaderException jre)
                {
                    McpLogger.LogError($"Invalid JSON received: {jre.Message}. Data: {data}");
                    // Attempt to send a parse error response. No requestId is available yet.
                    Send(CreateResponse(null, CreateErrorResponse($"Invalid JSON format: {jre.Message}", "invalid_json")).ToString(Formatting.None));
                    return;
                }

                var method = requestJson["method"]?.ToString();
                var parameters = requestJson["params"] as JObject ?? new JObject();
                var requestId = requestJson["id"]?.ToString();
                // We need to dispatch to Unity's main thread and wait for completion
                var tcs = new TaskCompletionSource<JObject>();

                if (string.IsNullOrEmpty(method))
                {
                    tcs.SetResult(CreateErrorResponse("Missing method in request", "invalid_request"));
                }
                else if (_server.TryGetTool(method, out var tool))
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteTool(tool, parameters, tcs));
                }
                else if (_server.TryGetResource(method, out var resource))
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(FetchResourceCoroutine(resource, parameters, tcs));
                }
                else
                {
                    tcs.SetResult(CreateErrorResponse($"Unknown method: {method}", "unknown_method"));
                }

                JObject responseJson = await tcs.Task;
                JObject jsonRpcResponse = CreateResponse(requestId, responseJson);
                string responseStr = jsonRpcResponse.ToString(Formatting.None);

                McpLogger.LogInfo($"WebSocket message response for request ID '{requestId}': {TruncateForLog(responseStr)}");

                // Send the response back to the client
                Send(responseStr);
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Error processing message: {ex.Message}");

                Send(CreateErrorResponse($"Internal server error: {ex.Message}", "internal_error").ToString(Formatting.None));
            }
        }

        private void CloseUntrackedConnection()
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server is restarting", CancellationToken.None).Wait();
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogInfo($"Error closing untracked WebSocket connection: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute a tool with the provided parameters
        /// </summary>
        private IEnumerator ExecuteTool(McpToolBase tool, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (tool.IsAsync)
                {
                    tool.ExecuteAsync(parameters, tcs);
                }
                else
                {
                    var result = tool.Execute(parameters);
                    tcs.SetResult(result);
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Error executing tool {tool.Name}: {ex.Message}\n{ex.StackTrace}");
                tcs.SetResult(CreateErrorResponse(
                    $"Failed to execute tool {tool.Name}: {ex.Message}",
                    "tool_execution_error"
                ));
            }

            yield return null;
        }

        /// <summary>
        /// Fetch a resource with the provided parameters
        /// </summary>
        private IEnumerator FetchResourceCoroutine(McpResourceBase resource, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (resource.IsAsync)
                {
                    resource.FetchAsync(parameters, tcs);
                }
                else
                {
                    var result = resource.Fetch(parameters);
                    tcs.SetResult(result);
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Error fetching resource {resource.Name}: {ex.Message}\n{ex.StackTrace}");
                tcs.SetResult(CreateErrorResponse(
                    $"Failed to fetch resource {resource.Name}: {ex.Message}",
                    "resource_fetch_error"
                ));
            }
            yield return null;
        }

        /// <summary>
        /// Create a JSON-RPC 2.0 response
        /// </summary>
        /// <param name="requestId">Request ID</param>
        /// <param name="result">Result object</param>
        /// <returns>JSON-RPC 2.0 response</returns>
        private JObject CreateResponse(string requestId, JObject result)
        {
            // Format as JSON-RPC 2.0 response
            JObject jsonRpcResponse = new JObject
            {
                ["id"] = requestId
            };

            // Add result or error
            if (result.TryGetValue("error", out var errorObj))
            {
                jsonRpcResponse["error"] = errorObj;
            }
            else
            {
                jsonRpcResponse["result"] = result;
            }

            return jsonRpcResponse;
        }
    }
}
