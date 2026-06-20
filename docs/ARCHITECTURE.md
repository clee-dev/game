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

**Fall-out-of-world safety net (added this session):** `PhysicsPickup.Update()`
despawns the object server-side once its Y position drops below `fallDespawnY`
(default `-40f`, `[SerializeField]`, per-instance tunable). Anything that
tunnels through thin/missing geometry was previously falling forever — a
permanently lost material-cap slot, or a softlocked tool. Lives on the shared
base class so both `MaterialItem` and `ToolItem` get it automatically.
**Unverified in-Editor** — no Unity Editor available this session.

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

**Updated (Part C):** gained a `characterController` field and a Game1 spawn
hook. When `OnActiveSceneChanged` fires with `current.name == "Game1"`, the
server calls `BuildSystem.Instance.GetPlayerSpawnPosition()` and sends the
result to the owning client via `[Rpc(SendTo.Owner)] TeleportToRpc(Vector3)`
(same target pattern as `HubPlayerState.TeleportToRpc`), which disables the
`CharacterController` for the one frame it sets `transform.position` so the
controller doesn't fight the teleport next physics step.

**Fixed (Part E): mouse look/cursor lock dead after returning to Hub.**
`PlayerCamera` only re-locks the cursor and re-enables mouse look in reaction
to `GameEvents.OnGameStarted`. That event was only ever fired once, from
`HubPlayerState.ApplySpawnState` on a player's first spawn into Hub (gated on
its `_isSpawnedIn` `NetworkVariable` actually changing value). The player's
`NetworkObject` is never destroyed/recreated across scene transitions, so on a
return trip from `Game1`/`LevelEditor` (`PauseMenu.LeaveToHub` or
`LevelSummaryUI.ReturnToHub`, both of which unlock the cursor for their own
button clicks and never re-lock it), `_isSpawnedIn` was already `true` and
didn't change -- nothing re-fired `OnGameStarted`, so the cursor stayed
unlocked and mouse look stayed off indefinitely. Fixed by having
`NetworkPlayer.OnActiveSceneChanged` re-fire `GameEvents.FireGameStarted()`
itself whenever `IsOwner && current.name == "Hub"`, independent of
`HubPlayerState`'s networked spawn-state tracking. **Unverified in-Editor** --
please confirm leaving to Hub from both `Game1` (Pause Menu and Level Summary)
and `LevelEditor` restores mouse look immediately.

**Fixed (Part F): cursor stuck locked in Level Editor, a direct regression
from the fix above.** Re-locking the cursor on every Hub return meant the
locked state now also carried, unbroken, into `LevelEditor` -- `ApplyComponentState()`
disables the `playerCamera` component for `passiveScenes`, but
`PlayerCamera.OnDisable()` only unsubscribes from `GameEvents`; it never calls
`DisableMouseLook()`. `LevelEditorController` reads `Mouse.current.position`
for placement raycasts, which only reports a real position once the OS cursor
is free, so a locked cursor meant every placement click landed at screen
center. Fixed by having `NetworkPlayer.OnActiveSceneChanged` also call
`playerCamera.SetLookEnabled(false)` directly whenever `IsOwner &&
IsPassiveScene()` -- symmetric to the Hub-entry branch above, and safe to call
even though `playerCamera.enabled` is false (`SetLookEnabled` is a plain
method call, not gated by `MonoBehaviour.enabled`). `LevelEditorPreviewController`
(the in-editor walk-around preview) manages `Cursor.lockState` independently
and doesn't touch `PlayerCamera`, so there's no conflict between the two.
**Unverified in-Editor.**

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
`LevelSelectKiosk` → `OrderStation` → `TrashBin` → `BuildTile` → `PhysicsPickup`.

Handles: pickup, place-material, hold-to-build, drop/throw. Renders crosshair,
order menu, kiosk menu, incoming-deliveries queue via `OnGUI` (no Canvas/uGUI
required).

