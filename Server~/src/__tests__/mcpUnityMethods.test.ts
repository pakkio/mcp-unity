import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { Logger, LogLevel } from '../utils/logger.js';
import { McpUnity, ConnectionState } from '../unity/mcpUnity.js';
import { ErrorType, McpUnityError } from '../utils/errors.js';

describe('McpUnity Advanced Methods and Branches', () => {
  let logger: Logger;
  let mcpUnity: McpUnity;

  beforeEach(() => {
    logger = new Logger('Test', LogLevel.NONE);
    mcpUnity = new McpUnity(logger, { queueingEnabled: true });
  });

  it('manages queueing settings, statistics, and command counts', () => {
    expect(mcpUnity.isQueueingEnabled).toBe(true);
    expect(mcpUnity.queuedCommandCount).toBe(0);

    const stats = mcpUnity.getQueueStats();
    expect(stats.size).toBe(0);

    mcpUnity.setQueueingEnabled(false);
    expect(mcpUnity.isQueueingEnabled).toBe(false);
  });

  it('returns connection metrics when connection is not yet started', () => {
    expect(mcpUnity.isConnected).toBe(false);
    expect(mcpUnity.isConnecting).toBe(false);

    const stats = mcpUnity.getConnectionStats();
    expect(stats.state).toBe(ConnectionState.Disconnected);
    expect(stats.pendingRequests).toBe(0);
  });

  it('adds and removes connection state listeners correctly', () => {
    const listener = jest.fn();
    const unsubscribe = mcpUnity.onConnectionStateChange(listener);
    expect(typeof unsubscribe).toBe('function');
    unsubscribe();
  });

  it('handles forceReconnect when connection is not started', () => {
    expect(() => mcpUnity.forceReconnect()).not.toThrow();
  });

  it('throws CONNECTION error when sending request while not connected and queueing is disabled', async () => {
    mcpUnity.setQueueingEnabled(false);
    await expect(mcpUnity.sendRequest({ method: 'test', params: {} })).rejects.toMatchObject({
      type: ErrorType.CONNECTION
    });
  });

  it('handles incoming messages, errors, and unknown request ids in handleMessage', () => {
    const anyMcp = mcpUnity as any;

    // 1. Invalid JSON
    expect(() => anyMcp.handleMessage('{invalid')).not.toThrow();

    // 2. Message without id
    expect(() => anyMcp.handleMessage('{"jsonrpc":"2.0"}')).not.toThrow();

    // 3. Message for unknown request
    expect(() => anyMcp.handleMessage('{"jsonrpc":"2.0","id":"unknown-1"}')).not.toThrow();

    // 4. Message with success result
    const resolve1 = jest.fn();
    const reject1 = jest.fn();
    anyMcp.pendingRequests.set('req-1', {
      resolve: resolve1,
      reject: reject1,
      timeout: setTimeout(() => {}, 10000),
      request: { id: 'req-1', method: 'test' }
    });
    anyMcp.handleMessage('{"jsonrpc":"2.0","id":"req-1","result":{"success":true}}');
    expect(resolve1).toHaveBeenCalledWith({ success: true });

    // 5. Message with error
    const resolve2 = jest.fn();
    const reject2 = jest.fn();
    anyMcp.pendingRequests.set('req-2', {
      resolve: resolve2,
      reject: reject2,
      timeout: setTimeout(() => {}, 10000),
      request: { id: 'req-2', method: 'test' }
    });
    anyMcp.handleMessage('{"jsonrpc":"2.0","id":"req-2","error":{"message":"Failed"}}');
    expect(reject2).toHaveBeenCalledWith(expect.objectContaining({
      type: ErrorType.TOOL_EXECUTION,
      message: 'Failed'
    }));
  });

  it('handles state changes: disconnection, max reconnection, and pending request rejection', () => {
    const anyMcp = mcpUnity as any;

    const resolve = jest.fn();
    const reject = jest.fn();
    anyMcp.pendingRequests.set('req-3', {
      resolve,
      reject,
      timeout: setTimeout(() => {}, 10000),
      request: { id: 'req-3', method: 'test' }
    });

    anyMcp.handleStateChange({
      previousState: ConnectionState.Connected,
      currentState: ConnectionState.Disconnected,
      reason: 'Max reconnection attempts reached'
    });

    expect(reject).toHaveBeenCalledWith(expect.objectContaining({
      type: ErrorType.CONNECTION
    }));
    expect(anyMcp.pendingRequests.size).toBe(0);
  });

  it('handles state listeners error safely', () => {
    const anyMcp = mcpUnity as any;
    anyMcp.stateListeners.add(() => {
      throw new Error('Listener error');
    });

    expect(() => anyMcp.handleStateChange({
      previousState: ConnectionState.Disconnected,
      currentState: ConnectionState.Connecting
    })).not.toThrow();
  });

  it('sends request when connected with mock connection', async () => {
    const anyMcp = mcpUnity as any;
    const mockSend = jest.fn();
    anyMcp.connection = {
      isConnected: true,
      isConnecting: false,
      send: mockSend,
      getStats: () => ({ isConnected: true, state: ConnectionState.Connected })
    };

    const requestPromise = mcpUnity.sendRequest({ method: 'get_bounds', params: { objectPath: 'Cube' } });
    expect(mockSend).toHaveBeenCalledWith(expect.stringContaining('get_bounds'));

    // Resolve the request
    const requestId = Array.from(anyMcp.pendingRequests.keys())[0] as string;
    anyMcp.handleMessage(JSON.stringify({ jsonrpc: '2.0', id: requestId, result: { success: true } }));

    const res = await requestPromise;
    expect(res).toEqual({ success: true });
  });

  it('replays in-flight read-only requests upon reconnect', () => {
    const anyMcp = mcpUnity as any;
    const mockSend = jest.fn();
    anyMcp.connection = {
      isConnected: true,
      send: mockSend
    };

    const resolve = jest.fn();
    const reject = jest.fn();
    anyMcp.pendingRequests.set('req-read', {
      resolve,
      reject,
      timeout: setTimeout(() => {}, 10000),
      request: { id: 'req-read', method: 'get_bounds' }
    });
    anyMcp.pendingReplay = [{ id: 'req-read', method: 'get_bounds' }];

    anyMcp.replayInFlightRequests();
    expect(mockSend).toHaveBeenCalledWith(expect.stringContaining('req-read'));
  });

  it('triages mutations immediately during reconnect while parking read requests', () => {
    const anyMcp = mcpUnity as any;
    const resolveRead = jest.fn();
    const rejectRead = jest.fn();
    const resolveMut = jest.fn();
    const rejectMut = jest.fn();

    anyMcp.pendingRequests.set('req-read-2', {
      resolve: resolveRead,
      reject: rejectRead,
      timeout: setTimeout(() => {}, 10000),
      request: { id: 'req-read-2', method: 'get_bounds' }
    });
    anyMcp.pendingRequests.set('req-mut', {
      resolve: resolveMut,
      reject: rejectMut,
      timeout: setTimeout(() => {}, 10000),
      request: { id: 'req-mut', method: 'create_prefab' }
    });

    anyMcp.triageInFlightRequests('socket dropped');

    expect(rejectMut).toHaveBeenCalledWith(expect.objectContaining({
      type: ErrorType.CONNECTION
    }));
    expect(anyMcp.pendingReplay).toEqual(expect.arrayContaining([
      expect.objectContaining({ method: 'get_bounds' })
    ]));
  });

  it('queues requests when disconnected and replaying queue executes them', async () => {
    const anyMcp = mcpUnity as any;
    mcpUnity.setQueueingEnabled(true);
    anyMcp.connection = {
      isConnected: false,
      isConnecting: true,
      connectionState: ConnectionState.Connecting,
      send: jest.fn(),
      getStats: () => ({ isConnected: false, state: ConnectionState.Connecting })
    };

    const queuedPromise = mcpUnity.sendRequest({ method: 'get_bounds', params: { objectPath: 'Cube' } });
    expect(mcpUnity.queuedCommandCount).toBe(1);

    // Mock connected state and sendRequestInternal
    anyMcp.connection = {
      isConnected: true,
      isConnecting: false,
      connectionState: ConnectionState.Connected,
      send: jest.fn(),
      getStats: () => ({ isConnected: true, state: ConnectionState.Connected })
    };
    anyMcp.sendRequestInternal = jest.fn().mockResolvedValue({ success: true });

    await anyMcp.replayQueuedCommands();
    expect(mcpUnity.queuedCommandCount).toBe(0);
    await expect(queuedPromise).resolves.toEqual({ success: true });
  });

  it('handles stop cleanly', async () => {
    await expect(mcpUnity.stop()).resolves.toBeUndefined();
  });
});
