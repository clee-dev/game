# Build System — Editor Wiring Guide

Everything in this PR that's pure C# (`Assets/Scripts/Blueprint/`, `Assets/Scripts/Build/`,
the `PlayerInteraction`/`InputReader`/`PhysicsPickup` edits, `Packages/manifest.json`,
`Assets/StreamingAssets/Blueprints/blueprint_001.json`) is already done and will compile
as-is. What's left is Editor-only work that can't be done by hand-editing YAML safely:
creating 5 prefabs, registering 3 of them as network prefabs, dropping one `BuildSystem`
object into the level scene, and fixing up two fields on the existing `Player` prefab.

Do these in order — each step depends on the previous one existing.

## 1. Create the item prefabs

### WoodPlank

A new prefab, components in this order:
- `Rigidbody`
- `Collider` (e.g. `BoxCollider`, not a trigger — see the raycast note in §4)
- `NetworkObject`
- `ClientNetworkTransform`
- `PhysicsPickup`
- `MaterialItem` — set **Material Type = Wood**

### Hammer

Same stack, swap the last component:
- `Rigidbody`, `Collider`, `NetworkObject`, `ClientNetworkTransform`, `PhysicsPickup`
- `ToolItem` — set **Tool Type = Hammer**

### BuildTile

A new prefab. Grid cells are spaced `BuildSystem.CellSize` (2 world units) apart — tiles
are spawned at `gridPosition * CellSize`, and `BuildTile` recovers its grid coordinate with
`Vector3Int.RoundToInt(transform.position / CellSize)`:
- `NetworkObject` — **no** `ClientNetworkTransform` (tiles never move after spawning)
- `BoxCollider` noticeably **smaller than 2 units** (e.g. `1.4 × 1.4 × 1.4`) — this is what
  `PlayerInteraction`'s raycast hits. Keep it well under the 2-unit cell spacing so adjacent
  tiles have a real gap between their colliders; flush/touching colliders make it nearly
  impossible to raycast-aim at one tile over its neighbor (this is exactly what caused the
  "can only place on foundations, everything is too close together" issue during testing)
- `BuildTile` script, with:
  - `ghostRenderer` → a transparent unlit quad/cube child, shown while the tile is `Empty`
  - `placedMaterialVisual` → child object toggled on when state is `MaterialPlaced`
  - `builtVisual` → child object toggled on when state is `Built`
  - `eligibleColor` / `ineligibleColor` → tint for the ghost (defaults are fine to start)
  - `progressBarRoot` + `progressFill` → optional world-space canvas above the tile;
    `progressFill` should be a `RectTransform` that the script scales 0→1 on X

You can leave the visuals as simple grey cubes for now — none of the gameplay logic
depends on what they look like, only on the field references being non-null.

### SupplyZoneSpawner

A plain (non-networked) prefab — no `NetworkObject`:
- Just a `GameObject` with the `SupplyZoneSpawner` script
- `materialPrefab` → the **WoodPlank** prefab from above
- `respawnCooldown` → defaults to 5s, fine as-is

### ToolDepotSpawner

Same idea, for tools — a plain (non-networked) prefab, no `NetworkObject`:
- Just a `GameObject` with the `ToolDepotSpawner` script
- `toolPrefab` → the **Hammer** prefab from above
- `respawnCooldown` → defaults to 5s, fine as-is

Like supply zones, the tool type is decided by this prefab's own field, not read from
the blueprint's `toolDepots[].tools` array — fine for the MVP's single tool.

## 2. Register network prefabs

Open `Assets/DefaultNetworkPrefabs.asset` (or the NetworkManager's prefab list, same
thing) and add:
- `BuildTile`
- `WoodPlank`
- `Hammer`

`SupplyZoneSpawner` and `ToolDepotSpawner` are **not** `NetworkObject`s and must not be
added here.

## 3. Add BuildSystem to the level scene

In `Assets/Scenes/Game1.unity`:
1. Create an empty `GameObject` named `BuildSystem` (it does not need a `NetworkObject`
   — every client loads the same blueprint independently, see the class doc comment).
2. Add the `BuildSystem` script.
3. Assign:
   - `tilePrefab` → the **BuildTile** prefab
   - `supplyZoneSpawnerPrefab` → the **SupplyZoneSpawner** prefab
   - `toolDepotSpawnerPrefab` → the **ToolDepotSpawner** prefab
   - `blueprintId` → leave as `blueprint_001` (matches
     `Assets/StreamingAssets/Blueprints/blueprint_001.json`)

