import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// configure_cvr_interactivity
// ============================================================================

const toolName = 'configure_cvr_interactivity';
const toolDescription = 'Configures ChilloutVR CCK interactivity: CVRInteractable trigger actions, CVRPickupObject physics grips, and CVRVariableBuffer networked variables.';
const paramsSchema = z.object({
  action: z.enum([
    'add_interactable',
    'configure_pickup',
    'setup_variable_buffer'
  ]).default('add_interactable').describe('Interactivity action to execute'),

  instanceId: z.number().optional().describe('Instance ID of the target GameObject'),
  objectPath: z.string().optional().describe('Hierarchy path of the target GameObject'),

  // add_interactable parameters
  interactionType: z.enum(['interact', 'grab', 'touch', 'look_at', 'area_trigger']).default('interact').describe('Trigger mechanism for the interaction'),
  actionType: z.enum(['toggle_gameobject', 'teleport_player', 'play_audio', 'set_animator_param', 'spawn_prefab']).default('toggle_gameobject').describe('Action performed upon trigger'),
  targetInstanceId: z.number().optional().describe('Instance ID of GameObject affected by the action'),
  targetObjectPath: z.string().optional().describe('Hierarchy path of GameObject affected by the action'),
  parameterName: z.string().optional().describe('Animator or property parameter name to drive'),
  parameterValue: z.string().optional().describe('Value to set on trigger'),

  // configure_pickup parameters
  gripType: z.enum(['origin', 'custom']).default('origin').describe('Hand grip anchor point'),
  autoHold: z.boolean().default(true).describe('Whether the object stays held without holding down grip button'),
  throwVelocityMultiplier: z.number().min(0).default(1.0).describe('Velocity multiplier applied when thrown'),
  dropOnTeleport: z.boolean().default(false).describe('Whether object is dropped if player teleports'),

  // setup_variable_buffer parameters
  variableName: z.string().optional().describe('Networked variable name for multiplayer synchronization'),
  variableType: z.enum(['bool', 'float', 'int', 'string']).default('int').describe('Type of synchronized variable'),
  defaultValue: z.string().optional().describe('Default starting value'),

  reason: z.string().optional().describe('Optional explanation of why interactivity is being configured')
});

export function registerConfigureCvrInteractivityTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
  if (params.instanceId === undefined && !params.objectPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' is required for configuring interactivity."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to configure CVR interactivity`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully configured CVR interactivity`
    }],
    structuredContent: {
      instanceId: response.instanceId,
      gameObjectName: response.gameObjectName,
      cckInstalled: response.cckInstalled,
      interactionType: response.interactionType,
      actionType: response.actionType,
      actionTarget: response.actionTarget,
      gripType: response.gripType,
      variableName: response.variableName,
      variableType: response.variableType
    }
  };
}
