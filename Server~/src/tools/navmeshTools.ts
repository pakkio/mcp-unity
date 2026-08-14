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
// manage_navmesh
// ============================================================================

const toolName = 'manage_navmesh';
const toolDescription = 'Manages Unity AI Navigation: bakes/clears NavMesh, configures NavMeshAgents and NavMeshObstacles, and queries pathfinding routes.';
const paramsSchema = z.object({
  action: z.enum([
    'get_status',
    'bake',
    'clear',
    'add_agent',
    'add_obstacle',
    'calculate_path'
  ]).default('get_status').describe('NavMesh action to perform'),

  instanceId: z.number().optional().describe('Instance ID of GameObject for add_agent or add_obstacle'),
  objectPath: z.string().optional().describe('Hierarchy path of GameObject for add_agent or add_obstacle'),

  // add_agent parameters
  speed: z.number().min(0).default(3.5).describe('Agent movement speed in m/s'),
  angularSpeed: z.number().min(0).default(120).describe('Maximum turning speed in deg/s'),
  acceleration: z.number().min(0).default(8).describe('Agent acceleration rate'),
  stoppingDistance: z.number().min(0).default(0.5).describe('Distance from target to stop in meters'),
  radius: z.number().min(0.01).default(0.5).describe('Agent cylinder collision radius'),
  height: z.number().min(0.01).default(2.0).describe('Agent height in meters'),
  autoBraking: z.boolean().default(true).describe('Whether the agent slows down when approaching target destination'),

  // add_obstacle parameters
  carving: z.boolean().default(true).describe('Whether obstacle carves a hole into the NavMesh dynamically'),
  shape: z.enum(['box', 'capsule']).default('box').describe('Obstacle boundary shape'),
  size: createVector3Schema('Dimensions of obstacle boundary box').optional(),
  center: createVector3Schema('Center offset of obstacle').optional(),

  // calculate_path parameters
  startPosition: createVector3Schema('Start position coordinate').optional(),
  targetPosition: createVector3Schema('Target destination coordinate').optional(),

  reason: z.string().optional().describe('Optional explanation of why NavMesh is being configured')
});

export function registerManageNavMeshTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
  if (['add_agent', 'add_obstacle'].includes(params.action) && params.instanceId === undefined && !params.objectPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      `Either 'instanceId' or 'objectPath' is required when action is '${params.action}'.`
    );
  }

  if (params.action === 'calculate_path' && (!params.startPosition || !params.targetPosition)) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'startPosition' and 'targetPosition' are required when action is 'calculate_path'."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to execute manage_navmesh`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully executed manage_navmesh`
    }],
    structuredContent: {
      hasNavMesh: response.hasNavMesh,
      agentCount: response.agentCount,
      obstacleCount: response.obstacleCount,
      surfacesBaked: response.surfacesBaked,
      instanceId: response.instanceId,
      gameObjectName: response.gameObjectName,
      status: response.status,
      isComplete: response.isComplete,
      waypointCount: response.waypointCount,
      totalDistance: response.totalDistance,
      waypoints: response.waypoints
    }
  };
}
