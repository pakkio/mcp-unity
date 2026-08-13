import * as z from 'zod';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { getToolTimeout } from '../utils/timeouts.js';

const toolName = 'capture_screenshot';
const toolDescription = 'Captures a screenshot of the Unity Scene view or a named/main Camera, returned as an image.';
const paramsSchema = z.object({
  view: z.enum(['scene', 'camera']).default('scene').describe('"scene" captures the active Scene view (Edit mode). "camera" renders a specific camera (works in Edit or Play mode).'),
  cameraName: z.string().optional().describe('GameObject name of the camera to capture when view is "camera". Defaults to Camera.main if omitted.'),
  maxWidth: z.number().int().min(64).max(4096).default(1024).describe('Maximum image width in pixels; height is scaled to match the source aspect ratio.'),
  format: z.enum(['png', 'jpg']).default('png').describe('Image encoding format'),
  jpgQuality: z.number().int().min(1).max(100).default(90).describe('JPEG quality (1-100), only used when format is "jpg"'),
  savePath: z.string().optional().describe('Local file path (relative to the Unity project root, or absolute) to save the screenshot. If specified, the image is saved directly on the Unity host to avoid base64 transport overhead.'),
});

/**
 * Creates and registers the Capture Screenshot tool with the MCP server
 */
export function registerCaptureScreenshotTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
    params: {
      view: params.view,
      cameraName: params.cameraName,
      maxWidth: params.maxWidth,
      format: params.format,
      jpgQuality: params.jpgQuality,
      savePath: params.savePath,
    }
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to capture screenshot'
    );
  }

  const content: any[] = [
    {
      type: 'text',
      text: response.message || 'Screenshot captured'
    }
  ];

  if (response.imageBase64) {
    content.push({
      type: 'image',
      data: response.imageBase64,
      mimeType: response.mimeType
    });
  }

  return { content };
}
