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

// ============================================================================
// import_local_file Tool
// ============================================================================

const importName = 'import_local_file';
export function registerImportLocalFileTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  server.tool(importName, 'Copies a file from an external path into the Unity Assets folder and imports it. Returns the asset path.', {
    sourcePath: z.string().describe('Absolute path to the file to import (e.g. C:\\\\Users\\\\ Downloads\\\\model.glb)'),
    destFolder: z.string().default('Assets').describe('Assets-relative destination folder (default: Assets)'),
    newName: z.string().optional().describe('Optional new filename for the imported asset'),
    overwrite: z.boolean().default(false).describe('Overwrite if destination file already exists')
  }, async (params) => {
    if (!params.sourcePath || params.sourcePath.trim() === '') {
      throw new McpUnityError(ErrorType.VALIDATION, "'sourcePath' is required");
    }
    logger.info(`Executing tool: ${importName}`, params);
    return callTool(mcpUnity, importName, params, 'Failed to import local file');
  });
}

// ============================================================================
// measure_distance Tool
// ============================================================================

const measureName = 'measure_distance';
export function registerMeasureDistanceTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  server.tool(measureName, 'Measures distance between two GameObjects (center-to-center and bounds edge-to-edge).', {
    ...identifierShape,
    referenceId: z.number().optional().describe('Reference GameObject instance ID'),
    referencePath: z.string().optional().describe('Reference GameObject hierarchy path')
  }, async (params) => {
    validateIdentifier(params);
    if (params.referenceId === undefined && (!params.referencePath || params.referencePath.trim() === '')) {
      throw new McpUnityError(ErrorType.VALIDATION, "Either 'referenceId' or 'referencePath' must be provided");
    }
    logger.info(`Executing tool: ${measureName}`, params);
    return callTool(mcpUnity, measureName, params, 'Failed to measure distance');
  });
}

// ============================================================================
// get_floor_height Tool
// ============================================================================

const floorName = 'get_floor_height';
export function registerGetFloorHeightTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  server.tool(floorName, 'Raycasts downward from a position to find the floor/ground height. Returns the hit point and surface normal.', {
    position: z.object({
      x: z.number().default(0).describe('X position'),
      y: z.number().default(100).describe('Y position (raycast origin height)'),
      z: z.number().default(0).describe('Z position')
    }).optional().describe('Origin position for the downward raycast'),
    maxDistance: z.number().min(0.1).default(200).describe('Maximum raycast distance in meters')
  }, async (params) => {
    logger.info(`Executing tool: ${floorName}`, params);
    return callTool(mcpUnity, floorName, params, 'Failed to find floor height');
  });
}

// ============================================================================
// get_nearby_objects Tool
// ============================================================================

const nearbyName = 'get_nearby_objects';
export function registerGetNearbyObjectsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  server.tool(nearbyName, 'Finds all GameObjects with colliders within a radius of a position or GameObject.', {
    ...identifierShape,
    position: z.object({
      x: z.number().default(0).describe('X position'),
      y: z.number().default(0).describe('Y position'),
      z: z.number().default(0).describe('Z position')
    }).optional().describe('Center position (alternative to instanceId/objectPath)'),
    radius: z.number().min(0.1).default(5).describe('Search radius in meters'),
    maxResults: z.number().int().min(1).max(200).default(50).describe('Maximum number of results'),
    includeInactive: z.boolean().default(false).describe('Include inactive GameObjects')
  }, async (params) => {
    if (params.instanceId === undefined && (!params.objectPath || params.objectPath.trim() === '') && !params.position) {
      throw new McpUnityError(ErrorType.VALIDATION, "Either 'instanceId'/'objectPath' or 'position' must be provided");
    }
    logger.info(`Executing tool: ${nearbyName}`, params);
    return callTool(mcpUnity, nearbyName, params, 'Failed to find nearby objects');
  });
}

// ============================================================================
// frame_camera_on Tool
// ============================================================================

const frameName = 'frame_camera_on';
export function registerFrameCameraOnTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  server.tool(frameName, 'Moves the Scene view camera to frame a specific GameObject. Optionally sets a distance multiplier.', {
    ...identifierShape,
    distanceFactor: z.number().min(0.5).default(2.0).describe('Multiplier for camera distance from bounds radius'),
    minDistance: z.number().min(0.1).default(2).describe('Minimum camera distance in meters'),
    maxDistance: z.number().default(50).describe('Maximum camera distance in meters')
  }, async (params) => {
    validateIdentifier(params);
    logger.info(`Executing tool: ${frameName}`, params);
    return callTool(mcpUnity, frameName, params, 'Failed to frame camera on object');
  });
}

// ============================================================================
// Register all spatial tools
// ============================================================================

export function registerSpatialTools(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  registerGetBoundsTool(server, mcpUnity, logger);
  registerPlaceNextToTool(server, mcpUnity, logger);
  registerFindLocalAssetsTool(server, mcpUnity, logger);
  registerImportLocalFileTool(server, mcpUnity, logger);
  registerMeasureDistanceTool(server, mcpUnity, logger);
  registerGetFloorHeightTool(server, mcpUnity, logger);
  registerGetNearbyObjectsTool(server, mcpUnity, logger);
  registerFrameCameraOnTool(server, mcpUnity, logger);
}
