# Current Work

Hub blueprint-select-and-start flow + the Level Editor (Systems Architecture, Section 11).
All C# is written and compiles by inspection (no Unity instance available in this
environment to actually build/run it — please do a sanity build before relying on this).
What's left is Editor-only setup that can't be done by hand-editing YAML safely.

## Part A — Hub: pick a blueprint, then start it from the ready area

Flow: a player interacts with a kiosk in the Hub, picks a blueprint from a numbered
pop-up (same shape as the existing order menu), the pick is broadcast to every client via
RPC, and standing on the existing ready area (`StartingArea` GameObject, `Hub.unity`,
already wired to load `Game1`) starts the level using that blueprint instead of
`BuildSystem`'s hardcoded default.

### 1. Add GameSession to Boot.unity

Open `Assets/Scenes/Boot.unity`, find the `Managers` GameObject (already holds
`SaveManager` and `SteamManager`). Add a `GameSession` component to it. No fields to
assign — it's a plain `DontDestroyOnLoad` singleton, same pattern as the other two.

### 2. Add a LevelSelectKiosk to Hub.unity

`StartingArea` (the existing ready-area GameObject in `Hub.unity`) is itself an
in-scene-placed `NetworkObject` (not from the network prefab list) with a `BoxCollider` +
`StartingAreaTrigger`. Add a new GameObject the same way:

1. Create a new GameObject in `Hub.unity` (e.g. "LevelSelectKiosk"), positioned wherever
   you want the kiosk to stand.
