# Wiring: Hub Level-Select Kiosk

**Status:** pending. Move this file to `docs/wiring/done/` once the smoke test checklist
passes.

All the code for this already exists and is wired together correctly:
`LevelSelectKiosk`, `BlueprintLoader`, `GameSession`, `PlayerInteraction`'s kiosk-menu
handling, and `BuildSystem.LoadBlueprintAndBuildLookups()` (reads
`GameSession.Instance?.SelectedBlueprintId`, falls back to its own Inspector
`blueprintId` if null). Nothing here needs new scripts or script changes — it's two
GameObjects that haven't been placed in their scenes yet.

## 1. Add GameSession to Boot.unity

Open `Assets/Scenes/Boot.unity`, select the existing `Managers` GameObject (already
holds `SaveManager` and `SteamManager`). Add a `GameSession` component to it. No fields
to assign — it's a plain `DontDestroyOnLoad` singleton, same pattern as the other two.

Without this step, `GameSession.Instance` is always `null` at runtime, so
`BuildSystem` silently falls back to its Inspector default every time (no crash, but
the kiosk selection is never read).

## 2. Add a LevelSelectKiosk to Hub.unity

`StartingArea` (the existing ready-area GameObject in `Hub.unity`) is an in-scene-placed
`NetworkObject` (not from the NetworkManager's prefab list) with a `BoxCollider` +
script. Add the kiosk the same way:

1. Create a new GameObject in `Hub.unity` (e.g. `"LevelSelectKiosk"`), positioned
   somewhere in the Hub that's easy to walk up to and clear of foot traffic to the
   `StartingArea` trigger.
2. Add components:
   - `NetworkObject`
   - A `BoxCollider`, **not** a trigger (uncheck `Is Trigger`) — matches `OrderStation`,
     since `PlayerInteraction`'s raycast targets it and it should physically block
     walk-through like a real kiosk would.
   - `LevelSelectKiosk` — no fields to assign; it scans
     `BlueprintLoader.GetAllBlueprintIds()` on spawn (built-in
     `StreamingAssets/Blueprints/*.json` plus anything saved to Steam Cloud from the
     Level Editor).
3. Optional, cosmetic: add a child primitive (e.g. a Cube, scaled to look like a
   terminal/kiosk) so players can see where to stand — `OrderStation` has a similar
   child `Cylinder` mesh under its collider root. Not required for the feature to work.

In-game: looking at the kiosk and pressing **E** opens a "Select Level" pop-up listing
every available blueprint by name; pressing the matching number key (1-9) picks one
(broadcast to all clients via RPC) and closes the menu; pressing **E** again with the
menu open closes it without picking. Standing on `StartingArea` then loads `Game1` as
before, and `BuildSystem` now builds from whichever blueprint was picked.

## Smoke test checklist

- [ ] Standing near the kiosk and pressing E opens "Select Level" listing every
      `StreamingAssets/Blueprints/*.json` blueprint by name, plus any blueprint saved
      from the Level Editor to Steam Cloud
- [ ] Picking one closes the menu; a second client sees the same pick reflected (no
      need to re-pick per client)
- [ ] Standing on `StartingArea` loads `Game1` and `BuildSystem` spawns the picked
      blueprint's tiles/zones/stations, not the Inspector's default `blueprintId`
- [ ] A level saved from the Level Editor (`Assets/Scenes/LevelEditor.unity` → Save)
      shows up in the kiosk list after refreshing (re-opening the menu calls
      `RefreshAvailableBlueprints()`) and loads correctly in `Game1`
- [ ] If nobody ever interacts with the kiosk, `Game1` still loads `BuildSystem`'s
      default blueprint (no regression from before this change)
