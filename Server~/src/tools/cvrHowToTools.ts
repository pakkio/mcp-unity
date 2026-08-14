import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// howto_cck
// ============================================================================

const toolName = 'howto_cck';
const toolDescription = 'Provides expert ChilloutVR CCK recipes, best practices, and optional GameObject scaffolding for vehicles, doors, elevators, mirrors, pickups, AAS, and optimization. Supports wildcard searches ("*", "veh*", "door*", "list").';
const paramsSchema = z.object({
  topic: z.string().default('*').describe('ChilloutVR CCK recipe topic or search pattern (e.g. "*", "list", "veh*", "vehicles", "door", "mirror", "elevator", "pickup", "aas", "video_player", "optimization")'),
  scaffold: z.boolean().default(false).describe('If true, automatically creates and sets up a pre-configured GameObject hierarchy in the active Unity scene'),
  objectName: z.string().optional().describe('Optional custom name for the scaffolded GameObject hierarchy'),
  reason: z.string().optional().describe('Optional explanation of why the CCK recipe is being consulted')
});

export function registerHowtoCckTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
      response.message || `Failed to consult howto_cck`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || response.guide || `Successfully fetched CCK recipe for ${params.topic}`
    }],
    structuredContent: {
      topic: response.topic,
      guide: response.guide,
      scaffoldedObject: response.scaffoldedObject,
      instanceId: response.instanceId,
      count: response.count,
      topics: response.topics
    }
  };
}
