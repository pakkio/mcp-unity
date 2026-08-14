import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

function createVector3Schema(description: string) {
  return z.object({
    x: z.number().describe('X component'),
    y: z.number().describe('Y component'),
    z: z.number().describe('Z component')
  }).describe(description);
}

// ============================================================================
// manage_occlusion_culling
// ============================================================================

const occlusionToolName = 'manage_occlusion_culling';
const occlusionToolDescription = 'Manages Occlusion Culling (bake, cancel, clear, status, setting Occluder/Occludee static flags, creating Occlusion Areas and Portals).';
const occlusionParamsSchema = z.object({
  action: z.enum([
    'get_status',
    'bake',
    'cancel',
    'clear',
    'set_static_flags',
    'create_occlusion_area',
    'create_occlusion_portal'
  ]).default('get_status').describe('Action to perform on Occlusion Culling system'),

  // Bake parameters
  smallestOccluder: z.number().min(0.1).optional().describe('Smallest occluder size in meters (default: 5.0)'),
  smallestHole: z.number().min(0.01).optional().describe('Smallest hole width in meters (default: 0.25)'),
  backfaceThreshold: z.number().min(5).max(100).optional().describe('Backface threshold percentage (default: 100)'),

  // Static flags parameters
  instanceId: z.number().optional().describe('Instance ID of GameObject to set static flags on'),
  objectPath: z.string().optional().describe('Hierarchy path of GameObject to set static flags on'),
  includeChildren: z.boolean().default(true).describe('Whether to apply flags recursively to child GameObjects'),
  occluder: z.boolean().optional().describe('Set Occluder Static flag (objects that block visibility, like walls/mountains)'),
  occludee: z.boolean().optional().describe('Set Occludee Static flag (objects that can be hidden behind occluders)'),
  contributeGI: z.boolean().optional().describe('Set Contribute GI static flag for lightmapping'),
  batching: z.boolean().optional().describe('Set Batching Static flag'),

  // Area & Portal parameters
  name: z.string().optional().describe('Name for created Occlusion Area or Portal'),
  center: createVector3Schema('Center position of the volume').optional(),
  size: createVector3Schema('Dimensions of the volume').optional(),
  open: z.boolean().optional().describe('For Occlusion Portal: whether the portal is currently open (lets light/visibility pass) or closed (occludes)'),

  reason: z.string().optional().describe('Optional explanation of why occlusion culling is being configured')
});

export function registerManageOcclusionCullingTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${occlusionToolName}`);

  server.tool(
    occlusionToolName,
    occlusionToolDescription,
    occlusionParamsSchema.shape,
    async (params: z.infer<typeof occlusionParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${occlusionToolName}`, params);
        const result = await occlusionToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${occlusionToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${occlusionToolName}`, error);
        throw error;
      }
    }
  );
}

async function occlusionToolHandler(mcpUnity: McpUnity, params: z.infer<typeof occlusionParamsSchema>): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: occlusionToolName,
    params
  }, { timeout: getToolTimeout(occlusionToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to execute manage_occlusion_culling`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully executed manage_occlusion_culling`
    }],
    structuredContent: {
      isBaking: response.isBaking,
      hasOcclusionData: response.hasOcclusionData,
      objectsUpdated: response.objectsUpdated,
      instanceId: response.instanceId,
      name: response.name,
      open: response.open
    }
  };
}

// ============================================================================
// configure_lod_group
// ============================================================================

const lodToolName = 'configure_lod_group';
const lodToolDescription = 'Creates or configures a LODGroup (Level of Detail) on a GameObject with custom screen percentage transitions (LOD0, LOD1, LOD2, Culled).';
const lodParamsSchema = z.object({
  instanceId: z.number().optional().describe('Instance ID of the GameObject containing the LOD levels'),
  objectPath: z.string().optional().describe('Hierarchy path of the GameObject containing the LOD levels'),
  fadeMode: z.enum(['none', 'crossfade', 'speedtree']).default('none').describe('LOD transition fade mode'),
  animateCrossFading: z.boolean().default(false).describe('Whether cross-fading is animated smoothly over time'),
  lods: z.array(z.object({
    screenRelativeTransitionHeight: z.number().min(0).max(1).describe('Screen height percentage (0-1) where this LOD tier is displayed'),
    renderers: z.array(z.string()).optional().describe('Hierarchy child paths of Renderers belonging to this LOD tier (auto-detected if omitted)')
  })).optional().describe('Custom LOD tiers definition. If omitted, automatically configures tiers from child objects named LOD0, LOD1, LOD2.'),
  reason: z.string().optional().describe('Optional explanation of why the LODGroup is being configured')
});

export function registerConfigureLODGroupTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${lodToolName}`);

  server.tool(
    lodToolName,
    lodToolDescription,
    lodParamsSchema.shape,
    async (params: z.infer<typeof lodParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${lodToolName}`, params);
        const result = await lodToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${lodToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${lodToolName}`, error);
        throw error;
      }
    }
  );
}

