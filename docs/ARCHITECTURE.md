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
  countdown that loads `Game1`.
- **Game1** — `BuildSystem` loads the selected blueprint (`GameSession.SelectedBlueprintId`,
  falling back to its own Inspector default) and spawns tiles, supply zones, tool
  depots, and order stations server-side.

`LevelEditor.unity` does not yet exist as a scene — the Level Editor scripts are
written but scene/GameObject wiring is a manual step (see `docs/wiring/`).

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

---

### Player

`PlayerController` — CharacterController movement, gravity, jump, sprint.

`PlayerCamera` — mouse look. Does NOT auto-lock cursor. Subscribes to
`GameEvents.OnGameStarted/Paused/Resumed` to enable/disable mouse look.

`InputReader` — Input System wrapper. Exposes `MoveInput`, `LookInput`,
`IsSprinting`, `JumpPressed`, `InteractPressed`, `InteractHeld`, `ConsumeInteract`.

`NetworkPlayer` — `OnNetworkSpawn()` explicitly enables/disables all components
based on `IsOwner`. Does not assume prefab default state.

`HubPlayerState` — Hub-specific spawn handling. Positions player at a
`HubSpawnPoints` location.

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

---

### Hub Blueprint-Select-and-Start Flow

`LevelSelectKiosk` — Hub-only `NetworkObject`. Scans available blueprint ids on
spawn, shows numbered pop-up, broadcasts selection via
`[Rpc(SendTo.Server)]`. Sends the blueprint **id string** (not a list index) to
avoid ordering mismatches across clients (each scans Steam Cloud independently).

`GameSession.SelectedBlueprintId` is set here, read by `BuildSystem` in Game1.

`StartingAreaTrigger` — in-scene-placed `NetworkObject`. Sole level-start path.
Countdown triggers scene load to Game1.

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

**The `LevelEditor.unity` scene does not yet exist.** Scripts are written; scene and
GameObject wiring is a manual step. See `docs/wiring/`.

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
disabled GameObject's `Update()` never runs).

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
  currently disabled in `Hub.unity`. No reachable pause/mute UI presently.
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