**Interaction feedback (2026-06-20) — `EvaluateFeedback()`, called once per
frame from the existing raycast block, reusing the same `tileTarget`/
`pickupTarget` the rest of `Update()` already derived (no second raycast).**
Drives two layers, both keyed off a single `CrosshairState` enum (`Default /
Hover / PlaceValid / PlaceInvalid / Build`):
- **Crosshair (`OnGUI`/`DrawCrosshair`)** — the existing centered dot now
  changes color per state (white/yellow/green/red), plus a thin square ring
  around it for `Hover`/`PlaceValid`/`Build` (`DrawRing`, four `GUI.DrawTexture`
  bars, reuses `Texture2D.whiteTexture`, no new texture asset).
- **Ghost tint on targeted `BuildTile`s** — `ApplyGhostTint`/`ClearGhostTint`
  write `_BaseColor` on `BuildTile.GhostRenderer` (new public accessor for the
  previously-private `ghostRenderer`) via a single reused `MaterialPropertyBlock`,
  green when the held `MaterialItem` matches `CanAcceptMaterial`, red otherwise.
  Only applies while the tile is `Empty` or `Destroyed` (the only states the
  ghost renderer is actually visible — see Build Tiles section). This layers on
  top of `BuildTile.RefreshVisual()`'s own per-material hue tint (`.material.color`,
  not MPB) rather than replacing it: the MPB override takes priority while
  targeted, then reverts to the hue tint the instant the target changes, since
  `RefreshVisual` never touches the property block.
- **Outline highlight on targeted loose `PhysicsPickup`s** — same
  `MaterialPropertyBlock` pattern, writes `_OutlineColor` (now `[PerRendererData]`
  in `ToonLit.shader` — see Shaders section) on `pickupTarget.GetComponentInChildren<Renderer>()`,
  only while `!pickupTarget.IsHeld`. Every tool/material prefab (`WoodPlank`,
  `Concrete`, `Steel`, `Hammer`, `Trowel`, `Welding Torch`) follows the same
  `Cube` child + `MeshRenderer` structure, so this needs no prefab-specific casing.
- Change-detection (`if (target != _lastTarget)`) on both layers means
  `SetPropertyBlock` only fires when the actual target changes, not every
  frame. Cleared in `OnNetworkDespawn` for the disconnect case; tile/pickup
  destruction mid-target is handled by the existing `!= null` guards (Unity's
  overloaded equality treats a destroyed object reference as `null`).
- **Not built:** a literal "press E" ring texture or shape system beyond the
  bars described above — kept to what `OnGUI`'s existing immediate-mode style
  already does, no new Canvas/Image/texture asset added.

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

**Fixed (2026-06-19, Part E): the whole level followed players back to the
Hub.** Every dynamically-spawned `NetworkObject` in the level (`BuildTile`,
`OrderStation`, and the materials/tools spawned at runtime by
`SupplyZoneSpawner`/`ToolDepotSpawner`/`OrderQueueSystem`) never set
`NetworkObject.DestroyWithScene`. NGO's default for a dynamically-spawned (not
scene-placed) `NetworkObject` is `DestroyWithScene = false`, which means on a
`LoadSceneMode.Single` transition NGO **migrates it into the next active scene
instead of destroying it** -- this is intentional default behavior (it's how a
held item or a player object is meant to survive a scene change), but it meant
every tile/depot/station/material/tool the server ever spawned in `Game1`
silently rode along into `Hub` instead of being destroyed with the scene.
Fixed by setting `netObj.DestroyWithScene = true` immediately after every
`Spawn()` call in `BuildSystem.SpawnFromBlueprint()`, `SupplyZoneSpawner.SpawnOne()`,
`ToolDepotSpawner.SpawnSlot()`, and `OrderQueueSystem.Deliver()`. Scene-placed
`NetworkObject`s (`LevelTimer`, `LevelEditorBlueprintSync`, `LevelSelectKiosk`,
`LevelEditorAccessPoint`, `StartingAreaTrigger`, `OrderQueueSystem` itself) were
never affected -- `IsSceneObject == true` objects are always destroyed with
their scene regardless of this flag, only runtime-`Instantiate`d ones default
to surviving. **Unverified in-Editor** -- no Unity Editor or C# compiler was
available this session; please load into `Game1`, leave to Hub, and confirm
the Hub is empty of leftover tiles/depots/stations/materials/tools.

