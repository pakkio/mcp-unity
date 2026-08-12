import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { getToolTimeout } from '../utils/timeouts.js';

const toolName = 'export_package';
const toolDescription = 'Exports assets or scenes to a .unitypackage file with optional dependencies';

const paramsSchema = z.object({
  assetPath: z.string().optional().describe("The asset or scene path to export (e.g. 'Assets/pakkio.unity')"),
  assetPaths: z.array(z.string()).optional().describe("List of asset paths to include in the package"),
  exportPath: z.string().optional().describe("Output .unitypackage path (e.g. 'pakkio.unitypackage' or 'Assets/Exports/pakkio.unitypackage')"),
  includeDependencies: z.boolean().optional().describe("Whether to include dependencies (textures, materials, scripts). Default: true"),
  recurse: z.boolean().optional().describe("Whether to recurse subfolders. Default: true")
});

export function registerExportPackageTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${toolName}`);

  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: any) => {
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

async function toolHandler(mcpUnity: McpUnity, params: any) {
  if (!params.assetPath && (!params.assetPaths || params.assetPaths.length === 0)) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'assetPath' or 'assetPaths' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to export package'
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || 'Successfully exported package'
    }],
    structuredContent: {
      exportPath: response.exportPath,
      fullPath: response.fullPath,
      assetCount: response.assetCount
    }
  };
}
