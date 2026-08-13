import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { ErrorType } from '../utils/errors.js';
import { registerBuildAndAnalysisTools } from '../tools/buildAndAnalysisTools.js';
import { TOOL_TIMEOUTS_MS } from '../utils/timeouts.js';

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

describe('Build and Analysis Tools', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('build_project', () => {
    it('registers the tool with correct schema', () => {
      registerBuildAndAnalysisTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith(
        'build_project',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
    });

    it('forwards build request to Unity and returns build details', async () => {
      registerBuildAndAnalysisTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'build_project')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: true,
        message: 'Successfully built project for StandaloneWindows64 at Builds/Win/Game.exe',
        totalErrors: 0,
        totalWarnings: 2,
        totalSize: 45000000,
        totalTimeSeconds: 12.5
      });

      const result = await handler({
        target: 'StandaloneWindows64',
        outputPath: 'Builds/Win/Game.exe',
        options: ['Development']
      });

      // A build blocks Unity's main thread for minutes, so it must override the default
      // request timeout (10s floor) or every call would reject before the build finishes.
      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'build_project',
        params: {
          target: 'StandaloneWindows64',
          outputPath: 'Builds/Win/Game.exe',
          options: ['Development']
        }
      }, { timeout: TOOL_TIMEOUTS_MS.build_project });
      expect(result.content[0].text).toBe('Successfully built project for StandaloneWindows64 at Builds/Win/Game.exe');
      expect(result.structuredContent).toEqual({
        totalErrors: 0,
        totalWarnings: 2,
        totalSize: 45000000,
        totalTimeSeconds: 12.5
      });
    });

    it('throws error when build fails in Unity', async () => {
      registerBuildAndAnalysisTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'build_project')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: false,
        message: 'Build failed with 1 errors.'
      });

      await expect(handler({
        target: 'StandaloneWindows64',
        outputPath: 'Builds/Win/Game.exe'
      })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION,
        message: 'Build failed with 1 errors.'
      });
    });
  });

  describe('get_compilation_errors', () => {
    it('registers the tool with correct schema', () => {
      registerBuildAndAnalysisTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith(
        'get_compilation_errors',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
    });

    it('forwards compilation query request to Unity and returns error list', async () => {
      registerBuildAndAnalysisTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'get_compilation_errors')![3] as (params: any) => Promise<any>;

      const errors = [
        { type: 'error', message: 'Assets/Player.cs(12,8): error CS0103: The name "x" does not exist in the current context' }
      ];

      mockSendRequest.mockResolvedValue({
        success: true,
        compilationFailed: true,
        errors
      });

      const result = await handler({});

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'get_compilation_errors',
        params: {}
      });
      expect(result.content[0].text).toContain('Compilation Failed: true. Found 1 compiler errors/warnings.');
      expect(result.structuredContent).toEqual({
        compilationFailed: true,
        errors
      });
    });
  });

  describe('find_script_references', () => {
    it('registers the tool with correct schema', () => {
      registerBuildAndAnalysisTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith(
        'find_script_references',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
    });

    it('forwards query to Unity and returns script reference references list', async () => {
      registerBuildAndAnalysisTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'find_script_references')![3] as (params: any) => Promise<any>;

      const references = [
        { gameObjectName: 'Player', gameObjectPath: 'MainScene/Player', instanceId: 101, enabled: true }
      ];

      mockSendRequest.mockResolvedValue({
        success: true,
        scriptName: 'PlayerController',
        referenceCount: 1,
        references
      });

      const result = await handler({ scriptName: 'PlayerController' });

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'find_script_references',
        params: { scriptName: 'PlayerController' }
      });
      expect(result.content[0].text).toContain("Found 1 GameObjects referencing script 'PlayerController'");
      expect(result.structuredContent).toEqual({
        scriptName: 'PlayerController',
        referenceCount: 1,
        references
      });
    });
  });
});