`BuildSystem` — orchestrator. Loads blueprint via `BlueprintLoader`. Builds
position/state lookups. Spawns tile/zone/depot/station prefabs server-side only.

**Player spawn placement (Part C):** `GetPlayerSpawnPosition()` (server-only)
hands out `CurrentBlueprint.playerSpawns` in order via `_nextPlayerSpawnIndex`,
which resets naturally every Game1 load (lives on `BuildSystem`, whose
`Awake()` runs fresh each scene load). Once every defined spawn has been
handed out once, further calls cycle back through them with a random offset
from `OverflowSpawnOffsets` (±3 units, X or Z) so extra players don't stack on
an existing one. Called from `NetworkPlayer.OnActiveSceneChanged` — see Player
section above.

**Dependency rules (implemented in `TileStructuralRules.cs`, shared by `BuildSystem`
at runtime and the Level Editor at author-time):** type-specific pairs, not a
uniform "needs tile below" rule --
- Foundation: no support needed, but must rest on empty space below (base of a stack)
- Floor, Column: supported by a Built Foundation directly below
- Wall, Window, Door, Furniture, Column: supported by a Built Floor directly below
- Decor: supported by a Built Wall horizontally adjacent (not below)

(This corrects an earlier, less precise version of this section that described a
uniform "needs tile below Built" rule for Floor/Wall/Furniture -- the actual rules
are per-type pairs, see `TileStructuralRules.SupportsAbove`/`SupportsAdjacent`.)

**Structural integrity / collapse cascade -- implemented.** See its own section
below.

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

### Structural Integrity / Collapse Cascade

**What it is:** When a load-bearing tile is destroyed, anything it was supporting
collapses too, cascading Jenga-style through the structure (`GAME_INTENT.md`
4.4, `PLANNED_FEATURES.md` "Structural Integrity / Collapse Cascade").

**No precomputed `supportDependents` graph.** `TileStructuralRules.HasSupport`
already re-derives "is this position currently supported" live from neighbor
tile states (it's how build eligibility/ghost visuals already worked before
this system existed). A cached reverse-edge graph would just be a second copy
of the same information with its own staleness risk, so the cascade reuses
`HasSupport` via `BuildSystem.IsEligible` directly instead.

- **`BuildTile.Collapse()`** — server-only. Stops any in-progress build
  coroutine, destroys whatever raw material was sitting on the tile if it was
  `MaterialPlaced` (`MaterialItem.DestroyInCollapse()`, despawns the
  `NetworkObject` — a `Built` tile has none, `ConsumeAsBuilt` already despawned
  it), then sets `_state.Value = TileState.Destroyed`.
- **`BuildSystem.CascadeCollapseFrom(Vector3Int pos)`** — called from
  `BuildTile.OnStateChanged` (gated `IsServer`) whenever a tile's state becomes
  `Destroyed`. Re-checks the cell above and the 4 horizontal neighbors (the
  only positions `TileStructuralRules` lets anything depend on — support never
  flows downward, so the cell below is never re-checked) via the new
  `CollapseIfUnsupported`, which calls `tile.Collapse()` on any
  `MaterialPlaced`/`Built` neighbor that's no longer `IsEligible`. Each nested
  `Collapse()` re-enters `OnStateChanged` → `CascadeCollapseFrom` on the
  server, so the cascade continues through the call stack with no explicit
  recursion bookkeeping and no risk of looping (each tile transitions to
  `Destroyed` at most once; `Collapse()` is a no-op if already `Destroyed`).
- **Repairable.** `Destroyed` behaves like `Empty` for `CanAcceptMaterial` —
  players can place material on a collapsed tile again, provided whatever
  supports that position is still standing (the same `IsEligible` check governs
  both). Decision: repairable, not permanent for the level — keeps a chaos
  event from being able to lock completion below 100% with no recourse, in
  line with the "demand-driven construction" pillar (work on any part of the
  blueprint, any time, as dependencies allow).
- **Ghost visual:** `BuildTile`'s ghost renderer now also shows (and stays
  enabled) while `Destroyed`, tinted toward red (`destroyedRedBlend`, default
  0.6) instead of the raw material hue, so collapsed rubble reads differently
  from a never-built `Empty` tile at a glance.
