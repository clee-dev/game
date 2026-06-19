# Wiring: Trowel & Welding Torch Tool Prefabs

**Status:** Pending. Pure Editor wiring — no new code required.

## Why this is wiring, not a feature to build

All the supporting code is already generic and ready:

- `ToolItem.cs` — `[SerializeField] private ToolType toolType` already accepts
  any `ToolType` (Hammer, Trowel, Torch).
- `ToolStats.cs` — `BuildDuration(ToolType)` already returns the correct values:
  Hammer 2.0s, Trowel 2.5s, Torch 4.0s.
- `ToolDepotSpawner.cs` — `toolPrefabs[]` array + `Configure(string[]
  toolTypeNames)` already supports any number of tool types per depot.

Nothing here needs a script change. This is the same pattern as `Hammer.prefab`,
just two more assets.

## What to build

For each of Trowel and Welding Torch:

1. Duplicate `Assets/Prefabs/Hammer.prefab` (or build fresh with the same
   component layout: model/mesh, collider, `PhysicsPickup`, `ToolItem`).
2. Set `ToolItem.toolType` to `Trowel` or `WeldingTorch` respectively.
3. Register the new prefab in `Assets/DefaultNetworkPrefabs.asset` (it has a
   `NetworkObject` component like every other pickup — required for NGO
   spawning, same as the other 6 entries already in that list).
4. Add the new prefab to the relevant `ToolDepotSpawner.prefab` instance(s)'
   `toolPrefabs[]` array — currently only `depot_hammer_0` exists in
   `blueprint_001.json`'s `toolDepots`, so a depot serving Trowel/Torch would
   need either a new depot entry in a blueprint JSON (or the Level Editor) or
   an additional entry in an existing depot's `tools` list + `toolPrefabs[]`.

## Dependencies

Blocked on: nothing. Code-complete already (`MaterialItem`, `PhysicsPickup`,
`PlayerInteraction`, `ToolItem`, `ToolStats` are all generic).

Blocks: Phase A material loop (Concrete needs Trowel, Steel needs Welding
Torch) — see `docs/PLANNED_FEATURES.md` for the Concrete/Steel material code
that still needs to be written separately from this wiring task.

## Note

`ToolDepotSpawner.prefab` had a separate, now-fixed bug where its serialized
field was the old pre-refactor `toolPrefab` scalar instead of the current
`toolPrefabs[]` array (see `docs/SESSION.md`, 2026-06-19). That's fixed and
unrelated to this task, but worth knowing if you're inspecting that prefab —
it should already show `toolPrefabs:` as an array with the Hammer entry.
