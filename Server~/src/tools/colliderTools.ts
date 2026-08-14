import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { getToolTimeout } from '../utils/timeouts.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// configure_colliders
// ============================================================================

const toolName = 'configure_colliders';
const toolDescription = 'Configures MeshColliders (convex hull, triggers, custom collision meshes), generates colliders across object hierarchies, or creates PhysicMaterial assets.';
const paramsSchema = z.object({
  action: z.enum([
    'configure_mesh_collider',
    'generate_hierarchy_colliders',
    'create_physics_material'
  ]).default('configure_mesh_collider').describe('The collider action to perform'),

  instanceId: z.number().optional().describe('Instance ID of the target GameObject'),
  objectPath: z.string().optional().describe('Hierarchy path of the target GameObject'),

  // configure_mesh_collider parameters
  convex: z.boolean().optional().describe('Enable convex hull generation (required for MeshColliders to collide with non-kinematic Rigidbodies)'),
  isTrigger: z.boolean().optional().describe('Whether the collider acts as a trigger (automatically enables convex if true)'),
  meshPath: z.string().optional().describe('Optional path or GUID of custom collision Mesh asset if different from visual mesh'),
  materialPath: z.string().optional().describe('Optional path or GUID of PhysicMaterial asset'),

  // generate_hierarchy_colliders parameters
  colliderType: z.enum(['box', 'capsule', 'sphere', 'convex_mesh', 'mesh']).default('box').describe('Type of collider to attach across child meshes'),
  includeChildren: z.boolean().default(true).describe('Whether to recursively search and add colliders to all child meshes'),
  replaceExisting: z.boolean().default(false).describe('Whether to replace existing colliders'),

  // create_physics_material parameters
  assetPath: z.string().optional().describe('Asset path to save new PhysicMaterial (e.g. "Assets/Physics/BouncyRubber.physicMaterial")'),
  dynamicFriction: z.number().min(0).default(0.6).describe('Friction used when moving (0 = frictionless ice, 1 = high friction)'),
  staticFriction: z.number().min(0).default(0.6).describe('Friction used when resting at standstill'),
  bounciness: z.number().min(0).max(1).default(0).describe('Bounciness factor (0 = no bounce, 1 = perfect restitution)'),
  frictionCombine: z.enum(['average', 'minimum', 'maximum', 'multiply']).default('average').describe('How friction of two colliding objects is combined'),
  bounceCombine: z.enum(['average', 'minimum', 'maximum', 'multiply']).default('average').describe('How bounciness of two colliding objects is combined'),

  reason: z.string().optional().describe('Optional explanation of why colliders/physics are being modified')
});

export function registerConfigureCollidersTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
  if (params.action !== 'create_physics_material' && params.instanceId === undefined && !params.objectPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' is required for this collider action."
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to configure colliders`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully executed configure_colliders`
    }],
    structuredContent: {
      instanceId: response.instanceId,
      gameObjectName: response.gameObjectName,
      convex: response.convex,
      isTrigger: response.isTrigger,
      sharedMesh: response.sharedMesh,
      collidersAdded: response.collidersAdded,
      colliderType: response.colliderType,
      assetPath: response.assetPath,
      guid: response.guid
    }
  };
}
