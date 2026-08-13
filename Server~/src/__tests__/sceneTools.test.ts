import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { ErrorType } from '../utils/errors.js';
import { registerCreateSceneTool } from '../tools/createSceneTool.js';
import { registerDeleteSceneTool } from '../tools/deleteSceneTool.js';
import { registerLoadSceneTool } from '../tools/loadSceneTool.js';
import { registerSaveSceneTool } from '../tools/saveSceneTool.js';
import { registerUnloadSceneTool } from '../tools/unloadSceneTool.js';
import { registerGetSceneInfoTool } from '../tools/getSceneInfoTool.js';
import { registerGetEditorStateTool } from '../tools/getEditorStateTool.js';

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

describe('Scene Tools', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('create_scene', () => {
    it('registers and executes create_scene', async () => {
      registerCreateSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith('create_scene', expect.any(String), expect.any(Object), expect.any(Function));

      const handler = mockServerTool.mock.calls.find(call => call[0] === 'create_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Created scene', scenePath: 'Assets/Scenes/Test.unity' });

      const res = await handler({ sceneName: 'Test' });
      expect(res.content[0].text).toContain('Created scene');
    });

    it('throws error when create_scene fails', async () => {
      registerCreateSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'create_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false, message: 'Creation failed' });

      await expect(handler({ sceneName: 'Test' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('delete_scene', () => {
    it('registers and executes delete_scene', async () => {
      registerDeleteSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'delete_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Deleted scene' });

      const res = await handler({ scenePath: 'Assets/Scenes/Test.unity' });
      expect(res.content[0].text).toContain('Deleted scene');
    });

    it('throws error when delete_scene fails', async () => {
      registerDeleteSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'delete_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ scenePath: 'Assets/Scenes/Test.unity' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('load_scene', () => {
    it('registers and executes load_scene', async () => {
      registerLoadSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'load_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Loaded scene' });

      const res = await handler({ scenePath: 'Assets/Scenes/Test.unity' });
      expect(res.content[0].text).toContain('Loaded scene');
    });

    it('throws error when load_scene fails', async () => {
      registerLoadSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'load_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ scenePath: 'Assets/Scenes/Test.unity' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('save_scene', () => {
    it('registers and executes save_scene with auto fallback', async () => {
      registerSaveSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'save_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Saved scene', scenePath: 'Assets/Scenes/Untitled.unity' });

      const res = await handler({});
      expect(res.content[0].text).toContain('Saved scene');
      expect(res.structuredContent.scenePath).toBe('Assets/Scenes/Untitled.unity');
    });

    it('validates scenePath when saveAs is true', async () => {
      registerSaveSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'save_scene')![3] as (params: any) => Promise<any>;

      await expect(handler({ saveAs: true })).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });

    it('throws error when save_scene fails in Unity', async () => {
      registerSaveSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'save_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ scenePath: 'Assets/Test.unity' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('unload_scene', () => {
    it('registers and executes unload_scene', async () => {
      registerUnloadSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'unload_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Unloaded scene' });

      const res = await handler({ sceneName: 'Test' });
      expect(res.content[0].text).toContain('Unloaded scene');
    });

    it('throws error when unload_scene fails', async () => {
      registerUnloadSceneTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'unload_scene')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ sceneName: 'Test' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('get_scene_info', () => {
    it('registers and executes get_scene_info', async () => {
      registerGetSceneInfoTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'get_scene_info')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({
        success: true,
        activeScene: { name: 'Main', path: 'Assets/Main.unity', buildIndex: 0, isDirty: false, isLoaded: true, rootCount: 5 },
        loadedSceneCount: 2,
        loadedScenes: [
          { name: 'Main', path: 'Assets/Main.unity', isActive: true },
          { name: 'Secondary', path: 'Assets/Secondary.unity', isActive: false }
        ]
      });

      const res = await handler({});
      expect(res.content[0].text).toContain('Active Scene: Main');
      expect(res.content[0].text).toContain('Loaded Scenes (2)');
    });

    it('throws error when get_scene_info fails', async () => {
      registerGetSceneInfoTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'get_scene_info')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('get_editor_state', () => {
    it('registers and executes get_editor_state with modal and unsaved scenes', async () => {
      registerGetEditorStateTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'get_editor_state')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({
        success: true,
        hasModalWindow: true,
        modalWindowCount: 1,
        modalWindows: [{ title: 'Save Modified', typeName: 'SaveModifiedScenesPopup' }],
        focusedWindow: 'Save Modified',
        openWindowCount: 4,
        openWindows: [],
        isCompiling: false,
        isUpdating: false,
        isPlaying: false,
        isPaused: false,
        hasUnsavedChanges: true,
        unsavedSceneCount: 1,
        unsavedScenes: [{ name: 'Untitled', path: '' }]
      });

      const res = await handler({});
      expect(res.content[0].text).toContain('Has Modal Window: true');
      expect(res.content[0].text).toContain('Save Modified');
      expect(res.structuredContent.hasModalWindow).toBe(true);
    });

    it('throws error when get_editor_state fails', async () => {
      registerGetEditorStateTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'get_editor_state')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });
});
