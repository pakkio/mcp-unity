import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

function createColorSchema(description: string) {
  return z.object({
    r: z.number().describe('Red (0-1)'),
    g: z.number().describe('Green (0-1)'),
    b: z.number().describe('Blue (0-1)'),
    a: z.number().default(1).describe('Alpha (0-1)')
  }).describe(description);
}

function createVector3Schema(description: string) {
  return z.object({
    x: z.number().describe('X component'),
    y: z.number().describe('Y component'),
    z: z.number().describe('Z component')
  }).describe(description);
}

// ============================================================================
// manage_lighting
// ============================================================================

const manageLightingToolName = 'manage_lighting';
const manageLightingToolDescription = 'Manages scene illumination, ambient lighting, skybox settings, and lightmap baking (bake, cancel, clear, status).';
const manageLightingParamsSchema = z.object({
  action: z.enum(['get_status', 'set_environment', 'bake', 'cancel_bake', 'clear_bake']).default('get_status').describe('The lighting action to perform: "get_status" to inspect, "set_environment" to configure ambient/skybox, "bake" to start lightmapping, "cancel_bake" to cancel, "clear_bake" to clear baked lightmaps.'),
  ambientMode: z.enum(['Skybox', 'Trilight', 'Flat']).optional().describe('Ambient illumination mode'),
  ambientIntensity: z.number().min(0).max(8).optional().describe('Ambient lighting intensity multiplier'),
  reflectionIntensity: z.number().min(0).max(1).optional().describe('Environment reflection intensity multiplier'),
  skyboxMaterial: z.string().optional().describe('Asset path or GUID of the skybox material'),
  sunLight: z.string().optional().describe('Hierarchy path of the directional sun Light'),
  sunLightId: z.number().optional().describe('Instance ID of the directional sun Light'),
  ambientColor: createColorSchema('Flat ambient light color').optional(),
  ambientSkyColor: createColorSchema('Trilight gradient sky ambient color').optional(),
  ambientEquatorColor: createColorSchema('Trilight gradient equator ambient color').optional(),
  ambientGroundColor: createColorSchema('Trilight gradient ground ambient color').optional(),
  reason: z.string().optional().describe('Optional explanation of why lighting is being modified')
});

export function registerManageLightingTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${manageLightingToolName}`);

  server.tool(
    manageLightingToolName,
    manageLightingToolDescription,
    manageLightingParamsSchema.shape,
    async (params: z.infer<typeof manageLightingParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${manageLightingToolName}`, params);
        const result = await manageLightingToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${manageLightingToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${manageLightingToolName}`, error);
        throw error;
      }
    }
  );
}

async function manageLightingToolHandler(mcpUnity: McpUnity, params: z.infer<typeof manageLightingParamsSchema>): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: manageLightingToolName,
    params
  }, { timeout: getToolTimeout(manageLightingToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to execute manage_lighting`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully executed manage_lighting`
    }],
    structuredContent: {
      isBaking: response.isBaking,
      lightmapCount: response.lightmapCount,
      ambientMode: response.ambientMode,
      ambientIntensity: response.ambientIntensity,
      reflectionIntensity: response.reflectionIntensity,
      skybox: response.skybox,
      sun: response.sun
    }
  };
}

// ============================================================================
// configure_light_probe_group
// ============================================================================

const probeGroupToolName = 'configure_light_probe_group';
const probeGroupToolDescription = 'Creates or populates a LightProbeGroup with an automated 3D grid layout of light probes for indirect dynamic lighting.';
const probeGroupParamsSchema = z.object({
  instanceId: z.number().optional().describe('Instance ID of an existing LightProbeGroup GameObject to update'),
  objectPath: z.string().optional().describe('Hierarchy path of an existing LightProbeGroup GameObject to update'),
  name: z.string().optional().describe('Name of the GameObject if creating a new LightProbeGroup (default: "LightProbeGroup")'),
  center: createVector3Schema('Center of the light probe volume (default: { x: 0, y: 1.5, z: 0 })').optional(),
  size: createVector3Schema('Dimensions of the probe grid volume (default: { x: 10, y: 3, z: 10 })').optional(),
  spacing: createVector3Schema('Spacing between probes on each axis in world units (default: { x: 2.5, y: 1.5, z: 2.5 })').optional(),
  reason: z.string().optional().describe('Optional explanation of why light probes are being configured')
});

export function registerConfigureLightProbeGroupTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${probeGroupToolName}`);

  server.tool(
    probeGroupToolName,
    probeGroupToolDescription,
    probeGroupParamsSchema.shape,
    async (params: z.infer<typeof probeGroupParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${probeGroupToolName}`, params);
        const result = await configureLightProbeGroupToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${probeGroupToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${probeGroupToolName}`, error);
        throw error;
      }
    }
  );
}

async function configureLightProbeGroupToolHandler(mcpUnity: McpUnity, params: z.infer<typeof probeGroupParamsSchema>): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: probeGroupToolName,
    params
  }, { timeout: getToolTimeout(probeGroupToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to configure light probe group`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully configured light probe group`
    }],
    structuredContent: {
      instanceId: response.instanceId,
      gameObjectName: response.gameObjectName,
      probeCount: response.probeCount,
      center: response.center,
      size: response.size
    }
  };
}

// ============================================================================
// create_reflection_probe
// ============================================================================

const reflectionProbeToolName = 'create_reflection_probe';
const reflectionProbeToolDescription = 'Creates or configures a Reflection Probe for localized environment reflections (box projection, resolution, size).';
const reflectionProbeParamsSchema = z.object({
  name: z.string().default('ReflectionProbe').describe('Name of the Reflection Probe GameObject'),
  position: createVector3Schema('World position to place the probe (default: { x: 0, y: 1.5, z: 0 })').optional(),
  size: createVector3Schema('Dimensions of the reflection box zone (default: { x: 10, y: 5, z: 10 })').optional(),
  resolution: z.number().int().default(128).describe('Cubemap resolution (e.g. 64, 128, 256, 512)'),
  boxProjection: z.boolean().default(true).describe('Whether box projection is enabled for parallax correction'),
  mode: z.enum(['baked', 'realtime', 'custom']).default('baked').describe('Reflection probe capture mode'),
  parentPath: z.string().optional().describe('Optional hierarchy path of parent GameObject'),
  parentId: z.number().optional().describe('Optional instance ID of parent GameObject'),
  reason: z.string().optional().describe('Optional explanation of why reflection probe is being created')
});

export function registerCreateReflectionProbeTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${reflectionProbeToolName}`);

  server.tool(
    reflectionProbeToolName,
    reflectionProbeToolDescription,
    reflectionProbeParamsSchema.shape,
    async (params: z.infer<typeof reflectionProbeParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${reflectionProbeToolName}`, params);
        const result = await createReflectionProbeToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${reflectionProbeToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${reflectionProbeToolName}`, error);
        throw error;
      }
    }
  );
}

async function createReflectionProbeToolHandler(mcpUnity: McpUnity, params: z.infer<typeof reflectionProbeParamsSchema>): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: reflectionProbeToolName,
    params
  }, { timeout: getToolTimeout(reflectionProbeToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to create reflection probe`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully created reflection probe`
    }],
    structuredContent: {
      instanceId: response.instanceId,
      name: response.name,
      path: response.path,
      mode: response.mode,
      resolution: response.resolution,
      boxProjection: response.boxProjection,
      size: response.size
    }
  };
}
