import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { getToolTimeout } from '../utils/timeouts.js';

// Build Project Tool
const buildToolName = 'build_project';
const buildToolDescription = 'Compiles and builds the Unity project for a target platform';

const buildParamsSchema = z.object({
  target: z.string().describe("Target platform (e.g. 'StandaloneWindows64', 'WebGL', 'Android', 'iOS', 'StandaloneOSX')"),
  outputPath: z.string().describe("Output file/folder path (e.g. 'Builds/Game.exe' or 'Builds/WebGL/')"),
  options: z.array(z.string()).optional().describe("Build Options (e.g. ['Development', 'AllowDebugging'])")
});

// Get Compilation Errors Tool
const getErrorsToolName = 'get_compilation_errors';
const getErrorsToolDescription = 'Retrieves active C# script compilation errors and warnings in the project';

const getErrorsParamsSchema = z.object({});

// Find Script References Tool
const findReferencesToolName = 'find_script_references';
const findReferencesToolDescription = 'Finds all GameObjects in the active scene referencing a specified C# script class';

const findReferencesParamsSchema = z.object({
  scriptName: z.string().describe("The name of the C# script class to search references for (e.g. 'PlayerController')")
});

export function registerBuildAndAnalysisTools(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${buildToolName}`);
  server.tool(
    buildToolName,
    buildToolDescription,
    buildParamsSchema.shape,
    async (params: any): Promise<CallToolResult> => {
      try {
        logger.info(`Executing tool: ${buildToolName}`, params);
        const result = await buildProjectHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${buildToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${buildToolName}`, error);
        throw error;
      }
    }
  );

  logger.info(`Registering tool: ${getErrorsToolName}`);
  server.tool(
    getErrorsToolName,
    getErrorsToolDescription,
    getErrorsParamsSchema.shape,
    async (params: any): Promise<CallToolResult> => {
      try {
        logger.info(`Executing tool: ${getErrorsToolName}`, params);
        const result = await getCompilationErrorsHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${getErrorsToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${getErrorsToolName}`, error);
        throw error;
      }
    }
  );

  logger.info(`Registering tool: ${findReferencesToolName}`);
  server.tool(
    findReferencesToolName,
    findReferencesToolDescription,
    findReferencesParamsSchema.shape,
    async (params: any): Promise<CallToolResult> => {
      try {
        logger.info(`Executing tool: ${findReferencesToolName}`, params);
        const result = await findScriptReferencesHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${findReferencesToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${findReferencesToolName}`, error);
        throw error;
      }
    }
  );
}

async function buildProjectHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.target || !params.outputPath) {
    throw new McpUnityError(ErrorType.VALIDATION, "Parameters 'target' and 'outputPath' are required");
  }

  const response = await mcpUnity.sendRequest({
    method: buildToolName,
    params
  }, { timeout: getToolTimeout(buildToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to build project'
    );
  }

  return {
    content: [{
      type: 'text' as const,
      text: response.message || 'Successfully built project'
    }],
    structuredContent: {
      totalErrors: response.totalErrors,
      totalWarnings: response.totalWarnings,
      totalSize: response.totalSize,
      totalTimeSeconds: response.totalTimeSeconds
    }
  };
}

async function getCompilationErrorsHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: getErrorsToolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to get compilation errors'
    );
  }

  return {
    content: [{
      type: 'text' as const,
      text: `Compilation Failed: ${response.compilationFailed}. Found ${response.errors?.length || 0} compiler errors/warnings.`
    }],
    structuredContent: {
      compilationFailed: response.compilationFailed,
      errors: response.errors || []
    }
  };
}

async function findScriptReferencesHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.scriptName) {
    throw new McpUnityError(ErrorType.VALIDATION, "Parameter 'scriptName' is required");
  }

  const response = await mcpUnity.sendRequest({
    method: findReferencesToolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to find script references'
    );
  }

  return {
    content: [{
      type: 'text' as const,
      text: `Found ${response.referenceCount || 0} GameObjects referencing script '${params.scriptName}'`
    }],
    structuredContent: {
      scriptName: response.scriptName,
      referenceCount: response.referenceCount,
      references: response.references || []
    }
  };
}