- **Debug-only trigger, no chaos events exist yet to drive this for real.**
  `PlayerInteraction` gained a host-only (`IsServer`-gated) manual demolish:
  looking at a `MaterialPlaced`/`Built` tile and pressing Backspace calls
  `tile.Collapse()` directly (no RPC needed — the host process *is* the
  server). An on-screen hint shows only for the host, only while targeting a
  collapsible tile. **This is explicitly a stand-in for Phase D's chaos event
  framework** (Termites is the planned first real trigger,
  `PLANNED_FEATURES.md` Phase D) — remove `HandleDebugDemolish`/
  `DrawDebugDemolishHint`/`_debugDemolishTarget` from `PlayerInteraction` once
  a real structural chaos event can call `BuildTile.Collapse()` instead.
- **Unverified — no Unity Editor or C# compiler available this session.** All
  changes were hand-verified by direct read only. Please build once, then
  playtest: destroy a load-bearing tile (e.g. a Foundation under a Floor under
  a Wall) with the debug key and confirm the whole stack above it collapses in
  one cascade; confirm a collapsed tile shows the red-tinted ghost and accepts
  a fresh material placement once its own support is intact; confirm a
  `MaterialPlaced` tile's raw material actually disappears (not left floating)
  when the tile it's sitting on collapses.

---

### Smart Wall System

**What it is:** Bitmask autotiling for `TileType.Wall` tiles only (`SMART_WALLS_1.md`)
— Door/Window are explicitly out of scope, flagged as open questions below, not
wired. Each Wall tile tracks a 4-bit N/E/S/W connection mask describing which of
its horizontal neighbors are themselves connected Wall tiles, and resolves that
mask to a mesh shape + Y rotation so straight runs, corners, T-junctions, and
4-way crosses render distinctly instead of every Wall tile looking identical.

- **`WallMeshVariant`** (`Assets/Scripts/Build/WallMeshVariant.cs`) — the 6 mesh
  shapes a mask resolves to: `Standalone, EndCap, Straight, Corner, TJunction,
  Cross`.
- **`WallVariantLookup`** (`Assets/Scripts/Build/WallVariantLookup.cs`) — the
  bitmask-to-variant table. Bit constants `North = 1, East = 2, South = 4, West
  = 8` (mask range 0–15); `GetVariant(byte mask)` is a deterministic 16-entry
  switch returning `(WallMeshVariant variant, float yRotation)`. Rotation
  convention: `EndCap`'s open end faces South at 0°, `Straight` runs N-S at 0°,
  `Corner` connects North+East at 0°, `TJunction`'s open side faces West at 0°,
  all rotating clockwise as the mask changes; `Cross` and `Standalone` are
  rotationally symmetric (always 0°). Both `BuildTile` (runtime) and
  `LevelEditorController` (author-time) build masks against these same bit
  values so they always resolve to identical variants/rotations.
- **`WallMeshSet`** (`Assets/Scripts/Build/WallMeshSet.cs`) — a `ScriptableObject`
  holding one `Mesh` per `WallMeshVariant`, assigned to `BuildTile.wallMeshSet`
  in the Inspector. This is the first custom `ScriptableObject` in the codebase.
  Swapping meshes (prototype cubes → real modular wall art) needs zero code
  changes since `WallVariantLookup` only ever deals in `WallMeshVariant`, never
  a concrete mesh.
- **`BuildTile._wallMask`** — server-write `NetworkVariable<byte>`, everyone-read,
  same pattern as `_state`/`_buildProgress`. `RecalculateWallMask()` (server-only,
  no-ops if `Type != TileType.Wall`) checks all 4 horizontal neighbors via
  `BuildSystem.GetLiveTileAt` and sets one bit per neighbor that's itself a Wall
  in `MaterialPlaced` or `Built` state, then writes the new mask.
  `RefreshVisual()` (runs identically on every machine, pure function of
  replicated state) reads `WallVariantLookup.GetVariant(_wallMask.Value)` and
  swaps `sharedMesh` on the ghost/placed/built `MeshFilter`s to match, then sets
  `transform.localEulerAngles` to the resolved Y rotation. Rotation is
  collision-safe: `BuildTile.prefab`'s `BoxCollider` is a symmetric `{1,1,1}`
  cube, so rotating it in 90° steps doesn't change its footprint.
