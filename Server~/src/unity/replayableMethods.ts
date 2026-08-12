/**
 * Methods that are safe to re-send after the WebSocket drops mid-request.
 *
 * Unity performs no request-id deduplication, so a request that was already received and executed
 * before the socket died would be executed a SECOND time if replayed. That is fine for pure reads
 * and harmless for genuinely idempotent operations, but not for anything that creates, deletes, or
 * mutates state: replaying `duplicate_gameobject` after Unity already handled it silently produces
 * two duplicates.
 *
 * So this is a strict allowlist of read-only methods. Anything absent is failed fast with a clear
 * "outcome unknown" error instead of being retried or left to hang until its timeout.
 *
 * Deliberately excluded even though they are read-ish:
 * - `run_tests`: replaying restarts an entire suite (and it carries a 300s timeout).
 * - `recompile_scripts`: triggers another domain reload, which is what dropped the socket.
 */
const REPLAYABLE_METHODS: ReadonlySet<string> = new Set([
  // Scene / GameObject reads
  'get_gameobject',
  'get_scenes_hierarchy',
  'get_scene_info',
  'get_play_mode_status',
  // Spatial reads
  'get_bounds',
  'measure_distance',
  'get_floor_height',
  'get_nearby_objects',
  // Asset / material reads
  'find_local_assets',
  'get_material_info',
  // Diagnostics
  'get_console_logs'
]);

export function isReplayableMethod(method: string): boolean {
  return REPLAYABLE_METHODS.has(method);
}
