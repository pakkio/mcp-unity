# ChilloutVR CCK Integration Guide

This guide explains how to use **MCP Unity** to build, configure, script, scaffold, and optimize content for **ChilloutVR** using the **ABI Content Creation Kit (CCK)**.

---

## 1. Overview & Architecture

ChilloutVR (by Alpha Blend Interactive) uses Unity to create worlds, avatars, and spawnable props. For security and cross-platform safety, ChilloutVR does not permit arbitrary custom C# scripts in uploaded AssetBundles. Instead, creators rely on the CCK component ecosystem:

- **World Roots**: `CVRWorld`, `CVRSpawnPoint`, `CVRMirror`, `CVRSeat`, `CVRPortal`.
- **Interactivity & State**: `CVRInteractable`, `CVRPickupObject`, `CVRVariableBuffer`.
- **Avatars**: `CVRAvatar`, `CVRAdvancedAvatarSettings` (AAS), `CVRFaceTracking`.
- **Physics & Locomotion**: Standard Unity `Rigidbody`, `WheelCollider`, and CCK seats.

### Unity Version Requirement
> [!IMPORTANT]
> **Official ChilloutVR CCK Unity Version**: **`Unity 2022.3.58f1`**
>
> All ChilloutVR content (Worlds, Avatars, Props) must be built with **Unity 2022.3.58f1** to ensure upload compatibility and runtime parity with the ChilloutVR game client. MCP Unity is tested and fully compatible with Unity 2022.3.58f1.

MCP Unity provides soft-linked tools that interact natively with CCK components when installed, and provide clean fallbacks and template scaffolding if CCK is not yet imported.

---

## 2. Dedicated CCK Tools

### 1. `manage_cvr_world`
Configures ChilloutVR world settings and environmental fixtures.

- **`setup_world`**: Creates the `CVRWorld` root GameObject with:
  - `respawnHeight` (e.g. `-50.0` meters)
  - `runSpeed`, `sprintMultiplier`, `jumpHeight`
  - `allowFlight`, `allowTeleport`
  - `gravity` vector
- **`add_spawn_point`**: Spawns and aligns `CVRSpawnPoint` objects and links them to the active world.
- **`create_mirror`**: Spawns an optimized `CVRMirror` with configurable culling masks (`Optimized`, `AvatarOnly`, `Transparent`, `Cutout`, `Full`).
- **`create_seat`**: Creates or converts any GameObject into an interactive sitting `CVRSeat`.
- **`create_portal`**: Creates a `CVRPortal` gateway linking to a target `worldId` and `instanceId`.

**Example:**
```json
{
  "method": "manage_cvr_world",
  "params": {
    "action": "setup_world",
    "respawnHeight": -50,
    "runSpeed": 4.5,
    "allowFlight": true
  }
}
```

---

### 2. `configure_cvr_interactivity`
Wires up interactive triggers, physics pickups, and multiplayer networked variable synchronization.

- **`add_interactable`**: Adds `CVRInteractable` with trigger mechanisms (`interact`, `grab`, `touch`, `look_at`, `area_trigger`) and action handlers (`toggle_gameobject`, `teleport_player`, `play_audio`, `set_animator_param`, `spawn_prefab`).
- **`configure_pickup`**: Attaches `CVRPickupObject` with custom grip points, auto-hold toggles, and throw velocity multipliers.
- **`setup_variable_buffer`**: Creates `CVRVariableBuffer` network synchronized variables (`bool`, `float`, `int`, `string`) for multiplayer logic.

**Example:**
```json
{
  "method": "configure_cvr_interactivity",
  "params": {
    "action": "add_interactable",
    "objectPath": "LightSwitch",
    "interactionType": "interact",
    "actionType": "toggle_gameobject",
    "targetObjectPath": "CeilingLight"
  }
}
```

---

### 3. `manage_cvr_avatar`
Automates avatar setup, viewpoint calculations, lip-sync, and AAS menus.

- **`setup_avatar`**:
  - Automatically calculates exact eye viewpoint (`viewPosition`) and mouth voice position from Humanoid `Animator` bones or mesh bounds.
  - Detects face meshes and connects 15 Oculus visemes and eye blink blendshapes.
