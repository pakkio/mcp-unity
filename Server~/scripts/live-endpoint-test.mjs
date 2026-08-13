#!/usr/bin/env node
// Live smoke test for every MCP Unity endpoint (tools, resources, prompts).
//
// Unlike src/__tests__/*.test.ts (which mock McpUnity.sendRequest), this spawns the actual
// built server and talks to a REAL running Unity Editor over its WebSocket bridge, exactly
// as a real MCP host would. It proves the endpoints work end-to-end, not just that they
// compile or satisfy a mock.
//
// Requires: `npm run build` already run, and a Unity Editor open with MCP Unity active
// (ws://localhost:8090 by default).
//
// Usage:
//   node scripts/live-endpoint-test.mjs                    # safe endpoints only (default)
//   node scripts/live-endpoint-test.mjs --include-expensive # also runs build_project,
//                                                            # set_play_mode_status, show_unity_dashboard
//
// Deliberately excluded even under --include-expensive: add_package. There is no
// corresponding "remove package" tool, so a successful call would permanently modify the
// target project's package manifest with no way for this suite to clean up after itself.
//
// Every scratch resource this suite creates is named/pathed with the "mcp_livetest_" prefix
// and is removed in a finally block, including on failure - the target project should be
// bit-for-bit unchanged after a run (a build_project artifact directory under --include-expensive
// is the one intentional exception; it is written to the OS temp dir and removed after use).

import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const serverRoot = path.resolve(here, '..');
const serverEntry = path.join(serverRoot, 'build', 'index.js');
const includeExpensive = process.argv.includes('--include-expensive');

const SCRATCH = {
  root: 'mcp_livetest_root',
  dup: 'mcp_livetest_dup',
  folder: 'Assets/mcp_livetest_folder',
  prefabName: 'mcp_livetest_prefab',
  materialName: 'mcp_livetest_material',
};

// ---------------------------------------------------------------------------
// result tracking
// ---------------------------------------------------------------------------
const results = [];
const covered = new Set();

function record(name, status, detail, ms) {
  results.push({ name, status, detail, ms });
  const icon = status === 'pass' ? '✓' : status === 'fail' ? '✗' : '—';
  const timing = ms != null ? ` (${ms}ms)` : '';
  const line = `${icon} ${name}${timing}${detail ? ' - ' + detail : ''}`;
  console.log(line);
}

async function step(name, fn) {
  covered.add(name);
  // Printed before the call, on its own line, not just on completion - the pass/fail line
  // alone gives no signal about which step a hang is currently stuck in until it eventually
  // resolves (or never does). This is run non-interactively (piped to a file/tail), so a `\r`
  // progress overwrite is invisible until a later real newline flushes it - a plain logged
  // line is what actually shows up promptly when tailing captured output during a hang.
  console.log(`… ${name}`);
  const start = Date.now();
  try {
    await fn();
    record(name, 'pass', null, Date.now() - start);
  } catch (err) {
    record(name, 'fail', err instanceof Error ? err.message : String(err), Date.now() - start);
  }
}

function skip(name, reason) {
  covered.add(name);
  record(name, 'skip', reason, null);
}

// ---------------------------------------------------------------------------
// call helpers
// ---------------------------------------------------------------------------
function textOf(result) {
  return result?.content?.find((c) => c.type === 'text')?.text ?? '';
}

async function callTool(client, name, args = {}, options) {
  const result = await client.callTool({ name, arguments: args }, undefined, options);
  if (result.isError) {
    throw new Error(`isError: true - ${textOf(result) || JSON.stringify(result)}`);
  }
  return result;
}

/** Calls a tool and returns true/false instead of throwing - for tools we expect might fail. */
async function callToolExpectingEither(client, name, args = {}) {
  const result = await client.callTool({ name, arguments: args });
  return { ok: !result.isError, result };
}

