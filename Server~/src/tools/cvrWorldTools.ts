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
// manage_cvr_world
// ============================================================================

const toolName = 'manage_cvr_world';
const toolDescription = 'Manages ChilloutVR CCK World components: CVRWorld root settings, spawn points, optimized mirrors, seats/chairs, and portals.';
const paramsSchema = z.object({
  action: z.enum([
    'setup_world',
    'add_spawn_point',
    'create_mirror',
    'create_seat',
    'create_portal'
  ]).default('setup_world').describe('World action to execute'),

  objectName: z.string().optional().describe('Name of the CVRWorld root GameObject'),
  respawnHeight: z.number().default(-50).describe('Y height where players respawn if falling off world'),
  runSpeed: z.number().min(0.1).default(4.0).describe('Base player walking/running speed'),
  sprintMultiplier: z.number().min(1.0).default(2.0).describe('Sprint speed multiplier'),
  jumpHeight: z.number().min(0).default(1.5).describe('Player jump height in meters'),
  allowFlight: z.boolean().default(true).describe('Whether flight mode is permitted in the world'),
  allowTeleport: z.boolean().default(true).describe('Whether VR teleport locomotion is permitted'),
  gravity: createVector3Schema('Custom world gravity vector').optional(),

  // add_spawn_point parameters
  spawnName: z.string().optional().describe('Name of the spawn point GameObject'),
  position: createVector3Schema('Position of the spawn point or mirror').optional(),
  rotation: createVector3Schema('Euler rotation of the spawn point').optional(),

  // create_mirror parameters
  mirrorName: z.string().optional().describe('Name of the mirror GameObject'),
  size: z.object({
    width: z.number().describe('Mirror width in meters'),
    height: z.number().describe('Mirror height in meters')
  }).optional().describe('Dimensions of the mirror quad'),
  mirrorType: z.enum(['full', 'transparent', 'cutout', 'optimized', 'avatar_only']).default('optimized').describe('Mirror rendering mode and optimization level'),

  // create_seat parameters
  seatName: z.string().optional().describe('Name of the seat/chair GameObject'),
  instanceId: z.number().optional().describe('Instance ID of an existing object to turn into a seat'),
  objectPath: z.string().optional().describe('Hierarchy path of an existing object to turn into a seat'),

  // create_portal parameters
  portalName: z.string().optional().describe('Name of the portal GameObject'),
  targetWorldId: z.string().optional().describe('Target ChilloutVR World GUID (e.g. "wrld_...")'),
  targetInstanceId: z.string().optional().describe('Target ChilloutVR instance ID'),

  reason: z.string().optional().describe('Optional explanation of why CVR world components are being configured')
});

export function registerManageCvrWorldTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to manage CVR world`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully executed manage_cvr_world`
    }],
    structuredContent: {
      worldName: response.worldName,
      spawnName: response.spawnName,
      mirrorName: response.mirrorName,
      seatName: response.seatName,
      portalName: response.portalName,
      instanceId: response.instanceId,
      cckInstalled: response.cckInstalled,
      respawnHeight: response.respawnHeight,
      runSpeed: response.runSpeed,
      jumpHeight: response.jumpHeight,
      allowFlight: response.allowFlight,
      allowTeleport: response.allowTeleport,
      targetWorldId: response.targetWorldId
    }
  };
}
