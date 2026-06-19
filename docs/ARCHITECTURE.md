# Architecture

Implementation source of truth. Describes what is **actually built** today.
For creative vision see `docs/GAME_INTENT.md`. For planned-but-not-built systems
see the Systems Architecture planning doc.

---

## Scene Flow

```
Boot.unity → MainMenu.unity → Hub.unity → Game1.unity
```

- **Boot** — `Managers` GameObject with three `DontDestroyOnLoad` singletons:
  `SaveManager`, `SteamManager`, `GameSession`.
- **MainMenu** — `MainMenuUI` drives host/join via `SteamLobbyManager`.
  `CreateLobby()` generates a room code and starts NGO host. `JoinByCode(code)`
  searches public Steam lobbies and starts NGO client. Steam friend invites handled
  via `OnGameLobbyJoinRequested`.
- **Hub** — `HubPlayerState` spawns each player at a `HubSpawnPoints` location and
  enables movement/camera/visuals only for the local owner. Players select a
  blueprint at a `LevelSelectKiosk`, then stand on the `StartingArea` trigger
  (`StartingAreaTrigger`, an in-scene-placed `NetworkObject`) which starts a
  countdown that loads `Game1`. A `LevelEditorAccessPoint` terminal sends the
  whole connected party into `LevelEditor.unity` instead (see Level Editor
  section below).
- **Game1** — `BuildSystem` loads the selected blueprint (`GameSession.SelectedBlueprintId`,
  falling back to its own Inspector default) and spawns tiles, supply zones, tool
  depots, and order stations server-side.

`LevelEditor.unity` exists and is fully wired (`LevelEditorController`,
`LevelEditorUI`, `EditorGridRenderer`, `LevelEditorCamera` all configured). It is
now in `EditorBuildSettings` and reachable in-game from `Hub.unity` via a
`LevelEditorAccessPoint` terminal (see below) — **this is a change from the
previously-documented "dev tool first, player unlock second" gating in
`GAME_INTENT.md` Phase F; flagged for Cameron's review, not unilaterally
resolved.** See `docs/SESSION.md` 2026-06-19 (Part B) for details.

---

## Networking Model

NGO 2.12.0 over custom `FacepunchTransport : NetworkTransport` (Facepunch
Steamworks). Server-authoritative via `NetworkVariable`s and RPCs. Clients read
and render only.

Key examples:
- `BuildTile.NetworkVariable<TileState>`
- `OrderQueueSystem.NetworkList<OrderEntry>`
- `LevelSelectKiosk.NetworkVariable<FixedString64Bytes>` (selected blueprint id)

`ClientNetworkTransform` subclasses NGO's `NetworkTransform` with
`OnIsServerAuthoritative() => false` for owner-authoritative movement. **Authority
Mode must also be set to Owner in the Inspector** — the code override alone is
ignored in Unity 6.

`PhysicsPickup` handles pickup/drop via ownership transfer. `NetworkVariable<bool>
_isHeld` replicates held state. Picking up requests ownership from server; dropping
returns it.

`NetworkPlayer` disables camera, audio listener, and input components on all player
objects except the local owner.

---

## Systems

### Steam / Bootstrap

`SteamManager` — `DontDestroyOnLoad` singleton. Initializes Facepunch with App ID
from `steam_appid.txt`. Runs `SteamClient.RunCallbacks()` each frame.

`SteamLobbyManager` — creates/joins Steam lobbies. Room codes are 6-character
alphanumeric (no 0/O/1/I). Code and host SteamId stored in lobby metadata. Has
`CurrentRoomCode` property used by `VoiceManager`.

`GameSession` — plain (non-networked) `DontDestroyOnLoad` singleton. Stores
`SelectedBlueprintId` (string). Read by `BuildSystem` when `Game1` loads.
**Naming conflict note:** the Systems Architecture planning doc describes a future
`GameSession` `NetworkObject` for shared money/timer. These are different. Rename
one before the economy system is built.

