import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { registerGetMenuItemsResource } from '../resources/getMenuItemResource.js';
import { registerGetHierarchyResource } from '../resources/getScenesHierarchyResource.js';
import { registerGetPackagesResource } from '../resources/getPackagesResource.js';
import { registerGetAssetsResource } from '../resources/getAssetsResource.js';
import { registerGetTestsResource } from '../resources/getTestsResource.js';
import { registerGetGameObjectResource } from '../resources/getGameObjectResource.js';
import { registerGetConsoleLogsResource } from '../resources/getConsoleLogsResource.js';
import { registerGameObjectHandlingPrompt } from '../prompts/gameobjectHandlingPrompt.js';
import { registerUnityDashboardPrompt } from '../prompts/unityDashboardPrompt.js';

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

const mockServerResource = jest.fn();
const mockServerPrompt = jest.fn();
const mockServer = {
  resource: mockServerResource,
  prompt: mockServerPrompt
};

describe('Resources and Prompts', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('resources', () => {
    it('registers and executes menu-items resource', async () => {
      registerGetMenuItemsResource(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerResource).toHaveBeenCalledWith('get_menu_items', 'unity://menu-items', expect.any(Object), expect.any(Function));

      const handler = mockServerResource.mock.calls.find(c => c[0] === 'get_menu_items')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, menuItems: ['Assets/Refresh'] });

      const res = await handler();
      expect(res.contents[0].text).toContain('Assets/Refresh');
    });

    it('registers and executes scenes-hierarchy resource', async () => {
      registerGetHierarchyResource(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerResource.mock.calls.find(c => c[0] === 'get_scenes_hierarchy')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, hierarchy: [{ name: 'Main Camera' }] });

      const res = await handler();
      expect(res.contents[0].text).toContain('Main Camera');
    });

    it('registers and executes packages resource', async () => {
      registerGetPackagesResource(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerResource.mock.calls.find(c => c[0] === 'get_packages')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, projectPackages: [{ name: 'com.unity.ugui', version: '1.0.0' }] });

      const res = await handler();
      expect(res.contents[0].text).toContain('com.unity.ugui');
    });

    it('registers and executes assets resource', async () => {
      registerGetAssetsResource(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerResource.mock.calls.find(c => c[0] === 'get_assets')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, assets: [{ path: 'Assets/Scene.unity' }] });

      const res = await handler();
      expect(res.contents[0].text).toContain('Assets/Scene.unity');
    });

    it('registers and executes tests resource', async () => {
      registerGetTestsResource(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerResource.mock.calls.find(c => c[0] === 'get_tests')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, tests: [{ name: 'TestA', fullName: 'Tests.TestA', path: '', testMode: 'EditMode', runState: 'Runnable' }] });

      const res = await handler(new URL('unity://tests/EditMode'), { testMode: 'EditMode' });
      expect(res.contents[0].text).toContain('TestA');
    });

    it('registers and executes gameobject resource', async () => {
      registerGetGameObjectResource(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerResource.mock.calls.find(c => c[0] === 'get_gameobject')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, name: 'Player' });

      const res = await handler(new URL('unity://gameobject/Player'), { id: 'Player' });
      expect(res.contents[0].text).toContain('Player');
    });

    it('registers and executes console logs resource', async () => {
      registerGetConsoleLogsResource(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerResource.mock.calls.find(c => c[0] === 'get_console_logs')![3];
      mockSendRequest.mockResolvedValueOnce({ success: true, logs: [{ message: 'test log' }] });

      const res = await handler(new URL('unity://logs/all'), { logType: 'all' });
      expect(res.contents[0].text).toContain('test log');
    });
  });

  describe('prompts', () => {
    it('registers and executes gameobject_handling_strategy prompt', async () => {
      registerGameObjectHandlingPrompt(mockServer as any);
      expect(mockServerPrompt).toHaveBeenCalledWith('gameobject_handling_strategy', expect.any(String), expect.any(Object), expect.any(Function));

      const handler = mockServerPrompt.mock.calls.find(c => c[0] === 'gameobject_handling_strategy')![3];
      const res = await handler({ gameObjectIdOrName: 'Player' });
      expect(res.messages[0].content.text).toContain('When working directly with GameObjects');
    });

    it('registers and executes unity_dashboard prompt', async () => {
      registerUnityDashboardPrompt(mockServer as any);
      expect(mockServerPrompt).toHaveBeenCalledWith('unity_dashboard', expect.any(String), expect.any(Object), expect.any(Function));

      const handler = mockServerPrompt.mock.calls.find(c => c[0] === 'unity_dashboard')![3];
      const res = await handler({});
      expect(res.messages[0].content.text).toContain('Unity Dashboard');
    });
  });
});