- **Trigger points** — `BuildTile.OnStateChanged`, gated `IsServer && Type ==
  TileType.Wall` (skipped entirely for non-Wall tiles instead of paying for a
  4-neighbor scan that would no-op anyway), calls `RecalculateWallMask()` on
  itself then `BuildSystem.NotifyNeighborsForWallMask(GridPosition)` so
  newly-connected/disconnected Wall neighbors re-run their own mask one hop out.
  `BuildSystem.SpawnFromBlueprint()` does a second pass over every live Wall
  tile after the initial spawn loop, calling `RecalculateWallMask()` once each
  — needed because `BuildTile.OnNetworkSpawn` only registers a tile in
  `_liveTilesByPosition`, it doesn't compute a mask, so a tile spawned early in
  the loop can't yet see neighbors spawned later in the same loop.
- **Level Editor mirror** — `LevelEditorController._editorWallVariants`
  (`Dictionary<Vector3Int, (WallMeshVariant, float)>`) is the author-time
  equivalent, existence-based instead of state-based (the editor's blueprint has
  no `MaterialPlaced`/`Built` concept, same as `CanPlaceTileType`'s structural
  check). `RebuildEditorWallVariants()` recomputes every Wall tile's mask from
  scratch against the current `Blueprint`, called from a `BlueprintChanged`
  handler registered in `Awake()` and explicitly from `ApplyRemoteBlueprint()`
  (the one path that doesn't fire `BlueprintChanged`). `CreateTileCube()` reads
  the rebuilt dictionary and applies the Y rotation to each spawned preview cube.
  **Adaptation from spec:** `SMART_WALLS_1.md` described two mechanisms — an
  incremental `RefreshEditorWallVariantsAround(pos)` for place/erase, plus a
  separate full-rebuild path for undo/redo. Implemented as a single full-rebuild
  on every `BlueprintChanged` instead: `Commands.Changed` (and therefore
  `BlueprintChanged`) fires identically for Run/Undo/Redo with no way to
  distinguish which triggered it or extract which position changed, since
  command closures are opaque at the `EditorCommandStack` level — so the
  incremental path can't actually be implemented for undo/redo. The spec itself
  notes the blueprint is small enough that a full pass is fine, which is the
  justification for using that cheaper-to-implement uniform approach everywhere
  rather than maintaining two code paths.
- **Asset wiring still pending** — see `docs/wiring/wall-mesh-set-default-asset.md`.
  No `WallMeshSet` asset instance exists yet; `BuildTile.prefab.wallMeshSet` is
  unassigned until Cameron creates and wires it in-Editor (consistent with this
  codebase's convention that new `.prefab`/`.asset` instances are hand-authored
  by Cameron, not generated by the AI).
- **Open questions, flagged per `SMART_WALLS_1.md`, not wired:**
  - Door/Window tiles don't participate in wall connections at all right now —
    a Wall tile next to a Door/Window sees it as "no connection," same as empty
    space. Should Door/Window count as a connection for the Wall's mask (and if
    so, does the Door/Window itself need any visual response)?
  - No diagonal connections — only the 4 cardinal neighbors are checked, so two
    Wall tiles touching only at a corner don't connect. Intentional per spec;
    flagging in case that's not the intended look once real modular art is in.
  - No vertical/multi-story connections — the mask is purely horizontal (N/E/S/W
    on one Y layer); a Wall tile directly above/below another Wall tile doesn't
    affect either mask. Open question if multi-story buildings need vertical
    wall continuity to read correctly.
  - Y-rotation + collision safety is **resolved, not open** — confirmed
    `BuildTile.prefab`'s `BoxCollider` is a symmetric `{1,1,1}` cube, so the
    90°-step rotation applied in `RefreshVisual()`/`CreateTileCube()` is safe.
