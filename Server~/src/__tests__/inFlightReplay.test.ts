import { jest, describe, it, expect, beforeEach, afterEach } from '@jest/globals';
import { WebSocketServer } from 'ws';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger, LogLevel } from '../utils/logger.js';
import { ErrorType } from '../utils/errors.js';
import { isReplayableMethod } from '../unity/replayableMethods.js';

/**
 * Covers what happens to requests that are in flight when Unity drops the WebSocket - the domain
 * reload / Play mode transition case. Read-only requests should survive the reconnect and resolve
 * normally; state-mutating ones must fail fast rather than be silently re-applied or left to hang
 * until their (possibly very long) timeout.
 */
describe('In-flight request handling across a reconnect', () => {
  const logger = new Logger('ReplayTest', LogLevel.ERROR);

  let wss: WebSocketServer;
  let mcpUnity: McpUnity;
  let port: number;
  /** Set per-test: decides whether the server answers a request or drops the socket instead. */
  let onRequest: (request: any, socket: any) => void;

  beforeEach(async () => {
    wss = new WebSocketServer({ port: 0 });
    await new Promise<void>((resolve) => wss.once('listening', resolve));
    const address = wss.address();
    port = typeof address === 'object' && address ? address.port : 0;

    wss.on('connection', (socket) => {
      socket.on('message', (raw) => onRequest(JSON.parse(raw.toString()), socket));
    });

    process.env.UNITY_PORT = String(port);
    process.env.UNITY_HOST = 'localhost';

    mcpUnity = new McpUnity(logger);
    await mcpUnity.start('in-flight-replay-test');
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  afterEach(async () => {
    await mcpUnity.stop();
    await new Promise<void>((resolve) => wss.close(() => resolve()));
    delete process.env.UNITY_PORT;
    delete process.env.UNITY_HOST;
  });

  it('re-sends a read-only request after the socket drops and resolves it on the retry', async () => {
    let attempt = 0;

    onRequest = (request, socket) => {
      attempt++;
      if (attempt === 1) {
        // Simulate Unity tearing the connection down mid-request (domain reload).
        socket.terminate();
        return;
      }
      socket.send(JSON.stringify({
        id: request.id,
        result: { success: true, type: 'text', message: 'ok', name: 'Cube' }
      }));
    };

    const result = await mcpUnity.sendRequest({ method: 'get_gameobject', params: { idOrName: 'Cube' } });

    expect(result.name).toBe('Cube');
    expect(attempt).toBeGreaterThanOrEqual(2);
  }, 20000);

  it('fails a state-mutating request fast instead of re-sending it', async () => {
    let attempt = 0;

    onRequest = (_request, socket) => {
      attempt++;
      socket.terminate();
    };

    await expect(
      mcpUnity.sendRequest({ method: 'duplicate_gameobject', params: { instanceId: 1 } })
    ).rejects.toMatchObject({ type: ErrorType.CONNECTION });

    // Must not have been re-sent: Unity may already have applied it.
    expect(attempt).toBe(1);
  }, 20000);

  it('surfaces the ambiguity in the error message rather than claiming failure', async () => {
    onRequest = (_request, socket) => socket.terminate();

    await expect(
      mcpUnity.sendRequest({ method: 'delete_gameobject', params: { instanceId: 1 } })
    ).rejects.toThrow(/may or may not have been applied/);
  }, 20000);
});

describe('replayable method allowlist', () => {
  it('treats pure reads as replayable', () => {
    expect(isReplayableMethod('get_gameobject')).toBe(true);
    expect(isReplayableMethod('get_console_logs')).toBe(true);
    expect(isReplayableMethod('get_bounds')).toBe(true);
  });

  it('never replays mutations or long-running operations', () => {
    expect(isReplayableMethod('duplicate_gameobject')).toBe(false);
    expect(isReplayableMethod('delete_gameobject')).toBe(false);
    expect(isReplayableMethod('update_component')).toBe(false);
    expect(isReplayableMethod('batch_execute')).toBe(false);
    // Replaying these would restart a whole suite / trigger another reload.
    expect(isReplayableMethod('run_tests')).toBe(false);
    expect(isReplayableMethod('recompile_scripts')).toBe(false);
  });
});
