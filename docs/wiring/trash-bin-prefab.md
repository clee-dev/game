# Wiring: TrashBin Prefab

**Status:** Pending. Pure Editor wiring — no further code required.

## Why this is wiring, not a feature to build

All the supporting code is in place:

- `TrashBin.cs` — `[RequireComponent(typeof(NetworkObject))]`, one RPC
  (`TrashItemRpc(NetworkObjectReference)`) that despawns whatever's passed in
  after confirming it has a `PhysicsPickup` component. Mirrors `OrderStation.cs`'s
  shape (a NetworkObject players RPC directly, not a plain spawner).
- `PlayerInteraction.cs` — raycasts for a `TrashBin` in its target set
  alongside `OrderStation`/`LevelSelectKiosk`/etc, shows "[E] Trash Held Item"
  in the prompt while holding something and looking at one, and calls
  `TrashItemRpc` on press (`HandleInteractPress` / `TrashHeldItem`).
- `BlueprintData.cs` / `EditableBlueprint.cs` — `TrashBinData { id, worldPosition }`
  and a `trashBins` array, same shape as `supplyZones`/`orderStations`/`toolDepots`.
  `BuildSystem.SpawnFromBlueprint()` null-guards this array specifically
  (`?? Array.Empty<TrashBinData>()`) since it's a newer field that no existing
  blueprint JSON has yet — old blueprints load fine with zero trash bins.
- `LevelEditorController.cs` — `WorldObjectCategory.TrashBin` is a full fifth
  category alongside `SupplyZone`/`OrderStation`/`ToolDepot`/`PlayerSpawn`:
  same `PlaceOrReplace`/`RemoveNearest` click-to-place/right-click-to-erase
  behavior, undo/redo support, a red marker color in `RefreshWorldObjectVisuals()`.
  `LevelEditorUI.cs` needed no new button code — `DrawEnumRow`'s generic
  `CycleEnum` already cycles through any enum, including the new value — just
  a `Trash Bins: N` count label was added to the World Objects panel.

Nothing here needs a script change. This is the same pattern as the Trowel/Torch
wiring task: the prefab itself and its field assignment are Editor-only steps
no remote/headless session can do.

## What to build

1. Create `Assets/Prefabs/TrashBin.prefab`: a `NetworkObject` + a `Collider`
   (for `PlayerInteraction`'s raycast to hit) + `TrashBin.cs`. Visually,
   anything readable as "a bin" is fine — `OrderStation.prefab` is the closest
   reference for component layout (root `BoxCollider` sized to the 1-unit grid
   cell for raycast targeting, a child mesh for the actual visual). Suggest
   reusing `Assets/Materials/Red.mat` for color-coding (it already exists,
   unused by anything else) so it's visually distinct from the yellow
   OrderStation and orange ToolDepot markers in the Level Editor.
2. Register the new prefab in `Assets/DefaultNetworkPrefabs.asset` (it has a
   `NetworkObject` component like every other networked prefab — required for
   NGO spawning, same as `OrderStation.prefab`'s entry).
3. In the `Game1` scene, select the `BuildSystem` GameObject and assign the new
   prefab to the `trashBinPrefab` field (alongside `tilePrefab`,
   `supplyZoneSpawnerPrefab`, `toolDepotSpawnerPrefab`, `orderStationPrefab`).
4. Optional: place a `TrashBin` in the Level Editor for `blueprint_001` (or any
   blueprint) via the new "World Objects" → "TrashBin" brush category, then
   save, so there's at least one in-game to test against. No blueprint has one
   yet — without this step `BuildSystem.SpawnFromBlueprint()`'s trash bin loop
   simply runs zero times, same as any other empty array.

## Dependencies

Blocked on: nothing. Code-complete already (`TrashBin`, `PhysicsPickup`,
`PlayerInteraction`, `BlueprintData`, `EditableBlueprint`,
`LevelEditorController`, `LevelEditorUI`, `BuildSystem` are all wired and
null-safe against zero trash bins existing).

Blocks: nothing yet — this is purely a quality-of-life recovery tool for
ordering mistakes, not a dependency for any other planned feature.
