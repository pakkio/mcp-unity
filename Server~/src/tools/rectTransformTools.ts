import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// Fresh Vector2 schemas per field to avoid JSON pointer local references in schemas
function createVector2Schema(description: string) {
  return z.object({
    x: z.number().describe('X component'),
    y: z.number().describe('Y component')
  }).describe(description);
}

// ============================================================================
// set_rect_transform
// ============================================================================

const toolName = 'set_rect_transform';
const toolDescription = 'Sets RectTransform UI layout properties (anchor presets, anchoredPosition, sizeDelta, pivot, anchorMin/Max, offsets) on UI Canvas elements.';
const paramsSchema = z.object({
  instanceId: z.number().optional().describe('The instance ID of the UI GameObject'),
  objectPath: z.string().optional().describe('The hierarchy path of the UI GameObject'),
  anchorPreset: z.enum([
    'center',
    'top_left',
    'top_center',
    'top_right',
    'middle_left',
    'middle_right',
    'bottom_left',
    'bottom_center',
    'bottom_right',
    'top_stretch',
    'bottom_stretch',
    'left_stretch',
    'right_stretch',
    'stretch_horizontal',
    'stretch_vertical',
    'stretch_all'
  ]).optional().describe('Optional preset for anchors and pivot'),
  anchoredPosition: createVector2Schema('Position relative to the anchor reference point').optional(),
  sizeDelta: createVector2Schema('Size delta (width and height relative to anchors)').optional(),
  anchorMin: createVector2Schema('Normalized min anchor point (0-1)').optional(),
  anchorMax: createVector2Schema('Normalized max anchor point (0-1)').optional(),
  pivot: createVector2Schema('Normalized pivot point (0-1)').optional(),
  offsetMin: createVector2Schema('Bottom-left offset from anchorMin').optional(),
  offsetMax: createVector2Schema('Top-right offset from anchorMax').optional(),
  reason: z.string().optional().describe('Optional explanation of why the RectTransform is being modified')
});

export function registerSetRectTransformTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
      "Either 'instanceId' or 'objectPath' must be provided."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to set RectTransform`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully set RectTransform`
    }],
    structuredContent: {
      anchoredPosition: response.anchoredPosition,
      sizeDelta: response.sizeDelta,
      anchorMin: response.anchorMin,
      anchorMax: response.anchorMax,
      pivot: response.pivot,
      rect: response.rect,
      warning: response.warning
    }
  };
}