`SaveManager` — local JSON at `Application.persistentDataPath/savedata.json`.
Stores currency, cosmetics, stats, save slots. `SteamCloudSave` handles Steam Cloud
file read/write for both save data and blueprints.

**Fixed (2026-06-19, Part B):** `SteamCloudSave.Write/Read/ListFiles` all called
into `SteamRemoteStorage` unconditionally. Running without Steam initialized
(`SteamManager.Initialized == false`) threw a `NullReferenceException` out of
`ListFiles`, breaking `BlueprintLoader`'s startup scan. All three methods now
short-circuit and return their empty/failure value (`false` / `null` / empty
list) when Steam isn't initialized.

---

### Player

`PlayerController` — CharacterController movement, gravity, jump, sprint.

`PlayerCamera` — mouse look. Does NOT auto-lock cursor. Subscribes to
`GameEvents.OnGameStarted/Paused/Resumed` to enable/disable mouse look.

`InputReader` — Input System wrapper. Exposes `MoveInput`, `LookInput`,
`IsSprinting`, `JumpPressed`, `InteractPressed`, `InteractHeld`, `ConsumeInteract`.

`NetworkPlayer` — `OnNetworkSpawn()` explicitly enables/disables all components
based on `IsOwner`. Does not assume prefab default state. **Updated (Part B):**
also re-evaluates on every `SceneManager.activeSceneChanged`, and now gates on
`IsOwner && !IsPassiveScene()` — `passiveScenes` (default: `["LevelEditor"]`)
force-disables every player's gameplay components (including the owner's) while
that scene is active, since `LevelEditor.unity` carries every connected player's
`NetworkObject` along (same NGO scene-transition mechanism as Hub → Game1; there's
no "personal/solo scene" concept) but has its own orthographic
`LevelEditorCamera`/controls and no use for player avatars. Also gained
`playerInteraction` to the local-only component list, and a
`[Rpc(SendTo.Server)] RequestLoadSceneRpc(FixedString64Bytes sceneName)` that
clients use to request a scene load (NGO's `NetworkSceneManager.LoadScene` is
server-only) — used by both `PauseMenu.LeaveToHub()` and
`LevelEditorAccessPoint.EnterLevelEditorRpc()`.

`HubPlayerState` — Hub-specific spawn handling. Positions player at a
`HubSpawnPoints` location.

`HubSpawnPoints` — 4 `Transform` positions, fully wired in `Hub.unity`. Confirmed
built and assigned; not a gap.

**Fixed (this session):** `Player.prefab`'s `HubPlayerState.cam` field was
unassigned (`fileID: 0`). Pointed it at the same `Camera` component already
referenced by `NetworkPlayer.playerCam` and `PlayerInteraction.playerCamera`.
Low severity — `NetworkPlayer` already gates the camera by `IsOwner`
independently — but was incorrect.

---

### Interaction & Pickup

`PlayerInteraction` — single forward raycast per frame. Checks in order:
`LevelSelectKiosk` → `OrderStation` → `BuildTile` → `PhysicsPickup`.

Handles: pickup, place-material, hold-to-build, drop/throw. Renders crosshair,
order menu, kiosk menu, incoming-deliveries queue via `OnGUI` (no Canvas/uGUI
required).

---

### Materials

`MaterialItem` — state machine: `Loose → Held → Placed → Built`. Mirrors
`PhysicsPickup`'s held flag.

Currently implemented:
- **Wood** — Light weight, Hammer tool, full prefab + built prefab (`WoodPlank`)

Defined but not yet implemented:
- **Concrete** — Medium weight, Trowel tool (no prefab, no gameplay)
- **Steel** — Heavy weight, Welding Torch tool (no prefab, no gameplay)
- **Any** — defined in `MaterialType` enum

---

### Tools

`ToolItem` — tags a `ToolType`. `ToolStats.BuildDuration`: Hammer 2.0s, Trowel 2.5s,
Torch 4.0s.

Currently implemented:
- **Hammer** — full prefab, works in gameplay

Currently defined but not implemented:
- Trowel, Welding Torch — no prefabs, no gameplay