`BuildSystem.Start()` only spawns tiles/supply zones when `NetworkManager.Singleton.IsServer`
is true, so this works correctly for both a dedicated host and a listen-server host.

## 4. Wire the Player prefab

Open `Assets/Prefabs/Player.prefab` and select the `PlayerInteraction` component.

Two fields were added by this PR and are currently **unassigned**:

- **`playerCamera`** — ⚠️ this field is typed `UnityEngine.Camera`. The prefab already has
  a field of the same name on `NetworkPlayer` and `HubPlayerState`, but those point at the
  **`PlayerCamera` script** component on the `Main Camera` child, not the actual `Camera`
  component. For `PlayerInteraction.playerCamera`, drag the `Main Camera` child object
  itself (Unity will pick up its `Camera` component) — don't reuse the same reference
  those other scripts use, it's the wrong type for this field.
- **`interactPrompt`** — optional. Assign a screen-space `TextMeshProUGUI` element if you
  want the "[E] Pick Up" / "[E] Place Material" / "[E] Hold to Build" / "[E] Drop" prompt;
  leave it empty if you don't have one yet, the script null-checks it.

Already correct, no action needed:
- `holdPoint` is already wired to the existing "Hold Point" child under the camera.
- `throwForce` already carries its old value (8); `interactRange` is a new field that
  will default to `2.5` until you set it explicitly.

**Raycast note:** `PlayerInteraction` calls `Physics.Raycast` with no `QueryTriggerInteraction`
override, so it uses your project's default (Edit > Project Settings > Physics >
"Queries Hit Triggers"). If that's off (Unity's default), make sure the colliders on
WoodPlank/Hammer/BuildTile are **not** marked `Is Trigger`, or the raycast won't see them.

## 5. Known gaps (not blockers, just not implemented yet)

- **Furniture dependency rule**: the three design docs disagree (GDD/Blueprint Schema say
  "adjacent built wall", Systems Architecture's table says "built tile directly below").
  `BuildSystem.IsEligible` currently implements the Systems Architecture version. Flag if
  you want it switched — `blueprint_001` doesn't use the `furniture` tile type, so this
  doesn't block testing the current blueprint either way.

## 6. Fixed since initial setup (no Editor work needed)

- A bug where pressing E while holding the hammer over a buildable tile dropped the
  hammer instead of starting the build (the single-press handler didn't account for
  "holding a tool over a tile you can build" and fell through to drop). Fixed in
  `PlayerInteraction.HandleInteractPress`.
- A small centered crosshair, drawn via `OnGUI` in `PlayerInteraction` — turns yellow
  when looking at something interactable. No Canvas/Image setup required, it's pure code.
- Tile spacing: tiles are now `CellSize` (2 units) apart instead of 1, see §1 BuildTile
  above. If you already built `blueprint_001`-shaped test geometry by hand for any reason,
  re-pull the latest `blueprint_001.json` so positions match the new spacing.
- Tool depots now spawn a tool via the new `ToolDepotSpawner` (§1/§3 above), mirroring
  `SupplyZoneSpawner` — no more manual placement needed.

## 7. Smoke test checklist

Run one host + one client (or two clients against a dedicated server):
- [ ] `BuildTile` ghosts appear at the 12 positions from `blueprint_001.json`; only the
      4 foundation tiles (y=0) show as eligible (brighter ghost color) at start
- [ ] Walking up to a `WoodPlank` from the supply zone and pressing E picks it up and
      snaps it to the hold point
- [ ] Looking at an eligible empty tile while holding wood and pressing E places it
      (tile visual switches to `placedMaterialVisual`, plank becomes server-owned/kinematic)
- [ ] Picking up the `Hammer`, looking at a tile with `MaterialPlaced`, and holding E
      drives the progress bar up over ~2s and completes the tile (`builtVisual` shows)
- [ ] Releasing E before completion resets progress after the 0.3s grace period
- [ ] Completing a foundation tile causes the floor tile above it to become eligible
      (ghost color change), and completing a floor/wall makes the door eligible once an
      adjacent wall is built
- [ ] The supply zone respawns a new plank ~5s after the first one is picked up
- [ ] The tool depot respawns a new hammer ~5s after the first one is picked up
- [ ] Dropping/disconnecting while holding an item releases it cleanly (no orphaned
      "stuck-held" object)
