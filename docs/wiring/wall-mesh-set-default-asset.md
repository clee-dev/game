# Wiring: WallMeshSet_Default Asset

**Status:** Pending. Pure Editor wiring — no further code required.

## Why this is wiring, not a feature to build

All the supporting code is in place:

- `WallMeshVariant.cs` — the 6 mesh shapes a connection mask resolves to
  (`Standalone, EndCap, Straight, Corner, TJunction, Cross`).
- `WallVariantLookup.cs` — the 16-entry bitmask-to-variant table, fully
  implemented and shared by both runtime (`BuildTile`) and the Level Editor
  (`LevelEditorController`).
- `WallMeshSet.cs` — a `[CreateAssetMenu]` `ScriptableObject` with one `Mesh`
  field per `WallMeshVariant`, plus `MeshFor(WallMeshVariant)`. This is the
  first custom `ScriptableObject` in the codebase, but the class itself needs
  no further code — it's just data.
- `BuildTile.cs` — `[SerializeField] private WallMeshSet wallMeshSet` reads
  this asset in `RefreshVisual()` to swap `sharedMesh` on the ghost/placed/built
  `MeshFilter`s and rotate the tile to match its `_wallMask`. Currently
  unassigned on `BuildTile.prefab`, so Wall tiles render with whatever mesh is
  already on the prefab today (no visual change yet).

Nothing here needs a script change. Same pattern as the TrashBin/Trowel-Torch
wiring tasks: creating a new asset instance and wiring a prefab field are
Editor-only steps no remote/headless session can do.

## What to build

1. In the Project window, right-click `Assets/Art/Meshes/Walls/` (create the
   folder if it doesn't exist) → **Create → Crazy Construction → Wall Mesh
   Set**. Name it `WallMeshSet_Default`.
2. For the prototype, assign Unity's builtin **Cube** mesh to all 6 slots
   (`standaloneMesh`, `endCapMesh`, `straightMesh`, `cornerMesh`,
   `tJunctionMesh`, `crossMesh`). A plain cube looks identical at every
   90-degree Y rotation, so this won't visually distinguish variants yet — it
   only exercises the mask → variant → mesh-swap data path ahead of real
   modular wall art. Swap in real meshes later with zero code changes.
3. Select `BuildTile.prefab` and assign `WallMeshSet_Default` to the new
   `wallMeshSet` field (under the "Smart Walls" header in the Inspector,
   alongside the existing "Visuals" fields).
4. Optional: build a short Wall run in the Level Editor (3+ tiles, an L, a T)
   for `blueprint_001` or any blueprint, save, then playtest in `Game1` to
   confirm `BuildTile`'s rotation updates live as tiles go `Empty` →
   `MaterialPlaced` → `Built`, and that the Level Editor's own preview cubes
   rotate to match. See `docs/ARCHITECTURE.md`'s Smart Wall System section for
   the full validation checklist.

## Dependencies

Blocked on: nothing. Code-complete already (`WallMeshVariant`,
`WallVariantLookup`, `WallMeshSet`, `BuildTile`, `BuildSystem`,
`LevelEditorController` are all wired; a `null` `wallMeshSet` is safely
no-op'd in `RefreshVisual()`, so Wall tiles work exactly as before until this
asset is assigned).

Blocks: nothing — Wall tiles already function correctly without this asset,
just without the per-connection mesh/rotation variety. This wiring task only
unlocks the *visual* payoff of the Smart Wall System.
