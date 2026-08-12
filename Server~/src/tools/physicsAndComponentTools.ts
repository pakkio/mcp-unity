import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// Raycast Query Tool
const raycastToolName = 'raycast_query';
const raycastToolDescription = 'Executes a physics raycast query in the Unity scene and returns hit information';

const vector3Schema = z.object({
  x: z.number(),
  y: z.number(),
  z: z.number()
});

const raycastParamsSchema = z.object({
  origin: vector3Schema.describe("Origin point of the raycast"),
  direction: vector3Schema.describe("Direction vector of the raycast"),
  maxDistance: z.number().optional().describe("Maximum distance to check (default: 1000)"),
  layerMask: z.number().int().optional().describe("Layer mask bitmask (default: all layers)")
});

// Copy Component Tool
const copyComponentToolName = 'copy_component';
const copyComponentDescription = 'Copies a component from a source GameObject to a target GameObject, preserving its field values';

const copyComponentParamsSchema = z.object({
  componentType: z.string().describe("The name of the component class to copy (e.g. 'BoxCollider')"),
  sourceInstanceId: z.number().int().optional().describe("The instance ID of the source GameObject"),
  sourceObjectPath: z.string().optional().describe("The hierarchy path of the source GameObject (e.g. 'Player')"),
  targetInstanceId: z.number().int().optional().describe("The instance ID of the target GameObject"),
  targetObjectPath: z.string().optional().describe("The hierarchy path of the target GameObject (e.g. 'Enemy')")
});

export function registerPhysicsAndComponentTools(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${raycastToolName}`);
  server.tool(
    raycastToolName,
    raycastToolDescription,
    raycastParamsSchema.shape,
    async (params: any): Promise<CallToolResult> => {
      try {
        logger.info(`Executing tool: ${raycastToolName}`, params);
        const result = await raycastQueryHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${raycastToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${raycastToolName}`, error);
        throw error;
      }
    }
  );

  logger.info(`Registering tool: ${copyComponentToolName}`);
  server.tool(
    copyComponentToolName,
    copyComponentDescription,
    copyComponentParamsSchema.shape,
    async (params: any): Promise<CallToolResult> => {
      try {
        logger.info(`Executing tool: ${copyComponentToolName}`, params);
        const result = await copyComponentHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${copyComponentToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${copyComponentToolName}`, error);
        throw error;
      }
    }
  );
}

async function raycastQueryHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.origin || !params.direction) {
    throw new McpUnityError(ErrorType.VALIDATION, "Parameters 'origin' and 'direction' are required");
  }

  const response = await mcpUnity.sendRequest({
    method: raycastToolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to execute raycast'
    );
  }

  let text = "Raycast did not hit any collider.";
  if (response.hit) {
    text = `Raycast hit GameObject '${response.gameObjectName}' at distance ${response.distance}m. Hit point: (${response.point.x}, ${response.point.y}, ${response.point.z})`;
  }

  return {
    content: [{
      type: 'text' as const,
      text
    }],
    data: response
  };
}

async function copyComponentHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.componentType) {
    throw new McpUnityError(ErrorType.VALIDATION, "Parameter 'componentType' is required");
  }
  if (!params.sourceInstanceId && !params.sourceObjectPath) {
    throw new McpUnityError(ErrorType.VALIDATION, "Either 'sourceInstanceId' or 'sourceObjectPath' must be provided");
  }
  if (!params.targetInstanceId && !params.targetObjectPath) {
    throw new McpUnityError(ErrorType.VALIDATION, "Either 'targetInstanceId' or 'targetObjectPath' must be provided");
  }

  const response = await mcpUnity.sendRequest({
    method: copyComponentToolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to copy component'
    );
  }

  return {
    content: [{
      type: 'text' as const,
      text: response.message || 'Successfully copied component'
    }],
    data: {
      action: response.action
    }
  };
}
