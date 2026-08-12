import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

const toolName = 'draw_scene_gizmo';
const toolDescription = 'Draws a temporary debug shape (line, box, or sphere) in the Unity Editor Scene View';

const vector3Schema = z.object({
  x: z.number(),
  y: z.number(),
  z: z.number()
});

const colorSchema = z.object({
  r: z.number(),
  g: z.number(),
  b: z.number(),
  a: z.number()
});

const paramsSchema = z.object({
  shapeType: z.string().describe("Type of shape to draw ('line', 'box', 'sphere')"),
  position: vector3Schema.optional().describe("Position of the shape (or start position for lines)"),
  endPosition: vector3Schema.optional().describe("End position (required for lines)"),
  size: vector3Schema.optional().describe("Size or scale of the shape (default: (1,1,1))"),
  color: colorSchema.optional().describe("Color of the shape (r,g,b,a 0-1, default: green)"),
  duration: z.number().optional().describe("Duration in seconds to display the shape (default: 5)")
});

export function registerDebugDrawTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${toolName}`);

  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: any): Promise<CallToolResult> => {
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

async function toolHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.shapeType) {
    throw new McpUnityError(ErrorType.VALIDATION, "Parameter 'shapeType' is required");
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to draw scene gizmo'
    );
  }

  return {
    content: [{
      type: 'text' as const,
      text: response.message || 'Successfully drawn scene gizmo'
    }]
  };
}