/**
 * Like callTool, but retries once if the failure is specifically the bridge's
 * "connection dropped while in flight" rejection - the one case where a blind retry is safe
 * here, since it fires only when Unity's own reconnect logic refused to resend a mutating
 * call rather than risk double-applying it (see mcpUnity.ts triageInFlightRequests). The
 * caller is responsible for only using this on a tool call it knows is idempotent; this test
 * suite only calls it for 'Assets/Refresh', which just re-scans for on-disk changes.
 *
 * The drop happens when a call lands while a *preceding* call's domain reload (e.g.
 * recompile_scripts) is still settling the WebSocket connection - a real race, not a bug, but
 * one this suite hits reliably by calling execute_menu_item immediately after recompile_scripts.
 */
async function callToolRetryOnConnectionDrop(client, name, args = {}, options) {
  try {
    return await callTool(client, name, args, options);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    if (!message.includes('Connection to Unity dropped')) {
      throw err;
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
    return await callTool(client, name, args, options);
  }
}

async function readResource(client, uri) {
  const result = await client.readResource({ uri });
  if (!result.contents || result.contents.length === 0) {
    throw new Error('empty contents array');
  }
  return result;
}

// ---------------------------------------------------------------------------
// main
// ---------------------------------------------------------------------------
async function main() {
  if (!fs.existsSync(serverEntry)) {
    console.error(`Server build not found at ${serverEntry} - run "npm run build" first.`);
    process.exit(1);
  }

  const transport = new StdioClientTransport({
    command: process.execPath,
    args: [serverEntry],
  });
  const client = new Client({ name: 'live-endpoint-test', version: '1.0.0' }, { capabilities: {} });

  console.log('Connecting to MCP Unity server...');
  await client.connect(transport);
  console.log('Connected.\n');

  try {
    // -- connectivity check: fail fast with a clear message if Unity isn't reachable --
    try {
      await callTool(client, 'get_scene_info');
    } catch (err) {
      console.error(
        `\nCould not reach Unity (${err instanceof Error ? err.message : err}).\n` +
        'Is the Unity Editor open with MCP Unity running (default ws://localhost:8090)?\n'
      );
      process.exitCode = 1;
      return;
    }

    console.log('=== Read-only endpoints ===');
    await runReadOnlyGroup(client);

    console.log('\n=== Scratch fixture setup ===');
    const fixtureOk = await setupFixtures(client);

    if (fixtureOk) {
      console.log('\n=== Mutating endpoints (scratch objects/assets only) ===');
      await runMutatingGroup(client);
    } else {
      skip('mutating group', 'scratch fixture setup failed - see above');
    }

    console.log('\n=== Resources ===');
    await runResourceGroup(client);

    console.log('\n=== Prompts ===');
    await runPromptGroup(client);

    if (includeExpensive) {
      console.log('\n=== Expensive/disruptive endpoints (--include-expensive) ===');
      await runExpensiveGroup(client);
    } else {
      skip('build_project', 'expensive - pass --include-expensive to run');
      skip('set_play_mode_status', 'disrupts the Editor session - pass --include-expensive to run');
      skip('show_unity_dashboard', 'opens VS Code UI - pass --include-expensive to run');
    }
    skip('add_package', 'no corresponding remove-package tool exists to clean up after it');

    console.log('\n=== Cleanup ===');
    await cleanupFixtures(client);

    await reportCoverageGaps(client);
  } finally {
    await client.close();
  }

  printSummary();
  process.exitCode = results.some((r) => r.status === 'fail') ? 1 : 0;
}

// ---------------------------------------------------------------------------
// groups
// ---------------------------------------------------------------------------
async function runReadOnlyGroup(client) {
  await step('get_scene_info', () => callTool(client, 'get_scene_info'));
  await step('get_scenes_hierarchy', () => callTool(client, 'get_scenes_hierarchy'));
  await step('get_play_mode_status', () => callTool(client, 'get_play_mode_status'));
  await step('get_console_logs', () => callTool(client, 'get_console_logs', { limit: 5 }));
  await step('get_compilation_errors', () => callTool(client, 'get_compilation_errors'));
  await step('find_local_assets', () => callTool(client, 'find_local_assets', { maxResults: 5 }));
  await step('find_script_references', () =>
    // "Transform" is on every GameObject, so this is a universally valid, always-present
    // target regardless of what (if any) custom scripts the target project has.
    callTool(client, 'find_script_references', { scriptName: 'Transform' })
  );
  await step('run_tests', () =>
    // The MCP SDK client's own default request timeout (60s) is shorter than Unity's
    // TestRunnerService wait (600s, matching Node's TOOL_TIMEOUTS_MS.run_tests) - without this
    // override the client would abandon the call and move on while the real test run keeps
    // executing in Unity's background, exactly the race this project hit once already.
    callTool(client, 'run_tests', { testMode: 'EditMode', returnOnlyFailures: true }, { timeout: 610000 })
  );
  await step('send_console_log', () =>
    callTool(client, 'send_console_log', { message: 'mcp_livetest: send_console_log check', type: 'info' })
  );

  // raycast straight down from a high point - works in any project regardless of scene contents,
  // and does not require a hit to succeed (hit: false is still success: true).
  await step('raycast_query', () =>
    callTool(client, 'raycast_query', {
      origin: { x: 0, y: 1000, z: 0 },
      direction: { x: 0, y: -1, z: 0 },
      maxDistance: 2000,
    })
  );
  await step('get_floor_height', () => callTool(client, 'get_floor_height', {}));
  await step('get_nearby_objects', () =>
    callTool(client, 'get_nearby_objects', { position: { x: 0, y: 0, z: 0 }, radius: 5 })
  );
}

let fixturesCreated = false;
let createdPrefabPath = null;

async function cleanResidualFixtures(client) {
  try {
    const res = await callTool(client, 'get_scenes_hierarchy');
    let hierarchy = res.structuredContent?.hierarchy;
    if (!hierarchy) {
      const text = textOf(res);
      if (text) {
        try {
          hierarchy = JSON.parse(text);
        } catch { }
      }
    }
    if (hierarchy && !Array.isArray(hierarchy) && Array.isArray(hierarchy.hierarchy)) {
      hierarchy = hierarchy.hierarchy;
    }
    const idsToDelete = [];

    function traverse(node) {
      if (!node) return;
      if (typeof node.name === 'string' && (node.name === SCRATCH.root || node.name === SCRATCH.dup || node.name.startsWith('mcp_livetest_'))) {
        if (node.instanceId != null) {
          idsToDelete.push(node.instanceId);
        }
      }
      if (Array.isArray(node.children)) {
        for (const child of node.children) traverse(child);
      }
      if (Array.isArray(node.rootObjects)) {
        for (const root of node.rootObjects) traverse(root);
      }
    }

    if (Array.isArray(hierarchy)) {
      for (const scene of hierarchy) {
        traverse(scene);
      }
    }

    for (const instanceId of idsToDelete) {
      try {
        await callTool(client, 'delete_gameobject', { instanceId });
      } catch { }
    }

    try {
      await callTool(client, 'delete_asset', { assetPath: SCRATCH.folder });
    } catch { }
  } catch { }
}

async function setupFixtures(client) {
  try {
    // Clean up any leftover fixtures from previously aborted runs first
    await cleanResidualFixtures(client);

    // update_gameobject creates a fresh, minimal GameObject at objectPath if none exists there -
    // this avoids depending on (or mutating) anything already in the target project's scene.
    await callTool(client, 'update_gameobject', {
      objectPath: SCRATCH.root,
      gameObjectData: { name: SCRATCH.root },
    });
    covered.add('duplicate_gameobject');
    await callTool(client, 'duplicate_gameobject', {
      objectPath: SCRATCH.root,
      newName: SCRATCH.dup,
    });
    // get_bounds and place_next_to both require a renderer or collider on every object
    // involved (SpatialToolUtils.TryGetBounds returns false otherwise) - a bare GameObject
    // has neither, so give both fixtures a BoxCollider before anything that needs bounds runs.
    await callTool(client, 'update_component', { objectPath: SCRATCH.root, componentName: 'BoxCollider' });
    await callTool(client, 'update_component', { objectPath: SCRATCH.dup, componentName: 'BoxCollider' });
    // assign_material requires an existing Renderer with at least one material slot -
    // a freshly added MeshRenderer defaults to one slot (the built-in default material) even
    // with no MeshFilter/mesh assigned, which is enough to exercise slot 0.
    await callTool(client, 'update_component', { objectPath: SCRATCH.root, componentName: 'MeshRenderer' });
    // Give the two scratch objects distinct positions so distance/nearby/place-next-to tests
    // are meaningful rather than operating on two coincident points.
    await callTool(client, 'set_transform', {
      objectPath: SCRATCH.dup,
      position: { x: 5, y: 0, z: 0 },
    });
    await callTool(client, 'create_folder', { parentFolder: 'Assets', newFolderName: 'mcp_livetest_folder' });
    fixturesCreated = true;
    console.log('  scratch fixtures ready: ' + SCRATCH.root + ', ' + SCRATCH.dup + ', ' + SCRATCH.folder);
    return true;
  } catch (err) {
    console.error('  fixture setup failed: ' + (err instanceof Error ? err.message : err));
    return false;
  }
}

async function runMutatingGroup(client) {
  await step('select_gameobject', () => callTool(client, 'select_gameobject', { objectPath: SCRATCH.root }));
  await step('get_gameobject (tool)', () => callTool(client, 'get_gameobject', { idOrName: SCRATCH.root }));
  await step('get_bounds', () => callTool(client, 'get_bounds', { objectPath: SCRATCH.root }));
  await step('move_gameobject', () =>
    callTool(client, 'move_gameobject', { objectPath: SCRATCH.root, position: { x: 0, y: 1, z: 0 } })
  );
  await step('rotate_gameobject', () =>
    callTool(client, 'rotate_gameobject', { objectPath: SCRATCH.root, rotation: { x: 0, y: 45, z: 0 } })
  );
  await step('scale_gameobject', () =>
    callTool(client, 'scale_gameobject', { objectPath: SCRATCH.root, scale: { x: 2, y: 2, z: 2 } })
  );
  await step('set_transform', () =>
    callTool(client, 'set_transform', {
      objectPath: SCRATCH.root,
      position: { x: 0, y: 0, z: 0 },
      rotation: { x: 0, y: 0, z: 0 },
      scale: { x: 1, y: 1, z: 1 },
    })
  );
  await step('measure_distance', () =>
    callTool(client, 'measure_distance', { objectPath: SCRATCH.root, referencePath: SCRATCH.dup })
  );
  await step('place_next_to', () =>
    callTool(client, 'place_next_to', {
      objectPath: SCRATCH.dup,
      referencePath: SCRATCH.root,
      direction: 'right',
      distance: 1,
    })
  );
  await step('frame_camera_on', () => callTool(client, 'frame_camera_on', { objectPath: SCRATCH.root }));
  await step('reparent_gameobject', () =>
    callTool(client, 'reparent_gameobject', { objectPath: SCRATCH.dup, newParent: SCRATCH.root })
  );
  await step('set_sibling_index', () =>
    callTool(client, 'set_sibling_index', { objectPath: SCRATCH.dup, siblingIndex: 0 })
  );
  // Undo the reparent so delete_gameobject(root) doesn't also delete dup as a side effect below -
  // both are deleted explicitly and independently in cleanup.
  await step('reparent_gameobject (restore)', () =>
    callTool(client, 'reparent_gameobject', { objectPath: SCRATCH.root + '/' + SCRATCH.dup, newParent: null })
  );

  await step('update_gameobject', () =>
    callTool(client, 'update_gameobject', {
      objectPath: SCRATCH.root,
      gameObjectData: { tag: 'Untagged' },
    })
  );
  await step('update_component', () =>
    callTool(client, 'update_component', {
      objectPath: SCRATCH.root,
      componentName: 'Transform',
      componentData: { localPosition: { x: 0, y: 0.5, z: 0 } },
    })
  );
  await step('copy_component', () =>
    callTool(client, 'copy_component', {
      componentType: 'Transform',
      sourceObjectPath: SCRATCH.root,
      targetObjectPath: SCRATCH.dup,
    })
  );
  await step('draw_scene_gizmo', () =>
    callTool(client, 'draw_scene_gizmo', {
      shapeType: 'sphere',
      position: { x: 0, y: 0, z: 0 },
      duration: 0.1,
    })
  );
  await step('capture_screenshot', () =>
    callTool(client, 'capture_screenshot', { view: 'scene', maxWidth: 256 })
  );
  await step('batch_execute', async () => {
    const result = await callTool(client, 'batch_execute', {
      operations: [
        { tool: 'get_scene_info', params: {} },
        { tool: 'get_play_mode_status', params: {} },
      ],
    });
    const results2 = result.structuredContent?.results;
    if (!Array.isArray(results2) || results2.length !== 2) {
      throw new Error(`expected 2 batched results, got: ${JSON.stringify(result.structuredContent)}`);
    }
  });

  // -- material lifecycle --
  const materialPath = `${SCRATCH.folder}/${SCRATCH.materialName}.mat`;
  await step('create_material', () =>
    callTool(client, 'create_material', {
      name: SCRATCH.materialName,
      savePath: materialPath,
      color: { r: 1, g: 0, b: 0, a: 1 },
    })
  );
  await step('assign_material', () =>
    callTool(client, 'assign_material', { objectPath: SCRATCH.root, materialPath })
  );
  await step('modify_material', () =>
    callTool(client, 'modify_material', {
      materialPath,
      properties: { _Metallic: 0.5 },
    })
  );
  await step('get_material_info', () => callTool(client, 'get_material_info', { materialPath }));

  // -- prefab + add_asset_to_scene --
  let prefabPath = null;
  let prefabUsable = false;
  await step('create_prefab', async () => {
    const result = await callTool(client, 'create_prefab', { prefabName: SCRATCH.prefabName });
    prefabPath = result.structuredContent?.prefabPath;
    if (!prefabPath || !prefabPath.startsWith('Assets/')) {
      throw new Error(`expected an Assets/-relative prefabPath, got: ${JSON.stringify(prefabPath)}`);
    }
    prefabUsable = true;
    createdPrefabPath = prefabPath;
  });
  if (prefabUsable) {
    await step('add_asset_to_scene', () =>
      callTool(client, 'add_asset_to_scene', { assetPath: prefabPath, parentPath: SCRATCH.root })
    );
  } else {
    // A failed create_prefab already reported the root cause above - running
    // add_asset_to_scene against a path known not to resolve would just be a duplicate
    // symptom of the same failure, not a second independent finding.
    skip('add_asset_to_scene', 'create_prefab did not produce a usable prefabPath - see above');
  }

  // -- import_local_file --
  const tempFile = path.join(os.tmpdir(), 'mcp_livetest_import.txt');
  fs.writeFileSync(tempFile, 'mcp live-endpoint-test scratch file');
  try {
    await step('import_local_file', () =>
      callTool(client, 'import_local_file', {
        sourcePath: tempFile,
        destFolder: SCRATCH.folder,
        overwrite: true,
      })
    );
  } finally {
    fs.rmSync(tempFile, { force: true });
  }

  // -- delete_asset / rename_asset (on a throwaway sub-scratch, not the shared folder) --
  await step('create_folder (rename target)', () =>
    callTool(client, 'create_folder', { parentFolder: SCRATCH.folder, newFolderName: 'rename_me' })
  );
  await step('rename_asset', () =>
    callTool(client, 'rename_asset', { assetPath: `${SCRATCH.folder}/rename_me`, newName: 'renamed' })
  );
  await step('delete_asset', () => callTool(client, 'delete_asset', { assetPath: `${SCRATCH.folder}/renamed` }));

  // -- export_package (output goes to the OS temp dir, not the project) --
  const exportPath = path.join(os.tmpdir(), 'mcp_livetest_export.unitypackage');
  await step('export_package', async () => {
    await callTool(client, 'export_package', {
      assetPath: SCRATCH.folder,
      exportPath,
      includeDependencies: false,
    });
    if (!fs.existsSync(exportPath)) {
      throw new Error(`export_package reported success but ${exportPath} does not exist`);
    }
  });
  fs.rmSync(exportPath, { force: true });

  // -- scene lifecycle: create (not made active) -> load additively -> save -> unload -> delete --
  const scratchScenePath = 'Assets/mcp_livetest_folder/mcp_livetest_scene.unity';
  let sceneCreated = false;
  await step('create_scene', async () => {
    const result = await callTool(client, 'create_scene', {
      sceneName: 'mcp_livetest_scene',
      folderPath: SCRATCH.folder,
      makeActive: false,
    });
    sceneCreated = true;
    if (result.structuredContent?.scenePath && result.structuredContent.scenePath !== scratchScenePath) {
      console.log(`  (scene saved at ${result.structuredContent.scenePath}, not the expected path - AssetDatabase likely deduplicated an existing file)`);
    }
  });
  if (sceneCreated) {
    await step('load_scene (additive)', () =>
      callTool(client, 'load_scene', { scenePath: scratchScenePath, additive: true })
    );
    await step('save_scene', () => callTool(client, 'save_scene', {}));
    await step('unload_scene', () => callTool(client, 'unload_scene', { scenePath: scratchScenePath }));
    await step('delete_scene', () => callTool(client, 'delete_scene', { scenePath: scratchScenePath }));
  } else {
    for (const n of ['load_scene (additive)', 'save_scene', 'unload_scene', 'delete_scene']) {
      skip(n, 'create_scene failed, nothing to load/save/unload/delete');
    }
  }

  await step('recompile_scripts', () => callTool(client, 'recompile_scripts', { returnWithLogs: false }));

  // Assets/Refresh is idempotent and side-effect-free (just re-scans for on-disk changes),
  // unlike almost every other menu path, which is why it's the one used throughout this
  // suite's own manual verification history too - and why it's safe to retry here (see
  // callToolRetryOnConnectionDrop) if it lands while recompile_scripts' domain reload above
  // is still settling the connection.
  await step('execute_menu_item', () =>
    callToolRetryOnConnectionDrop(client, 'execute_menu_item', { menuPath: 'Assets/Refresh' })
  );
}

async function cleanupFixtures(client) {
  covered.add('delete_gameobject');
  // Clean up all residual test GameObjects and folders
  await cleanResidualFixtures(client);

  if (createdPrefabPath) {
    try {
      await callTool(client, 'delete_asset', { assetPath: createdPrefabPath });
      console.log(`  cleaned up: delete_asset(${JSON.stringify({ assetPath: createdPrefabPath })})`);
    } catch (err) {
      console.error(`  cleanup failed: delete_asset(${JSON.stringify({ assetPath: createdPrefabPath })}) - ${err instanceof Error ? err.message : err}`);
    }
  }
}

async function runResourceGroup(client) {
  await step('resource: unity://scenes_hierarchy', () => readResource(client, 'unity://scenes_hierarchy'));
  await step('resource: unity://menu-items', () => readResource(client, 'unity://menu-items'));
  await step('resource: unity://packages', () => readResource(client, 'unity://packages'));
  await step('resource: unity://assets', () => readResource(client, 'unity://assets'));
  await step('resource: unity://ui/dashboard', () => readResource(client, 'unity://ui/dashboard'));
  await step('resource: unity://gameobject/{idOrName}', () =>
    readResource(client, `unity://gameobject/${encodeURIComponent(SCRATCH.root)}`)
  );
  await step('resource: unity://tests/{testMode}', () => readResource(client, 'unity://tests/EditMode'));
  await step('resource: unity://logs/{logType}', () =>
    readResource(client, 'unity://logs/info?offset=0&limit=5&includeStackTrace=false')
  );
}

async function runPromptGroup(client) {
  await step('prompt: gameobject_handling_strategy', async () => {
    const result = await client.getPrompt({
      name: 'gameobject_handling_strategy',
      arguments: { gameObjectIdOrName: SCRATCH.root },
    });
    if (!result.messages || result.messages.length === 0) throw new Error('empty messages array');
  });
  await step('prompt: unity_dashboard', async () => {
    const result = await client.getPrompt({ name: 'unity_dashboard', arguments: {} });
    if (!result.messages || result.messages.length === 0) throw new Error('empty messages array');
  });
}

async function runExpensiveGroup(client) {
  // set_play_mode_status: exercised as play -> confirm -> stop -> confirm, restoring edit mode
  // before returning control either way.
  await step('set_play_mode_status (play/stop round trip)', async () => {
    await callTool(client, 'set_play_mode_status', { action: 'play' });
    await new Promise((r) => setTimeout(r, 500));
    const status1 = await callTool(client, 'get_play_mode_status');
    try {
      if (status1.structuredContent?.isPlaying !== true) {
        throw new Error(`expected isPlaying: true after 'play', got ${JSON.stringify(status1.structuredContent)}`);
      }
    } finally {
      await callTool(client, 'set_play_mode_status', { action: 'stop' });
      await new Promise((r) => setTimeout(r, 500));
    }
    const status2 = await callTool(client, 'get_play_mode_status');
    if (status2.structuredContent?.isPlaying !== false) {
      throw new Error(`expected isPlaying: false after 'stop', got ${JSON.stringify(status2.structuredContent)}`);
    }
  });

  await step('show_unity_dashboard', () => callTool(client, 'show_unity_dashboard'));

  // build_project needs at least one enabled Build Settings scene - create a throwaway one,
  // exactly as verified manually earlier in this project's history.
  const buildScenePath = 'Assets/mcp_livetest_folder/mcp_livetest_build_scene.unity';
  let buildSceneCreated = false;
  await step('create_scene (for build_project)', async () => {
    await callTool(client, 'create_scene', {
      sceneName: 'mcp_livetest_build_scene',
      folderPath: SCRATCH.folder,
      addToBuildSettings: true,
      makeActive: false,
    });
    buildSceneCreated = true;
  });

  if (buildSceneCreated) {
    const outDir = fs.mkdtempSync(path.join(os.tmpdir(), 'mcp_livetest_build_'));
    const outputPath = path.join(outDir, 'McpLiveTestBuild.exe');
    try {
      await step('build_project', () =>
        // Same client-side timeout hazard as run_tests: the MCP SDK client's 60s default is
        // shorter than Node's own 30-minute build_project override, so this needs its own
        // explicit ceiling too.
        callTool(client, 'build_project', {
          target: 'StandaloneWindows64',
          outputPath,
          options: ['Development'],
        }, { timeout: 1810000 })
      );
    } finally {
      fs.rmSync(outDir, { recursive: true, force: true });
      try {
        await callTool(client, 'delete_scene', { scenePath: buildScenePath });
      } catch (err) {
        console.error(`  cleanup failed: delete_scene(build scene) - ${err instanceof Error ? err.message : err}`);
      }
    }
  } else {
    skip('build_project', 'create_scene for Build Settings failed');
  }
}

// ---------------------------------------------------------------------------
// coverage + summary
// ---------------------------------------------------------------------------
async function reportCoverageGaps(client) {
  try {
    const { tools } = await client.listTools();
    const uncoveredTools = tools
      .map((t) => t.name)
      .filter((name) => ![...covered].some((c) => c === name || c.startsWith(name + ' ') || c.includes(`'${name}'`)));
    if (uncoveredTools.length > 0) {
      console.log('\n=== Coverage gap: tools with no test in this suite ===');
      for (const name of uncoveredTools) console.log(`  - ${name}`);
    }
  } catch (err) {
    console.error(`Could not compute coverage gaps: ${err instanceof Error ? err.message : err}`);
  }
}

function printSummary() {
  const pass = results.filter((r) => r.status === 'pass').length;
  const fail = results.filter((r) => r.status === 'fail').length;
  const skipped = results.filter((r) => r.status === 'skip').length;

  console.log('\n' + '='.repeat(60));
  console.log(`Live endpoint test: ${pass} passed, ${fail} failed, ${skipped} skipped`);
  if (fail > 0) {
    console.log('\nFailures:');
    for (const r of results.filter((r) => r.status === 'fail')) {
      console.log(`  ✗ ${r.name}: ${r.detail}`);
    }
  }
  console.log('='.repeat(60));
}

main().catch((err) => {
  console.error('Fatal error running live endpoint test:', err);
  process.exitCode = 1;
});
