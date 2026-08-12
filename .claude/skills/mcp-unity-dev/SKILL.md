---
name: mcp-unity-dev
description: Use when working in this repo (mcp-unity) via the mcp-unity MCP tools - either editing scene/GameObjects through the tools, or modifying the mcp-unity C#/TypeScript package itself. Covers tool gotchas discovered through hands-on debugging (object references, hierarchy reordering, duplicate corruption), the git-package update/verify workflow, and Unity vehicle-physics pitfalls (glTF import rotation, WheelCollider inertia). Load before touching WheelCollider/Rigidbody setups, before assuming a tool works a certain way, or before adding a new mcp-unity tool.
---

# mcp-unity development notes

Hard-won knowledge from actually using and extending mcp-unity in this repo. Read the section relevant to your task before you start, not after you hit the bug.

## If you're modifying the mcp-unity package itself

The Unity project that actually runs this code (`My project (1)`) pulls it as a **git package**, not a local reference — editing files in this repo does nothing to the running Unity Editor until you:

1. Edit the C# (`Editor/Tools/*.cs`, `McpUnityServer.cs`) and/or TypeScript (`Server~/src/tools/*.ts`, `index.ts`)
2. **New `.cs` files need a `.meta` file** — Unity treats git packages as immutable and silently *drops* any asset without one (no error, the type just doesn't exist, giving a confusing `CS0246` in an unrelated file). Generate a GUID (`powershell -Command "[guid]::NewGuid().ToString('N')"`) and write the meta in the same format as a sibling file.
3. `cd Server~ && npm run build` to catch TypeScript errors
4. Commit (no AI co-author line — this user's preference), push to `origin/main`
5. In Unity: **Package Manager → In Project → MCP Unity → Update** (or remove/re-add if no Update button). This triggers a domain reload.
6. Verify via `get_console_logs` — check the `PackageCache` folder hash in any log line matches your new commit SHA, and confirm no compile errors before using the new tool.

Tool registration is two-sided and easy to half-do: a new tool needs a Unity `McpToolBase` subclass registered in `McpUnityServer.cs RegisterTools()`, *and* a Node `registerXTool()` in `Server~/src/tools/*.ts` registered in `index.ts`. Forgetting either side gives a confusing "tool not found" or silently does nothing.

## Tool gotchas (don't rediscover these empirically)

- **`reparent_gameobject` does not reorder the Hierarchy.** It changes parent only; Unity's `SetParent` is a no-op for sibling index when the parent is unchanged, and even a genuine temp-parent bounce doesn't reliably reorder. Use `set_sibling_index` instead (absolute `siblingIndex`, or `insertAfterInstanceId`/`insertBeforeInstanceId` relative to a sibling).
- **`duplicate_gameobject` on a nested child under a scaled/rotated parent used to corrupt the clone's transform.** Fixed by instantiating with the parent specified directly (`Instantiate(original, parent)`) instead of parentless-then-reparent. If you ever see a duplicate land at a wildly wrong position/rotation/scale, this is the failure mode to suspect.
- **Component fields that reference other scene objects** (a `Transform`, `Rigidbody`, or another `Component`) go through `update_component`'s `componentData` using `{"instanceId": N}` or `{"objectPath": "Parent/Child"}` — *not* `{"path": ...}`/`{"guid": ...}`, which are asset-only (AssetDatabase). If the target field type doesn't match the resolved object directly (e.g. field is `Rigidbody` but you passed the GameObject's ID), it falls back to `GetComponent` automatically. `List<T>`/array fields accept a JSON array of the same per-element shapes.
- **`WheelCollider`, `Collider`, and a few other components are deliberately excluded from `get_gameobject`'s property dump** (`"_skipped": "Detailed property serialization skipped for safety"`). You cannot read back `radius`, `isTrigger`, `suspensionSpring`, etc. through the read tool — you can still *write* them via `update_component`, you just have to verify by testing behavior (does it fall through the floor? does it detect ground?), not by reading the value back.
- **`set_play_mode_status action:"play"` (and `"stop"`) frequently returns `"Connection failed"` or times out** — this is expected, it's the domain reload dropping the WebSocket mid-response. The action still goes through; `sleep 5-8` then poll `get_play_mode_status` to confirm.
- **Unity discards all runtime changes to existing scene objects when you exit Play mode** (auto-revert to the edit-time saved state). If you need to inspect what happened during a Play session, query *while still in Play mode* — don't stop first.
- Prefer `batch_execute` for any run of >2-3 related tool calls — it's dramatically faster than sequential calls and each op gets its own success/failure in the summary.

## Unity vehicle physics pitfalls (WheelCollider + Rigidbody)

Both of these produce the same symptom — **the Rigidbody free-falls through the floor with zero collision response, forever** — so don't assume it's a floor/collider problem without isolating first (see below).

1. **glTF-imported root rotation breaks anything computing motion via `transform.forward`/`transform.up`.** glTFast (and similar importers) can leave the top-level imported node with a raw axis-conversion rotation (e.g. `eulerAngles (270,0,0)`) that's visually invisible because a child node has the exact compensating rotation. But it means `transform.forward` no longer points where the model *looks* like it's facing — it can point straight up. Anything doing `Vector3.Dot(transform.forward, rigidbody.velocity)` (common in custom vehicle controllers, e.g. CVR's `CVRWheelHubController`) will then measure the wrong thing entirely (vertical fall speed instead of forward speed), silently breaking torque/speed curves.
   - **Fix:** zero the rotation on the physics root, and cancel it on the immediate glTF child node so the visual mesh doesn't move. Recompute and reapply the world positions of anything else parented under the root (wheels, seats, etc.) after, since their local values will have shifted to compensate.
   - **Diagnostic:** check the root's `eulerAngles` (not `localEulerAngles`) and its `forward`/`up` vectors. If `up` isn't close to `(0,1,0)`, this is your bug.
2. **A Rigidbody driven purely by `WheelCollider`s (no other collider on the body) gets a degenerate inertia tensor** — `inertiaTensor: (1,1,1)` instead of a realistic mass-distribution value. WheelColliders don't contribute mass/inertia to the Rigidbody they're attached to. The suspension physics needs a sane inertia tensor to stabilize, so without a real body collider the car just sinks through the ground no matter how the suspension spring/distance is tuned.
   - **Fix:** add a `BoxCollider` (or similar) sized to the vehicle body directly on the Rigidbody GameObject. Watch `inertiaTensor` in a `get_gameobject` read — it should jump from `(1,1,1)` to real numbers (e.g. `(1856, 2007, 443)`) once a body collider is present.
   - **Sanity check after fixing:** the car should settle *slightly above* its pre-drop resting height (suspension compression holding it up), not sink to the exact starting height.

**Isolation method when something falls through the floor:** don't guess — test with a plain `BoxCollider` + `Rigidbody` positioned with **no initial overlap** with the floor (this matters: a box that starts already embedded in a thin non-convex MeshCollider can itself tunnel through, giving a false negative). A clean object that lands and settles proves the floor collider and layer collision matrix are fine, narrowing the bug to the vehicle setup specifically.

## Sketchfab → Unity import workflow

- Requires the `com.unity.cloud.gltfast` package (add via `add_package`) to import `.glb`/`.gltf` — Unity has no built-in importer.
- Sketchfab models often have **completely generic node names** (`Cube.003_6`, `Circle.038_1`, `Object_47`, ...) with no indication of what's a wheel vs. body panel. To find specific parts (e.g. wheels) programmatically: pull `MeshRenderer.bounds` (world-space) for every leaf mesh node and cluster by position/size — a wheel cluster shows up as a small, roughly-cubic bounding box near the corners of the overall model, distinct from large flat body panels.
- Many mesh transform nodes have **identity local transform** (`position` equal to the parent/root's world position, rotation/scale identity) because the actual shape offset is baked into the mesh vertex data, not the transform. Don't assume a node's `Transform.position` tells you where its geometry actually is — check `MeshRenderer.bounds` instead.