- **`configure_aas`**: Configures Advanced Avatar Settings (AAS) radial wheels, toggles, float sliders, sub-menus, and color pickers.
- **`setup_face_tracking`**: Binds unified face tracking blendshapes to `CVRFaceTracking`.

**Example:**
```json
{
  "method": "manage_cvr_avatar",
  "params": {
    "action": "setup_avatar",
    "objectPath": "RobotAvatar"
  }
}
```

---

### 4. `configure_cvr_vehicle`
Creates and tunes drivable 4-wheel vehicles and passenger seating.

- **`create_car_rig`**:
  - Creates a `Rigidbody` (mass 1200kg, interpolate, dynamic continuous collision).
  - Sets an offset low center of mass (`CenterOfMass` at Y = -0.35m) to prevent rollovers.
  - Adds 4 `WheelCollider`s with tuned spring/damper suspension.
  - Sets up driver `CVRSeat`, steering wheel with `CVRInteractable`, headlights, engine `AudioSource`, and `CVRVariableBuffer`.
- **`configure_suspension`**: Fine-tunes spring forces, damper rates, suspension travel distance, and tire friction curves.
- **`add_passenger_seats`**: Spawns and aligns passenger `CVRSeat` components (up to 8 seats).

**Example:**
```json
{
  "method": "configure_cvr_vehicle",
  "params": {
    "action": "create_car_rig",
    "vehicleName": "DuneBuggy",
    "mass": 1400,
    "spring": 35000,
    "damper": 5000
  }
}
```

---

### 5. `inspect_cvr_cck` (Pre-Flight Validation & Audit)
Performs automated pre-upload validation and performance audits against ChilloutVR limits:

- Verifies required root components (`CVRWorld` or `CVRAvatar`).
- Checks polygon/triangle budgets:
  - Worlds: Recommended under 500,000–1,500,000 triangles.
  - Avatars: Recommended under 70,000 triangles for Good rating.
- Audits SkinnedMeshRenderer counts and material slot counts.
- Verifies AudioSource 3D spatialization (prevents 2D audio world flooding).
- Detects disallowed custom scripts.
- Returns a structured `PASS`, `WARNING`, or `ERROR` report with actionable optimization advice.

**Example:**
```json
{
  "method": "inspect_cvr_cck",
  "params": {
    "action": "validate_content",
    "contentType": "world"
  }
}
```

---

### 6. `howto_cck` (Recipes & Scaffolding)
Provides production recipes and instant GameObject hierarchy scaffolding.

- Supports wildcard and topic searches (`*`, `list`, `veh*`, `door*`, `mirror*`, `opt*`).
- Generates step-by-step best practices.
- When `scaffold: true` is set, creates and wires up the ready-to-use GameObject structure directly in your Unity scene.

**Supported Topics**:
- `vehicles`: Drivable cars, suspension math, low center of mass, CVRSeat, steering wheel.
- `door`: Interactive sliding/swinging doors with `Animator`, `CVRInteractable`, and multiplayer sync.
- `mirror`: Performance-tuned `CVRMirror` modes and layer culling masks.
- `elevator`: Moving platforms, floor state animators, and avatar parenting.
- `pickup`: Physics props, weapons, custom grips, and throw velocity.
- `aas`: Advanced Avatar Settings radial menus, sliders, and color pickers.
- `video_player`: 16:9 screen quads with 3D audio attenuation.
- `portals`: Gateway hubs and sub-world loading.
- `optimization`: Pre-upload checklists for draw calls, lightmap baking, and probe grids.

---

## 3. Synergy with Core MCP Unity Tools

When building for ChilloutVR, combine CCK tools with:
- **`probuilder_create_shape`**: Greybox world geometry, rooms, ramps, and obstacles.
- **`manage_terrain`**: Sculpt world terrain with Perlin noise, textures, and foliage.
- **`manage_lighting` & `configure_light_probe_group`**: Bake static illumination and generate automated 3D light probe grids for dynamic avatar lighting.
- **`configure_colliders`**: Generate optimized convex mesh colliders for world floors and physics props.
- **`configure_texture_settings`**: Optimize texture memory, max sizes, and crunch compression before upload.
