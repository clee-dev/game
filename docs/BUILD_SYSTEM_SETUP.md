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
  - `progressBarRoot` + `progressFill` → a small world-space canvas above the tile,
    built from **three separate objects**. Do not point either field at the Canvas's
    own RectTransform — that's the mistake that makes the bar flash full-screen
    instead of showing as a small bar over the tile:
    1. A child `Canvas` (e.g. "ProgressCanvas"). Render Mode **must be `World Space`**,
       not the default `Screen Space - Overlay` — Overlay ignores the object's
       transform and auto-sizes its rect to the screen resolution, so toggling it on
       lights up the whole screen instead of a small bar above the tile. Position it
       above the tile, e.g. local position `(0, 2, 0)`. No `GraphicRaycaster` needed,
       nothing on it is clickable. Set its `RectTransform` width/height to something
       tile-sized (e.g. width `1`, height `0.25`) while leaving scale at `(1,1,1)`,
       rather than leaving Unity's default `100×100` and compensating with a tiny
       scale factor.
    2. A child `Image` under the canvas (e.g. "Background") stretched to fill that
       rect (`anchorMin (0,0)`, `anchorMax (1,1)`), dark/grey — just the bar's
       outline/backdrop, not wired to any script field.
    3. A second child `Image` (e.g. "Fill"), anchored to the **left edge** —
       `anchorMin (0,0)`, `anchorMax (0,1)`, **pivot `(0, 0.5)`** — sized to match the
       background's full width via its `sizeDelta`. The left pivot matters: the
       script scales this object's `localScale.x` from 0→1
       (`BuildTile.UpdateProgressBar`), and with a left pivot it grows rightward like
       a normal progress bar instead of expanding from the center.
    - Assign `progressBarRoot` → the **ProgressCanvas** GameObject itself (the whole
      canvas toggles on/off — disabled canvases cost ~nothing). Assign
      `progressFill` → the **Fill** object's `RectTransform`, not the canvas's own.

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

### OrderStation

A new prefab, for the ordering system (Systems Architecture, Section 5.3):
- `NetworkObject` — unlike the spawners above, this **is** a `NetworkObject`. Placing
  an order is a client-to-server RPC, and RPCs need a `NetworkBehaviour` to live on.
- `Collider` (not a trigger) — this is what `PlayerInteraction`'s raycast hits, same
  raycast-note caveat as §4 below.
- `OrderStation` script:
  - `materialPrefab` → the **WoodPlank** prefab from above (mirrors
    SupplyZoneSpawner/ToolDepotSpawner — the material type is decided by this prefab's
    field, not read from the blueprint's `orderStations[]` entry)
  - `orderQuantity` → defaults to 3, fine as-is
  - `deliveryPoint` → optional. Leave unassigned to have orders land on the station
    itself, or assign a separate child/Transform elsewhere to match the design doc's
    "targetSupplyZone" concept (e.g. a delivery point closer to the build site)

## 2. Register network prefabs

Open `Assets/DefaultNetworkPrefabs.asset` (or the NetworkManager's prefab list, same
thing) and add:
- `BuildTile`
- `WoodPlank`
- `Hammer`
- `OrderStation`

`SupplyZoneSpawner` and `ToolDepotSpawner` are **not** `NetworkObject`s and must not be
added here.

## 3. Add BuildSystem and OrderQueueSystem to the level scene

In `Assets/Scenes/Game1.unity`:
1. Create an empty `GameObject` named `BuildSystem` (it does not need a `NetworkObject`
   — every client loads the same blueprint independently, see the class doc comment).
2. Add the `BuildSystem` script.
3. Assign:
   - `tilePrefab` → the **BuildTile** prefab
   - `supplyZoneSpawnerPrefab` → the **SupplyZoneSpawner** prefab
   - `toolDepotSpawnerPrefab` → the **ToolDepotSpawner** prefab
   - `orderStationPrefab` → the **OrderStation** prefab
   - `blueprintId` → leave as `blueprint_001` (matches
     `Assets/StreamingAssets/Blueprints/blueprint_001.json`)
4. Create a second empty `GameObject` named `OrderQueueSystem` — this one **does** need
   a `NetworkObject`, since its pending-order queue and material-cap counters are
   genuinely shared server state, not something every client can compute independently
   (unlike `BuildSystem`, see its class doc comment). Add the `OrderQueueSystem` script;
   `materialCap` (default 15) and `deliveryDelay` (default 15s) are fine as-is.

`BuildSystem.Start()` only spawns tiles/supply zones when `NetworkManager.Singleton.IsServer`
is true, so this works correctly for both a dedicated host and a listen-server host.
`blueprint_001` has no `orderStations` entries, so `OrderQueueSystem` only matters for
the material cap in that blueprint — no orders will ever be placed against it until a
blueprint with `orderStations` entries exists.

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

- **Portable order station**: the design doc's "iPad item" shop upgrade (carry the order
  station around instead of using a fixed one) isn't implemented — there's no shop/economy
  system yet to unlock it from. `OrderStation` is fixed-position only for now.
- **Material cap doesn't scale with player count**: the design doc calls for `materialCap`
  to be "adjusted per player count" but specifies no formula, so `OrderQueueSystem.materialCap`
  is a flat tunable value (default 15) regardless of how many players are connected.
- **Rejected orders don't refund money**: there's no currency/economy system in the project
  yet, so `OrderStation.WouldExceedCap` / `OrderQueueSystem.TryPlaceOrder` block the order
  and the prompt shows "Material cap reached", but no money is deducted or returned since
  none is ever spent.

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
- The progress bar canvas now billboards toward the local player's camera while it's
  visible (`BuildTile.LateUpdate`), so it stays readable instead of facing whatever
  direction it was spawned in. Pure code, no Editor wiring needed — it only rotates
  `progressBarRoot` while that object is active, i.e. while someone is actively
  building the tile, so idle tiles cost nothing.
- The old "Furniture dependency rule" ambiguity is resolved by splitting it into two tile
  types instead of picking one doc's rule over the other: `Furniture` (ground furniture —
  beds, chairs) keeps the "tile directly below must be built" rule, and the new `Decor`
  (wall-mounted furniture — paintings, hanging pots) uses the "adjacent built tile" rule.
  Pure enum/code change (`BlueprintEnums.TileType`, `BuildSystem.IsEligible`), no Editor
  wiring needed; `blueprint_001` doesn't use either tile type yet.
- A full ordering system (Systems Architecture, Section 5.3): `OrderStation` (placed per
  blueprint `orderStations[]` entry, §1/§3 above) lets a player order a batch of materials
  that arrive after a delivery delay, gated by `OrderQueueSystem`'s global material cap
  across every Loose/Held/Placed material in the world. The top-right "Incoming Deliveries"
  list (`PlayerInteraction.DrawOrderQueue`) is pure `OnGUI` code reading `OrderQueueSystem`'s
  replicated order list, no Canvas/Editor setup needed.

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
- [ ] Looking at an `OrderStation` and pressing E places an order; it immediately appears
      in the top-right "Incoming Deliveries" list on every connected client with a
      counting-down timer
- [ ] ~15s after ordering, the ordered quantity of materials spawns at the delivery point
      (or the station itself if `deliveryPoint` is unassigned) and the queue entry disappears
- [ ] Once enough materials are Loose/Held/Placed in the world to hit `materialCap`, the
      `OrderStation` prompt switches to "Material cap reached" and pressing E does nothing
      until enough materials are built (consumed) or despawned to free up headroom
