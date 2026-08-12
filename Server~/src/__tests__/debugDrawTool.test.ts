import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { ErrorType } from '../utils/errors.js';
import { registerDebugDrawTool } from '../tools/debugDrawTool.js';

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

describe('Debug Draw Tool', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('draw_scene_gizmo', () => {
    it('registers the tool with correct schema', () => {
      registerDebugDrawTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith(
        'draw_scene_gizmo',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
    });

    it('forwards debug draw request to Unity and returns confirmation message', async () => {
      registerDebugDrawTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'draw_scene_gizmo')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: true,
        message: "Successfully queued debug shape 'box' in Scene view for 5 seconds."
      });

      const result = await handler({
        shapeType: 'box',
        position: { x: 0, y: 0, z: 0 },
        size: { x: 1, y: 1, z: 1 },
        color: { r: 1, g: 0, b: 0, a: 1 },
        duration: 5
      });

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'draw_scene_gizmo',
        params: {
          shapeType: 'box',
          position: { x: 0, y: 0, z: 0 },
          size: { x: 1, y: 1, z: 1 },
          color: { r: 1, g: 0, b: 0, a: 1 },
          duration: 5
        }
      });
      expect(result.content[0].text).toContain("Successfully queued debug shape 'box'");
    });

    it('throws validation error when shapeType is missing', async () => {
      registerDebugDrawTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'draw_scene_gizmo')![3] as (params: any) => Promise<any>;

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });
  });
});
