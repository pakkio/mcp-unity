import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { ErrorType } from '../utils/errors.js';
import {
  registerGetBoundsTool,
  registerPlaceNextToTool,
  registerFindLocalAssetsTool,
  registerImportLocalFileTool,
  registerMeasureDistanceTool,
  registerGetFloorHeightTool,
  registerGetNearbyObjectsTool,
  registerFrameCameraOnTool
} from '../tools/spatialTools.js';
import { registerTransformTools } from '../tools/transformTools.js';

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

describe('Spatial and Transform Tools', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('spatial tools', () => {
    it('registers and executes spatial tools', async () => {
      const mockServerTool = jest.fn();
      const mockServer = { tool: mockServerTool };

      registerGetBoundsTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      registerPlaceNextToTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      registerFindLocalAssetsTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      registerImportLocalFileTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      registerMeasureDistanceTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      registerGetFloorHeightTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      registerGetNearbyObjectsTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      registerFrameCameraOnTool(mockServer as any, mockMcpUnity as any, mockLogger as any);

      // get_bounds
      const getBoundsHandler = mockServerTool.mock.calls.find(c => c[0] === 'get_bounds')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Bounds found', center: { x: 0, y: 0, z: 0 }, size: { x: 1, y: 1, z: 1 } });
      const boundsRes = await getBoundsHandler({ objectPath: 'Cube' });
      expect(boundsRes.content[0].text).toContain('Bounds found');

      // place_next_to
      const placeHandler = mockServerTool.mock.calls.find(c => c[0] === 'place_next_to')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Placed next to' });
      const placeRes = await placeHandler({ objectPath: 'CubeA', referencePath: 'CubeB', direction: 'right' });
      expect(placeRes.content[0].text).toContain('Placed next to');

      // find_local_assets
      const findHandler = mockServerTool.mock.calls.find(c => c[0] === 'find_local_assets')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, assets: [{ path: 'Assets/test.mat' }] });
      const findRes = await findHandler({ query: 'test' });
      expect(findRes.content[0].text).toContain('Assets/test.mat');

      // import_local_file
      const importHandler = mockServerTool.mock.calls.find(c => c[0] === 'import_local_file')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Imported file' });
      const importRes = await importHandler({ sourcePath: '/tmp/test.png', destinationPath: 'Assets/test.png' });
      expect(importRes.content[0].text).toContain('Imported file');

      // measure_distance
      const measureHandler = mockServerTool.mock.calls.find(c => c[0] === 'measure_distance')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Distance measured', centerDistance: 5, edgeDistance: 4 });
      const measureRes = await measureHandler({ objectPath: 'CubeA', referencePath: 'CubeB' });
      expect(measureRes.content[0].text).toContain('centerDistance');

      // get_floor_height
      const floorHandler = mockServerTool.mock.calls.find(c => c[0] === 'get_floor_height')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Floor height found', hit: true, height: 0 });
      const floorRes = await floorHandler({ position: { x: 0, y: 5, z: 0 } });
      expect(floorRes.content[0].text).toContain('height');

      // get_nearby_objects
      const nearbyHandler = mockServerTool.mock.calls.find(c => c[0] === 'get_nearby_objects')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, objects: [{ name: 'Cube' }] });
      const nearbyRes = await nearbyHandler({ objectPath: 'Player', radius: 10 });
      expect(nearbyRes.content[0].text).toContain('Cube');

      // frame_camera_on
      const frameHandler = mockServerTool.mock.calls.find(c => c[0] === 'frame_camera_on')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Camera framed' });
      const frameRes = await frameHandler({ objectPath: 'Player' });
      expect(frameRes.content[0].text).toContain('Camera framed');
    });
  });

  describe('transform tools', () => {
    it('registers and executes transform tools (move, rotate, scale, set_transform)', async () => {
      const mockServerTool = jest.fn();
      const mockServer = { tool: mockServerTool };

      registerTransformTools(mockServer as any, mockMcpUnity as any, mockLogger as any);

      // move_gameobject
      const moveHandler = mockServerTool.mock.calls.find(c => c[0] === 'move_gameobject')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Moved GameObject' });
      const moveRes = await moveHandler({ objectPath: 'Player', position: { x: 1, y: 2, z: 3 } });
      expect(moveRes.content[0].text).toContain('Moved GameObject');

      // rotate_gameobject
      const rotHandler = mockServerTool.mock.calls.find(c => c[0] === 'rotate_gameobject')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Rotated GameObject' });
      const rotRes = await rotHandler({ objectPath: 'Player', rotation: { x: 0, y: 90, z: 0 } });
      expect(rotRes.content[0].text).toContain('Rotated GameObject');

      // scale_gameobject
      const scaleHandler = mockServerTool.mock.calls.find(c => c[0] === 'scale_gameobject')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Scaled GameObject' });
      const scaleRes = await scaleHandler({ objectPath: 'Player', scale: { x: 2, y: 2, z: 2 } });
      expect(scaleRes.content[0].text).toContain('Scaled GameObject');

      // set_transform
      const setHandler = mockServerTool.mock.calls.find(c => c[0] === 'set_transform')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, message: 'Set transform' });
      const setRes = await setHandler({ objectPath: 'Player', position: { x: 1, y: 1, z: 1 } });
      expect(setRes.content[0].text).toContain('Set transform');
    });

    it('validates identifiers and parameters in transform tools', async () => {
      const mockServerTool = jest.fn();
      const mockServer = { tool: mockServerTool };

      registerTransformTools(mockServer as any, mockMcpUnity as any, mockLogger as any);

      const moveHandler = mockServerTool.mock.calls.find(c => c[0] === 'move_gameobject')![3];
      await expect(moveHandler({ position: { x: 0, y: 0, z: 0 } })).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });

      const setHandler = mockServerTool.mock.calls.find(c => c[0] === 'set_transform')![3];
      await expect(setHandler({ objectPath: 'Player' })).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });
  });
});
