import { jest, describe, it, expect, beforeEach, afterEach } from '@jest/globals';

// Mock WebSocket before importing modules that use it
const mockWebSocketInstances: any[] = [];

const createMockWebSocket = (overrides: Record<string, any> = {}) => ({
  readyState: 1,
  onopen: null,
  onclose: null,
  onerror: null,
  onmessage: null,
  send: jest.fn(),
  close: jest.fn(),
  terminate: jest.fn(),
  ping: jest.fn(),
  on: jest.fn(),
  removeAllListeners: jest.fn(),
  ...overrides
});

const mockWebSocketConstructor = jest.fn(() => {
  const socket = createMockWebSocket();
  mockWebSocketInstances.push(socket);
  return socket;
});

const mockWebSocketModule = Object.assign(mockWebSocketConstructor, {
  CONNECTING: 0,
  OPEN: 1,
  CLOSING: 2,
  CLOSED: 3
});

jest.unstable_mockModule('ws', () => ({
  default: mockWebSocketModule,
  WebSocket: mockWebSocketModule
}));

// Dynamic imports after mocking
const { UnityConnection, ConnectionState } = await import('../unity/unityConnection');
const { Logger, LogLevel } = await import('../utils/logger');
const { McpUnityError, ErrorType } = await import('../utils/errors');

// Type imports
import type { ConnectionStateChange } from '../unity/unityConnection';

// Create a logger that doesn't output anything (for testing)
const createTestLogger = () => {
  process.env.LOGGING = 'false';
  process.env.LOGGING_FILE = 'false';
  return new Logger('Test', LogLevel.ERROR);
};

describe('UnityConnection', () => {
  let connection: InstanceType<typeof UnityConnection>;
  let testLogger: InstanceType<typeof Logger>;

  beforeEach(() => {
    testLogger = createTestLogger();
    mockWebSocketConstructor.mockImplementation(() => {
      const socket = createMockWebSocket();
      mockWebSocketInstances.push(socket);
      return socket;
    });
    mockWebSocketConstructor.mockClear();
    mockWebSocketInstances.length = 0;

    connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      clientName: 'TestClient',
      minReconnectDelay: 100,
      maxReconnectDelay: 1000,
      heartbeatInterval: 0
    });
  });

  afterEach(() => {
    connection.disconnect();
    jest.clearAllMocks();
  });

  describe('Initial State', () => {
    it('should start in disconnected state', () => {
      expect(connection.connectionState).toBe(ConnectionState.Disconnected);
    });

    it('should not be connected initially', () => {
      expect(connection.isConnected).toBe(false);
    });

    it('should not be connecting initially', () => {
      expect(connection.isConnecting).toBe(false);
    });

    it('should have -1 for timeSinceLastPong before any connection', () => {
      expect(connection.timeSinceLastPong).toBe(-1);
    });
  });

  describe('State Change Events', () => {
    it('should emit stateChange event when connect is called', (done) => {
      let firstEvent = true;
      connection.on('stateChange', (change: ConnectionStateChange) => {
        // Only check the first state change event
        if (firstEvent && change.currentState === ConnectionState.Connecting) {
          firstEvent = false;
          expect(change.previousState).toBe(ConnectionState.Disconnected);
          expect(change.currentState).toBe(ConnectionState.Connecting);
          done();
        }
      });

      connection.connect().catch(() => {});
    });

    it('should include reason in state change', (done) => {
      let eventReceived = false;
      connection.on('stateChange', (change: ConnectionStateChange) => {
        if (!eventReceived && change.currentState === ConnectionState.Connecting) {
          eventReceived = true;
          expect(change.reason).toBeDefined();
          done();
        }
      });

      connection.connect().catch(() => {});
    });
  });

  describe('Configuration', () => {
    it('should update configuration dynamically', () => {
      connection.updateConfig({ heartbeatInterval: 60000 });
      expect(connection.connectionState).toBe(ConnectionState.Disconnected);
    });
  });

  describe('getStats', () => {
    it('should return correct stats in initial state', () => {
      const stats = connection.getStats();
      expect(stats.state).toBe(ConnectionState.Disconnected);
      expect(stats.reconnectAttempt).toBe(0);
      expect(stats.timeSinceLastPong).toBe(-1);
      expect(stats.isAwaitingPong).toBe(false);
    });
  });

  describe('Disconnect', () => {
    it('should set state to disconnected on manual disconnect', () => {
      connection.disconnect('Test disconnect');
      expect(connection.connectionState).toBe(ConnectionState.Disconnected);
    });

    it('should emit stateChange event when disconnecting from connecting state', (done) => {
      // First start connecting, then disconnect
      connection.on('stateChange', (change: ConnectionStateChange) => {
        if (change.currentState === ConnectionState.Disconnected &&
            change.previousState !== ConnectionState.Disconnected) {
          done();
        }
      });

      // Start connection then immediately disconnect
      connection.connect().catch(() => {});
      // Give time for the connecting state to be set
      setTimeout(() => {
        connection.disconnect('Test disconnect');
      }, 10);
    });
  });

  describe('Send', () => {
    it('should throw error when not connected', () => {
      expect(() => connection.send('test')).toThrow(McpUnityError);
    });
  });

  describe('WebSocket options', () => {
    it('sends the MCP client name as a header without setting WebSocket origin', async () => {
      const connectPromise = connection.connect();

      expect(mockWebSocketConstructor).toHaveBeenCalledWith(
        'ws://localhost:8090/McpUnity',
        {
          headers: {
            'X-Client-Name': 'TestClient'
          }
        }
      );

      const [, options] = mockWebSocketConstructor.mock.calls[0];
      expect(options).not.toHaveProperty('origin');

      mockWebSocketInstances[0].onopen();
      await connectPromise;
    });
  });

  describe('forceReconnect', () => {
    it('should trigger connecting state', () => {
      connection.forceReconnect();
      expect(connection.isConnecting).toBe(true);
    });
  });

  describe('WebSocket Event Handlers and Messages', () => {
    it('handles onmessage, onerror, and onclose events from socket', async () => {
      const connectPromise = connection.connect();
      const socket = mockWebSocketInstances[0];
      socket.onopen();
      await connectPromise;

      expect(connection.isConnected).toBe(true);

      // onmessage
      const messageSpy = jest.fn();
      connection.on('message', messageSpy);
      socket.onmessage({ data: '{"jsonrpc":"2.0","result":{}}' });
      expect(messageSpy).toHaveBeenCalledWith('{"jsonrpc":"2.0","result":{}}');

      // send
      connection.send('test payload');
      expect(socket.send).toHaveBeenCalledWith('test payload');

      // onerror
      const errorSpy = jest.fn();
      connection.on('error', errorSpy);
      socket.onerror(new Error('Socket fail'));
      expect(errorSpy).toHaveBeenCalled();

      // onclose
      socket.onclose({ code: 1006, reason: 'Abnormal closure' });
      expect(connection.connectionState).toBe(ConnectionState.Reconnecting);
    });

    it('throws error when send is called while not connected', () => {
      expect(() => connection.send('data')).toThrow(expect.objectContaining({
        type: ErrorType.CONNECTION
      }));
    });

    it('handles PlayMode close code and max reconnection limits', async () => {
      const conn = new UnityConnection(testLogger, {
        host: 'localhost',
        port: 8090,
        requestTimeout: 5000,
        maxReconnectAttempts: 1,
        minReconnectDelay: 50,
        maxReconnectDelay: 100
      });
      conn.on('error', () => {});

      const connectPromise = conn.connect();
      const socket = mockWebSocketInstances[mockWebSocketInstances.length - 1];
      socket.onopen();
      await connectPromise;

      // Simulate Play mode close code (4001)
      socket.onclose({ code: 4001, reason: 'Play mode enter' });
      expect((conn as any).isPlayModeReconnect).toBe(true);

      // Now test max reconnection attempts limit
      (conn as any).isPlayModeReconnect = false;
      (conn as any).reconnectAttempt = 1;
      (conn as any).handleConnectionFailure(new McpUnityError(ErrorType.CONNECTION, 'Fail'));
      expect(conn.connectionState).toBe(ConnectionState.Disconnected);

      conn.disconnect();
    });

    it('handles pong events and provides connection stats', async () => {
      const conn = new UnityConnection(testLogger, {
        host: 'localhost',
        port: 8090,
        requestTimeout: 5000,
        heartbeatInterval: 1000
      });
      conn.on('error', () => {});

      const connectPromise = conn.connect();
      const socket = mockWebSocketInstances[mockWebSocketInstances.length - 1];
      socket.onopen();
      await connectPromise;

      // Trigger pong listener
      const pongCallback = socket.on.mock.calls.find((c: any[]) => c[0] === 'pong')?.[1];
      if (pongCallback) {
        pongCallback();
      }

      const stats = conn.getStats();
      expect(conn.isConnected).toBe(true);
      expect(stats.state).toBe(ConnectionState.Connected);
      expect(typeof conn.timeSinceLastPong).toBe('number');

      conn.disconnect();
    });
  });
});

