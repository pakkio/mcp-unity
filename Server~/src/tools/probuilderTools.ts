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
// probuilder_create_shape
// ============================================================================

const createShapeToolName = 'probuilder_create_shape';
const createShapeToolDescription = 'Creates a 3D ProBuilder greyboxing shape (cube, stair, cylinder, arch, prism, plane, door, pipe, cone, torus, sphere).';
const createShapeParamsSchema = z.object({
  shapeType: z.enum([
    'cube',
    'stair',
    'cylinder',
    'arch',
    'prism',
    'plane',
    'door',
    'pipe',
    'cone',
    'torus',
    'sphere'
  ]).default('cube').describe('The type of 3D shape to generate'),
  name: z.string().optional().describe('Optional name for the created GameObject'),
  size: createVector3Schema('Dimensions of the shape (width, height, length)').optional(),
  position: createVector3Schema('World position to place the shape').optional(),
  rotation: createVector3Schema('Euler rotation in degrees').optional(),
  parentPath: z.string().optional().describe('Optional hierarchy path of the parent GameObject'),
  parentId: z.number().optional().describe('Optional instance ID of the parent GameObject'),
  reason: z.string().optional().describe('Optional explanation of why this shape is being created')
});

export function registerProBuilderCreateShapeTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${createShapeToolName}`);

  server.tool(
    createShapeToolName,
    createShapeToolDescription,
    createShapeParamsSchema.shape,
    async (params: z.infer<typeof createShapeParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${createShapeToolName}`, params);
        const result = await createShapeToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${createShapeToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${createShapeToolName}`, error);
        throw error;
      }
    }
  );
}

async function createShapeToolHandler(mcpUnity: McpUnity, params: z.infer<typeof createShapeParamsSchema>): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: createShapeToolName,
    params
  }, { timeout: getToolTimeout(createShapeToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to create ProBuilder shape`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully created ProBuilder shape`
    }],
    structuredContent: {
      instanceId: response.instanceId,
      name: response.name,
      path: response.path,
      shapeType: response.shapeType
    }
  };
}

// ============================================================================
// probuilder_mesh_op
// ============================================================================

const meshOpToolName = 'probuilder_mesh_op';
const meshOpToolDescription = 'Performs operations on ProBuilder meshes (subdivide, export to asset, strip ProBuilder scripts to convert to standard MeshFilter/MeshRenderer).';
const meshOpParamsSchema = z.object({
  instanceId: z.number().optional().describe('Instance ID of the ProBuilder GameObject'),
  objectPath: z.string().optional().describe('Hierarchy path of the ProBuilder GameObject'),
  operation: z.enum([
    'subdivide',
    'strip_probuilder_scripts',
    'export_asset'
  ]).default('subdivide').describe('The mesh operation to perform: "subdivide" to increase geometry density, "strip_probuilder_scripts" to convert into a normal static MeshRenderer, "export_asset" to save the mesh as a reusable .asset file'),
  exportPath: z.string().optional().describe('Project path to save exported mesh (e.g. "Assets/Models/MyMesh.asset")'),
  reason: z.string().optional().describe('Optional explanation of why the mesh operation is being performed')
});

export function registerProBuilderMeshOpTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${meshOpToolName}`);

  server.tool(
    meshOpToolName,
    meshOpToolDescription,
    meshOpParamsSchema.shape,
    async (params: z.infer<typeof meshOpParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${meshOpToolName}`, params);
        const result = await meshOpToolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${meshOpToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${meshOpToolName}`, error);
        throw error;
      }
    }
  );
}

async function meshOpToolHandler(mcpUnity: McpUnity, params: z.infer<typeof meshOpParamsSchema>): Promise<CallToolResult> {
  if (params.instanceId === undefined && !params.objectPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: meshOpToolName,
    params
  }, { timeout: getToolTimeout(meshOpToolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to execute ProBuilder mesh operation`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully executed ProBuilder mesh operation`
    }],
    structuredContent: {
      gameObjectName: response.gameObjectName,
      assetPath: response.assetPath
    }
  };
}
