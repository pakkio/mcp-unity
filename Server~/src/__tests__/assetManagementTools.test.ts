import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { ErrorType } from '../utils/errors.js';
import { registerAssetManagementTools } from '../tools/assetManagementTools.js';

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

describe('Asset Management Tools', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('delete_asset', () => {
    it('registers the tool with correct schema', () => {
      registerAssetManagementTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith(
        'delete_asset',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
    });

    it('forwards delete request to Unity and returns text confirmation', async () => {
      registerAssetManagementTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      // delete_asset is registered first
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'delete_asset')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: true,
        message: 'Successfully deleted asset at Assets/test.txt'
      });

      const result = await handler({ assetPath: 'Assets/test.txt' });

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'delete_asset',
        params: { assetPath: 'Assets/test.txt' }
      });
      expect(result.content[0].text).toBe('Successfully deleted asset at Assets/test.txt');
    });

    it('throws error when delete fails in Unity', async () => {
      registerAssetManagementTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'delete_asset')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: false,
        message: 'File is write-protected'
      });

      await expect(handler({ assetPath: 'Assets/test.txt' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION,
        message: 'File is write-protected'
      });
    });
  });

  describe('rename_asset', () => {
    it('registers the tool with correct schema', () => {
      registerAssetManagementTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith(
        'rename_asset',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
    });

    it('forwards rename request to Unity and returns new path details', async () => {
      registerAssetManagementTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'rename_asset')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: true,
        message: "Successfully renamed asset at 'Assets/old.png' to 'new'",
        newName: 'new',
        newPath: 'Assets/new.png'
      });

      const result = await handler({ assetPath: 'Assets/old.png', newName: 'new' });

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'rename_asset',
        params: { assetPath: 'Assets/old.png', newName: 'new' }
      });
      expect(result.content[0].text).toBe("Successfully renamed asset at 'Assets/old.png' to 'new'");
      expect(result.structuredContent).toEqual({
        newName: 'new',
        newPath: 'Assets/new.png'
      });
    });

    it('throws error when rename fails in Unity', async () => {
      registerAssetManagementTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'rename_asset')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: false,
        message: 'Name already exists'
      });

      await expect(handler({ assetPath: 'Assets/old.png', newName: 'new' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION,
        message: 'Name already exists'
      });
    });
  });

  describe('create_folder', () => {
    it('registers the tool with correct schema', () => {
      registerAssetManagementTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      expect(mockServerTool).toHaveBeenCalledWith(
        'create_folder',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
    });

    it('forwards folder creation to Unity and returns path and guid', async () => {
      registerAssetManagementTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'create_folder')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: true,
        message: "Successfully created folder 'Sub' in 'Assets'",
        guid: 'mock-guid-123',
        folderPath: 'Assets/Sub'
      });

      const result = await handler({ parentFolder: 'Assets', newFolderName: 'Sub' });

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'create_folder',
        params: { parentFolder: 'Assets', newFolderName: 'Sub' }
      });
      expect(result.content[0].text).toBe("Successfully created folder 'Sub' in 'Assets'");
      expect(result.structuredContent).toEqual({
        guid: 'mock-guid-123',
        folderPath: 'Assets/Sub'
      });
    });

    it('throws error when folder creation fails in Unity', async () => {
      registerAssetManagementTools(mockServer as any, mockMcpUnity as any, mockLogger as any);
      const handler = mockServerTool.mock.calls.find(call => call[0] === 'create_folder')![3] as (params: any) => Promise<any>;

      mockSendRequest.mockResolvedValue({
        success: false,
        message: 'Parent path not found'
      });

      await expect(handler({ parentFolder: 'Assets/NonExistent', newFolderName: 'Sub' })).rejects.toMatchObject({
        type: ErrorType.TOOL_EXECUTION,
        message: 'Parent path not found'
      });
    });
  });
});
