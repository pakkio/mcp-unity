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
// manage_cvr_avatar
// ============================================================================

const toolName = 'manage_cvr_avatar';
const toolDescription = 'Manages ChilloutVR CCK Avatar components: CVRAvatar setup, automatic viewpoint/voice calculation, visemes, blink blendshapes, and Advanced Avatar Settings (AAS).';
const paramsSchema = z.object({
  action: z.enum([
    'setup_avatar',
    'configure_aas',
    'setup_face_tracking'
  ]).default('setup_avatar').describe('Avatar action to perform'),

  instanceId: z.number().optional().describe('Instance ID of the avatar root GameObject'),
  objectPath: z.string().optional().describe('Hierarchy path of the avatar root GameObject'),

  // setup_avatar custom overrides
  viewPosition: createVector3Schema('Optional explicit eye viewpoint coordinates (auto-calculated from head/eyes if omitted)').optional(),
  voicePosition: createVector3Schema('Optional explicit voice emission coordinates (auto-calculated if omitted)').optional(),

  // configure_aas parameters
  settingName: z.string().optional().describe('Name of the Advanced Avatar Setting (e.g. "HatToggle", "ColorPicker")'),
  settingType: z.enum(['toggle', 'slider', 'color_picker', 'submenu']).default('toggle').describe('Type of AAS menu control'),
  defaultValue: z.number().default(0).describe('Default value for the setting'),

  reason: z.string().optional().describe('Optional explanation of why the avatar is being configured')
});

export function registerManageCvrAvatarTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
      "Either 'instanceId' or 'objectPath' is required for managing CVR avatar."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to manage CVR avatar`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully managed CVR avatar`
    }],
    structuredContent: {
      avatarName: response.avatarName,
      instanceId: response.instanceId,
      cckInstalled: response.cckInstalled,
      viewPosition: response.viewPosition,
      voicePosition: response.voicePosition,
      faceRenderer: response.faceRenderer,
      settingName: response.settingName,
      settingType: response.settingType
    }
  };
}
