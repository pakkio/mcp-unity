using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using McpUnity.Utils;

namespace McpUnity.Unity
{
    /// <summary>
    /// A native .NET standard based WebSocket server that implements RFC 6455 over a TcpListener.
    /// Eliminates the dependency on third-party websocket-sharp.dll.
    /// </summary>
    public class McpWebSocketServer
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _path;
        private readonly McpUnityServer _server;
        private readonly int _connectionGeneration;

        private const int HeaderReadBufferSize = 256;
        private const int MaxHeaderLineLength = 16 * 1024;

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private bool _isListening;

        private readonly ConcurrentDictionary<string, McpUnitySocketHandler> _connections = new ConcurrentDictionary<string, McpUnitySocketHandler>();

        public bool IsListening => _isListening;
        public ICollection<McpUnitySocketHandler> ActiveConnections => _connections.Values;

        public McpWebSocketServer(string host, int port, string path, McpUnityServer server, int connectionGeneration)
        {
            _host = host;
            _port = port;
            _path = path;
            _server = server;
            _connectionGeneration = connectionGeneration;
        }

        public void Start()
        {
            if (_isListening) return;

            IPAddress ipAddress;
            if (_host == "0.0.0.0")
            {
                ipAddress = IPAddress.Any;
            }
            else if (_host == "localhost")
            {
                ipAddress = IPAddress.Loopback;
            }
            else if (!IPAddress.TryParse(_host, out ipAddress))
            {
                ipAddress = IPAddress.Loopback;
            }

            _listener = new TcpListener(ipAddress, _port);
            // Allow address reuse to match standard socket options
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();

            _isListening = true;
            _cts = new CancellationTokenSource();

            // Run accept loop in a separate task
            Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!_isListening) return;

            _isListening = false;
            _cts?.Cancel();

            try
            {
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                McpLogger.LogInfo($"Error stopping TcpListener: {ex.Message}");
            }

