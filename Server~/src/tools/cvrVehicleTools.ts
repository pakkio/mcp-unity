import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// configure_cvr_vehicle
// ============================================================================

const toolName = 'configure_cvr_vehicle';
const toolDescription = 'Creates and configures ChilloutVR CCK drivable vehicles: 4-wheel chassis rigs, WheelCollider suspension tuning, and passenger seats.';
const paramsSchema = z.object({
  action: z.enum([
    'create_car_rig',
    'configure_suspension',
    'add_passenger_seats'
  ]).default('create_car_rig').describe('Vehicle action to execute'),

  // Target object (for configure_suspension and add_passenger_seats)
  instanceId: z.number().optional().describe('Instance ID of the target vehicle GameObject'),
  objectPath: z.string().optional().describe('Hierarchy path of the target vehicle GameObject'),

  // create_car_rig parameters
  vehicleName: z.string().optional().describe('Name of the new vehicle GameObject (default "CVR_Drivable_Car")'),
  mass: z.number().min(10).optional().describe('Vehicle chassis mass in kg (default 1200)'),
  spring: z.number().min(100).optional().describe('Suspension spring force (default 30000)'),
  damper: z.number().min(10).optional().describe('Suspension damper rate (default 4500)'),
  suspensionDistance: z.number().min(0.01).optional().describe('Suspension travel distance in meters (default 0.2)'),
  wheelRadius: z.number().min(0.05).optional().describe('Wheel radius in meters (default 0.35)'),
  addHeadlights: z.boolean().default(true).describe('Whether to create spot headlights'),
  addEngineAudio: z.boolean().default(true).describe('Whether to create 3D spatialized engine audio source'),

  // configure_suspension extra parameters
  targetPosition: z.number().min(0).max(1).optional().describe('Suspension resting target position (0.0 - 1.0)'),
  forwardStiffness: z.number().min(0.1).optional().describe('Tire forward friction stiffness multiplier'),
  sidewaysStiffness: z.number().min(0.1).optional().describe('Tire sideways/lateral friction stiffness multiplier'),
  centerOfMassY: z.number().optional().describe('Center of mass Y offset (e.g. -0.35 to prevent rollover)'),

  // add_passenger_seats parameters
  seatCount: z.number().int().min(1).max(8).default(3).describe('Number of passenger seats to create (1 to 8)'),

  reason: z.string().optional().describe('Optional explanation of why the vehicle is being configured')
});

export function registerConfigureCvrVehicleTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
      response.message || `Failed to configure CVR vehicle`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully executed ${toolName}`
    }],
    structuredContent: {
      vehicleName: response.vehicleName,
      instanceId: response.instanceId,
      mass: response.mass,
      wheelCount: response.wheelCount,
      wheels: response.wheels,
      hasDriverSeat: response.hasDriverSeat,
      hasHeadlights: response.hasHeadlights,
      hasEngineAudio: response.hasEngineAudio,
      wheelsTuned: response.wheelsTuned,
      seatsAdded: response.seatsAdded,
      seats: response.seats,
      cckInstalled: response.cckInstalled
    }
  };
}
