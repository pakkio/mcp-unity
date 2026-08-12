/**
 * Per-tool request timeout overrides, in milliseconds.
 *
 * The default request timeout (McpUnitySettings.RequestTimeoutSeconds, 10s minimum) is sized
 * for ordinary scene/GameObject operations. A handful of tools routinely run longer than that -
 * package resolution, domain reloads, test runs - and were silently relying on
 * McpUnity.sendRequest's unused `options.timeout` override never actually being passed. That
 * meant every one of these was one slow CI machine away from a spurious timeout.
 */
export const TOOL_TIMEOUTS_MS: Record<string, number> = {
  run_tests: 300000,           // Full suites can run for several minutes
  recompile_scripts: 60000,    // Domain reload + compilation
  set_play_mode_status: 30000, // 'play'/'stop' trigger a domain reload that drops the socket mid-response
  capture_screenshot: 30000,   // Large scene view / high maxWidth encoding
  add_package: 180000,         // Git/registry resolution can be slow or network-bound
  export_package: 60000,       // Large asset trees with dependencies
  create_prefab: 30000,
  import_local_file: 60000     // Large source files (e.g. glTF models) plus AssetDatabase import
};

export function getToolTimeout(toolName: string): number | undefined {
  return TOOL_TIMEOUTS_MS[toolName];
}
