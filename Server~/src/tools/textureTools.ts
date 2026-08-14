import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// configure_texture_settings
// ============================================================================

const toolName = 'configure_texture_settings';
const toolDescription = 'Configures texture asset import settings (Normal Map, Sprite, Default, Max Size, sRGB, Filter Mode, Wrap Mode, Read/Write).';
const paramsSchema = z.object({
  texturePath: z.string().describe('Project path or GUID of the texture asset to configure (e.g. "Assets/Textures/Stone_Normal.png")'),
  textureType: z.enum(['default', 'normalmap', 'sprite', 'cookie', 'gui', 'singlechannel']).optional().describe('Texture type interpretation'),
  maxTextureSize: z.number().int().optional().describe('Maximum allowed texture resolution (e.g. 256, 512, 1024, 2048, 4096)'),
  wrapMode: z.enum(['repeat', 'clamp', 'mirror', 'mirroronce']).optional().describe('Texture wrap/tiling mode'),
  filterMode: z.enum(['point', 'bilinear', 'trilinear']).optional().describe('Filtering mode (Point for pixel art, Bilinear/Trilinear for smooth rendering)'),
  sRGBTexture: z.boolean().optional().describe('Enable for color textures (Albedo/Diffuse), disable for linear data textures (Normal Maps, Roughness, Masks)'),
  isReadable: z.boolean().optional().describe('Enable Read/Write on CPU for texture pixel sampling'),
  generateMipMaps: z.boolean().optional().describe('Whether to generate mipmap levels for distant rendering'),
  compression: z.enum(['compressed', 'uncompressed', 'highquality', 'lowquality']).optional().describe('Texture compression format quality'),
  reason: z.string().optional().describe('Optional explanation of why texture settings are being modified')
});

export function registerConfigureTextureSettingsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
  if (!params.texturePath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Parameter 'texturePath' must be provided."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to configure texture settings`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully configured texture settings`
    }],
    structuredContent: {
      texturePath: response.texturePath,
      textureType: response.textureType,
      maxTextureSize: response.maxTextureSize,
      wrapMode: response.wrapMode,
      filterMode: response.filterMode,
      sRGBTexture: response.sRGBTexture,
      isReadable: response.isReadable,
      mipmapEnabled: response.mipmapEnabled
    }
  };
}