2. Add components: `NetworkObject`, a `Collider` (not a trigger — same raycast caveat as
   `BuildSystem`'s OrderStation, see `docs/BUILD_SYSTEM_SETUP.md` §4), `LevelSelectKiosk`.
3. No fields to assign on `LevelSelectKiosk` itself — it scans
   `BlueprintLoader.GetAllBlueprintIds()` on spawn (built-in `StreamingAssets/Blueprints/`
   blueprints plus anything saved to Steam Cloud from the Level Editor).

In-game: looking at the kiosk and pressing **E** opens a "Select Level" pop-up listing
every available blueprint by name; pressing the matching number key (1–9) picks one
(broadcast to all clients) and closes the menu; pressing **E** again with the menu open
closes it without picking. Standing on `StartingArea` then loads `Game1` as before, and
`BuildSystem` now builds from whichever blueprint was picked (falls back to its own
`blueprintId` Inspector field if nobody picked anything).

### 3. Reassign ToolDepotSpawner.prefab's tool list (manual, Inspector-only)

`ToolDepotSpawner` used to have a single `toolPrefab` field; it's now `toolPrefabs`
(an array), so a depot can offer different tools per blueprint instead of always
spawning one hardcoded type. Renaming the field broke the existing serialized
reference in `Assets/Prefabs/ToolDepotSpawner.prefab` — Unity can't auto-migrate a
scalar field to an array via `FormerlySerializedAs`, so this needs a two-click fix:

1. Open `Assets/Prefabs/ToolDepotSpawner.prefab`.
2. Select the `ToolDepotSpawner` component — you'll see an empty `Tool Prefabs` array
   where `Tool Prefab` used to be.
3. Set its size to 1 and drag the **Hammer** prefab into slot 0 (this restores the
   previous behavior).
4. Once Trowel/Torch `ToolItem` prefabs exist, add them as additional array entries —
   any blueprint whose `toolDepots[].tools` references a tool type with no matching
   prefab in this array logs a warning and just skips that tool (see
   `ToolDepotSpawner.Configure`).

### 4. Smoke test checklist

- [ ] Standing near the kiosk and pressing E opens "Select Level" listing every
      `StreamingAssets/Blueprints/*.json` blueprint by name
- [ ] Picking one closes the menu; a second client sees the same pick reflected (no
      need to re-pick per client)
- [ ] Standing on `StartingArea` loads `Game1` and `BuildSystem` spawns the picked
      blueprint's tiles/zones/stations, not the Inspector's default `blueprintId`
- [ ] If nobody ever interacts with the kiosk, `Game1` still loads `BuildSystem`'s
      default blueprint (no regression from before this change)
- [ ] Tool depots still spawn a Hammer after the prefab reassignment in step 3

## Part B — Level Editor

New scripts, all in `Assets/Scripts/LevelEditor/` (not `Editor/` — Unity strips folders
literally named `Editor` from player builds, and this needs to run in actual play mode
for Preview Mode to work):

- `EditorCommand.cs` — Command Pattern undo/redo (`IEditorCommand`, `ActionCommand`,
  `EditorCommandStack`)
- `EditableBlueprint.cs` — mutable in-memory blueprint model, converts to/from
  `BlueprintData` at load/save time
- `LevelEditorCamera.cs` — orthographic pan/zoom bird's-eye camera
- `EditorGridRenderer.cs` — `GL.Lines` grid for the active Y-layer
- `LevelEditorController.cs` — orchestrator: modes, layer, brush, tile/world-object
  placement+erase+edit (all undoable), placeholder cube/sphere visuals, New/Load/Save,
  Preview Mode entry/exit
- `LevelEditorPreviewController.cs` — self-contained first-person walk-through (WASD +
  mouse look, ESC to exit), built entirely in code, no prefab needed
- `LevelEditorUI.cs` — all `OnGUI` panels (top bar, tile palette, world object panel,
  tile property panel, contract settings, save/load)

### 1. Create the LevelEditor scene

1. Create a new scene, `Assets/Scenes/LevelEditor.unity`.
2. Create an empty GameObject (e.g. "LevelEditor") and add:
   - `LevelEditorController`
   - `EditorGridRenderer` — assign its `controller` field to the same GameObject
   - `LevelEditorUI` — assign its `controller` field to the same GameObject
3. Create a Camera GameObject (or reuse the scene's default Main Camera) and add
   `LevelEditorCamera` to it. Assign `LevelEditorController.editorCamera` (on the
   "LevelEditor" GameObject from step 2) to this Camera.
4. Add this scene to Build Settings if you want to reach it via a scene-load path from
   elsewhere; otherwise it's fine to open and test directly in the Editor via Play Mode.

### 2. Using it

- **Tiles mode**: left-click an empty grid cell to place a tile with the current brush
  (Type/Material/Tool/Health, left panel); left-click an existing tile to select it
  (right panel shows its properties); right-click erases. `[`/`]` change the active
  Y-layer — tiles on other layers render smaller and darkened.
- **World Objects mode**: left-click places a Supply Zone / Order Station / Tool Depot /
  Player Spawn (whichever's selected in the left panel) at the clicked ground position;
  right-click erases the nearest one of the selected category within ~1 unit. Tool
  depot contents (which tools each one offers) are toggled per-depot once at least one
  exists.
- **Preview**: WASD + mouse look, ESC to return to the editor. Spawns at the first
  Player Spawn if one exists, otherwise above the grid's center.
- **Ctrl+Z / Ctrl+Y**: undo/redo (tile and world-object placement/erase/property-edit
  only — contract settings, grid size, and tool-depot checkboxes are direct edits, not
  undoable; an accepted scope cut for a v1 dev tool).
- **Save**: writes to local `StreamingAssets/Blueprints/<id>.json` and Steam Cloud (so
  it's selectable from the Hub kiosk in Part A). Auto-prefixes the id with
  `blueprint_` if it isn't already. **Load**: lists every available id (local + cloud).

### 3. Known gaps (not blockers, just not implemented yet)

- Shrinking grid size in Contract Settings doesn't validate against orphaning
  out-of-bounds tiles — they just become invisible/unreachable until you grow the grid
  back or erase them manually.
- No confirmation prompt on "New" — it discards the current in-memory blueprint
  immediately (anything unsaved is lost).
- Placeholder visuals only (primitive cubes/spheres, tinted by type/category) — no art.

### 4. Smoke test checklist

- [ ] Placing/erasing/selecting tiles in Tiles mode works and Ctrl+Z/Ctrl+Y undo/redo
      them correctly, including property edits made via the right-hand panel
- [ ] Placing/erasing each World Object category works and is undoable
- [ ] Changing layer with `[`/`]` updates the grid renderer and dims off-layer tiles
- [ ] Save writes a valid JSON file to `StreamingAssets/Blueprints/` and the kiosk
      (Part A) picks it up after a refresh
- [ ] Load repopulates the grid correctly from a previously-saved blueprint
- [ ] Preview Mode spawns at a Player Spawn (or the grid center if none exist), walks
      and collides with placed tiles, and ESC returns cleanly to edit mode with the
      editor camera reactivated
