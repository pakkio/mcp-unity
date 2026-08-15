import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { ErrorType } from '../utils/errors.js';
import {
  registerDuplicateGameObjectTool,
  registerDeleteGameObjectTool,
  registerReparentGameObjectTool,
  registerSetSiblingIndexTool
} from '../tools/gameObjectTools.js';
import { registerGetGameObjectTool } from '../tools/getGameObjectTool.js';
import { registerUpdateGameObjectTool } from '../tools/updateGameObjectTool.js';
import { registerUpdateComponentTool } from '../tools/updateComponentTool.js';
import { registerRemoveComponentTool } from '../tools/removeComponentTool.js';
import { registerSelectGameObjectTool } from '../tools/selectGameObjectTool.js';
import { registerCreatePrefabTool } from '../tools/createPrefabTool.js';
import { registerAddAssetToSceneTool } from '../tools/addAssetToSceneTool.js';

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

describe('GameObject and Component Tools', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('duplicate_gameobject', () => {
    it('registers and executes duplicate_gameobject', async () => {
      registerDuplicateGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'duplicate_gameobject')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Duplicated object', instanceID: 101, name: 'Cube_Copy' });

      const res = await handler({ instanceId: 100, newName: 'Cube_Copy' });
      expect(res.content[0].text).toContain('Duplicated object');
    });

    it('validates required target parameters', async () => {
      registerDuplicateGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'duplicate_gameobject')![3] as (params: any) => Promise<any>;

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });
  });

  describe('delete_gameobject', () => {
    it('registers and executes delete_gameobject', async () => {
      registerDeleteGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'delete_gameobject')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Deleted object' });

      const res = await handler({ objectPath: 'Cube' });
      expect(res.content[0].text).toContain('Deleted object');
    });

    it('validates parameters and handles failure for delete_gameobject', async () => {
      registerDeleteGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'delete_gameobject')![3] as (params: any) => Promise<any>;

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });

      mockSendRequest.mockResolvedValue({ success: false });
      await expect(handler({ objectPath: 'Missing' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('reparent_gameobject', () => {
    it('registers and executes reparent_gameobject', async () => {
      registerReparentGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'reparent_gameobject')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Reparented object' });

      const res = await handler({ objectPath: 'Child', parentObjectPath: 'Parent' });
      expect(res.content[0].text).toContain('Reparented object');
    });

    it('validates parameters and handles failure for reparent_gameobject', async () => {
      registerReparentGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'reparent_gameobject')![3] as (params: any) => Promise<any>;

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });

      mockSendRequest.mockResolvedValue({ success: false });
      await expect(handler({ objectPath: 'Child' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('set_sibling_index', () => {
    it('registers and executes set_sibling_index', async () => {
      registerSetSiblingIndexTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'set_sibling_index')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Set sibling index' });

      const res = await handler({ objectPath: 'Child', siblingIndex: 0 });
      expect(res.content[0].text).toContain('Set sibling index');
    });

    it('validates parameters and handles failure for set_sibling_index', async () => {
      registerSetSiblingIndexTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'set_sibling_index')![3] as (params: any) => Promise<any>;

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });

      mockSendRequest.mockResolvedValue({ success: false });
      await expect(handler({ objectPath: 'Child', siblingIndex: 1 })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('get_gameobject', () => {
    it('registers and executes get_gameobject', async () => {
      registerGetGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'get_gameobject')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({
        success: true,
        name: 'Player',
        instanceID: 42,
        activeSelf: true,
        tag: 'PlayerTag',
        layer: 'Default',
        transform: { position: { x: 0, y: 1, z: 0 } },
        components: [{ type: 'Transform' }]
      });

      const res = await handler({ idOrName: 'Player' });
      expect(res.content[0].text).toContain('Player');
    });

    it('handles failure for get_gameobject', async () => {
      registerGetGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'get_gameobject')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ idOrName: 'Missing' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('update_gameobject', () => {
    it('registers and executes update_gameobject', async () => {
      registerUpdateGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'update_gameobject')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Updated GameObject' });

      const res = await handler({ objectPath: 'Player', isStatic: true });
      expect(res.content[0].text).toContain('Updated GameObject');
    });

    it('validates target parameters in update_gameobject', async () => {
      registerUpdateGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'update_gameobject')![3] as (params: any) => Promise<any>;

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });
    it('handles failure for update_gameobject', async () => {
      registerUpdateGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'update_gameobject')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ objectPath: 'Player', isStatic: true })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('update_component', () => {
    it('registers and executes update_component', async () => {
      registerUpdateComponentTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'update_component')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Updated Component' });

      const res = await handler({ objectPath: 'Player', componentName: 'BoxCollider', propertyValues: { isTrigger: true } });
      expect(res.content[0].text).toContain('Updated Component');
    });

    it('validates required parameters in update_component', async () => {
      registerUpdateComponentTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'update_component')![3] as (params: any) => Promise<any>;

      await expect(handler({ objectPath: 'Player' })).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });

    it('handles failure for update_component', async () => {
      registerUpdateComponentTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'update_component')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ objectPath: 'Player', componentName: 'BoxCollider', propertyValues: { isTrigger: true } })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('remove_component', () => {
    it('registers and executes remove_component', async () => {
      registerRemoveComponentTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'remove_component')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Removed Component' });

      const res = await handler({ objectPath: 'Player', componentName: 'CircleDriver' });
      expect(res.content[0].text).toContain('Removed Component');
    });

    it('validates required parameters in remove_component', async () => {
      registerRemoveComponentTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'remove_component')![3] as (params: any) => Promise<any>;

      await expect(handler({ objectPath: 'Player' })).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });

    it('handles failure for remove_component', async () => {
      registerRemoveComponentTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'remove_component')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ objectPath: 'Player', componentName: 'CircleDriver' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('select_gameobject', () => {
    it('registers and executes select_gameobject', async () => {
      registerSelectGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'select_gameobject')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Selected GameObject' });

      const res = await handler({ objectName: 'Player' });
      expect(res.content[0].text).toContain('Selected GameObject');
    });

    it('validates required parameters in select_gameobject', async () => {
      registerSelectGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'select_gameobject')![3] as (params: any) => Promise<any>;

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });

    it('handles failure for select_gameobject', async () => {
      registerSelectGameObjectTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'select_gameobject')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ objectName: 'Missing' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('create_prefab', () => {
    it('registers and executes create_prefab', async () => {
      registerCreatePrefabTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'create_prefab')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Created prefab' });

      const res = await handler({ prefabName: 'MyPrefab', savePath: 'Assets/Prefabs/MyPrefab.prefab' });
      expect(res.content[0].text).toContain('Created prefab');
    });

    it('handles failure for create_prefab', async () => {
      registerCreatePrefabTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'create_prefab')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ prefabName: 'MyPrefab' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('add_asset_to_scene', () => {
    it('registers and executes add_asset_to_scene', async () => {
      registerAddAssetToSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'add_asset_to_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Added asset to scene' });

      const res = await handler({ assetPath: 'Assets/Prefabs/MyPrefab.prefab' });
      expect(res.content[0].text).toContain('Added asset to scene');
    });

    it('handles failure for add_asset_to_scene', async () => {
      registerAddAssetToSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'add_asset_to_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ assetPath: 'Assets/Missing.prefab' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });
});
