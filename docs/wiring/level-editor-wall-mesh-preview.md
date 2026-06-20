# Wiring: Level Editor Wall Mesh Preview

**Status:** Pending. Pure Editor wiring — no further code required.

## Why this is wiring, not a feature to build

All the supporting code is in place:

- `LevelEditorController.cs` now has `[SerializeField] private WallMeshSet
  wallMeshSet`, the same type/asset `BuildTile.wallMeshSet` reads at runtime.
- `CreateTileCube()` already swaps the placeholder cube for
  `wallMeshSet.MeshFor(variant)` on Wall tiles when this field is assigned,
  at the correct scale (full for the current layer, dimmed/shrunk to match
  the existing off-layer cube ratio otherwise) and the correct Y rotation.
- The asset itself already exists: `Assets/Art/Meshes/Walls/WallMeshSet-Default.asset`
  (Cameron wired this to `BuildTile.prefab` in commit `5b380a4`, "wall meshes").

Nothing here needs a script change. The field is null-safe — Wall tiles in the
Level Editor keep rendering as the original colored cube (same as every other
`TileType`) until this is wired, exactly as before.

## What to build

1. Open `Assets/Scenes/LevelEditor.unity`.
2. Find the GameObject carrying the `LevelEditorController` component (same
   object as `EditorGridRenderer`/`LevelEditorUI`'s `controller` reference —
   it already has `editorCamera` and `pauseCanvas` assigned).
3. Drag `Assets/Art/Meshes/Walls/WallMeshSet-Default.asset` onto the new
   `Wall Mesh Set` field in the Inspector.
4. Save the scene.
5. Optional: open the Level Editor, place a few Wall tiles in different
   configurations (a straight run, an L, a T, a +) and confirm the preview
   now shows the real connection-shaped meshes (not cubes), matching what the
   same layout produces in `Game1` at runtime.

## Dependencies

Blocked on: nothing. Code-complete already (`WallMeshSet`, `WallVariantLookup`,
`LevelEditorController` are all wired; a `null` `wallMeshSet` is safely
no-op'd in `CreateTileCube()`, so Wall tiles keep their current cube preview
exactly as before until this asset is assigned here).

Blocks: nothing — the Level Editor's Wall tile preview already functions
correctly (color-coded cube, correct rotation) without this. This wiring task
only unlocks the same visual payoff in the editor that `wall-mesh-set-default-asset.md`
already unlocked at runtime.