- **Unverified — no Unity Editor or C# compiler available this session.** All
  changes were hand-verified by direct read only. Once `WallMeshSet_Default`
  exists and is wired (see the wiring doc), playtest: place a single Wall tile
  and confirm it shows `Standalone`; place a second Wall tile adjacent to it and
  confirm both flip to `EndCap` facing each other; extend to a 3-tile straight
  run and confirm the middle tile shows `Straight`; form an L and confirm the
  corner tile shows `Corner` at the right rotation; form a T and a + and confirm
  `TJunction`/`Cross`; confirm the Level Editor's preview cubes rotate to match
  exactly what the same layout produces in-game; confirm destroying/repairing a
  Wall tile (Structural Integrity collapse/repair above) correctly updates its
  neighbors' masks live.

---

### Timer System (Phase B)

`LevelTimer` — in-scene-placed `NetworkObject` in `Game1.unity` (same pattern as
`LevelEditorBlueprintSync` in `LevelEditor.unity` — no `DefaultNetworkPrefabs`
entry, never runtime-instantiated). `NetworkVariable<float> _remainingTime`,
server-authoritative. On `OnNetworkSpawn`, the server seeds it from
`BuildSystem.Instance.CurrentBlueprint.contractDefaults.timeLimitSeconds` (already
present in every current blueprint JSON — no separate Contract System needed for
this). Server `Update()` ticks it down; every machine's local
`_remainingTime.OnValueChanged` callback (not just the server's) calls
`BuildSystem.EvaluateCompletion(forced: true)` the instant the replicated value
crosses zero, mirroring `BuildTile`'s `_state.OnValueChanged` pattern so the
level-end signal fires identically everywhere, not just server-side.

`BuildSystem.EvaluateCompletion(bool forced = false)` — the shared level-end
evaluation both the timer and natural completion route through.
`BuildTile.OnStateChanged` calls it (unforced) every time a tile becomes `Built`;
it's a no-op until `BuiltTileCount() == TotalTiles`. `LevelTimer` calls it forced
on expiry, ending the level at whatever completion percentage was reached.
Guarded by `BuildSystem.LevelEnded` so it only ever fires once. Success is
`CompletionPercent >= contractDefaults.completionThresholds.full`. Fires
`GameEvents.OnLevelEnded(bool success, float completionPercent)`.

`LevelTimerHUD` — plain `MonoBehaviour` on `TimerCanvas` (screen-space overlay,
always visible, not gated by `PauseMenu`). Formats `LevelTimer.Instance.RemainingTime`
as `MM:SS` every frame; swaps to a "Complete!" / "Time's Up" message on
`GameEvents.OnLevelEnded`.

**Scoped out of this pass (still open in `PLANNED_FEATURES.md`):** the full
Contract System (`ContractData`/`ContractManager`, contract selection screen),
payout calculation, and the post-level scene transition back to the Hub. Those
are listed under Win/Loss Conditions / Phase C Economy and weren't part of the
Timer System's own "what to build" list — `GameEvents.OnLevelEnded` is the hook
future work on those should subscribe to rather than re-deriving completion logic.

**Unverified — no Unity Editor available this session:** the new `LevelTimer`
NetworkObject and `TimerCanvas` in `Game1.unity` were hand-authored as Force-Text
YAML (new `GlobalObjectIdHash`, new fileIDs in the `5200000` range, checked for
zero collisions against the rest of the file) rather than added in-Editor. Please
open `Game1.unity` once and confirm: the countdown counts down and displays
correctly, the text position/size reads fine on screen, and a full build triggers
the "Complete!" message while running out the clock triggers "Time's Up".

---

### Level Summary UI

`LevelSummaryUI` — plain `MonoBehaviour` on `SummaryCanvas` in `Game1.unity`
(screen-space overlay, fileIDs in the `5300000` range). Subscribes to
`GameEvents.OnLevelEnded` and pops up `summaryPanel` with `resultText`
("Level Complete" / "Time's Up") and `completionText` (`N% Built`), unlocking the
cursor so the player can click. `returnToHubButton` calls the local player's
`NetworkPlayer.RequestLoadSceneRpc("Hub")` — the exact same relay
`PauseMenu.LeaveToHub` uses, since `NetworkSceneManager.LoadScene` is server-only —
falling back to loading `MainMenu` directly if there's no `NetworkManager` session
(e.g. opened standalone in-Editor).

**`blameSummaryRoot` — reserved, not implemented.** A `BlameSummaryRoot`
RectTransform sits inside `SummaryPanel`, inactive, with no content. This is
where the planned blame summary (who made the most mistakes, who carried the
most materials, death count, etc. — see `docs/PLANNED_FEATURES.md`, "Level
Summary") will go once that's built. Don't repurpose this GameObject for
anything else; it's deliberately empty space reserved for that feature.

**Unverified — no Unity Editor available this session:** `SummaryCanvas`'s full
hierarchy (panel, result/completion text, button, reserved blame-summary slot)
was hand-authored as Force-Text YAML, same caveat as `LevelTimer`/`TimerCanvas`
above. Please open `Game1.unity` and confirm layout, and playtest that both the
success and timer-expiry paths show the right text and that "Return to Hub"
actually returns the whole party to `Hub.unity`.

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

**Fixed (this session): ordered items spawned inside each other and flew
apart.** `Deliver()` used to spread a multi-item order out horizontally with a
hardcoded `0.5f`-unit offset — smaller than a `WoodPlank`'s actual `1x1x1`
collider, so every delivery after the first spawned overlapping the one before
it and the physics solver violently separated them on the first
`FixedUpdate`. `Deliver()` now stacks deliveries vertically instead, spaced by
the prefab's actual `Collider.bounds.size.y` (read live off
`delivery.prefab`, not a hardcoded constant — scales automatically to
Concrete/Steel once those exist) plus a small `StackGap` (`0.05f`) so collider
faces don't spawn flush against each other. Each item now drops a short
distance onto the one below it and settles under normal gravity instead of
exploding apart. **Unverified in-Editor** — no Unity Editor available this
session.

**Trash Bin (added this session):** `TrashBin.cs` — `NetworkObject`, one RPC
(`TrashItemRpc(NetworkObjectReference)`, same trust model as
`BuildTile.PlaceMaterialRpc`: `TryGet` → component check → act, no sender
verification). Confirms the referenced object has a `PhysicsPickup` before
despawning it, so it works on materials and tools alike. Player-requested
recovery valve for ordering mistakes — not in `GAME_INTENT.md`. Wired into
`PlayerInteraction` as a fifth raycast target (alongside `BuildTile` in the
priority order above) and into the blueprint/Level Editor pipeline as a fifth
`WorldObjectCategory` (see Blueprint System / Level Editor below). **The
prefab asset itself is not yet created** — see
`docs/wiring/trash-bin-prefab.md`. Until Cameron builds it and assigns
`BuildSystem.trashBinPrefab` in the `Game1` scene Inspector, the
`trashBins` blueprint loop spawns nothing (null-guarded, same as any other
empty array).

---

### Blueprint System

**Data classes** (`Assets/Scripts/Blueprint/BlueprintData.cs`): `BlueprintData`,
`TileData`, `GridPosition`, `WorldPosition`, `SupplyZoneData`, `OrderStationData`,
`ToolDepotData`, `TrashBinData`, `ContractDefaults`, `CompletionThresholds`. All
`[Serializable]` with public fields only (required for Newtonsoft + IL2CPP).

**`TrashBinData` (added this session):** `{ id, worldPosition }`, same shape as
`ToolDepotData`/`SupplyZoneData`. `BlueprintData.trashBins` is a newer field no
existing saved blueprint JSON has — `BuildSystem.SpawnFromBlueprint()`
null-guards this one array specifically (`?? Array.Empty<TrashBinData>()`)
where the older world-object loops aren't guarded, since old blueprints
loading fine with zero trash bins is the expected case, not an error.
`EditableBlueprint` mirrors it the same way as `ToolDepots` (`List<TrashBinData>`,
`NextTrashBinId()` numbering helper).

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

**Added (Part E):** `LevelSelectKiosk`'s menu now has a trailing "Enter Level
Editor" option after the scanned blueprint list (`OptionCount` is
`_availableBlueprintIds.Length + 1`; `IsLevelEditorOption(index)` is true for
the last row). Selecting it calls a new `LevelSelectKiosk.EnterLevelEditorRpc()`
— the same one-line `NetworkManager.Singleton.SceneManager.LoadScene("LevelEditor",
LoadSceneMode.Single)` `LevelEditorAccessPoint.EnterLevelEditorRpc()` already
does, duplicated rather than shared so each Hub terminal stays self-contained.
`PlayerInteraction.SelectKioskOption()` branches on `IsLevelEditorOption(index)`
to call this instead of `SelectBlueprintRpc`. This is a second, more
discoverable path into `LevelEditor.unity` alongside the standalone
`LevelEditorAccessPoint` terminal — both remain in `Hub.unity`, this doesn't
replace either one.

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

