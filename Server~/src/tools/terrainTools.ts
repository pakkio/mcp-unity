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

function createVector2Schema(description: string) {
  return z.object({
    x: z.number().describe('X component'),
    y: z.number().describe('Y component')
  }).describe(description);
}

function createRegionSchema() {
  return z.object({
    startX: z.number().min(0).max(1).default(0).describe('Normalized starting X coordinate (0-1)'),
    startZ: z.number().min(0).max(1).default(0).describe('Normalized starting Z coordinate (0-1)'),
    width: z.number().min(0).max(1).default(1).describe('Normalized width of region (0-1)'),
    height: z.number().min(0).max(1).default(1).describe('Normalized height/depth of region (0-1)')
  }).describe('Normalized subregion of terrain (0-1 coordinates)');
}

// ============================================================================
// manage_terrain
// ============================================================================

const toolName = 'manage_terrain';
const toolDescription = 'Creates, sculpts, paints, and manages Unity Terrains (procedural Perlin noise, height editing, terrain layers, tree scattering).';
const paramsSchema = z.object({
  action: z.enum(['create', 'sculpt', 'add_layer', 'add_trees', 'get_info']).default('get_info').describe('The action to perform: "create" to spawn a new terrain, "sculpt" to modify heights, "add_layer" to add texture layers, "add_trees" to scatter foliage, "get_info" to inspect.'),
  instanceId: z.number().optional().describe('Instance ID of the Terrain GameObject (defaults to active terrain in scene if omitted)'),
  objectPath: z.string().optional().describe('Hierarchy path of the Terrain GameObject'),
  
  // create action parameters
  terrainName: z.string().optional().describe('Name for the Terrain GameObject (default: "Terrain")'),
  assetPath: z.string().optional().describe('Project path to save the TerrainData asset (e.g. "Assets/Terrains/MyTerrain_Data.asset")'),
  size: createVector3Schema('Dimensions of the terrain (width, max height, length). Default: { x: 500, y: 100, z: 500 }').optional(),
  heightmapResolution: z.number().int().optional().describe('Heightmap grid resolution (e.g. 129, 257, 513). Default: 513'),
  alphamapResolution: z.number().int().optional().describe('Texture splatmap resolution (e.g. 512). Default: 512'),
  position: createVector3Schema('World position to place the terrain').optional(),

  // sculpt action parameters
  operation: z.enum(['perlin_noise', 'flatten', 'raise_lower', 'smooth']).optional().describe('Sculpting operation to perform'),
  height: z.number().min(0).max(1).optional().describe('Normalized target height for "flatten" operation (0-1)'),
  delta: z.number().optional().describe('Normalized height change for "raise_lower" operation (-1 to 1)'),
  scale: z.number().optional().describe('Perlin noise frequency scale (default: 20)'),
  heightScale: z.number().min(0).max(1).optional().describe('Maximum height multiplier for procedural noise (0-1, default: 0.35)'),
  octaves: z.number().int().min(1).max(8).optional().describe('Noise detail octaves (default: 3)'),
  persistence: z.number().optional().describe('Noise persistence across octaves (default: 0.5)'),
  lacunarity: z.number().optional().describe('Noise frequency multiplier across octaves (default: 2.0)'),
  offsetX: z.number().optional().describe('Perlin noise X seed offset'),
  offsetZ: z.number().optional().describe('Perlin noise Z seed offset'),
  iterations: z.number().int().min(1).max(10).optional().describe('Smoothing filter passes for "smooth" operation'),
  region: createRegionSchema().optional(),

  // add_layer action parameters
  diffuseTexture: z.string().optional().describe('Asset path or GUID of the diffuse texture for the terrain layer'),
  normalMap: z.string().optional().describe('Optional asset path or GUID of the normal map texture'),
  tileSize: createVector2Schema('Tiling size in world units (e.g. { x: 15, y: 15 })').optional(),
  layerAssetPath: z.string().optional().describe('Path to save the .terrainlayer asset'),

  // add_trees action parameters
  treePrefab: z.string().optional().describe('Asset path or GUID of the tree Prefab to scatter'),
  count: z.number().int().min(1).max(2000).optional().describe('Number of tree instances to spawn (default: 50)'),
  minHeight: z.number().min(0).max(1).optional().describe('Normalized minimum elevation where trees can spawn (0-1)'),
  maxHeight: z.number().min(0).max(1).optional().describe('Normalized maximum elevation where trees can spawn (0-1)'),
  randomScaleMin: z.number().optional().describe('Minimum random scale for trees (default: 0.8)'),
  randomScaleMax: z.number().optional().describe('Maximum random scale for trees (default: 1.2)'),
  clearExisting: z.boolean().optional().describe('If true, clears existing tree instances before scattering'),

  reason: z.string().optional().describe('Optional explanation of why the terrain is being modified')
});

export function registerManageTerrainTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
    params
  }, { timeout: getToolTimeout(toolName) });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to execute manage_terrain`
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || `Successfully executed manage_terrain`
    }],
    structuredContent: {
      instanceId: response.instanceId,
      terrainPath: response.terrainPath,
      assetPath: response.assetPath,
      size: response.size,
      operation: response.operation,
      layerIndex: response.layerIndex,
      treesAdded: response.treesAdded,
      totalTrees: response.totalTrees,
      layers: response.layers,
      treePrototypes: response.treePrototypes,
      heightmapResolution: response.heightmapResolution
    }
  };
}
