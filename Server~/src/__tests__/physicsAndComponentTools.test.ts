import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { ErrorType } from '../utils/errors.js';
import { registerPhysicsAndComponentTools } from '../tools/physicsAndComponentTools.js';

const mockSendRequest = jest.fn();
const mockMcpUnity = {
  sendRequest: mockSendRequest
};

const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn()
};

const mockServerTool = jest.fn();
const mockServer = {
  tool: mockServerTool
};

describe('Physics and Component Tools', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('raycast_query', () => {
    it('registers the tool with correct schema', () => {
      registerPhysicsAndComponentTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith(
        'raycast_query',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
    });

    it('forwards raycast request to Unity and returns hit details when hit is true', async () => {
      registerPhysicsAndComponentTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'raycast_query')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: true,
        hit: true,
        distance: 5.5,
        point: { x: 0, y: 1.5, z: 5.5 },
        normal: { x: 0, y: 0, z: -1 },
        gameObjectName: 'Wall',
        gameObjectPath: 'Environment/Wall',
        instanceId: 202
      });

      const result = await handler({
        origin: { x: 0, y: 1.5, z: 0 },
        direction: { x: 0, y: 0, z: 1 },
        maxDistance: 10
      });

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'raycast_query',
        params: {
          origin: { x: 0, y: 1.5, z: 0 },
          direction: { x: 0, y: 0, z: 1 },
          maxDistance: 10
        }
      });
      expect(result.content[0].text).toContain("Raycast hit GameObject 'Wall' at distance 5.5m.");
      expect(result.data).toEqual(expect.objectContaining({
        hit: true,
        gameObjectName: 'Wall'
      }));
    });

    it('returns no hit message when hit is false', async () => {
      registerPhysicsAndComponentTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'raycast_query')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: true,
        hit: false
      });

      const result = await handler({
        origin: { x: 0, y: 0, z: 0 },
        direction: { x: 0, y: 1, z: 0 }
      });

      expect(result.content[0].text).toContain("Raycast did not hit any collider.");
    });
  });

  describe('copy_component', () => {
    it('registers the tool with correct schema', () => {
      registerPhysicsAndComponentTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith(
        'copy_component',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
    });

    it('forwards request to Unity and returns success', async () => {
      registerPhysicsAndComponentTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'copy_component')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: true,
        message: "Successfully pasted component values of 'BoxCollider' to 'Enemy'",
        action: 'pasted_values'
      });

      const result = await handler({
        componentType: 'BoxCollider',
        sourceObjectPath: 'Player',
        targetObjectPath: 'Enemy'
      });

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'copy_component',
        params: {
          componentType: 'BoxCollider',
          sourceObjectPath: 'Player',
          targetObjectPath: 'Enemy'
        }
      });
      expect(result.content[0].text).toContain("Successfully pasted component values of 'BoxCollider'");
      expect(result.data).toEqual({
        action: 'pasted_values'
      });
    });

    it('throws validation error when source parameters are missing', async () => {
      registerPhysicsAndComponentTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'copy_component')![3] as (params: any) => Promise<any>;

      await expect(handler({
        componentType: 'BoxCollider',
        targetObjectPath: 'Enemy'
      })).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });
  });
});
