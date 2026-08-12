import { jest, describe, it, expect, beforeAll, afterAll } from '@jest/globals';
import { WebSocketServer } from 'ws';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger, LogLevel } from '../utils/logger.js';
import { registerGetBoundsTool } from '../tools/spatialTools.js';
import { registerBatchExecuteTool } from '../tools/batchExecuteTool.js';

/**
 * End-to-end test of the Node <-> Unity wire protocol, using a real WebSocket server that speaks
 * the same JSON-RPC-ish protocol as McpUnitySocketHandler.cs, instead of mocking
 * McpUnity.sendRequest directly like the other tool test suites do.
 *
 * Mocking sendRequest only exercises a tool's own response-shaping code with a response object
 * the test itself constructed - it can't catch a bug in how that object is produced or consumed
 * along the way. It would not have caught the real regression this test targets: spatialTools.ts's
 * callTool() discarded every structured field from Unity's response and returned only the
 * one-line message, so get_bounds/find_local_assets/measure_distance/get_floor_height/
 * get_nearby_objects all silently returned no usable data despite reporting success. This test
 * drives the real tool handlers through a real WebSocket round trip and asserts the structured
 * payload survives end to end - the class of bug a fully-mocked unit test is structurally unable
 * to catch.
 */
describe('Unity wire protocol integration', () => {
  let wss: WebSocketServer;
  let mcpUnity: McpUnity;
  let respond: (method: string, params: unknown) => unknown;

  const logger = new Logger('WireProtocolTest', LogLevel.ERROR);

  beforeAll(async () => {
    wss = new WebSocketServer({ port: 0 });
    await new Promise<void>((resolve) => wss.once('listening', resolve));
    const address = wss.address();
    const port = typeof address === 'object' && address ? address.port : 0;

    wss.on('connection', (socket) => {
      socket.on('message', (raw) => {
        const request = JSON.parse(raw.toString());
        const result = respond(request.method, request.params);
        socket.send(JSON.stringify({ id: request.id, result }));
      });
    });

    // Env var overrides take priority over any discovered McpUnitySettings.json (see
    // resolveUnityConnectionConfig), so this is safe regardless of what ancestor directories
    // the test happens to run under.
    process.env.UNITY_PORT = String(port);
    process.env.UNITY_HOST = 'localhost';

    mcpUnity = new McpUnity(logger);
    await mcpUnity.start('wire-protocol-integration-test');

    // Let the connection settle into Connected state before the first request.
    await new Promise((resolve) => setTimeout(resolve, 100));
  });

  afterAll(async () => {
    await mcpUnity.stop();
    await new Promise<void>((resolve) => wss.close(() => resolve()));
    delete process.env.UNITY_PORT;
    delete process.env.UNITY_HOST;
  });

  it('round-trips structured bounds data through get_bounds without losing fields', async () => {
    respond = (method) => {
      expect(method).toBe('get_bounds');
      return {
        success: true,
        type: 'text',
        message: "Bounds calculated for 'Cube'.",
        instanceId: 42,
        name: 'Cube',
        path: '/Cube',
        bounds: {
          center: { x: 0, y: 0.5, z: 0 },
          size: { x: 1, y: 1, z: 1 },
          min: { x: -0.5, y: 0, z: -0.5 },
          max: { x: 0.5, y: 1, z: 0.5 }
        }
      };
    };

    const mockServerTool = jest.fn();
    registerGetBoundsTool({ tool: mockServerTool } as any, mcpUnity, logger);
    const handler = mockServerTool.mock.calls[0][3] as (params: any) => Promise<any>;

    const result = await handler({ instanceId: 42 });

    expect(result.structuredContent).toBeDefined();
    expect(result.structuredContent.bounds.size).toEqual({ x: 1, y: 1, z: 1 });
    expect(result.structuredContent.instanceId).toBe(42);
    expect(result.content[0].text).toContain('"bounds"');
  });

  it('round-trips batch_execute results including successful operation payloads', async () => {
    respond = (method) => {
      expect(method).toBe('batch_execute');
      return {
        success: true,
        type: 'text',
        message: 'Successfully executed 1/1 operations.',
        results: [
          { index: 0, id: '0', success: true, result: { success: true, instanceId: 7, name: 'Sphere' } }
        ],
        summary: { total: 1, succeeded: 1, failed: 0, executed: 1 }
      };
    };

    const mockServerTool = jest.fn();
    registerBatchExecuteTool({ tool: mockServerTool } as any, mcpUnity, logger);
    const handler = mockServerTool.mock.calls[0][3] as (params: any) => Promise<any>;

    const result = await handler({ operations: [{ tool: 'get_gameobject', params: { idOrName: '7' } }] });

    expect(result.structuredContent.results[0].result.name).toBe('Sphere');
    expect(result.content[0].text).toContain('Sphere');
  });
});
