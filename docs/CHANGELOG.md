# Changelog

Running record of what's been built, newest first. Scoped "how do I wire this up"
instructions for in-progress work live in `docs/CURRENT_WORK.md` and get deleted once
done — this file is the permanent summary that replaces them.

## 2026-06-18 (2)

- **Redundancy audit + cleanup.** Reviewed the codebase against `docs/HANDOFF.md` and
  the design docs. Deleted `ConnectionManager.cs` (dead code, superseded by the Steam
  lobby system, no remaining references anywhere). Fixed `ToolDepotSpawner.Update()` to
  cache its `PhysicsPickup` reference instead of calling `GetComponent` every frame,
  matching `SupplyZoneSpawner`'s pattern. Disabled the `Launcher` GameObject
  (`MiniGameLauncher`) in `Hub.unity` — it and `StartingAreaTrigger` were both active
  and both driving the same status text, with `MiniGameLauncher` pointed at a
  "Test_game" scene that doesn't exist on disk. `StartingAreaTrigger` (target:
  `Game1`, the real gameplay scene) is now the sole path into a level from the Hub.
  Remaining known debt, left as-is for now: `NetworkPlayer`/`HubPlayerState` overlap
  on `Player.prefab` (duplicate ownership-gated enable logic); `LobbyUI` and its
  panels are disabled in `Hub.unity`, which also orphans `ReadyManager.StartGame()`
  (only caller was LobbyUI's Start Game button) and currently leaves no reachable
  pause/mute UI in the Hub scene.

## 2026-06-18

- **Order menu: mouse unlock + clickable buttons (done).** `PlayerCamera` gained
  `SetLookEnabled(bool)`; `PlayerInteraction` calls it when the order menu opens/closes
  and now exposes `SelectOrderOption(int)` / `CloseOrderMenu()` / `IsOrderMenuOpen` /
  `OpenOrderMenuTarget` for UI Buttons to call alongside the existing number-key
  selection. `OrderMenuPanel` toggles the menu panel's active state from the always-on
  `Canvas` object (a disabled GameObject can't poll its own visibility, so this can't
  live on the panel itself). Wired and tested working in-game.
- **Ordering system: multi-material picker + serialization fix.** `OrderQueueSystem`'s
  `OrderEntry` now implements `INetworkSerializeByMemcpy`, fixing a runtime
  `ArgumentException` from `NetworkList<OrderEntry>` having no registered serializer.
  `OrderStation.materialPrefab` (single) became `availableMaterials` (array); ordering
  is now `PlaceOrderRpc(int materialIndex)` with server-side bounds checking. Pressing
  `E` on a station opens a small pop-up listing each material as a numbered option.
- **Orders deliver to the matching supply zone, not the station.** `OrderStation`
  resolves delivery position via `SupplyZoneSpawner.All`, matching on material type,
  instead of dropping materials at the station's own position.
- **Full ordering system (Systems Architecture §5.3).** `OrderStation` +
  `OrderQueueSystem`: global material cap across Loose/Held/Placed materials, delivery
  countdown, top-right "Incoming Deliveries" `OnGUI` list.
- **Furniture/Decor tile-type split.** `TileType.Furniture` (ground furniture — "tile
  below must be built") and the new `TileType.Decor` (wall-mounted — "adjacent built
  tile" rule) replace the old single ambiguous `Furniture` rule.