describe('ConnectionState Enum', () => {
  it('should have correct values', () => {
    expect(ConnectionState.Disconnected).toBe('disconnected');
    expect(ConnectionState.Connecting).toBe('connecting');
    expect(ConnectionState.Connected).toBe('connected');
    expect(ConnectionState.Reconnecting).toBe('reconnecting');
  });
});

describe('Exponential Backoff Configuration', () => {
  it('should accept backoff configuration', () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      minReconnectDelay: 1000,
      maxReconnectDelay: 30000,
      reconnectBackoffMultiplier: 2
    });

    expect(connection.connectionState).toBe(ConnectionState.Disconnected);
    connection.disconnect();
  });
});

describe('Connection timeout handling', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    mockWebSocketConstructor.mockImplementation(() => {
      const socket = createMockWebSocket({ readyState: 0 });
      mockWebSocketInstances.push(socket);
      return socket;
    });
    mockWebSocketConstructor.mockClear();
    mockWebSocketInstances.length = 0;
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('uses a dedicated connect timeout instead of the request timeout', async () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 60000,
      connectTimeout: 250,
      minReconnectDelay: 100,
      maxReconnectDelay: 1000,
      heartbeatInterval: 0
    });

    const connectPromise = connection.connect();
    const connectResult = expect(connectPromise).rejects.toMatchObject({
      type: ErrorType.CONNECTION,
      message: 'Connection timeout'
    });

    await jest.advanceTimersByTimeAsync(250);

    await connectResult;
    expect(mockWebSocketConstructor).toHaveBeenCalledTimes(1);
    expect(connection.connectionState).toBe(ConnectionState.Reconnecting);

    connection.disconnect();
  });
});
