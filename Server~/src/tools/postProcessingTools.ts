import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// configure_post_processing
// ============================================================================

const toolName = 'configure_post_processing';
const toolDescription = 'Creates and configures Post-Processing Volumes and Volume Profiles (Bloom, Tonemapping, Vignette, Color Adjustments, Depth of Field, Chromatic Aberration).';
const paramsSchema = z.object({
  action: z.enum([
    'create_volume',
    'setup_global_volume',
    'add_override',
    'get_profile_info'
  ]).default('create_volume').describe('Post-processing action to execute'),

  volumeName: z.string().optional().describe('Name of the Volume GameObject in hierarchy (defaults to "Global Post Processing Volume")'),
  profilePath: z.string().optional().describe('Asset path of the VolumeProfile asset (e.g. "Assets/Settings/MainPostProfile.asset")'),
  isGlobal: z.boolean().default(true).describe('Whether volume influences camera everywhere in scene (global vs local box volume)'),
  weight: z.number().min(0).max(1).default(1).describe('Volume interpolation blend weight (0-1)'),
  priority: z.number().default(0).describe('Volume stacking priority (higher priority overrides lower)'),

  // add_override parameters
  effectType: z.enum([
    'bloom',
    'tonemapping',
    'vignette',
    'color_adjustments',
    'depth_of_field',
    'chromatic_aberration',
    'motion_blur',
    'white_balance',
    'film_grain'
  ]).optional().describe('Post-processing effect override type to add or configure'),

  settings: z.record(z.any()).optional().describe('Key-value dictionary of effect parameters (e.g. { intensity: 1.5, threshold: 0.9, mode: "ACES" })'),
  reason: z.string().optional().describe('Optional explanation of why post-processing is being configured')
});

export function registerConfigurePostProcessingTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
  if (params.action === 'add_override' && !params.effectType) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Parameter 'effectType' is required when action is 'add_override'."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to configure post processing`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully configured post processing`
    }],
    structuredContent: {
      volumeName: response.volumeName,
      instanceId: response.instanceId,
      profilePath: response.profilePath,
      profileName: response.profileName,
      effectType: response.effectType,
      componentClass: response.componentClass,
      overrideCount: response.overrideCount,
      overrides: response.overrides
    }
  };
}
