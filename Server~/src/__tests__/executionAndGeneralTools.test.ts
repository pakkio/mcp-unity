import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { ErrorType } from '../utils/errors.js';
import { registerRunTestsTool } from '../tools/runTestsTool.js';
import { registerMenuItemTool } from '../tools/menuItemTool.js';
import { registerSendConsoleLogTool } from '../tools/sendConsoleLogTool.js';
import { registerRecompileScriptsTool } from '../tools/recompileScriptsTool.js';
import { registerExportPackageTool } from '../tools/exportPackageTool.js';
import { registerCaptureScreenshotTool } from '../tools/captureScreenshotTool.js';
import { registerAddPackageTool } from '../tools/addPackageTool.js';

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

describe('Execution and General Tools', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('run_tests', () => {
    it('registers and executes run_tests', async () => {
      registerRunTestsTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'run_tests')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({
        success: true,
        message: 'Tests completed',
        totalTests: 10,
        passed: 10,
        failed: 0,
        inconclusive: 0,
        skipped: 0
      });

      const res = await handler({ testMode: 'EditMode' });
      expect(res.content[0].text).toContain('Tests completed');
    });

    it('throws error when run_tests fails', async () => {
      registerRunTestsTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'run_tests')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('execute_menu_item', () => {
    it('registers and executes execute_menu_item', async () => {
      registerMenuItemTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'execute_menu_item')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Executed menu item' });

      const res = await handler({ menuItemPath: 'Assets/Refresh' });
      expect(res.content[0].text).toContain('Executed menu item');
    });

    it('throws error when execute_menu_item fails in Unity', async () => {
      registerMenuItemTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'execute_menu_item')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ menuItemPath: 'Assets/Invalid' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('send_console_log', () => {
    it('registers and executes send_console_log', async () => {
      registerSendConsoleLogTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'send_console_log')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Sent log' });

      const res = await handler({ message: 'Hello', logType: 'Log' });
      expect(res.content[0].text).toContain('Sent log');
    });

    it('throws error when send_console_log fails in Unity', async () => {
      registerSendConsoleLogTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'send_console_log')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({ message: 'Hello', logType: 'Log' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('recompile_scripts', () => {
    it('registers and executes recompile_scripts', async () => {
      registerRecompileScriptsTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'recompile_scripts')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Recompiled scripts' });

      const res = await handler({});
      expect(res.content[0].text).toContain('Recompiled scripts');
    });

    it('throws error when recompile_scripts fails in Unity', async () => {
      registerRecompileScriptsTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'recompile_scripts')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('export_package', () => {
    it('registers and executes export_package', async () => {
      registerExportPackageTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'export_package')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Exported package' });

      const res = await handler({ assetPaths: ['Assets/Test'], exportPath: 'test.unitypackage' });
      expect(res.content[0].text).toContain('Exported package');
    });

    it('validates parameters in export_package', async () => {
      registerExportPackageTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'export_package')![3] as (params: any) => Promise<any>;

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });
    });
  });

  describe('capture_screenshot', () => {
    it('registers and executes capture_screenshot', async () => {
      registerCaptureScreenshotTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'capture_screenshot')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Screenshot captured', imageBase64: 'abc123' });

      const res = await handler({ savePath: 'screenshot.png' });
      expect(res.content[0].text).toContain('Screenshot captured');
    });

    it('throws error when capture_screenshot fails', async () => {
      registerCaptureScreenshotTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'capture_screenshot')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: false });

      await expect(handler({})).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });

  describe('add_package', () => {
    it('registers and executes add_package', async () => {
      registerAddPackageTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'add_package')![3] as (params: any) => Promise<any>;
      mockSendRequest.mockResolvedValue({ success: true, message: 'Package added' });

      const res = await handler({ source: 'registry', packageName: 'com.unity.test-framework' });
      expect(res.content[0].text).toContain('Package added');
    });

    it('validates source parameters and failure in add_package', async () => {
      registerAddPackageTool(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'add_package')![3] as (params: any) => Promise<any>;

      await expect(handler({ source: 'registry' })).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });

      await expect(handler({ source: 'github' })).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });

      await expect(handler({ source: 'disk' })).rejects.toMatchObject({
        type: ErrorType.VALIDATION
      });

      mockSendRequest.mockResolvedValue({ success: false });
      await expect(handler({ source: 'registry', packageName: 'com.unity.test' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION
      });
    });
  });
});