`ToolDepotSpawner` — server-only. `toolPrefabs` array + `Configure(string[]
toolTypeNames)`. One depot can offer multiple tool types per blueprint. Tool types
with no matching prefab are skipped with a warning.

**Fixed (this session):** `ToolDepotSpawner.prefab` still serialized the old
pre-refactor scalar field `toolPrefab` instead of the current `toolPrefabs[]`
array. The Hammer reference was orphaned (Unity does not migrate a renamed
`[SerializeField]` automatically), so `toolPrefabs` was empty and no tool ever
spawned at the depot in `Game1`. Re-serialized as a single-entry array pointing
at the same Hammer prefab.

---

### Build Tiles

`BuildTile` — `NetworkVariable<TileState>` (`Empty / MaterialPlaced / Built /
Destroyed`). Ghost, placed, and built visuals. World-space progress bar canvas
that billboards toward local camera while visible.

`BuildSystem` — orchestrator. Loads blueprint via `BlueprintLoader`. Builds
position/state lookups. Spawns tile/zone/depot/station prefabs server-side only.

**Dependency rules (implemented):**
- Foundation: no dependencies
- Floor: needs tile below Built
- Wall: needs tile below Built
- Furniture: needs tile below Built
- Decor: needs an adjacent Built tile

**Not implemented:** structural integrity / collapse cascade. The
`supportDependents` graph and Jenga-style collapse on destruction are planned but
do not exist in code.