            CloseAllClients(1001, "Server stopping");
            _connections.Clear();
        }

        public void CloseAllClients(ushort closeCode, string reason)
        {
            var handlers = new List<McpUnitySocketHandler>(_connections.Values);
            foreach (var handler in handlers)
            {
                try
                {
                    handler.Close(closeCode, reason);
                }
                catch (Exception ex)
                {
                    McpLogger.LogInfo($"Error closing connection {handler.ID}: {ex.Message}");
                }
            }
            _connections.Clear();
        }

        public void CloseSession(string id, ushort closeCode, string reason)
        {
            if (_connections.TryGetValue(id, out var handler))
            {
                handler.Close(closeCode, reason);
                _connections.TryRemove(id, out _);
            }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (_isListening && !ct.IsCancellationRequested)
            {
                try
                {
                    TcpClient tcpClient = await _listener.AcceptTcpClientAsync();
                    // Process connection in a separate task
                    _ = Task.Run(() => ProcessConnectionAsync(tcpClient, ct), ct);
                }
                catch (Exception)
                {
                    if (!_isListening) break;
                    await Task.Delay(100, ct);
                }
            }
        }

        private async Task ProcessConnectionAsync(TcpClient tcpClient, CancellationToken ct)
        {
            using (tcpClient)
            {
                NetworkStream stream = tcpClient.GetStream();
                try
                {
                    // 1. Read HTTP Handshake Request
                    var headers = new NameValueCollection();
                    string requestPath = "";
                    string secWebSocketKey = null;

                    var lineReader = new BufferedLineReader(stream, HeaderReadBufferSize);
                    string firstLine = await lineReader.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(firstLine)) return;

                    var requestParts = firstLine.Split(' ');
                    if (requestParts.Length >= 2)
                    {
                        requestPath = requestParts[1];
                    }

                    // Read remaining headers
                    while (true)
                    {
                        string line = await lineReader.ReadLineAsync(ct);
                        if (string.IsNullOrEmpty(line)) break; // End of headers

                        int colonIdx = line.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string key = line.Substring(0, colonIdx).Trim();
                            string val = line.Substring(colonIdx + 1).Trim();
                            headers.Add(key, val);

                            if (key.Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                            {
                                secWebSocketKey = val;
                            }
                        }
                    }

                    // 2. Validate Path and WebSocket upgrade headers
                    string cleanPath = requestPath.Split('?')[0]; // strip query params
                    if (!cleanPath.Equals(_path, StringComparison.OrdinalIgnoreCase) && 
                        !cleanPath.Equals(_path + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        // Path mismatch, send 404
                        byte[] responseBytes = Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                        return;
                    }

                    if (string.IsNullOrEmpty(secWebSocketKey))
                    {
                        // Invalid WebSocket request, send 400
                        byte[] responseBytes = Encoding.UTF8.GetBytes("HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                        return;
                    }

                    // 3. Compute Sec-WebSocket-Accept and complete handshake
                    string combined = secWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                    byte[] sha1Hash;
                    using (var sha1 = SHA1.Create())
                    {
                        sha1Hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(combined));
                    }
                    string acceptKey = Convert.ToBase64String(sha1Hash);

                    var responseBuilder = new StringBuilder();
                    responseBuilder.Append("HTTP/1.1 101 Switching Protocols\r\n");
                    responseBuilder.Append("Upgrade: websocket\r\n");
                    responseBuilder.Append("Connection: Upgrade\r\n");
                    responseBuilder.Append($"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n");

                    byte[] handshakeBytes = Encoding.UTF8.GetBytes(responseBuilder.ToString());
                    await stream.WriteAsync(handshakeBytes, 0, handshakeBytes.Length, ct);

                    // 4. Upgrade stream to WebSocket. lineReader may have buffered bytes past the
                    // blank line terminating the headers (e.g. the client's first WS frame arriving
                    // in the same TCP segment as the handshake) - replay those before the raw stream
                    // so they aren't silently dropped.
                    Stream wsStream = lineReader.WrapRemainder(stream);
                    using (WebSocket webSocket = WebSocket.CreateFromStream(wsStream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30)))
                    {
                        string id = Guid.NewGuid().ToString();
                        var handler = new McpUnitySocketHandler(_server, _connectionGeneration, id, webSocket, headers);
                        _connections[id] = handler;

                        // Call connection lifecycle methods
                        handler.OnOpen();

                        // Receive loop
                        byte[] buffer = new byte[8192];
                        using (var messageBuffer = new MemoryStream())
                        {
                            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                            {
                                WebSocketReceiveResult result;
                                try
                                {
                                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                                }
                                catch (OperationCanceledException)
                                {
                                    // Server stopping or receive cancelled.
                                    break;
                                }
                                catch (WebSocketException ex)
                                {
                                    if (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely ||
                                        ex.WebSocketErrorCode == WebSocketError.Success ||
                                        ex.WebSocketErrorCode == WebSocketError.InvalidState ||
                                        ex.InnerException is SocketException ||
                                        ex.Message.IndexOf("close handshake", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        McpLogger.LogInfo($"WebSocket client disconnected: {ex.Message}");
                                    }
                                    else
                                    {
                                        McpLogger.LogWarning($"WebSocket receive error: {ex.Message}");
                                    }
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    McpLogger.LogError($"Unexpected WebSocket receive error: {ex.Message}");
                                    break;
                                }

                                if (result.MessageType == WebSocketMessageType.Close)
                                {
                                    try
                                    {
                                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed by peer", CancellationToken.None);
                                    }
                                    catch { }
                                    break;
                                }

                                if (result.MessageType == WebSocketMessageType.Binary)
                                {
                                    // MCP traffic uses text frames; discard any accumulated
                                    // fragments from a prior message and skip binary payloads.
                                    messageBuffer.SetLength(0);
                                    continue;
                                }

                                if (result.Count > 0)
                                {
                                    messageBuffer.Write(buffer, 0, result.Count);
                                }

                                if (result.EndOfMessage)
                                {
                                    string messageText = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                                    messageBuffer.SetLength(0);
                                    handler.OnMessage(messageText);
                                }
                            }

                            handler.OnClose();
                            _connections.TryRemove(id, out _);
                        }
                    }
                }
                catch (Exception ex)
                {
                    McpLogger.LogError($"Connection error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Reads CRLF-terminated HTTP header lines off a stream, buffering reads into a chunk
        /// instead of one byte at a time. Unlike a stateless per-call read, this keeps whatever a
        /// chunk read past the end of the current line in an internal carry-over buffer instead of
        /// discarding it - a single client write (the common case: the whole handshake request
        /// arrives in one TCP segment) can contain every header line, and dropping the bytes after
        /// the first '\n' left every later ReadLineAsync call blocked on a read that would never
        /// arrive, since the client was done writing and waiting on a response.
        /// </summary>
        private sealed class BufferedLineReader
        {
            private readonly Stream _stream;
            private readonly byte[] _buffer;
            private int _bufferOffset;
            private int _bufferLength;

            public BufferedLineReader(Stream stream, int bufferSize)
            {
                _stream = stream;
                _buffer = new byte[bufferSize];
            }

            public async Task<string> ReadLineAsync(CancellationToken ct)
            {
                var line = new List<byte>(128);
                while (true)
                {
                    if (_bufferOffset >= _bufferLength)
                    {
                        _bufferLength = await _stream.ReadAsync(_buffer, 0, _buffer.Length, ct);
                        _bufferOffset = 0;
                        if (_bufferLength == 0)
                        {
                            return Encoding.UTF8.GetString(line.ToArray());
                        }
                    }

                    while (_bufferOffset < _bufferLength)
                    {
                        byte val = _buffer[_bufferOffset++];
                        if (val == '\n')
                        {
                            return Encoding.UTF8.GetString(line.ToArray());
                        }

                        if (val != '\r')
                        {
                            line.Add(val);
                            if (line.Count > MaxHeaderLineLength)
                            {
                                throw new InvalidDataException("HTTP header line exceeds maximum allowed length");
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Wraps <paramref name="stream"/> so any bytes already pulled into this reader's
            /// internal buffer past the last line consumed are replayed first, then reads fall
            /// through to <paramref name="stream"/> as normal. Returns <paramref name="stream"/>
            /// unchanged when nothing was left over.
            /// </summary>
            public Stream WrapRemainder(Stream stream)
            {
                if (_bufferOffset >= _bufferLength)
                {
                    return stream;
                }

                int count = _bufferLength - _bufferOffset;
                byte[] remainder = new byte[count];
                Array.Copy(_buffer, _bufferOffset, remainder, 0, count);
                _bufferOffset = _bufferLength;
                return new PrefixedStream(stream, remainder);
            }
        }

        /// <summary>
        /// A read-through stream that yields a fixed prefix of already-buffered bytes before
        /// falling through to the inner stream. Used to hand a WebSocket layer bytes that were
        /// over-read while parsing the HTTP handshake off the same connection.
        /// </summary>
        private sealed class PrefixedStream : Stream
        {
            private readonly Stream _inner;
            private byte[] _prefix;
            private int _prefixOffset;

            public PrefixedStream(Stream inner, byte[] prefix)
            {
                _inner = inner;
                _prefix = prefix;
            }

            public override bool CanRead => true;
            public override bool CanWrite => _inner.CanWrite;
            public override bool CanSeek => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_prefix != null)
                {
                    int n = Math.Min(count, _prefix.Length - _prefixOffset);
                    Array.Copy(_prefix, _prefixOffset, buffer, offset, n);
                    _prefixOffset += n;
                    if (_prefixOffset >= _prefix.Length)
                    {
                        _prefix = null;
                    }
                    return n;
                }

                return await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override void Write(byte[] buffer, int offset, int count) =>
                _inner.Write(buffer, offset, count);

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                _inner.WriteAsync(buffer, offset, count, cancellationToken);

            public override void Flush() => _inner.Flush();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