async function lodToolHandler(mcpUnity: McpUnity, params: z.infer<typeof lodParamsSchema>): Promise<CallToolResult> {
  if (params.instanceId === undefined && !params.objectPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: lodToolName,
    params
  }, { timeout: getToolTimeout(lodToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to configure LODGroup`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully configured LODGroup`
    }],
    structuredContent: {
      instanceId: response.instanceId,
      gameObjectName: response.gameObjectName,
      lodCount: response.lodCount,
      fadeMode: response.fadeMode,
      lods: response.lods
    }
  };
}

// ============================================================================
// configure_camera_culling
// ============================================================================

const cameraCullingToolName = 'configure_camera_culling';
const cameraCullingToolDescription = 'Configures Camera frustum & culling properties: FOV, orthographic size, layer draw distances (layerCullDistances), far clipping, and tests whether objects are inside the Camera Frustum.';
const cameraCullingParamsSchema = z.object({
  cameraName: z.string().optional().describe('Name of the Camera GameObject (defaults to Main Camera if omitted)'),
  instanceId: z.number().optional().describe('Instance ID of the Camera GameObject'),
  objectPath: z.string().optional().describe('Hierarchy path of the Camera GameObject'),
  fieldOfView: z.number().min(1).max(179).optional().describe('Vertical Field of View in degrees (for perspective camera)'),
  orthographic: z.boolean().optional().describe('Whether the camera projection is orthographic'),
  orthographicSize: z.number().min(0.01).optional().describe('Half-size of the camera in orthographic mode'),
  cullingMask: z.number().int().optional().describe('Layer culling bitmask determining which layers are rendered by this camera'),
  useOcclusionCulling: z.boolean().optional().describe('Enable or disable Occlusion Culling on this camera'),
  farClipPlane: z.number().min(1).optional().describe('Maximum rendering draw distance in meters (frustum far plane)'),
  nearClipPlane: z.number().min(0.01).optional().describe('Near clipping plane in meters (frustum near plane)'),
  layerCullDistances: z.array(z.object({
    layer: z.string().optional().describe('Layer name (e.g. "SmallProps", "Vegetation")'),
    layerIndex: z.number().int().min(0).max(31).optional().describe('Layer index (0-31)'),
    distance: z.number().min(0).describe('Maximum draw distance in meters for objects in this layer (0 uses default camera far clip)')
  })).optional().describe('Per-layer maximum draw distances to cull small props at closer ranges'),
  testTargetInstanceId: z.number().optional().describe('Optional instance ID of a target GameObject to test if it is currently inside the camera frustum'),
  testTargetObjectPath: z.string().optional().describe('Optional hierarchy path of a target GameObject to test if it is currently inside the camera frustum'),
  reason: z.string().optional().describe('Optional explanation of why camera culling/frustum is being configured')
});

export function registerConfigureCameraCullingTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${cameraCullingToolName}`);

  server.tool(
    cameraCullingToolName,
    cameraCullingToolDescription,
    cameraCullingParamsSchema.shape,
    async (params: z.infer<typeof cameraCullingParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${cameraCullingToolName}`, params);
        const result = await cameraCullingToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${cameraCullingToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${cameraCullingToolName}`, error);
        throw error;
      }
    }
  );
}

async function cameraCullingToolHandler(mcpUnity: McpUnity, params: z.infer<typeof cameraCullingParamsSchema>): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: cameraCullingToolName,
    params
  }, { timeout: getToolTimeout(cameraCullingToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to configure camera culling`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully configured camera culling`
    }],
    structuredContent: {
      cameraName: response.cameraName,
      fieldOfView: response.fieldOfView,
      orthographic: response.orthographic,
      orthographicSize: response.orthographicSize,
      useOcclusionCulling: response.useOcclusionCulling,
      farClipPlane: response.farClipPlane,
      nearClipPlane: response.nearClipPlane,
      cullingMask: response.cullingMask,
      testTarget: response.testTarget
    }
  };
}
