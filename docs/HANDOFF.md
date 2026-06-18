# Handoff — Context Dump for Next Session

Branch: `claude/stoic-einstein-28wnud`, latest commit `a393b18`. Working tree clean,
pushed to origin.

## What's built and working

**Ordering system (Systems Architecture §5.3)** — `OrderStation` + `OrderQueueSystem`
(`Assets/Scripts/Build/`):
- Global material cap tracked across Loose/Held/Placed states, delivery countdown,
  top-right "Incoming Deliveries" `OnGUI` list.
- `OrderStation.availableMaterials[]` (array, not a single material) + server RPC
  `PlaceOrderRpc(int materialIndex)` with bounds checking. Orders deliver to the
  `SupplyZoneSpawner` matching the material type, not to the station itself.
- `OrderQueueSystem.OrderEntry` implements `INetworkSerializeByMemcpy` — required for
  any blittable struct used in `NetworkList<T>`, otherwise NGO throws
  `ArgumentException: Serialization has not been generated for type X` at runtime.
  **Pattern to remember**: any new struct put in a `NetworkList<T>` needs this.

**Order menu UI (just finished this session)** — players can now open the station menu
with `E` and either press number keys (1-9, unchanged) or click buttons with the mouse:
- `PlayerCamera.SetLookEnabled(bool)` — new public method, unlocks/shows cursor and
  stops mouse-look when `false`. Deliberately separate from the `GameEvents` pause-menu
  toggle (different concern, per-feature not per-pause-state).
- `PlayerInteraction` exposes `SelectOrderOption(int)`, `CloseOrderMenu()`,
  `IsOrderMenuOpen`, `OpenOrderMenuTarget` for UI Buttons to call.
- `Assets/Scripts/OrderMenuPanel.cs` (new) — fixes a bug where the menu panel started
  inactive and nothing ever turned it on. **Key lesson**: a disabled GameObject's own
  `Update()` never runs, so a "watch this bool and toggle my own active state" script
  can't live on the object it's toggling — it has to live on an always-active sibling
  (here, the prefab's `Canvas` object) and call `panel.SetActive(...)` on the target.

**Furniture/Decor tile split** — `TileType.Furniture` (ground, needs tile below built)
vs `TileType.Decor` (wall-mounted, needs adjacent built tile) replace one ambiguous type.

## Known follow-ups not yet done (low priority, flagged not fixed)

1. **`OrderMenuPanel` polls every frame instead of using an event.** Works fine
   (cheap bool check), but the cleaner shape would be `PlayerInteraction` raising an
   event (extend `GameEvents`, or a local C# event) that `OrderMenuPanel` subscribes to
   in `OnEnable`/unsubscribes in `OnDisable`, instead of checking in `Update()`. Flagged
   for a later audit pass, not blocking.
2. **"Back" button on the order menu currently calls `SelectOrderOption(3)`** instead of
   `CloseOrderMenu()` — index 3 is out of bounds (`availableMaterials` only has Wood at
   index 0), so it silently no-ops. Should be rewired in the Editor to call
   `PlayerInteraction.CloseOrderMenu()` with no argument instead.
3. **"Concrete" and "Steel" order buttons are wired to indices 1 and 2** but no material
   prefabs exist yet for those types (only `WoodPlank.prefab`; `MaterialType.Concrete`/
   `Steel` are defined enum values with nothing built on them). Not a bug — just
   unfinished content. Buttons will start working once those prefabs exist and get added
   to `OrderStation.availableMaterials`.

## Project conventions established (apply these going forward)

- **NGO pattern**: server-authoritative `NetworkVariable`/`NetworkList`, client calls
  `[Rpc(SendTo.Server)]` methods, server validates and mutates state.
- **Cursor lock/visibility** centralized in `PlayerCamera.cs`. Two separate toggle paths
  on purpose: `GameEvents` (pause menu, per-client) and `SetLookEnabled` (on-demand,
  per-feature like the order menu) — don't conflate them.
- **Per-player UI** (anything whose `Button.OnClick` needs to hit "the local player's"
  component) must live inside `Player.prefab` itself, as a child of the existing
  `Canvas` (alongside `InteractPrompt`) — never as a separate scene-level Canvas, since
  the player object spawns dynamically per-client over the network and a scene Canvas
  has no build-time way to reference it.
- **Steam persistence pattern already exists and works** — `SteamCloudSave.cs` wraps
  `SteamRemoteStorage.FileWrite/FileRead`; `SaveManager.cs` demonstrates "try Steam
  Cloud, fall back to local file at `Application.persistentDataPath`" via
  `JsonUtility`. Reuse this pattern for any new persisted data — no new Steam SDK
  integration needed.
- **Existing Blueprint data layer** (`Assets/Scripts/Blueprint/`): `BlueprintData.cs`
  (plain `[Serializable]` classes mirroring a JSON schema — `TileData`, `SupplyZoneData`,
  `OrderStationData`, `ToolDepotData`, `ContractDefaults`, etc.), `BlueprintLoader.cs`
  (reads `*.json` from `Application.streamingAssetsPath/Blueprints/` via
  `Newtonsoft.Json`, read-only — no save path exists yet), `BlueprintEnums.cs` (string
  ⇄ enum parsing for `TileType`/`MaterialType`/`ToolType`). This is the schema any new
  "save a blueprint" feature should target.
- **Doc workflow** (user's explicit instruction): `docs/CURRENT_WORK.md` is a scoped
  "how do I wire up the thing we're doing right now" doc — gets cleared/deleted once
  that feature is wired and tested. `docs/CHANGELOG.md` is the permanent newest-first
  running log, append-only, never rewritten.
- **Division of labor**: Claude writes all C# code and prefab/scene YAML that's safe to
  hand-edit; the user does Editor-only work (dragging Inspector references, building
  Canvas/Button hierarchies, placing GameObjects) since that can't be scripted blind.
  `CURRENT_WORK.md` is written as a numbered checklist for exactly that handoff.

## Next task — Level Editor (Section 11 spec) — NOT STARTED

The user pasted a full design spec (their doc's "Section 11") for an in-game Level
Editor: Purpose/Scope, Camera Setup (with a code sample), Y-Layer Selection, Edit Mode
Tools (Tile Palette, Tile Property Panel, World Object Placement, Contract Settings
Panel, Undo-Redo Command Pattern with a code sample).

**The exact original text did not survive context compaction and is not recoverable
from the transcript log** — only a structural summary exists (the section list above).
Whoever picks this up needs the user to **re-paste the Section 11 spec** before writing
any camera-rig or undo/redo code, rather than guessing at the exact API shape from a
summary.

Two decisions were already made and confirmed with the user, and still apply:
1. **Blueprint persistence**: save/load through Steam Cloud, reusing the existing
   `SteamCloudSave`/`SaveManager` pattern (see above) — no new Steam SDK work needed.
   User's words: "the idea is that we can save these blueprints and reuse them later.
   for now they can go in the steam user data and be selected in the hub to spawn into."
2. **Tool depots should support multiple tools, not one.** `ToolDepotData.tools` is
   already a `string[]` in the blueprint schema, but `ToolDepotSpawner.cs`
   (`Assets/Scripts/Build/`) currently only has a single `[SerializeField] ToolItem
   toolPrefab` and spawns one. This needs extending to spawn/replace from a list of
   tool types as part of the Level Editor work (so depots placed in the editor can offer
   more than one tool).