**Per-material hue visuals (Part B):** `BuildTile.BaseHueFor(MaterialType)` is a
stand-in for real per-material textures, which **do not exist yet** as assets.
Ghost shows the raw hue at the eligible/ineligible alpha; Placed/Built lerp the
hue toward blue/green (`placedBlueBlend`/`builtGreenBlend`, both 0.5 default) so
build progress reads at a glance. Chosen hues: Wood tan `(0.76, 0.60, 0.42)`,
Concrete gray `(0.65, 0.65, 0.65)`, Steel cool-gray `(0.55, 0.58, 0.62)`. The
ghost material was also swapped from an opaque material (`BuildTile.prefab` was
pointing `ghostRenderer` at a non-transparent material, which is presumably why
ghosts previously didn't read as ghostly) to a new `ToonTransparentGhost.mat`
(uses `ToonTransparent.shader`, `_BaseColor` alpha 0.3) so the per-material tint
is actually visible through transparency instead of solid-colored.

---

### Supply & Tool Depots

`SupplyZoneSpawner` / `ToolDepotSpawner` — plain (non-NetworkObject) MonoBehaviours,
server-only. Spawn items and replace them after a cooldown.

---

### Ordering System

`OrderStation` — `NetworkObject`. Numbered `OnGUI` material picker.

`OrderQueueSystem` — `NetworkObject`. `NetworkList<OrderEntry>` (uses
`INetworkSerializeByMemcpy` for NGO compatibility). Enforces:
- Global material cap (counts all Loose/Held/Placed materials + pending-order
  reservations, default 15)
- Delivery delay (default 15s) before spawning at matching `SupplyZoneSpawner`
  or overriding `deliveryPoint`

Not yet implemented: per-player-count scaling of cap or delay.

---

### Blueprint System

**Data classes** (`Assets/Scripts/Blueprint/BlueprintData.cs`): `BlueprintData`,
`TileData`, `GridPosition`, `WorldPosition`, `SupplyZoneData`, `OrderStationData`,
`ToolDepotData`, `ContractDefaults`, `CompletionThresholds`. All `[Serializable]`
with public fields only (required for Newtonsoft + IL2CPP).

**Enums** (`BlueprintEnums.cs`): `TileType`, `MaterialType`, `ToolType`, `TileState`,
`MaterialState`.

**Newtonsoft.Json** is required. `JsonUtility` cannot handle nested lists in
`BlueprintData`.

`BlueprintLoader` — reads the union of `StreamingAssets/Blueprints/*.json` and Steam
Cloud files, deduped via `HashSet<string>` of ids. Only `blueprint_001.json` exists
on disk today.

**Fixed (Part B):** `blueprint_001.json`'s `order_station_0` world position
overlapped a default tool-depot/supply-zone spawn location, causing two
prefabs to spawn intersecting each other in `Game1`. Moved from
`(4.5, 0.5, -1)` to `(1, 0.5, 4)`. **Unverified in-editor** — re-check spawn
layout visually once an Editor is available.

---

### Hub Blueprint-Select-and-Start Flow

`LevelSelectKiosk` — Hub-only `NetworkObject`. Scans available blueprint ids on
spawn, shows numbered pop-up, broadcasts selection via
`[Rpc(SendTo.Server)]`. Sends the blueprint **id string** (not a list index) to
avoid ordering mismatches across clients (each scans Steam Cloud independently).

**Fixed (Part B):** the script existed and was fully implemented, but no
`LevelSelectKiosk` instance had ever actually been placed in `Hub.unity` — a
real gap, not just docs drift (unlike the other "already done" findings in the
2026-06-19 audit session above). Added one in-scene at `(-14, 0.5, -22)`,
targetable via `PlayerInteraction`'s raycast like `OrderStation`/`BuildTile`.
**Placement is unverified in-editor** (no Unity Editor available this session)
— Cameron should confirm it doesn't intersect Hub geometry.

`GameSession.SelectedBlueprintId` is set here, read by `BuildSystem` in Game1.

`StartingAreaTrigger` — in-scene-placed `NetworkObject`. Sole level-start path
into `Game1`. Countdown triggers scene load.

`LevelEditorAccessPoint` (Part B, new) — Hub-only `NetworkObject`, same
raycast-target pattern as `LevelSelectKiosk`. Press E,
`EnterLevelEditorRpc()` sends the whole connected party into `LevelEditor.unity`
(same all-players-travel-together NGO scene-transition mechanism
`StartingAreaTrigger` uses for Game1 — see `NetworkPlayer.passiveScenes` above
for how player avatars are neutralized once there). Placed at `(2, 0.5, -22)`,
**unverified in-editor**, same caveat as above.

**Design-intent flag:** making the Level Editor reachable from the Hub by any
player, and adding `LevelEditor.unity` to `EditorBuildSettings` to support the
scene transition, changes the previously-documented "dev tool first, player
unlock second" gating from `GAME_INTENT.md` Phase F. This was requested
explicitly as part of this session's task list, but the *intent* change wasn't
separately confirmed with Cameron — flagged here and in `docs/SESSION.md` for
review rather than silently treated as settled.

---

### Level Editor

Location: `Assets/Scripts/LevelEditor/` (deliberately NOT in `Editor/` — survives
player builds, needed for Play Mode preview).

- `EditorCommand` — Command-pattern undo/redo
- `EditableBlueprint` — mutable in-memory model, converts to/from `BlueprintData`
- `LevelEditorCamera` — orthographic pan/zoom bird's-eye
- `EditorGridRenderer` — per-frame grid via `GL.Lines` / `OnRenderObject`
- `LevelEditorController` — orchestrator: tile placement, layer switching,
  Preview Mode, New/Load/Save
- `LevelEditorPreviewController` — self-contained first-person walkthrough
- `LevelEditorUI` — all panels via `OnGUI`

Saves write to `StreamingAssets/Blueprints/` and Steam Cloud simultaneously, so
anything saved is immediately selectable from the Hub kiosk.

**The `LevelEditor.unity` scene exists and is fully wired.** Saves write to
`StreamingAssets/Blueprints/` and Steam Cloud, so anything saved there is
immediately selectable from the Hub `LevelSelectKiosk` with zero extra work
(`BlueprintLoader.GetAllBlueprintIds()` already unifies both sources).

**Fixed (Part B):** placing a SupplyZone/OrderStation/ToolDepot/PlayerSpawn
World Object on a cell that already had one of the same category stacked a
second entry on top instead of replacing it (`AddWorldObject` always appended).
`LevelEditorController.PlaceOrReplace<T>` now checks for an existing entry
within `pickRadius` (1 unit) of the click and replaces it in place (undoable,
same as add) instead of stacking; falls through to add-new if the spot is
empty. `PlayerSpawn`'s 4-player cap still applies on the add path only —
replacing an existing spawn never changes the count.

**Pause overlay + Preview Mode conflict (Part B, found and fixed):** see "Pause
Menu" below — `LevelEditorController` now has a `pauseCanvas` field toggled off
during Preview Mode to avoid a double-Escape-handler conflict with
`LevelEditorPreviewController`.

---

### Pause Menu

`PauseMenu` (Part B, new) — plain `MonoBehaviour`, one instance per gameplay
scene (`Game1.unity`, `LevelEditor.unity`). `Update()` unconditionally toggles
on `Keyboard.current.escapeKey.wasPressedThisFrame`. `Pause()` shows
`pausePanel`, unlocks the cursor, fires `GameEvents.FireGamePaused()`.
`Resume()` reverses it via `GameEvents.FireGameResumed()`. `LeaveToHub()` routes
through the local player's `NetworkPlayer.RequestLoadSceneRpc("Hub")` if a
`NetworkManager` session exists, else falls back to a plain
`SceneManager.LoadScene("MainMenu")`.

`PlayerCamera` already subscribed to `GameEvents.OnGamePaused/OnGameResumed`
before this session (see Player section above), so `Game1.unity`'s wiring
needed no script changes — only the Canvas/Button scene hierarchy (Canvas +
CanvasScaler + GraphicRaycaster root, `PausePanel` background, `ResumeButton` +
`LeaveToHubButton` each with a child TMP label), modeled directly on the
pre-existing `LobbyUI` Canvas hierarchy in `Hub.unity`. `Game1.unity` already
had an `EventSystem` + `InputSystemUIInputModule`; `LevelEditor.unity` did not
and got one added (field-for-field copy of Game1's).

**Found and fixed (Part B):** `LevelEditor.unity`'s own
`LevelEditorPreviewController` independently listens for Escape to call
`LevelEditorController.ExitPreviewMode()` while in Preview Mode. Wiring an
always-active `PauseMenu` into the same scene meant pressing Escape during
Preview Mode would trigger both handlers simultaneously, each fighting over
`Cursor.lockState`/`Cursor.visible`. Fixed by giving `LevelEditorController` a
`pauseCanvas` field (the `PauseCanvas` GameObject), set inactive in
`EnterPreviewMode()` and reactivated in `ExitPreviewMode()` — mirrors the
existing `editorCamera.gameObject.SetActive(...)` pattern in the same two
methods. No analogous conflict exists in `Game1.unity` (no sub-mode there owns
Escape independently).

**Design-intent flag:** `LevelEditorUI`'s doc comment states the editor's own
panels are deliberately `OnGUI`-only ("needs no Canvas/Button hierarchy to be
usable"). The new Pause overlay is Canvas-based, layered on top of that
OnGUI-only editor UI. Treated as a separate, narrower-scoped concern rather
than a contradiction — both UI paradigms coexist fine in the same scene — but
this is a judgment call, not something Cameron explicitly signed off on.

**Unverified:** exact visual layout (button positions/sizes, panel appearance)
in both scenes — no Unity Editor was available this session to render and
visually confirm either Pause UI. Structural wiring (every fileID
cross-reference) was verified by direct reads of the scene YAML, but Cameron
should open both scenes once and eyeball it.

---

### Voice

`VoiceManager` — Vivox via UGS. Flow: `InitializeAsync` (UGS + Vivox) →
`LoginAsync` → `JoinChannelAsync` (channel keyed by room code) → periodic
`Set3DPosition(Camera.main.gameObject, channelName)` every 0.3s → `LeaveChannelAsync`
/ `LogoutAsync`.

Uses `_pendingChannel` to handle race condition where lobby fires before Vivox
login completes.

Mute: `VivoxService.Instance.MuteInputDevice()` (synchronous in Unity 6 SDK —
no Async suffix).

---

### UI

`MainMenuUI`, `HubUI`, `LobbyUI` (PreLobby / InLobby / Pause panels),
`OrderMenuPanel` (always-active parent GameObject, inner panel toggled — a
disabled GameObject's `Update()` never runs), `PauseMenu` (Game1/LevelEditor
in-gameplay pause overlay, see "Pause Menu" below).

`GameEvents` static event bus: `OnGameStarted`, `OnGamePaused`, `OnGameResumed`.

---

### Shaders

`ToonLit.shader` — two-pass URP cel shader. Pass 1: inverse hull outline (back
faces). Pass 2: cel shading (smoothstep NdotL) + rim light.

`ToonEmissive.shader` — extends ToonLit. HDR emission channel added to fragment
output after lighting. `_EmissionMap` defaults to `"white"` — emission color comes
through without requiring a map. Pulse controlled by `_EmissionPulseSpeed`.

`ToonTransparent.shader` — ToonLit without outline pass. Transparent queue,
`Blend SrcAlpha OneMinusSrcAlpha`, `ZWrite Off`. Optional fresnel opacity via
`_FresnelStrength`.

`ToonWater.shader` — transparent. Vertex wave animation (world-space XZ sin
waves). Depth-based shallow/deep color blend. Depth-based foam at geometry edges.
Scrolling two-layer normal map. Cel shading on animated normals. Requires depth
texture (enabled in URP pipeline asset).

`ScreenSpaceOutlines.shader` + `FullScreenPassRendererFeature` — Roberts Cross
depth edge detection. Draws outlines between separate objects. Injection point:
Before Rendering Post Processing. Does NOT include `Blit.hlsl` (causes errors in
Unity 6). Uses `sampler_LinearClamp` (pre-declared — do not redeclare).

---

## Dead / Orphaned Code

Kept in repo but not part of the live flow:

- `MiniGameLauncher` — references a scene that no longer exists. `StartingAreaTrigger`
  is the sole level-start path.
- `ReadyManager` — its only caller was `LobbyUI`'s "Start Game" button, which is
  currently disabled in `Hub.unity`. `LobbyUI`'s own `PausePanel` (Hub/lobby-side)
  is still unreachable for the same reason — unrelated to the new `PauseMenu`
  Canvas added to `Game1`/`LevelEditor` this session (see "Pause Menu" above),
  which is reachable.
- `ConnectionManager` — old localhost test script from early NGO setup. Superseded
  by `SteamLobbyManager`.

---

## Known Gaps vs Design Docs

Designed but not yet in code:
- Weight classes / movement speed penalties for carried objects
- Two-player shared carry for heavy items
- Concrete (cement + water, hardening timer) and Steel materials/tools
- Structural integrity / collapse cascade
- Contracts, timer, win/loss, payout
- Economy (shared money pool, company fee) and shop
- Chaos event framework and any individual event
- Contract selection screen
- Player-count scaling of any tunable
- Death / respawn / ghost mode

---

## Unity 6 Gotchas

- Renaming or retyping a `[SerializeField]` (e.g. a scalar field to an array)
  does **not** migrate existing serialized references in prefabs/scenes unless
  `[FormerlySerializedAs]` is used. The old value silently becomes orphaned —
  no compile error, no obvious runtime crash, just missing behavior. Audit
  prefabs after any field rename/retype refactor.
- `ClientNetworkTransform` Authority Mode must be **Owner** in the Inspector.
  Code override alone is ignored.
- Use `[Rpc(SendTo.Server, InvokePermission = ...)]` not deprecated `[ServerRpc]`.
- Use `Keyboard.current?.key.wasPressedThisFrame` not legacy `Input.GetKey`.
- EventSystem must use Input System UI Input Module.
- All display TextMeshPro labels need **Raycast Target unchecked**.
- Do NOT include `Blit.hlsl` in custom URP shaders — causes `sampler_BlitTexture
  undeclared` error. Declare textures manually.
- Do NOT redeclare `sampler_LinearClamp` — already declared by `Core.hlsl`.
- SSAO renderer feature is removed. The shader does not exist at its expected
  package path in Unity 6. Do not re-add it.
