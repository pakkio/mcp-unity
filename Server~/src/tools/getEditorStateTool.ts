import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';

const toolName = 'get_editor_state';
const toolDescription = 'Gets the current Unity Editor state, including modal dialogs, focused/open windows, compilation/play status, and unsaved scene changes.';

const paramsSchema = z.object({});

export function registerGetEditorStateTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to get editor state'
    );
  }

  let text = `Editor State:\n`;
  text += `  Has Modal Window: ${response.hasModalWindow}\n`;
  if (response.hasModalWindow && Array.isArray(response.modalWindows)) {
    text += `  Modal Windows (${response.modalWindowCount}):\n`;
    for (const win of response.modalWindows) {
      text += `    - "${win.title}" (${win.typeName})\n`;
    }
  }
  text += `  Focused Window: ${response.focusedWindow || '(none)'}\n`;
  text += `  Is Compiling: ${response.isCompiling}\n`;
  text += `  Is Updating: ${response.isUpdating}\n`;
  text += `  Is Playing: ${response.isPlaying}\n`;
  text += `  Is Paused: ${response.isPaused}\n`;
  text += `  Has Unsaved Changes: ${response.hasUnsavedChanges}\n`;
  if (response.hasUnsavedChanges && Array.isArray(response.unsavedScenes)) {
    text += `  Unsaved Scenes (${response.unsavedSceneCount}):\n`;
    for (const sc of response.unsavedScenes) {
      text += `    - "${sc.name}": ${sc.path || '(untitled)'}\n`;
    }
  }

  return {
    content: [{
      type: response.type || 'text',
      text
    }],
    structuredContent: {
      hasModalWindow: response.hasModalWindow,
      modalWindowCount: response.modalWindowCount,
      modalWindows: response.modalWindows,
      focusedWindow: response.focusedWindow,
      openWindowCount: response.openWindowCount,
      openWindows: response.openWindows,
      isCompiling: response.isCompiling,
      isUpdating: response.isUpdating,
      isPlaying: response.isPlaying,
      isPaused: response.isPaused,
      hasUnsavedChanges: response.hasUnsavedChanges,
      unsavedSceneCount: response.unsavedSceneCount,
      unsavedScenes: response.unsavedScenes
    }
  };
}