**`WorldObjectCategory.TrashBin` (added this session):** fifth category
alongside `SupplyZone`/`OrderStation`/`ToolDepot`/`PlayerSpawn`. Same
`PlaceOrReplace`/`RemoveNearest` click-to-place/right-click-to-erase behavior
and undo/redo support as the other four; red marker color in
`RefreshWorldObjectVisuals()` (`Assets/Materials/Red.mat`-colored, distinct
from yellow OrderStation / orange ToolDepot markers). `LevelEditorUI.cs`
needed no new button code for the brush selector — `DrawEnumRow`'s generic
`CycleEnum<T>` already cycles through any enum's `Enum.GetValues`, including
this new value — only a `Trash Bins: N` count label was added to the World
Objects panel.

**Pause overlay + Preview Mode conflict (Part B, found and fixed):** see "Pause
Menu" below — `LevelEditorController` now has a `pauseCanvas` field toggled off
during Preview Mode to avoid a double-Escape-handler conflict with
`LevelEditorPreviewController`.

**Multiplayer access model + host-only editing (Part C, new):** any connected
player can be in `LevelEditor.unity` together, but only the host edits.
`LevelEditorController` gained a static `Instance` (matching
`BuildSystem.Instance`/`HubSpawnPoints.Instance`) and `bool IsHost =>
NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer`.
`Update()` and `LevelEditorUI.OnGUI()` both return immediately for non-hosts
before processing any editing input — non-hosts see a "Spectating" label
instead of the tool panels but keep camera pan/zoom.

