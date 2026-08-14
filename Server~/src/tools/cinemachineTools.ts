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
// create_virtual_camera
// ============================================================================

const toolName = 'create_virtual_camera';
const toolDescription = 'Creates and configures Cinemachine Virtual Cameras (Follow, LookAt, 3rd Person, First Person POV, Orbit, Framing).';
const paramsSchema = z.object({
  cameraName: z.string().default('CM_VirtualCamera').describe('Name of the Virtual Camera GameObject'),
  cameraType: z.enum(['third_person', 'first_person', 'look_at', 'static', 'orbit']).default('third_person').describe('Virtual camera framing style'),
  priority: z.number().int().default(10).describe('Cinemachine priority (highest active priority takes control of the Main Camera)'),
  fieldOfView: z.number().min(1).max(179).default(60).describe('Camera lens vertical Field of View'),

  followTargetInstanceId: z.number().optional().describe('Instance ID of target GameObject for camera to follow'),
  followTargetObjectPath: z.string().optional().describe('Hierarchy path of target GameObject for camera to follow'),

  lookAtTargetInstanceId: z.number().optional().describe('Instance ID of target GameObject for camera to aim at'),
  lookAtTargetObjectPath: z.string().optional().describe('Hierarchy path of target GameObject for camera to aim at'),

  followOffset: createVector3Schema('Camera offset position relative to follow target (e.g. { x: 0, y: 2, z: -4 })').optional(),
  reason: z.string().optional().describe('Optional explanation of why the virtual camera is being created')
});

export function registerCreateVirtualCameraTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
      response.message || `Failed to create virtual camera`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully created virtual camera`
    }],
    structuredContent: {
      cameraName: response.cameraName,
      instanceId: response.instanceId,
      cameraType: response.cameraType,
      priority: response.priority,
      fieldOfView: response.fieldOfView,
      followOffset: response.followOffset
    }
  };
}
