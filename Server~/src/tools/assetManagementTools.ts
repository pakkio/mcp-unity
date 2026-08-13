import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// Delete Asset Tool
const deleteToolName = 'delete_asset';
const deleteToolDescription = 'Deletes an asset or folder at the specified path and cleans up its corresponding .meta file';

const deleteParamsSchema = z.object({
  assetPath: z.string().describe("The path of the asset to delete (relative to project root, starting with 'Assets/')")
});

// Rename Asset Tool
const renameToolName = 'rename_asset';
const renameToolDescription = 'Renames an asset or folder at the specified path';

const renameParamsSchema = z.object({
  assetPath: z.string().describe("The path of the asset to rename (relative to project root, starting with 'Assets/')"),
  newName: z.string().describe("The new name for the asset (extension is optional and handled automatically)")
});

// Create Folder Tool
const createFolderToolName = 'create_folder';
const createFolderDescription = 'Creates a new folder at the specified parent path';

const createFolderParamsSchema = z.object({
  parentFolder: z.string().describe("The parent folder path (relative to project root, starting with 'Assets/')"),
  newFolderName: z.string().describe("The name of the new folder to create")
});

export function registerAssetManagementTools(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${deleteToolName}`);
  server.tool(
    deleteToolName,
    deleteToolDescription,
    deleteParamsSchema.shape,
    async (params: any): Promise<CallToolResult> => {
      try {
        logger.info(`Executing tool: ${deleteToolName}`, params);
        const result = await deleteAssetHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${deleteToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${deleteToolName}`, error);
        throw error;
      }
    }
  );

  logger.info(`Registering tool: ${renameToolName}`);
  server.tool(
    renameToolName,
    renameToolDescription,
    renameParamsSchema.shape,
    async (params: any): Promise<CallToolResult> => {
      try {
        logger.info(`Executing tool: ${renameToolName}`, params);
        const result = await renameAssetHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${renameToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${renameToolName}`, error);
        throw error;
      }
    }
  );

  logger.info(`Registering tool: ${createFolderToolName}`);
  server.tool(
    createFolderToolName,
    createFolderDescription,
    createFolderParamsSchema.shape,
    async (params: any): Promise<CallToolResult> => {
      try {
        logger.info(`Executing tool: ${createFolderToolName}`, params);
        const result = await createFolderHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${createFolderToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${createFolderToolName}`, error);
        throw error;
      }
    }
  );
}

async function deleteAssetHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.assetPath) {
    throw new McpUnityError(ErrorType.VALIDATION, "Parameter 'assetPath' is required");
  }

  const response = await mcpUnity.sendRequest({
    method: deleteToolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to delete asset'
    );
  }

  return {
    content: [{
      type: 'text' as const,
      text: response.message || 'Successfully deleted asset'
    }]
  };
}

async function renameAssetHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.assetPath || !params.newName) {
    throw new McpUnityError(ErrorType.VALIDATION, "Parameters 'assetPath' and 'newName' are required");
  }

  const response = await mcpUnity.sendRequest({
    method: renameToolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to rename asset'
    );
  }

  return {
    content: [{
      type: 'text' as const,
      text: response.message || 'Successfully renamed asset'
    }],
    structuredContent: {
      newName: response.newName,
      newPath: response.newPath
    }
  };
}

async function createFolderHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.parentFolder || !params.newFolderName) {
    throw new McpUnityError(ErrorType.VALIDATION, "Parameters 'parentFolder' and 'newFolderName' are required");
  }

  const response = await mcpUnity.sendRequest({
    method: createFolderToolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to create folder'
    );
  }

  return {
    content: [{
      type: 'text' as const,
      text: response.message || 'Successfully created folder'
    }],
    structuredContent: {
      guid: response.guid,
      folderPath: response.folderPath
    }
  };
}