Non-host clients see the host's edits live via new
`LevelEditorBlueprintSync : NetworkBehaviour`, on its own scene-placed
`NetworkObject` (same pattern as `LevelEditorAccessPoint`/`LevelSelectKiosk` —
no `DefaultNetworkPrefabs` registration needed). `EditorCommandStack` gained a
`Changed` event (fires on `Run`/`Undo`/`Redo`, not `Clear`), which
`LevelEditorController` forwards as `BlueprintChanged` (also fired from
`NewBlueprint()`/`LoadBlueprint()`). The host broadcasts its current
blueprint as JSON via `[Rpc(SendTo.NotServer)]` on every `BlueprintChanged`;
clients rebuild their local view via new
`LevelEditorController.ApplyRemoteBlueprint(BlueprintData)`, which replaces
`Blueprint`, clears the (host-only) command stack, and refreshes visuals.
Late joiners are covered by the same path: a non-host's `OnNetworkSpawn`
requests the current blueprint via
`[Rpc(SendTo.Server, InvokePermission = Everyone)]`, which the host answers
through the same broadcast method.

**Flagged, unverified:** `[Rpc(SendTo.NotServer)]` is this codebase's first
use of that RPC target (everything else is `SendTo.Server`/`SendTo.Owner`).
No Unity Editor/NGO package was available to compile-check it this session —
confirm it builds.

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
faces). Pass 2: cel shading (smoothstep NdotL) + rim light. `_OutlineColor` is
now `[PerRendererData]` (2026-06-20) so `PlayerInteraction` can override it
per-instance via `MaterialPropertyBlock` for the pickup hover highlight without
breaking SRP Batcher compatibility for every other material on this shader —
the attribute only changes how the Inspector/batcher treat the property, every
existing material's serialized default (`0.05, 0.05, 0.05, 1`) is untouched.

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
