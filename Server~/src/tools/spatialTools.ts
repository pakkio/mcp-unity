import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';

const identifierShape = {
  instanceId: z.number().optional().describe('The GameObject instance ID'),
  objectPath: z.string().optional().describe('The GameObject hierarchy path')
};

function validateIdentifier(params: { instanceId?: number; objectPath?: string }) {
  if (params.instanceId === undefined && (!params.objectPath || params.objectPath.trim() === '')) {
    throw new McpUnityError(ErrorType.VALIDATION, "Either 'instanceId' or 'objectPath' must be provided");
  }
}

async function callTool(mcpUnity: McpUnity, method: string, params: any, failure: string) {
  const response = await mcpUnity.sendRequest({ method, params });
  if (!response.success) throw new McpUnityError(ErrorType.TOOL_EXECUTION, response.message || failure);
  return { content: [{ type: response.type, text: response.message || failure }] };
}

const boundsName = 'get_bounds';
export function registerGetBoundsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  server.tool(boundsName, 'Gets combined world-space bounds for a GameObject renderers or colliders.', {
    ...identifierShape,
    includeInactive: z.boolean().default(false).describe('Include inactive child renderers and colliders')
  }, async (params) => {
    validateIdentifier(params);
    logger.info(`Executing tool: ${boundsName}`, params);
    return callTool(mcpUnity, boundsName, params, 'Failed to calculate GameObject bounds');
  });
}

const placeName = 'place_next_to';
export function registerPlaceNextToTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  server.tool(placeName, 'Places one GameObject next to another using bounds and an edge-to-edge distance in meters.', {
    ...identifierShape,
    referenceId: z.number().optional().describe('Reference GameObject instance ID'),
    referencePath: z.string().optional().describe('Reference GameObject hierarchy path'),
    direction: z.enum(['left', 'right', 'forward', 'back', 'above', 'below']).default('right').describe('Direction relative to the reference object'),
    distance: z.number().min(0).default(0).describe('Gap between object bounds in meters'),
    useReferenceRotation: z.boolean().default(true).describe('Use the reference object orientation for horizontal directions'),
    reason: z.string().optional().describe('Optional explanation logged in Unity')
  }, async (params) => {
    validateIdentifier(params);
    if (params.referenceId === undefined && (!params.referencePath || params.referencePath.trim() === '')) {
      throw new McpUnityError(ErrorType.VALIDATION, "Either 'referenceId' or 'referencePath' must be provided");
    }
    logger.info(`Executing tool: ${placeName}`, params);
    return callTool(mcpUnity, placeName, params, 'Failed to place GameObject next to reference');
  });
}

const assetsName = 'find_local_assets';
export function registerFindLocalAssetsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  server.tool(assetsName, 'Finds local assets in the Unity project by name, type, or extension.', {
    query: z.string().default('').describe('Case-insensitive filename search text'),
    extension: z.string().optional().describe('Optional extension such as glb, fbx, or prefab'),
    root: z.string().default('Assets').describe('Assets-relative folder to search'),
    maxResults: z.number().int().min(1).max(500).default(100).describe('Maximum number of results')
  }, async (params) => {
    logger.info(`Executing tool: ${assetsName}`, params);
    return callTool(mcpUnity, assetsName, params, 'Failed to find local assets');
  });
}
