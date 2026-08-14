import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// apply_prefab_overrides
// ============================================================================

const applyPrefabToolName = 'apply_prefab_overrides';
const applyPrefabToolDescription = 'Applies modifications/overrides from a scene prefab instance back to its source Prefab Asset on disk.';
const applyPrefabParamsSchema = z.object({
  instanceId: z.number().optional().describe('The instance ID of the prefab instance or child GameObject'),
  objectPath: z.string().optional().describe('The hierarchy path of the prefab instance or child GameObject'),
  reason: z.string().optional().describe('Optional explanation of why overrides are being applied')
});

export function registerApplyPrefabOverridesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${applyPrefabToolName}`);

  server.tool(
    applyPrefabToolName,
    applyPrefabToolDescription,
    applyPrefabParamsSchema.shape,
    async (params: z.infer<typeof applyPrefabParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${applyPrefabToolName}`, params);
        const result = await applyPrefabToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${applyPrefabToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${applyPrefabToolName}`, error);
        throw error;
      }
    }
  );
}

async function applyPrefabToolHandler(mcpUnity: McpUnity, params: z.infer<typeof applyPrefabParamsSchema>): Promise<CallToolResult> {
  validateGameObjectIdentifier(params);

  const response = await mcpUnity.sendRequest({
    method: applyPrefabToolName,
    params
  }, { timeout: getToolTimeout(applyPrefabToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to apply prefab overrides`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully applied prefab overrides`
    }],
    structuredContent: {
      assetPath: response.assetPath,
      rootName: response.rootName,
      warning: response.warning
    }
  };
}

// ============================================================================
// revert_prefab_overrides
// ============================================================================

const revertPrefabToolName = 'revert_prefab_overrides';
const revertPrefabToolDescription = 'Reverts property and component overrides on a scene prefab instance back to the prefab asset defaults.';
const revertPrefabParamsSchema = z.object({
  instanceId: z.number().optional().describe('The instance ID of the prefab instance or child GameObject'),
  objectPath: z.string().optional().describe('The hierarchy path of the prefab instance or child GameObject'),
  reason: z.string().optional().describe('Optional explanation of why overrides are being reverted')
});

export function registerRevertPrefabOverridesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${revertPrefabToolName}`);

  server.tool(
    revertPrefabToolName,
    revertPrefabToolDescription,
    revertPrefabParamsSchema.shape,
    async (params: z.infer<typeof revertPrefabParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${revertPrefabToolName}`, params);
        const result = await revertPrefabToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${revertPrefabToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${revertPrefabToolName}`, error);
        throw error;
      }
    }
  );
}

async function revertPrefabToolHandler(mcpUnity: McpUnity, params: z.infer<typeof revertPrefabParamsSchema>): Promise<CallToolResult> {
  validateGameObjectIdentifier(params);

  const response = await mcpUnity.sendRequest({
    method: revertPrefabToolName,
    params
  }, { timeout: getToolTimeout(revertPrefabToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to revert prefab overrides`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully reverted prefab overrides`
    }],
    structuredContent: {
      rootName: response.rootName
    }
  };
}

// ============================================================================
// unpack_prefab
// ============================================================================

const unpackPrefabToolName = 'unpack_prefab';
const unpackPrefabToolDescription = 'Unpacks a prefab instance in the scene into regular GameObjects, disconnecting it from the prefab asset.';
const unpackPrefabParamsSchema = z.object({
  instanceId: z.number().optional().describe('The instance ID of the prefab instance or child GameObject'),
  objectPath: z.string().optional().describe('The hierarchy path of the prefab instance or child GameObject'),
  unpackMode: z.enum(['outermost', 'completely']).default('outermost').describe('Unpack mode: "outermost" unparents outermost prefab root, "completely" unpacks all nested prefabs recursively'),
  reason: z.string().optional().describe('Optional explanation of why the prefab is being unpacked')
});

export function registerUnpackPrefabTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${unpackPrefabToolName}`);

  server.tool(
    unpackPrefabToolName,
    unpackPrefabToolDescription,
    unpackPrefabParamsSchema.shape,
    async (params: z.infer<typeof unpackPrefabParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${unpackPrefabToolName}`, params);
        const result = await unpackPrefabToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${unpackPrefabToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${unpackPrefabToolName}`, error);
        throw error;
      }
    }
  );
}

async function unpackPrefabToolHandler(mcpUnity: McpUnity, params: z.infer<typeof unpackPrefabParamsSchema>): Promise<CallToolResult> {
  validateGameObjectIdentifier(params);

  const response = await mcpUnity.sendRequest({
    method: unpackPrefabToolName,
    params
  }, { timeout: getToolTimeout(unpackPrefabToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to unpack prefab`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully unpacked prefab`
    }],
    structuredContent: {
      rootName: response.rootName,
      unpackMode: response.unpackMode
    }
  };
}

function validateGameObjectIdentifier(params: { instanceId?: number; objectPath?: string }) {
  if (params.instanceId === undefined && !params.objectPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided."
    );
  }
}
