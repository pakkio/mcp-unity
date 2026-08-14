import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// manage_tags_and_layers
// ============================================================================

const toolName = 'manage_tags_and_layers';
const toolDescription = 'Gets or adds Unity Tags, Physics Layers (indices 8-31), Sorting Layers, configures Physics Layer Collision Matrix, and assigns layers to GameObjects recursively.';
const paramsSchema = z.object({
  action: z.enum([
    'get',
    'add_tag',
    'add_layer',
    'add_sorting_layer',
    'set_collision_matrix',
    'set_object_layer'
  ]).default('get').describe('The action to perform: "get" to list tags/layers, "add_tag", "add_layer", "add_sorting_layer", "set_collision_matrix" (configure layer collision physics), or "set_object_layer" (assign layer to GameObject & children).'),
  name: z.string().optional().describe('The name of the tag, layer, or sorting layer to add (required for add_* actions)'),
  layerIndex: z.number().int().min(0).max(31).optional().describe('Layer index (0-31) for add_layer or set_object_layer'),
  
  // set_object_layer parameters
  instanceId: z.number().optional().describe('Instance ID of the GameObject to set layer on'),
  objectPath: z.string().optional().describe('Hierarchy path of the GameObject to set layer on'),
  layer: z.string().optional().describe('Layer name to assign to the GameObject'),
  includeChildren: z.boolean().default(true).describe('Whether to apply the layer recursively to all child GameObjects (default: true)'),

  // set_collision_matrix parameters
  layerA: z.string().optional().describe('Name of the first layer for collision matrix rule'),
  layerAIndex: z.number().int().min(0).max(31).optional().describe('Index of first layer'),
  layerB: z.string().optional().describe('Name of the second layer for collision matrix rule'),
  layerBIndex: z.number().int().min(0).max(31).optional().describe('Index of second layer'),
  ignoreCollision: z.boolean().default(true).describe('True to ignore/disable physics collision between layerA and layerB, false to enable collision'),

  reason: z.string().optional().describe('Optional explanation of why the tag/layer is being modified')
});

export function registerManageTagsAndLayersTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${toolName}`);

  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: z.infer<typeof paramsSchema>) => {
      try {
        logger.info(`Executing tool: ${toolName}`, params);
        const result = await toolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${toolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${toolName}`, error);
        throw error;
      }
    }
  );
}

async function toolHandler(mcpUnity: McpUnity, params: z.infer<typeof paramsSchema>): Promise<CallToolResult> {
  if (['add_tag', 'add_layer', 'add_sorting_layer'].includes(params.action) && !params.name) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      `'name' is required when action is '${params.action}'.`
    );
  }

  if (params.action === 'set_object_layer' && params.instanceId === undefined && !params.objectPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' is required when action is 'set_object_layer'."
    );
  }

  if (params.action === 'set_collision_matrix' && !params.layerA && params.layerAIndex === undefined) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'layerA' or 'layerAIndex' is required when action is 'set_collision_matrix'."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to manage tags and layers`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully executed manage_tags_and_layers`
    }],
    structuredContent: {
      tags: response.tags,
      layers: response.layers,
      sortingLayers: response.sortingLayers,
      tag: response.tag,
      layerIndex: response.layerIndex,
      sortingLayerId: response.sortingLayerId,
      name: response.name,
      alreadyExisted: response.alreadyExisted,
      layerA: response.layerA,
      layerB: response.layerB,
      ignoreCollision: response.ignoreCollision,
      targetObject: response.targetObject,
      objectsUpdated: response.objectsUpdated
    }
  };
}
